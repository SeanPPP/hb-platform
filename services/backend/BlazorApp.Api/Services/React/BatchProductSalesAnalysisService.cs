using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 批量货号销量的原始成交明细读取。
/// 日统计表没有成交时折扣字段，因此本服务不以当前商品价格或日均价反推折扣。
/// </summary>
public sealed class BatchProductSalesAnalysisService : IBatchProductSalesAnalysisService
{
    internal const int MaxItemNumbers = 500;
    internal const int MaxDays = 366;
    private readonly ISqlSugarClient _db;
    private readonly ISqlSugarClient _posmDb;
    private readonly ISqlSugarClient _hbSalesDb;
    private readonly BatchProductSalesAnalysisFactReader _factReader;
    private readonly IProductStoreDailyStatisticQueueService _productStoreDailyQueue;
    private readonly ILogger<BatchProductSalesAnalysisService> _logger;

    public BatchProductSalesAnalysisService(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        HBSalesRecordSqlSugarContext hbSalesContext,
        IProductStoreDailyStatisticQueueService productStoreDailyQueue,
        ILogger<BatchProductSalesAnalysisService> logger)
        : this(context.Db, posmContext.Db, hbSalesContext.Db, productStoreDailyQueue, logger) { }

    internal BatchProductSalesAnalysisService(
        ISqlSugarClient db,
        ISqlSugarClient posmDb,
        ISqlSugarClient hbSalesDb,
        IProductStoreDailyStatisticQueueService productStoreDailyQueue,
        ILogger<BatchProductSalesAnalysisService> logger)
    {
        _db = db;
        _posmDb = posmDb;
        _hbSalesDb = hbSalesDb;
        _factReader = new BatchProductSalesAnalysisFactReader(db, posmDb, hbSalesDb);
        _productStoreDailyQueue = productStoreDailyQueue;
        _logger = logger;
    }

    public async Task<ApiResponse<BatchProductSalesOptionsDto>> GetOptionsAsync(
        IReadOnlyList<string>? scopedStoreCodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = NormalizeScope(scopedStoreCodes);
        if (scope is { Count: 0 })
            return ApiResponse<BatchProductSalesOptionsDto>.OK(new BatchProductSalesOptionsDto());

        var query = _db.Queryable<Store>().Where(s => s.IsDeleted == false);
        if (scope != null)
            query = query.Where(s => scope.Contains(s.StoreCode));
        var stores = await query.Select(s => new BatchProductSalesStoreDto
        {
            Code = s.StoreCode,
            Name = s.StoreName,
        }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return ApiResponse<BatchProductSalesOptionsDto>.OK(new BatchProductSalesOptionsDto
        {
            Stores = stores.Where(s => !string.IsNullOrWhiteSpace(s.Code))
                .OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase).ToList(),
        });
    }

