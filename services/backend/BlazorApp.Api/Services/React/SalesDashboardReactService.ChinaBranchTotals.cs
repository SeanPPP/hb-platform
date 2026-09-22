using BlazorApp.Api.Cache;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 移动端商品报告「分店中国货占比」的分子：按分店汇总全部中国供应商的销售额。
/// 口径与中国供应商排行（不带 supplierCode 时）一致：同一数据源、同一套 200/直写码识别、同一分店范围；
/// 区别只在同期——排行的同期只保留本期上榜的供应商，这里覆盖同期期间卖过货的全部中国供应商。
/// </summary>
public partial class SalesDashboardReactService
{
    private sealed class ChinaBranchMetrics
    {
        public decimal TotalAmount { get; set; }
        public int TotalQuantity { get; set; }
        public int SupplierCount { get; set; }
        public decimal GrossProfit { get; set; }
        public int StatisticRowCount { get; set; }
        public int CostedRowCount { get; set; }
        public int GrossProfitRowCount { get; set; }
    }

    public async Task<List<ChinaSupplierBranchTotalDto>> GetChinaSupplierBranchTotalsAsync(
        DateRangeDto dateRange,
        List<string>? branchCodes,
        ProductReportStatisticStatusDto statisticStatus
    )
    {
        try
        {
            ValidateDateRange(dateRange);
            if (!IsProductStatisticFresh(statisticStatus))
                return new List<ChinaSupplierBranchTotalDto>();

            // 显式空列表代表没有授权分店；统计查询构建器会把空列表当成“不过滤”，必须在这里先挡住。
            if (branchCodes != null && NormalizeCodes(branchCodes).Count == 0)
                return new List<ChinaSupplierBranchTotalDto>();

            if (_useSupplierRollups)
                return await GetChinaSupplierBranchTotalsFromRollupsAsync(dateRange, branchCodes, statisticStatus);

            var cacheKey = SalesDashboardCacheKeys.ChinaSupplierBranchTotals(
                dateRange,
                branchCodes,
                statisticStatus.CacheVersion
            );
            if (
                _cache.TryGetValue<List<ChinaSupplierBranchTotalDto>>(cacheKey, out var cachedResult)
                && cachedResult != null
                && cachedResult.Count != 0
            )
            {
                return cachedResult;
            }

            // 不指定供应商：读取全部中国商品映射，中国供应商集合含停用与软删除，与排行全量口径一致。
            var chinaProductMap = await GetChinaSupplierProductMapAsync(null);
            var targetChinaSupplierCodes = await GetChinaSupplierCodeSetAsync(chinaProductMap.Values);

            var currentRows = await LoadAllChinaSupplierBranchRowsAsync(
                dateRange.StartDate.Date,
                dateRange.EndDate.Date,
                branchCodes,
                chinaProductMap,
                targetChinaSupplierCodes
            );
            var compareRows = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue
                ? await LoadAllChinaSupplierBranchRowsAsync(
                    dateRange.CompareStartDate.Value.Date,
                    dateRange.CompareEndDate.Value.Date,
                    branchCodes,
                    chinaProductMap,
                    targetChinaSupplierCodes
                )
                : new List<SupplierBranchAggregateRow>();

            var branchNames = await GetStoreNameMapAsync(CollectBranchCodes(currentRows, compareRows));
            var result = BuildChinaSupplierBranchTotals(dateRange, currentRows, compareRows, branchNames);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                .SetSlidingExpiration(TimeSpan.FromMinutes(5));
            _cache.Set(cacheKey, result, cacheOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetChinaSupplierBranchTotalsAsync failed");
            throw;
        }
    }

    /// <summary>
    /// 读取一个期间内全部中国供应商的「供应商 × 分店」汇总行（旧日统计路径）。
    /// 与排行一致：200 行通过 POSM 映射还原为中国供应商，映射不到的 200 行不计入；
    /// 该期间商品统计一行都没有时，改读中国供应商独立统计表兜底。
    /// </summary>
    private async Task<List<SupplierBranchAggregateRow>> LoadAllChinaSupplierBranchRowsAsync(
        DateTime startDate,
        DateTime endDate,
        List<string>? branchCodes,
        Dictionary<string, string> chinaProductMap,
        HashSet<string> targetChinaSupplierCodes
    )
    {
        var query = await BuildProductReportStatisticQueryAsync(startDate, endDate, branchCodes);
        query = ApplyChinaSupplierStatisticFilter(
            query,
            targetChinaSupplierCodes,
            chinaProductMap,
            limitLegacy200Products: false
        );
        var rows = await query
            .GroupBy(s => new { s.ProductCode, s.BranchCode, s.SupplierCode })
            .Select(s => new ProductSupplierBranchAggregateRow
            {
                ProductCode = s.ProductCode,
                BranchCode = s.BranchCode,
                SupplierCode = s.SupplierCode,
                TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                GrossProfit = SqlFunc.AggregateSum(s.GrossProfit),
                StatisticRowCount = SqlFunc.AggregateCount(s.ProductCode),
                CostedRowCount = SqlFunc.AggregateCount(s.TotalCost),
                GrossProfitRowCount = SqlFunc.AggregateCount(s.GrossProfit),
            })
            .ToListAsync();

        if (rows.Any())
            return ResolveChinaSupplierBranchRows(rows, chinaProductMap, targetChinaSupplierCodes);

        return await QueryChinaSupplierStoreAggregateRowsAsync(
            startDate,
            endDate,
            branchCodes,
            targetChinaSupplierCodes
        );
    }

    private Task<List<ChinaSupplierBranchTotalDto>> GetChinaSupplierBranchTotalsFromRollupsAsync(
        DateRangeDto range,
        List<string>? branches,
        ProductReportStatisticStatusDto status
    ) =>
        ReadCompleteReportAsync(
            range,
            status,
            version => SalesDashboardCacheKeys.ChinaSupplierBranchTotals(range, branches, version),
            service => service.ReadChinaSupplierBranchTotalsFromRollupsAsync(range, branches),
            () => new List<ChinaSupplierBranchTotalDto>()
        );

    private async Task<List<ChinaSupplierBranchTotalDto>> ReadChinaSupplierBranchTotalsFromRollupsAsync(
        DateRangeDto range,
        List<string>? branches
    )
    {
        // 不能复用 ReadSupplierRollupMetricsAsync：它会把同期限制在本期出现的供应商，还带 topN 截断。
        var current = await QuerySupplierRollupRowsAsync(true, true, range.StartDate, range.EndDate, branches, null);
        var compare = range.CompareStartDate.HasValue && range.CompareEndDate.HasValue
            ? await QuerySupplierRollupRowsAsync(
                true,
                true,
                range.CompareStartDate.Value,
                range.CompareEndDate.Value,
                branches,
                null
            )
            : new List<SupplierRollupReadRow>();
        var currentRows = current.Select(ToSupplierBranchAggregateRow).ToList();
        var compareRows = compare.Select(ToSupplierBranchAggregateRow).ToList();
        var branchNames = await GetStoreNameMapAsync(CollectBranchCodes(currentRows, compareRows));
        return BuildChinaSupplierBranchTotals(range, currentRows, compareRows, branchNames);
    }

    private static SupplierBranchAggregateRow ToSupplierBranchAggregateRow(SupplierRollupReadRow row) => new()
    {
        SupplierCode = row.SupplierCode,
        BranchCode = row.BranchCode,
        TotalAmount = row.TotalAmount,
        TotalQuantity = row.TotalQuantity,
        OrderCount = row.OrderCount,
        GrossProfit = row.GrossProfit,
        StatisticRowCount = row.StatisticRowCount,
        CostedRowCount = row.CostedRowCount,
        GrossProfitRowCount = row.GrossProfitRowCount,
    };

    private static HashSet<string> CollectBranchCodes(params IEnumerable<SupplierBranchAggregateRow>[] rowSets) =>
        rowSets
            .SelectMany(rows => rows)
            .Select(row => row.BranchCode?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ChinaBranchMetrics> SumChinaRowsByBranch(IEnumerable<SupplierBranchAggregateRow> rows) =>
        rows
            .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode) && !string.IsNullOrWhiteSpace(row.SupplierCode))
            .GroupBy(row => row.BranchCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new ChinaBranchMetrics
                {
                    TotalAmount = group.Sum(row => row.TotalAmount),
                    TotalQuantity = group.Sum(row => row.TotalQuantity),
                    SupplierCount = group
                        .Select(row => row.SupplierCode.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    // SQL SUM 会忽略 NULL；毛利是否可信由三类行数是否对齐判断，这里先按 0 累加。
                    GrossProfit = group.Sum(row => row.GrossProfit ?? 0m),
                    StatisticRowCount = group.Sum(row => row.StatisticRowCount),
                    CostedRowCount = group.Sum(row => row.CostedRowCount),
                    GrossProfitRowCount = group.Sum(row => row.GrossProfitRowCount),
                },
                StringComparer.OrdinalIgnoreCase
            );

    private static List<ChinaSupplierBranchTotalDto> BuildChinaSupplierBranchTotals(
        DateRangeDto range,
        IEnumerable<SupplierBranchAggregateRow> currentRows,
        IEnumerable<SupplierBranchAggregateRow> compareRows,
        IReadOnlyDictionary<string, string> branchNames
    )
    {
        var hasCompare = range.CompareStartDate.HasValue && range.CompareEndDate.HasValue;
        var current = SumChinaRowsByBranch(currentRows);
        var compare = SumChinaRowsByBranch(compareRows);
        var empty = new ChinaBranchMetrics();

        // 只在同期卖过中国货的分店也要返回（本期为 0），否则分店同期合计会少算。
        return current.Keys
            .Union(compare.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(branchCode =>
            {
                var now = current.GetValueOrDefault(branchCode) ?? empty;
                var before = compare.GetValueOrDefault(branchCode) ?? empty;
                var grossProfit = GetCompleteGrossProfit(
                    now.GrossProfit,
                    now.StatisticRowCount,
                    now.CostedRowCount,
                    now.GrossProfitRowCount
                );
                var compareGrossProfit = hasCompare
                    ? GetCompleteGrossProfit(
                        before.GrossProfit,
                        before.StatisticRowCount,
                        before.CostedRowCount,
                        before.GrossProfitRowCount
                    )
                    : null;
                return new ChinaSupplierBranchTotalDto
                {
                    StartDate = range.StartDate.Date,
                    EndDate = range.EndDate.Date,
                    BranchCode = branchCode,
                    BranchName = branchNames.TryGetValue(branchCode, out var branchName) && !string.IsNullOrWhiteSpace(branchName)
                        ? branchName
                        : branchCode,
                    TotalAmount = now.TotalAmount,
                    TotalQuantity = now.TotalQuantity,
                    SupplierCount = now.SupplierCount,
                    GrossProfit = grossProfit,
                    GrossMarginRate = CalculateGrossMarginRate(now.TotalAmount, grossProfit),
                    CostStatus = GetCostStatus(now.StatisticRowCount, now.CostedRowCount, now.GrossProfitRowCount),
                    CompareTotalAmount = hasCompare ? before.TotalAmount : null,
                    CompareTotalQuantity = hasCompare ? before.TotalQuantity : null,
                    CompareGrossProfit = compareGrossProfit,
                    CompareGrossMarginRate = hasCompare
                        ? CalculateGrossMarginRate(before.TotalAmount, compareGrossProfit)
                        : null,
                    CompareCostStatus = hasCompare
                        ? GetCostStatus(before.StatisticRowCount, before.CostedRowCount, before.GrossProfitRowCount)
                        : NoActivityCostStatus,
                };
            })
            .OrderByDescending(row => row.TotalAmount)
            .ThenBy(row => row.BranchCode, StringComparer.Ordinal)
            .ToList();
    }
}
