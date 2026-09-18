using System.Text.Json;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 格式 2 的日级折扣快照队列。状态行是进度和 fencing 的唯一事实；快照仅在同一事务内随 Fresh 状态发布。
/// </summary>
internal sealed class BatchProductSalesDiscountDailyStore(ISqlSugarClient db)
{
    internal const int SnapshotFormat = 2;
    internal const int RuleVersion = 1;
    internal const int MaxAttempts = 5;
    internal const int SnapshotInsertBatchSize = 100;
    internal const string WaitingForCanonicalStatus = "WaitingCanonical";
    internal static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan CanonicalWaitDelay = TimeSpan.FromSeconds(30);
    // 已请求过的 canonical 重算仍然失败时，距该次失败满此间隔才再请求。对账不一致这类确定性失败
    // 每次重算结果都相同，立即重试只会每 3~4 分钟跑一次重型重算并与其他统计任务死锁。
    internal static readonly TimeSpan FailedCanonicalRetryDelay = TimeSpan.FromHours(6);

    internal bool SchemaReady
    {
        get
        {
            if (!db.DbMaintenance.IsAnyTable("BatchProductSalesDiscountSnapshot", false)
                || !db.DbMaintenance.IsAnyTable("BatchProductSalesDiscountRefreshState", false))
                return false;
            return db.DbMaintenance.GetColumnInfosByTableName("BatchProductSalesDiscountSnapshot", false)
                .Any(column => string.Equals(column.DbColumnName, "SnapshotFormat", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed record ClaimedDay(BatchProductSalesDiscountRefreshState State, string LeaseToken);

    internal async Task EnsureQueuedAsync(IEnumerable<DateTime> dates, DateTime nowUtc, CancellationToken token)
    {
        var requested = dates.Select(x => x.Date).Distinct().OrderBy(x => x).ToArray();
        if (requested.Length == 0) return;
        var existing = (await db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.Null)
            .Where(x => requested.Contains(x.Date)).Select(x => x.Date).ToListAsync()).ToHashSet();
        var missing = requested.Where(day => !existing.Contains(day)).Select(day => new BatchProductSalesDiscountRefreshState
        {
            Date = day,
            Status = "Queued",
            RuleVersion = RuleVersion,
            RequestedAtUtc = nowUtc,
            NextAttemptAtUtc = nowUtc,
        }).ToList();
        if (missing.Count == 0) return;
        token.ThrowIfCancellationRequested();
        try { await db.Insertable(missing).ExecuteCommandAsync(); }
        catch
        {
            // Date 是主键；并发调度发现同一缺口时，逐日确认已被另一实例插入。
            foreach (var state in missing)
                if (await GetAsync(state.Date) == null) throw;
        }
    }

    internal async Task<BatchProductSalesDiscountRefreshState?> GetAsync(DateTime date) =>
        await db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.Null)
            .Where(x => x.Date == date.Date).FirstAsync();

    // 保留供现有 SQL Server 队列测试和旧调用点使用的默认五分钟最近日期巡检节奏。
    internal Task<ClaimedDay?> ClaimNextAsync(DateTime nowUtc, IReadOnlyCollection<DateTime> preferredDates,
        CancellationToken token) => ClaimNextAsync(nowUtc, preferredDates, new DateTime(1753, 1, 1), new DateTime(9999, 12, 31),
            TimeSpan.FromMinutes(5), token);

    internal async Task<ClaimedDay?> ClaimNextAsync(DateTime nowUtc, IReadOnlyCollection<DateTime> preferredDates,
        DateTime coverageStart, DateTime coverageEnd, TimeSpan recentFreshCheckInterval, CancellationToken token)
    {
        coverageStart = coverageStart.Date;
        coverageEnd = coverageEnd.Date;
        // 崩溃后由租约到期恢复；不把旧 token 留给接管者。
        await db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.Status == "Queued")
            .SetColumns(x => x.LeaseToken == null)
            .SetColumns(x => x.LeaseUntilUtc == null)
            .SetColumns(x => x.NextAttemptAtUtc == nowUtc)
            .Where(x => x.Date >= coverageStart && x.Date <= coverageEnd && x.Status == "Running"
                && x.LeaseUntilUtc != null && x.LeaseUntilUtc <= nowUtc)
            .ExecuteCommandAsync();

        var preferred = preferredDates.Select(x => x.Date).Distinct().ToHashSet();
        var candidates = await db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.Null)
            .Where(x => x.Date >= coverageStart && x.Date <= coverageEnd
                && (((x.Status == "Queued" || x.Status == "Failed" || x.Status == WaitingForCanonicalStatus)
                        && x.NextAttemptAtUtc <= nowUtc)
                    || x.Status == "Fresh"))
            .ToListAsync();
        var candidate = OrderEligibleCandidates(candidates, nowUtc, preferred, coverageStart, coverageEnd,
            recentFreshCheckInterval).FirstOrDefault();
        if (candidate == null) return null;

        token.ThrowIfCancellationRequested();
        var leaseToken = Guid.NewGuid().ToString("N");
        var leaseUntil = nowUtc.Add(ExecutionLeaseDuration);
        var consumeFailureAttempt = ShouldConsumeFailureAttempt(candidate.Status);
        var update = db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.Status == "Running")
            .SetColumns(x => x.LeaseToken == leaseToken)
            .SetColumns(x => x.LeaseUntilUtc == leaseUntil)
            .SetColumns(x => x.LastError == null);
        if (consumeFailureAttempt)
            update = update.SetColumns(x => x.Attempts == x.Attempts + 1);
        var changed = await update
            .Where(x => x.Date == candidate.Date && x.Attempts == candidate.Attempts
                && (((x.Status == "Queued" || x.Status == "Failed" || x.Status == WaitingForCanonicalStatus)
                        && x.NextAttemptAtUtc <= nowUtc)
                    || x.Status == "Fresh"))
            .ExecuteCommandAsync();
        if (changed != 1) return null;

