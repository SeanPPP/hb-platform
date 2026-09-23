using BlazorApp.Api.Interfaces;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 定期预热移动端商品报告「中国供应商」页签的默认视图缓存。
/// 当天统计每次刷新都会换新的批次版本，首个打开的用户原本要冷加载 2–6 秒（2026-09-22 生产实测）；
/// 这里按固定间隔走与前台完全相同的读取路径：已缓存时只花几毫秒，未缓存时由后台先算好。
/// 每个实例各自维护内存缓存，所以不需要分布式租约。
/// </summary>
public sealed class MobileChinaReportCacheWarmupWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<MobileChinaReportCacheWarmupWorker> logger) : BackgroundService
{
    private const string EnabledKey = "Reports:MobileChinaTabWarmup:Enabled";
    private const string IntervalKey = "Reports:MobileChinaTabWarmup:IntervalSeconds";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(15);
    // 单轮预算：三块数据冷算通常几秒；超过预算说明库在忙，本轮放弃，避免后台把前台拖得更慢。
    private static readonly TimeSpan RunBudget = TimeSpan.FromSeconds(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(EnabledKey, true))
        {
            logger.LogInformation("移动端中国供应商页签缓存预热已通过配置关闭（{Key}）。", EnabledKey);
            return;
        }

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "移动端中国供应商页签缓存预热本轮失败"); }

            try { await Task.Delay(GetInterval(), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        // 预热只读、只写本实例内存，不走调度总开关：那个检查会顺带写实例心跳，且关调度时（如本地调试）预热仍应生效。

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        budget.CancelAfter(RunBudget);
        try
        {
            await services.GetRequiredService<ISalesDashboardCacheWarmer>().WarmUpMobileChinaTabAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("移动端中国供应商页签缓存预热超过 {Budget} 秒预算，本轮放弃。", RunBudget.TotalSeconds);
        }
    }

    private TimeSpan GetInterval()
    {
        var seconds = configuration.GetValue<int?>(IntervalKey);
        if (seconds is null || seconds <= 0)
        {
            return DefaultInterval;
        }
        var interval = TimeSpan.FromSeconds(seconds.Value);
        return interval < MinimumInterval ? MinimumInterval : interval;
    }
}
