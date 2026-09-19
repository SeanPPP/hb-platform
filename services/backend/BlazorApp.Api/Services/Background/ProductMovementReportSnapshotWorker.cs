using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 按门店预计算商品经营分析快照，让「全部分店」这类要聚合全部日统计的查询不再实时计算。
/// 由分布式租约保证多实例下只有一个在刷新；表未部署或计划任务总开关关闭时直接跳过。
/// </summary>
public sealed class ProductMovementReportSnapshotWorker(
    IServiceScopeFactory scopes,
    ILogger<ProductMovementReportSnapshotWorker> logger) : BackgroundService
{
    private const string LeaseTaskType = nameof(ProductMovementReportSnapshotWorker);
    private const string LeaseScope = "store-snapshot";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    // 单轮只在预算内领取新门店；已开始的门店用自己的超时跑完，租约时长留足余量。
    private static readonly TimeSpan RunBudget = TimeSpan.FromMinutes(10);
    // 单店实测 1–5 秒（2026-09-19 生产），3 分钟足以覆盖库负载抖动，又不会长时间占着租约。
    private const int StoreCommandTimeoutSeconds = 180;
    private static readonly TimeSpan StaleBuildingAfter = TimeSpan.FromMinutes(30);

    private bool _schemaMissingLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "商品经营分析快照后台任务暂不可用"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        if (!await services.GetRequiredService<ScheduledTaskRuntimeControlService>().IsLeaseManagedWorkerEnabledAsync())
        {
            return;
        }

        var db = services.GetRequiredService<SqlSugarContext>().Db;
        var store = new ProductMovementReportSnapshotStore(db);
        if (!store.SchemaReady)
        {
            if (!_schemaMissingLogged)
            {
                logger.LogWarning("商品经营分析快照表尚未部署，后台任务跳过；请先执行 SqlScripts/ProductMovementReportSnapshot.sql");
                _schemaMissingLogged = true;
            }
            return;
        }

        var leases = services.GetRequiredService<ScheduledTaskLeaseService>();
        var lease = await leases.TryAcquireAsync(LeaseTaskType, LeaseScope, LeaseDuration);
        if (!lease.Acquired || string.IsNullOrWhiteSpace(lease.Lease?.LeaseToken))
        {
            return;
        }

        var leaseToken = lease.Lease.LeaseToken;
        var success = false;
        var previousTimeout = db.Ado.CommandTimeOut;
        try
        {
            db.Ado.CommandTimeOut = StoreCommandTimeoutSeconds;
            var today = SalesStatisticsBusinessDate.GetBusinessDate(DateTimeOffset.UtcNow);
            var activeStores = await store.GetActiveStoreCodesAsync();
            var latestRuns = await store.GetLatestReadyRunsAsync(today, null);
            var due = ProductMovementReportSnapshotPolicy.SelectStoresDue(activeStores, latestRuns, DateTime.UtcNow);
            var deadline = DateTime.UtcNow.Add(RunBudget);

            foreach (var storeCode in due)
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                await leases.EnsureActiveAsync(LeaseTaskType, LeaseScope, leaseToken, LeaseDuration, "商品经营分析快照");
                await RefreshStoreAsync(store, today, storeCode);
            }

            // 前一天的批次对今天的查询已无用，保留一天仅为跨零点时仍在读取的请求兜底。
            await store.CleanupBeforeAsync(today.AddDays(-1));
            success = true;
        }
        finally
        {
            db.Ado.CommandTimeOut = previousTimeout;
            await leases.CompleteAsync(LeaseTaskType, LeaseScope, leaseToken, success);
        }
    }

    private async Task RefreshStoreAsync(ProductMovementReportSnapshotStore store, DateTime today, string storeCode)
    {
        var elapsed = Stopwatch.StartNew();
        var runId = await store.BeginRunAsync(today, storeCode, DateTime.UtcNow);
        try
        {
            await store.PublishAsync(today, storeCode, runId);
            logger.LogInformation(
                "商品经营分析快照已发布: Store={StoreCode}, AsOfDate={AsOfDate:yyyy-MM-dd}, ElapsedMs={ElapsedMs}",
                storeCode, today, elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // 单店失败不影响其他门店；读取端缺这家店时会整体回到实时计算。
            logger.LogWarning(ex, "商品经营分析快照失败: Store={StoreCode}, ElapsedMs={ElapsedMs}", storeCode, elapsed.ElapsedMilliseconds);
            try { await store.MarkFailedAsync(runId, ex.Message, DateTime.UtcNow); }
            catch (Exception markEx) { logger.LogWarning(markEx, "商品经营分析快照失败状态写入失败: Store={StoreCode}", storeCode); }
        }

        await store.CleanupStoreAsync(today, storeCode, DateTime.UtcNow - StaleBuildingAfter);
    }
}
