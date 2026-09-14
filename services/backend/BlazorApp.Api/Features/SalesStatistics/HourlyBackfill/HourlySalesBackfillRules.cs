using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

internal enum HourlySalesBackfillSourceState
{
    Success,
    Empty,
    Unavailable,
}

internal sealed record HourlySalesBackfillSourceStatus(
    string Source,
    HourlySalesBackfillSourceState State,
    int RowCount,
    string? Watermark,
    string? ContentHash,
    string? Error = null,
    DateTime? ObservedAtUtc = null);

internal sealed record HourlySalesBackfillSourceRow(
    string Source,
    string BranchCode,
    int? Hour,
    string OrderId,
    decimal Amount,
    int Quantity,
    bool CountOrder,
    string? CrossSourceBusinessKey = null);

internal sealed record HourlySalesBackfillDailyTarget(
    decimal Amount, int Quantity, int OrderCount, string? BranchName = null);

internal sealed record HourlySalesBackfillIssue(string Code, string Message);

internal sealed record HourlySalesBackfillBuildResult(
    bool Valid,
    IReadOnlyList<HourlySalesStatistic> Rows,
    IReadOnlyList<HourlySalesBackfillIssue> Issues);

/// <summary>无数据库依赖的小时聚合与日统计对账规则。</summary>
internal static class HourlySalesBackfillRules
{
    internal const string Version = "hourly-posm-hbsales-v1";

