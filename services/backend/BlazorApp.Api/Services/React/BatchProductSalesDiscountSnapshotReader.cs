using System.Text.Json;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 只读取调度器发布的格式 2 日快照。查询端绝不补算、排队或扫描成交源。
/// </summary>
internal sealed class BatchProductSalesDiscountSnapshotReader(ISqlSugarClient db)
{
    private const int CurrentRuleVersion = 1;

    internal bool SchemaReady
    {
        get
        {
            if (!db.DbMaintenance.IsAnyTable("BatchProductSalesDiscountSnapshot", false)
                || !db.DbMaintenance.IsAnyTable("BatchProductSalesDiscountRefreshState", false))
                return false;

            try
            {
                // 已存在旧表而新增列尚未迁移时，查询端安全返回未知，不能发出带新列的 SQL。
                return db.DbMaintenance.GetColumnInfosByTableName("BatchProductSalesDiscountSnapshot", false)
                    .Any(column => string.Equals(column.DbColumnName, "SnapshotFormat", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }

    internal async Task<BatchProductSalesDiscountSnapshotReadResult> ReadAsync(
        string productCode,
        DateTime start,
        DateTime end,
        IReadOnlyList<string> stores,
        IReadOnlyList<BatchProductSalesAggregateRow> statistics,
        CancellationToken token)
    {
        if (!SchemaReady)
            return new(MarkUnknown(statistics, productCode, start, end, stores), "Unavailable", null);

        var endExclusive = end.Date.AddDays(1);
        // 每个表只做一个范围读取；绝不能按日期发出 N 次查询。
        var states = await db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.Null)
            .Where(s => s.Date >= start.Date && s.Date < endExclusive).ToListAsync();
        var snapshots = await db.Queryable<BatchProductSalesDiscountSnapshot>().With(SqlWith.Null)
            .Where(s => s.SnapshotFormat == 2 && s.ProductCode == productCode
                && s.StartDate >= start.Date && s.StartDate < endExclusive && s.EndDate == s.StartDate)
            .ToListAsync();
        token.ThrowIfCancellationRequested();

        var stateByDay = states.GroupBy(state => state.Date.Date).ToDictionary(group => group.Key, group => group.ToList());
        var snapshotByDay = snapshots.GroupBy(snapshot => snapshot.StartDate.Date).ToDictionary(group => group.Key, group => group.ToList());
        var statisticsByDay = statistics.GroupBy(row => row.Date.Date).ToDictionary(group => group.Key, group => group.ToList());
        var rows = new List<BatchProductSalesAggregateRow>();
        var outcomes = new List<BatchProductSalesDiscountDayOutcome>();
        var usedUpdatedAt = new List<DateTime>();

        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var dayStatistics = statisticsByDay.GetValueOrDefault(day) ?? [];
            var dayStates = stateByDay.GetValueOrDefault(day) ?? [];
            var daySnapshots = snapshotByDay.GetValueOrDefault(day) ?? [];
            if (dayStates.Count != 1)
            {
                rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                outcomes.Add(BatchProductSalesDiscountDayOutcome.Backfilling);
                continue;
            }

            var state = dayStates[0];
            if (!HasCommittedGeneration(state))
            {
                rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                outcomes.Add(state.RuleVersion != CurrentRuleVersion
                    ? BatchProductSalesDiscountDayOutcome.OutOfSync
                    : IsRetrying(state.Status)
                        ? BatchProductSalesDiscountDayOutcome.Backfilling
                        : BatchProductSalesDiscountDayOutcome.Unavailable);
                continue;
            }

            // 调度器不会为无成交商品写格式 2 行。已有完成代际且当前授权范围无统计行即为零日；
            // SnapshotCount 是全天所有商品数量，不能用其他商品的活动把当前商品误判为缺失。
            if (daySnapshots.Count == 0)
            {
                if (dayStatistics.Count == 0)
                    outcomes.Add(IsFresh(state.Status)
                        ? BatchProductSalesDiscountDayOutcome.Fresh
                        : BatchProductSalesDiscountDayOutcome.Refreshing);
                else
                {
                    rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                    outcomes.Add(BatchProductSalesDiscountDayOutcome.OutOfSync);
                }
                continue;
            }

            if (daySnapshots.Count != 1 || !IsUsable(daySnapshots[0], state))
            {
                rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                outcomes.Add(IsRetrying(daySnapshots[0].Status)
                    ? BatchProductSalesDiscountDayOutcome.Backfilling
                    : BatchProductSalesDiscountDayOutcome.OutOfSync);
                continue;
            }

            try
            {
                var snapshotRows = JsonSerializer.Deserialize<List<BatchProductSalesAggregateRow?>>(daySnapshots[0].PayloadJson!) ?? [];
                if (snapshotRows.Any(row => !IsContractRow(row, day, productCode)))
                    throw new JsonException("折扣日快照的聚合行不符合发布契约。");
                var authorizedRows = snapshotRows
                    .Where(row => row is not null && stores.Contains(row.BranchCode, StringComparer.OrdinalIgnoreCase))
                    .Select(row => row!)
                    .ToList();
                // 对当前授权范围逐日逐店核对；未授权分店从不参与响应或校验。
                if (!BatchProductSalesStatisticReader.TotalsMatch(dayStatistics, authorizedRows))
                {
                    rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                    outcomes.Add(BatchProductSalesDiscountDayOutcome.OutOfSync);
                    continue;
                }
                rows.AddRange(authorizedRows);
                usedUpdatedAt.Add(daySnapshots[0].CompletedAtUtc!.Value);
                // 巡检会暂时把 Fresh 状态改为 Running；同源已发布快照经当前授权统计核验后仍可安全展示。
                outcomes.Add(IsFresh(state.Status)
                    ? BatchProductSalesDiscountDayOutcome.Fresh
                    : BatchProductSalesDiscountDayOutcome.Refreshing);
            }
            catch (JsonException)
            {
                rows.AddRange(MarkUnknown(dayStatistics, productCode, day, day, stores));
                outcomes.Add(BatchProductSalesDiscountDayOutcome.OutOfSync);
            }
        }

        var status = outcomes.All(outcome => outcome == BatchProductSalesDiscountDayOutcome.Fresh) ? "Fresh"
            : outcomes.All(outcome => outcome is BatchProductSalesDiscountDayOutcome.Fresh or BatchProductSalesDiscountDayOutcome.Refreshing)
                && outcomes.Any(outcome => outcome == BatchProductSalesDiscountDayOutcome.Refreshing) ? "Refreshing"
            : outcomes.Any(outcome => outcome is BatchProductSalesDiscountDayOutcome.Fresh or BatchProductSalesDiscountDayOutcome.Refreshing) ? "Partial"
            : outcomes.Any(outcome => outcome == BatchProductSalesDiscountDayOutcome.OutOfSync) ? "OutOfSync"
            : outcomes.Any(outcome => outcome == BatchProductSalesDiscountDayOutcome.Backfilling) ? "Backfilling"
            : "Unavailable";
        // 更新时间只取已经解析且通过当前授权范围逐店核对的快照，失配行不能误导使用者。
        DateTime? updatedAt = usedUpdatedAt.Count == 0 ? null : usedUpdatedAt.Max();
        return new(rows, status, updatedAt);
    }

    private static bool HasCommittedGeneration(BatchProductSalesDiscountRefreshState state) =>
        state.RuleVersion == CurrentRuleVersion
        && state.CompletedAtUtc.HasValue
        && !string.IsNullOrWhiteSpace(state.SourceVersion)
        && !string.IsNullOrWhiteSpace(state.StatisticsVersion);

    private static bool IsUsable(BatchProductSalesDiscountSnapshot snapshot, BatchProductSalesDiscountRefreshState state) =>
        snapshot.SnapshotFormat == 2
        && snapshot.StartDate.Date == snapshot.EndDate.Date
        && snapshot.CompletedAtUtc.HasValue
        && !string.IsNullOrWhiteSpace(snapshot.PayloadJson)
        && !string.IsNullOrWhiteSpace(snapshot.SourceVersion)
        && !string.IsNullOrWhiteSpace(state.SourceVersion)
        && string.Equals(snapshot.Status, "Fresh", StringComparison.OrdinalIgnoreCase)
        && string.Equals(snapshot.SourceVersion, state.SourceVersion, StringComparison.Ordinal);

    private static bool IsFresh(string? status) => string.Equals(status, "Fresh", StringComparison.OrdinalIgnoreCase);

    private static bool IsContractRow(BatchProductSalesAggregateRow? row, DateTime day, string productCode) =>
        row is not null
        && row.Date.Date == day
        && string.Equals(row.ProductCode, productCode, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(row.BranchCode)
        && row.RegularQuantity + row.DiscountQuantity + row.UnknownQuantity == row.Quantity;

    private static bool IsRetrying(string? status) => string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Backfilling", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus, StringComparison.OrdinalIgnoreCase);

    internal static List<BatchProductSalesAggregateRow> MarkUnknown(
        IReadOnlyList<BatchProductSalesAggregateRow> statistics,
        string productCode,
        DateTime start,
        DateTime end,
        IReadOnlyList<string> stores)
    {
        var authorizedStores = stores.Where(store => !string.IsNullOrWhiteSpace(store))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rows = statistics
            // 即使调用者失误传入范围外统计行，也不能把它放进当前授权响应。
            .Where(row => authorizedStores.Contains(row.BranchCode, StringComparer.OrdinalIgnoreCase))
            .Select(row => new BatchProductSalesAggregateRow
        {
            Date = row.Date.Date, BranchCode = row.BranchCode, ProductCode = row.ProductCode,
            Quantity = row.Quantity, SalesAmount = row.SalesAmount,
            UnknownQuantity = row.Quantity, UnknownRowCount = Math.Max(1, row.UnknownRowCount),
        }).ToList();
        // 未完成或失配时，每个日期、每个授权分店都需要未知标记：只有部分分店有日统计也不能把其余分店补成完成零日。
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
            foreach (var store in authorizedStores)
                if (!rows.Any(row => row.Date.Date == day
                    && string.Equals(row.BranchCode, store, StringComparison.OrdinalIgnoreCase)))
                    rows.Add(new BatchProductSalesAggregateRow
                {
                    Date = day, BranchCode = store, ProductCode = productCode, UnknownRowCount = 1,
                });
        return rows;
    }
}

internal sealed record BatchProductSalesDiscountSnapshotReadResult(
    List<BatchProductSalesAggregateRow> Rows,
    string Status,
    DateTime? UpdatedAt);

internal enum BatchProductSalesDiscountDayOutcome
{
    Fresh,
    Refreshing,
    Backfilling,
    OutOfSync,
    Unavailable,
}
