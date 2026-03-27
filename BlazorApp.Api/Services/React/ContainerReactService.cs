using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
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
        private readonly IMapper _mapper;
        private readonly ILogger<ContainerReactService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public ContainerReactService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<ContainerReactService> logger
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
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
                // 根据货柜编码查询，预加载明细
                var container = await _context
                    .Db.Queryable<Container>()
                    .Includes(x => x.Details)
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                return _mapper.Map<ContainerMainDto>(container);
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
        /// 更新货柜基本信息（实际到货日期、汇率、运费、备注）
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

                // 保存并限制更新列，避免覆盖其他字段
                var rowsAffected = await _context
                    .Db.Updateable(container)
                    .UpdateColumns(x => new
                    {
                        x.ActualArrivalDate,
                        x.ExchangeRate,
                        x.ShippingFee,
                        x.Remarks,
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
                    if (update.IsActive.HasValue && detail.IsActive != update.IsActive.Value)
                    {
                        detail.IsActive = update.IsActive.Value;
                        changed = true;
                    }
                    if (changed)
                    {
                        changedDetails.Add(detail);
                    }
                }

                var totalUpdated = 0;

                // 开启事务，确保多表更新的原子性
                if (changedDetails.Count > 0)
                {
                    await _context.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 第二步：更新货柜明细表
                        var rows = await _context
                            .Db.Updateable(changedDetails)
                            .UpdateColumns(x => new
                            {
                                x.AdjustmentRate,
                                x.ImportPrice,
                                x.TransportCost,
                                x.OEMPrice,
                                x.IsActive,
                            })
                            .WhereColumns(x => new { x.DetailCode })
                            .ExecuteCommandAsync();
                        totalUpdated = rows;

                        // 第三步：同步更新国内商品表的名称信息
                        // 提取需要更新商品的请求
                        var productUpdates = updates
                            .Select(u =>
                            {
                                if (detailMap.TryGetValue(u.HGUID, out var d))
                                {
                                    return new
                                    {
                                        d.ProductCode,
                                        u.商品名称,
                                        u.英文名称,
                                    };
                                }
                                return null;
                            })
                            .Where(x => x != null && !string.IsNullOrEmpty(x.ProductCode))
                            .ToList();

                        // 获取涉及的商品编码
                        var productCodes = productUpdates
                            .Select(x => x!.ProductCode!)
                            .Distinct()
                            .ToList();

                        // 预加载商品数据，用于判断是否为已有商品以及更新名称
                        var productMap = new Dictionary<string, DomesticProduct>();
                        if (productCodes.Count > 0)
                        {
                            var products = await _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(p => productCodes.Contains(p.ProductCode))
                                .ToListAsync();

                            // 构建商品编码到商品实体的映射
                            productMap = products.ToDictionary(p => p.ProductCode, p => p);
                            var changedProducts = new List<DomesticProduct>();

                            // 遍历更新商品名称
                            foreach (var u in productUpdates)
                            {
                                if (u == null)
                                    continue;
                                if (!productMap.TryGetValue(u.ProductCode!, out var p))
                                    continue;

                                var pChanged = false;
                                if (
                                    !string.IsNullOrEmpty(u.商品名称)
                                    && p.ProductName != u.商品名称
                                )
                                {
                                    p.ProductName = u.商品名称;
                                    pChanged = true;
                                }
                                if (
                                    !string.IsNullOrEmpty(u.英文名称)
                                    && p.EnglishProductName != u.英文名称
                                )
                                {
                                    p.EnglishProductName = u.英文名称;
                                    pChanged = true;
                                }
                                if (pChanged)
                                {
                                    changedProducts.Add(p);
                                }
                            }

                            // 批量更新国内商品表
                            if (changedProducts.Count > 0)
                            {
                                await _context
                                    .Db.Updateable(changedProducts)
                                    .UpdateColumns(x => new { x.ProductName, x.EnglishProductName })
                                    .WhereColumns(x => new { x.ProductCode })
                                    .ExecuteCommandAsync();
                            }
                        }

                        // 第四步：同步已有商品的价格和上下架状态到关联表
                        // 通过 productMap 判断商品是否为已有商品（商品表中已存在）
                        var existingProductUpdates = updates
                            .Where(u =>
                                detailMap.TryGetValue(u.HGUID, out var d)
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
                            var productCodesToUpdate = existingProductUpdates
                                .Select(x => x.Detail.ProductCode!)
                                .Distinct()
                                .ToList();

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

                            // 4.2 批量更新 Product 表（进口价格 -> 进货价 PurchasePrice）
                            // 使用 CASE WHEN 语句批量更新，避免 N+1 查询问题
                            var importPriceUpdates = existingProductUpdates
                                .Where(x => x.Update.进口价格.HasValue)
                                .ToList();
                            if (importPriceUpdates.Count > 0)
                            {
                                var caseBuilder = new System.Text.StringBuilder();
                                var pList = new List<SugarParameter>();
                                for (int i = 0; i < importPriceUpdates.Count; i++)
                                {
                                    var item = importPriceUpdates[i];
                                    caseBuilder.Append($" WHEN ProductCode = @Pc{i} THEN @Pp{i}");
                                    pList.Add(
                                        new SugarParameter($"@Pc{i}", item.Detail.ProductCode)
                                    );
                                    pList.Add(
                                        new SugarParameter($"@Pp{i}", item.Update.进口价格!.Value)
                                    );
                                }
                                var inClause = string.Join(
                                    ", ",
                                    Enumerable
                                        .Range(0, importPriceUpdates.Count)
                                        .Select(i => $"@Pc{i}")
                                );
                                var sql =
                                    $"UPDATE Product SET PurchasePrice = CASE {caseBuilder} ELSE PurchasePrice END WHERE ProductCode IN ({inClause})";
                                await _context.Db.Ado.ExecuteCommandAsync(sql, pList);
                            }

                            // 4.3 批量更新 StoreRetailPrice 表（进口价格 -> 进货价 PurchasePrice）
                            // 使用 CASE WHEN 语句批量更新，避免 N+1 查询问题
                            if (importPriceUpdates.Count > 0)
                            {
                                var caseBuilder = new System.Text.StringBuilder();
                                var pList = new List<SugarParameter>();
                                for (int i = 0; i < importPriceUpdates.Count; i++)
                                {
                                    var item = importPriceUpdates[i];
                                    caseBuilder.Append($" WHEN ProductCode = @Pc{i} THEN @Pp{i}");
                                    pList.Add(
                                        new SugarParameter($"@Pc{i}", item.Detail.ProductCode)
                                    );
                                    pList.Add(
                                        new SugarParameter($"@Pp{i}", item.Update.进口价格!.Value)
                                    );
                                }
                                var inClause = string.Join(
                                    ", ",
                                    Enumerable
                                        .Range(0, importPriceUpdates.Count)
                                        .Select(i => $"@Pc{i}")
                                );
                                var sql =
                                    $"UPDATE StoreRetailPrice SET PurchasePrice = CASE {caseBuilder} ELSE PurchasePrice END WHERE ProductCode IN ({inClause})";
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

        /// <summary>
        /// 创建新货柜
        /// </summary>
        public async Task<string> CreateContainerAsync(CreateContainerDto dto)
        {
            try
            {
                _logger.LogInformation("[React] 开始创建货柜: {ContainerNumber}", dto.货柜编号);

                // 检查货柜编号是否已存在，避免重复创建
                var exists = await _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerNumber == dto.货柜编号)
                    .AnyAsync();

                if (exists)
                {
                    throw new InvalidOperationException($"货柜编号 {dto.货柜编号} 已存在");
                }

                // 创建新货柜实体
                var container = new Container
                {
                    ContainerCode = Guid.NewGuid().ToString(),
                    ContainerNumber = dto.货柜编号,
                    LoadingDate = dto.装柜日期,
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
                        _logger.LogWarning(exSum, "更新货柜 {ContainerCode} 汇总字段失败，但不影响删除结果", code);
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
    }
}
