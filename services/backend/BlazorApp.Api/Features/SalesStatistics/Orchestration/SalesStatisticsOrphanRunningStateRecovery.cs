using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 回收完整刷新遗留的非商品统计孤儿 Running 行。
/// 这些类型只由完整刷新的 RunStep 写 Running：进程被杀时 catch 不会执行，
/// 失去日期会话锁时又会故意不写 Failed，因此需要旁路把确认无人执行的行改成 Failed，
/// 让统计对齐页亮红、提示补算。ProductStoreDaily 有自己的排队回收，这里不碰。
/// </summary>
internal sealed class SalesStatisticsOrphanRunningStateRecovery
{
    /// <summary>与完整刷新租约时长一致；超过它仍 Running 且无人持锁即视为执行者已消失。</summary>
    internal static readonly TimeSpan RunningTimeout = TimeSpan.FromHours(2);

    internal const string RecoveredErrorMessage =
        "完整刷新执行者已中断（进程终止或会话锁丢失），状态由后台自动回收；请在统计对齐页补算该日期。";

    private readonly ISqlSugarClient _db;
    private readonly ILogger _logger;

    internal SalesStatisticsOrphanRunningStateRecovery(ISqlSugarClient db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    internal async Task<int> RecoverAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - RunningTimeout;
        var candidates = await _db.Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                state.StatisticType != SalesStatisticType.ProductStoreDaily
                && state.Status == SalesStatisticRefreshStatus.Running
                && (state.StartedAtUtc <= cutoff
                    || (state.StartedAtUtc == null && state.LastCheckedAtUtc <= cutoff))
            )
            .Select(state => new { state.StatisticType, state.Date, state.StartedAtUtc })
            .ToListAsync();
        if (candidates.Count == 0)
            return 0;

        var dates = candidates.Select(row => row.Date.Date).Distinct().ToList();
        var busyDates = await FindBusyDatesAsync(dates, nowUtc);
        var recovered = 0;
        foreach (var candidate in candidates.OrderBy(row => row.Date).ThenBy(row => row.StatisticType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (busyDates == null || busyDates.Contains(candidate.Date.Date))
                continue;

            var statisticType = candidate.StatisticType;
            var dayStart = candidate.Date.Date;
            var nextDay = dayStart.AddDays(1);
            // 用"开始时间早于截止点"而非等值比较做条件更新：新执行者写 Running 时一定把
            // StartedAtUtc 设为当前时间，因此探测之后才开始的执行不会被本次更新误伤；
            // 也避开 SqlSugar 以 datetime 下发参数时与 datetime2 列等值比较的精度问题。
            var updated = await _db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(row => row.Status == SalesStatisticRefreshStatus.Failed)
                .SetColumns(row => row.ErrorMessage == RecoveredErrorMessage)
                .SetColumns(row => row.LastCheckedAtUtc == nowUtc)
                .Where(row =>
                    row.StatisticType == statisticType
                    && row.Date >= dayStart
                    && row.Date < nextDay
                    && row.Status == SalesStatisticRefreshStatus.Running
                    && (row.StartedAtUtc <= cutoff
                        || (row.StartedAtUtc == null && row.LastCheckedAtUtc <= cutoff))
                )
                .ExecuteCommandAsync();
            if (updated > 0)
            {
                recovered += updated;
                _logger.LogWarning(
                    "已回收孤儿统计状态: StatisticType={StatisticType}, Date={Date}, StartedAtUtc={StartedAtUtc}",
                    statisticType,
                    dayStart.ToString("yyyy-MM-dd"),
                    candidate.StartedAtUtc
                );
            }
        }

        return recovered;
    }

    /// <summary>
    /// 返回仍有执行者的日期；探测失败返回 null，本轮整体跳过。
    /// SQL Server 上完整刷新全程持有日期 session applock，这是最可靠的判活依据；
    /// 旧版 TTL 租约（滚动部署期间的旧实例）另按 LeaseUntilUtc 判断。
    /// sqlsess1 租约的 LeaseUntilUtc 是 9999 标记，崩溃后会一直留着，不能按 TTL 当作活跃。
    /// </summary>
    private async Task<HashSet<DateTime>?> FindBusyDatesAsync(List<DateTime> dates, DateTime nowUtc)
    {
        var busy = new HashSet<DateTime>();
        var scopeKeys = dates.ToDictionary(date => date.ToString("yyyy-MM-dd"), date => date);
        var keys = scopeKeys.Keys.ToList();
        var leases = await _db.Queryable<ScheduledTaskLease>()
            .Where(lease =>
                lease.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
                && keys.Contains(lease.ScopeKey)
                && lease.Status == ScheduledTaskLeaseStatus.Running
                && lease.LeaseUntilUtc != null
                && lease.LeaseUntilUtc > nowUtc
            )
            .Select(lease => new { lease.ScopeKey, lease.LeaseToken })
            .ToListAsync();
        foreach (var lease in leases)
        {
            var isSessionLease = lease.LeaseToken?.StartsWith(
                SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix,
                StringComparison.Ordinal) == true;
            if (!isSessionLease && scopeKeys.TryGetValue(lease.ScopeKey, out var date))
                busy.Add(date);
        }

        if (_db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            return busy;

        try
        {
            // 独立连接只读探测，不占用锁，不影响正在准备开始的完整刷新。
            await using var connection = new SqlConnection(_db.CurrentConnectionConfig.ConnectionString);
            await connection.OpenAsync();
            foreach (var (scopeKey, date) in scopeKeys)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT APPLOCK_TEST(N'public', @resource, N'Exclusive', N'Session');";
                command.Parameters.AddWithValue(
                    "@resource",
                    SalesStatisticsDateExecutionGuard.GetLockResource(scopeKey)
                );
                // APPLOCK_TEST 返回 0 表示当前拿不到锁，即有执行者持有该日期。
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
                    busy.Add(date);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "探测全日统计 SQL 会话锁失败，本轮不回收孤儿统计状态");
            return null;
        }

        return busy;
    }
}
