using BlazorApp.Api.Interfaces.React;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 定时用实时价格复核未完成的价格更新任务。
/// 任务的生成是同步的（挂在审计收口上），这里只负责分店侧自行改价带来的形态流转与自动取消，
/// 让 Web 监控页无需有人打开移动端列表也能看到准确状态。
/// </summary>
public sealed class StorePriceUpdateTaskReconcileWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorePriceUpdateTaskReconcileWorker> _logger;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    public StorePriceUpdateTaskReconcileWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StorePriceUpdateTaskReconcileWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("StorePriceUpdateTasks:ReconcileEnabled", true))
        {
            return;
        }

        var intervalMinutes = Math.Max(1, _configuration.GetValue("StorePriceUpdateTasks:ReconcileIntervalMinutes", 5));
        // 启动后稍等，避开启动迁移与缓存预热的高峰。
        await DelayAsync(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStorePriceUpdateTaskService>();
                await service.ReconcilePendingAsync(null, stoppingToken);

                if (DateTime.UtcNow - _lastPurgeUtc > TimeSpan.FromHours(24))
                {
                    var purged = await service.PurgeExpiredAsync(stoppingToken);
                    _lastPurgeUtc = DateTime.UtcNow;
                    if (purged > 0)
                    {
                        _logger.LogInformation("已清理过期的价格更新任务 {Count} 条", purged);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "价格更新任务定时对账失败");
            }

            await DelayAsync(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 停机信号：由外层循环条件退出。
        }
    }
}
