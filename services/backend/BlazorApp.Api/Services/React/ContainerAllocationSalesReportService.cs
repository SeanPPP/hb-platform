using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 货柜到货后的配货与销售报表服务。
/// </summary>
public sealed class ContainerAllocationSalesReportService : IContainerAllocationSalesReportService
{
    private const string ChinaContainerSupplierCode = "200";
    private const string ReconciliationFailurePrefix = "商品统计与分店营业额统计不一致:";
    private readonly SqlSugarContext _context;
    private readonly IContainerReactService _containerService;
    private readonly ILogger<ContainerAllocationSalesReportService> _logger;

    public ContainerAllocationSalesReportService(
        SqlSugarContext context,
        IContainerReactService containerService,
        ILogger<ContainerAllocationSalesReportService> logger
    )
    {
        _context = context;
        _containerService = containerService;
        _logger = logger;
    }

    public async Task<ContainerAllocationSalesReportResponse> QueryAsync(
        string containerGuid,
        ContainerAllocationSalesQueryRequest request
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerGuid);
        request ??= new ContainerAllocationSalesQueryRequest();

        var reportContext = await LoadReportContextAsync(containerGuid, request.StartDate, request.EndDate);
        if (!reportContext.CanQuery)
        {
            return CreateUnavailableResponse(reportContext, request);
        }

