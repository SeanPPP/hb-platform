namespace BlazorApp.Api.Services.Attendance;

public sealed class FaceAttendanceWorker(IServiceScopeFactory scopeFactory, ILogger<FaceAttendanceWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<FaceAttendanceService>();
                if (configuration.GetValue("FaceAttendance:Enabled", false)) await service.ProcessQueuedAsync(stoppingToken);
                else await service.CleanupExpiredPhotosAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("人脸考勤后台核验循环失败 ({ErrorType})，保留数据库事件等待重试", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
