namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 在当前调度实例持续恢复、排空并汇总商品分店每日统计持久队列。
/// </summary>
public sealed class ProductStoreDailyStatisticRecoveryService : BackgroundService
{
    private static readonly TimeSpan ActiveDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PassiveDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductStoreDailyStatisticRecoveryService> _logger;

    public ProductStoreDailyStatisticRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductStoreDailyStatisticRecoveryService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runtimeControl = scope.ServiceProvider
                    .GetRequiredService<ScheduledTaskRuntimeControlService>();
                if (!await runtimeControl.IsCurrentInstanceSchedulerEnabledAsync())
                {
                    delay = PassiveDelay;
                }
                else
                {
                    var queue = scope.ServiceProvider
                        .GetRequiredService<IProductStoreDailyStatisticQueueService>();
                    var recovered = await queue.RecoverExpiredRunningClaimsAsync(stoppingToken);
                    var drained = await queue.DrainOnceAsync(stoppingToken);
                    var finalized = await queue.FinalizeJobsAsync(stoppingToken);
                    if (recovered + drained + finalized > 0)
                    {
                        delay = ActiveDelay;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品分店每日统计持久队列循环失败");
                delay = FailureDelay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