        candidate.LeaseToken = leaseToken;
        candidate.LeaseUntilUtc = leaseUntil;
        if (consumeFailureAttempt) candidate.Attempts++;
        return new(candidate, leaseToken);
    }

    internal static bool ShouldConsumeFailureAttempt(string status) =>
        !string.Equals(status, WaitingForCanonicalStatus, StringComparison.Ordinal);

    internal static int AttemptsAfterCanonicalWait(int claimedAttempts, string priorStatus) =>
        ShouldConsumeFailureAttempt(priorStatus) ? Math.Max(0, claimedAttempts - 1) : claimedAttempts;

    internal sealed record CanonicalReconciliationDecision(bool Request, DateTime NextAttemptAtUtc);

    /// <summary>
    /// 决定是否请求 canonical 日统计重算，以及折扣日下次何时再检查。
    /// 首次请求、状态缺失照旧立即请求；排队/运行中短间隔等待。
    /// 已请求过且 canonical 失败时，以失败时间（LastCheckedAtUtc）为起点退避，到期前既不重新入队也不短轮询。
    /// </summary>
    internal static CanonicalReconciliationDecision DecideCanonicalReconciliation(bool reconcileRequested,
        string? canonicalStatus, DateTime? canonicalCheckedAtUtc, DateTime nowUtc)
    {
        var shortWait = nowUtc.Add(CanonicalWaitDelay);
        if (!reconcileRequested || string.IsNullOrWhiteSpace(canonicalStatus)) return new(true, shortWait);
        if (!string.Equals(canonicalStatus, SalesStatisticRefreshStatus.Failed, StringComparison.OrdinalIgnoreCase))
            return new(false, shortWait);
        var retryAt = canonicalCheckedAtUtc?.Add(FailedCanonicalRetryDelay);
        return retryAt == null || retryAt <= nowUtc ? new(true, shortWait) : new(false, retryAt.Value);
    }

    /// <summary>最近业务日的已发布数据按短间隔刷新；历史回填/恢复从范围起点向后推进。</summary>
    internal static IReadOnlyList<BatchProductSalesDiscountRefreshState> OrderEligibleCandidates(
        IEnumerable<BatchProductSalesDiscountRefreshState> candidates, DateTime nowUtc,
        IReadOnlySet<DateTime> preferredDates, DateTime coverageStart, DateTime coverageEnd,
        TimeSpan recentFreshCheckInterval)
    {
        var recentCutoff = nowUtc - recentFreshCheckInterval;
        var historicalCutoff = nowUtc.AddDays(-1);
        return candidates.Where(state => state.Date >= coverageStart.Date && state.Date <= coverageEnd.Date
                && (state.Status is "Queued" or "Failed" or WaitingForCanonicalStatus
                || state.Status == "Fresh" && (!state.LastCheckedAtUtc.HasValue
                    || state.LastCheckedAtUtc <= (preferredDates.Contains(state.Date) ? recentCutoff : historicalCutoff))))
            // 最近已发布数据保持短间隔刷新；未完成的最近日期仍归入历史回填顺序，避免跳过范围起点。
            .OrderByDescending(state => state.Status == "Fresh" && preferredDates.Contains(state.Date))
            // Queued/Failed/WaitingCanonical 代表尚未完成的回填或恢复，必须先于非最近的历史 Fresh 巡检。
            .ThenBy(state => state.Status == "Fresh" ? 1 : 0)
            // 历史 Fresh 每天仅巡检一次；未巡检和最久未巡检的日期优先，日期只用于稳定排序。
            .ThenBy(state => state.Status == "Fresh" ? state.LastCheckedAtUtc ?? DateTime.MinValue : DateTime.MaxValue)
            // 回填/恢复从范围内最早日期向最新日期推进；Fresh 的日期仅在检查时间相同时升序稳定排序。
            .ThenBy(state => state.Status == "Fresh" ? DateTime.MaxValue : state.Date)
            .ThenBy(state => state.Status == "Fresh" ? state.Date : DateTime.MinValue)
            .ToList();
    }

    /// <summary>仅返回已到重试时间的历史待办，供 worker 决定短轮询还是空闲轮询。</summary>
    internal Task<bool> HasDueBackfillAsync(DateTime coverageStart, DateTime coverageEnd, DateTime nowUtc) =>
        db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.Null)
            .AnyAsync(x => x.Date >= coverageStart.Date && x.Date <= coverageEnd.Date
                && (x.Status == "Queued" || x.Status == "Failed" || x.Status == WaitingForCanonicalStatus)
                && x.NextAttemptAtUtc <= nowUtc);

    /// <summary>读取 canonical 日统计状态；失败行的 LastCheckedAtUtc 即失败时间，用于重算退避。</summary>
    internal async Task<(string? Status, DateTime? CheckedAtUtc)> ReadCanonicalStateAsync(DateTime date, CancellationToken token)
    {
        // 半开区间与其他日期读取一致：SqlSugar 在 SQLite 上的 DateTime 等值比较会格式错位而读不到行。
        var day = date.Date;
        var nextDay = day.AddDays(1);
        var state = await db.Queryable<SalesStatisticRefreshState>().With(SqlWith.Null)
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily && x.Date >= day && x.Date < nextDay)
            .Select(x => new { x.Status, x.LastCheckedAtUtc }).FirstAsync();
        token.ThrowIfCancellationRequested();
        return (state?.Status, state?.LastCheckedAtUtc);
    }

    internal async Task EnsureOwnershipAsync(ClaimedDay claim, DateTime nowUtc, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var changed = await db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.LeaseUntilUtc == nowUtc.Add(ExecutionLeaseDuration))
            .Where(x => x.Date == claim.State.Date && x.Status == "Running" && x.LeaseToken == claim.LeaseToken
                && x.LeaseUntilUtc != null && x.LeaseUntilUtc > nowUtc)
            .ExecuteCommandAsync();
        if (changed != 1)
            throw new InvalidOperationException($"折扣日任务执行权已变化，拒绝旧 worker 提交: {claim.State.Date:yyyy-MM-dd}");
    }

    internal async Task RecordCheckedAsync(ClaimedDay claim, string statisticsVersion, string sourceVersion,
        DateTime nowUtc, CancellationToken token)
    {
        await EnsureOwnershipAsync(claim, nowUtc, token);
        var changed = await db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.Status == "Fresh")
            .SetColumns(x => x.LastCheckedAtUtc == nowUtc)
            .SetColumns(x => x.Attempts == 0)
            .SetColumns(x => x.LeaseToken == null)
            .SetColumns(x => x.LeaseUntilUtc == null)
            .SetColumns(x => x.LastError == null)
            .Where(x => x.Date == claim.State.Date && x.Status == "Running" && x.LeaseToken == claim.LeaseToken)
            .ExecuteCommandAsync();
        if (changed != 1) throw new InvalidOperationException("折扣日任务检查完成时已失去执行权");
    }

    internal async Task PublishAsync(ClaimedDay claim, string statisticsVersion, string sourceVersion,
        IReadOnlyDictionary<string, List<BatchProductSalesAggregateRow>> payloadByProduct, DateTime nowUtc,
        CancellationToken token)
    {
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
            await EnsureOwnershipAsync(claim, nowUtc, token);
            var day = claim.State.Date.Date;
            var snapshots = payloadByProduct.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(pair => new BatchProductSalesDiscountSnapshot
            {
                Id = CreateDailyId(day, pair.Key),
                SnapshotFormat = SnapshotFormat,
                SourceVersion = sourceVersion,
                ProductCode = pair.Key.Trim().ToUpperInvariant(),
                StartDate = day,
                EndDate = day,
                StoreCodesJson = "[]",
                Status = "Fresh",
                Attempts = claim.State.Attempts,
                RequestedAtUtc = claim.State.RequestedAtUtc,
                NextAttemptAtUtc = nowUtc,
                CompletedAtUtc = nowUtc,
                PayloadJson = JsonSerializer.Serialize(pair.Value),
            }).ToList();

            await db.Ado.BeginTranAsync();
            try
            {
                token.ThrowIfCancellationRequested();
                var owner = await db.Queryable<BatchProductSalesDiscountRefreshState>().With(SqlWith.UpdLock)
                    .Where(x => x.Date == day && x.Status == "Running" && x.LeaseToken == claim.LeaseToken
                        && x.LeaseUntilUtc != null && x.LeaseUntilUtc > nowUtc).FirstAsync();
                if (owner == null) throw new InvalidOperationException("折扣日任务发布前已失去执行权");

                await db.Deleteable<BatchProductSalesDiscountSnapshot>()
                    .Where(x => x.SnapshotFormat == SnapshotFormat && x.StartDate >= day && x.StartDate < day.AddDays(1)
                        && x.EndDate >= day && x.EndDate < day.AddDays(1))
                    .ExecuteCommandAsync();
                // 单日可有上万商品及大 JSON；分批降低 SQL 构造/编译开销，但所有批次仍与 Fresh 状态同事务提交。
                foreach (var batch in snapshots.Chunk(SnapshotInsertBatchSize))
                {
                    token.ThrowIfCancellationRequested();
                    await db.Insertable(batch.ToList()).ExecuteCommandAsync();
                }
                token.ThrowIfCancellationRequested();
                var updated = await db.Updateable<BatchProductSalesDiscountRefreshState>()
                    .SetColumns(x => x.Status == "Fresh")
                    .SetColumns(x => x.Attempts == 0)
                    .SetColumns(x => x.RuleVersion == RuleVersion)
                    .SetColumns(x => x.StatisticsVersion == statisticsVersion)
                    .SetColumns(x => x.SourceVersion == sourceVersion)
                    .SetColumns(x => x.CompletedAtUtc == nowUtc)
                    .SetColumns(x => x.LastCheckedAtUtc == nowUtc)
                    .SetColumns(x => x.SnapshotCount == snapshots.Count)
                    .SetColumns(x => x.ReconcileRequested == false)
                    .SetColumns(x => x.LeaseToken == null)
                    .SetColumns(x => x.LeaseUntilUtc == null)
                    .SetColumns(x => x.LastError == null)
                    .Where(x => x.Date == day && x.Status == "Running" && x.LeaseToken == claim.LeaseToken)
                    .ExecuteCommandAsync();
                if (updated != 1) throw new InvalidOperationException("折扣日任务发布时已失去执行权");
                await db.Ado.CommitTranAsync();
            }
            catch (Exception original)
            {
                // 取消的业务令牌不能取消回滚；回滚失败也不能遮蔽首次数据库/取消异常。
                db.Ado.RemoveCancellationToken();
                try { await db.Ado.RollbackTranAsync(); }
                catch (Exception rollback) { original.Data["DiscountSnapshotRollbackError"] = rollback.Message; }
                throw;
            }
        }
        finally
        {
            if (previousToken.HasValue) db.Ado.CancellationToken = previousToken.Value;
            else db.Ado.RemoveCancellationToken();
        }

    }

    /// <summary>canonical 队列已受理时仅短暂等待，不消耗折扣计算的失败重试次数。</summary>
    internal async Task WaitForCanonicalRefreshAsync(ClaimedDay claim, DateTime nowUtc, CancellationToken token,
        string? diagnostic = null, DateTime? nextAttemptAtUtc = null)
    {
        await EnsureOwnershipAsync(claim, nowUtc, token);
        var attemptsAfterWait = AttemptsAfterCanonicalWait(claim.State.Attempts, claim.State.Status);
        var persistedDiagnostic = diagnostic is { Length: > 2000 } ? diagnostic[..2000] : diagnostic;
        // 退避时由调用方给出更晚的检查时间；不允许早于常规短等待，避免误传过去时间造成忙轮询。
        var nextAttempt = nextAttemptAtUtc is { } requested && requested > nowUtc.Add(CanonicalWaitDelay)
            ? requested : nowUtc.Add(CanonicalWaitDelay);
        var changed = await db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.Status == WaitingForCanonicalStatus)
            .SetColumns(x => x.NextAttemptAtUtc == nextAttempt)
            // Claim 阶段尚不知 canonical 是否可读；进入普通等待时归还本轮未发生计算的尝试计数。
            .SetColumns(x => x.Attempts == attemptsAfterWait)
            .SetColumns(x => x.ReconcileRequested == true)
            .SetColumns(x => x.LeaseToken == null)
            .SetColumns(x => x.LeaseUntilUtc == null)
            .SetColumns(x => x.LastError == persistedDiagnostic)
            .Where(x => x.Date == claim.State.Date && x.Status == "Running" && x.LeaseToken == claim.LeaseToken)
            .ExecuteCommandAsync();
        if (changed != 1) throw new InvalidOperationException("折扣日任务等待 canonical 时已失去执行权");
    }

    /// <summary>从 canonical 等待恢复后，真正读取成交源前才重新占用一次计算失败预算。</summary>
    internal async Task BeginComputationAsync(ClaimedDay claim, DateTime nowUtc, CancellationToken token)
    {
        if (!string.Equals(claim.State.Status, WaitingForCanonicalStatus, StringComparison.Ordinal)) return;
        await EnsureOwnershipAsync(claim, nowUtc, token);
        var changed = await db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.Attempts == x.Attempts + 1)
            .Where(x => x.Date == claim.State.Date && x.Status == "Running" && x.LeaseToken == claim.LeaseToken)
            .ExecuteCommandAsync();
        if (changed != 1) throw new InvalidOperationException("折扣日任务开始计算时已失去执行权");
        claim.State.Attempts++;
    }

    internal async Task FinishFailureAsync(ClaimedDay claim, string reason, DateTime nowUtc, bool reconcileRequested,
        CancellationToken token, bool preserveExistingReconcileRequested = true)
    {
        var previousToken = db.Ado.CancellationToken;
        db.Ado.CancellationToken = token;
        try
        {
            token.ThrowIfCancellationRequested();
            var attempts = claim.State.Attempts;
            var retryAt = attempts >= MaxAttempts
                ? nowUtc.AddHours(24)
                : nowUtc.AddMinutes(Math.Min(60, Math.Max(2, attempts * 3)));
            var persistedReason = reason.Length > 2000 ? reason.Substring(0, 2000) : reason;
            // SqlSugar 会把实体成员写进 SET 的表达式翻译。先在 C# 计算布尔值，避免它把
            // `ReconcileRequested OR @value` 当成赋值右侧 SQL 表达式，破坏失败状态的租约归还。
            var persistedReconcileRequested = reconcileRequested
                || (preserveExistingReconcileRequested && claim.State.ReconcileRequested);
            await db.Updateable<BatchProductSalesDiscountRefreshState>()
                .SetColumns(x => x.Status == "Failed")
                .SetColumns(x => x.NextAttemptAtUtc == retryAt)
                .SetColumns(x => x.LastError == persistedReason)
                // 一旦已成功请求 canonical 重算，后续围栏或超时失败不能把该事实清掉。
                .SetColumns(x => x.ReconcileRequested == persistedReconcileRequested)
                .SetColumns(x => x.LeaseToken == null)
                .SetColumns(x => x.LeaseUntilUtc == null)
                .Where(x => x.Date == claim.State.Date && x.Status == "Running" && x.LeaseToken == claim.LeaseToken)
                .ExecuteCommandAsync();
        }
        finally
        {
            if (previousToken.HasValue) db.Ado.CancellationToken = previousToken.Value;
            else db.Ado.RemoveCancellationToken();
        }

    }

    internal async Task<List<BatchProductSalesAggregateRow>> ReadDailyStatisticsAsync(DateTime date, CancellationToken token)
    {
        var day = date.Date;
        var rows = await db.Queryable<ProductStoreDailySalesStatistic>().With(SqlWith.Null)
            .Where(x => x.Date >= day && x.Date < day.AddDays(1))
            .GroupBy(x => new { x.Date, x.BranchCode, x.ProductCode })
            .Select(x => new BatchProductSalesAggregateRow
            {
                Date = x.Date,
                BranchCode = x.BranchCode,
                ProductCode = x.ProductCode,
                Quantity = SqlFunc.AggregateSum(x.TotalQuantity),
                UnknownQuantity = SqlFunc.AggregateSum(x.TotalQuantity),
                SalesAmount = SqlFunc.AggregateSum(x.TotalAmount),
            }).ToListAsync();
        token.ThrowIfCancellationRequested();
        return rows;
    }

    /// <summary>只对已发布的销量金额事实做排序哈希，刻意排除成本与刷新时间戳。</summary>
    internal async Task<string> ReadStatisticsVersionAsync(DateTime date, CancellationToken token)
    {
        var rows = await ReadDailyStatisticsAsync(date, token);
        var canonical = rows.OrderBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BranchCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                Date = x.Date.Date,
                ProductCode = x.ProductCode.Trim().ToUpperInvariant(),
                BranchCode = x.BranchCode.Trim().ToUpperInvariant(),
                x.Quantity,
                x.SalesAmount,
            });
        return BatchProductSalesStatisticReader.Hash(JsonSerializer.Serialize(canonical));
    }

    internal static string CreateDailyId(DateTime day, string productCode) => BatchProductSalesStatisticReader.Hash(
        JsonSerializer.Serialize(new { format = SnapshotFormat, day = day.Date, product = productCode.Trim().ToUpperInvariant() }));
}
