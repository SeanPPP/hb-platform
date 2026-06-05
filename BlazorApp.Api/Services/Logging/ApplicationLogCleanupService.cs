namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLogCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApplicationLogCleanupService> _logger;

        public ApplicationLogCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<ApplicationLogCleanupService> logger
        )
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ApplicationLogService>();
                    var deleted = await service.CleanupExpiredLogsAsync(DateTime.UtcNow);
                    _logger.LogInformation("中心日志过期清理完成，删除 {Count} 条", deleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "中心日志过期清理失败");
                }
            }
        }
    }
}
