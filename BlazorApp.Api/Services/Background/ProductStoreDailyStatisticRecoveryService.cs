using BlazorApp.Api.Services;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 后端启动时解锁因进程重启中断的商品分店每日统计任务。
    /// </summary>
    public class ProductStoreDailyStatisticRecoveryService : IHostedService
    {
        private static readonly TimeSpan StaleTaskTimeout = TimeSpan.FromMinutes(30);

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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();
                var recoveredCount = await service.RecoverTimedOutProductStoreDailyRecalculationJobsAsync(
                    StaleTaskTimeout
                );

                if (recoveredCount > 0)
                {
                    _logger.LogWarning(
                        "已恢复 {RecoveredCount} 个超时的商品分店每日统计任务为 Pending",
                        recoveredCount
                    );
                }
                else
                {
                    _logger.LogInformation("未发现超时的商品分店每日统计任务");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复超时商品分店每日统计任务失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
