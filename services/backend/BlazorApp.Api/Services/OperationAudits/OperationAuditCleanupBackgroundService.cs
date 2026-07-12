namespace BlazorApp.Api.Services.OperationAudits;

public sealed class OperationAuditCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OperationAuditCleanupBackgroundService> _logger;

    public OperationAuditCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OperationAuditCleanupBackgroundService> logger
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
                var service = scope.ServiceProvider.GetRequiredService<OperationAuditRetentionService>();
                var deleted = await service.CleanupExpiredAsync(DateTime.UtcNow, stoppingToken);
                _logger.LogInformation("操作日志过期清理完成，删除 {Count} 条", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "操作日志过期清理失败");
            }
        }
    }
}
