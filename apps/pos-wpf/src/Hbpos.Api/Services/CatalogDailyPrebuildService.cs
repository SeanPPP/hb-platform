using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public interface IActiveCatalogStoreProvider
{
    Task<IReadOnlyList<string>> GetActiveStoreCodesAsync(CancellationToken cancellationToken);
}

/// <summary>复用目录服务的门店查询，确保只取得 active 且未删除的门店。</summary>
public sealed class CatalogActiveStoreProvider(IServiceScopeFactory scopeFactory) : IActiveCatalogStoreProvider
{
    public async Task<IReadOnlyList<string>> GetActiveStoreCodesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var catalogService = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var stores = await catalogService.GetStoresAsync(cancellationToken);
        return stores
            .Select(store => store.StoreCode)
            .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
            .ToArray();
    }
}

/// <summary>
/// 每日 Australia/Brisbane 01:00 预构建目录。服务启动后若已错过当天 01:00，
/// 只等待下一日，避免在营业时间补跑。
/// </summary>
public sealed class CatalogDailyPrebuildService(
    IActiveCatalogStoreProvider activeStoreProvider,
    ICatalogBackgroundRefreshScheduler refreshScheduler,
    IOptions<CatalogDailyPrebuildOptions> options,
    TimeProvider timeProvider,
    ILogger<CatalogDailyPrebuildService> logger) : BackgroundService
{
    private const string BrisbaneTimeZoneId = "Australia/Brisbane";

    public async Task RunDailyPrebuildAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var stores = await activeStoreProvider.GetActiveStoreCodesAsync(cancellationToken);
        var storeCodes = stores
            .Select(storeCode => storeCode.Trim())
            .Where(storeCode => storeCode.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation(
            "Catalog daily prebuild started runId={RunId} storeCount={StoreCount}",
            runId,
            storeCodes.Length);

        var results = await Task.WhenAll(storeCodes.Select(storeCode =>
            WaitForStoreRefreshAsync(runId, storeCode, cancellationToken)));
        var failedCount = results.Count(result => !result);

        logger.LogInformation(
            "Catalog daily prebuild completed runId={RunId} storeCount={StoreCount} failedStoreCount={FailedStoreCount}",
            runId,
            storeCodes.Length,
            failedCount);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Catalog daily prebuild is disabled");
            return;
        }

        TimeZoneInfo brisbane;
        try
        {
            brisbane = TimeZoneInfo.FindSystemTimeZoneById(BrisbaneTimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            logger.LogCritical(
                exception,
                "Catalog daily prebuild cannot start because timezone={TimeZoneId} is unavailable",
                BrisbaneTimeZoneId);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextRunUtc = CatalogDailyPrebuildSchedule.GetNextRunUtc(now, brisbane);
            var delay = nextRunUtc - now;
            logger.LogInformation(
                "Catalog daily prebuild scheduled nextRunUtc={NextRunUtc} timezone={TimeZoneId}",
                nextRunUtc,
                BrisbaneTimeZoneId);

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
                var wokeAt = timeProvider.GetUtcNow();
                if (!CatalogDailyPrebuildSchedule.IsWithinStartWindow(wokeAt, nextRunUtc))
                {
                    logger.LogWarning(
                        "Catalog daily prebuild skipped after late wake scheduledRunUtc={ScheduledRunUtc} wokeAtUtc={WokeAtUtc}",
                        nextRunUtc,
                        wokeAt);
                    continue;
                }

                await RunDailyPrebuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Catalog daily prebuild run failed timezone={TimeZoneId}",
                    BrisbaneTimeZoneId);
            }
        }
    }

    private async Task<bool> WaitForStoreRefreshAsync(
        Guid runId,
        string storeCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await refreshScheduler.QueueRefreshAsync(storeCode).WaitAsync(cancellationToken);
            logger.LogInformation(
                "Catalog daily prebuild store completed runId={RunId} store={StoreCode}",
                runId,
                storeCode);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Catalog daily prebuild store failed runId={RunId} store={StoreCode}",
                runId,
                storeCode);
            return false;
        }
    }
}
