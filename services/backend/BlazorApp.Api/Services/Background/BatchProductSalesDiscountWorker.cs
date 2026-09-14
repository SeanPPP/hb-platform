using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BlazorApp.Api.Services.Background;

/// <summary>只由当前调度实例驱动的格式 2 折扣日快照；请求线程不创建工作项，也不读取成交源。</summary>
public sealed class BatchProductSalesDiscountWorker(
    IServiceScopeFactory scopes,
    IOptions<ScheduledTaskOptions> options,
    ILogger<BatchProductSalesDiscountWorker> logger) : BackgroundService
{
    private const string GlobalLeaseTaskType = nameof(BatchProductSalesDiscountWorker);
    private const string GlobalLeaseScope = "daily-format-2";
    private static readonly TimeSpan GlobalLeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunBudget = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan SourceReadLimit = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan BackfillPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = IdlePollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { interval = await RunOnceAsync(stoppingToken) ? BackfillPollInterval : IdlePollInterval; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "商品折扣日快照后台任务暂不可用"); }
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        if (!await services.GetRequiredService<ScheduledTaskRuntimeControlService>().IsCurrentInstanceSchedulerEnabledAsync()) return false;
        var db = services.GetRequiredService<SqlSugarContext>().Db;
        var store = new BatchProductSalesDiscountDailyStore(db);
        if (!store.SchemaReady)
        {
            logger.LogWarning("折扣日快照表尚未完成部署，后台任务跳过；请先运行版本化 schema migration");
            return false;
        }
        var leases = services.GetRequiredService<ScheduledTaskLeaseService>();
        var global = await leases.TryAcquireAsync(GlobalLeaseTaskType, GlobalLeaseScope, GlobalLeaseDuration);
        if (!global.Acquired || string.IsNullOrWhiteSpace(global.Lease?.LeaseToken)) return false;
        var globalToken = global.Lease.LeaseToken;
        var success = false;
        try
        {
            var today = SalesStatisticsBusinessDate.GetBusinessDate(DateTimeOffset.UtcNow);
            // SQL Server 单个 IN 参数列表有限制；五年仍明显高于默认两年且保持一次缺口扫描可执行。
            var historicalYears = Math.Clamp(options.Value.DiscountSnapshotHistoricalYears, 1, 5);
            var coverage = BuildCoverageDays(today, historicalYears);
            var coverageStart = coverage[0];
            var coverageEnd = coverage[^1];
            var recentDays = Math.Clamp(options.Value.DiscountSnapshotRecentDays, 1, 14);
            var preferred = Enumerable.Range(0, recentDays).Select(offset => today.AddDays(-offset)).ToArray();
            var deadline = DateTime.UtcNow.Add(RunBudget);
            using var runBudget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            runBudget.CancelAfter(RunBudget);
            try
            {
                var nowUtc = DateTime.UtcNow;
                await store.EnsureQueuedAsync(coverage, nowUtc, runBudget.Token);
                await leases.EnsureActiveAsync(GlobalLeaseTaskType, GlobalLeaseScope, globalToken, GlobalLeaseDuration, "折扣日快照持久队列");
                var recentCheckInterval = TimeSpan.FromMinutes(Math.Max(1, options.Value.DiscountSnapshotRecentCheckMinutes));
                var batchSize = Math.Clamp(options.Value.DiscountSnapshotBatchSize, 1, 48);
                for (var completed = 0; completed < batchSize && DateTime.UtcNow < deadline; completed++)
                {
                    await leases.EnsureActiveAsync(GlobalLeaseTaskType, GlobalLeaseScope, globalToken, GlobalLeaseDuration, "折扣日快照持久队列");
                    var claim = await store.ClaimNextAsync(DateTime.UtcNow, preferred, coverageStart, coverageEnd,
                        recentCheckInterval, runBudget.Token);
                    if (claim == null) break;
                    await ExecuteClaimAsync(services, store, leases, globalToken, claim, runBudget.Token);
                }
            }
            catch (OperationCanceledException) when (runBudget.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                // 单轮预算耗尽不是完成；下一轮短间隔续跑，并由租约继续保护当前实例。
            }
            success = true;
            return runBudget.IsCancellationRequested || await store.HasDueBackfillAsync(coverageStart, coverageEnd, DateTime.UtcNow);
        }
        finally { await leases.CompleteAsync(GlobalLeaseTaskType, GlobalLeaseScope, globalToken, success); }
    }

    private async Task ExecuteClaimAsync(IServiceProvider services, BatchProductSalesDiscountDailyStore store,
        ScheduledTaskLeaseService leases, string globalToken, BatchProductSalesDiscountDailyStore.ClaimedDay claim,
        CancellationToken stoppingToken)
    {
        var day = claim.State.Date.Date;
        try
        {
            await ValidateOwnershipAsync(store, leases, globalToken, claim, stoppingToken);
            var canonical = new BatchProductSalesStatisticReader(services.GetRequiredService<SqlSugarContext>().Db);
            var canonicalStatus = await canonical.StatusAsync(day, day, stoppingToken);
            if (!canonicalStatus.IsFresh)
            {
                var stateStatus = await store.ReadCanonicalStatusAsync(day, stoppingToken);
                if (BatchProductSalesDiscountDailyStore.ShouldRequestCanonicalReconciliation(
                        claim.State.ReconcileRequested, stateStatus))
                    await RequestCanonicalReconciliationAsync(services, day, stoppingToken);
                await store.WaitForCanonicalRefreshAsync(claim, DateTime.UtcNow, CancellationToken.None);
                return;
            }
            await store.BeginComputationAsync(claim, DateTime.UtcNow, stoppingToken);
            var statisticsBefore = await store.ReadStatisticsVersionAsync(day, stoppingToken);
            var source = new BatchProductSalesDiscountSnapshotSourceReader(
                services.GetRequiredService<SqlSugarContext>().Db,
                services.GetRequiredService<POSMSqlSugarContext>().Db,
                services.GetRequiredService<HBSalesRecordSqlSugarContext>().Db);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(SourceReadLimit);
            // 同一个准备结果既用于变更检查，也用于聚合，完整来源扫描保持前后各一次。
            var prepared = await source.CapturePreparedAsync(day, timeout.Token);
            var sourceBefore = prepared.SourceVersion;
            if (claim.State.Status == "Fresh" && !claim.State.ReconcileRequested
                && claim.State.RuleVersion == BatchProductSalesDiscountDailyStore.RuleVersion
                && claim.State.StatisticsVersion == statisticsBefore && claim.State.SourceVersion == sourceBefore)
            {
                await store.RecordCheckedAsync(claim, statisticsBefore, sourceBefore, DateTime.UtcNow, stoppingToken);
                return;
            }
            await ValidateOwnershipAsync(store, leases, globalToken, claim, stoppingToken);
            var readResult = await source.ReadPreparedDayAsync(prepared, timeout.Token);
            var sourceRows = readResult.Rows;
            var statisticRows = await store.ReadDailyStatisticsAsync(day, stoppingToken);
            var sourceAfter = readResult.SourceVersion;
            var statisticsAfter = await store.ReadStatisticsVersionAsync(day, stoppingToken);
            if (sourceAfter != sourceBefore || statisticsAfter != statisticsBefore)
            {
                await store.FinishFailureAsync(claim, "读取期间成交源或销售日统计发生变化，等待下次围栏重试", DateTime.UtcNow, false, stoppingToken);
                return;
            }
            var payload = BuildPayload(day, statisticRows, sourceRows, out var mismatches);
            if (mismatches.Count > 0)
            {
                var mismatchDiagnostic = FormatMismatchDiagnostic(mismatches);
                logger.LogWarning("商品折扣日快照聚合不一致: {Date}, {@Mismatches}", day, mismatches);
                if (claim.State.ReconcileRequested)
                {
                    // 已请求的 canonical 已发布但数值仍不一致，记为真正计算失败；下次退避重试可重新入队。
                    await store.FinishFailureAsync(claim, mismatchDiagnostic,
                        DateTime.UtcNow, false, CancellationToken.None, preserveExistingReconcileRequested: false);
                    return;
                }
                if (BatchProductSalesDiscountDailyStore.ShouldRequestCanonicalReconciliation(claim.State.ReconcileRequested, null))
                    await RequestCanonicalReconciliationAsync(services, day, stoppingToken);
                await store.WaitForCanonicalRefreshAsync(claim, DateTime.UtcNow, CancellationToken.None, mismatchDiagnostic);
                return;
            }
            await ValidateOwnershipAsync(store, leases, globalToken, claim, stoppingToken);
            await store.PublishAsync(claim, statisticsAfter, sourceAfter, payload, DateTime.UtcNow, stoppingToken);
            logger.LogInformation("商品折扣日快照已发布: {Date}, Products={ProductCount}", day, payload.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await store.FinishFailureAsync(claim, "服务停止，等待租约恢复", DateTime.UtcNow, false, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // 先记录原始错误，避免状态持久化异常遮蔽真正的计算失败原因。
            logger.LogWarning(ex, "商品折扣日快照失败: {Date}, Attempt={Attempts}", day, claim.State.Attempts);
            await store.FinishFailureAsync(claim, ex.Message, DateTime.UtcNow, false, CancellationToken.None);
        }
    }

    internal sealed record BatchProductSalesDiscountMismatch(string ProductCode, string StoreCode,
        decimal ExpectedQuantity, decimal SourceQuantity, decimal ExpectedAmount, decimal SourceAmount);

    internal static Dictionary<string, List<BatchProductSalesAggregateRow>> BuildPayload(DateTime day,
        IReadOnlyCollection<BatchProductSalesAggregateRow> statistics, IReadOnlyCollection<BatchProductSalesAggregateRow> source,
        out IReadOnlyList<BatchProductSalesDiscountMismatch> mismatches)
    {
        var expected = statistics.GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var actual = source.GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<BatchProductSalesAggregateRow>>(StringComparer.OrdinalIgnoreCase);
        var diagnosticRows = new List<BatchProductSalesDiscountMismatch>(capacity: 5);
        foreach (var product in expected.Keys.Union(actual.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(product => product, StringComparer.OrdinalIgnoreCase))
        {
            var statisticRows = expected.GetValueOrDefault(product) ?? [];
            var sourceRows = actual.GetValueOrDefault(product) ?? [];
            if (!BatchProductSalesStatisticReader.TotalsMatch(statisticRows, sourceRows))
            {
                diagnosticRows.AddRange(BuildMismatchDiagnostics(product, statisticRows, sourceRows,
                    remaining: 5 - diagnosticRows.Count));
                continue;
            }
            result[product] = BatchProductSalesStatisticReader.PrepareSnapshot(statisticRows, sourceRows)
                .Where(x => x.Date.Date == day).ToList();
        }
        mismatches = diagnosticRows;
        return result;
    }

    private static IReadOnlyList<BatchProductSalesDiscountMismatch> BuildMismatchDiagnostics(string product,
        IEnumerable<BatchProductSalesAggregateRow> statistics, IEnumerable<BatchProductSalesAggregateRow> source, int remaining)
    {
        static Dictionary<(DateTime Date, string Store), (decimal Quantity, decimal Amount)> Totals(
            IEnumerable<BatchProductSalesAggregateRow> rows) => rows
                .GroupBy(row => (row.Date.Date, row.BranchCode.ToUpperInvariant()))
                .ToDictionary(group => group.Key, group => (group.Sum(row => row.Quantity), group.Sum(row => row.SalesAmount)));

        var expected = Totals(statistics);
        var actual = Totals(source);
        return expected.Keys.Union(actual.Keys).OrderBy(key => key.Date).ThenBy(key => key.Store, StringComparer.Ordinal)
            .Select(key => (Key: key, Expected: expected.GetValueOrDefault(key), Source: actual.GetValueOrDefault(key)))
            // 与 TotalsMatch 完全相同：数量严格相等，成交金额按日统计存储精度四位小数比对。
            .Where(row => row.Expected.Quantity != row.Source.Quantity
                || row.Expected.Amount != Math.Round(row.Source.Amount, 4, MidpointRounding.AwayFromZero))
            .Take(Math.Max(0, remaining))
            .Select(row => new BatchProductSalesDiscountMismatch(product.Trim().ToUpperInvariant(), row.Key.Store,
                row.Expected.Quantity, row.Source.Quantity, row.Expected.Amount, row.Source.Amount))
            .ToList();
    }

    private static string FormatMismatchDiagnostic(IReadOnlyList<BatchProductSalesDiscountMismatch> mismatches)
    {
        const int maxLength = 2000;
        var value = JsonSerializer.Serialize(new { Type = "ProductStoreDailyTotalsMismatch", Mismatches = mismatches });
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>覆盖范围按日历年计算，避免闰年把两年回填截短为 730 或 731 天。</summary>
    internal static IReadOnlyList<DateTime> BuildCoverageDays(DateTime today, int historicalYears)
    {
        var start = today.Date.AddYears(-historicalYears);
        return Enumerable.Range(0, (today.Date - start).Days + 1).Select(offset => start.AddDays(offset)).ToArray();
    }

    private static async Task ValidateOwnershipAsync(BatchProductSalesDiscountDailyStore store,
        ScheduledTaskLeaseService leases, string globalToken, BatchProductSalesDiscountDailyStore.ClaimedDay claim,
        CancellationToken token)
    {
        await leases.EnsureActiveAsync(GlobalLeaseTaskType, GlobalLeaseScope, globalToken, GlobalLeaseDuration, "折扣日快照持久队列");
        await store.EnsureOwnershipAsync(claim, DateTime.UtcNow, token);
    }

    /// <summary>既有持久队列会把 Fresh 日期显式重置为排队重算；折扣 worker 不直接写 canonical 日统计。</summary>
    private static Task RequestCanonicalReconciliationAsync(IServiceProvider services, DateTime day, CancellationToken token) =>
        services.GetRequiredService<IProductStoreDailyStatisticQueueService>()
            .EnqueueAsync([day], "batch-product-sales-discount-reconcile", maxConcurrency: 1, cancellationToken: token);
}
