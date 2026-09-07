using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Services.React;

public partial class SalesDashboardReactService
{
    private sealed class RevenueSnapshotStoreRow
    {
        public DateTime Date { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int OrderCount { get; set; }
    }

    private sealed class RevenueSnapshotHourlyRow
    {
        public DateTime Date { get; set; }
        // SQL Server 批次可直接返回当前期(0)/同期(1)的预聚合行；SQLite/raw fallback 保持 null。
        public int? Period { get; set; }
        public int Hour { get; set; }
        public string? BranchCode { get; set; }
        public string? BranchName { get; set; }
        public decimal TotalAmount { get; set; }
        public int? OrderCount { get; set; }
    }

    private sealed class RevenueSnapshotHourlyCoverageRow
    {
        public DateTime Date { get; set; }
        public string BranchCode { get; set; } = string.Empty;
    }

    private sealed class RevenueSnapshotRefreshRow
    {
        public string StatisticType { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? LastAggregatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    private sealed class RevenueSnapshotStatus
    {
        public bool Complete { get; init; }
        public bool CurrentComplete { get; init; }
        public bool CompareComplete { get; init; }
        public bool CurrentHourlyComplete { get; init; }
        public bool CompareHourlyComplete { get; init; }
        public bool CompareStoreComplete { get; init; }
        public bool Refreshing { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public string Version { get; init; } = string.Empty;
    }

    private sealed class RevenueSnapshotMetric
    {
        public decimal Revenue { get; init; }
        public int Orders { get; init; }
        public decimal Aov => Orders > 0 ? Revenue / Orders : 0m;
    }

    /// <summary>
    /// 一次读取营业额排行、分时和周层级数据。
    /// 这里刻意不调用旧的 Ensure/Refresh 路径：报表读取只消费已发布的统计表，
    /// 刷新中的不完整行由上一版完整 bundle 兜底。
    /// </summary>
    public async Task<RevenueReportSnapshotDto> GetRevenueReportSnapshotAsync(
        DateRangeDto dateRange,
        List<string>? branchCodes = null,
        List<string>? focusBranchCodes = null,
        int? topN = null,
        CancellationToken cancellationToken = default
    )
    {
        ValidateDateRange(dateRange);
        var normalizedBranches = NormalizeBranchCodes(branchCodes);
        var normalizedFocusBranches = NormalizeBranchCodes(focusBranchCodes);
        if (branchCodes != null && normalizedBranches.Count == 0)
            return new RevenueReportSnapshotDto { StatisticStatus = SalesStatisticRefreshStatus.Fresh };
        if (focusBranchCodes != null && normalizedFocusBranches.Count == 0)
            return new RevenueReportSnapshotDto { StatisticStatus = SalesStatisticRefreshStatus.Fresh };

        var startDate = dateRange.StartDate.Date;
        var endDate = dateRange.EndDate.Date;
        var compareStartDate = dateRange.CompareStartDate?.Date;
        var compareEndDate = dateRange.CompareEndDate?.Date;
        var cacheKey = BuildRevenueSnapshotCacheKey(
            dateRange,
            normalizedBranches,
            normalizedFocusBranches,
            topN
        );
        var useSqlServerBatch = _context.Db.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer;

        async Task<RevenueReportSnapshotDto> ReadAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<RevenueSnapshotRefreshRow> refreshRows;
            List<RevenueSnapshotStoreRow> storeRows;
            List<RevenueSnapshotHourlyRow> hourlyRows;
            List<RevenueSnapshotHourlyCoverageRow> hourlyCoverageRows;
            Dictionary<string, string> activeStoreNames;
            if (useSqlServerBatch)
            {
                var batch = await ReadRevenueSnapshotBatchAsync(
                    dateRange,
                    normalizedBranches,
                    normalizedFocusBranches,
                    branchCodes == null,
                    cancellationToken
                );
                refreshRows = batch.RefreshRows;
                storeRows = batch.StoreRows;
                hourlyRows = batch.HourlyRows;
                hourlyCoverageRows = batch.HourlyCoverageRows;
                activeStoreNames = batch.ActiveStoreNames;
            }
            else
            {
                refreshRows = await ReadRevenueSnapshotRefreshRowsAsync(
                    startDate,
                    endDate,
                    compareStartDate,
                    compareEndDate,
                    cancellationToken
                );
                storeRows = new List<RevenueSnapshotStoreRow>();
                hourlyRows = new List<RevenueSnapshotHourlyRow>();
                hourlyCoverageRows = new List<RevenueSnapshotHourlyCoverageRow>();
                activeStoreNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            var status = BuildRevenueSnapshotStatus(
                refreshRows,
                startDate,
                endDate,
                compareStartDate,
                compareEndDate,
                null,
                storeRows,
                hourlyCoverageRows
            );

            if (_cache.TryGetValue<RevenueReportSnapshotDto>(cacheKey, out var cached)
                && cached != null
                && (!status.Complete || cached.CacheVersion == status.Version))
            {
                // 刷新中的旧 bundle 是已提交且完整的快照，不能把它降级为 Pending；
                // 只有当前没有旧 bundle 时才允许返回标记为 Pending 的当前行。
                if (!status.Complete)
                    return CloneRevenueSnapshot(cached, status);
                return cached;
            }

            if (branchCodes != null)
                activeStoreNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var displayBranchCodes = normalizedBranches.Count > 0
                ? normalizedBranches.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : activeStoreNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!useSqlServerBatch)
            {
                var hourlyScope = focusBranchCodes == null
                    ? normalizedBranches
                    : normalizedFocusBranches;
                if (branchCodes != null && focusBranchCodes != null)
                {
                    var authorized = normalizedBranches.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    hourlyScope = normalizedFocusBranches
                        .Where(authorized.Contains)
                        .ToList();
                }
                storeRows = await ReadRevenueStoreRowsAsync(
                    startDate,
                    endDate,
                    compareStartDate,
                    compareEndDate,
                    normalizedBranches,
                    cancellationToken
                );
                hourlyRows = hourlyScope.Count == 0 && focusBranchCodes != null
                    ? new List<RevenueSnapshotHourlyRow>()
                    : await ReadRevenueHourlyRowsAsync(
                        startDate,
                        endDate,
                        compareStartDate,
                        compareEndDate,
                        hourlyScope,
                        cancellationToken
                    );
                hourlyCoverageRows = hourlyRows
                    .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode))
                    .Select(row => new RevenueSnapshotHourlyCoverageRow
                    {
                        Date = row.Date,
                        BranchCode = row.BranchCode!.Trim(),
                    })
                    .GroupBy(row => row.Date.Date)
                    .SelectMany(dateGroup => dateGroup
                        .GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
                        .Select(branchGroup => branchGroup.First()))
                    .ToList();
            }

            status = BuildRevenueSnapshotStatus(
                refreshRows,
                startDate,
                endDate,
                compareStartDate,
                compareEndDate,
                focusBranchCodes == null ? null : normalizedFocusBranches,
                storeRows,
                hourlyCoverageRows
            );

            if (_cache.TryGetValue<RevenueReportSnapshotDto>(cacheKey, out cached)
                && cached != null
                && (!status.Complete || cached.CacheVersion == status.Version))
                return !status.Complete ? CloneRevenueSnapshot(cached, status) : cached;

            var branches = BuildRevenueBranches(
                storeRows,
                displayBranchCodes,
                activeStoreNames,
                startDate,
                endDate,
                compareStartDate,
                compareEndDate,
                topN
            );
            var hourly = BuildRevenueHourly(hourlyRows, startDate, endDate, compareStartDate, compareEndDate);
            var weeklyScope = focusBranchCodes != null
                ? normalizedFocusBranches.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : branchCodes != null
                    ? normalizedBranches.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : null;
            var weekly = BuildRevenueWeekly(
                storeRows
                    .Where(row => weeklyScope == null || weeklyScope.Contains(row.BranchCode))
                    .Select(row => new WeeklyPerformanceStatisticRow
                    {
                        Date = row.Date,
                        BranchCode = row.BranchCode,
                        BranchName = row.BranchName,
                        TotalAmount = row.TotalAmount,
                        OrderCount = row.OrderCount,
                        AverageOrderValue = row.OrderCount > 0 ? row.TotalAmount / row.OrderCount : 0m,
                    })
                    .ToList(),
                startDate,
                endDate,
                compareStartDate,
                compareEndDate
            );

            var result = new RevenueReportSnapshotDto
            {
                Branches = branches,
                Hourly = hourly,
                Weekly = weekly,
                StatisticsPending = !status.Complete,
                CurrentPeriodPending = !status.CurrentComplete,
                ComparePeriodPending = !status.CompareComplete,
                HourlyCurrentPending = !status.CurrentHourlyComplete,
                HourlyComparePending = !status.CompareHourlyComplete,
                WeeklyComparePending = !status.CompareStoreComplete,
                RefreshInProgress = status.Refreshing,
                StatisticStatus = status.Complete
                    ? SalesStatisticRefreshStatus.Fresh
                    : SalesStatisticRefreshStatus.Pending,
                StatisticMessage = status.Complete ? null : "统计快照尚未完成。",
                StatisticUpdatedAt = status.UpdatedAt,
                CacheVersion = status.Version,
                StatisticsExpectedBranchCount = displayBranchCodes.Count,
                StatisticsSnapshotBranchCount = branches.Count,
            };

            if (status.Complete)
            {
                _cache.Set(
                    cacheKey,
                    result,
                    new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                        .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                );
            }

            return result;
        }

