using BlazorApp.Api.Services.Background;

namespace BlazorApp.Api.Services;

/// <summary>一次只推进一个日期；无审计表或无批次时保持静默。</summary>
public sealed class HourlySalesBackfillWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<HourlySalesBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                if (!configuration.GetValue<bool>("SalesStatistics:HourlyBackfillWorkerEnabled"))
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }
                using var scope = scopes.CreateScope();
                var runtimeControl = scope.ServiceProvider
                    .GetRequiredService<ScheduledTaskRuntimeControlService>();
                if (!await runtimeControl.IsCurrentInstanceSchedulerEnabledAsync())
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }
                var service = scope.ServiceProvider.GetRequiredService<HourlySalesBackfillService>();
                if (service.SchemaReady())
                {
                    var leases = scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();
                    var lease = await leases.TryAcquireAsync(
                        "HourlySalesBackfillWorker", "global", TimeSpan.FromMinutes(30));
                    if (lease.Acquired && lease.Lease?.LeaseToken is { Length: > 0 } token)
                    {
                        try
                        {
                            worked = await service.RunOneAsync(stoppingToken, () => leases.RenewAsync(
                                "HourlySalesBackfillWorker", "global", token, TimeSpan.FromMinutes(30)));
                        }
                        finally { await leases.CompleteAsync("HourlySalesBackfillWorker", "global", token, true); }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "分时历史回填后台批次执行失败"); }
            try
            {
                await Task.Delay(worked ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

}