    internal static HourlySalesBackfillBuildResult Build(
        DateTime date,
        IReadOnlyCollection<HourlySalesBackfillSourceRow> sourceRows,
        IReadOnlyDictionary<string, HourlySalesBackfillDailyTarget> dailyTargets,
        IReadOnlyCollection<string> requiredSources,
        IReadOnlyCollection<HourlySalesBackfillSourceStatus>? sourceStatuses)
    {
        var issues = new List<HourlySalesBackfillIssue>();
        var required = requiredSources.Select(source => source.Trim()).Where(source => source.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var statuses = sourceStatuses?.ToList() ?? [];
        var invalidContract = required.Count == 0
            || statuses.Any(status => !required.Contains(status.Source))
            || sourceRows.Any(row => !required.Contains(row.Source))
            || required.Any(source => statuses.Count(status => string.Equals(
                status.Source, source, StringComparison.OrdinalIgnoreCase)) != 1);
        foreach (var status in statuses.Where(status => required.Contains(status.Source)))
        {
            var actualCount = sourceRows.Count(row => string.Equals(
                row.Source, status.Source, StringComparison.OrdinalIgnoreCase));
            invalidContract |= status.RowCount != actualCount
                || string.IsNullOrWhiteSpace(status.Watermark)
                || string.IsNullOrWhiteSpace(status.ContentHash)
                || (status.State == HourlySalesBackfillSourceState.Empty && actualCount != 0)
                || (status.State == HourlySalesBackfillSourceState.Success && actualCount == 0);
        }
        if (invalidContract)
            issues.Add(new("source-contract", "必需来源状态缺失、重复或与实际行数不一致"));
        if (statuses.Any(status => status.State == HourlySalesBackfillSourceState.Unavailable))
            issues.Add(new("source-unavailable", "至少一个必需销售来源不可读取，不能把失败当作空销售"));

        if (sourceRows.Any(row => row.Hour is < 0 or > 23 or null))
            issues.Add(new("missing-hour", "销售明细缺少可验证的真实结账小时"));
        if (sourceRows.Any(row => string.IsNullOrWhiteSpace(row.BranchCode)))
            issues.Add(new("missing-branch", "销售明细缺少分店编码"));
        if (sourceRows.Any(row => row.CountOrder && string.IsNullOrWhiteSpace(row.OrderId)))
            issues.Add(new("missing-order", "用于订单计数的销售明细缺少订单键"));

        var crossingOrders = sourceRows
            .Where(row => row.CountOrder && row.Hour.HasValue && !string.IsNullOrWhiteSpace(row.OrderId))
            .GroupBy(row => $"{row.Source.Trim().ToUpperInvariant()}|{row.BranchCode.Trim().ToUpperInvariant()}|{row.OrderId}")
            .Any(group => group.Select(row => row.Hour!.Value).Distinct().Count() > 1);
        if (crossingOrders)
            issues.Add(new("order-crosses-hours", "同一来源订单跨越多个小时，无法确定唯一小时"));
        var crossingBranches = sourceRows
            .Where(row => row.CountOrder && !string.IsNullOrWhiteSpace(row.OrderId))
            .GroupBy(row => $"{row.Source.Trim().ToUpperInvariant()}|{row.OrderId}")
            .Any(group => group.Select(row => row.BranchCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (crossingBranches)
            issues.Add(new("order-crosses-branches", "同一来源订单映射到多个分店，无法安全计数"));

        var crossSourceOverlap = sourceRows
            .Where(row => !string.IsNullOrWhiteSpace(row.CrossSourceBusinessKey))
            .GroupBy(row => row.CrossSourceBusinessKey!, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Select(row => row.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (crossSourceOverlap)
            issues.Add(new("cross-source-overlap", "发现跨来源业务订单重叠，需人工裁决后才能回填"));

        if (issues.Count > 0)
            return new(false, [], issues);

        var branchRows = sourceRows
            .GroupBy(row => new { Branch = row.BranchCode.Trim(), Hour = row.Hour!.Value })
            .Select(group =>
            {
                var orders = group.Where(row => row.CountOrder)
                    .Select(row => $"{row.Source.Trim().ToUpperInvariant()}:{row.OrderId}")
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var amount = group.Sum(row => row.Amount);
                return new HourlySalesStatistic
                {
                    Date = date.Date,
                    Hour = group.Key.Hour,
                    BranchCode = group.Key.Branch,
                    BranchName = dailyTargets.TryGetValue(group.Key.Branch, out var target)
                        && !string.IsNullOrWhiteSpace(target.BranchName)
                            ? target.BranchName
                            : group.Key.Branch,
                    TotalAmount = amount,
                    TotalQuantity = group.Sum(row => row.Quantity),
                    OrderCount = orders,
                    CustomerCount = orders,
                    AverageOrderValue = orders == 0 ? 0m : amount / orders,
                };
            })
            .OrderBy(row => row.Hour).ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actualDaily = branchRows.GroupBy(row => row.BranchCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new HourlySalesBackfillDailyTarget(
                group.Sum(row => row.TotalAmount),
                group.Sum(row => row.TotalQuantity),
                group.Sum(row => row.OrderCount ?? 0)), StringComparer.OrdinalIgnoreCase);
        var allBranches = actualDaily.Keys.Union(dailyTargets.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var branch in allBranches)
        {
            actualDaily.TryGetValue(branch, out var actual);
            dailyTargets.TryGetValue(branch, out var expected);
            actual ??= new(0m, 0, 0);
            expected ??= new(0m, 0, 0);
            if (Math.Abs(actual.Amount - expected.Amount) > 0.01m
                || actual.Quantity != expected.Quantity
                || actual.OrderCount != expected.OrderCount)
                issues.Add(new("daily-reconciliation",
                    $"分店 {branch} 小时/日统计不一致: "
                    + $"amount {actual.Amount}/{expected.Amount}, "
                    + $"quantity {actual.Quantity}/{expected.Quantity}, "
                    + $"orders {actual.OrderCount}/{expected.OrderCount}"));
        }
        var allRows = branchRows.GroupBy(row => row.Hour).Select(group =>
        {
            var amount = group.Sum(row => row.TotalAmount);
            var orders = group.Sum(row => row.OrderCount ?? 0);
            return new HourlySalesStatistic
            {
                Date = date.Date,
                Hour = group.Key,
                BranchCode = "ALL",
                BranchName = "All Stores",
                TotalAmount = amount,
                TotalQuantity = group.Sum(row => row.TotalQuantity),
                OrderCount = orders,
                CustomerCount = orders,
                AverageOrderValue = orders == 0 ? 0m : amount / orders,
            };
        });
        var candidate = branchRows.Concat(allRows).OrderBy(row => row.Hour)
            .ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase).ToList();
        return new(issues.Count == 0, candidate, issues);
    }
}
