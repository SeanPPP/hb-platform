using BlazorApp.Api.Data;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 定期回收完整刷新遗留的非商品统计孤儿 Running 行。
/// 条件更新幂等，多实例同时执行也只会有一个实例真正改到行，无需额外租约。
/// </summary>
public sealed class SalesStatisticsOrphanRunningStateRecoveryService(
    IServiceScopeFactory scopes,
    ILogger<SalesStatisticsOrphanRunningStateRecoveryService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "孤儿统计状态回收失败"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        // 与商品分店每日统计回收同一开关：计划任务总开关关闭时不改任何统计状态。
        if (!await services.GetRequiredService<ScheduledTaskRuntimeControlService>().IsLeaseManagedWorkerEnabledAsync())
            return 0;

        var db = services.GetRequiredService<SqlSugarContext>().Db;
        return await new SalesStatisticsOrphanRunningStateRecovery(db, logger)
            .RecoverAsync(DateTime.UtcNow, stoppingToken);
    }
}
