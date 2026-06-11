using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// React 货柜服务
    /// 提供货柜及其明细的增删改查功能，支持批量操作和价格同步
    /// </summary>
    public class ContainerReactService : IContainerReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<ContainerReactService> _logger;
        private readonly IContainerHqSyncService _containerHqSyncService;
        private readonly ITranslationService _translationService;

        public ContainerReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            HBSalesSqlSugarContext hbSalesContext,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<ContainerReactService> logger,
            IContainerHqSyncService containerHqSyncService,
            ITranslationService translationService
        )
        {
            _context = context;
            _hqContext = hqContext;
            _hbSalesContext = hbSalesContext;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
            _containerHqSyncService = containerHqSyncService;
            _translationService = translationService;
        }

        private bool IsValidEnglishName(string? englishName)
        {
            return !string.IsNullOrWhiteSpace(englishName)
                && !_translationService.ContainsChinese(englishName);
        }

        private async Task<string?> NormalizeEnglishNameForWriteAsync(string? englishName)
        {
            if (string.IsNullOrWhiteSpace(englishName))
            {
                return null;
            }

            var normalized = englishName.Trim();
            if (!_translationService.ContainsChinese(normalized))
            {
                return normalized;
            }

            // 英文名称栏本身可能被中文污染，先翻译后再进入最终英文校验。
            var translations = await _translationService.BatchTranslateToEnglishAsync(
                new List<string> { normalized }
            );
            if (
                translations.TryGetValue(normalized, out var translated)
                && !string.Equals(translated?.Trim(), normalized, StringComparison.Ordinal)
                && IsValidEnglishName(translated)
            )
            {
                return translated!.Trim();
            }

            return null;
        }

        /// <summary>
        /// 按货柜明细重新汇总主表统计字段
        /// </summary>
        private async Task RefreshContainerSummariesAsync(IEnumerable<string?> containerCodes)
        {
            var codes = containerCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct()
                .ToList();
            if (codes.Count == 0)
            {
                return;
            }

            var containers = await _context
                .Db.Queryable<Container>()
                .Where(c => codes.Contains(c.ContainerCode))
                .ToListAsync();
            if (containers.Count == 0)
            {
                return;
            }

            var allDetails = await _context
                .Db.Queryable<ContainerDetail>()
                .Where(d => codes.Contains(d.ContainerCode) && !d.IsDeleted)
                .ToListAsync();
            var detailsByContainer = allDetails
                .GroupBy(d => d.ContainerCode)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var container in containers)
            {
                if (detailsByContainer.TryGetValue(container.ContainerCode, out var details))
                {
                    container.TotalPieces = details.Sum(d => d.LoadingPieces ?? 0m);
                    container.TotalQuantity = details.Sum(d => d.LoadingQuantity ?? 0m);
                    container.TotalVolume = details.Sum(d => d.TotalVolume ?? 0m);
                    container.TotalAmount = details.Sum(d => d.TotalAmount ?? 0m);
                }
                else
                {
                    container.TotalPieces = 0m;
                    container.TotalQuantity = 0m;
                    container.TotalVolume = 0m;
                    container.TotalAmount = 0m;
                }
            }

            await _context
                .Db.Updateable(containers)
                .UpdateColumns(x => new
                {
                    x.TotalPieces,
                    x.TotalQuantity,
                    x.TotalVolume,
                    x.TotalAmount,
                })
                .WhereColumns(x => new { x.ContainerCode })
                .ExecuteCommandAsync();
        }

        private static ContainerMainDto MapContainerHeader(Container container)
        {
            return new ContainerMainDto
            {
                HGUID = container.ContainerCode,
                货柜编号 = container.ContainerNumber,
                装柜日期 = container.LoadingDate,
                预计到岸日期 = container.EstimatedArrivalDate,
                实际到货日期 = container.ActualArrivalDate,
                合计件数 = container.TotalPieces,
                合计数量 = container.TotalQuantity,
                合计金额 = container.TotalAmount,
                总体积 = container.TotalVolume,
                成本浮率 = container.CostFloatRate,
                汇率 = container.ExchangeRate,
                运费 = container.ShippingFee,
                备注 = container.Remarks,
                状态 = container.Status,
                // 详情页首屏只需要头部信息，明细由 products/query 独立懒加载。
                Details = new List<ContainerDetailDto>(),
            };
        }

        private static string? NormalizeKeyword(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool HasAny(IReadOnlyCollection<string>? values)
        {
            return values != null && values.Count > 0;
        }

        private static string NormalizeProductTypeFilter(string value)
        {
            return value switch
            {
                "normal" => "普通商品",
                "set" => "套装商品",
                "setChild" => "套装子商品",
                _ => value,
            };
        }

        private ISugarQueryable<ContainerDetail, WarehouseProduct, DomesticProduct, Product> BuildContainerDetailQuery(
            ContainerDetailQueryDto request
        )
        {
            var itemNumber = NormalizeKeyword(request.ItemNumber);
            var barcode = NormalizeKeyword(request.Barcode);
            var productName = NormalizeKeyword(request.ProductName);
            var englishName = NormalizeKeyword(request.EnglishName);
            var remark = NormalizeKeyword(request.Remark);

            var query = _context
                .Db.Queryable<ContainerDetail>()
                .LeftJoin<WarehouseProduct>((cd, wp) => cd.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((cd, wp, dp) => cd.ProductCode == dp.ProductCode)
                .LeftJoin<Product>((cd, wp, dp, lp) => cd.ProductCode == lp.ProductCode)
                .Where((cd, wp, dp, lp) => cd.ContainerCode == request.ContainerGuid)
                .Where((cd, wp, dp, lp) => !cd.IsDeleted)
                .Where((cd, wp, dp, lp) => cd.ProductCode != null);

            if (itemNumber != null)
            {
                query = query.Where((cd, wp, dp, lp) => dp.HBProductNo != null && dp.HBProductNo.Contains(itemNumber));
            }
            if (barcode != null)
            {
                query = query.Where((cd, wp, dp, lp) => dp.Barcode != null && dp.Barcode.Contains(barcode));
            }
            if (productName != null)
            {
                query = query.Where((cd, wp, dp, lp) => dp.ProductName != null && dp.ProductName.Contains(productName));
            }
            if (englishName != null)
            {
                query = query.Where((cd, wp, dp, lp) => dp.EnglishProductName != null && dp.EnglishProductName.Contains(englishName));
            }
            if (remark != null)
            {
                query = query.Where((cd, wp, dp, lp) => cd.Remarks != null && cd.Remarks.Contains(remark));
            }

            if (HasAny(request.ProductTypes))
            {
                var productTypes = request.ProductTypes.Select(NormalizeProductTypeFilter).ToList();
                query = query.Where((cd, wp, dp, lp) => productTypes.Contains(cd.ProductType ?? "普通商品"));
            }
            if (HasAny(request.NewProductStates))
            {
                var states = request.NewProductStates;
                query = query.Where((cd, wp, dp, lp) =>
                    (states.Contains("new") && lp.ProductCode == null)
                    || (states.Contains("existing") && lp.ProductCode != null)
                );
            }
            if (HasAny(request.MatchTypes))
            {
                var matchTypes = request.MatchTypes;
                query = query.Where((cd, wp, dp, lp) =>
                    (matchTypes.Contains("productCode") && lp.ProductCode != null)
                    || (matchTypes.Contains("unmatched") && lp.ProductCode == null)
                    || (matchTypes.Contains("supplierItem") && lp.ProductCode == null && dp.HBProductNo != null)
                );
            }
            if (HasAny(request.WarehouseStatus))
            {
                var statuses = request.WarehouseStatus;
                query = query.Where((cd, wp, dp, lp) =>
                    (statuses.Contains("active") && wp.IsActive == true)
                    || (statuses.Contains("inactive") && wp.IsActive != true)
                );
            }
            if (HasAny(request.SelectedTags))
            {
                var tags = request.SelectedTags;
                if (tags.Contains("new") || tags.Contains("existing"))
                {
                    // 标签筛选同组取并集，不同组取交集，保持与前端旧本地筛选语义一致。
                    query = query.Where((cd, wp, dp, lp) =>
                        (tags.Contains("new") && lp.ProductCode == null)
                        || (tags.Contains("existing") && lp.ProductCode != null)
                    );
                }
                if (tags.Contains("noOemPrice") || tags.Contains("abnormalImport"))
                {
                    query = query.Where((cd, wp, dp, lp) =>
                        (tags.Contains("noOemPrice") && lp.ProductCode == null && (cd.OEMPrice == null || cd.OEMPrice <= 0))
                        || (tags.Contains("abnormalImport") && (cd.ImportPrice == null || cd.ImportPrice <= 0))
                    );
                }
                if (tags.Contains("active") || tags.Contains("inactive"))
                {
                    query = query.Where((cd, wp, dp, lp) =>
                        (tags.Contains("active") && wp.IsActive == true)
                        || (tags.Contains("inactive") && wp.IsActive != true)
                    );
                }
            }

            var containerPiecesMin = request.ContainerPiecesMin ?? request.ContainerPieces?.Min;
            var containerPiecesMax = request.ContainerPiecesMax ?? request.ContainerPieces?.Max;
            var containerQuantityMin = request.ContainerQuantityMin ?? request.ContainerQuantity?.Min;
            var containerQuantityMax = request.ContainerQuantityMax ?? request.ContainerQuantity?.Max;
            var domesticPriceMin = request.DomesticPriceMin ?? request.DomesticPrice?.Min;
            var domesticPriceMax = request.DomesticPriceMax ?? request.DomesticPrice?.Max;
            var floatRateMin = request.FloatRateMin ?? request.FloatRate?.Min;
            var floatRateMax = request.FloatRateMax ?? request.FloatRate?.Max;
            var transportCostMin = request.TransportCostMin ?? request.TransportCost?.Min;
            var transportCostMax = request.TransportCostMax ?? request.TransportCost?.Max;
            var warehouseImportPriceMin = request.WarehouseImportPriceMin ?? request.WarehouseImportPrice?.Min;
            var warehouseImportPriceMax = request.WarehouseImportPriceMax ?? request.WarehouseImportPrice?.Max;
            var importPriceMin = request.ImportPriceMin ?? request.ImportPrice?.Min;
            var importPriceMax = request.ImportPriceMax ?? request.ImportPrice?.Max;
            var oemPriceMin = request.OemPriceMin ?? request.OemPrice?.Min;
            var oemPriceMax = request.OemPriceMax ?? request.OemPrice?.Max;

            if (containerPiecesMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingPieces >= containerPiecesMin);
            if (containerPiecesMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingPieces <= containerPiecesMax);
            if (containerQuantityMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingQuantity >= containerQuantityMin);
            if (containerQuantityMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingQuantity <= containerQuantityMax);
            if (domesticPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.DomesticPrice >= domesticPriceMin);
            if (domesticPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.DomesticPrice <= domesticPriceMax);
            if (floatRateMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.AdjustmentRate >= floatRateMin);
            if (floatRateMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.AdjustmentRate <= floatRateMax);
            if (transportCostMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.TransportCost >= transportCostMin);
            if (transportCostMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.TransportCost <= transportCostMax);
            if (warehouseImportPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => wp.ImportPrice >= warehouseImportPriceMin);
            if (warehouseImportPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => wp.ImportPrice <= warehouseImportPriceMax);
            if (importPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.ImportPrice >= importPriceMin);
            if (importPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.ImportPrice <= importPriceMax);
            if (oemPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.OEMPrice >= oemPriceMin);
            if (oemPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.OEMPrice <= oemPriceMax);

            return query;
        }

        private static ISugarQueryable<ContainerDetail, WarehouseProduct, DomesticProduct, Product> ApplyContainerDetailSort(
            ISugarQueryable<ContainerDetail, WarehouseProduct, DomesticProduct, Product> query,
            ContainerDetailQueryDto request
        )
        {
            var descending = string.Equals(request.SortOrder, "descend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
            var orderType = descending ? OrderByType.Desc : OrderByType.Asc;

            return (request.SortBy ?? "itemNumber").Trim() switch
            {
                "barcode" => query.OrderBy((cd, wp, dp, lp) => dp.Barcode, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "productName" => query.OrderBy((cd, wp, dp, lp) => dp.ProductName, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "englishName" => query.OrderBy((cd, wp, dp, lp) => dp.EnglishProductName, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "productType" => query.OrderBy((cd, wp, dp, lp) => cd.ProductType, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "containerPieces" => query.OrderBy((cd, wp, dp, lp) => cd.LoadingPieces, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "containerQuantity" => query.OrderBy((cd, wp, dp, lp) => cd.LoadingQuantity, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "domesticPrice" => query.OrderBy((cd, wp, dp, lp) => cd.DomesticPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "floatRate" => query.OrderBy((cd, wp, dp, lp) => cd.AdjustmentRate, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "transportCost" => query.OrderBy((cd, wp, dp, lp) => cd.TransportCost, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "warehouseImportPrice" => query.OrderBy((cd, wp, dp, lp) => wp.ImportPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "importPrice" => query.OrderBy((cd, wp, dp, lp) => cd.ImportPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "oemPrice" => query.OrderBy((cd, wp, dp, lp) => cd.OEMPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "warehouseStatus" => query.OrderBy((cd, wp, dp, lp) => wp.IsActive, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "remark" => query.OrderBy((cd, wp, dp, lp) => cd.Remarks, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                _ => query.OrderBy((cd, wp, dp, lp) => dp.HBProductNo, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
            };
        }

        /// <summary>
        /// 获取货柜列表（支持分页、日期过滤、货号筛选）
        /// </summary>
        public async Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request)
        {
            try
            {
                // 构建基础查询，预加载明细数据
                var query = _context.Db.Queryable<Container>().Includes(x => x.Details);

                // 日期范围过滤
                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    // 根据日期类型选择不同的日期字段进行过滤
                    if (
                        request.DateType == "实际到货日期"
                        || request.DateType == "Actual Arrival Date"
                    )
                    {
                        query = query.Where(x =>
                            x.ActualArrivalDate >= request.StartDate.Value
                            && x.ActualArrivalDate <= request.EndDate.Value
                        );
                    }
                    else
                    {
                        // 默认使用预计到货日期
                        query = query.Where(x =>
                            x.EstimatedArrivalDate >= request.StartDate.Value
                            && x.EstimatedArrivalDate <= request.EndDate.Value
                        );
                    }
                }

                // 过滤掉无效状态（Status 为 null 的记录）
                query = query.Where(x => x.Status != null);

                // 货号筛选：查找包含指定货号的货柜
                if (!string.IsNullOrEmpty(request.ItemNumberFilter))
                {
                    // 通过明细表关联商品表，查找匹配的货柜编码
                    var containerCodesWithItem = await _context
                        .Db.Queryable<ContainerDetail>()
                        .LeftJoin<DomesticProduct>((cd, p) => cd.ProductCode == p.ProductCode)
                        .Where(
                            (cd, p) =>
                                !cd.IsDeleted
                                && p.HBProductNo != null
                                && p.HBProductNo.Contains(request.ItemNumberFilter)
                        )
                        .Select((cd, p) => cd.ContainerCode)
                        .ToListAsync();

                    if (containerCodesWithItem.Any())
                    {
                        query = query.Where(c => containerCodesWithItem.Contains(c.ContainerCode));
                    }
                    else
                    {
                        // 没有匹配的货柜，返回空结果
                        return new ContainerListResponse
                        {
                            Containers = new List<ContainerMainDto>(),
                            TotalCount = 0,
                            Page = request.Page,
                            PageSize = request.PageSize,
                        };
                    }
                }

                // 排序：根据日期类型选择排序字段
                if (request.DateType == "实际到货日期" || request.DateType == "Actual Arrival Date")
                {
                    query = query.OrderByDescending(x => x.ActualArrivalDate);
                }
                else
                {
                    query = query.OrderByDescending(x => x.EstimatedArrivalDate);
                }

                // 获取总数用于分页
                var totalCount = await query.CountAsync();

                // 分页查询
                var containers = await query
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToListAsync();

                // 映射到 DTO 并返回
                var result = new ContainerListResponse
                {
                    Containers = _mapper.Map<List<ContainerMainDto>>(containers),
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize,
                };

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜列表失败");
                throw;
            }
        }

        /// <summary>
        /// 获取单个货柜详情
        /// </summary>
        public async Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid)
        {
            try
            {
                // 详情头部接口只返回货柜主表，明细改由 QueryContainerDetailsAsync 懒加载。
                var container = await _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                if (container == null)
                {
                    return null;
                }

                return MapContainerHeader(container);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜详情失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        /// <summary>
        /// 更新货柜基本信息（实际到货日期、汇率、运费、备注、状态）
        /// </summary>
        public async Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto)
        {
            try
            {
                // 查找货柜
                var container = await _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerGuid}", containerGuid);
                    return false;
                }

                // 根据 DTO 的中文字段逐个更新
                if (dto.实际到货日期.HasValue)
                {
                    container.ActualArrivalDate = dto.实际到货日期.Value;
                }

                if (dto.汇率.HasValue)
                {
                    container.ExchangeRate = dto.汇率.Value;
                }

                if (dto.运费.HasValue)
                {
                    container.ShippingFee = dto.运费.Value;
                }

                if (dto.备注 != null)
                {
                    container.Remarks = dto.备注;
                }

                if (dto.状态.HasValue)
                {
                    container.Status = dto.状态.Value;
                }

                // 保存并限制更新列，避免覆盖其他字段
                var rowsAffected = await _context
                    .Db.Updateable(container)
                    .UpdateColumns(x => new
                    {
                        x.ActualArrivalDate,
                        x.ExchangeRate,
                        x.ShippingFee,
                        x.Remarks,
                        x.Status,
                    })
                    .Where(x => x.ContainerCode == containerGuid)
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "更新货柜信息成功: {ContainerGuid}, 影响行数: {RowsAffected}",
                    containerGuid,
                    rowsAffected
                );
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新货柜信息失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        /// <summary>
        /// 获取货柜中的商品明细列表（含商品信息和仓库价格）
        /// </summary>
        public async Task<List<ContainerDetailDto>> GetContainerProductsAsync(string containerGuid)
        {
            try
            {
                // 多表联查：明细表 + 仓库商品表 + 国内商品表 + 本地商品表
                // 获取完整的商品信息和价格数据
                var products = await _context
                    .Db.Queryable<ContainerDetail>()
                    .LeftJoin<WarehouseProduct>((cd, wp) => cd.ProductCode == wp.ProductCode)
                    .LeftJoin<DomesticProduct>((cd, wp, dp) => cd.ProductCode == dp.ProductCode)
                    .LeftJoin<Product>((cd, wp, dp, lp) => cd.ProductCode == lp.ProductCode)
                    .Where((cd, wp, dp, lp) => cd.ContainerCode == containerGuid)
                    .Where((cd, wp, dp, lp) => cd.ProductCode != null)
                    .OrderBy((cd, wp, dp, lp) => cd.ProductCode)
                    .Select(
                        (cd, wp, dp, lp) =>
                            new ContainerDetailDto
                            {
                                HGUID = cd.DetailCode,
                                主表GUID = cd.ContainerCode,
                                商品编码 = cd.ProductCode,
                                LocalSupplierCode = lp.LocalSupplierCode,
                                装柜类型 = cd.LoadingType,
                                商品类型 = cd.ProductType,
                                套装数量 = cd.SetQuantity,
                                装柜件数 = cd.LoadingPieces,
                                装柜数量 = cd.LoadingQuantity,
                                国内价格 = cd.DomesticPrice,
                                调整浮率 = cd.AdjustmentRate,
                                进口价格 = cd.ImportPrice,
                                贴牌价格 = cd.OEMPrice,
                                单件装箱数 = cd.PackingQuantity,
                                单件体积 = cd.UnitVolume,
                                合计装柜金额 = cd.TotalAmount,
                                合计装柜体积 = cd.TotalVolume,
                                运输成本 = cd.TransportCost,
                                备注 = cd.Remarks,
                                // 判断是否新商品：本地商品表中不存在该商品编码
                                是否新商品 = lp.ProductCode == null,
                                商品信息 = new ContainerProductInfoDto
                                {
                                    商品编码 = dp.ProductCode,
                                    LocalSupplierCode = lp.LocalSupplierCode,
                                    货号 = dp.HBProductNo,
                                    商品名称 = dp.ProductName,
                                    英文名称 = dp.EnglishProductName,
                                    商品图片 = dp.ProductImage,
                                    条形码 = dp.Barcode,
                                },
                                // 仓库商品的价格和上下架状态
                                WarehouseImportPrice = wp.ImportPrice,
                                WarehouseOEMPrice = wp.OEMPrice,
                                WarehouseIsActive = wp.IsActive,
                            }
                    )
                    .ToListAsync();

                return products;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜商品列表失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        /// <summary>
        /// 按服务端筛选、排序和内部分页查询货柜商品明细
        /// </summary>
        public async Task<ContainerDetailQueryResultDto> QueryContainerDetailsAsync(
            ContainerDetailQueryDto request
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ContainerGuid))
                {
                    return new ContainerDetailQueryResultDto
                    {
                        PageNumber = Math.Max(1, request.PageNumber),
                        PageSize = Math.Clamp(request.PageSize, 1, 500),
                    };
                }

                var pageNumber = Math.Max(1, request.PageNumber);
                var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 500);
                var query = BuildContainerDetailQuery(request);
                var total = await query.Clone().CountAsync();

                // tagStats 必须按当前服务端筛选口径统计，不能只统计当前懒加载块。
                var stats = new ContainerDetailTagStatsDto
                {
                    All = total,
                    New = await query.Clone().Where((cd, wp, dp, lp) => lp.ProductCode == null).CountAsync(),
                    Existing = await query.Clone().Where((cd, wp, dp, lp) => lp.ProductCode != null).CountAsync(),
                    NoOemPrice = await query.Clone().Where((cd, wp, dp, lp) => lp.ProductCode == null && (cd.OEMPrice == null || cd.OEMPrice <= 0)).CountAsync(),
                    AbnormalImport = await query.Clone().Where((cd, wp, dp, lp) => cd.ImportPrice == null || cd.ImportPrice <= 0).CountAsync(),
                    Active = await query.Clone().Where((cd, wp, dp, lp) => wp.IsActive == true).CountAsync(),
                    Inactive = await query.Clone().Where((cd, wp, dp, lp) => wp.IsActive != true).CountAsync(),
                };

                var items = await ApplyContainerDetailSort(query.Clone(), request)
                    .Select(
                        (cd, wp, dp, lp) =>
                            new ContainerDetailDto
                            {
                                HGUID = cd.DetailCode,
                                主表GUID = cd.ContainerCode,
                                商品编码 = cd.ProductCode,
                                LocalSupplierCode = lp.LocalSupplierCode,
                                装柜类型 = cd.LoadingType,
                                商品类型 = cd.ProductType,
                                套装数量 = cd.SetQuantity,
                                装柜件数 = cd.LoadingPieces,
                                装柜数量 = cd.LoadingQuantity,
                                国内价格 = cd.DomesticPrice,
                                调整浮率 = cd.AdjustmentRate,
                                进口价格 = cd.ImportPrice,
                                贴牌价格 = cd.OEMPrice,
                                单件装箱数 = cd.PackingQuantity,
                                单件体积 = cd.UnitVolume,
                                合计装柜金额 = cd.TotalAmount,
                                合计装柜体积 = cd.TotalVolume,
                                运输成本 = cd.TransportCost,
                                备注 = cd.Remarks,
                                是否新商品 = lp.ProductCode == null,
                                WarehouseImportPrice = wp.ImportPrice,
                                WarehouseOEMPrice = wp.OEMPrice,
                                WarehouseIsActive = wp.IsActive,
                                商品信息 = new ContainerProductInfoDto
                                {
                                    商品编码 = dp.ProductCode,
                                    LocalSupplierCode = lp.LocalSupplierCode,
                                    货号 = dp.HBProductNo,
                                    商品名称 = dp.ProductName,
                                    英文名称 = dp.EnglishProductName,
                                    商品图片 = dp.ProductImage,
                                    条形码 = dp.Barcode,
                                    商品规格 = dp.ProductSpecification,
                                    单件装箱数 = cd.PackingQuantity,
                                    单件体积 = cd.UnitVolume,
                                    商品类型 = cd.ProductType,
                                    套装数量 = cd.SetQuantity,
                                },
                            }
                    )
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new ContainerDetailQueryResultDto
                {
                    Items = items,
                    ItemsTotal = total,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    HasMore = pageNumber * pageSize < total,
                    TagStats = stats,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "查询货柜商品明细失败, ContainerGuid: {ContainerGuid}",
                    request.ContainerGuid
                );
                throw;
            }
        }

        /// <summary>
        /// 获取货柜商品明细列表（支持日期过滤、货号筛选、排序）
        /// </summary>
        public async Task<List<ContainerDetailDto>> GetFilteredContainerProductsAsync(
            ContainerQueryRequest request
        )
        {
            try
            {
                // 第一步：根据日期条件筛选货柜
                var containerQuery = _context.Db.Queryable<Container>();

                // 日期范围过滤
                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    if (
                        request.DateType == "实际到货日期"
                        || request.DateType == "Actual Arrival Date"
                    )
                    {
                        containerQuery = containerQuery.Where(x =>
                            x.ActualArrivalDate >= request.StartDate.Value
                            && x.ActualArrivalDate <= request.EndDate.Value
                        );
                    }
                    else
                    {
                        containerQuery = containerQuery.Where(x =>
                            x.EstimatedArrivalDate >= request.StartDate.Value
                            && x.EstimatedArrivalDate <= request.EndDate.Value
                        );
                    }
                }

                containerQuery = containerQuery.Where(x => x.Status != null);

                // 获取符合条件的货柜编码列表
                var containerCodes = await containerQuery
                    .Select(x => x.ContainerCode)
                    .ToListAsync();

                // 第二步：查询这些货柜中的商品明细
                var productsQuery = _context
                    .Db.Queryable<ContainerDetail>()
                    .Includes(x => x.Product)
                    .Includes(x => x.LocalProduct)
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .Where(x => x.ProductCode != null);

                // 货号筛选
                if (!string.IsNullOrEmpty(request.ItemNumberFilter))
                {
                    productsQuery = productsQuery.Where(x =>
                        (
                            x.Product != null
                            && x.Product.HBProductNo != null
                            && x.Product.HBProductNo.Contains(request.ItemNumberFilter)
                        )
                    );
                }

                // 动态排序处理
                var sortBy = request.SortBy ?? "货号";
                var sortDirection = request.SortDirection ?? "asc";

                switch (sortBy)
                {
                    case var s
                        when string.Equals(s, "货号", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ItemNumber", StringComparison.OrdinalIgnoreCase):
                        // 按货号排序
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x =>
                                x.Product != null ? x.Product.HBProductNo : ""
                            )
                            : productsQuery.OrderBy(x =>
                                x.Product != null ? x.Product.HBProductNo : ""
                            );
                        break;
                    case var s
                        when string.Equals(s, "商品编码", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ProductCode", StringComparison.OrdinalIgnoreCase):
                        // 按商品编码排序
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x => x.ProductCode)
                            : productsQuery.OrderBy(x => x.ProductCode);
                        break;
                    case var s
                        when string.Equals(s, "商品名称", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ProductName", StringComparison.OrdinalIgnoreCase):
                        // 按商品名称排序
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x =>
                                x.Product != null ? x.Product.ProductName : ""
                            )
                            : productsQuery.OrderBy(x =>
                                x.Product != null ? x.Product.ProductName : ""
                            );
                        break;
                    default:
                        // 默认按货号升序
                        productsQuery = productsQuery.OrderBy(x =>
                            x.Product != null ? x.Product.HBProductNo : ""
                        );
                        break;
                }

                var products = await productsQuery.ToListAsync();
                var containerDetails = _mapper.Map<List<ContainerDetailDto>>(products);

                return containerDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜商品明细列表失败");
                throw;
            }
        }

        /// <summary>
        /// 获取日期过滤选项（过去两周、未来五周的预计到货日期）
        /// </summary>
        public Task<List<DateFilterOption>> GetDateFilterOptionsAsync()
        {
            try
            {
                var now = DateTime.Now;
                var options = new List<DateFilterOption>();

                // 过去两周的实际到货选项
                options.Add(
                    new DateFilterOption
                    {
                        Label = "Arrived in the Past Week",
                        Value = "past_2_week_actual",
                        StartDate = now.AddDays(-14),
                        EndDate = now,
                        DateType = "Actual Arrival Date",
                    }
                );

                // 未来五周的预计到货选项
                for (int i = 0; i < 5; i++)
                {
                    var weekStart = now.AddDays(i * 7);
                    var weekEnd = weekStart.AddDays(6);
                    options.Add(
                        new DateFilterOption
                        {
                            Label =
                                i == 0
                                    ? "Estimated Arrival This Week"
                                    : $"Estimated Arrival in Week {i + 1}",
                            Value = $"future_week_{i + 1}",
                            StartDate = weekStart,
                            EndDate = weekEnd,
                            DateType = "Estimated Arrival Date",
                        }
                    );
                }

                return Task.FromResult(options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日期过滤选项失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新货柜明细
        /// 功能：
        /// 1. 更新明细表的价格和上下架状态
        /// 2. 同步更新国内商品表的名称
        /// 3. 同步更新仓库商品表、本地商品表、门店零售价表的价格
        /// 采用 CASE WHEN 批量更新避免 N+1 查询问题
        /// </summary>
        public async Task<int> BatchUpdateDetailsAsync(List<UpdateContainerDetailDto> updates)
        {
            try
            {
                // 参数校验
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新明细列表为空");
                    return 0;
                }

                _logger.LogInformation(
                    "[React] 开始批量更新货柜明细，数量: {Count}",
                    updates.Count
                );

                // 第一步：查询需要更新的明细记录
                var hguids = updates.Select(u => u.HGUID).Distinct().ToList();
                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .ToListAsync();

                // 构建明细编码到明细实体的映射，便于快速查找
                var detailMap = details.ToDictionary(d => d.DetailCode, d => d);
                var changedDetails = new List<ContainerDetail>();
                var updatedRequestGuids = new HashSet<string>();

                // 遍历更新请求，逐个应用变更
                foreach (var update in updates)
                {
                    if (!detailMap.TryGetValue(update.HGUID, out var detail))
                    {
                        _logger.LogWarning("[React] 明细不存在: {DetailGuid}", update.HGUID);
                        continue;
                    }

                    // 检测每个字段是否有变更，避免不必要的更新
                    var changed = false;
                    if (update.调整浮率.HasValue && detail.AdjustmentRate != update.调整浮率.Value)
                    {
                        detail.AdjustmentRate = update.调整浮率.Value;
                        changed = true;
                    }
                    if (update.国内价格.HasValue && detail.DomesticPrice != update.国内价格.Value)
                    {
                        detail.DomesticPrice = update.国内价格.Value;
                        changed = true;
                    }
                    if (update.进口价格.HasValue && detail.ImportPrice != update.进口价格.Value)
                    {
                        detail.ImportPrice = update.进口价格.Value;
                        changed = true;
                    }
                    if (update.运输成本.HasValue && detail.TransportCost != update.运输成本.Value)
                    {
                        detail.TransportCost = update.运输成本.Value;
                        changed = true;
                    }
                    if (update.贴牌价格.HasValue && detail.OEMPrice != update.贴牌价格.Value)
                    {
                        detail.OEMPrice = update.贴牌价格.Value;
                        changed = true;
                    }
                    if (update.单件装箱数.HasValue && detail.PackingQuantity != update.单件装箱数.Value)
                    {
                        detail.PackingQuantity = update.单件装箱数.Value;
                        changed = true;
                    }
                    if (update.单件体积.HasValue && detail.UnitVolume != update.单件体积.Value)
                    {
                        detail.UnitVolume = update.单件体积.Value;
                        changed = true;
                    }
                    if (update.装柜数量.HasValue && detail.LoadingQuantity != update.装柜数量.Value)
                    {
                        detail.LoadingQuantity = update.装柜数量.Value;
                        changed = true;
                    }
                    if (update.合计装柜体积.HasValue && detail.TotalVolume != update.合计装柜体积.Value)
                    {
                        detail.TotalVolume = update.合计装柜体积.Value;
                        changed = true;
                    }
                    if (update.合计装柜金额.HasValue && detail.TotalAmount != update.合计装柜金额.Value)
                    {
                        detail.TotalAmount = update.合计装柜金额.Value;
                        changed = true;
                    }
                    if (update.IsActive.HasValue && detail.IsActive != update.IsActive.Value)
                    {
                        detail.IsActive = update.IsActive.Value;
                        changed = true;
                    }
                    if (changed)
                    {
                        changedDetails.Add(detail);
                        updatedRequestGuids.Add(update.HGUID);
                    }
                }

                // 提前整理有效明细请求，名称回写不能依赖价格/状态字段是否变化。
                var validDetailUpdates = updates
                    .Select(u =>
                    {
                        if (detailMap.TryGetValue(u.HGUID, out var detail))
                        {
                            return new
                            {
                                Update = u,
                                Detail = detail,
                                ProductCode = detail.ProductCode,
                            };
                        }
                        return null;
                    })
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ProductCode))
                    .Select(x => x!)
                    .ToList();

                var productCodes = validDetailUpdates
                    .Select(x => x.ProductCode!)
                    .Distinct()
                    .ToList();

                // 预加载商品数据：既用于名称回写，也用于判断价格同步是否是已有商品。
                var productMap = new Dictionary<string, DomesticProduct>();
                if (productCodes.Count > 0)
                {
                    var products = await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(p => productCodes.Contains(p.ProductCode))
                        .ToListAsync();
                    productMap = products.ToDictionary(p => p.ProductCode, p => p);
                }

                var productNameUpdates = validDetailUpdates
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Update.商品名称)
                        || !string.IsNullOrWhiteSpace(x.Update.英文名称)
                        || x.Update.ClearEnglishName == true
                    )
                    .GroupBy(x => x.ProductCode!)
                    .Select(group =>
                    {
                        string? englishName = null;
                        var hasEnglishNameIntent = false;

                        foreach (var item in group)
                        {
                            if (item.Update.ClearEnglishName == true)
                            {
                                // 清空是显式意图，必须覆盖前面同商品的英文名称更新。
                                englishName = null;
                                hasEnglishNameIntent = true;
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(item.Update.英文名称))
                            {
                                englishName = item.Update.英文名称!.Trim();
                                hasEnglishNameIntent = true;
                            }
                        }

                        return new
                        {
                            ProductCode = group.Key,
                            商品名称 = group
                                .Select(x => x.Update.商品名称)
                                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                            英文名称 = englishName,
                            HasEnglishNameIntent = hasEnglishNameIntent,
                        };
                    })
                    .ToList();

                var changedProducts = new List<DomesticProduct>();
                var changedProductCodes = new HashSet<string>();
                foreach (var productUpdate in productNameUpdates)
                {
                    if (!productMap.TryGetValue(productUpdate.ProductCode, out var product))
                    {
                        continue;
                    }

                    var productChanged = false;
                    if (
                        !string.IsNullOrWhiteSpace(productUpdate.商品名称)
                        && product.ProductName != productUpdate.商品名称
                    )
                    {
                        product.ProductName = productUpdate.商品名称;
                        productChanged = true;
                    }
                    if (
                        productUpdate.HasEnglishNameIntent
                        && string.IsNullOrWhiteSpace(productUpdate.英文名称)
                        && product.EnglishProductName != null
                    )
                    {
                        product.EnglishProductName = null;
                        productChanged = true;
                    }
                    else if (
                        productUpdate.HasEnglishNameIntent
                        && !string.IsNullOrWhiteSpace(productUpdate.英文名称)
                    )
                    {
                        var normalizedEnglishName = await NormalizeEnglishNameForWriteAsync(
                            productUpdate.英文名称
                        );
                        if (
                            normalizedEnglishName != null
                            && product.EnglishProductName != normalizedEnglishName
                        )
                        {
                            product.EnglishProductName = normalizedEnglishName;
                            productChanged = true;
                        }
                        else if (normalizedEnglishName == null)
                        {
                            // 翻译失败或 mock 降级时可能返回中文/中英混合，不能污染英文名称字段。
                            _logger.LogWarning(
                                "跳过仍包含中文的货柜明细英文名称写回: ProductCode={ProductCode}, EnglishName={EnglishName}",
                                productUpdate.ProductCode,
                                productUpdate.英文名称
                            );
                        }
                    }

                    if (productChanged)
                    {
                        changedProducts.Add(product);
                        changedProductCodes.Add(productUpdate.ProductCode);
                    }
                }

                foreach (var item in validDetailUpdates)
                {
                    var hasNameUpdate =
                        !string.IsNullOrWhiteSpace(item.Update.商品名称)
                        || !string.IsNullOrWhiteSpace(item.Update.英文名称)
                        || item.Update.ClearEnglishName == true;
                    if (hasNameUpdate && changedProductCodes.Contains(item.ProductCode!))
                    {
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                }

                // 开启事务，确保多表更新的原子性
                if (changedDetails.Count > 0 || changedProducts.Count > 0)
                {
                    await _context.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 第二步：更新货柜明细表
                        if (changedDetails.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedDetails)
                                .UpdateColumns(x => new
                                {
                                    x.AdjustmentRate,
                                    x.DomesticPrice,
                                    x.ImportPrice,
                                    x.TransportCost,
                                    x.OEMPrice,
                                    x.PackingQuantity,
                                    x.UnitVolume,
                                    x.LoadingQuantity,
                                    x.TotalVolume,
                                    x.TotalAmount,
                                    x.IsActive,
                                })
                                .WhereColumns(x => new { x.DetailCode })
                                .ExecuteCommandAsync();

                            // 明细合计变化后，同事务刷新货柜主表汇总，保证列表和详情头部一致。
                            await RefreshContainerSummariesAsync(
                                changedDetails.Select(detail => detail.ContainerCode)
                            );
                        }

                        // 第三步：同步更新国内商品表的名称信息
                        if (changedProducts.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedProducts)
                                .UpdateColumns(x => new { x.ProductName, x.EnglishProductName })
                                .WhereColumns(x => new { x.ProductCode })
                                .ExecuteCommandAsync();
                        }

                        // 第四步：同步已有商品的价格和上下架状态到关联表
                        // 通过 productMap 判断商品是否为已有商品（商品表中已存在）
                        var existingProductUpdates = updates
                            .Where(u =>
                                detailMap.TryGetValue(u.HGUID, out var d)
                                && u.SkipRelatedProductSync != true
                                && !string.IsNullOrEmpty(d.ProductCode)
                                && productMap.ContainsKey(d.ProductCode)
                                && (
                                    u.进口价格.HasValue
                                    || u.贴牌价格.HasValue
                                    || u.IsActive.HasValue
                                )
                            )
                            .Select(u => new { Update = u, Detail = detailMap[u.HGUID] })
                            .ToList();

                        if (existingProductUpdates.Count > 0)
                        {
                            // 4.1 批量更新 WarehouseProduct 表（进口价格、贴牌价格、上下架状态）
                            // 使用 CASE WHEN 语句批量更新，避免 N+1 查询问题
                            var warehouseUpdates = existingProductUpdates
                                .Where(x =>
                                    x.Update.进口价格.HasValue
                                    || x.Update.贴牌价格.HasValue
                                    || x.Update.IsActive.HasValue
                                )
                                .ToList();

                            if (warehouseUpdates.Count > 0)
                            {
                                var importCases = new List<string>();
                                var oemCases = new List<string>();
                                var activeCases = new List<string>();
                                var pList = new List<SugarParameter>();

                                for (int i = 0; i < warehouseUpdates.Count; i++)
                                {
                                    var item = warehouseUpdates[i];
                                    pList.Add(
                                        new SugarParameter($"@Pc{i}", item.Detail.ProductCode)
                                    );

                                    if (item.Update.进口价格.HasValue)
                                    {
                                        pList.Add(
                                            new SugarParameter(
                                                $"@Imp{i}",
                                                item.Update.进口价格.Value
                                            )
                                        );
                                        importCases.Add($"WHEN ProductCode = @Pc{i} THEN @Imp{i}");
                                    }
                                    if (item.Update.贴牌价格.HasValue)
                                    {
                                        pList.Add(
                                            new SugarParameter(
                                                $"@OEM{i}",
                                                item.Update.贴牌价格.Value
                                            )
                                        );
                                        oemCases.Add($"WHEN ProductCode = @Pc{i} THEN @OEM{i}");
                                    }
                                    if (item.Update.IsActive.HasValue)
                                    {
                                        pList.Add(
                                            new SugarParameter(
                                                $"@Act{i}",
                                                item.Update.IsActive.Value ? 1 : 0
                                            )
                                        );
                                        activeCases.Add($"WHEN ProductCode = @Pc{i} THEN @Act{i}");
                                    }
                                }

                                var inClause = string.Join(
                                    ", ",
                                    Enumerable
                                        .Range(0, warehouseUpdates.Count)
                                        .Select(i => $"@Pc{i}")
                                );
                                var setClause = new List<string>();

                                if (importCases.Count > 0)
                                    setClause.Add(
                                        $"ImportPrice = CASE {string.Join(" ", importCases)} ELSE ImportPrice END"
                                    );
                                if (oemCases.Count > 0)
                                    setClause.Add(
                                        $"OEMPrice = CASE {string.Join(" ", oemCases)} ELSE OEMPrice END"
                                    );
                                if (activeCases.Count > 0)
                                    setClause.Add(
                                        $"IsActive = CASE {string.Join(" ", activeCases)} ELSE IsActive END"
                                    );

                                if (setClause.Count > 0)
                                {
                                    var sql =
                                        $"UPDATE WarehouseProduct SET {string.Join(", ", setClause)} WHERE ProductCode IN ({inClause})";
                                    await _context.Db.Ado.ExecuteCommandAsync(sql, pList);
                                }
                            }

                            // 4.2 批量更新 Product 表（进口价格 -> 进货价，贴牌价格 -> 零售价）
                            // 使用 CASE WHEN 语句批量更新，避免 N+1 查询问题
                            var importPriceUpdates = existingProductUpdates
                                .Where(x => x.Update.进口价格.HasValue)
                                .ToList();
                            var oemPriceUpdates = existingProductUpdates
                                .Where(x => x.Update.贴牌价格.HasValue)
                                .ToList();
                            if (importPriceUpdates.Count > 0 || oemPriceUpdates.Count > 0)
                            {
                                var purchaseCaseBuilder = new System.Text.StringBuilder();
                                var retailCaseBuilder = new System.Text.StringBuilder();
                                var pList = new List<SugarParameter>();
                                var priceProductCodes = existingProductUpdates
                                    .Where(x => x.Update.进口价格.HasValue || x.Update.贴牌价格.HasValue)
                                    .Select(x => x.Detail.ProductCode)
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Distinct()
                                    .ToList();
                                for (int i = 0; i < priceProductCodes.Count; i++)
                                {
                                    pList.Add(new SugarParameter($"@Pc{i}", priceProductCodes[i]));
                                }
                                for (int i = 0; i < importPriceUpdates.Count; i++)
                                {
                                    purchaseCaseBuilder.Append($" WHEN ProductCode = @PurchasePc{i} THEN @PurchasePrice{i}");
                                    pList.Add(new SugarParameter($"@PurchasePc{i}", importPriceUpdates[i].Detail.ProductCode));
                                    pList.Add(new SugarParameter($"@PurchasePrice{i}", importPriceUpdates[i].Update.进口价格!.Value));
                                }
                                for (int i = 0; i < oemPriceUpdates.Count; i++)
                                {
                                    retailCaseBuilder.Append($" WHEN ProductCode = @RetailPc{i} THEN @RetailPrice{i}");
                                    pList.Add(new SugarParameter($"@RetailPc{i}", oemPriceUpdates[i].Detail.ProductCode));
                                    pList.Add(new SugarParameter($"@RetailPrice{i}", oemPriceUpdates[i].Update.贴牌价格!.Value));
                                }
                                var inClause = string.Join(
                                    ", ",
                                    Enumerable
                                        .Range(0, priceProductCodes.Count)
                                        .Select(i => $"@Pc{i}")
                                );
                                var setClause = new List<string>();
                                if (importPriceUpdates.Count > 0)
                                    setClause.Add($"PurchasePrice = CASE {purchaseCaseBuilder} ELSE PurchasePrice END");
                                if (oemPriceUpdates.Count > 0)
                                    setClause.Add($"RetailPrice = CASE {retailCaseBuilder} ELSE RetailPrice END");
                                var sql =
                                    $"UPDATE Product SET {string.Join(", ", setClause)} WHERE ProductCode IN ({inClause})";
                                await _context.Db.Ado.ExecuteCommandAsync(sql, pList);
                            }

                            // 4.3 批量更新 StoreRetailPrice 表（进口价格 -> 进货价，贴牌价格 -> 分店零售价）
                            // 使用 CASE WHEN 语句批量更新，避免 N+1 查询问题
                            if (importPriceUpdates.Count > 0 || oemPriceUpdates.Count > 0)
                            {
                                var purchaseCaseBuilder = new System.Text.StringBuilder();
                                var retailCaseBuilder = new System.Text.StringBuilder();
                                var pList = new List<SugarParameter>();
                                var storePriceProductCodes = existingProductUpdates
                                    .Where(x => x.Update.进口价格.HasValue || x.Update.贴牌价格.HasValue)
                                    .Select(x => x.Detail.ProductCode)
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Distinct()
                                    .ToList();
                                for (int i = 0; i < storePriceProductCodes.Count; i++)
                                {
                                    pList.Add(new SugarParameter($"@Pc{i}", storePriceProductCodes[i]));
                                }
                                for (int i = 0; i < importPriceUpdates.Count; i++)
                                {
                                    purchaseCaseBuilder.Append($" WHEN ProductCode = @PurchasePc{i} THEN @PurchasePrice{i}");
                                    pList.Add(new SugarParameter($"@PurchasePc{i}", importPriceUpdates[i].Detail.ProductCode));
                                    pList.Add(new SugarParameter($"@PurchasePrice{i}", importPriceUpdates[i].Update.进口价格!.Value));
                                }
                                for (int i = 0; i < oemPriceUpdates.Count; i++)
                                {
                                    retailCaseBuilder.Append($" WHEN ProductCode = @RetailPc{i} THEN @RetailPrice{i}");
                                    pList.Add(new SugarParameter($"@RetailPc{i}", oemPriceUpdates[i].Detail.ProductCode));
                                    pList.Add(new SugarParameter($"@RetailPrice{i}", oemPriceUpdates[i].Update.贴牌价格!.Value));
                                }
                                var inClause = string.Join(
                                    ", ",
                                    Enumerable
                                        .Range(0, storePriceProductCodes.Count)
                                        .Select(i => $"@Pc{i}")
                                );
                                var setClause = new List<string>();
                                if (importPriceUpdates.Count > 0)
                                    setClause.Add($"PurchasePrice = CASE {purchaseCaseBuilder} ELSE PurchasePrice END");
                                if (oemPriceUpdates.Count > 0)
                                    setClause.Add($"StoreRetailPriceValue = CASE {retailCaseBuilder} ELSE StoreRetailPriceValue END");
                                var sql =
                                    $"UPDATE StoreRetailPrice SET {string.Join(", ", setClause)} WHERE ProductCode IN ({inClause})";
                                await _context.Db.Ado.ExecuteCommandAsync(sql, pList);
                            }

                            _logger.LogInformation(
                                "[React] 同步已有商品价格到关联表，数量: {Count}",
                                existingProductUpdates.Count
                            );
                        }

                        await _context.Db.Ado.CommitTranAsync();
                    }
                    catch
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }

                var totalUpdated = updatedRequestGuids.Count;

                _logger.LogInformation(
                    "[React] 批量更新货柜明细完成，成功更新: {TotalUpdated}/{Total}",
                    totalUpdated,
                    updates.Count
                );

                return totalUpdated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 批量更新货柜明细失败");
                throw;
            }
        }

        private async Task<List<string>> ResolveContainerDetailBatchScopeHguidsAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request
        )
        {
            var selectedHguids = request.SelectedHguids
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList();
            if (selectedHguids.Count > 0)
            {
                return selectedHguids;
            }

            if (request.Query == null)
            {
                return new List<string>();
            }

            // 未勾选时以当前服务端筛选条件为作用范围，避免前端为了批量操作补拉整柜明细。
            request.Query.ContainerGuid = containerGuid;
            return await BuildContainerDetailQuery(request.Query)
                .Select((cd, wp, dp, lp) => cd.DetailCode)
                .ToListAsync();
        }

        private static decimal? CalculateScopedTransportCost(
            ContainerDetail detail,
            Container container
        )
        {
            if (
                !container.ShippingFee.HasValue
                || !container.TotalVolume.HasValue
                || container.TotalVolume.Value <= 0
                || !detail.TotalVolume.HasValue
                || !detail.LoadingQuantity.HasValue
                || detail.LoadingQuantity.Value <= 0
            )
            {
                return detail.TransportCost;
            }

            var cost =
                container.ShippingFee.Value
                * detail.TotalVolume.Value
                / detail.LoadingQuantity.Value
                / container.TotalVolume.Value;
            return Math.Round(cost, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal? CalculateScopedImportPrice(
            ContainerDetail detail,
            Container container,
            decimal floatRate,
            decimal? transportCost
        )
        {
            if (
                !container.ExchangeRate.HasValue
                || container.ExchangeRate.Value <= 0
                || !detail.DomesticPrice.HasValue
            )
            {
                return detail.ImportPrice;
            }

            var price =
                ((detail.DomesticPrice.Value / container.ExchangeRate.Value + (transportCost ?? 0m))
                    * floatRate
                    * 10m)
                / 11m;
            return Math.Round(price, 2, MidpointRounding.AwayFromZero);
        }

        private async Task<List<ContainerDetail>> GetScopedDetailsAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request
        )
        {
            var hguids = await ResolveContainerDetailBatchScopeHguidsAsync(containerGuid, request);
            if (hguids.Count == 0)
            {
                return new List<ContainerDetail>();
            }

            return await _context
                .Db.Queryable<ContainerDetail>()
                .Where(detail => hguids.Contains(detail.DetailCode))
                .ToListAsync();
        }

        public async Task<int> ApplyFloatRateByScopeAsync(
            string containerGuid,
            ContainerDetailApplyFloatRateRequestDto request
        )
        {
            if (!request.FloatRate.HasValue)
            {
                return 0;
            }

            var container = await _context
                .Db.Queryable<Container>()
                .FirstAsync(c => c.ContainerCode == containerGuid);
            if (container == null)
            {
                return 0;
            }

            var details = await GetScopedDetailsAsync(containerGuid, request);
            var updates = details
                .Select(detail =>
                {
                    var transportCost = CalculateScopedTransportCost(detail, container);
                    return new UpdateContainerDetailDto
                    {
                        HGUID = detail.DetailCode,
                        调整浮率 = request.FloatRate,
                        运输成本 = transportCost,
                        进口价格 = CalculateScopedImportPrice(
                            detail,
                            container,
                            request.FloatRate.Value,
                            transportCost
                        ),
                    };
                })
                .ToList();

            return await BatchUpdateDetailsAsync(updates);
        }

        public async Task<int> ApplyPricesByScopeAsync(
            string containerGuid,
            ContainerDetailApplyPricesRequestDto request
        )
        {
            if (!request.ImportPrice.HasValue && !request.OemPrice.HasValue)
            {
                return 0;
            }

            var details = await GetScopedDetailsAsync(containerGuid, request);
            var updates = details
                .Select(detail => new UpdateContainerDetailDto
                {
                    HGUID = detail.DetailCode,
                    进口价格 = request.ImportPrice ?? detail.ImportPrice,
                    贴牌价格 = request.OemPrice ?? detail.OEMPrice,
                })
                .ToList();

            return await BatchUpdateDetailsAsync(updates);
        }

        public async Task<int> RecalculateCostsByScopeAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request
        )
        {
            var container = await _context
                .Db.Queryable<Container>()
                .FirstAsync(c => c.ContainerCode == containerGuid);
            if (container == null)
            {
                return 0;
            }

            var details = await GetScopedDetailsAsync(containerGuid, request);
            var updates = details
                .Select(detail =>
                {
                    var floatRate = detail.AdjustmentRate ?? 1m;
                    var transportCost = CalculateScopedTransportCost(detail, container);
                    return new UpdateContainerDetailDto
                    {
                        HGUID = detail.DetailCode,
                        调整浮率 = floatRate,
                        运输成本 = transportCost,
                        进口价格 = CalculateScopedImportPrice(
                            detail,
                            container,
                            floatRate,
                            transportCost
                        ),
                    };
                })
                .ToList();

            return await BatchUpdateDetailsAsync(updates);
        }

        /// <summary>
        /// 创建新货柜
        /// </summary>
        public async Task<string> CreateContainerAsync(CreateContainerDto dto)
        {
            try
            {
                var containerNumber = dto.货柜编号.Trim();
                var loadingDate = dto.装柜日期?.Date;
                _logger.LogInformation("[React] 开始创建货柜: {ContainerNumber}", containerNumber);

                // 货柜编号允许重复，只限制同一编号在同一装柜日期重复创建。
                var existsQuery = _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerNumber == containerNumber);
                existsQuery = loadingDate.HasValue
                    ? existsQuery.Where(x =>
                        x.LoadingDate >= loadingDate.Value
                        && x.LoadingDate < loadingDate.Value.AddDays(1)
                    )
                    : existsQuery.Where(x => x.LoadingDate == null);
                var exists = await existsQuery.AnyAsync();

                if (exists)
                {
                    var loadingDateText = loadingDate?.ToString("yyyy-MM-dd") ?? "未设置";
                    throw new InvalidOperationException($"货柜编号 {containerNumber} 在装柜日期 {loadingDateText} 已存在");
                }

                // 创建新货柜实体
                var container = new Container
                {
                    ContainerCode = Guid.NewGuid().ToString(),
                    ContainerNumber = containerNumber,
                    LoadingDate = loadingDate,
                    EstimatedArrivalDate = dto.预计到岸日期,
                    ActualArrivalDate = null,
                    ExchangeRate = dto.汇率,
                    ShippingFee = dto.运费,
                    Remarks = dto.备注,
                    Status = 0, // 默认状态：已装柜/草稿
                    TotalPieces = 0,
                    TotalQuantity = 0,
                    TotalAmount = 0,
                    TotalVolume = 0,
                    CostFloatRate = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false,
                };

                // 插入数据库并返回实体
                var result = await _context.Db.Insertable(container).ExecuteReturnEntityAsync();

                _logger.LogInformation(
                    "[React] 创建货柜成功: {ContainerCode}, 货柜编号: {ContainerNumber}",
                    result.ContainerCode,
                    result.ContainerNumber
                );

                return result.ContainerCode;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[React] 创建货柜失败, 货柜编号: {ContainerNumber}",
                    dto.货柜编号
                );
                throw;
            }
        }

        /// <summary>
        /// 查找货柜（兼容 ContainerNumber 或 ContainerCode）
        /// </summary>
        private async Task<Container?> FindContainerByIdAsync(string containerId)
        {
            if (string.IsNullOrWhiteSpace(containerId))
                return null;
            // 优先按业务编号匹配
            var container = await _context
                .Db.Queryable<Container>()
                .Where(x => x.ContainerNumber == containerId)
                .FirstAsync();
            if (container != null)
                return container;
            // 退化为按编码匹配
            container = await _context
                .Db.Queryable<Container>()
                .Where(x => x.ContainerCode == containerId)
                .FirstAsync();
            return container;
        }

        /// <summary>
        /// 检查商品是否已存在于指定货柜中
        /// 用于前端在添加商品前进行冲突检测
        /// </summary>
        public async Task<List<ContainerConflictItemDto>> CheckConflictsAsync(
            string containerId,
            List<string> productCodes
        )
        {
            try
            {
                // 查找货柜
                var container = await FindContainerByIdAsync(containerId);
                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerId}", containerId);
                    return new List<ContainerConflictItemDto>();
                }

                // 过滤有效的商品编码
                var codes = productCodes
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
                if (!codes.Any())
                    return new List<ContainerConflictItemDto>();

                // 查询货柜中已存在的商品明细
                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(x =>
                        x.ContainerCode == container.ContainerCode
                        && x.ProductCode != null
                        && codes.Contains(x.ProductCode)
                    )
                    .Select(x => new ContainerConflictItemDto
                    {
                        ProductCode = x.ProductCode!,
                        ExistingPieces = x.LoadingPieces,
                        ExistingPackingQuantity = x.PackingQuantity,
                        ExistingUnitVolume = x.UnitVolume,
                    })
                    .ToListAsync();

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查货柜明细冲突失败");
                throw;
            }
        }

        /// <summary>
        /// 将商品分配到货柜
        /// 支持三种冲突处理策略：replace（替换）、merge（合并）、keep（保留原数据）
        /// </summary>
        public async Task<AssignProductsResultDto> AssignProductsAsync(
            string containerId,
            List<AssignProductItemDto> items,
            string resolution,
            string? notes
        )
        {
            var result = new AssignProductsResultDto();
            try
            {
                // 查找货柜
                var container = await FindContainerByIdAsync(containerId);
                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerId}", containerId);
                    // 货柜不存在，全部标记为失败
                    result.Failed.AddRange(
                        items.Select(i => new AssignProductFailedItemDto
                        {
                            ProductCode = i.ProductCode,
                            Error = "货柜不存在",
                        })
                    );
                    return result;
                }

                // 判断是否为覆盖模式（override = 替换，其他 = 累加）
                var isOverride = string.Equals(
                    resolution,
                    "override",
                    StringComparison.OrdinalIgnoreCase
                );

                // 【修复 N+1 查询问题】预加载货柜明细数据，避免在循环中逐个查询
                var productCodes = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.ProductCode))
                    .Select(i => i.ProductCode)
                    .Distinct()
                    .ToList();
                Dictionary<string, ContainerDetail> detailDict;
                try
                {
                    var existingDetails = await _context
                        .Db.Queryable<ContainerDetail>()
                        .Where(x =>
                            x.ContainerCode == container.ContainerCode
                            && productCodes.Contains(x.ProductCode)
                        )
                        .ToListAsync();
                    detailDict = existingDetails.ToDictionary(d => d.ProductCode);
                }
                catch (Exception exPreload)
                {
                    _logger.LogError(exPreload, "预加载货柜明细数据失败");
                    detailDict = new Dictionary<string, ContainerDetail>();
                }

                // 逐个处理商品
                foreach (var item in items)
                {
                    try
                    {
                        // 参数校验
                        if (string.IsNullOrWhiteSpace(item.ProductCode))
                        {
                            result.Failed.Add(
                                new AssignProductFailedItemDto
                                {
                                    ProductCode = "",
                                    Error = "ProductCode 不能为空",
                                }
                            );
                            continue;
                        }

                        // 从预加载字典中查找货柜明细（修复 N+1 查询）
                        detailDict.TryGetValue(item.ProductCode, out var detail);

                        if (detail == null)
                        {
                            // 场景1：新建明细
                            detail = new ContainerDetail
                            {
                                ContainerCode = container.ContainerCode,
                                ProductCode = item.ProductCode,
                                LoadingPieces = item.Quantity,
                                PackingQuantity = item.PackingQuantity,
                                UnitVolume = item.UnitVolume,
                                DomesticPrice = item.DomesticPrice,
                                OEMPrice = item.OEMPrice,
                                Remarks = string.IsNullOrWhiteSpace(item.Notes)
                                    ? notes
                                    : item.Notes,
                            };

                            // 计算装柜数量（装箱数 × 件数）
                            detail.LoadingQuantity =
                                (detail.PackingQuantity ?? 0m) * (detail.LoadingPieces ?? 0m);
                            // 计算总体积（件数 × 单件体积）
                            detail.TotalVolume =
                                (detail.LoadingPieces ?? 0m) * (detail.UnitVolume ?? 0m);
                            // 统一调用计算方法更新总金额（统一精度：金额2位、体积3位）
                            detail.UpdateCalculatedFields();

                            await _context.Db.Insertable(detail).ExecuteCommandAsync();
                            result.Created++;
                        }
                        else
                        {
                            // 场景2：更新已有明细
                            var currentPieces = detail.LoadingPieces ?? 0m;
                            // 根据冲突处理策略决定件数：覆盖模式直接替换，否则累加
                            detail.LoadingPieces = isOverride
                                ? item.Quantity
                                : (currentPieces + item.Quantity);

                            // 更新装箱数（如提供）
                            if (item.PackingQuantity.HasValue)
                            {
                                detail.PackingQuantity = item.PackingQuantity.Value;
                            }
                            // 更新单件体积（如提供）
                            if (item.UnitVolume.HasValue)
                            {
                                detail.UnitVolume = item.UnitVolume.Value;
                            }
                            // 更新国内价格（如提供）
                            if (item.DomesticPrice.HasValue)
                            {
                                detail.DomesticPrice = item.DomesticPrice.Value;
                            }
                            // 更新贴牌价格（如提供）
                            if (item.OEMPrice.HasValue)
                            {
                                detail.OEMPrice = item.OEMPrice.Value;
                            }
                            // 追加备注
                            var noteText = string.IsNullOrWhiteSpace(item.Notes)
                                ? notes
                                : item.Notes;
                            if (!string.IsNullOrWhiteSpace(noteText))
                            {
                                detail.Remarks = string.IsNullOrWhiteSpace(detail.Remarks)
                                    ? noteText
                                    : ($"{detail.Remarks}; {noteText}");
                            }

                            // 重新计算装柜数量与总体积
                            detail.LoadingQuantity =
                                (detail.PackingQuantity ?? 0m) * (detail.LoadingPieces ?? 0m);
                            detail.TotalVolume =
                                (detail.LoadingPieces ?? 0m) * (detail.UnitVolume ?? 0m);
                            // 统一调用计算方法更新总金额与总体积
                            detail.UpdateCalculatedFields();

                            // 保存更新
                            await _context
                                .Db.Updateable(detail)
                                .UpdateColumns(x => new
                                {
                                    x.LoadingPieces,
                                    x.PackingQuantity,
                                    x.UnitVolume,
                                    x.DomesticPrice,
                                    x.OEMPrice,
                                    x.LoadingQuantity,
                                    x.TotalVolume,
                                    x.TotalAmount,
                                    x.Remarks,
                                })
                                .ExecuteCommandAsync();
                            result.Updated++;
                        }
                    }
                    catch (Exception exItem)
                    {
                        _logger.LogError(
                            exItem,
                            "分配商品失败: ProductCode={ProductCode}",
                            item.ProductCode
                        );
                        result.Failed.Add(
                            new AssignProductFailedItemDto
                            {
                                ProductCode = item.ProductCode,
                                Error = exItem.Message,
                            }
                        );
                    }
                }

                // 可选：更新主表汇总字段（TotalPieces/TotalQuantity/TotalVolume/TotalAmount）
                try
                {
                    var detailsAll = await _context
                        .Db.Queryable<ContainerDetail>()
                        .Where(x => x.ContainerCode == container.ContainerCode)
                        .ToListAsync();

                    container.TotalPieces = detailsAll.Sum(d => d.LoadingPieces ?? 0m);
                    container.TotalQuantity = detailsAll.Sum(d => d.LoadingQuantity ?? 0m);
                    container.TotalVolume = detailsAll.Sum(d => d.TotalVolume ?? 0m);
                    container.TotalAmount = detailsAll.Sum(d => d.TotalAmount ?? 0m);

                    await _context
                        .Db.Updateable(container)
                        .UpdateColumns(x => new
                        {
                            x.TotalPieces,
                            x.TotalQuantity,
                            x.TotalVolume,
                            x.TotalAmount,
                        })
                        .ExecuteCommandAsync();
                }
                catch (Exception exSum)
                {
                    _logger.LogWarning(exSum, "更新主表汇总字段失败，但不影响分配结果");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配商品到货柜失败");
                throw;
            }
        }

        /// <summary>
        /// 批量删除货柜明细
        /// 删除后会自动更新货柜主表的汇总字段
        /// </summary>
        public async Task<int> BatchDeleteDetailsAsync(List<string> hguids)
        {
            try
            {
                // 参数校验
                if (hguids == null || !hguids.Any())
                {
                    _logger.LogWarning("[React] 批量删除明细列表为空");
                    return 0;
                }

                _logger.LogInformation("[React] 开始批量删除货柜明细，数量: {Count}", hguids.Count);

                // 查询要删除的明细以及涉及的货柜编码（用于后续更新汇总）
                var detailsToDelete = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .Select(d => new { d.DetailCode, d.ContainerCode })
                    .ToListAsync();

                if (!detailsToDelete.Any())
                {
                    _logger.LogWarning("[React] 未找到待删除的明细");
                    return 0;
                }

                // 收集涉及的货柜编码
                var containerCodes = detailsToDelete
                    .Select(x => x.ContainerCode)
                    .Where(c => c != null)
                    .Distinct()
                    .ToList();

                // 执行删除操作
                var deletedRows = await _context
                    .Db.Deleteable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .ExecuteCommandAsync();

                // 【修复 N+1 查询问题】批量获取所有涉及的货柜和明细数据，避免循环查询
                // 1. 批量获取所有货柜
                var containers = await _context
                    .Db.Queryable<Container>()
                    .Where(c => containerCodes.Contains(c.ContainerCode))
                    .ToListAsync();
                var containerDict = containers.ToDictionary(c => c.ContainerCode);

                // 2. 批量获取所有货柜明细（用于汇总计算）
                var allDetails = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .ToListAsync();
                var detailsByContainer = allDetails
                    .GroupBy(d => d.ContainerCode)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 3. 在内存中计算汇总值并逐个更新（保持原有的异常处理粒度）
                foreach (var code in containerCodes)
                {
                    try
                    {
                        // 从字典中获取货柜（内存操作，无数据库查询）
                        if (!containerDict.TryGetValue(code, out var container))
                            continue;

                        // 从字典中获取明细（内存操作，无数据库查询）
                        if (detailsByContainer.TryGetValue(code, out var detailsAll))
                        {
                            // 计算汇总值
                            container.TotalPieces = detailsAll.Sum(d => d.LoadingPieces ?? 0m);
                            container.TotalQuantity = detailsAll.Sum(d => d.LoadingQuantity ?? 0m);
                            container.TotalVolume = detailsAll.Sum(d => d.TotalVolume ?? 0m);
                            container.TotalAmount = detailsAll.Sum(d => d.TotalAmount ?? 0m);
                        }
                        else
                        {
                            // 如果没有明细，所有汇总字段归零
                            container.TotalPieces = 0m;
                            container.TotalQuantity = 0m;
                            container.TotalVolume = 0m;
                            container.TotalAmount = 0m;
                        }

                        // 保存汇总更新
                        await _context
                            .Db.Updateable(container)
                            .UpdateColumns(x => new
                            {
                                x.TotalPieces,
                                x.TotalQuantity,
                                x.TotalVolume,
                                x.TotalAmount,
                            })
                            .ExecuteCommandAsync();
                    }
                    catch (Exception exSum)
                    {
                        // 汇总更新失败不影响删除结果，仅记录警告
                        _logger.LogWarning(
                            exSum,
                            "更新货柜 {ContainerCode} 汇总字段失败，但不影响删除结果",
                            code
                        );
                    }
                }

                _logger.LogInformation(
                    "[React] 批量删除货柜明细完成，成功删除: {Deleted}/{Total}",
                    deletedRows,
                    hguids.Count
                );
                return deletedRows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 批量删除货柜明细失败");
                throw;
            }
        }

        /// <summary>
        /// 获取即将到港的货柜及其商品列表（Coming Soon 页面专用）
        /// 条件：未来8周内预计到港 + 最近一周内实际到港
        /// </summary>
        public async Task<List<ComingSoonContainerDto>> GetComingSoonContainersAsync()
        {
            try
            {
                var now = DateTime.Now;
                var eightWeeksLater = now.AddDays(56); // 8周
                var oneWeekAgo = now.AddDays(-7); // 一周前

                _logger.LogInformation(
                    "[React] 获取即将到港货柜: 当前日期={Now}, 未来8周截止={EightWeeksLater}, 一周前={OneWeekAgo}",
                    now,
                    eightWeeksLater,
                    oneWeekAgo
                );

                // 查询条件：
                // 1. 未来8周内预计到港的货柜 (EstimatedArrivalDate 在 now 和 eightWeeksLater 之间，且 ActualArrivalDate 为空)
                // 2. 最近一周内实际到港的货柜 (ActualArrivalDate 在 oneWeekAgo 和 now 之间)
                var query = _context
                    .Db.Queryable<Container>()
                    .Where(x => x.Status != null && x.Status > 0);

                var containers = await query.ToListAsync();

                // 过滤符合条件的货柜
                var comingSoonContainers = containers
                    .Where(c =>
                        // 条件1: 未来8周内预计到港 且 未实际到港
                        (
                            c.EstimatedArrivalDate.HasValue
                            && c.EstimatedArrivalDate >= now
                            && c.EstimatedArrivalDate <= eightWeeksLater
                            && !c.ActualArrivalDate.HasValue
                        )
                        ||
                        // 条件2: 最近一周内实际到港
                        (
                            c.ActualArrivalDate.HasValue
                            && c.ActualArrivalDate >= oneWeekAgo
                            && c.ActualArrivalDate <= now
                        )
                    )
                    .ToList();

                _logger.LogInformation(
                    "[React] 找到 {Count} 个即将到港/已到港货柜",
                    comingSoonContainers.Count
                );

                var result = new List<ComingSoonContainerDto>();

                foreach (var container in comingSoonContainers)
                {
                    // 获取每个货柜的商品明细
                    var details = await _context
                        .Db.Queryable<ContainerDetail>()
                        .LeftJoin<DomesticProduct>((cd, p) => cd.ProductCode == p.ProductCode)
                        .Where((cd, p) => cd.ContainerCode == container.ContainerCode)
                        .Where((cd, p) => cd.ProductCode != null)
                        .Select(
                            (cd, p) =>
                                new ComingSoonProductDto
                                {
                                    商品编码 = cd.ProductCode,
                                    货号 = p.HBProductNo,
                                    商品名称 = p.ProductName,
                                    英文名称 = p.EnglishProductName,
                                    商品图片 = p.ProductImage,
                                    装柜数量 = cd.LoadingQuantity,
                                }
                        )
                        .ToListAsync();

                    result.Add(
                        new ComingSoonContainerDto
                        {
                            货柜编号 = container.ContainerNumber,
                            货柜编码 = container.ContainerCode,
                            装柜日期 = container.LoadingDate,
                            预计到岸日期 = container.EstimatedArrivalDate,
                            实际到货日期 = container.ActualArrivalDate,
                            状态 = container.Status,
                            商品列表 = details,
                        }
                    );
                }

                // 按预计到岸日期排序（已到港的排在前面）
                result = result
                    .OrderByDescending(x => x.实际到货日期.HasValue)
                    .ThenBy(x => x.预计到岸日期)
                    .ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 获取即将到港货柜失败");
                throw;
            }
        }

        public async Task<SyncResult> SyncContainersWithDetailsFromHqAsync(
            DateTime? startDate = null
        )
        {
            return await _containerHqSyncService.SyncIncrementalAsync(startDate);
        }

        public async Task<SyncResult> PushContainersToHbSalesAsync(List<string> containerGuids)
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                if (containerGuids == null || !containerGuids.Any())
                {
                    result.IsSuccess = false;
                    result.Message = "未选择要推送的货柜";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                _logger.LogInformation(
                    "[ContainerPush] 开始推送 {Count} 个货柜到HBSales",
                    containerGuids.Count
                );

                var containers = await _context
                    .Db.Queryable<Container>()
                    .Where(x => containerGuids.Contains(x.ContainerCode))
                    .ToListAsync();

                if (!containers.Any())
                {
                    result.IsSuccess = false;
                    result.Message = "未找到对应的货柜记录";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                var containerCodes = containers
                    .Select(c => c.ContainerCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .ToListAsync();

                var existingHqMaster = await _hbSalesContext
                    .Db.Queryable<CPT_RED_货柜单主表HBSales>()
                    .Where(x =>
                        SqlFunc.ContainsArray(
                            containers.Select(c => c.ContainerCode).ToList(),
                            x.HGUID
                        )
                    )
                    .ToListAsync();
                var existingMasterGuids = new HashSet<string>(
                    existingHqMaster
                        .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                        .Select(x => x.HGUID!)
                );

                var existingHqDetail = await _hbSalesContext
                    .Db.Queryable<CPT_RED_货柜单详情表Store>()
                    .Where(x => SqlFunc.ContainsArray(containerCodes, x.主表GUID))
                    .ToListAsync();
                var existingDetailGuids = new HashSet<string>(
                    existingHqDetail
                        .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                        .Select(x => x.HGUID!)
                );

                var masterToAdd = new List<CPT_RED_货柜单主表HBSales>();
                var masterToUpdate = new List<CPT_RED_货柜单主表HBSales>();
                var detailToAdd = new List<CPT_RED_货柜单详情表Store>();
                var detailToUpdate = new List<CPT_RED_货柜单详情表Store>();

                foreach (var container in containers)
                {
                    var hqEntity = MapToHqMasterForHbSales(container);
                    if (existingMasterGuids.Contains(container.ContainerCode!))
                        masterToUpdate.Add(hqEntity);
                    else
                        masterToAdd.Add(hqEntity);
                }

                foreach (var detail in details)
                {
                    var hqDetail = MapToHqDetail(detail);
                    if (existingDetailGuids.Contains(detail.DetailCode!))
                        detailToUpdate.Add(hqDetail);
                    else
                        detailToAdd.Add(hqDetail);
                }

                if (masterToAdd.Any())
                {
                    await _hbSalesContext
                        .Db.Fastest<CPT_RED_货柜单主表HBSales>()
                        .AS("CPT_RED_货柜单主表")
                        .PageSize(5000)
                        .BulkCopyAsync(masterToAdd);
                }

                if (masterToUpdate.Any())
                {
                    await _hbSalesContext
                        .Db.Fastest<CPT_RED_货柜单主表HBSales>()
                        .AS("CPT_RED_货柜单主表")
                        .PageSize(5000)
                        .BulkUpdateAsync(masterToUpdate);
                }

                if (detailToAdd.Any())
                {
                    await _hbSalesContext
                        .Db.Fastest<CPT_RED_货柜单详情表Store>()
                        .AS("CPT_RED_货柜单详情表")
                        .PageSize(5000)
                        .BulkCopyAsync(detailToAdd);
                }

                if (detailToUpdate.Any())
                {
                    await _hbSalesContext
                        .Db.Fastest<CPT_RED_货柜单详情表Store>()
                        .AS("CPT_RED_货柜单详情表")
                        .PageSize(5000)
                        .BulkUpdateAsync(detailToUpdate);
                }

                result.IsSuccess = true;
                result.AddedCount = masterToAdd.Count + detailToAdd.Count;
                result.UpdatedCount = masterToUpdate.Count + detailToUpdate.Count;
                result.TotalCount = containers.Count;
                result.Message =
                    $"推送完成：主表新增{masterToAdd.Count}/更新{masterToUpdate.Count}，明细新增{detailToAdd.Count}/更新{detailToUpdate.Count}";

                _logger.LogInformation(
                    "[ContainerPush] 推送到HBSales完成: {Message}",
                    result.Message
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ContainerPush] 推送货柜到HBSales失败");
                result.IsSuccess = false;
                result.Message = $"推送失败: {ex.Message}";
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        private static CPT_RED_货柜单主表Store MapToHqMaster(Container c)
        {
            return new CPT_RED_货柜单主表Store
            {
                HGUID = c.ContainerCode,
                货柜编号 = c.ContainerNumber,
                装柜日期 = c.LoadingDate,
                预计到岸日期 = c.EstimatedArrivalDate,
                实际到货日期 = c.ActualArrivalDate,
                合计件数 = c.TotalPieces,
                合计数量 = c.TotalQuantity,
                合计金额 = c.TotalAmount,
                总体积 = c.TotalVolume,
                成本浮率 = c.CostFloatRate,
                汇率 = c.ExchangeRate,
                运费 = c.ShippingFee,
                备注 = c.Remarks,
                备注2 = c.Remarks2,
                状态 = c.Status,
                FGC_LastModifyDate = DateTime.UtcNow,
            };
        }

        private static CPT_RED_货柜单主表HBSales MapToHqMasterForHbSales(Container c)
        {
            return new CPT_RED_货柜单主表HBSales
            {
                HGUID = c.ContainerCode,
                货柜编号 = c.ContainerNumber,
                装柜日期 = c.LoadingDate,
                预计到岸日期 = c.EstimatedArrivalDate,
                合计件数 = c.TotalPieces,
                合计数量 = c.TotalQuantity,
                合计金额 = c.TotalAmount,
                总体积 = c.TotalVolume,
                运费 = c.ShippingFee,
                备注 = c.Remarks,
                状态 = c.Status,
                FGC_LastModifyDate = DateTime.UtcNow,
            };
        }

        private static CPT_RED_货柜单详情表Store MapToHqDetail(ContainerDetail d)
        {
            return new CPT_RED_货柜单详情表Store
            {
                HGUID = d.DetailCode,
                主表GUID = d.ContainerCode,
                商品编码 = d.ProductCode,
                装柜类型 = d.LoadingType,
                混装GUID = d.MixedGroupCode,
                商品类型 = d.ProductType,
                套装数量 = d.SetQuantity,
                装柜件数 = d.LoadingPieces,
                装柜数量 = d.LoadingQuantity,
                国内价格 = d.DomesticPrice,
                调整浮率 = d.AdjustmentRate,
                进口价格 = d.ImportPrice,
                贴牌价格 = d.OEMPrice,
                单件装箱数 = d.PackingQuantity,
                单件体积 = d.UnitVolume,
                合计装柜金额 = d.TotalAmount,
                合计装柜体积 = d.TotalVolume,
                运输成本 = d.TransportCost,
                备注 = d.Remarks,
                状态 = d.Status,
                FGC_LastModifyDate = DateTime.UtcNow,
            };
        }
    }
}
