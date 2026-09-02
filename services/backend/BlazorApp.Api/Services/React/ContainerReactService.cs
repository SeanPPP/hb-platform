using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
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
        private readonly IWarehouseProductChangeHistoryService _changeHistoryService;
        private readonly ICurrentUserService _currentUserService;

        public ContainerReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            HBSalesSqlSugarContext hbSalesContext,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<ContainerReactService> logger,
            IContainerHqSyncService containerHqSyncService,
            ITranslationService translationService,
            IWarehouseProductChangeHistoryService changeHistoryService,
            ICurrentUserService currentUserService
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
            _changeHistoryService = changeHistoryService;
            _currentUserService = currentUserService;
        }

        private bool IsValidEnglishName(string? englishName)
        {
            return !string.IsNullOrWhiteSpace(englishName)
                && !_translationService.ContainsChinese(englishName);
        }

        private string? NormalizeEnglishNameForWrite(string? englishName)
        {
            if (string.IsNullOrWhiteSpace(englishName))
            {
                return null;
            }

            var normalized = englishName.Trim();
            return IsValidEnglishName(normalized) ? normalized : null;
        }

        /// <summary>
        /// 按货柜明细重新汇总主表统计字段
        /// </summary>
        private async Task RefreshContainerSummariesAsync(
            ContainerMutationLockScope? mutationLock,
            IEnumerable<string?> containerCodes
        )
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

            if (mutationLock == null)
            {
                throw new ContainerMutationScopeChangedException(codes);
            }
            mutationLock.EnsureCovers(_context.Db, codes);

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

        private static string NormalizeRequiredCode(string? value, string fieldName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException($"{fieldName}不能为空");
            }

            return normalized;
        }

        private static string MapDomesticProductTypeLabel(int? productType)
        {
            // 货柜明细商品类型展示以国内商品表为准，明细表 ProductType 只保留为历史快照字段。
            return productType switch
            {
                1 => "套装商品",
                2 => "多码商品",
                _ => "普通商品",
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
                query = query.Where((cd, wp, dp, lp) =>
                    (lp.ProductName != null && lp.ProductName.Contains(englishName))
                    || (lp.ProductName == null && dp.EnglishProductName != null && dp.EnglishProductName.Contains(englishName))
                );
            }
            if (remark != null)
            {
                query = query.Where((cd, wp, dp, lp) => cd.Remarks != null && cd.Remarks.Contains(remark));
            }

            if (HasAny(request.ProductTypes))
            {
                var productTypes = request.ProductTypes;
                query = query.Where((cd, wp, dp, lp) =>
                    (productTypes.Contains("normal") && dp.ProductType == 0)
                    || (productTypes.Contains("set") && dp.ProductType == 1)
                    || (productTypes.Contains("multi") && dp.ProductType == 2)
                    // 套装子项仍取货柜明细历史快照字段，国内商品表中不会作为独立商品类型维护。
                    || (productTypes.Contains("setChild") && cd.ProductType == "套装子商品")
                );
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
            var middlePackQuantityMinValue = request.MiddlePackQuantityMin ?? request.MiddlePackQuantity?.Min;
            var middlePackQuantityMaxValue = request.MiddlePackQuantityMax ?? request.MiddlePackQuantity?.Max;
            var middlePackQuantityMin = middlePackQuantityMinValue.HasValue
                ? (int?)Math.Ceiling(middlePackQuantityMinValue.Value)
                : null;
            var middlePackQuantityMax = middlePackQuantityMaxValue.HasValue
                ? (int?)Math.Floor(middlePackQuantityMaxValue.Value)
                : null;
            var containerQuantityMin = request.ContainerQuantityMin ?? request.ContainerQuantity?.Min;
            var containerQuantityMax = request.ContainerQuantityMax ?? request.ContainerQuantity?.Max;
            var packingQuantityMin = request.PackingQuantityMin ?? request.PackingQuantity?.Min;
            var packingQuantityMax = request.PackingQuantityMax ?? request.PackingQuantity?.Max;
            var unitVolumeMin = request.UnitVolumeMin ?? request.UnitVolume?.Min;
            var unitVolumeMax = request.UnitVolumeMax ?? request.UnitVolume?.Max;
            var domesticPriceMin = request.DomesticPriceMin ?? request.DomesticPrice?.Min;
            var domesticPriceMax = request.DomesticPriceMax ?? request.DomesticPrice?.Max;
            var floatRateMin = request.FloatRateMin ?? request.FloatRate?.Min;
            var floatRateMax = request.FloatRateMax ?? request.FloatRate?.Max;
            var transportCostMin = request.TransportCostMin ?? request.TransportCost?.Min;
            var transportCostMax = request.TransportCostMax ?? request.TransportCost?.Max;
            var unitTransportCostMin = request.UnitTransportCostMin ?? request.UnitTransportCost?.Min;
            var unitTransportCostMax = request.UnitTransportCostMax ?? request.UnitTransportCost?.Max;
            var warehouseImportPriceMin = request.WarehouseImportPriceMin ?? request.WarehouseImportPrice?.Min;
            var warehouseImportPriceMax = request.WarehouseImportPriceMax ?? request.WarehouseImportPrice?.Max;
            var lastOEMPriceMin = request.LastOEMPriceMin ?? request.LastOEMPrice?.Min;
            var lastOEMPriceMax = request.LastOEMPriceMax ?? request.LastOEMPrice?.Max;
            var importPriceMin = request.ImportPriceMin ?? request.ImportPrice?.Min;
            var importPriceMax = request.ImportPriceMax ?? request.ImportPrice?.Max;
            var oemPriceMin = request.OemPriceMin ?? request.OemPrice?.Min;
            var oemPriceMax = request.OemPriceMax ?? request.OemPrice?.Max;

            if (containerPiecesMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingPieces >= containerPiecesMin);
            if (containerPiecesMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingPieces <= containerPiecesMax);
            if (middlePackQuantityMin != null)
                query = query.Where((cd, wp, dp, lp) => (wp.MinOrderQuantity ?? dp.MiddlePackQuantity) >= middlePackQuantityMin);
            if (middlePackQuantityMax != null)
                query = query.Where((cd, wp, dp, lp) => (wp.MinOrderQuantity ?? dp.MiddlePackQuantity) <= middlePackQuantityMax);
            if (containerQuantityMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingQuantity >= containerQuantityMin);
            if (containerQuantityMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.LoadingQuantity <= containerQuantityMax);
            if (packingQuantityMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.PackingQuantity >= packingQuantityMin);
            if (packingQuantityMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.PackingQuantity <= packingQuantityMax);
            if (unitVolumeMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.UnitVolume >= unitVolumeMin);
            if (unitVolumeMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.UnitVolume <= unitVolumeMax);
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
            if (unitTransportCostMin != null)
            {
                // 单件运输成本按前端展示保留两位小数，服务端用原始区间匹配，避免不同数据库 ROUND 翻译差异。
                var minRawValue = Convert.ToDouble(unitTransportCostMin.Value - 0.0050001m);
                query = query.Where((cd, wp, dp, lp) => SqlFunc.ToDouble(cd.TransportCost) * SqlFunc.ToDouble(cd.PackingQuantity) >= minRawValue);
            }
            if (unitTransportCostMax != null)
            {
                // max 使用开区间上界，避免 0.505 这类会显示为 0.51 的值被 0.50 筛选命中。
                var maxRawValue = Convert.ToDouble(unitTransportCostMax.Value + 0.0050001m);
                query = query.Where((cd, wp, dp, lp) => SqlFunc.ToDouble(cd.TransportCost) * SqlFunc.ToDouble(cd.PackingQuantity) < maxRawValue);
            }
            // 实时仓库价列按 WarehouseProduct 过滤，Last* 快照只保留历史比较基准。
            if (warehouseImportPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => wp.ImportPrice >= warehouseImportPriceMin);
            if (warehouseImportPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => wp.ImportPrice <= warehouseImportPriceMax);
            if (lastOEMPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => wp.OEMPrice >= lastOEMPriceMin);
            if (lastOEMPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => wp.OEMPrice <= lastOEMPriceMax);
            if (importPriceMin != null)
                query = query.Where((cd, wp, dp, lp) => cd.ImportPrice >= importPriceMin);
            if (importPriceMax != null)
                query = query.Where((cd, wp, dp, lp) => cd.ImportPrice <= importPriceMax);
            // 零售价列新商品取明细价，已有商品取仓库实时价；筛选拆成 OR，避免 SQLite 下 IIF where 空命中。
            if (oemPriceMin != null)
                query = query.Where((cd, wp, dp, lp) =>
                    (lp.ProductCode == null && cd.OEMPrice >= oemPriceMin)
                    || (lp.ProductCode != null && wp.OEMPrice >= oemPriceMin)
                );
            if (oemPriceMax != null)
                query = query.Where((cd, wp, dp, lp) =>
                    (lp.ProductCode == null && cd.OEMPrice <= oemPriceMax)
                    || (lp.ProductCode != null && wp.OEMPrice <= oemPriceMax)
                );

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
                "englishName" => query.OrderBy((cd, wp, dp, lp) => lp.ProductName ?? dp.EnglishProductName, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "productType" => query.OrderBy((cd, wp, dp, lp) => dp.ProductType, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "containerPieces" => query.OrderBy((cd, wp, dp, lp) => cd.LoadingPieces, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "middlePackQuantity" => query.OrderBy((cd, wp, dp, lp) => wp.MinOrderQuantity ?? dp.MiddlePackQuantity, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "containerQuantity" => query.OrderBy((cd, wp, dp, lp) => cd.LoadingQuantity, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "packingQuantity" => query.OrderBy((cd, wp, dp, lp) => cd.PackingQuantity, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "unitVolume" => query.OrderBy((cd, wp, dp, lp) => cd.UnitVolume, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "domesticPrice" => query.OrderBy((cd, wp, dp, lp) => cd.DomesticPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "floatRate" => query.OrderBy((cd, wp, dp, lp) => cd.AdjustmentRate, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "transportCost" => query.OrderBy((cd, wp, dp, lp) => cd.TransportCost, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "unitTransportCost" => query.OrderBy((cd, wp, dp, lp) => cd.TransportCost * cd.PackingQuantity, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "warehouseImportPrice" => query.OrderBy((cd, wp, dp, lp) => wp.ImportPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "lastOEMPrice" => query.OrderBy((cd, wp, dp, lp) => wp.OEMPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "importPrice" => query.OrderBy((cd, wp, dp, lp) => cd.ImportPrice, orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
                "oemPrice" => query.OrderBy((cd, wp, dp, lp) => SqlFunc.IIF(SqlFunc.IsNull(lp.ProductCode, "") == "", cd.OEMPrice, wp.OEMPrice), orderType).OrderBy((cd, wp, dp, lp) => cd.DetailCode),
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

                if (!string.IsNullOrWhiteSpace(request.ContainerNumberFilter))
                {
                    var containerNumberFilter = request.ContainerNumberFilter.Trim();
                    query = query.Where(x =>
                        x.ContainerNumber != null && x.ContainerNumber.Contains(containerNumberFilter)
                    );
                }

                // 列头日期过滤按整天闭区间处理，避免结束日当天有时间部分的数据被排除。
                if (request.LoadingDateStart.HasValue)
                {
                    var start = request.LoadingDateStart.Value.Date;
                    query = query.Where(x => x.LoadingDate >= start);
                }
                if (request.LoadingDateEnd.HasValue)
                {
                    var endExclusive = request.LoadingDateEnd.Value.Date.AddDays(1);
                    query = query.Where(x => x.LoadingDate < endExclusive);
                }
                if (request.EstimatedArrivalDateStart.HasValue)
                {
                    var start = request.EstimatedArrivalDateStart.Value.Date;
                    query = query.Where(x => x.EstimatedArrivalDate >= start);
                }
                if (request.EstimatedArrivalDateEnd.HasValue)
                {
                    var endExclusive = request.EstimatedArrivalDateEnd.Value.Date.AddDays(1);
                    query = query.Where(x => x.EstimatedArrivalDate < endExclusive);
                }
                if (request.ActualArrivalDateStart.HasValue)
                {
                    var start = request.ActualArrivalDateStart.Value.Date;
                    query = query.Where(x => x.ActualArrivalDate >= start);
                }
                if (request.ActualArrivalDateEnd.HasValue)
                {
                    var endExclusive = request.ActualArrivalDateEnd.Value.Date.AddDays(1);
                    query = query.Where(x => x.ActualArrivalDate < endExclusive);
                }

                if (request.TotalPiecesMin.HasValue)
                {
                    query = query.Where(x => x.TotalPieces >= request.TotalPiecesMin.Value);
                }
                if (request.TotalPiecesMax.HasValue)
                {
                    query = query.Where(x => x.TotalPieces <= request.TotalPiecesMax.Value);
                }
                if (request.TotalAmountMin.HasValue)
                {
                    query = query.Where(x => x.TotalAmount >= request.TotalAmountMin.Value);
                }
                if (request.TotalAmountMax.HasValue)
                {
                    query = query.Where(x => x.TotalAmount <= request.TotalAmountMax.Value);
                }
                if (request.TotalVolumeMin.HasValue)
                {
                    query = query.Where(x => x.TotalVolume >= request.TotalVolumeMin.Value);
                }
                if (request.TotalVolumeMax.HasValue)
                {
                    query = query.Where(x => x.TotalVolume <= request.TotalVolumeMax.Value);
                }
                if (request.Statuses?.Any() == true)
                {
                    var statuses = request.Statuses.Distinct().ToList();
                    query = query.Where(x => x.Status.HasValue && statuses.Contains(x.Status.Value));
                }

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
        /// 更新货柜基本信息
        /// </summary>
        public async Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto)
        {
            try
            {
                var deadlockRetryCount = 0;
                while (true)
                {
                    await _context.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 编号 + 装柜日期去重跨越所有货柜；独占总闸关闭不同货柜并发改成同一组合的窗口。
                        await ContainerMutationLock.AcquireAllAsync(_context.Db);

                        // 货柜头和明细汇总写同一主表行；持独占总闸后再读取，避免覆盖并发汇总。
                        var container = await _context
                            .Db.Queryable<Container>()
                            .Where(x => x.ContainerCode == containerGuid && !x.IsDeleted)
                            .FirstAsync();

                        if (container == null)
                        {
                            await _context.Db.Ado.CommitTranAsync();
                            _logger.LogWarning("货柜不存在: {ContainerGuid}", containerGuid);
                            return false;
                        }

                        var nextContainerNumber = !string.IsNullOrWhiteSpace(dto.货柜编号)
                            ? dto.货柜编号.Trim()
                            : container.ContainerNumber;
                        var nextLoadingDate = dto.装柜日期 ?? container.LoadingDate;
                        var currentContainerNumber = container.ContainerNumber?.Trim();
                        var containerNumberChanged = !string.Equals(
                            nextContainerNumber,
                            currentContainerNumber,
                            StringComparison.Ordinal
                        );
                        var loadingDateChanged = nextLoadingDate?.Date != container.LoadingDate?.Date;

                        // 仅在编号或装柜日期实际变化时做去重，避免历史脏数据阻断状态/备注等无关保存。
                        var shouldCheckDuplicate = containerNumberChanged || loadingDateChanged;
                        if (shouldCheckDuplicate && !string.IsNullOrWhiteSpace(nextContainerNumber))
                        {
                            var duplicateQuery = _context
                                .Db.Queryable<Container>()
                                .Where(x =>
                                    x.ContainerCode != containerGuid
                                    && x.ContainerNumber == nextContainerNumber
                                );
                            duplicateQuery = nextLoadingDate.HasValue
                                ? duplicateQuery.Where(x =>
                                    x.LoadingDate >= nextLoadingDate.Value.Date
                                    && x.LoadingDate < nextLoadingDate.Value.Date.AddDays(1)
                                )
                                : duplicateQuery.Where(x => x.LoadingDate == null);

                            if (await duplicateQuery.AnyAsync())
                            {
                                var loadingDateText =
                                    nextLoadingDate?.ToString("yyyy-MM-dd") ?? "未设置";
                                throw new InvalidOperationException(
                                    $"货柜编号 {nextContainerNumber} 在装柜日期 {loadingDateText} 已存在"
                                );
                            }
                        }

                        // 根据 DTO 的中文字段逐个更新，避免前端未传字段覆盖已有基础信息。
                        if (!string.IsNullOrWhiteSpace(nextContainerNumber))
                        {
                            container.ContainerNumber = nextContainerNumber;
                        }

                        if (dto.装柜日期.HasValue)
                        {
                            container.LoadingDate = dto.装柜日期.Value;
                        }

                        if (dto.预计到岸日期.HasValue)
                        {
                            container.EstimatedArrivalDate = dto.预计到岸日期.Value;
                        }

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

                        // 保存并限制更新列，避免覆盖并发流程维护的汇总字段。
                        var rowsAffected = await _context
                            .Db.Updateable(container)
                            .UpdateColumns(x => new
                            {
                                x.ContainerNumber,
                                x.LoadingDate,
                                x.EstimatedArrivalDate,
                                x.ActualArrivalDate,
                                x.ExchangeRate,
                                x.ShippingFee,
                                x.Remarks,
                                x.Status,
                            })
                            .Where(x => x.ContainerCode == containerGuid && !x.IsDeleted)
                            .ExecuteCommandAsync();

                        await _context.Db.Ado.CommitTranAsync();
                        _logger.LogInformation(
                            "更新货柜信息成功: {ContainerGuid}, 影响行数: {RowsAffected}",
                            containerGuid,
                            rowsAffected
                        );
                        return rowsAffected > 0;
                    }
                    catch (Exception exception)
                    {
                        await RollbackContainerMutationTransactionSafelyAsync(exception);
                        if (
                            !ContainerMutationLock.ShouldRetryDeadlock(
                                exception,
                                deadlockRetryCount
                            )
                        )
                        {
                            throw;
                        }

                        deadlockRetryCount++;
                        var delayMilliseconds = Random.Shared.Next(100, 301);
                        _logger.LogWarning(
                            exception,
                            "更新货柜信息遇到 SQL Server 1205，{DelayMilliseconds}ms 后完整重试一次",
                            delayMilliseconds
                        );
                        await Task.Delay(delayMilliseconds);
                    }
                }
            }
            catch (Exception ex) when (ContainerMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
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
                                ProductCategoryGUID = cd.TargetWarehouseCategoryGUID ?? lp.WarehouseCategoryGUID,
                                装柜类型 = cd.LoadingType,
                                商品类型 = cd.ProductType,
                                套装数量 = cd.SetQuantity,
                                装柜件数 = cd.LoadingPieces,
                                中包数 = wp.MinOrderQuantity ?? dp.MiddlePackQuantity,
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
                                    ProductCategoryGUID = cd.TargetWarehouseCategoryGUID ?? lp.WarehouseCategoryGUID,
                                    货号 = dp.HBProductNo,
                                    商品名称 = dp.ProductName,
                                    // 已有商品按本地主档商品名称展示英文列；未建主档时才回退国内英文名。
                                    英文名称 = lp.ProductName ?? dp.EnglishProductName,
                                    商品图片 = dp.ProductImage,
                                    条形码 = dp.Barcode,
                                    // SqlSugar 投影内不能调用 C# helper，这里映射需与 MapDomesticProductTypeLabel 保持一致。
                                    商品类型 = dp.ProductType == 1 ? "套装商品" : dp.ProductType == 2 ? "多码商品" : "普通商品",
                                },
                                // Last* 保留货柜明细快照；Warehouse* 展示仓库商品实时价。
                                LastImportPrice = cd.LastImportPrice,
                                LastOEMPrice = cd.LastOEMPrice,
                                WarehouseImportPrice = wp.ImportPrice,
                                WarehouseOEMPrice = wp.OEMPrice,
                                ReadonlyOemPrice = lp.ProductCode == null ? dp.OEMPrice : wp.OEMPrice,
                                WarehouseIsActive = wp.IsActive,
                            }
                    )
                    .ToListAsync();

                await FillContainerDetailCategoryNamesAsync(products);
                FillContainerDetailProductImages(products);
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
                    var emptyTotalComputed = request.IncludeTotal || request.IncludeStats;
                    return new ContainerDetailQueryResultDto
                    {
                        PageNumber = Math.Max(1, request.PageNumber),
                        PageSize = Math.Clamp(request.PageSize, 1, 500),
                        TotalComputed = emptyTotalComputed,
                        StatsComputed = request.IncludeStats,
                    };
                }

                var pageNumber = Math.Max(1, request.PageNumber);
                var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 500);
                var query = BuildContainerDetailQuery(request);
                var total = 0;
                var stats = new ContainerDetailTagStatsDto();
                var totalComputed = request.IncludeTotal || request.IncludeStats;

                if (request.IncludeStats)
                {
                    // 标签统计里 All 已经是同口径总数；请求统计时同步视为 total 已计算，避免响应契约出现歧义组合。
                    stats = await QueryContainerDetailTagStatsAsync(query);
                    total = stats.All;
                }
                else if (request.IncludeTotal)
                {
                    total = await query.Clone().CountAsync();
                }

                var takeSize = totalComputed ? pageSize : pageSize + 1;
                var loadedItems = await ApplyContainerDetailSort(query.Clone(), request)
                    .Select(
                        (cd, wp, dp, lp) =>
                            new ContainerDetailDto
                            {
                                HGUID = cd.DetailCode,
                                主表GUID = cd.ContainerCode,
                                商品编码 = cd.ProductCode,
                                LocalSupplierCode = lp.LocalSupplierCode,
                                ProductCategoryGUID = cd.TargetWarehouseCategoryGUID ?? lp.WarehouseCategoryGUID,
                                装柜类型 = cd.LoadingType,
                                商品类型 = cd.ProductType,
                                套装数量 = cd.SetQuantity,
                                装柜件数 = cd.LoadingPieces,
                                中包数 = wp.MinOrderQuantity ?? dp.MiddlePackQuantity,
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
                                LastImportPrice = cd.LastImportPrice,
                                LastOEMPrice = cd.LastOEMPrice,
                                // Last* 保留货柜明细快照；Warehouse* 展示仓库商品实时价。
                                WarehouseImportPrice = wp.ImportPrice,
                                WarehouseOEMPrice = wp.OEMPrice,
                                ReadonlyOemPrice = lp.ProductCode == null ? dp.OEMPrice : wp.OEMPrice,
                                WarehouseIsActive = wp.IsActive,
                                商品信息 = new ContainerProductInfoDto
                                {
                                    商品编码 = dp.ProductCode,
                                    LocalSupplierCode = lp.LocalSupplierCode,
                                    ProductCategoryGUID = cd.TargetWarehouseCategoryGUID ?? lp.WarehouseCategoryGUID,
                                    货号 = dp.HBProductNo,
                                    商品名称 = dp.ProductName,
                                    // 已有商品按本地主档商品名称展示英文列；未建主档时才回退国内英文名。
                                    英文名称 = lp.ProductName ?? dp.EnglishProductName,
                                    商品图片 = dp.ProductImage,
                                    条形码 = dp.Barcode,
                                    商品规格 = dp.ProductSpecification,
                                    单件装箱数 = cd.PackingQuantity,
                                    单件体积 = cd.UnitVolume,
                                    // SqlSugar 投影内不能调用 C# helper，这里映射需与 MapDomesticProductTypeLabel 保持一致。
                                    商品类型 = dp.ProductType == 1 ? "套装商品" : dp.ProductType == 2 ? "多码商品" : "普通商品",
                                    套装数量 = cd.SetQuantity,
                                },
                            }
                    )
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(takeSize)
                    .ToListAsync();

                // 追加页不再计算 total；多取一条即可判断是否还有下一页，返回前截回正常页大小。
                var hasMore = totalComputed
                    ? pageNumber * pageSize < total
                    : loadedItems.Count > pageSize;
                var items = totalComputed
                    ? loadedItems
                    : loadedItems.Take(pageSize).ToList();

                await FillContainerDetailCategoryNamesAsync(items);
                FillContainerDetailProductImages(items);
                return new ContainerDetailQueryResultDto
                {
                    Items = items,
                    ItemsTotal = totalComputed ? total : 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    HasMore = hasMore,
                    TotalComputed = totalComputed,
                    StatsComputed = request.IncludeStats,
                    TagStats = request.IncludeStats ? stats : new ContainerDetailTagStatsDto(),
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

        private async Task<ContainerDetailTagStatsDto> QueryContainerDetailTagStatsAsync(
            ISugarQueryable<ContainerDetail, WarehouseProduct, DomesticProduct, Product> query
        )
        {
            var stats = await query.Clone()
                .Select((cd, wp, dp, lp) => new
                {
                    All = SqlFunc.AggregateCount(cd.DetailCode),
                    New = SqlFunc.AggregateCount(SqlFunc.IIF(lp.ProductCode == null, cd.DetailCode, null)),
                    Existing = SqlFunc.AggregateCount(SqlFunc.IIF(lp.ProductCode != null, cd.DetailCode, null)),
                    NoOemPrice = SqlFunc.AggregateCount(SqlFunc.IIF(lp.ProductCode == null && (cd.OEMPrice == null || cd.OEMPrice <= 0), cd.DetailCode, null)),
                    AbnormalImport = SqlFunc.AggregateCount(SqlFunc.IIF(cd.ImportPrice == null || cd.ImportPrice <= 0, cd.DetailCode, null)),
                    Active = SqlFunc.AggregateCount(SqlFunc.IIF(wp.IsActive == true, cd.DetailCode, null)),
                    Inactive = SqlFunc.AggregateCount(SqlFunc.IIF(wp.IsActive != true, cd.DetailCode, null)),
                })
                .FirstAsync();

            return stats == null
                ? new ContainerDetailTagStatsDto()
                : new ContainerDetailTagStatsDto
                {
                    All = stats.All,
                    New = stats.New,
                    Existing = stats.Existing,
                    NoOemPrice = stats.NoOemPrice,
                    AbnormalImport = stats.AbnormalImport,
                    Active = stats.Active,
                    Inactive = stats.Inactive,
                };
        }

        private static void FillContainerDetailProductImages(List<ContainerDetailDto> items)
        {
            foreach (var item in items)
            {
                if (item.商品信息 == null)
                {
                    continue;
                }

                // 货柜明细和国内商品页共用默认图片规则，避免国内商品有默认图但明细接口返回空图。
                item.商品信息.商品图片 = ProductImageUrlHelper.EnsureImageUrl(
                    item.商品信息.商品图片,
                    item.商品信息.货号 ?? item.商品编码 ?? string.Empty
                );
            }
        }

        private async Task FillContainerDetailCategoryNamesAsync(List<ContainerDetailDto> items)
        {
            var categoryGuids = items
                .Select(item => item.ProductCategoryGUID)
                .Select(NormalizeCategoryGuid)
                .Where(guid => guid != null)
                .Select(guid => guid!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (categoryGuids.Count == 0)
            {
                return;
            }

            // 分类名称只用于当前返回块展示，单独批量回填，避免扩大明细主查询的筛选、统计和分页风险。
            var categories = await _context.Db.Queryable<WarehouseCategory>()
                .Where(category =>
                    categoryGuids.Contains(category.CategoryGUID.Trim().ToLower())
                    && !category.IsDeleted
                )
                .Select(category => new
                {
                    category.CategoryGUID,
                    category.CategoryName,
                })
                .ToListAsync();

            var categoryByGuid = categories
                .Select(category => new
                {
                    Category = category,
                    NormalizedGuid = NormalizeCategoryGuid(category.CategoryGUID),
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.NormalizedGuid))
                .GroupBy(x => x.NormalizedGuid!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Category,
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var item in items)
            {
                var normalizedGuid = NormalizeCategoryGuid(item.ProductCategoryGUID);
                if (normalizedGuid == null || !categoryByGuid.TryGetValue(normalizedGuid, out var category))
                {
                    continue;
                }

                item.ProductCategoryGUID = category.CategoryGUID;
                item.ProductCategoryName = category.CategoryName;
                if (item.商品信息 != null)
                {
                    item.商品信息.ProductCategoryGUID = item.ProductCategoryGUID;
                    item.商品信息.ProductCategoryName = category.CategoryName;
                }
            }
        }

        private static string? NormalizeCategoryGuid(string? categoryGuid)
        {
            var normalized = categoryGuid?.Trim();
            return string.IsNullOrEmpty(normalized) ? null : normalized.ToLowerInvariant();
        }

        private async Task<Dictionary<string, string>> ValidateTargetCategoryUpdatesAsync(
            List<UpdateContainerDetailDto> updates
        )
        {
            var requestedCategoryGuids = updates
                .Where(update => update.ProductCategoryGUID != null)
                .Select(update => NormalizeCategoryGuid(update.ProductCategoryGUID))
                .ToList();

            if (requestedCategoryGuids.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (requestedCategoryGuids.Any(guid => guid == null))
            {
                throw new InvalidOperationException("请选择目标分类");
            }

            var normalizedCategoryGuids = requestedCategoryGuids
                .Select(guid => guid!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 目标分类会同步写入货柜明细和 Product 主档，写入前必须确认分类真实存在且未删除。
            var categories = await _context.Db.Queryable<WarehouseCategory>()
                .Where(category =>
                    normalizedCategoryGuids.Contains(category.CategoryGUID.Trim().ToLower())
                    && !category.IsDeleted
                )
                .Select(category => new
                {
                    category.CategoryGUID,
                })
                .ToListAsync();

            var canonicalCategoryGuidByNormalizedGuid = categories
                .GroupBy(category => NormalizeCategoryGuid(category.CategoryGUID), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Key != null)
                .ToDictionary(
                    group => group.Key!,
                    group => group.First().CategoryGUID.Trim(),
                    StringComparer.OrdinalIgnoreCase
                );

            if (normalizedCategoryGuids.Any(guid => !canonicalCategoryGuidByNormalizedGuid.ContainsKey(guid)))
            {
                throw new InvalidOperationException("目标分类不存在或已删除");
            }

            return canonicalCategoryGuidByNormalizedGuid;
        }

        private static string? GetValidatedCategoryGuidForWrite(
            string? categoryGuid,
            IReadOnlyDictionary<string, string> canonicalCategoryGuids
        )
        {
            if (categoryGuid == null)
            {
                return null;
            }

            var normalizedGuid = NormalizeCategoryGuid(categoryGuid);
            if (normalizedGuid == null)
            {
                throw new InvalidOperationException("请选择目标分类");
            }

            return canonicalCategoryGuids[normalizedGuid];
        }

        public async Task<List<ContainerDomesticSetCodeDto>> GetDomesticSetCodesAsync(
            string productCode
        )
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return new List<ContainerDomesticSetCodeDto>();
            }

            var product = await _context
                .Db.Queryable<DomesticProduct>()
                .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                .FirstAsync();
            if (product == null)
            {
                return new List<ContainerDomesticSetCodeDto>();
            }

            // 货柜明细弹窗必须读取国内套装表，不能使用仓库/POS 多码价格快照。
            return await _context
                .Db.Queryable<DomesticSetProduct>()
                .Where(item => item.ProductCode == productCode && !item.IsDeleted)
                .OrderBy(item => item.SetProductNo)
                .Select(item => new ContainerDomesticSetCodeDto
                {
                    ProductCode = item.ProductCode,
                    ItemNumber = product.HBProductNo,
                    ProductType = product.ProductType,
                    SetProductCode = item.SetProductCode,
                    SetItemNumber = item.SetProductNo,
                    Barcode = item.SetBarcode,
                    RetailPrice = item.OEMPrice ?? item.DomesticPrice,
                    PurchasePrice = item.ImportPrice,
                })
                .ToListAsync();
        }

        public async Task<int> UpdateDomesticSetCodePricesAsync(
            string productCode,
            UpdateContainerDomesticSetCodePricesRequestDto request,
            string updatedBy
        )
        {
            if (string.IsNullOrWhiteSpace(productCode) || request.Items.Count == 0)
            {
                return 0;
            }

            var setProductCodes = request
                .Items.Select(item => item.SetProductCode)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList();
            if (setProductCodes.Count == 0)
            {
                return 0;
            }

            var existingItems = await _context
                .Db.Queryable<DomesticSetProduct>()
                .Where(item =>
                    item.ProductCode == productCode
                    && setProductCodes.Contains(item.SetProductCode)
                    && !item.IsDeleted
                )
                .ToListAsync();
            var existingMap = existingItems.ToDictionary(item => item.SetProductCode);
            var now = DateTime.UtcNow;
            var changedItems = new List<DomesticSetProduct>();

            foreach (var update in request.Items)
            {
                if (
                    string.IsNullOrWhiteSpace(update.SetProductCode)
                    || !existingMap.TryGetValue(update.SetProductCode, out var item)
                )
                {
                    continue;
                }

                var changed = false;
                // 货柜明细弹窗的“价格”回写国内套装表 OEMPrice，不覆盖 DomesticPrice。
                if (item.OEMPrice != update.RetailPrice)
                {
                    item.OEMPrice = update.RetailPrice;
                    changed = true;
                }
                if (item.ImportPrice != update.PurchasePrice)
                {
                    item.ImportPrice = update.PurchasePrice;
                    changed = true;
                }
                if (changed)
                {
                    item.UpdatedAt = now;
                    item.UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy;
                    changedItems.Add(item);
                }
            }

            if (changedItems.Count == 0)
            {
                return 0;
            }

            await _context
                .Db.Updateable(changedItems)
                .UpdateColumns(item => new
                {
                    item.OEMPrice,
                    item.ImportPrice,
                    item.UpdatedAt,
                    item.UpdatedBy,
                })
                .WhereColumns(item => new { item.SetProductCode })
                .ExecuteCommandAsync();

            return changedItems.Count;
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
                    .Includes(x => x.WarehouseProduct)
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

        public async Task<AlignDomesticProductCodeResultDto> AlignDomesticProductCodeAsync(
            AlignDomesticProductCodeRequestDto request
        )
        {
            if (request == null)
            {
                throw new InvalidOperationException("请求参数不能为空");
            }

            var detailHguid = NormalizeRequiredCode(request.DetailHguid, "明细GUID");
            var oldProductCode = NormalizeRequiredCode(
                request.ExpectedDomesticProductCode,
                "原国内商品编码"
            );
            var targetProductCode = NormalizeRequiredCode(request.TargetProductCode, "本地主档商品编码");
            var supplierCode = NormalizeRequiredCode(request.SupplierCode, "供应商代码");

            if (string.Equals(oldProductCode, targetProductCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("国内商品编码已与本地主档一致");
            }

            var detail = await _context
                .Db.Queryable<ContainerDetail>()
                .FirstAsync(d => d.DetailCode == detailHguid && !d.IsDeleted);
            if (detail == null)
            {
                throw new InvalidOperationException("货柜明细不存在或已删除");
            }
            if (!string.Equals(detail.ProductCode?.Trim(), oldProductCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("明细商品编码已变化，请刷新后重试");
            }
            if (string.Equals(detail.ProductType?.Trim(), "套装子商品", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("套装子商品关联套装结构，暂不支持单独对齐编码");
            }

            var domesticProduct = await _context
                .Db.Queryable<DomesticProduct>()
                .FirstAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
            if (domesticProduct == null)
            {
                throw new InvalidOperationException("原国内商品不存在或已删除");
            }
            if (
                !string.Equals(
                    domesticProduct.SupplierCode?.Trim(),
                    supplierCode,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException("国内商品供应商代码与候选供应商不一致，不能对齐编码");
            }

            var localProduct = await _context
                .Db.Queryable<Product>()
                .FirstAsync(p => p.ProductCode == targetProductCode && !p.IsDeleted);
            if (localProduct == null)
            {
                throw new InvalidOperationException("本地主档商品不存在或已删除");
            }
            if (
                !string.Equals(
                    localProduct.LocalSupplierCode?.Trim(),
                    supplierCode,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException("供应商代码与本地主档不一致，不能对齐编码");
            }

            var domesticItemNumber = domesticProduct.HBProductNo?.Trim();
            var localItemNumber = localProduct.ItemNumber?.Trim();
            if (
                string.IsNullOrWhiteSpace(domesticItemNumber)
                || string.IsNullOrWhiteSpace(localItemNumber)
                || !string.Equals(domesticItemNumber, localItemNumber, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException("国内商品货号与本地主档货号不一致，不能对齐编码");
            }

            var targetDomesticExists = await _context
                .Db.Queryable<DomesticProduct>()
                .AnyAsync(p => p.ProductCode == targetProductCode && !p.IsDeleted);
            if (targetDomesticExists)
            {
                throw new InvalidOperationException("目标国内商品编码已存在，不能自动合并");
            }

            var oldLocalCodeExists = await _context
                .Db.Queryable<Product>()
                .AnyAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
            var oldWarehouseCodeExists = await _context
                .Db.Queryable<WarehouseProduct>()
                .AnyAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
            if (oldLocalCodeExists || oldWarehouseCodeExists)
            {
                throw new InvalidOperationException("原国内商品编码已存在本地主档或仓库商品，不能自动改码");
            }

            await _context.Db.Ado.BeginTranAsync();
            try
            {
                // 该人工操作会全局改写旧商品编码，使用独占总闸关闭新货柜插入同编码的 phantom 窗口。
                var mutationLock = await ContainerMutationLock.AcquireAllAsync(_context.Db);
                var transactionalContainerCodes = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => d.ProductCode == oldProductCode && !d.IsDeleted)
                    .Select(d => d.ContainerCode)
                    .ToListAsync();
                mutationLock.EnsureCovers(_context.Db, transactionalContainerCodes);

                // 事务内复查核心前置条件，避免确认弹窗打开后数据被并发改动仍继续级联改码。
                var transactionalDetail = await _context
                    .Db.Queryable<ContainerDetail>()
                    .FirstAsync(d => d.DetailCode == detailHguid && !d.IsDeleted);
                if (transactionalDetail == null)
                {
                    throw new InvalidOperationException("货柜明细不存在或已删除");
                }
                if (
                    !string.Equals(
                        transactionalDetail.ProductCode?.Trim(),
                        oldProductCode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException("明细商品编码已变化，请刷新后重试");
                }
                if (string.Equals(transactionalDetail.ProductType?.Trim(), "套装子商品", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("套装子商品关联套装结构，暂不支持单独对齐编码");
                }

                var transactionalDomesticProduct = await _context
                    .Db.Queryable<DomesticProduct>()
                    .FirstAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
                if (transactionalDomesticProduct == null)
                {
                    throw new InvalidOperationException("原国内商品不存在或已删除");
                }
                if (
                    !string.Equals(
                        transactionalDomesticProduct.SupplierCode?.Trim(),
                        supplierCode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException("国内商品供应商代码与候选供应商不一致，不能对齐编码");
                }

                var transactionalLocalProduct = await _context
                    .Db.Queryable<Product>()
                    .FirstAsync(p => p.ProductCode == targetProductCode && !p.IsDeleted);
                if (transactionalLocalProduct == null)
                {
                    throw new InvalidOperationException("本地主档商品不存在或已删除");
                }
                if (
                    !string.Equals(
                        transactionalLocalProduct.LocalSupplierCode?.Trim(),
                        supplierCode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException("供应商代码与本地主档不一致，不能对齐编码");
                }

                var transactionalDomesticItemNumber = transactionalDomesticProduct.HBProductNo?.Trim();
                var transactionalLocalItemNumber = transactionalLocalProduct.ItemNumber?.Trim();
                if (
                    string.IsNullOrWhiteSpace(transactionalDomesticItemNumber)
                    || string.IsNullOrWhiteSpace(transactionalLocalItemNumber)
                    || !string.Equals(
                        transactionalDomesticItemNumber,
                        transactionalLocalItemNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException("国内商品货号与本地主档货号不一致，不能对齐编码");
                }

                var targetDomesticExistsInTransaction = await _context
                    .Db.Queryable<DomesticProduct>()
                    .AnyAsync(p => p.ProductCode == targetProductCode && !p.IsDeleted);
                if (targetDomesticExistsInTransaction)
                {
                    throw new InvalidOperationException("目标国内商品编码已存在，不能自动合并");
                }
                var oldLocalCodeExistsInTransaction = await _context
                    .Db.Queryable<Product>()
                    .AnyAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
                var oldWarehouseCodeExistsInTransaction = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .AnyAsync(p => p.ProductCode == oldProductCode && !p.IsDeleted);
                if (oldLocalCodeExistsInTransaction || oldWarehouseCodeExistsInTransaction)
                {
                    throw new InvalidOperationException("原国内商品编码已存在本地主档或仓库商品，不能自动改码");
                }

                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { oldProductCode, targetProductCode }
                );
                if (!beforeSnapshots.TryGetValue(targetProductCode, out var targetBeforeSnapshot))
                {
                    throw new InvalidOperationException("无法读取本地主档审计快照，请刷新后重试");
                }

                // Product.ProductCode 是权威主键；确认后只把国内侧引用从旧编码迁到本地主档编码。
                var updatedDomesticProducts = await _context.Db.Ado.ExecuteCommandAsync(
                    "UPDATE DomesticProduct SET ProductCode = @TargetProductCode WHERE ProductCode = @OldProductCode AND SupplierCode = @SupplierCode AND IsDeleted = 0",
                    new List<SugarParameter>
                    {
                        new("@TargetProductCode", targetProductCode),
                        new("@OldProductCode", oldProductCode),
                        new("@SupplierCode", supplierCode),
                    }
                );
                if (updatedDomesticProducts != 1)
                {
                    throw new InvalidOperationException("原国内商品编码已变化，请刷新后重试");
                }

                var updatedContainerDetails = await _context
                    .Db.Updateable<ContainerDetail>()
                    .SetColumns(d => d.ProductCode == targetProductCode)
                    .Where(d => d.ProductCode == oldProductCode && !d.IsDeleted)
                    .ExecuteCommandAsync();
                var updatedDomesticSetProducts = await _context
                    .Db.Updateable<DomesticSetProduct>()
                    .SetColumns(p => p.ProductCode == targetProductCode)
                    .Where(p => p.ProductCode == oldProductCode && !p.IsDeleted)
                    .ExecuteCommandAsync();
                var updatedProductGrades = await _context
                    .Db.Updateable<ProductGrade>()
                    .SetColumns(p => p.ProductCode == targetProductCode)
                    .Where(p => p.ProductCode == oldProductCode && !p.IsDeleted)
                    .ExecuteCommandAsync();
                var updatedDomesticProductCreationLogs = await _context
                    .Db.Updateable<DomesticProductCreationLog>()
                    .SetColumns(p => p.ProductCode == targetProductCode)
                    .Where(p => p.ProductCode == oldProductCode && !p.IsDeleted)
                    .ExecuteCommandAsync();

                var afterSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { targetProductCode }
                );
                var auditActorName = _currentUserService.GetCurrentUsername();
                auditActorName = string.IsNullOrWhiteSpace(auditActorName)
                    ? "System"
                    : auditActorName.Trim();
                var isSystemActor = string.Equals(
                    auditActorName,
                    "System",
                    StringComparison.OrdinalIgnoreCase
                );
                var auditActorUserGuid = _currentUserService.GetCurrentUserGuid();
                isSystemActor = string.IsNullOrWhiteSpace(auditActorUserGuid) && isSystemActor;
                // 事件挂在仍然有效的新编码下；旧编码保留在 before 快照中形成单条编码差异。
                await _changeHistoryService.RecordChangesAsync(
                    new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                        StringComparer.OrdinalIgnoreCase
                    )
                    {
                        [targetProductCode] = targetBeforeSnapshot with
                        {
                            ProductCode = oldProductCode,
                        },
                    },
                    afterSnapshots,
                    new WarehouseProductChangeHistoryContextDto
                    {
                        Action = "Update",
                        Source = "ContainerDetail",
                        SourceReference = string.IsNullOrWhiteSpace(transactionalDetail.ContainerCode)
                            ? detailHguid
                            : transactionalDetail.ContainerCode,
                        ActorUserGuid = string.IsNullOrWhiteSpace(auditActorUserGuid)
                            ? null
                            : auditActorUserGuid,
                        ActorName = auditActorName,
                        ActorType = isSystemActor ? "System" : "User",
                        OccurredAtUtc = DateTime.UtcNow,
                    }
                );

                await _context.Db.Ado.CommitTranAsync();

                return new AlignDomesticProductCodeResultDto
                {
                    OldProductCode = oldProductCode,
                    NewProductCode = targetProductCode,
                    UpdatedDomesticProducts = updatedDomesticProducts,
                    UpdatedContainerDetails = updatedContainerDetails,
                    UpdatedDomesticSetProducts = updatedDomesticSetProducts,
                    UpdatedProductGrades = updatedProductGrades,
                    UpdatedDomesticProductCreationLogs = updatedDomesticProductCreationLogs,
                };
            }
            catch (ContainerMutationScopeChangedException exception)
            {
                await RollbackContainerMutationTransactionSafelyAsync(exception);
                throw new ContainerMutationLockException("scope-changed", -1, exception);
            }
            catch (Exception exception)
            {
                await RollbackContainerMutationTransactionSafelyAsync(exception);
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
            var result = await BatchUpdateDetailsCoreAsync(
                updates,
                countValidNoOps: false,
                repairMissingStoreRelations: false,
                containerGuid: null
            );
            return result.TotalUpdated;
        }

        /// <summary>
        /// 兼容旧客户端的无货柜范围部分成功入口。
        /// </summary>
        public Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsDetailedAsync(
            List<UpdateContainerDetailDto> updates
        )
        {
            return BatchUpdateDetailsCoreAsync(
                updates,
                countValidNoOps: true,
                repairMissingStoreRelations: true,
                containerGuid: null
            );
        }

        /// <summary>
        /// 在指定货柜范围内批量更新明细，并返回 React 页面所需的部分成功和字段级错误。
        /// </summary>
        public Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsDetailedAsync(
            string containerGuid,
            List<UpdateContainerDetailDto> updates
        )
        {
            return BatchUpdateDetailsCoreAsync(
                updates,
                countValidNoOps: true,
                repairMissingStoreRelations: true,
                containerGuid
            );
        }

        private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsCoreAsync(
            List<UpdateContainerDetailDto> updates,
            bool countValidNoOps,
            bool repairMissingStoreRelations,
            string? containerGuid
        )
        {
            if (updates == null || updates.Count == 0)
            {
                return await BatchUpdateDetailsAttemptAsync(
                    updates ?? new List<UpdateContainerDetailDto>(),
                    countValidNoOps,
                    repairMissingStoreRelations,
                    containerGuid,
                    mutationLock: null
                );
            }

            // 锁前只读取候选货柜编号；所有会计算或回写的数据必须在持锁事务中重新读取。
            List<string> candidateContainerCodes = string.IsNullOrWhiteSpace(containerGuid)
                ? await _context.Db.Queryable<ContainerDetail>()
                    .Where(detail =>
                        updates.Select(update => update.HGUID).Contains(detail.DetailCode)
                        && !detail.IsDeleted
                    )
                    .Select(detail => detail.ContainerCode)
                    .ToListAsync()
                : new List<string> { containerGuid! };
            var deadlockRetryCount = 0;
            var scopeChangeRetryCount = 0;
            while (true)
            {
                await _context.Db.Ado.BeginTranAsync();
                try
                {
                    // 旧入口的 HGUID 可能全部不存在；此时使用独占总闸完成锁内复查。
                    // 若明细在候选查询后出现，仍可保证更新受业务锁保护。
                    var mutationLock = candidateContainerCodes.Count == 0
                        ? await ContainerMutationLock.AcquireAllAsync(_context.Db)
                        : await ContainerMutationLock.AcquireContainersAsync(
                            _context.Db,
                            candidateContainerCodes
                        );
                    var result = await BatchUpdateDetailsAttemptAsync(
                        updates,
                        countValidNoOps,
                        repairMissingStoreRelations,
                        containerGuid,
                        mutationLock
                    );
                    await _context.Db.Ado.CommitTranAsync();
                    return result;
                }
                catch (ContainerMutationScopeChangedException exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    if (scopeChangeRetryCount++ > 0)
                    {
                        throw new ContainerMutationLockException("scope-changed", -1, exception);
                    }
                    candidateContainerCodes = exception.ActualContainerCodes.ToList();
                }
                catch (Exception exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    if (!ContainerMutationLock.ShouldRetryDeadlock(exception, deadlockRetryCount))
                    {
                        throw;
                    }
                    deadlockRetryCount++;
                    await Task.Delay(Random.Shared.Next(100, 301));
                }
            }
        }

        private async Task RollbackContainerMutationTransactionSafelyAsync(
            Exception originalException
        )
        {
            if (_context.Db.Ado.Transaction == null)
            {
                return;
            }

            try
            {
                await _context.Db.Ado.RollbackTranAsync();
            }
            catch (Exception rollbackException)
            {
                // SQL Server 死锁会先回滚事务；清理失败不得覆盖原始并发异常。
                ContainerMutationLock.ResetFailedTransaction(_context.Db);
                _logger.LogWarning(
                    rollbackException,
                    "[React] 回滚货柜变更事务失败，保留原始异常: {OriginalMessage}",
                    originalException.Message
                );
            }
        }

        private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsAttemptAsync(
            List<UpdateContainerDetailDto> updates,
            bool countValidNoOps,
            bool repairMissingStoreRelations,
            string? containerGuid,
            ContainerMutationLockScope? mutationLock
        )
        {
            try
            {
                var result = new ContainerDetailBatchUpdateResultDto
                {
                    TotalRequested = updates?.Count ?? 0,
                };

                // 参数校验
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新明细列表为空");
                    return result;
                }

                var duplicateHguids = updates
                    .Where(update => !string.IsNullOrWhiteSpace(update.HGUID))
                    .GroupBy(update => update.HGUID, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (duplicateHguids.Count > 0)
                {
                    if (!countValidNoOps)
                    {
                        // 严格内部入口没有字段级返回载体，重复明细必须整体拒绝，不能按请求顺序覆盖。
                        throw new InvalidOperationException("批量更新请求包含重复货柜明细");
                    }

                    // React/移动端部分成功入口将重复明细整行拒绝；其余唯一明细仍可独立保存。
                    foreach (var hguid in duplicateHguids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                    {
                        result.ValidationErrors.Add(
                            new ContainerDetailBatchUpdateValidationErrorDto
                            {
                                HGUID = hguid,
                                Field = "*",
                                Code = "DUPLICATE_DETAIL_UPDATE",
                                Message = "同一请求不能重复提交同一货柜明细",
                            }
                        );
                    }
                    updates = updates
                        .Where(update => !duplicateHguids.Contains(update.HGUID))
                        .ToList();
                    _logger.LogInformation(
                        "[React] 开始批量更新货柜明细，请求: {RequestedCount}，有效: {ValidCount}，重复拒绝: {DuplicateCount}",
                        result.TotalRequested,
                        updates.Count,
                        duplicateHguids.Count
                    );
                    if (updates.Count == 0)
                    {
                        _logger.LogInformation(
                            "[React] 批量更新货柜明细完成，成功更新: {TotalUpdated}/{Total}",
                            0,
                            result.TotalRequested
                        );
                        return result;
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "[React] 开始批量更新货柜明细，请求: {RequestedCount}，有效: {ValidCount}，重复拒绝: {DuplicateCount}",
                        result.TotalRequested,
                        updates.Count,
                        0
                    );
                }

                // 第一步：查询需要更新的明细记录
                var hguids = updates.Select(u => u.HGUID).Distinct().ToList();
                var allExistingDetails = await _context.Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode) && !d.IsDeleted)
                    .ToListAsync();
                var details = string.IsNullOrWhiteSpace(containerGuid)
                    ? allExistingDetails
                    : allExistingDetails
                        .Where(detail => detail.ContainerCode == containerGuid)
                        .ToList();
                var outOfScopeHguids = string.IsNullOrWhiteSpace(containerGuid)
                    ? new HashSet<string>()
                    : allExistingDetails
                        .Where(detail => detail.ContainerCode != containerGuid)
                        .Select(detail => detail.DetailCode)
                        .ToHashSet();

                var actualContainerCodes = details
                    .Select(detail => detail.ContainerCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .ToList();
                if (actualContainerCodes.Count > 0)
                {
                    if (mutationLock == null)
                    {
                        throw new ContainerMutationScopeChangedException(
                            ContainerMutationLock.NormalizeContainerCodes(actualContainerCodes)
                        );
                    }
                    mutationLock.EnsureCovers(_context.Db, actualContainerCodes);
                }

                // 构建明细编码到明细实体的映射，便于快速查找
                var detailMap = details.ToDictionary(d => d.DetailCode, d => d);
                var changedDetails = new List<ContainerDetail>();
                var updatedRequestGuids = new HashSet<string>();
                var existingDetailUpdates = updates
                    .Where(update => detailMap.ContainsKey(update.HGUID))
                    .ToList();
                foreach (var update in updates.Where(update => !detailMap.ContainsKey(update.HGUID)))
                {
                    result.ValidationErrors.Add(
                        new ContainerDetailBatchUpdateValidationErrorDto
                        {
                            HGUID = update.HGUID,
                            Field = "*",
                            Code = outOfScopeHguids.Contains(update.HGUID)
                                ? "DETAIL_OUTSIDE_CONTAINER"
                                : "DETAIL_NOT_FOUND",
                            Message = outOfScopeHguids.Contains(update.HGUID)
                                ? "货柜明细不属于当前货柜"
                                : "货柜明细不存在",
                        }
                    );
                }

                // 不存在的 HGUID 整行阻断，也不能让该行携带的分类值触发后续校验。
                var validatedTargetCategoryGuids = await ValidateTargetCategoryUpdatesAsync(
                    existingDetailUpdates
                );

                // OEM 与上下架同时写明细和商品主档；同一规范化商品必须先合并意图，
                // 冲突时只拒绝该字段，不能让请求顺序决定最终主档值。
                var rejectedOemPriceHguids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rejectedActiveHguids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var productGroup in existingDetailUpdates
                    .Where(update =>
                        !string.IsNullOrWhiteSpace(detailMap[update.HGUID].ProductCode)
                    )
                    .GroupBy(
                        update => detailMap[update.HGUID].ProductCode!.Trim(),
                        StringComparer.OrdinalIgnoreCase
                    ))
                {
                    var oemUpdates = productGroup
                        .Where(update => update.贴牌价格.HasValue)
                        .ToList();
                    if (oemUpdates.Select(update => update.贴牌价格!.Value).Distinct().Count() > 1)
                    {
                        foreach (var update in oemUpdates)
                        {
                            if (!rejectedOemPriceHguids.Add(update.HGUID))
                            {
                                continue;
                            }
                            result.ValidationErrors.Add(
                                new ContainerDetailBatchUpdateValidationErrorDto
                                {
                                    HGUID = update.HGUID,
                                    Field = "贴牌价格",
                                    Code = "CONFLICTING_PRODUCT_OEM_PRICE",
                                    Message = "同一商品的贴牌价格更新意图冲突",
                                }
                            );
                        }
                    }

                    var activeUpdates = productGroup
                        .Where(update => update.IsActive.HasValue)
                        .ToList();
                    if (activeUpdates.Select(update => update.IsActive!.Value).Distinct().Count() > 1)
                    {
                        foreach (var update in activeUpdates)
                        {
                            if (!rejectedActiveHguids.Add(update.HGUID))
                            {
                                continue;
                            }
                            result.ValidationErrors.Add(
                                new ContainerDetailBatchUpdateValidationErrorDto
                                {
                                    HGUID = update.HGUID,
                                    Field = "IsActive",
                                    Code = "CONFLICTING_PRODUCT_ACTIVE_STATE",
                                    Message = "同一商品的上下架更新意图冲突",
                                }
                            );
                        }
                    }
                }

                bool HasAcceptedOemPrice(UpdateContainerDetailDto update) =>
                    update.贴牌价格.HasValue
                    && !rejectedOemPriceHguids.Contains(update.HGUID);

                bool HasAcceptedActive(UpdateContainerDetailDto update) =>
                    update.IsActive.HasValue
                    && !rejectedActiveHguids.Contains(update.HGUID);

                var adjustmentRateWrites = new HashSet<string>();
                var domesticPriceWrites = new HashSet<string>();
                var importPriceWrites = new HashSet<string>();
                var transportCostWrites = new HashSet<string>();
                var oemPriceWrites = new HashSet<string>();
                var packingQuantityWrites = new HashSet<string>();
                var unitVolumeWrites = new HashSet<string>();
                var loadingQuantityWrites = new HashSet<string>();
                var totalVolumeWrites = new HashSet<string>();
                var totalAmountWrites = new HashSet<string>();
                var activeWrites = new HashSet<string>();
                var categoryWrites = new HashSet<string>();
                var remarkWrites = new HashSet<string>();

                foreach (var update in existingDetailUpdates)
                {
                    var hasDirectDetailIntent =
                        update.调整浮率.HasValue
                        || update.国内价格.HasValue
                        || (!repairMissingStoreRelations && update.进口价格.HasValue)
                        || update.运输成本.HasValue
                        || HasAcceptedOemPrice(update)
                        || update.单件装箱数.HasValue
                        || update.单件体积.HasValue
                        || update.装柜数量.HasValue
                        || update.合计装柜体积.HasValue
                        || update.合计装柜金额.HasValue
                        || HasAcceptedActive(update)
                        || update.ProductCategoryGUID != null
                        || update.备注 != null;
                    if (countValidNoOps && hasDirectDetailIntent)
                    {
                        // 有效目标上的同值保存也是成功，避免前端把 no-op 误判为静默跳过。
                        updatedRequestGuids.Add(update.HGUID);
                    }
                }

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
                        adjustmentRateWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.国内价格.HasValue && detail.DomesticPrice != update.国内价格.Value)
                    {
                        detail.DomesticPrice = update.国内价格.Value;
                        domesticPriceWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (
                        !repairMissingStoreRelations
                        && update.进口价格.HasValue
                        && detail.ImportPrice != update.进口价格.Value
                    )
                    {
                        detail.ImportPrice = update.进口价格.Value;
                        importPriceWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.运输成本.HasValue && detail.TransportCost != update.运输成本.Value)
                    {
                        detail.TransportCost = update.运输成本.Value;
                        transportCostWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (HasAcceptedOemPrice(update) && detail.OEMPrice != update.贴牌价格!.Value)
                    {
                        detail.OEMPrice = update.贴牌价格.Value;
                        oemPriceWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.单件装箱数.HasValue && detail.PackingQuantity != update.单件装箱数.Value)
                    {
                        detail.PackingQuantity = update.单件装箱数.Value;
                        packingQuantityWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.单件体积.HasValue && detail.UnitVolume != update.单件体积.Value)
                    {
                        detail.UnitVolume = update.单件体积.Value;
                        unitVolumeWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.装柜数量.HasValue && detail.LoadingQuantity != update.装柜数量.Value)
                    {
                        detail.LoadingQuantity = update.装柜数量.Value;
                        loadingQuantityWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.合计装柜体积.HasValue && detail.TotalVolume != update.合计装柜体积.Value)
                    {
                        detail.TotalVolume = update.合计装柜体积.Value;
                        totalVolumeWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.合计装柜金额.HasValue && detail.TotalAmount != update.合计装柜金额.Value)
                    {
                        detail.TotalAmount = update.合计装柜金额.Value;
                        totalAmountWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (HasAcceptedActive(update) && detail.IsActive != update.IsActive!.Value)
                    {
                        detail.IsActive = update.IsActive.Value;
                        activeWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (
                        update.备注 != null
                        && !string.Equals(detail.Remarks, update.备注, StringComparison.Ordinal)
                    )
                    {
                        detail.Remarks = update.备注;
                        remarkWrites.Add(update.HGUID);
                        changed = true;
                    }
                    if (update.ProductCategoryGUID != null)
                    {
                        var nextCategoryGuid = GetValidatedCategoryGuidForWrite(
                            update.ProductCategoryGUID,
                            validatedTargetCategoryGuids
                        );
                        if (detail.TargetWarehouseCategoryGUID != nextCategoryGuid)
                        {
                            // 目标分类先落在货柜明细上；未匹配新商品创建时会继承它。
                            detail.TargetWarehouseCategoryGUID = nextCategoryGuid;
                            categoryWrites.Add(update.HGUID);
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        changedDetails.Add(detail);
                        updatedRequestGuids.Add(update.HGUID);
                    }
                }

                foreach (var update in existingDetailUpdates)
                {
                    var detail = detailMap[update.HGUID];
                    if (
                        !string.IsNullOrWhiteSpace(detail.ProductCode)
                        || update.SkipRelatedProductSync == true
                    )
                    {
                        continue;
                    }

                    var clearEnglishName = update.ClearEnglishName == true;
                    var hasEnglishName = !string.IsNullOrWhiteSpace(update.英文名称);
                    if (!clearEnglishName && !hasEnglishName)
                    {
                        continue;
                    }

                    if (
                        hasEnglishName
                        && _translationService.ContainsChinese(update.英文名称!.Trim())
                    )
                    {
                        result.ValidationErrors.Add(
                            new ContainerDetailBatchUpdateValidationErrorDto
                            {
                                HGUID = update.HGUID,
                                Field = "英文名称",
                                Code = "CONTAINS_CHINESE",
                                Message = "英文名称不能包含中文",
                            }
                        );
                        if (!clearEnglishName)
                        {
                            continue;
                        }
                    }

                    result.ValidationErrors.Add(
                        new ContainerDetailBatchUpdateValidationErrorDto
                        {
                            HGUID = update.HGUID,
                            Field = "英文名称",
                            Code = "RELATED_PRODUCT_NOT_FOUND",
                            Message = "关联国内商品或本地主档商品不存在",
                        }
                    );
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
                var auditBatchGuid = Guid.NewGuid();
                var auditActorName = _currentUserService.GetCurrentUsername();
                var auditActorGuid = _currentUserService.GetCurrentUserGuid();
                var now = DateTime.UtcNow;
                // SkipRelatedProductSync 只允许写货柜明细，关联主数据回填必须统一走这个过滤集合。
                var relatedSyncDetailUpdates = validDetailUpdates
                    .Where(x => x.Update.SkipRelatedProductSync != true)
                    .ToList();

                // 预加载商品数据：既用于名称回写，也用于判断价格同步是否是已有商品。
                var productMap = new Dictionary<string, DomesticProduct>();
                if (productCodes.Count > 0)
                {
                    var products = await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                        .ToListAsync();
                    productMap = products.ToDictionary(p => p.ProductCode, p => p);
                }

                var warehouseProductMap = new Dictionary<string, WarehouseProduct>();
                if (productCodes.Count > 0)
                {
                    var warehouseProducts = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                        .ToListAsync();
                    warehouseProductMap = warehouseProducts.ToDictionary(p => p.ProductCode, p => p);
                }

                var matchedLocalProductCodes = new HashSet<string>();
                var localProductMap = new Dictionary<string, Product>();
                if (productCodes.Count > 0)
                {
                    var matchedProducts = await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                        .ToListAsync();
                    localProductMap = matchedProducts
                        .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                        .ToDictionary(product => product.ProductCode!, product => product);
                    matchedLocalProductCodes = matchedProducts
                        .Select(product => product.ProductCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!)
                        .ToHashSet();
                }

                var acceptedEnglishNameByUpdate =
                    new Dictionary<UpdateContainerDetailDto, string?>();
                var englishNameCandidates =
                    new List<(
                        UpdateContainerDetailDto Update,
                        string ProductCode,
                        string? EnglishName
                    )>();

                foreach (var item in relatedSyncDetailUpdates)
                {
                    var clearEnglishName = item.Update.ClearEnglishName == true;
                    string? normalizedEnglishName = null;

                    if (!string.IsNullOrWhiteSpace(item.Update.英文名称))
                    {
                        normalizedEnglishName = item.Update.英文名称!.Trim();
                        if (_translationService.ContainsChinese(normalizedEnglishName))
                        {
                            result.ValidationErrors.Add(
                                new ContainerDetailBatchUpdateValidationErrorDto
                                {
                                    HGUID = item.Update.HGUID,
                                    Field = "英文名称",
                                    Code = "CONTAINS_CHINESE",
                                    Message = "英文名称不能包含中文",
                                }
                            );

                            // 同一行显式清空仍是独立有效意图；否则只拒绝英文名称字段。
                            if (!clearEnglishName)
                            {
                                continue;
                            }
                            normalizedEnglishName = null;
                        }
                    }

                    if (!clearEnglishName && normalizedEnglishName == null)
                    {
                        continue;
                    }

                    if (
                        !productMap.ContainsKey(item.ProductCode!)
                        && !localProductMap.ContainsKey(item.ProductCode!)
                    )
                    {
                        result.ValidationErrors.Add(
                            new ContainerDetailBatchUpdateValidationErrorDto
                            {
                                HGUID = item.Update.HGUID,
                                Field = "英文名称",
                                Code = "RELATED_PRODUCT_NOT_FOUND",
                                Message = "关联国内商品或本地主档商品不存在",
                            }
                        );
                        continue;
                    }

                    englishNameCandidates.Add(
                        (
                            item.Update,
                            item.ProductCode!,
                            clearEnglishName ? null : normalizedEnglishName
                        )
                    );
                }

                foreach (var group in englishNameCandidates.GroupBy(item => item.ProductCode))
                {
                    var distinctIntents = group
                        .Select(item =>
                            item.EnglishName == null
                                ? "\0CLEAR"
                                : $"NAME:{item.EnglishName}"
                        )
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (distinctIntents.Count > 1)
                    {
                        foreach (var item in group)
                        {
                            result.ValidationErrors.Add(
                                new ContainerDetailBatchUpdateValidationErrorDto
                                {
                                    HGUID = item.Update.HGUID,
                                    Field = "英文名称",
                                    Code = "CONFLICTING_PRODUCT_ENGLISH_NAME",
                                    Message = "同一商品的英文名称更新意图冲突",
                                }
                            );
                        }
                        continue;
                    }

                    foreach (var item in group)
                    {
                        acceptedEnglishNameByUpdate[item.Update] = item.EnglishName;
                        if (countValidNoOps)
                        {
                            updatedRequestGuids.Add(item.Update.HGUID);
                        }
                    }
                }

                var existingProductUpdates = relatedSyncDetailUpdates
                    .Where(x =>
                        !string.IsNullOrEmpty(x.Detail.ProductCode)
                        && localProductMap.ContainsKey(x.Detail.ProductCode)
                        && (
                            x.Update.进口价格.HasValue
                            || HasAcceptedOemPrice(x.Update)
                            || HasAcceptedActive(x.Update)
                        )
                    )
                    .Select(x => new { x.Update, x.Detail })
                    .ToList();

                foreach (var item in existingProductUpdates)
                {
                    // React 详细保存会在锁内确认进口价是否可提交；其它字段可先按原契约计数。
                    if (
                        !repairMissingStoreRelations
                        || HasAcceptedOemPrice(item.Update)
                        || HasAcceptedActive(item.Update)
                    )
                    {
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                }

                var productNameUpdates = relatedSyncDetailUpdates
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Update.商品名称)
                        || acceptedEnglishNameByUpdate.ContainsKey(x.Update)
                    )
                    .GroupBy(x => x.ProductCode!)
                    .Select(group =>
                    {
                        var englishNameUpdates = group
                            .Where(item =>
                                acceptedEnglishNameByUpdate.ContainsKey(item.Update)
                            )
                            .ToList();

                        return new
                        {
                            ProductCode = group.Key,
                            商品名称 = group
                                .Select(x => x.Update.商品名称)
                                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                            英文名称 = englishNameUpdates.Count == 0
                                ? null
                                : acceptedEnglishNameByUpdate[englishNameUpdates[0].Update],
                            HasEnglishNameIntent = englishNameUpdates.Count > 0,
                        };
                    })
                    .ToList();

                if (countValidNoOps)
                {
                    foreach (var item in relatedSyncDetailUpdates)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.Update.商品名称)
                            && productMap.ContainsKey(item.ProductCode!)
                        )
                        {
                            updatedRequestGuids.Add(item.Update.HGUID);
                        }
                        if (
                            item.Update.中包数.HasValue
                            && productMap.ContainsKey(item.ProductCode!)
                        )
                        {
                            updatedRequestGuids.Add(item.Update.HGUID);
                        }
                    }
                }

                var changedProducts = new List<DomesticProduct>();
                var changedProductCodes = new HashSet<string>();
                var changedLocalNameProducts = new Dictionary<string, Product>();
                var changedMiddlePackWarehouseProducts = new Dictionary<string, WarehouseProduct>();
                var changedLocalCategoryProducts = new Dictionary<string, Product>();

                foreach (var item in relatedSyncDetailUpdates)
                {
                    if (item.Update.ProductCategoryGUID == null)
                    {
                        continue;
                    }

                    var nextCategoryGuid = GetValidatedCategoryGuidForWrite(
                        item.Update.ProductCategoryGUID,
                        validatedTargetCategoryGuids
                    );
                    if (
                        nextCategoryGuid != null
                        && localProductMap.TryGetValue(item.ProductCode!, out var localProduct)
                        && localProduct.WarehouseCategoryGUID != nextCategoryGuid
                    )
                    {
                        // 已有商品立即同步仓库分类；未匹配商品只保留明细目标分类等待创建。
                        localProduct.WarehouseCategoryGUID = nextCategoryGuid;
                        localProduct.UpdatedAt = now;
                        localProduct.UpdatedBy = auditActorName;
                        changedLocalCategoryProducts[item.ProductCode!] = localProduct;
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                }

                foreach (var item in relatedSyncDetailUpdates)
                {
                    if (!item.Update.中包数.HasValue)
                    {
                        continue;
                    }

                    if (!productMap.TryGetValue(item.ProductCode!, out var product))
                    {
                        continue;
                    }

                    // 中包数的主数据来源是国内商品表；仓库有值时仅作为显示优先级和已匹配商品同步字段。
                    var nextMiddlePackQuantity = (int)item.Update.中包数.Value;
                    var middlePackChanged = false;
                    if (product.MiddlePackQuantity != nextMiddlePackQuantity)
                    {
                        product.MiddlePackQuantity = nextMiddlePackQuantity;
                        product.UpdatedAt = now;
                        product.UpdatedBy = auditActorName;
                        if (changedProductCodes.Add(item.ProductCode!))
                        {
                            changedProducts.Add(product);
                        }
                        middlePackChanged = true;
                    }

                    if (
                        matchedLocalProductCodes.Contains(item.ProductCode!)
                        && warehouseProductMap.TryGetValue(item.ProductCode!, out var warehouseProduct)
                        && warehouseProduct.MinOrderQuantity != nextMiddlePackQuantity
                    )
                    {
                        warehouseProduct.MinOrderQuantity = nextMiddlePackQuantity;
                        warehouseProduct.UpdatedAt = now;
                        warehouseProduct.UpdatedBy = auditActorName;
                        changedMiddlePackWarehouseProducts[item.ProductCode!] = warehouseProduct;
                        middlePackChanged = true;
                    }

                    if (middlePackChanged)
                    {
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                }

                foreach (var productUpdate in productNameUpdates)
                {
                    productMap.TryGetValue(productUpdate.ProductCode, out var product);
                    localProductMap.TryGetValue(productUpdate.ProductCode, out var localProduct);

                    var productChanged = false;
                    var localProductChanged = false;
                    if (
                        product != null
                        &&
                        !string.IsNullOrWhiteSpace(productUpdate.商品名称)
                        && product.ProductName != productUpdate.商品名称
                    )
                    {
                        product.ProductName = productUpdate.商品名称;
                        product.UpdatedAt = now;
                        product.UpdatedBy = auditActorName;
                        productChanged = true;
                    }
                    if (
                        productUpdate.HasEnglishNameIntent
                        && string.IsNullOrWhiteSpace(productUpdate.英文名称)
                    )
                    {
                        if (product != null && product.EnglishProductName != null)
                        {
                            product.EnglishProductName = null;
                            product.UpdatedAt = now;
                            product.UpdatedBy = auditActorName;
                            productChanged = true;
                        }
                        if (localProduct != null && localProduct.EnglishName != null)
                        {
                            // 清空英文名称只清本地英文名，保留 ProductName，避免 POS 显示名被清空。
                            localProduct.EnglishName = null;
                            localProduct.UpdatedAt = now;
                            localProduct.UpdatedBy = auditActorName;
                            localProductChanged = true;
                        }
                    }
                    else if (
                        productUpdate.HasEnglishNameIntent
                        && !string.IsNullOrWhiteSpace(productUpdate.英文名称)
                    )
                    {
                        var normalizedEnglishName = NormalizeEnglishNameForWrite(
                            productUpdate.英文名称
                        );
                        if (
                            normalizedEnglishName != null
                        )
                        {
                            if (product != null && product.EnglishProductName != normalizedEnglishName)
                            {
                                product.EnglishProductName = normalizedEnglishName;
                                product.UpdatedAt = now;
                                product.UpdatedBy = auditActorName;
                                productChanged = true;
                            }
                            if (localProduct != null)
                            {
                                // 货柜英文名是前台展示名来源；已有 POS 商品需同步显示名和英文名。
                                if (localProduct.ProductName != normalizedEnglishName)
                                {
                                    localProduct.ProductName = normalizedEnglishName;
                                    localProduct.UpdatedAt = now;
                                    localProduct.UpdatedBy = auditActorName;
                                    localProductChanged = true;
                                }
                                if (localProduct.EnglishName != normalizedEnglishName)
                                {
                                    localProduct.EnglishName = normalizedEnglishName;
                                    localProduct.UpdatedAt = now;
                                    localProduct.UpdatedBy = auditActorName;
                                    localProductChanged = true;
                                }
                            }
                        }
                        else if (normalizedEnglishName == null)
                        {
                            // 英文名称含中文时跳过，不能污染英文名称字段。
                            _logger.LogWarning(
                                "跳过仍包含中文的货柜明细英文名称写回: ProductCode={ProductCode}, EnglishName={EnglishName}",
                                productUpdate.ProductCode,
                                productUpdate.英文名称
                            );
                        }
                    }

                    if (product != null && productChanged)
                    {
                        if (changedProductCodes.Add(productUpdate.ProductCode))
                        {
                            changedProducts.Add(product);
                        }
                    }
                    if (localProduct != null && localProductChanged)
                    {
                        changedLocalNameProducts[productUpdate.ProductCode] = localProduct;
                    }
                }

                foreach (var item in relatedSyncDetailUpdates)
                {
                    var hasNameUpdate =
                        !string.IsNullOrWhiteSpace(item.Update.商品名称)
                        || acceptedEnglishNameByUpdate.ContainsKey(item.Update);
                    if (hasNameUpdate && changedProductCodes.Contains(item.ProductCode!))
                    {
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                    if (hasNameUpdate && changedLocalNameProducts.ContainsKey(item.ProductCode!))
                    {
                        updatedRequestGuids.Add(item.Update.HGUID);
                    }
                }

                // 多表写入必须处于调用方已取锁的同一事务中。
                if (
                    changedDetails.Count > 0
                    || changedProducts.Count > 0
                    || changedLocalNameProducts.Count > 0
                    || changedMiddlePackWarehouseProducts.Count > 0
                    || changedLocalCategoryProducts.Count > 0
                    || existingProductUpdates.Count > 0
                    || (
                        repairMissingStoreRelations
                        && existingDetailUpdates.Any(update => update.进口价格.HasValue)
                    )
                )
                {
                    if (_context.Db.Ado.Transaction == null || mutationLock == null)
                    {
                        throw new InvalidOperationException(
                            "货柜明细写入必须由持锁调用方在事务内执行"
                        );
                    }

                    // 缩小批量写阶段的局部变量作用域，事务仍由外层持锁调用方管理。
                    {
                        var rejectedImportPriceHguids = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase
                        );
                        var repairPurchasePrices = new Dictionary<string, decimal>(
                            StringComparer.OrdinalIgnoreCase
                        );

                        void RejectImportPriceForProduct(
                            string productCode,
                            string code,
                            string message
                        )
                        {
                            foreach (var item in existingProductUpdates.Where(item =>
                                item.Update.进口价格.HasValue
                                && string.Equals(
                                    item.Detail.ProductCode?.Trim(),
                                    productCode,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            ))
                            {
                                if (!rejectedImportPriceHguids.Add(item.Update.HGUID))
                                {
                                    continue;
                                }
                                result.ValidationErrors.Add(
                                    new ContainerDetailBatchUpdateValidationErrorDto
                                    {
                                        HGUID = item.Update.HGUID,
                                        Field = "进口价格",
                                        Code = code,
                                        Message = message,
                                    }
                                );
                            }
                        }

                        if (repairMissingStoreRelations)
                        {
                            foreach (var group in existingProductUpdates
                                .Where(item =>
                                    item.Update.进口价格.HasValue
                                    && !string.IsNullOrWhiteSpace(item.Detail.ProductCode)
                                )
                                .GroupBy(
                                    item => item.Detail.ProductCode!.Trim(),
                                    StringComparer.OrdinalIgnoreCase
                                ))
                            {
                                var distinctPrices = group
                                    .Select(item => item.Update.进口价格!.Value)
                                    .Distinct()
                                    .ToList();
                                if (distinctPrices.Count == 1)
                                {
                                    repairPurchasePrices[group.Key] = distinctPrices[0];
                                    continue;
                                }

                                RejectImportPriceForProduct(
                                    group.Key,
                                    "CONFLICTING_PRODUCT_IMPORT_PRICE",
                                    "同一商品的进口价格更新意图冲突"
                                );
                            }
                        }

                        SetChildPurchasePriceLockScope? setChildPurchasePriceLock = null;
                        SetChildPurchasePriceLockScope? detailedImportLock = null;
                        if (repairMissingStoreRelations)
                        {
                            if (repairPurchasePrices.Count > 0)
                            {
                                var partialLock = await SetChildPurchasePriceMutationLock
                                    .AcquireProductsPartiallyAsync(
                                        _context.Db,
                                        repairPurchasePrices.Keys
                                    );
                                detailedImportLock = partialLock.LockScope;
                                foreach (var productCode in partialLock.BusyProductCodes)
                                {
                                    RejectImportPriceForProduct(
                                        productCode,
                                        SetChildPurchasePriceMutationLock.BusyErrorCode,
                                        "套装子项成本正在被其他操作更新，请稍后重试"
                                    );
                                    repairPurchasePrices.Remove(productCode);
                                }
                            }
                        }
                        else if (productCodes.Count > 0)
                        {
                            setChildPurchasePriceLock = await SetChildPurchasePriceMutationLock
                                .AcquireProductsAsync(_context.Db, productCodes);
                        }

                        // 快照必须在同一事务内、任何业务写入前读取；需成本同步的商品已先取得业务锁。
                        var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(productCodes);

                        if (repairMissingStoreRelations)
                        {
                            if (repairPurchasePrices.Count > 0)
                            {
                                if (detailedImportLock == null)
                                {
                                    throw new InvalidOperationException(
                                        "详细保存缺少套装子项成本批量业务锁"
                                    );
                                }
                                var repair = await new SetChildPurchasePriceService(
                                    _context.Db
                                ).RepairMissingStoreRelationsLockedAsync(
                                    detailedImportLock,
                                    repairPurchasePrices,
                                    string.IsNullOrWhiteSpace(auditActorName)
                                        ? "System"
                                        : auditActorName
                                );
                                result.AutoRepairedStoreGroupCount +=
                                    repair.AutoRepairedStoreGroupCount;
                                result.AutoRepairedRelationCount += repair.AutoRepairedRelationCount;

                                foreach (var failure in repair.Failures.Values)
                                {
                                    RejectImportPriceForProduct(
                                        failure.ProductCode,
                                        failure.Code,
                                        failure.Message
                                    );
                                }
                            }

                            // 进口价在结构检查后才写入实体，失败商品不会被后续宽列更新意外带入数据库。
                            foreach (var update in existingDetailUpdates.Where(update =>
                                update.进口价格.HasValue
                                && !rejectedImportPriceHguids.Contains(update.HGUID)
                            ))
                            {
                                var detail = detailMap[update.HGUID];
                                if (detail.ImportPrice != update.进口价格!.Value)
                                {
                                    detail.ImportPrice = update.进口价格.Value;
                                    importPriceWrites.Add(update.HGUID);
                                    if (!changedDetails.Any(row => row.DetailCode == detail.DetailCode))
                                    {
                                        changedDetails.Add(detail);
                                    }
                                }
                                if (countValidNoOps)
                                {
                                    updatedRequestGuids.Add(update.HGUID);
                                }
                            }
                        }

                        bool HasAcceptedImportPrice(UpdateContainerDetailDto update) =>
                            update.进口价格.HasValue
                            && !rejectedImportPriceHguids.Contains(update.HGUID);

                        // 第二步：更新货柜明细表
                        if (changedDetails.Count > 0)
                        {
                            var allChangedDetailRows = changedDetails
                                .GroupBy(row => row.DetailCode)
                                .Select(group => group.Last())
                                .ToList();
                            // 最坏情况每行会占 27 个参数（13 个字段的键和值加明细键）；75 行为
                            // 2,025 个参数，预留货柜范围参数后仍低于 SQL Server 的 2,100 上限。
                            foreach (var changedDetailRows in allChangedDetailRows.Chunk(75))
                            {
                                var sqlParameters = new List<SugarParameter>();
                                var setClauses = new List<string>();

                            // 每个业务列仅为本次已接受字段生成 CASE 分支：单条参数化 SQL 避免 N+1，
                            // 同时绝不将事务前读到的其它列值宽写回，保护并发编辑的不同字段。
                            void AddDetailColumnCase(
                                string columnName,
                                string parameterPrefix,
                                HashSet<string> writeCodes,
                                Func<ContainerDetail, object?> valueSelector
                            )
                            {
                                var rows = changedDetailRows
                                    .Where(row => writeCodes.Contains(row.DetailCode))
                                    .ToList();
                                if (rows.Count == 0)
                                {
                                    return;
                                }

                                var cases = new List<string>();
                                for (var index = 0; index < rows.Count; index++)
                                {
                                    var keyParameter = $"@{parameterPrefix}Key{index}";
                                    var valueParameter = $"@{parameterPrefix}Value{index}";
                                    sqlParameters.Add(
                                        new SugarParameter(keyParameter, rows[index].DetailCode)
                                    );
                                    sqlParameters.Add(
                                        new SugarParameter(valueParameter, valueSelector(rows[index]))
                                    );
                                    cases.Add($"WHEN DetailCode = {keyParameter} THEN {valueParameter}");
                                }
                                setClauses.Add(
                                    $"{columnName} = CASE {string.Join(" ", cases)} ELSE {columnName} END"
                                );
                            }

                            AddDetailColumnCase(
                                "AdjustmentRate",
                                "AdjustmentRate",
                                adjustmentRateWrites,
                                row => row.AdjustmentRate
                            );
                            AddDetailColumnCase(
                                "DomesticPrice",
                                "DomesticPrice",
                                domesticPriceWrites,
                                row => row.DomesticPrice
                            );
                            AddDetailColumnCase(
                                "ImportPrice",
                                "ImportPrice",
                                importPriceWrites,
                                row => row.ImportPrice
                            );
                            AddDetailColumnCase(
                                "TransportCost",
                                "TransportCost",
                                transportCostWrites,
                                row => row.TransportCost
                            );
                            AddDetailColumnCase(
                                "OEMPrice",
                                "OEMPrice",
                                oemPriceWrites,
                                row => row.OEMPrice
                            );
                            AddDetailColumnCase(
                                "PackingQuantity",
                                "PackingQuantity",
                                packingQuantityWrites,
                                row => row.PackingQuantity
                            );
                            AddDetailColumnCase(
                                "UnitVolume",
                                "UnitVolume",
                                unitVolumeWrites,
                                row => row.UnitVolume
                            );
                            AddDetailColumnCase(
                                "LoadingQuantity",
                                "LoadingQuantity",
                                loadingQuantityWrites,
                                row => row.LoadingQuantity
                            );
                            AddDetailColumnCase(
                                "TotalVolume",
                                "TotalVolume",
                                totalVolumeWrites,
                                row => row.TotalVolume
                            );
                            AddDetailColumnCase(
                                "TotalAmount",
                                "TotalAmount",
                                totalAmountWrites,
                                row => row.TotalAmount
                            );
                            AddDetailColumnCase(
                                "IsActive",
                                "IsActive",
                                activeWrites,
                                row => row.IsActive
                            );
                            AddDetailColumnCase(
                                "TargetWarehouseCategoryGUID",
                                "TargetWarehouseCategoryGUID",
                                categoryWrites,
                                row => row.TargetWarehouseCategoryGUID
                            );
                            AddDetailColumnCase(
                                "Remarks",
                                "Remarks",
                                remarkWrites,
                                row => row.Remarks
                            );

                            if (setClauses.Count > 0)
                            {
                                var detailCodeParameters = new List<string>();
                                for (var index = 0; index < changedDetailRows.Length; index++)
                                {
                                    var detailParameter = $"@DetailCode{index}";
                                    detailCodeParameters.Add(detailParameter);
                                    sqlParameters.Add(
                                        new SugarParameter(
                                            detailParameter,
                                            changedDetailRows[index].DetailCode
                                        )
                                    );
                                }
                                var scopeClause = string.Empty;
                                if (!string.IsNullOrWhiteSpace(containerGuid))
                                {
                                    scopeClause = " AND ContainerCode = @ContainerGuid";
                                    sqlParameters.Add(
                                        new SugarParameter("@ContainerGuid", containerGuid)
                                    );
                                }
                                var sql =
                                    $"UPDATE ContainerDetail SET {string.Join(", ", setClauses)} "
                                    + $"WHERE DetailCode IN ({string.Join(", ", detailCodeParameters)}) "
                                    + "AND (IsDeleted = 0 OR IsDeleted IS NULL)"
                                    + scopeClause;
                                await _context.Db.Ado.ExecuteCommandAsync(sql, sqlParameters);
                            }
                            }

                            // 明细合计变化后，同事务刷新货柜主表汇总，保证列表和详情头部一致。
                            await RefreshContainerSummariesAsync(
                                mutationLock,
                                changedDetails.Select(detail => detail.ContainerCode)
                            );
                        }

                        // 第三步：同步更新国内商品表的名称和中包数。
                        if (changedProducts.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedProducts)
                                .UpdateColumns(x => new
                                {
                                    x.ProductName,
                                    x.EnglishProductName,
                                    x.MiddlePackQuantity,
                                    x.UpdatedAt,
                                    x.UpdatedBy,
                                })
                                .WhereColumns(x => new { x.ProductCode })
                                .ExecuteCommandAsync();
                        }

                        // 英文名称保存同时同步已存在的 POS 商品主档，供前台和仓库商品页直接展示。
                        if (changedLocalNameProducts.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedLocalNameProducts.Values.ToList())
                                .UpdateColumns(x => new
                                {
                                    x.ProductName,
                                    x.EnglishName,
                                    x.UpdatedAt,
                                    x.UpdatedBy,
                                })
                                .WhereColumns(x => new { x.ProductCode })
                                .ExecuteCommandAsync();
                        }

                        // 第四步：已匹配本地商品时，同步中包数到仓库商品表；未匹配商品不创建仓库商品。
                        if (changedMiddlePackWarehouseProducts.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedMiddlePackWarehouseProducts.Values.ToList())
                                .UpdateColumns(x => new { x.MinOrderQuantity, x.UpdatedAt, x.UpdatedBy })
                                .WhereColumns(x => new { x.ProductCode })
                                .ExecuteCommandAsync();
                        }

                        // 已匹配本地商品的目标分类需要同步到 Product.WarehouseCategoryGUID，供仓库分类页和前台分类筛选使用。
                        if (changedLocalCategoryProducts.Count > 0)
                        {
                            await _context
                                .Db.Updateable(changedLocalCategoryProducts.Values.ToList())
                                .UpdateColumns(x => new { x.WarehouseCategoryGUID, x.UpdatedAt, x.UpdatedBy })
                                .WhereColumns(x => new { x.ProductCode })
                                .ExecuteCommandAsync();
                        }

                        // 名称/英文名/分类可能只写 Product 或 DomesticProduct；同时刷新对应仓库商品的审计列，
                        // 让列表更新时间/更新人与本次历史事件保持一致。窄列更新不改变仓库商品业务字段。
                        var warehouseProductAuditCodes = changedProducts
                            .Select(product => product.ProductCode)
                            .Concat(changedLocalNameProducts.Keys)
                            .Concat(changedLocalCategoryProducts.Keys)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Where(code => !changedMiddlePackWarehouseProducts.ContainsKey(code))
                            .ToList();
                        if (warehouseProductAuditCodes.Count > 0)
                        {
                            await _context.Db.Updateable<WarehouseProduct>()
                                .SetColumns(product => new WarehouseProduct
                                {
                                    UpdatedAt = now,
                                    UpdatedBy = auditActorName,
                                })
                                .Where(product =>
                                    product.ProductCode != null
                                    && warehouseProductAuditCodes.Contains(product.ProductCode)
                                    && !product.IsDeleted
                                )
                                .ExecuteCommandAsync();
                        }

                        if (existingProductUpdates.Count > 0)
                        {
                            // 先按商品编码合并已接受意图，避免同商品的多条货柜明细重复占用 SQL 参数。
                            // OEM/上下架/进口价的冲突已在前面逐字段拒绝，此处每个商品最多保留一个确定值。
                            var productSyncPlans = existingProductUpdates
                                .GroupBy(
                                    item => item.Detail.ProductCode!.Trim(),
                                    StringComparer.OrdinalIgnoreCase
                                )
                                .Select(group =>
                                {
                                    var importItem = group.LastOrDefault(item =>
                                        HasAcceptedImportPrice(item.Update)
                                    );
                                    var oemItem = group.LastOrDefault(item =>
                                        HasAcceptedOemPrice(item.Update)
                                    );
                                    var activeItem = group.LastOrDefault(item =>
                                        HasAcceptedActive(item.Update)
                                    );
                                    return new ContainerProductSyncPlan(
                                        group.Key,
                                        importItem?.Update.进口价格,
                                        oemItem?.Update.贴牌价格,
                                        activeItem?.Update.IsActive
                                    );
                                })
                                .Where(plan =>
                                    plan.ImportPrice.HasValue
                                    || plan.OEMPrice.HasValue
                                    || plan.IsActive.HasValue
                                )
                                .ToList();

                            await UpdateWarehouseProductsForContainerDetailAsync(
                                productSyncPlans,
                                now,
                                auditActorName
                            );
                            await UpdateProductsForContainerDetailAsync(
                                productSyncPlans,
                                now,
                                auditActorName
                            );
                            await UpdateStoreRetailPricesForContainerDetailAsync(productSyncPlans);

                            var importPriceUpdates = productSyncPlans
                                .Where(plan => plan.ImportPrice.HasValue)
                                .ToList();
                            if (importPriceUpdates.Count > 0)
                            {
                                var costProductCodes = importPriceUpdates
                                    .Select(item => item.ProductCode)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();
                                var purchasePriceService = new SetChildPurchasePriceService(
                                    _context.Db
                                );
                                var recalculationActor = string.IsNullOrWhiteSpace(auditActorName)
                                    ? "System"
                                    : auditActorName;
                                if (repairMissingStoreRelations)
                                {
                                    if (detailedImportLock == null)
                                    {
                                        throw new InvalidOperationException(
                                            "详细保存缺少套装子项成本批量业务锁"
                                        );
                                    }
                                    await purchasePriceService.RecalculateLockedAsync(
                                        detailedImportLock,
                                        costProductCodes,
                                        storeCodes: null,
                                        updatedBy: recalculationActor
                                    );
                                }
                                else if (setChildPurchasePriceLock != null)
                                {
                                    await purchasePriceService.RecalculateLockedAsync(
                                        setChildPurchasePriceLock,
                                        costProductCodes,
                                        storeCodes: null,
                                        updatedBy: recalculationActor
                                    );
                                }
                            }

                            _logger.LogInformation(
                                "[React] 同步已有商品价格到仓库表、Product 和分店进货价，数量: {Count}",
                                existingProductUpdates.Count
                            );
                        }

                        var afterSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(productCodes);
                        await _changeHistoryService.RecordChangesAsync(
                            beforeSnapshots,
                            afterSnapshots,
                            new WarehouseProductChangeHistoryContextDto
                            {
                                Action = "BatchUpdate",
                                Source = "ContainerDetail",
                                SourceReference = string.Join(",", validDetailUpdates
                                    .Select(item => item.Detail.ContainerCode)
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                                BatchGuid = auditBatchGuid,
                                ActorUserGuid = auditActorGuid,
                                ActorName = auditActorName,
                            }
                        );
                    }
                }

                var totalUpdated = updatedRequestGuids.Count;
                result.TotalUpdated = totalUpdated;

                _logger.LogInformation(
                    "[React] 批量更新货柜明细完成，成功更新: {TotalUpdated}/{Total}",
                    totalUpdated,
                    result.TotalRequested
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 批量更新货柜明细失败");
                throw;
            }
        }

        private const int ContainerProductCaseBatchSize = 400;

        /// <summary>
        /// 将一商品一意图的同步计划以参数化 CASE 分块写入 WarehouseProduct。
        /// 400 行最坏为 1,602 参数（编码、三字段值、审计），低于 SQL Server 2,100 上限。
        /// </summary>
        private async Task UpdateWarehouseProductsForContainerDetailAsync(
            IReadOnlyCollection<ContainerProductSyncPlan> plans,
            DateTime now,
            string? updatedBy
        )
        {
            foreach (var batch in plans.Chunk(ContainerProductCaseBatchSize))
            {
                var parameters = new List<SugarParameter>();
                var importCases = new List<string>();
                var oemCases = new List<string>();
                var activeCases = new List<string>();
                var productCodeParameters = new List<string>();
                for (var index = 0; index < batch.Length; index++)
                {
                    var plan = batch[index];
                    var productParameter = $"@ProductCode{index}";
                    productCodeParameters.Add(productParameter);
                    parameters.Add(new SugarParameter(productParameter, plan.ProductCode));
                    if (plan.ImportPrice.HasValue)
                    {
                        var valueParameter = $"@ImportPrice{index}";
                        parameters.Add(new SugarParameter(valueParameter, plan.ImportPrice.Value));
                        importCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                    }
                    if (plan.OEMPrice.HasValue)
                    {
                        var valueParameter = $"@OEMPrice{index}";
                        parameters.Add(new SugarParameter(valueParameter, plan.OEMPrice.Value));
                        oemCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                    }
                    if (plan.IsActive.HasValue)
                    {
                        var valueParameter = $"@IsActive{index}";
                        parameters.Add(
                            new SugarParameter(valueParameter, plan.IsActive.Value ? 1 : 0)
                        );
                        activeCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                    }
                }
                parameters.Add(new SugarParameter("@UpdatedAt", now));
                parameters.Add(new SugarParameter("@UpdatedBy", updatedBy));
                var setClauses = new List<string>
                {
                    "UpdatedAt = @UpdatedAt",
                    "UpdatedBy = @UpdatedBy",
                };
                if (importCases.Count > 0)
                    setClauses.Add(
                        $"ImportPrice = CASE {string.Join(" ", importCases)} ELSE ImportPrice END"
                    );
                if (oemCases.Count > 0)
                    setClauses.Add($"OEMPrice = CASE {string.Join(" ", oemCases)} ELSE OEMPrice END");
                if (activeCases.Count > 0)
                    setClauses.Add($"IsActive = CASE {string.Join(" ", activeCases)} ELSE IsActive END");
                var sql =
                    $"UPDATE WarehouseProduct SET {string.Join(", ", setClauses)} "
                    + $"WHERE ProductCode IN ({string.Join(", ", productCodeParameters)}) "
                    + "AND (IsDeleted = 0 OR IsDeleted IS NULL)";
                await _context.Db.Ado.ExecuteCommandAsync(sql, parameters);
            }
        }

        /// <summary>
        /// 仅同步货柜明细允许回写的本地主档价格，按商品编码分块避免参数超限。
        /// </summary>
        private async Task UpdateProductsForContainerDetailAsync(
            IReadOnlyCollection<ContainerProductSyncPlan> plans,
            DateTime now,
            string? updatedBy
        )
        {
            foreach (var batch in plans
                .Where(plan => plan.ImportPrice.HasValue || plan.OEMPrice.HasValue)
                .Chunk(ContainerProductCaseBatchSize))
            {
                var parameters = new List<SugarParameter>();
                var purchaseCases = new List<string>();
                var retailCases = new List<string>();
                var productCodeParameters = new List<string>();
                for (var index = 0; index < batch.Length; index++)
                {
                    var plan = batch[index];
                    var productParameter = $"@ProductCode{index}";
                    productCodeParameters.Add(productParameter);
                    parameters.Add(new SugarParameter(productParameter, plan.ProductCode));
                    if (plan.ImportPrice.HasValue)
                    {
                        var valueParameter = $"@PurchasePrice{index}";
                        parameters.Add(new SugarParameter(valueParameter, plan.ImportPrice.Value));
                        purchaseCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                    }
                    if (plan.OEMPrice.HasValue)
                    {
                        var valueParameter = $"@RetailPrice{index}";
                        parameters.Add(new SugarParameter(valueParameter, plan.OEMPrice.Value));
                        retailCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                    }
                }
                parameters.Add(new SugarParameter("@UpdatedAt", now));
                parameters.Add(new SugarParameter("@UpdatedBy", updatedBy));
                var setClauses = new List<string>
                {
                    "UpdatedAt = @UpdatedAt",
                    "UpdatedBy = @UpdatedBy",
                };
                if (purchaseCases.Count > 0)
                    setClauses.Add(
                        $"PurchasePrice = CASE {string.Join(" ", purchaseCases)} ELSE PurchasePrice END"
                    );
                if (retailCases.Count > 0)
                    setClauses.Add(
                        $"RetailPrice = CASE {string.Join(" ", retailCases)} ELSE RetailPrice END"
                    );
                var sql =
                    $"UPDATE Product SET {string.Join(", ", setClauses)} "
                    + $"WHERE ProductCode IN ({string.Join(", ", productCodeParameters)}) "
                    + "AND (IsDeleted = 0 OR IsDeleted IS NULL)";
                await _context.Db.Ado.ExecuteCommandAsync(sql, parameters);
            }
        }

        /// <summary>
        /// 分店零售价仅接收进口价对应的进货价；每批 400 个商品保持参数预算安全。
        /// </summary>
        private async Task UpdateStoreRetailPricesForContainerDetailAsync(
            IReadOnlyCollection<ContainerProductSyncPlan> plans
        )
        {
            foreach (var batch in plans
                .Where(plan => plan.ImportPrice.HasValue)
                .Chunk(ContainerProductCaseBatchSize))
            {
                var parameters = new List<SugarParameter>();
                var purchaseCases = new List<string>();
                var productCodeParameters = new List<string>();
                for (var index = 0; index < batch.Length; index++)
                {
                    var plan = batch[index];
                    var productParameter = $"@ProductCode{index}";
                    var valueParameter = $"@PurchasePrice{index}";
                    productCodeParameters.Add(productParameter);
                    parameters.Add(new SugarParameter(productParameter, plan.ProductCode));
                    parameters.Add(new SugarParameter(valueParameter, plan.ImportPrice!.Value));
                    purchaseCases.Add($"WHEN ProductCode = {productParameter} THEN {valueParameter}");
                }
                var sql =
                    $"UPDATE StoreRetailPrice SET PurchasePrice = CASE {string.Join(" ", purchaseCases)} ELSE PurchasePrice END "
                    + $"WHERE ProductCode IN ({string.Join(", ", productCodeParameters)}) "
                    + "AND (IsDeleted = 0 OR IsDeleted IS NULL)";
                await _context.Db.Ado.ExecuteCommandAsync(sql, parameters);
            }
        }

        private sealed record ContainerProductSyncPlan(
            string ProductCode,
            decimal? ImportPrice,
            decimal? OEMPrice,
            bool? IsActive
        );

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
                // 选中项也必须回库按当前货柜收敛，避免请求体夹带其他货柜明细。
                return await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(detail =>
                        detail.ContainerCode == containerGuid
                        && selectedHguids.Contains(detail.DetailCode)
                    )
                    .Select(detail => detail.DetailCode)
                    .ToListAsync();
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

        private async Task<int> ExecuteScopedBatchUpdateUnderContainerLockAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request,
            Func<Container?, List<ContainerDetail>, List<UpdateContainerDetailDto>> buildUpdates,
            string operation
        )
        {
            var deadlockRetryCount = 0;
            while (true)
            {
                await _context.Db.Ado.BeginTranAsync();
                try
                {
                    var mutationLock = await ContainerMutationLock.AcquireContainersAsync(
                        _context.Db,
                        new[] { containerGuid }
                    );

                    // 作用范围、货柜成本字段和明细计算输入必须在同一把货柜锁内重新解析。
                    var container = await _context
                        .Db.Queryable<Container>()
                        .Where(item => item.ContainerCode == containerGuid && !item.IsDeleted)
                        .FirstAsync();
                    var hguids = await ResolveContainerDetailBatchScopeHguidsAsync(
                        containerGuid,
                        request
                    );
                    var details = hguids.Count == 0
                        ? new List<ContainerDetail>()
                        : await _context
                            .Db.Queryable<ContainerDetail>()
                            .Where(detail =>
                                hguids.Contains(detail.DetailCode)
                                && detail.ContainerCode == containerGuid
                                && !detail.IsDeleted
                            )
                            .ToListAsync();
                    mutationLock.EnsureCovers(
                        _context.Db,
                        details.Select(detail => detail.ContainerCode)
                    );

                    var updates = buildUpdates(container, details);
                    var updateResult = await BatchUpdateDetailsAttemptAsync(
                        updates,
                        countValidNoOps: false,
                        repairMissingStoreRelations: false,
                        containerGuid: containerGuid,
                        mutationLock: mutationLock
                    );

                    await _context.Db.Ado.CommitTranAsync();
                    return updateResult.TotalUpdated;
                }
                catch (Exception exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    if (
                        !ContainerMutationLock.ShouldRetryDeadlock(
                            exception,
                            deadlockRetryCount
                        )
                    )
                    {
                        _logger.LogError(exception, "[React] {Operation}失败", operation);
                        throw;
                    }

                    deadlockRetryCount++;
                    var delayMilliseconds = Random.Shared.Next(100, 301);
                    _logger.LogWarning(
                        exception,
                        "[React] {Operation}遇到 SQL Server 1205，{DelayMilliseconds}ms 后完整重试一次",
                        operation,
                        delayMilliseconds
                    );
                    await Task.Delay(delayMilliseconds);
                }
            }
        }

        private static void EnsureContainerCostInputs(Container container)
        {
            if (!container.ExchangeRate.HasValue || container.ExchangeRate.Value <= 0)
            {
                throw new InvalidOperationException("缺少汇率，无法重算成本");
            }
            if (!container.ShippingFee.HasValue)
            {
                throw new InvalidOperationException("缺少运费，无法重算成本");
            }
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

            return await ExecuteScopedBatchUpdateUnderContainerLockAsync(
                containerGuid,
                request,
                (container, details) =>
                {
                    if (container == null)
                    {
                        return new List<UpdateContainerDetailDto>();
                    }

                    // 批量调浮率会同步重算运输成本和进口价格，必须使用锁后主表成本字段。
                    EnsureContainerCostInputs(container);
                    return details
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
                                // 系统按浮率重算的进货价只落货柜明细，不覆盖人工确认后的仓库进货价。
                                SkipRelatedProductSync = true,
                            };
                        })
                        .ToList();
                },
                "按筛选范围批量调浮率"
            );
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

            return await ExecuteScopedBatchUpdateUnderContainerLockAsync(
                containerGuid,
                request,
                (_, details) =>
                {
                    return details
                        .Select(detail =>
                        {
                            var update = new UpdateContainerDetailDto
                            {
                                HGUID = detail.DetailCode,
                            };
                            // 批量改价只同步用户实际提交的字段，避免旧零售价被当成本次改价写回主档。
                            if (request.ImportPrice.HasValue)
                            {
                                update.进口价格 = request.ImportPrice.Value;
                            }
                            if (request.OemPrice.HasValue)
                            {
                                update.贴牌价格 = request.OemPrice.Value;
                            }
                            return update;
                        })
                        .ToList();
                },
                "按筛选范围批量改价"
            );
        }

        public async Task<int> RecalculateCostsByScopeAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request
        )
        {
            const decimal minimumRecalculateFloatRate = 1.30m;

            return await ExecuteScopedBatchUpdateUnderContainerLockAsync(
                containerGuid,
                request,
                (container, details) =>
                {
                    if (container == null)
                    {
                        return new List<UpdateContainerDetailDto>();
                    }

                    // 重算成本由服务端兜底执行，且只使用持锁后重读的主表和明细输入。
                    EnsureContainerCostInputs(container);
                    return details
                        .Select(detail =>
                        {
                            // 重算成本时不能再把空浮率按 1 处理；统一托底到 1.30 并写回明细。
                            var floatRate =
                                detail.AdjustmentRate.HasValue
                                && detail.AdjustmentRate.Value >= minimumRecalculateFloatRate
                                    ? detail.AdjustmentRate.Value
                                    : minimumRecalculateFloatRate;
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
                                // 运费/汇率触发的成本重算只更新货柜明细，仓库价格等到人工保存明细再同步。
                                SkipRelatedProductSync = true,
                            };
                        })
                        .ToList();
                },
                "按筛选范围重算成本"
            );
        }

        public async Task<int> BackfillLastPricesByScopeAsync(
            string containerGuid,
            ContainerDetailBatchScopeDto request
        )
        {
            await _context.Db.Ado.BeginTranAsync();
            try
            {
                var mutationLock = await ContainerMutationLock.AcquireContainersAsync(
                    _context.Db,
                    new[] { containerGuid }
                );
                var hguids = await ResolveContainerDetailBatchScopeHguidsAsync(
                    containerGuid,
                    request
                );
                if (hguids.Count == 0)
                {
                    await _context.Db.Ado.CommitTranAsync();
                    return 0;
                }

                // 选中明细必须在持锁后重读并再次收敛到路由货柜，避免旧筛选结果跨柜写入。
                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(detail =>
                        hguids.Contains(detail.DetailCode)
                        && detail.ContainerCode == containerGuid
                        && !detail.IsDeleted
                    )
                    .ToListAsync();
                mutationLock.EnsureCovers(_context.Db, details.Select(detail => detail.ContainerCode));

                var productCodes = details
                    .Select(detail => detail.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Distinct()
                    .ToList();
                if (productCodes.Count == 0)
                {
                    await _context.Db.Ado.CommitTranAsync();
                    return 0;
                }

                var warehouseProducts = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(product => productCodes.Contains(product.ProductCode))
                    .ToListAsync();
                var warehouseMap = warehouseProducts
                    .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                    .ToDictionary(product => product.ProductCode!);

                var changedDetails = new List<ContainerDetail>();
                foreach (var detail in details)
                {
                    if (
                        string.IsNullOrWhiteSpace(detail.ProductCode)
                        || !warehouseMap.TryGetValue(detail.ProductCode, out var warehouseProduct)
                    )
                    {
                        continue;
                    }

                    var changed = false;
                    if (!detail.LastImportPrice.HasValue && warehouseProduct.ImportPrice.HasValue)
                    {
                        // 回填只补空快照，绝不覆盖历史货柜已经保存的上次价格。
                        detail.LastImportPrice = warehouseProduct.ImportPrice;
                        changed = true;
                    }
                    if (!detail.LastOEMPrice.HasValue && warehouseProduct.OEMPrice.HasValue)
                    {
                        detail.LastOEMPrice = warehouseProduct.OEMPrice;
                        changed = true;
                    }

                    if (changed)
                    {
                        changedDetails.Add(detail);
                    }
                }

                if (changedDetails.Count > 0)
                {
                    await _context
                        .Db.Updateable(changedDetails)
                        .UpdateColumns(detail => new
                        {
                            detail.LastImportPrice,
                            detail.LastOEMPrice,
                        })
                        .WhereColumns(detail => new { detail.DetailCode })
                        .ExecuteCommandAsync();
                }

                await _context.Db.Ado.CommitTranAsync();
                return changedDetails.Count;
            }
            catch (Exception exception)
            {
                await RollbackContainerMutationTransactionSafelyAsync(exception);
                throw;
            }
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

                await _context.Db.Ado.BeginTranAsync();
                try
                {
                    // 创建前的跨柜去重检查和插入必须处于同一独占总闸事务内。
                    await ContainerMutationLock.AcquireAllAsync(_context.Db);

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
                        throw new InvalidOperationException(
                            $"货柜编号 {containerNumber} 在装柜日期 {loadingDateText} 已存在"
                        );
                    }

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
                        Status = 0,
                        TotalPieces = 0,
                        TotalQuantity = 0,
                        TotalAmount = 0,
                        TotalVolume = 0,
                        CostFloatRate = null,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsDeleted = false,
                    };

                    var result = await _context
                        .Db.Insertable(container)
                        .ExecuteReturnEntityAsync();
                    await _context.Db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "[React] 创建货柜成功: {ContainerCode}, 货柜编号: {ContainerNumber}",
                        result.ContainerCode,
                        result.ContainerNumber
                    );
                    return result.ContainerCode;
                }
                catch (Exception exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    throw;
                }
            }
            catch (Exception ex) when (ContainerMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
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
                var candidateContainer = await FindContainerByIdAsync(containerId);
                if (candidateContainer == null)
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

                await _context.Db.Ado.BeginTranAsync();
                try
                {
                    var mutationLock = await ContainerMutationLock.AcquireContainersAsync(
                        _context.Db,
                        new[] { candidateContainer.ContainerCode }
                    );
                    var container = await FindContainerByIdAsync(containerId);
                    if (container == null)
                    {
                        throw new ContainerMutationLockException(
                            "container-disappeared",
                            -1
                        );
                    }
                    mutationLock.EnsureCovers(
                        _context.Db,
                        new[] { container.ContainerCode }
                    );

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
                    var existingDetails = await _context
                        .Db.Queryable<ContainerDetail>()
                        .Where(x =>
                            x.ContainerCode == container.ContainerCode
                            && x.ProductCode != null
                            && productCodes.Contains(x.ProductCode)
                        )
                        .ToListAsync();
                    var detailDict = existingDetails
                        .Where(d => !string.IsNullOrWhiteSpace(d.ProductCode))
                        .ToDictionary(d => d.ProductCode!, d => d);

                var warehouseProductMap = new Dictionary<string, WarehouseProduct>();
                if (productCodes.Count > 0)
                {
                    var warehouseProducts = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(p => productCodes.Contains(p.ProductCode))
                        .ToListAsync();
                    warehouseProductMap = warehouseProducts
                        .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                        .ToDictionary(product => product.ProductCode!);
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
                            warehouseProductMap.TryGetValue(item.ProductCode, out var warehouseProduct);

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
                                // 上次价格仅在新建明细时快照；后续仓库商品调价不自动覆盖历史货柜。
                                LastImportPrice = warehouseProduct?.ImportPrice,
                                LastOEMPrice = warehouseProduct?.OEMPrice,
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
                            // 更新零售价（如提供）
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
                        when (!ContainerMutationLock.TryResolveConflict(exItem, out _))
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

                    // 明细写入与汇总必须在同一货柜事务内，汇总失败时整批回滚。
                    await RefreshContainerSummariesAsync(
                        mutationLock,
                        new[] { container.ContainerCode }
                    );
                    await _context.Db.Ado.CommitTranAsync();
                    return result;
                }
                catch (ContainerMutationScopeChangedException exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    throw new ContainerMutationLockException("scope-changed", -1, exception);
                }
                catch (Exception exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    throw;
                }
            }
            catch (Exception ex) when (ContainerMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
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

                // 锁前只解析候选货柜；删除目标和汇总数据必须在持锁事务内重新读取。
                var candidateContainerCodes = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .Select(d => d.ContainerCode)
                    .ToListAsync();

                if (!candidateContainerCodes.Any())
                {
                    _logger.LogWarning("[React] 未找到待删除的明细");
                    return 0;
                }

                await _context.Db.Ado.BeginTranAsync();
                try
                {
                    var mutationLock = await ContainerMutationLock.AcquireContainersAsync(
                        _context.Db,
                        candidateContainerCodes
                    );
                    var detailsToDelete = await _context
                        .Db.Queryable<ContainerDetail>()
                        .Where(d => hguids.Contains(d.DetailCode))
                        .Select(d => new { d.DetailCode, d.ContainerCode })
                        .ToListAsync();
                    if (!detailsToDelete.Any())
                    {
                        await _context.Db.Ado.CommitTranAsync();
                        return 0;
                    }

                    var containerCodes = detailsToDelete
                        .Select(detail => detail.ContainerCode)
                        .ToList();
                    mutationLock.EnsureCovers(_context.Db, containerCodes);

                    var deletedRows = await _context
                        .Db.Deleteable<ContainerDetail>()
                        .Where(d => hguids.Contains(d.DetailCode))
                        .ExecuteCommandAsync();

                    await RefreshContainerSummariesAsync(mutationLock, containerCodes);
                    await _context.Db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "[React] 批量删除货柜明细完成，成功删除: {Deleted}/{Total}",
                        deletedRows,
                        hguids.Count
                    );
                    return deletedRows;
                }
                catch (ContainerMutationScopeChangedException exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    throw new ContainerMutationLockException("scope-changed", -1, exception);
                }
                catch (Exception exception)
                {
                    await RollbackContainerMutationTransactionSafelyAsync(exception);
                    throw;
                }
            }
            catch (Exception ex) when (ContainerMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
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
