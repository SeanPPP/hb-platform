using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;

namespace BlazorApp.Api.Services.Background;

/// <summary>安装独立快照表后启用；沿用全局租约服务，前台请求不承担成交扫描。</summary>
public sealed class BatchProductSalesDiscountWorker(IServiceScopeFactory scopes,
    ILogger<BatchProductSalesDiscountWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
                var store = new BatchProductSalesDiscountStore(db);
                if (store.SchemaReady)
                {
                    var leases = scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();
                    var lease = await leases.TryAcquireAsync(nameof(BatchProductSalesDiscountWorker), "global", TimeSpan.FromMinutes(3));
                    if (lease.Acquired && lease.Lease?.LeaseToken is { Length: > 0 } leaseToken)
                    {
                        try
                        {
                            var job = await store.ClaimAsync(DateTime.UtcNow);
                            if (job != null)
                            {
                                try
                                {
                                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                                    var reader = new BatchProductSalesAnalysisFactReader(db,
                                        scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>().Db,
                                        scope.ServiceProvider.GetRequiredService<HBSalesRecordSqlSugarContext>().Db);
                                    await store.ComputeAsync(job, reader.ReadAsync, timeout.Token);
                                }
                                catch (Exception ex)
                                {
                                    await store.FinishAsync(job, "Failed", null, DateTime.UtcNow);
                                    if (stoppingToken.IsCancellationRequested) throw;
                                    logger.LogWarning(ex, "商品折扣聚合失败，任务 {Id}，尝试 {Attempts}", job.Id, job.Attempts);
                                }
                            }
                        }
                        finally { await leases.CompleteAsync(nameof(BatchProductSalesDiscountWorker), "global", leaseToken, true); }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "商品折扣后台队列暂不可用"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