        // SQL Server reader 自己在批次内完成能力选择和事务边界，避免再做一次
        // snapshot capability probe/BEGIN/COMMIT；SQLite 继续复用通用只读快照包装。
        return useSqlServerBatch
            ? await ReadAsync()
            : await ReadReportSnapshotAsync(ReadAsync);
    }

    private static string BuildRevenueSnapshotCacheKey(
        DateRangeDto dateRange,
        IReadOnlyCollection<string> branchCodes,
        IReadOnlyCollection<string> focusBranchCodes,
        int? topN
    )
    {
        static string ScopeKey(IReadOnlyCollection<string> codes) => codes.Count == 0
            ? "all"
            : string.Join(",", codes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase));

        return $"RevenueReportSnapshot_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_"
            + $"{dateRange.CompareStartDate:yyyyMMdd}_{dateRange.CompareEndDate:yyyyMMdd}_"
            + $"{dateRange.CompareMode}_{ScopeKey(branchCodes)}_{ScopeKey(focusBranchCodes)}_{topN?.ToString() ?? "all"}";
    }

    private async Task<List<RevenueSnapshotRefreshRow>> ReadRevenueSnapshotRefreshRowsAsync(
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasCompare = compareStartDate.HasValue && compareEndDate.HasValue;
        return await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                (state.StatisticType == SalesStatisticType.StoreSales
                 || state.StatisticType == SalesStatisticType.HourlySales)
                && ((state.Date >= startDate && state.Date <= endDate)
                    || (hasCompare
                        && state.Date >= compareStartDate!.Value
                        && state.Date <= compareEndDate!.Value)))
            .Select(state => new RevenueSnapshotRefreshRow
            {
                StatisticType = state.StatisticType,
                Date = state.Date,
                Status = state.Status,
                LastAggregatedAtUtc = state.LastAggregatedAtUtc,
                CompletedAtUtc = state.CompletedAtUtc,
            })
            .ToListAsync();
    }

    private static RevenueSnapshotStatus BuildRevenueSnapshotStatus(
        IReadOnlyCollection<RevenueSnapshotRefreshRow> rows,
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        IReadOnlyCollection<string>? hourlyBranchCodes,
        IReadOnlyCollection<RevenueSnapshotStoreRow>? storeRows,
        IReadOnlyCollection<RevenueSnapshotHourlyCoverageRow>? hourlyCoverageRows
    )
    {
        var currentDates = EnumerateRevenueSnapshotDates(startDate, endDate);
        var compareDates = compareStartDate.HasValue && compareEndDate.HasValue
            ? EnumerateRevenueSnapshotDates(compareStartDate.Value, compareEndDate.Value)
            : new List<DateTime>();
        var lookup = rows
            .GroupBy(row => (row.StatisticType, Date: row.Date.Date))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.LastAggregatedAtUtc).First());
        bool IsStoreComplete(DateTime date)
        {
            return lookup.TryGetValue((SalesStatisticType.StoreSales, date.Date), out var storeState)
                && string.Equals(storeState.Status, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase)
                && storeState.LastAggregatedAtUtc.HasValue
                && storeState.CompletedAtUtc.HasValue;
        }

        bool IsHourlyComplete(DateTime date)
        {
            var hourlyStateComplete = lookup.TryGetValue((SalesStatisticType.HourlySales, date.Date), out var hourlyState)
                && string.Equals(hourlyState.Status, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase)
                && hourlyState.LastAggregatedAtUtc.HasValue
                && hourlyState.CompletedAtUtc.HasValue;

            // Hourly 状态表是在历史 hourly 快照之后才引入的。已有分时行代表旧批次
            // 已提交；只有状态存在且明确处于 Running/Queued 时才必须等待新一版。
            var hourlyBusy = hourlyState != null && (
                string.Equals(hourlyState.Status, SalesStatisticRefreshStatus.Running, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hourlyState.Status, SalesStatisticRefreshStatus.Queued, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hourlyState.Status, SalesStatisticRefreshStatus.Pending, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hourlyState.Status, SalesStatisticRefreshStatus.Failed, StringComparison.OrdinalIgnoreCase));
            var expectedHourlyBranches = storeRows?
                .Where(row => row.Date.Date == date.Date
                    && (hourlyBranchCodes == null || hourlyBranchCodes.Contains(row.BranchCode)))
                .Select(row => row.BranchCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actualHourlyBranches = hourlyCoverageRows?
                .Where(row => row.Date.Date == date.Date && !string.IsNullOrWhiteSpace(row.BranchCode))
                .Select(row => row.BranchCode.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hourlyCoverageComplete = expectedHourlyBranches.SetEquals(actualHourlyBranches)
                && (expectedHourlyBranches.Count > 0 || actualHourlyBranches.Count == 0);
            if (hourlyBusy)
                return false;
            if (hourlyStateComplete)
                return hourlyCoverageComplete;
            // 历史 hourly 状态表缺失时，仅接受与 StoreSales 同日同分店集合完全一致的旧行。
            return hourlyCoverageComplete && actualHourlyBranches.Count > 0;
        }

        var currentStoreComplete = currentDates.All(IsStoreComplete);
        var currentHourlyComplete = currentDates.All(IsHourlyComplete);
        var compareStoreComplete = compareDates.Count == 0 || compareDates.All(IsStoreComplete);
        var compareHourlyComplete = compareDates.Count == 0 || compareDates.All(IsHourlyComplete);
        var complete = currentStoreComplete && currentHourlyComplete && compareStoreComplete;
        var refreshing = rows.Any(row =>
            string.Equals(row.Status, SalesStatisticRefreshStatus.Queued, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, SalesStatisticRefreshStatus.Running, StringComparison.OrdinalIgnoreCase));
        var updatedAt = rows
            .Select(row => row.CompletedAtUtc ?? row.LastAggregatedAtUtc)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();
        var versionSource = string.Join(
            "|",
            rows.OrderBy(row => row.Date).ThenBy(row => row.StatisticType).Select(row =>
                $"{row.StatisticType}:{row.Date:yyyyMMdd}:{row.Status}:{row.LastAggregatedAtUtc?.Ticks ?? 0}:{row.CompletedAtUtc?.Ticks ?? 0}")
        );

        return new RevenueSnapshotStatus
        {
            Complete = complete,
            CurrentComplete = currentStoreComplete && currentHourlyComplete,
            CompareComplete = compareStoreComplete && compareHourlyComplete,
            CurrentHourlyComplete = currentHourlyComplete,
            CompareHourlyComplete = compareHourlyComplete,
            CompareStoreComplete = compareStoreComplete,
            Refreshing = refreshing || !complete || !compareHourlyComplete,
            UpdatedAt = rows.Any(row => row.CompletedAtUtc.HasValue || row.LastAggregatedAtUtc.HasValue)
                ? DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)
                : null,
            Version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionSource))),
        };
    }

    private static List<DateTime> EnumerateRevenueSnapshotDates(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start > end)
            return new List<DateTime>();
        return Enumerable.Range(0, (end - start).Days + 1)
            .Select(offset => start.AddDays(offset))
            .ToList();
    }

    private async Task<List<RevenueSnapshotStoreRow>> ReadRevenueStoreRowsAsync(
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        IReadOnlyCollection<string> branchCodes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _context.Db.Queryable<StoreSalesStatistic>();
        query = compareStartDate.HasValue && compareEndDate.HasValue
            ? query.Where(row =>
                (row.Date >= startDate && row.Date <= endDate)
                || (row.Date >= compareStartDate.Value && row.Date <= compareEndDate.Value))
            : query.Where(row => row.Date >= startDate && row.Date <= endDate);
        if (branchCodes.Count > 0)
            query = query.Where(row => branchCodes.Contains(row.BranchCode));

        return await query.Select(row => new RevenueSnapshotStoreRow
        {
            Date = row.Date,
            BranchCode = row.BranchCode,
            BranchName = row.BranchName,
            TotalAmount = row.TotalAmount,
            OrderCount = row.OrderCount,
        }).ToListAsync();
    }

    private async Task<List<RevenueSnapshotHourlyRow>> ReadRevenueHourlyRowsAsync(
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        IReadOnlyCollection<string> branchCodes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _context.Db.Queryable<HourlySalesStatistic>()
            .Where(row => row.BranchCode != null && row.BranchCode != "ALL");
        query = compareStartDate.HasValue && compareEndDate.HasValue
            ? query.Where(row =>
                (row.Date >= startDate && row.Date <= endDate)
                || (row.Date >= compareStartDate.Value && row.Date <= compareEndDate.Value))
            : query.Where(row => row.Date >= startDate && row.Date <= endDate);
        if (branchCodes.Count > 0)
            query = query.Where(row => branchCodes.Contains(row.BranchCode!));

        return await query.Select(row => new RevenueSnapshotHourlyRow
        {
            Date = row.Date,
            Period = null,
            Hour = row.Hour,
            BranchCode = row.BranchCode,
            BranchName = row.BranchName,
            TotalAmount = row.TotalAmount,
            OrderCount = row.OrderCount ?? 0,
        }).ToListAsync();
    }

    private static List<ExecutiveBranchPerformanceDto> BuildRevenueBranches(
        IReadOnlyCollection<RevenueSnapshotStoreRow> rows,
        HashSet<string> displayBranchCodes,
        Dictionary<string, string> activeStoreNames,
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        int? topN
    )
    {
        var current = rows.Where(row => row.Date.Date >= startDate && row.Date.Date <= endDate)
            .GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new RevenueSnapshotMetric
            {
                Revenue = group.Sum(row => row.TotalAmount),
                Orders = group.Sum(row => row.OrderCount),
            }, StringComparer.OrdinalIgnoreCase);
        var compare = new Dictionary<string, RevenueSnapshotMetric>(StringComparer.OrdinalIgnoreCase);
        if (compareStartDate.HasValue && compareEndDate.HasValue)
        {
            compare = rows.Where(row => row.Date.Date >= compareStartDate.Value && row.Date.Date <= compareEndDate.Value)
                .GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => new RevenueSnapshotMetric
                {
                    Revenue = group.Sum(row => row.TotalAmount),
                    Orders = group.Sum(row => row.OrderCount),
                }, StringComparer.OrdinalIgnoreCase);
        }

        var codes = displayBranchCodes.Count > 0
            ? displayBranchCodes
            : current.Keys.Union(compare.Keys, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(activeStoreNames, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.BranchName) && !names.ContainsKey(row.BranchCode))
                names[row.BranchCode] = row.BranchName;
        }
        var result = codes.Select(code =>
        {
            current.TryGetValue(code, out var currentMetric);
            compare.TryGetValue(code, out var compareMetric);
            var name = names.GetValueOrDefault(code, code);
            return new ExecutiveBranchPerformanceDto
            {
                BranchCode = code,
                BranchName = string.IsNullOrWhiteSpace(name) ? code : name,
                Revenue = currentMetric?.Revenue ?? 0,
                RevenueLY = compareMetric?.Revenue ?? 0,
                OrderCount = currentMetric?.Orders ?? 0,
                OrderCountLY = compareMetric?.Orders ?? 0,
                Aov = currentMetric?.Aov ?? 0,
                AovLY = compareMetric?.Aov ?? 0,
            };
        }).OrderByDescending(item => item.Revenue)
            .ThenBy(item => item.BranchCode, StringComparer.OrdinalIgnoreCase)
            .Take(topN is > 0 ? topN.Value : int.MaxValue)
            .Select((item, index) =>
            {
                item.Rank = index + 1;
                return item;
            }).ToList();
        return result;
    }

    private static List<ExecutiveHourlyTrafficDto> BuildRevenueHourly(
        IReadOnlyCollection<RevenueSnapshotHourlyRow> rows,
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate
    )
    {
        static bool IsInPeriod(
            RevenueSnapshotHourlyRow row,
            int period,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime? compareStart,
            DateTime? compareEnd
        )
        {
            // 预聚合 SQL 行已经被 reader 标记为所属期；不要再用其代表日期做二次过滤。
            if (row.Period.HasValue)
                return row.Period.Value == period;
            var start = period == 0 ? periodStart : compareStart;
            var end = period == 0 ? periodEnd : compareEnd;
            return start.HasValue && end.HasValue
                && row.Date.Date >= start.Value.Date
                && row.Date.Date <= end.Value.Date;
        }

        var current = rows.Where(row => IsInPeriod(row, 0, startDate, endDate, compareStartDate, compareEndDate))
            .GroupBy(row => (BranchCode: row.BranchCode!.Trim().ToUpperInvariant(), row.Hour))
            .ToDictionary(group => group.Key, group => new RevenueSnapshotMetric
            {
                Revenue = group.Sum(row => row.TotalAmount),
                Orders = group.Sum(row => row.OrderCount ?? 0),
            });
        var compare = new Dictionary<(string BranchCode, int Hour), RevenueSnapshotMetric>();
        if (compareStartDate.HasValue && compareEndDate.HasValue)
        {
            compare = rows.Where(row => IsInPeriod(row, 1, startDate, endDate, compareStartDate, compareEndDate))
                .GroupBy(row => (BranchCode: row.BranchCode!.Trim().ToUpperInvariant(), row.Hour))
                .ToDictionary(group => group.Key, group => new RevenueSnapshotMetric
                {
                    Revenue = group.Sum(row => row.TotalAmount),
                    Orders = group.Sum(row => row.OrderCount ?? 0),
                });
        }
        var names = rows.Where(row => !string.IsNullOrWhiteSpace(row.BranchCode))
            .GroupBy(row => row.BranchCode!.Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.Select(row => row.BranchName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key);
        var keys = current.Keys.Union(compare.Keys).OrderBy(key => key.BranchCode).ThenBy(key => key.Hour).ToList();
        return keys.GroupBy(key => key.BranchCode).SelectMany(group =>
        {
            var max = group.Max(key => current.GetValueOrDefault(key)?.Revenue ?? 0m);
            return group.Select(key =>
            {
                current.TryGetValue(key, out var currentMetric);
                compare.TryGetValue(key, out var compareMetric);
                var revenue = currentMetric?.Revenue ?? 0;
                return new ExecutiveHourlyTrafficDto
                {
                    Hour = $"{key.Hour:D2}:00",
                    BranchCode = key.BranchCode,
                    BranchName = names.GetValueOrDefault(key.BranchCode, key.BranchCode),
                    Revenue = revenue,
                    RevenueLY = compareMetric?.Revenue ?? 0,
                    OrderCount = currentMetric?.Orders ?? 0,
                    OrderCountLY = compareMetric?.Orders ?? 0,
                    Percentage = max > 0 ? (int)(revenue * 100 / max) : 0,
                    IsPeak = max > 0 && revenue >= max * 0.8m,
                };
            });
        }).ToList();
    }

    private static List<WeeklyPerformanceHierarchyDto> BuildRevenueWeekly(
        IReadOnlyCollection<WeeklyPerformanceStatisticRow> rows,
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate
    )
    {
        var currentRows = rows.Where(row => row.Date.Date >= startDate && row.Date.Date <= endDate)
            .ToDictionary(row => (row.Date.Date, row.BranchCode.Trim().ToUpperInvariant()), row => row);
        var compareRows = rows.Where(row => compareStartDate.HasValue && compareEndDate.HasValue
                && row.Date.Date >= compareStartDate.Value && row.Date.Date <= compareEndDate.Value)
            .Select(row => new
            {
                Key = (startDate.AddDays((row.Date.Date - compareStartDate!.Value).Days), row.BranchCode.Trim().ToUpperInvariant()),
                Row = row,
            })
            .Where(item => item.Key.Item1 >= startDate && item.Key.Item1 <= endDate)
            .ToDictionary(item => item.Key, item => item.Row);

        var aligned = currentRows.Keys.Union(compareRows.Keys).Select(key =>
        {
            currentRows.TryGetValue(key, out var current);
            compareRows.TryGetValue(key, out var compare);
            return new WeeklyPerformanceAlignedRow
            {
                Date = key.Item1,
                BranchCode = current?.BranchCode ?? compare?.BranchCode ?? key.Item2,
                BranchName = current?.BranchName ?? compare?.BranchName ?? key.Item2,
                TotalAmount = current?.TotalAmount ?? 0,
                OrderCount = current?.OrderCount ?? 0,
                AverageOrderValue = current?.AverageOrderValue ?? 0,
                CompareTotalAmount = compare?.TotalAmount ?? 0,
                CompareOrderCount = compare?.OrderCount ?? 0,
                CompareAverageOrderValue = compare?.AverageOrderValue ?? 0,
            };
        }).ToList();

        var result = new List<WeeklyPerformanceHierarchyDto>();
        foreach (var weekGroup in aligned.GroupBy(row => new { Year = ISOWeek.GetYear(row.Date), Week = ISOWeek.GetWeekOfYear(row.Date) })
                     .OrderByDescending(group => group.Key.Year)
                     .ThenByDescending(group => group.Key.Week)
                     .ThenByDescending(group => group.Sum(row => row.TotalAmount)))
        {
            var weekKey = $"w{weekGroup.Key.Year}-{weekGroup.Key.Week:D2}";
            var week = new WeeklyPerformanceHierarchyDto
            {
                Key = weekKey,
                Level = "week",
                Hierarchy = $"{weekGroup.Key.Year}-W{weekGroup.Key.Week:D2}",
                Children = new List<WeeklyPerformanceHierarchyDto>(),
            };
            foreach (var branchGroup in weekGroup.GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(group => group.Sum(row => row.TotalAmount)))
            {
                var branch = new WeeklyPerformanceHierarchyDto
                {
                    Key = $"{weekKey}-{branchGroup.Key}",
                    Level = "branch",
                    Hierarchy = branchGroup.First().BranchName,
                    Children = branchGroup.OrderByDescending(row => row.Date).Select(row => new WeeklyPerformanceHierarchyDto
                    {
                        Key = $"{weekKey}-{branchGroup.Key}-{row.Date:yyyyMMdd}",
                        Level = "date",
                        Hierarchy = row.Date.ToString("yyyy-MM-dd"),
                        Revenue = row.TotalAmount,
                        RevenueLY = row.CompareTotalAmount,
                        Orders = row.OrderCount,
                        OrdersLY = row.CompareOrderCount,
                        Aov = row.AverageOrderValue,
                        AovLY = row.CompareAverageOrderValue,
                        YoYChange = row.CompareTotalAmount > 0
                            ? ((row.TotalAmount - row.CompareTotalAmount) / row.CompareTotalAmount) * 100
                            : null,
                    }).ToList(),
                };
                branch.Revenue = branch.Children!.Sum(child => child.Revenue);
                branch.RevenueLY = branch.Children.Sum(child => child.RevenueLY);
                branch.Orders = branch.Children.Sum(child => child.Orders);
                branch.OrdersLY = branch.Children.Sum(child => child.OrdersLY);
                branch.Aov = branch.Orders > 0 ? branch.Revenue / branch.Orders : 0;
                branch.AovLY = branch.OrdersLY > 0 ? branch.RevenueLY / branch.OrdersLY : 0;
                branch.YoYChange = branch.RevenueLY > 0
                    ? ((branch.Revenue - branch.RevenueLY) / branch.RevenueLY) * 100
                    : null;
                week.Children.Add(branch);
            }
            week.Revenue = week.Children.Sum(child => child.Revenue);
            week.RevenueLY = week.Children.Sum(child => child.RevenueLY);
            week.Orders = week.Children.Sum(child => child.Orders);
            week.OrdersLY = week.Children.Sum(child => child.OrdersLY);
            week.Aov = week.Orders > 0 ? week.Revenue / week.Orders : 0;
            week.AovLY = week.OrdersLY > 0 ? week.RevenueLY / week.OrdersLY : 0;
            week.YoYChange = week.RevenueLY > 0
                ? ((week.Revenue - week.RevenueLY) / week.RevenueLY) * 100
                : null;
            result.Add(week);
        }
        return result;
    }

    private static RevenueReportSnapshotDto CloneRevenueSnapshot(
        RevenueReportSnapshotDto source,
        RevenueSnapshotStatus refreshStatus
    ) => new()
    {
        Branches = source.Branches,
        Hourly = source.Hourly,
        Weekly = source.Weekly,
        StatisticsPending = false,
        CurrentPeriodPending = false,
        ComparePeriodPending = source.ComparePeriodPending,
        HourlyCurrentPending = false,
        HourlyComparePending = source.HourlyComparePending,
        WeeklyComparePending = source.WeeklyComparePending,
        RefreshInProgress = true,
        StatisticStatus = SalesStatisticRefreshStatus.Fresh,
        StatisticMessage = "后台正在发布下一版统计，当前显示最近完整快照。",
        StatisticUpdatedAt = source.StatisticUpdatedAt,
        CacheVersion = source.CacheVersion,
        StatisticsExpectedBranchCount = source.StatisticsExpectedBranchCount,
        StatisticsSnapshotBranchCount = source.StatisticsSnapshotBranchCount,
    };
}