        var productCodes = reportContext.Products.Select(x => x.ProductCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allocations = await LoadAllocationsAsync(reportContext.StartDate, reportContext.EndDate, productCodes);
        var statisticStatus = await GetStatisticStatusAsync(reportContext.StartDate, reportContext.EndDate);
        var salesReady = statisticStatus.CanExposeMetrics;
        var sales = salesReady
            ? await LoadSalesAsync(reportContext.StartDate, reportContext.EndDate, productCodes)
            : new List<SalesRow>();

        var allocationByProduct = allocations.GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, AggregateAllocation, StringComparer.OrdinalIgnoreCase);
        var salesByProduct = sales.GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, AggregateSales, StringComparer.OrdinalIgnoreCase);
        var allItems = reportContext.Products.Select(product =>
        {
            allocationByProduct.TryGetValue(product.ProductCode, out var allocation);
            salesByProduct.TryGetValue(product.ProductCode, out var sale);
            return BuildProductItem(product, allocation, sale, salesReady);
        }).ToList();

        var totals = BuildTotals(allItems, salesReady);
        var filtered = ApplySearch(allItems, request.Search);
        var sorted = ApplySort(filtered, request.SortBy, request.SortDirection);
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        return new ContainerAllocationSalesReportResponse
        {
            ContainerGuid = containerGuid,
            ContainerNumber = reportContext.Container.货柜编号,
            ArrivalDate = reportContext.ArrivalDate,
            ArrivalDateBasis = reportContext.ArrivalDateBasis,
            IsEstimatedArrivalDate = reportContext.ArrivalDateBasis == ContainerArrivalDateBasis.Expected,
            CanQuery = true,
            StartDate = reportContext.StartDate,
            EndDate = reportContext.EndDate,
            DayCount = reportContext.DayCount,
            StartWeek = reportContext.StartWeek,
            EndWeek = reportContext.EndWeek,
            RangeLabel = reportContext.RangeLabel,
            Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
            Total = filtered.Count,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Totals = totals,
            StatisticStatus = statisticStatus.Status,
            StatisticMessage = statisticStatus.Message,
        };
    }

    public async Task<ContainerAllocationSalesBranchesResponse> QueryBranchesAsync(
        string containerGuid,
        ContainerAllocationSalesBranchesQueryRequest request
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerGuid);
        ArgumentNullException.ThrowIfNull(request);
        var productCode = NormalizeCode(request.ProductCode);
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("商品编码不能为空。", nameof(request));

        var reportContext = await LoadReportContextAsync(
            containerGuid,
            request.StartDate,
            request.EndDate,
            loadProducts: false
        );
        if (!reportContext.CanQuery)
            throw new ArgumentException(reportContext.QueryMessage ?? "当前货柜不能查询配销数据。", nameof(request));
        // 分店明细只需校验单个商品归属，避免加载整柜明细及商品补全联表。
        var belongsToContainer = await _context.Db.Queryable<ContainerDetail>()
            .AnyAsync(x =>
                x.ContainerCode == containerGuid
                && x.ProductCode != null
                && SqlFunc.ToUpper(x.ProductCode.Trim()) == productCode
            );
        if (!belongsToContainer)
            throw new KeyNotFoundException("货柜中不存在该商品。");

        var productCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { productCode };
        var allocations = await LoadAllocationsAsync(reportContext.StartDate, reportContext.EndDate, productCodes);
        var statisticStatus = await GetStatisticStatusAsync(reportContext.StartDate, reportContext.EndDate);
        var salesReady = statisticStatus.CanExposeMetrics;
        var sales = salesReady
            ? await LoadSalesAsync(reportContext.StartDate, reportContext.EndDate, productCodes)
            : new List<SalesRow>();

        var allocationByBranch = allocations.GroupBy(x => x.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, AggregateAllocation, StringComparer.OrdinalIgnoreCase);
        var salesByBranch = sales.GroupBy(x => x.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, AggregateSales, StringComparer.OrdinalIgnoreCase);
        // Pending 等未完成状态不能返回销售数值，但仍需读取历史分店键，保留停用分店可见性。
        IEnumerable<string> historicalSalesBranchCodes = salesReady
            ? salesByBranch.Keys
            : await LoadSalesBranchCodesAsync(reportContext.StartDate, reportContext.EndDate, productCodes);
        var historicalCodes = allocationByBranch.Keys.Concat(historicalSalesBranchCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stores = await _context.Db.Queryable<Store>().Where(x => !x.IsDeleted).ToListAsync();
        var storeByCode = stores.Where(x => !string.IsNullOrWhiteSpace(x.StoreCode))
            .GroupBy(x => NormalizeCode(x.StoreCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var visibleCodes = stores.Where(x => x.IsActive).Select(x => NormalizeCode(x.StoreCode))
            .Concat(historicalCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = visibleCodes.Select(branchCode =>
        {
            allocationByBranch.TryGetValue(branchCode, out var allocation);
            salesByBranch.TryGetValue(branchCode, out var sale);
            storeByCode.TryGetValue(branchCode, out var store);
            return BuildBranchItem(branchCode, store, allocation, sale, salesReady);
        }).ToList();

        return new ContainerAllocationSalesBranchesResponse
        {
            ContainerGuid = containerGuid,
            ProductCode = productCode,
            StartDate = reportContext.StartDate,
            EndDate = reportContext.EndDate,
            StatisticStatus = statisticStatus.Status,
            StatisticMessage = statisticStatus.Message,
            Items = items,
        };
    }

    private async Task<ReportContext> LoadReportContextAsync(
        string containerGuid,
        DateTime? requestedStart,
        DateTime? requestedEnd,
        bool loadProducts = true
    )
    {
        // 货柜主档与明细顺序读取，避免同一 scoped SqlSugar 连接并发占用。
        var container = await _containerService.GetContainerDetailAsync(containerGuid)
            ?? throw new KeyNotFoundException("货柜不存在。");
        var details = loadProducts
            ? await _containerService.GetContainerProductsAsync(containerGuid)
            : new List<ContainerDetailDto>();
        var products = details
            .Where(x => !string.IsNullOrWhiteSpace(x.商品编码))
            .GroupBy(x => NormalizeCode(x.商品编码), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group.First();
                var itemNumber = group.Select(x => x.商品信息?.货号)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                return new ProductIdentity(
                    group.Key,
                    group.Sum(x => x.装柜数量 ?? 0m),
                    // 货柜商品接口已完成图片联表与默认地址补齐，报表直接复用，避免二次查询覆盖正确图片。
                    group.Select(x => x.商品信息?.商品图片)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    itemNumber,
                    group.Select(x => x.商品信息?.商品名称).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                        ?? representative.商品信息?.英文名称
                );
            })
            .OrderBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actualDate = container.实际到货日期?.Date;
        var expectedDate = container.预计到岸日期?.Date;
        var arrivalDate = actualDate ?? expectedDate;
        var basis = actualDate.HasValue
            ? ContainerArrivalDateBasis.Actual
            : expectedDate.HasValue ? ContainerArrivalDateBasis.Expected : (ContainerArrivalDateBasis?)null;

        if (!arrivalDate.HasValue)
            return ReportContext.Unavailable(container, products, "货柜缺少实际到货日和预计到岸日，请先补充日期。", basis, null);
        if (arrivalDate.Value > DateTime.Today)
            return ReportContext.Unavailable(container, products, "货柜尚未到货，暂不能查询配销数据。", basis, arrivalDate);

        var startDate = requestedStart?.Date ?? arrivalDate.Value;
        var endDate = requestedEnd?.Date ?? MinDate(startDate.AddDays(27), DateTime.Today);
        if (startDate < arrivalDate.Value)
            throw new ArgumentException("开始日期不能早于货柜到货日期。", nameof(requestedStart));
        if (startDate > DateTime.Today)
            throw new ArgumentException("开始日期不能晚于今天。", nameof(requestedStart));
        if (endDate < startDate)
            throw new ArgumentException("结束日期不能早于开始日期。", nameof(requestedEnd));
        if (endDate > DateTime.Today)
            throw new ArgumentException("结束日期不能晚于今天。", nameof(requestedEnd));

        var startWeek = (startDate - arrivalDate.Value).Days / 7 + 1;
        var endWeek = (endDate - arrivalDate.Value).Days / 7 + 1;
        var dayCount = (endDate - startDate).Days + 1;
        return new ReportContext(
            container, products, arrivalDate.Value, basis!.Value, startDate, endDate,
            dayCount, startWeek, endWeek, $"到货后第 {startWeek}-{endWeek} 周，共 {dayCount} 天", true, null
        );
    }

    private async Task<List<AllocationRow>> LoadAllocationsAsync(
        DateTime startDate,
        DateTime endDate,
        HashSet<string> productCodes
    )
    {
        if (productCodes.Count == 0)
            return new List<AllocationRow>();

        var productCodeList = productCodes.ToList();
        // 直接在数据库连接订单与明细，并只投影报表字段，避免加载全平台订单及超长 OrderGUID IN 参数。
        var rows = await _context.Db.Queryable<WareHouseOrderDetails>()
            .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
            .Where((detail, order) =>
                !detail.IsDeleted
                && !order.IsDeleted
                && detail.ProductCode != null
                && productCodeList.Contains(SqlFunc.ToUpper(detail.ProductCode.Trim()))
                && SqlFunc.IsNull(order.OutboundDate, order.OrderDate) >= startDate
                && SqlFunc.IsNull(order.OutboundDate, order.OrderDate) < endDate.AddDays(1)
            )
            .Select((detail, order) => new AllocationQueryRow
            {
                ProductCode = detail.ProductCode,
                DetailStoreCode = detail.StoreCode,
                OrderStoreCode = order.StoreCode,
                Quantity = detail.AllocQuantity,
                ImportPrice = detail.ImportPrice,
            })
            .ToListAsync();

        return rows
            .Select(row =>
            {
                var productCode = NormalizeCode(row.ProductCode);
                // 明细分店为空或只有空格时，统一回退订单主表分店。
                var rawBranchCode = string.IsNullOrWhiteSpace(row.DetailStoreCode)
                    ? row.OrderStoreCode
                    : row.DetailStoreCode;
                var quantity = row.Quantity ?? 0m;
                return new AllocationRow(
                    productCode,
                    NormalizeCode(rawBranchCode),
                    quantity,
                    quantity * (row.ImportPrice ?? 0m)
                );
            })
            .ToList();
    }

    private async Task<List<SalesRow>> LoadSalesAsync(
        DateTime startDate,
        DateTime endDate,
        HashSet<string> productCodes
    )
    {
        if (productCodes.Count == 0)
            return new List<SalesRow>();

        var productCodeList = productCodes.ToList();
        // 商品编码在 SQL 中按 Trim + Upper 归一化，和内存合并口径保持一致。
        var query = _context.Db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x =>
                x.Date >= startDate
                && x.Date < endDate.AddDays(1)
                && x.SupplierCode == ChinaContainerSupplierCode
                && productCodeList.Contains(SqlFunc.ToUpper(x.ProductCode.Trim()))
            );

        // 数值指标在数据库按商品和分店聚合，避免把商品×分店×日期的全部统计行加载到应用层。
        var rows = await query.Clone()
            .GroupBy(x => new
            {
                ProductCode = SqlFunc.ToUpper(x.ProductCode.Trim()),
                BranchCode = SqlFunc.ToUpper(x.BranchCode.Trim()),
            })
            .Select(x => new SalesQueryRow
            {
                ProductCode = SqlFunc.ToUpper(x.ProductCode.Trim()),
                BranchCode = SqlFunc.ToUpper(x.BranchCode.Trim()),
                Quantity = SqlFunc.AggregateSum(x.TotalQuantity),
                Amount = SqlFunc.AggregateSum(x.TotalAmount),
                GrossProfit = SqlFunc.AggregateSum(x.GrossProfit),
            })
            .ToListAsync();

        // 条件聚合在不同数据库方言上差异较大，单独聚合缺成本键，仍只返回商品+分店粒度。
        var missingCostKeys = await query.Clone()
            .Where(x =>
                x.TotalCost == null
                || x.GrossProfit == null
                || SqlFunc.ToUpper(x.CostSource) == "MISSING"
            )
            .GroupBy(x => new
            {
                ProductCode = SqlFunc.ToUpper(x.ProductCode.Trim()),
                BranchCode = SqlFunc.ToUpper(x.BranchCode.Trim()),
            })
            .Select(x => new SalesMissingCostKey
            {
                ProductCode = SqlFunc.ToUpper(x.ProductCode.Trim()),
                BranchCode = SqlFunc.ToUpper(x.BranchCode.Trim()),
            })
            .ToListAsync();
        var missingCostKeySet = missingCostKeys
            .Select(x => (NormalizeCode(x.ProductCode), NormalizeCode(x.BranchCode)))
            .ToHashSet();

        return rows.Select(x => new SalesRow(
                NormalizeCode(x.ProductCode),
                NormalizeCode(x.BranchCode),
                x.Quantity,
                x.Amount,
                x.GrossProfit,
                !missingCostKeySet.Contains((NormalizeCode(x.ProductCode), NormalizeCode(x.BranchCode)))
            ))
            .ToList();
    }

    private async Task<StatisticStatus> GetStatisticStatusAsync(DateTime startDate, DateTime endDate)
    {
        var states = await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(x =>
                x.StatisticType == SalesStatisticType.ProductStoreDaily
                && x.Date >= startDate
                && x.Date < endDate.AddDays(1)
            )
            .ToListAsync();

        if (states.Count == 0)
            return new StatisticStatus(SalesStatisticRefreshStatus.Pending, "商品销售统计尚未生成。", false);
        var expectedDays = (endDate - startDate).Days + 1;
        var hasCompleteDateCoverage = states.Select(x => x.Date.Date).Distinct().Count() == expectedDays;
        // 只有全区间逐日完成，且 Failed 明确属于稳定前缀的对账差异时，才允许读取已落库指标。
        var canExposeMetrics = hasCompleteDateCoverage && states.All(state =>
            state.Status == SalesStatisticRefreshStatus.Fresh
            || (
                state.Status == SalesStatisticRefreshStatus.Failed
                && IsReconciliationFailure(state.ErrorMessage)
            )
        );
        var failedStates = states
            .Where(x => x.Status == SalesStatisticRefreshStatus.Failed)
            .OrderBy(x => x.Date)
            .ToList();
        // 操作失败优先于对账差异，组内按日期排序，避免数据库无序返回掩盖真正任务异常。
        var failed = failedStates.FirstOrDefault(x => !IsReconciliationFailure(x.ErrorMessage))
            ?? failedStates.FirstOrDefault();
        if (failed != null)
            return new StatisticStatus(
                SalesStatisticRefreshStatus.Failed,
                failed.ErrorMessage ?? "商品销售统计生成失败。",
                canExposeMetrics
            );
        if (states.Any(x => x.Status is SalesStatisticRefreshStatus.Queued or SalesStatisticRefreshStatus.Running))
            return new StatisticStatus(SalesStatisticRefreshStatus.Pending, "商品销售统计正在重算中。", false);
        if (states.Any(x => x.Status == SalesStatisticRefreshStatus.Stale))
            return new StatisticStatus(SalesStatisticRefreshStatus.Stale, "商品销售统计正在等待补算。", false);
        if (states.Any(x => x.Status == SalesStatisticRefreshStatus.Pending))
            return new StatisticStatus(SalesStatisticRefreshStatus.Pending, "商品销售统计正在生成中。", false);
        if (!hasCompleteDateCoverage)
            return new StatisticStatus(SalesStatisticRefreshStatus.Pending, "日期范围内仍有商品销售统计未生成。", false);
        if (!canExposeMetrics)
            return new StatisticStatus(SalesStatisticRefreshStatus.Pending, "商品销售统计状态尚未完成。", false);
        return new StatisticStatus(SalesStatisticRefreshStatus.Fresh, null, true);
    }

    private async Task<List<string>> LoadSalesBranchCodesAsync(
        DateTime startDate,
        DateTime endDate,
        HashSet<string> productCodes
    )
    {
        if (productCodes.Count == 0)
            return new List<string>();

        var productCodeList = productCodes.ToList();
        var rows = await _context.Db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x =>
                x.Date >= startDate
                && x.Date < endDate.AddDays(1)
                && x.SupplierCode == ChinaContainerSupplierCode
                && productCodeList.Contains(SqlFunc.ToUpper(x.ProductCode.Trim()))
            )
            .GroupBy(x => SqlFunc.ToUpper(x.BranchCode.Trim()))
            .Select(x => new SalesBranchKey
            {
                BranchCode = SqlFunc.ToUpper(x.BranchCode.Trim()),
            })
            .ToListAsync();

        return rows.Select(x => NormalizeCode(x.BranchCode))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ContainerAllocationSalesProductDto BuildProductItem(
        ProductIdentity product,
        AllocationAggregate? allocation,
        SalesAggregate? sale,
        bool salesReady
    )
    {
        return new ContainerAllocationSalesProductDto
        {
            ProductCode = product.ProductCode,
            ProductImage = product.ProductImage,
            ItemNumber = product.ItemNumber,
            ProductName = product.ProductName,
            LoadingQuantity = product.LoadingQuantity,
            AllocationQuantity = allocation?.Quantity ?? 0m,
            AllocationImportAmount = allocation?.Amount ?? 0m,
            SalesQuantity = salesReady ? sale?.Quantity ?? 0m : null,
            SalesAmount = salesReady ? sale?.Amount ?? 0m : null,
            AverageSalesPrice = salesReady ? Divide(sale?.Amount ?? 0m, sale?.Quantity ?? 0m) : null,
            GrossProfit = salesReady && (sale?.CostComplete ?? true) ? sale?.GrossProfit ?? 0m : null,
            GrossMarginRate = salesReady && (sale?.CostComplete ?? true)
                ? Divide(sale?.GrossProfit ?? 0m, sale?.Amount ?? 0m)
                : null,
            IsGrossMarginComplete = salesReady ? sale?.CostComplete ?? true : null,
        };
    }

    private static ContainerAllocationSalesBranchDto BuildBranchItem(
        string branchCode,
        Store? store,
        AllocationAggregate? allocation,
        SalesAggregate? sale,
        bool salesReady
    )
    {
        return new ContainerAllocationSalesBranchDto
        {
            BranchCode = branchCode,
            BranchName = string.IsNullOrWhiteSpace(store?.StoreName) ? branchCode : store.StoreName,
            IsActive = store?.IsActive ?? false,
            AllocationQuantity = allocation?.Quantity ?? 0m,
            AllocationImportAmount = allocation?.Amount ?? 0m,
            SalesQuantity = salesReady ? sale?.Quantity ?? 0m : null,
            SalesAmount = salesReady ? sale?.Amount ?? 0m : null,
            AverageSalesPrice = salesReady ? Divide(sale?.Amount ?? 0m, sale?.Quantity ?? 0m) : null,
            GrossProfit = salesReady && (sale?.CostComplete ?? true) ? sale?.GrossProfit ?? 0m : null,
            GrossMarginRate = salesReady && (sale?.CostComplete ?? true)
                ? Divide(sale?.GrossProfit ?? 0m, sale?.Amount ?? 0m)
                : null,
            IsGrossMarginComplete = salesReady ? sale?.CostComplete ?? true : null,
        };
    }

    private static ContainerAllocationSalesTotalsDto BuildTotals(
        List<ContainerAllocationSalesProductDto> items,
        bool salesReady
    )
    {
        var totalSalesAmount = items.Sum(x => x.SalesAmount ?? 0m);
        var totalSalesQuantity = items.Sum(x => x.SalesQuantity ?? 0m);
        var grossMarginComplete = salesReady && items.All(x => x.IsGrossMarginComplete != false);
        var grossProfit = items.Sum(x => x.GrossProfit ?? 0m);
        return new ContainerAllocationSalesTotalsDto
        {
            ProductCount = items.Count,
            LoadingQuantity = items.Sum(x => x.LoadingQuantity),
            AllocationQuantity = items.Sum(x => x.AllocationQuantity),
            AllocationImportAmount = items.Sum(x => x.AllocationImportAmount),
            SalesQuantity = salesReady ? totalSalesQuantity : null,
            SalesAmount = salesReady ? totalSalesAmount : null,
            AverageSalesPrice = salesReady ? Divide(totalSalesAmount, totalSalesQuantity) : null,
            GrossProfit = grossMarginComplete ? grossProfit : null,
            GrossMarginRate = grossMarginComplete ? Divide(grossProfit, totalSalesAmount) : null,
            IsGrossMarginComplete = salesReady ? grossMarginComplete : null,
        };
    }

    private static List<ContainerAllocationSalesProductDto> ApplySearch(
        List<ContainerAllocationSalesProductDto> items,
        string? search
    )
    {
        if (string.IsNullOrWhiteSpace(search))
            return items;
        var keyword = search.Trim();
        return items.Where(x =>
            x.ProductCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (x.ItemNumber?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            || (x.ProductName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();
    }

    private static List<ContainerAllocationSalesProductDto> ApplySort(
        List<ContainerAllocationSalesProductDto> items,
        string? sortBy,
        string? sortDirection
    )
    {
        var desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var key = sortBy?.Trim().ToLowerInvariant();
        IOrderedEnumerable<ContainerAllocationSalesProductDto> ordered = key switch
        {
            "itemnumber" => Order(items, x => x.ItemNumber, desc),
            "loadingquantity" => Order(items, x => x.LoadingQuantity, desc),
            "allocationquantity" => Order(items, x => x.AllocationQuantity, desc),
            "allocationimportamount" => Order(items, x => x.AllocationImportAmount, desc),
            "salesquantity" => Order(items, x => x.SalesQuantity, desc),
            "salesamount" => Order(items, x => x.SalesAmount, desc),
            "averagesalesprice" => Order(items, x => x.AverageSalesPrice, desc),
            "grossmarginrate" => Order(items, x => x.GrossMarginRate, desc),
            _ => Order(items, x => x.ProductCode, desc),
        };
        return ordered.ThenBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IOrderedEnumerable<ContainerAllocationSalesProductDto> Order<TKey>(
        IEnumerable<ContainerAllocationSalesProductDto> items,
        Func<ContainerAllocationSalesProductDto, TKey> selector,
        bool descending
    ) => descending ? items.OrderByDescending(selector) : items.OrderBy(selector);

    private static AllocationAggregate AggregateAllocation(IEnumerable<AllocationRow> rows) =>
        new(rows.Sum(x => x.Quantity), rows.Sum(x => x.Amount));

    private static SalesAggregate AggregateSales(IEnumerable<SalesRow> rows)
    {
        var list = rows.ToList();
        return new SalesAggregate(
            list.Sum(x => x.Quantity),
            list.Sum(x => x.Amount),
            list.Sum(x => x.GrossProfit ?? 0m),
            list.All(x => x.CostComplete)
        );
    }

    private static ContainerAllocationSalesReportResponse CreateUnavailableResponse(
        ReportContext context,
        ContainerAllocationSalesQueryRequest request
    ) => new()
    {
        ContainerGuid = context.Container.HGUID ?? string.Empty,
        ContainerNumber = context.Container.货柜编号,
        ArrivalDate = context.ArrivalDate,
        ArrivalDateBasis = context.ArrivalDateBasis,
        IsEstimatedArrivalDate = context.ArrivalDateBasis == ContainerArrivalDateBasis.Expected,
        CanQuery = false,
        QueryMessage = context.QueryMessage,
        PageNumber = Math.Max(1, request.PageNumber),
        PageSize = Math.Clamp(request.PageSize, 1, 200),
        StatisticStatus = SalesStatisticRefreshStatus.Pending,
        StatisticMessage = context.QueryMessage,
    };

    private static decimal? Divide(decimal numerator, decimal denominator) =>
        denominator == 0m ? null : numerator / denominator;

    private static DateTime MinDate(DateTime left, DateTime right) => left <= right ? left : right;
    private static string NormalizeCode(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static bool IsReconciliationFailure(string? message) =>
        message?.StartsWith(ReconciliationFailurePrefix, StringComparison.Ordinal) == true;

    private sealed record ProductIdentity(
        string ProductCode,
        decimal LoadingQuantity,
        string? ProductImage,
        string? ItemNumber,
        string? ProductName
    );
    private sealed class AllocationQueryRow
    {
        public string? ProductCode { get; set; }
        public string? DetailStoreCode { get; set; }
        public string? OrderStoreCode { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? ImportPrice { get; set; }
    }

    private sealed class SalesQueryRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public string BranchCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal? GrossProfit { get; set; }
    }

    private sealed class SalesMissingCostKey
    {
        public string ProductCode { get; set; } = string.Empty;
        public string BranchCode { get; set; } = string.Empty;
    }

    private sealed class SalesBranchKey
    {
        public string BranchCode { get; set; } = string.Empty;
    }

    private sealed record AllocationRow(string ProductCode, string BranchCode, decimal Quantity, decimal Amount);
    private sealed record SalesRow(string ProductCode, string BranchCode, decimal Quantity, decimal Amount, decimal? GrossProfit, bool CostComplete);
    private sealed record AllocationAggregate(decimal Quantity, decimal Amount);
    private sealed record SalesAggregate(decimal Quantity, decimal Amount, decimal GrossProfit, bool CostComplete);
    private sealed record StatisticStatus(string Status, string? Message, bool CanExposeMetrics);
    private sealed record ReportContext(
        ContainerMainDto Container,
        List<ProductIdentity> Products,
        DateTime? ArrivalDate,
        ContainerArrivalDateBasis? ArrivalDateBasis,
        DateTime StartDate,
        DateTime EndDate,
        int DayCount,
        int StartWeek,
        int EndWeek,
        string RangeLabel,
        bool CanQuery,
        string? QueryMessage
    )
    {
        public static ReportContext Unavailable(
            ContainerMainDto container,
            List<ProductIdentity> products,
            string message,
            ContainerArrivalDateBasis? basis,
            DateTime? arrivalDate
        ) => new(container, products, arrivalDate, basis, default, default, 0, 0, 0, string.Empty, false, message);
    }
}