    public async Task<ApiResponse<BatchProductSalesQueryResultDto>> QueryAsync(
        BatchProductSalesQueryRequestDto request,
        IReadOnlyList<string>? scopedStoreCodes,
        CancellationToken cancellationToken = default)
    {
        var range = ValidateRange(request);
        var itemNumbers = NormalizeItemNumbers(request.ItemNumbers);
        var storeCodes = await ResolveEffectiveStoreScopeAsync(request.StoreCodes, scopedStoreCodes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = await ResolveItemMatchesAsync(itemNumbers, cancellationToken);
        var productCodes = matches.Where(m => m.Status == "matched")
            .SelectMany(m => m.ProductCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var products = await LoadProductsAsync(productCodes, cancellationToken);
        var statisticStatus = await GetStatisticStatusAsync(range.StartDate, range.EndDate, cancellationToken);
        var queueResult = !statisticStatus.IsFresh && productCodes.Count > 0
            ? await QueuePendingStatisticDatesAsync(statisticStatus.PendingDates, cancellationToken)
            : BatchProductSalesStatisticQueueResult.NotNeeded;
        // 摘要只需要净销量，直接从稳定的商品-分店-日事实表在 SQL 端聚合；
        // 不能为 500 个货号把全年逐笔成交明细加载到 API 进程。
        var quantityRows = _db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(s => s.Date >= range.StartDate && s.Date <= range.EndDate && productCodes.Contains(s.ProductCode));
        if (storeCodes != null)
            quantityRows = quantityRows.Where(s => storeCodes.Contains(s.BranchCode));
        var quantities = (await quantityRows.GroupBy(s => s.ProductCode).Select(s => new ProductQuantityRow
        {
            ProductCode = s.ProductCode,
            Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
        }).ToListAsync()).Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
            .ToDictionary(row => row.ProductCode!, row => row.Quantity, StringComparer.OrdinalIgnoreCase);
        var result = new BatchProductSalesQueryResultDto
        {
            StartDate = range.StartDate,
            EndDate = range.EndDate,
            StoreCodes = storeCodes,
            Matches = matches,
            StatisticStatus = statisticStatus.Status,
            StatisticUpdatedAt = statisticStatus.UpdatedAt,
            Products = statisticStatus.IsFresh ? products.Select(product => new BatchProductSalesProductSummaryDto
            {
                ProductCode = product.ProductCode, ItemNumber = product.ItemNumber,
                ProductName = product.ProductName, EnglishName = product.EnglishName,
                Barcode = product.Barcode, ImageUrl = product.ImageUrl,
                Quantity = quantities.TryGetValue(product.ProductCode, out var quantity) ? quantity : 0m,
            }).OrderBy(p => p.ItemNumber, StringComparer.OrdinalIgnoreCase).ToList() : [],
        };
        if (statisticStatus.IsFresh)
        {
            result.Warnings.Add("摘要净销量来自商品分店日统计；折扣拆分仅在单商品明细中按成交记录计算。");
        }
        else
        {
            result.Warnings.Add("商品日统计尚未完整刷新，未返回摘要销量，避免把未生成数据误认为零销量。");
            queueResult.AppendWarnings(result.Warnings);
        }
        return ApiResponse<BatchProductSalesQueryResultDto>.OK(result);
    }

    public async Task<ApiResponse<BatchProductSalesDetailDto>> GetDetailAsync(
        BatchProductSalesDetailRequestDto request,
        IReadOnlyList<string>? scopedStoreCodes,
        CancellationToken cancellationToken = default)
    {
        var range = ValidateRange(request);
        var productCode = NormalizeRequired(request.ProductCode, "productCode");
        var storeCodes = await ResolveEffectiveStoreScopeAsync(request.StoreCodes, scopedStoreCodes, cancellationToken);
        var product = (await LoadProductsAsync([productCode], cancellationToken)).SingleOrDefault();
        if (product == null)
            throw new BatchProductSalesAnalysisValidationException("商品不存在。");

        var facts = await _factReader.ReadAsync([productCode], range.StartDate, range.EndDate, storeCodes, cancellationToken);
        var storeNames = await LoadStoreNamesAsync(facts.Select(f => f.BranchCode), cancellationToken);
        var result = new BatchProductSalesDetailDto
        {
            StartDate = range.StartDate,
            EndDate = range.EndDate,
            StoreCodes = storeCodes,
            Product = product,
            Metrics = BuildAggregateMetrics(facts),
            Daily = BuildAggregateDaily(facts, range.StartDate, range.EndDate),
            Branches = facts.GroupBy(f => f.BranchCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new BatchProductSalesBranchDto
                {
                    BranchCode = group.Key,
                    BranchName = storeNames.TryGetValue(group.Key, out var name) ? name : group.Key,
                    Metrics = BuildAggregateMetrics(group),
                    Daily = BuildAggregateDaily(group, range.StartDate, range.EndDate),
                }).ToList(),
        };
        AddSourceWarning(result.Warnings, range);
        var statistic = await GetProductStatisticTotalsAsync(productCode, range.StartDate, range.EndDate, storeCodes, cancellationToken);
        if (statistic.IsFresh && (statistic.Quantity != result.Metrics.Quantity || statistic.Amount != result.Metrics.SalesAmount))
            result.Warnings.Add($"成交明细与 Fresh 商品日统计存在差异：销量 {result.Metrics.Quantity - statistic.Quantity:+0.####;-0.####;0}，金额 {result.Metrics.SalesAmount - statistic.Amount:+0.00;-0.00;0.00}。");
        return ApiResponse<BatchProductSalesDetailDto>.OK(result);
    }

    private async Task<List<BatchProductSalesMatchDto>> ResolveItemMatchesAsync(
        IReadOnlyList<string> itemNumbers, CancellationToken cancellationToken)
    {
        var rows = await _db.Queryable<Product>()
            .Where(p => p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
            .Select(p => new { p.ItemNumber, p.ProductCode }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var byItem = rows.Where(r => !string.IsNullOrWhiteSpace(r.ItemNumber) && !string.IsNullOrWhiteSpace(r.ProductCode))
            .GroupBy(r => r.ItemNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ProductCode!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        return itemNumbers.Select(item =>
        {
            var codes = byItem.TryGetValue(item, out var candidates) ? candidates : [];
            return new BatchProductSalesMatchDto
            {
                ItemNumber = item, ProductCodes = codes,
                Status = codes.Count switch { 0 => "notFound", 1 => "matched", _ => "ambiguous" },
            };
        }).ToList();
    }

    private async Task<List<BatchProductSalesProductDto>> LoadProductsAsync(
        IReadOnlyList<string> productCodes, CancellationToken cancellationToken)
    {
        if (productCodes.Count == 0) return [];
        var products = await _db.Queryable<Product>().Where(p => productCodes.Contains(p.ProductCode))
            .Select(p => new BatchProductSalesProductDto
            {
                ProductCode = p.ProductCode ?? string.Empty, ItemNumber = p.ItemNumber ?? string.Empty, ProductName = p.ProductName ?? string.Empty,
                EnglishName = p.EnglishName, Barcode = p.Barcode, ImageUrl = p.ProductImage,
            }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return products.Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    internal static BatchProductSalesMetricsDto BuildAggregateMetrics(IEnumerable<BatchProductSalesAggregateRow> facts)
    {
        var rows = facts.ToList();
        var unknownRows = rows.Sum(row => row.UnknownRowCount);
        var regular = rows.Sum(row => row.RegularQuantity);
        var discount = rows.Sum(row => row.DiscountQuantity);
        var unknown = rows.Sum(row => row.UnknownQuantity);
        return new BatchProductSalesMetricsDto
        {
            Quantity = regular + discount + unknown, RegularQuantity = regular, DiscountQuantity = discount,
            UnknownQuantity = unknown, ReturnQuantity = rows.Sum(row => row.ReturnQuantity), SalesAmount = rows.Sum(row => row.SalesAmount),
            // 未知销售与退货可能刚好抵消；必须以未知行数而不是净数量判断状态。
            DiscountStatus = unknownRows > 0 ? (regular != 0m || discount != 0m ? "partial" : "unknown") : "complete",
            OriginalPriceMin = MinPrice(rows.Select(row => row.OriginalPriceMin)), OriginalPriceMax = MaxPrice(rows.Select(row => row.OriginalPriceMax)),
            DiscountPriceMin = MinPrice(rows.Select(row => row.DiscountPriceMin)), DiscountPriceMax = MaxPrice(rows.Select(row => row.DiscountPriceMax)),
        };
    }

    private static List<BatchProductSalesDailyDto> BuildAggregateDaily(IEnumerable<BatchProductSalesAggregateRow> facts, DateTime start, DateTime end)
    {
        var map = facts.GroupBy(f => f.Date).ToDictionary(g => g.Key, g => BuildAggregateMetrics(g));
        var result = new List<BatchProductSalesDailyDto>();
        for (var date = start; date <= end; date = date.AddDays(1))
            result.Add(new BatchProductSalesDailyDto { Date = date, Metrics = map.TryGetValue(date, out var metrics) ? metrics : new BatchProductSalesMetricsDto { DiscountStatus = "complete" } });
        return result;
    }

    private static decimal? MinPrice(IEnumerable<decimal?> values) { var rows = values.Where(value => value.HasValue).Select(value => value!.Value).ToList(); return rows.Count == 0 ? null : rows.Min(); }
    private static decimal? MaxPrice(IEnumerable<decimal?> values) { var rows = values.Where(value => value.HasValue).Select(value => value!.Value).ToList(); return rows.Count == 0 ? null : rows.Max(); }

    private async Task<Dictionary<string, string>> LoadStoreNamesAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var storeCodes = codes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (storeCodes.Count == 0) return new(StringComparer.OrdinalIgnoreCase);
        var rows = await _db.Queryable<Store>().Where(s => storeCodes.Contains(s.StoreCode))
            .Select(s => new { s.StoreCode, s.StoreName }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.StoreCode)).ToDictionary(r => r.StoreCode, r => r.StoreName ?? r.StoreCode, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> LoadAllStoreCodesAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Queryable<Store>().Where(s => s.IsDeleted == false).Select(s => s.StoreCode).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return NormalizeStoreCodes(rows);
    }

    private async Task<List<string>> ResolveEffectiveStoreScopeAsync(IEnumerable<string>? requested,
        IReadOnlyList<string>? granted, CancellationToken cancellationToken)
    {
        var requestedScope = ResolveStoreScope(requested, granted);
        var activeStores = await LoadAllStoreCodesAsync(cancellationToken);
        // null 表示管理员全店，但“全店”只能是 options 也会展示的有效门店。
        return requestedScope == null
            ? activeStores
            : requestedScope.Where(code => activeStores.Contains(code, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private async Task<BatchProductSalesStatisticStatus> GetStatisticStatusAsync(
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var states = await _db.Queryable<SalesStatisticRefreshState>()
            .Where(state => state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Date >= startDate && state.Date <= endDate)
            .Select(state => new { state.Date, state.Status, state.CompletedAtUtc, state.LastCheckedAtUtc }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var byDate = states.GroupBy(state => state.Date.Date).ToDictionary(group => group.Key, group => group.ToList());
        var pendingDates = new List<DateTime>();
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            if (!byDate.TryGetValue(date, out var dateStates)
                || dateStates.Count != 1
                || !string.Equals(dateStates[0].Status, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase))
            {
                pendingDates.Add(date);
            }
        }
        var updatedAt = states.Select(state => state.CompletedAtUtc ?? state.LastCheckedAtUtc)
            .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Max();
        return new BatchProductSalesStatisticStatus(pendingDates.Count == 0,
            pendingDates.Count == 0 ? SalesStatisticRefreshStatus.Fresh : SalesStatisticRefreshStatus.Pending,
            updatedAt, pendingDates);
    }

    private async Task<BatchProductSalesStatisticQueueResult> QueuePendingStatisticDatesAsync(
        IReadOnlyList<DateTime> pendingDates,
        CancellationToken cancellationToken)
    {
        var submitted = new List<DateTime>();
        var alreadyActive = new List<DateTime>();
        var failures = new List<DateTime>();
        foreach (var segment in SplitStatisticQueueSegments(pendingDates))
        {
            try
            {
                var result = segment.IsYearBackfill
                    ? await _productStoreDailyQueue.EnqueueYearBackfillAsync(
                        segment.Dates, "batch-product-sales-analysis", cancellationToken: cancellationToken)
                    : await _productStoreDailyQueue.EnqueueAsync(
                        segment.Dates, "batch-product-sales-analysis", cancellationToken: cancellationToken);
                submitted.AddRange(result.SubmittedDates.Select(date => date.Date));
                alreadyActive.AddRange(result.SkippedDates.Select(date => date.Date));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 队列事务失败时不能把 Pending 说成已经提交；其余独立分段仍可尝试恢复可用性。
                _logger.LogError(ex, "批量货号销量查询提交商品日统计失败，日期 {StartDate:yyyy-MM-dd} 至 {EndDate:yyyy-MM-dd}",
                    segment.Dates[0], segment.Dates[^1]);
                failures.AddRange(segment.Dates);
            }
        }
        return new BatchProductSalesStatisticQueueResult(submitted, alreadyActive, failures);
    }

    /// <summary>
    /// 2025 年读取 HBSales 配对年度事实，沿用年度回填队列与其全局串行租约；其他年份遵守常规 31 天任务上限。
    /// Fresh 日期不会传入，因此重查冷门历史不会清空或重建已完成区间。
    /// </summary>
    internal static List<BatchProductSalesStatisticQueueSegment> SplitStatisticQueueSegments(IEnumerable<DateTime> pendingDates)
    {
        var dates = pendingDates.Select(date => date.Date).Distinct().OrderBy(date => date).ToList();
        var segments = new List<BatchProductSalesStatisticQueueSegment>();
        foreach (var group in dates.GroupBy(date => date.Year))
        {
            var groupDates = group.ToList();
            if (group.Key == 2025)
            {
                // 输入范围最多 366 天，单年度最多 365 天，符合年度回填队列上限。
                segments.Add(new BatchProductSalesStatisticQueueSegment(groupDates, true));
                continue;
            }
            foreach (var chunk in groupDates.Chunk(31))
                segments.Add(new BatchProductSalesStatisticQueueSegment(chunk.ToList(), false));
        }
        return segments;
    }

    private async Task<(bool IsFresh, decimal Quantity, decimal Amount)> GetProductStatisticTotalsAsync(string productCode,
        DateTime startDate, DateTime endDate, IReadOnlyCollection<string> storeCodes, CancellationToken cancellationToken)
    {
        var status = await GetStatisticStatusAsync(startDate, endDate, cancellationToken);
        if (!status.IsFresh) return (false, 0m, 0m);
        var rows = await _db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.ProductCode == productCode && row.Date >= startDate && row.Date <= endDate && storeCodes.Contains(row.BranchCode))
            .Select(row => new { row.TotalQuantity, row.TotalAmount }).ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return (true, rows.Sum(row => (decimal)row.TotalQuantity), rows.Sum(row => row.TotalAmount));
    }

    internal static (DateTime StartDate, DateTime EndDate) ValidateRange(BatchProductSalesScopeDto request)
    {
        var start = request.StartDate.Date; var end = request.EndDate.Date;
        if (start == DateTime.MinValue || end == DateTime.MinValue || start > end || end > GetBrisbaneToday() || (end - start).TotalDays + 1 > MaxDays)
            throw new BatchProductSalesAnalysisValidationException("日期范围无效，最多 366 天。");
        return (start, end);
    }
    internal static List<string> NormalizeItemNumbers(IEnumerable<string>? values)
    {
        var items = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (items.Count == 0 || items.Count > MaxItemNumbers) throw new BatchProductSalesAnalysisValidationException("货号数量必须在 1 到 500 之间。");
        return items;
    }
    internal static List<string>? ResolveStoreScope(IEnumerable<string>? requested, IReadOnlyList<string>? granted)
    {
        var normalizedRequested = NormalizeStoreCodes(requested);
        var normalizedGranted = NormalizeScope(granted);
        if (normalizedGranted is { Count: 0 }) throw new BatchProductSalesAnalysisForbiddenException();
        if (normalizedGranted == null) return normalizedRequested.Count == 0 ? null : normalizedRequested;
        if (normalizedRequested.Count == 0) return normalizedGranted;
        if (normalizedRequested.Any(code => !normalizedGranted.Contains(code, StringComparer.OrdinalIgnoreCase))) throw new BatchProductSalesAnalysisForbiddenException();
        return normalizedRequested;
    }
    private static List<string>? NormalizeScope(IReadOnlyList<string>? values) => values == null ? null : NormalizeStoreCodes(values);
    private static List<string> NormalizeStoreCodes(IEnumerable<string>? values) => values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    private static string NormalizeRequired(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new BatchProductSalesAnalysisValidationException($"{name} 不能为空。") : value.Trim();
    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    private static DateTime GetBrisbaneToday()
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane")).Date; }
        catch (TimeZoneNotFoundException) { return DateTime.UtcNow.Date; }
    }
    private static void AddSourceWarning(List<string> warnings, (DateTime StartDate, DateTime EndDate) range)
    {
        warnings.Add("销量和折扣拆分来自成交明细；未知折扣不会按当前商品价格推断。");
        if (range.StartDate.Year <= 2025 && range.EndDate.Year >= 2025) warnings.Add("2025 年按既有口径合并 HBSales 与 POSM，已排除 HBSales 单据类型 2 和 POSM 非完成订单。");
    }

    private sealed record BatchProductSalesStatisticStatus(
        bool IsFresh,
        string Status,
        DateTime? UpdatedAt,
        IReadOnlyList<DateTime> PendingDates);

    internal sealed record BatchProductSalesStatisticQueueSegment(
        IReadOnlyList<DateTime> Dates,
        bool IsYearBackfill);

    private sealed class BatchProductSalesStatisticQueueResult
    {
        public static readonly BatchProductSalesStatisticQueueResult NotNeeded = new([], [], []);

        public BatchProductSalesStatisticQueueResult(
            IReadOnlyCollection<DateTime> submittedDates,
            IReadOnlyCollection<DateTime> alreadyActiveDates,
            IReadOnlyCollection<DateTime> failedDates)
        {
            SubmittedDates = submittedDates.Distinct().OrderBy(date => date).ToList();
            AlreadyActiveDates = alreadyActiveDates.Distinct().OrderBy(date => date).ToList();
            FailedDates = failedDates.Distinct().OrderBy(date => date).ToList();
        }

        private IReadOnlyList<DateTime> SubmittedDates { get; }
        private IReadOnlyList<DateTime> AlreadyActiveDates { get; }
        private IReadOnlyList<DateTime> FailedDates { get; }

        public void AppendWarnings(List<string> warnings)
        {
            if (SubmittedDates.Count > 0)
                warnings.Add($"已将 {SubmittedDates.Count} 个缺失或未完成统计日期提交到持久队列，等待后台生成后可重试查询。");
            if (AlreadyActiveDates.Count > 0)
                warnings.Add($"{AlreadyActiveDates.Count} 个统计日期已有活动队列任务，未重复创建任务。");
            if (FailedDates.Count > 0)
                warnings.Add($"{FailedDates.Count} 个统计日期提交失败，尚未排队，请人工重试。");
        }
    }

    private sealed class ProductQuantityRow { public string? ProductCode { get; set; } public decimal Quantity { get; set; } }
}

public sealed class BatchProductSalesAnalysisValidationException(string message) : ArgumentException(message);
public sealed class BatchProductSalesAnalysisForbiddenException : UnauthorizedAccessException;
