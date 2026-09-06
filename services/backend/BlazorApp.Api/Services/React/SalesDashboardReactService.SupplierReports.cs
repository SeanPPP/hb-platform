using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public partial class SalesDashboardReactService
{
    private readonly bool _useSupplierRollups;
    private static readonly ConditionalWeakTable<IMemoryCache, ConcurrentDictionary<string, Lazy<Task<object>>>>
        CompleteReportReads = new();

    private sealed class ReportSnapshotChangedException : Exception { }

    internal sealed class SupplierRollupReadRow
    {
        public string SupplierCode { get; set; } = string.Empty;
        public string BranchCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int TotalQuantity { get; set; }
        public int OrderCount { get; set; }
        public int StoreCount { get; set; }
        public decimal? GrossProfit { get; set; }
        public int StatisticRowCount { get; set; }
        public int CostedRowCount { get; set; }
        public int GrossProfitRowCount { get; set; }
        public int InvalidRowCount { get; set; }
    }

    private async Task<ProductReportStatisticStatusDto> GetSupplierBackedProductReportStatusAsync(DateRangeDto range)
    {
        var dates = EnumerateReportDates(range.StartDate.Date, range.EndDate.Date);
        if (range.CompareStartDate.HasValue && range.CompareEndDate.HasValue)
            dates.AddRange(EnumerateReportDates(range.CompareStartDate.Value.Date, range.CompareEndDate.Value.Date));
        var requestedDates = dates.Distinct().OrderBy(date => date).ToList();
        var first = requestedDates[0];
        var end = requestedDates[^1].AddDays(1);
        var types = new[] { SalesStatisticType.ProductStoreDaily, SalesStatisticType.AustralianSupplierStoreSales, SalesStatisticType.ChinaSupplierStoreSales };
        // 一次读取三类状态；前台三个组件使用同一版本定义，禁止拼接不同批次的数据。
        var states = (await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(state => state.Date >= first && state.Date < end && types.Contains(state.StatisticType))
            .ToListAsync()).Where(state => requestedDates.Contains(state.Date.Date)).ToList();
        var source = string.Join("|", states.OrderBy(state => state.Date).ThenBy(state => state.StatisticType)
            .Select(state => $"{state.StatisticType}:{state.Date:yyyyMMdd}:{state.Status}:{state.LastAggregatedAtUtc?.Ticks}:{state.CompletedAtUtc?.Ticks}:{state.SourceProductVersion}"));
        var result = new ProductReportStatisticStatusDto
        {
            CacheVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            // 报告包含多天和同期时，以最旧的完整水位说明整份报告的新鲜度。
            StatisticUpdatedAt = states.Select(state => state.CompletedAtUtc ?? state.LastAggregatedAtUtc).DefaultIfEmpty().Min(),
            StatisticStatus = SalesStatisticRefreshStatus.Pending,
            StatisticMessage = "供应商统计尚未生成完整。",
        };
        if (states.Any(state => state.Status == SalesStatisticRefreshStatus.Failed))
        {
            result.StatisticStatus = SalesStatisticRefreshStatus.Failed;
            result.StatisticMessage = "统计更新失败，等待后台恢复。";
            return result;
        }
        foreach (var date in requestedDates)
        {
            var product = states.SingleOrDefault(state => state.Date.Date == date && state.StatisticType == SalesStatisticType.ProductStoreDaily);
            if (product == null || product.Status != SalesStatisticRefreshStatus.Fresh || !product.CompletedAtUtc.HasValue || !product.LastAggregatedAtUtc.HasValue)
                return result;
            var productVersion = SupplierStatisticVersion.GetProductVersion(product);
            if (string.IsNullOrWhiteSpace(productVersion))
                return result;
            foreach (var type in types.Skip(1))
            {
                var supplier = states.SingleOrDefault(state => state.Date.Date == date && state.StatisticType == type);
                if (supplier == null || supplier.Status != SalesStatisticRefreshStatus.Fresh
                    || !supplier.CompletedAtUtc.HasValue || !supplier.LastAggregatedAtUtc.HasValue
                    || supplier.SourceProductVersion != productVersion)
                    return result;
            }
        }
        result.StatisticStatus = SalesStatisticRefreshStatus.Fresh;
        result.StatisticMessage = null;
        return result;
    }

    private static void CopyReportStatisticStatus(ProductReportStatisticStatusDto source, ProductReportStatisticStatusDto target)
    {
        target.StatisticStatus = source.StatisticStatus;
        target.StatisticMessage = source.StatisticMessage;
        target.StatisticUpdatedAt = source.StatisticUpdatedAt;
        target.CacheVersion = source.CacheVersion;
    }

    private async Task<T> ReadCompleteReportAsync<T>(DateRangeDto range, ProductReportStatisticStatusDto status,
        Func<string, string> queryKey, Func<SalesDashboardReactService, Task<T>> read, Func<T> empty) where T : class
    {
        ProductReportStatisticStatusDto before;
        try
        {
            before = await GetProductReportStatisticStatusAsync(range);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "读取完整报表版本失败");
            status.StatisticStatus = SalesStatisticRefreshStatus.Failed;
            status.StatisticMessage = "统计读取失败，请稍后重试。";
            return empty();
        }
        CopyReportStatisticStatus(before, status);
        if (!IsProductStatisticFresh(before))
            return empty();
        // 通过既有缓存键管理器登记实际键，让手动清缓存仍能覆盖这条读取路径。
        var key = queryKey($"complete:{before.CacheVersion}");
        if (_cache.TryGetValue<T>(key, out var cached) && cached != null)
            return cached;

        var flights = CompleteReportReads.GetValue(_cache, _ => new());
        var pending = flights.GetOrAdd(key, _ => new Lazy<Task<object>>(async () =>
        {
            // 共享计算拥有自己的 DI scope；调用端取消不会释放正在服务其他请求的数据库上下文。
            using var scope = _serviceScopeFactory?.CreateScope();
            var executor = scope?.ServiceProvider.GetService<ISalesDashboardReactService>() as SalesDashboardReactService ?? this;
            var value = await read(executor);
            var after = await executor.GetProductReportStatisticStatusAsync(range);
            if (!IsProductStatisticFresh(after) || after.CacheVersion != before.CacheVersion)
                throw new ReportSnapshotChangedException();
            _cache.Set(key, value, DETAIL_CACHE_DURATION);
            return value;
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return (T)await pending.Value;
        }
        catch (ReportSnapshotChangedException)
        {
            // 控制器复用这个包络对象，必须同步标记 Pending，不能给空数据附上 Fresh。
            status.StatisticStatus = SalesStatisticRefreshStatus.Pending;
            status.StatisticMessage = "统计版本正在更新。";
            return empty();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "读取完整报表数据失败");
            status.StatisticStatus = SalesStatisticRefreshStatus.Failed;
            status.StatisticMessage = "统计读取失败，请稍后重试。";
            return empty();
        }
        finally
        {
            flights.TryRemove(new KeyValuePair<string, Lazy<Task<object>>>(key, pending));
        }
    }

    private async Task<List<SupplierRollupReadRow>> QuerySupplierRollupRowsAsync(bool china, bool byBranch,
        DateTime start, DateTime end, List<string>? branches, List<string>? suppliers)
    {
        if (branches != null && NormalizeCodes(branches).Count == 0)
            return new();
        // 表名与列片段均为内部常量，业务筛选只使用参数。
        var table = china ? "ChinaSupplierStoreSalesDetail" : "AustralianSupplierStoreSalesDetail";
        var parameters = new List<SugarParameter> { new("@start", start.Date), new("@end", end.Date.AddDays(1)) };
        var filters = new StringBuilder();
        void Filter(string column, string name, List<string>? codes)
        {
            var normalized = NormalizeCodes(codes);
            if (normalized.Count == 0) return;
            parameters.Add(new SugarParameter(name, JsonSerializer.Serialize(normalized)));
            var rows = _context.Db.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? $"OPENJSON({name})" : $"json_each({name})";
            filters.Append($" AND [{column}] IN (SELECT [value] FROM {rows})");
        }
        Filter("BranchCode", "@branches", branches);
        Filter("SupplierCode", "@suppliers", suppliers);
        var branchSelect = byBranch ? "[BranchCode]" : "''";
        var branchGroup = byBranch ? ", [BranchCode]" : "";
        var sql = $"""
            SELECT [SupplierCode], {branchSelect} AS BranchCode,
                SUM([TotalAmount]) AS TotalAmount, SUM([TotalQuantity]) AS TotalQuantity,
                SUM(COALESCE([OrderCount], 0)) AS OrderCount, COUNT(DISTINCT [BranchCode]) AS StoreCount,
                SUM([GrossProfit]) AS GrossProfit,
                SUM(COALESCE([StatisticRowCount], 0)) AS StatisticRowCount,
                SUM(COALESCE([CostedRowCount], 0)) AS CostedRowCount,
                SUM(COALESCE([GrossProfitRowCount], 0)) AS GrossProfitRowCount,
                SUM(CASE WHEN [StatisticRowCount] IS NULL OR [CostedRowCount] IS NULL OR [GrossProfitRowCount] IS NULL THEN 1 ELSE 0 END) AS InvalidRowCount
            FROM [{table}]
            WHERE [Date] >= @start AND [Date] < @end {filters}
            GROUP BY [SupplierCode]{branchGroup}
            """;
        var result = await _context.Db.Ado.SqlQueryAsync<SupplierRollupReadRow>(sql, parameters.ToArray());
        if (result.Any(row => row.InvalidRowCount > 0))
            throw new ReportSnapshotChangedException();
        return result;
    }

    private async Task<List<(SupplierRollupReadRow Current, SupplierRollupReadRow Compare, string SupplierName, string BranchName)>>
        ReadSupplierRollupMetricsAsync(bool china, bool byBranch, DateRangeDto range, List<string>? branches, List<string>? suppliers, int? topN)
    {
        var current = await QuerySupplierRollupRowsAsync(china, byBranch, range.StartDate, range.EndDate, branches, suppliers);
        current = current.OrderByDescending(row => row.TotalAmount).ThenBy(row => row.SupplierCode).ThenBy(row => row.BranchCode).ToList();
        if (topN.HasValue) current = current.Take(Math.Max(0, topN.Value)).ToList();
        if (current.Count == 0) return new();
        var selectedSuppliers = current.Select(row => row.SupplierCode).Distinct().ToList();
        var comparison = range.CompareStartDate.HasValue && range.CompareEndDate.HasValue
            ? await QuerySupplierRollupRowsAsync(china, byBranch, range.CompareStartDate.Value, range.CompareEndDate.Value, branches, selectedSuppliers)
            : new List<SupplierRollupReadRow>();
        string Key(SupplierRollupReadRow row) => byBranch ? $"{row.BranchCode}|{row.SupplierCode}" : row.SupplierCode;
        var compare = comparison.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var names = china ? await GetChinaSupplierNameMapAsync(selectedSuppliers) : await GetAustralianSupplierNameMapAsync(selectedSuppliers);
        var stores = byBranch ? await GetStoreNameMapAsync(current.Select(row => row.BranchCode).ToHashSet()) : new Dictionary<string, string>();
        return current.Select(row => (row, compare.GetValueOrDefault(Key(row)) ?? new SupplierRollupReadRow(),
            names.GetValueOrDefault(row.SupplierCode) ?? row.SupplierCode, stores.GetValueOrDefault(row.BranchCode) ?? row.BranchCode)).ToList();
    }

    private static decimal? RollupProfit(SupplierRollupReadRow row) => GetCompleteGrossProfit(row.GrossProfit,
        row.StatisticRowCount, row.CostedRowCount, row.GrossProfitRowCount);

    private Task<List<SupplierSalesRankDto>> GetSupplierRankFromRollupsAsync(DateRangeDto range, List<string>? branches,
        int topN, string? supplierCode, ProductReportStatisticStatusDto status) =>
        ReadCompleteReportAsync(range, status, version => SalesDashboardCacheKeys.SupplierRank(range, branches, topN, supplierCode, version),
            async service => (await service.ReadSupplierRollupMetricsAsync(false, false, range, branches,
                string.IsNullOrWhiteSpace(supplierCode) ? null : new() { supplierCode.Trim() }, topN))
                .Select(row => ToSupplierRank(range, row.Current, row.Compare, row.SupplierName)).ToList(), () => new List<SupplierSalesRankDto>());

    private Task<List<ChinaSupplierSalesRankDto>> GetChinaSupplierRankFromRollupsAsync(DateRangeDto range, List<string>? branches,
        int topN, string? supplierCode, ProductReportStatisticStatusDto status) =>
        ReadCompleteReportAsync(range, status, version => SalesDashboardCacheKeys.ChinaSupplierRank(range, branches, topN, supplierCode, version),
            async service => (await service.ReadSupplierRollupMetricsAsync(true, false, range, branches,
                string.IsNullOrWhiteSpace(supplierCode) ? null : new() { supplierCode.Trim() }, topN))
                .Select(row => ToChinaSupplierRank(ToSupplierRank(range, row.Current, row.Compare, row.SupplierName))).ToList(), () => new List<ChinaSupplierSalesRankDto>());

    private Task<List<SupplierStoreSalesDto>> GetSupplierStoresFromRollupsAsync(DateRangeDto range, List<string> suppliers,
        List<string>? branches, ProductReportStatisticStatusDto status) =>
        ReadCompleteReportAsync(range, status, version => SalesDashboardCacheKeys.SupplierStore(range, suppliers, branches, version),
            async service => (await service.ReadSupplierRollupMetricsAsync(false, true, range, branches, suppliers, null))
                .Select(row => ToSupplierStore(range, row.Current, row.Compare, row.SupplierName, row.BranchName)).ToList(), () => new List<SupplierStoreSalesDto>());

    private Task<List<ChinaSupplierStoreSalesDto>> GetChinaSupplierStoresFromRollupsAsync(DateRangeDto range, List<string> suppliers,
        List<string>? branches, ProductReportStatisticStatusDto status) =>
        ReadCompleteReportAsync(range, status, version => SalesDashboardCacheKeys.ChinaSupplierStore(range, suppliers, branches, version),
            async service => (await service.ReadSupplierRollupMetricsAsync(true, true, range, branches, suppliers, null))
                .Select(row => ToChinaSupplierStore(ToSupplierStore(range, row.Current, row.Compare, row.SupplierName, row.BranchName))).ToList(), () => new List<ChinaSupplierStoreSalesDto>());

    private SupplierSalesRankDto ToSupplierRank(DateRangeDto range, SupplierRollupReadRow current, SupplierRollupReadRow compare, string name)
    {
        var hasCompare = range.CompareStartDate.HasValue && range.CompareEndDate.HasValue;
        var profit = RollupProfit(current);
        var compareProfit = hasCompare ? RollupProfit(compare) : null;
        return new SupplierSalesRankDto
        {
            StartDate = range.StartDate.Date, EndDate = range.EndDate.Date, SupplierCode = current.SupplierCode, SupplierName = name,
            TotalAmount = current.TotalAmount, TotalQuantity = current.TotalQuantity, OrderCount = current.OrderCount, StoreCount = current.StoreCount,
            AverageTransaction = current.OrderCount > 0 ? current.TotalAmount / current.OrderCount : 0,
            GrossProfit = profit, GrossMarginRate = CalculateGrossMarginRate(current.TotalAmount, profit),
            CompareTotalAmount = hasCompare ? compare.TotalAmount : null, CompareOrderCount = hasCompare ? compare.OrderCount : null,
            CompareAverageTransaction = hasCompare ? (compare.OrderCount > 0 ? compare.TotalAmount / compare.OrderCount : 0) : null,
            TotalAmountGrowth = hasCompare ? CalculateGrowth(current.TotalAmount, compare.TotalAmount) : null,
            CompareGrossProfit = compareProfit, CompareGrossMarginRate = hasCompare ? CalculateGrossMarginRate(compare.TotalAmount, compareProfit) : null,
        };
    }

    private static ChinaSupplierSalesRankDto ToChinaSupplierRank(SupplierSalesRankDto row) => new()
    {
        StartDate = row.StartDate, EndDate = row.EndDate, SupplierCode = row.SupplierCode, SupplierName = row.SupplierName,
        TotalAmount = row.TotalAmount, TotalQuantity = row.TotalQuantity, OrderCount = row.OrderCount, StoreCount = row.StoreCount,
        AverageTransaction = row.AverageTransaction, GrossProfit = row.GrossProfit, GrossMarginRate = row.GrossMarginRate,
        CompareTotalAmount = row.CompareTotalAmount, CompareOrderCount = row.CompareOrderCount, CompareAverageTransaction = row.CompareAverageTransaction,
        TotalAmountGrowth = row.TotalAmountGrowth, CompareGrossProfit = row.CompareGrossProfit, CompareGrossMarginRate = row.CompareGrossMarginRate,
    };

    private SupplierStoreSalesDto ToSupplierStore(DateRangeDto range, SupplierRollupReadRow current, SupplierRollupReadRow compare,
        string supplierName, string branchName)
    {
        var row = ToSupplierRank(range, current, compare, supplierName);
        return new SupplierStoreSalesDto
        {
            StartDate = row.StartDate, EndDate = row.EndDate, SupplierCode = row.SupplierCode, SupplierName = row.SupplierName,
            BranchCode = current.BranchCode, BranchName = branchName, TotalAmount = row.TotalAmount, TotalQuantity = row.TotalQuantity,
            OrderCount = row.OrderCount, AverageTransaction = row.AverageTransaction, GrossProfit = row.GrossProfit, GrossMarginRate = row.GrossMarginRate,
            CompareTotalAmount = row.CompareTotalAmount, CompareOrderCount = row.CompareOrderCount, CompareAverageTransaction = row.CompareAverageTransaction,
            TotalAmountGrowth = row.TotalAmountGrowth, CompareGrossProfit = row.CompareGrossProfit, CompareGrossMarginRate = row.CompareGrossMarginRate,
        };
    }

    private static ChinaSupplierStoreSalesDto ToChinaSupplierStore(SupplierStoreSalesDto row) => new()
    {
        StartDate = row.StartDate, EndDate = row.EndDate, SupplierCode = row.SupplierCode, SupplierName = row.SupplierName,
        BranchCode = row.BranchCode, BranchName = row.BranchName, TotalAmount = row.TotalAmount, TotalQuantity = row.TotalQuantity,
        OrderCount = row.OrderCount, AverageTransaction = row.AverageTransaction, GrossProfit = row.GrossProfit, GrossMarginRate = row.GrossMarginRate,
        CompareTotalAmount = row.CompareTotalAmount, CompareOrderCount = row.CompareOrderCount, CompareAverageTransaction = row.CompareAverageTransaction,
        TotalAmountGrowth = row.TotalAmountGrowth, CompareGrossProfit = row.CompareGrossProfit, CompareGrossMarginRate = row.CompareGrossMarginRate,
    };
}
