using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceBacklogSamplerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PerformanceMetricAggregateStore _aggregateStore;
    private readonly PerformanceCollectorCoordinator _coordinator;
    private readonly PerformanceMetricsOptions _options;
    private readonly ILogger<PerformanceBacklogSamplerService> _logger;

    public PerformanceBacklogSamplerService(
        IServiceScopeFactory scopeFactory,
        PerformanceMetricAggregateStore aggregateStore,
        PerformanceCollectorCoordinator coordinator,
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceBacklogSamplerService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _aggregateStore = aggregateStore;
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleAsync(stoppingToken);
            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            await SampleOnceAsync(
                db,
                _aggregateStore,
                _coordinator,
                _options,
                DateTime.UtcNow,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "采样 HQ 同步积压失败");
        }
    }

    internal static async Task<bool> SampleOnceAsync(
        SqlSugar.ISqlSugarClient db,
        PerformanceMetricAggregateStore aggregateStore,
        PerformanceCollectorCoordinator coordinator,
        PerformanceMetricsOptions options,
        DateTime utcNow,
        CancellationToken cancellationToken = default
    )
    {
        var windowStart = FloorToMinute(PerformanceUtc.Normalize(utcNow));
        var windowEnd = windowStart.AddMinutes(1);
        var lease = await coordinator.TryAcquireAsync(
            db,
            "hq-backlog",
            utcNow,
            TimeSpan.FromSeconds(45),
            windowStart,
            cancellationToken
        );
        if (lease == null)
        {
            return false;
        }
        if (lease.CursorUtc >= windowEnd)
        {
            await coordinator.ReleaseAsync(db, lease, utcNow, cancellationToken);
            return false;
        }

        var operations = await db
            .Queryable<PerformanceOperationalRun>()
            .Where(item => item.Category == "hq")
            .Select(item => item.Operation)
            .Distinct()
            .ToListAsync(cancellationToken);
        var activeOperations = await db
            .Queryable<PerformanceOperationalRun>()
            .Where(item =>
                item.Category == "hq"
                && (
                    item.Status == "queued"
                    || item.Status == "running"
                    || item.Status == "retry_wait"
                )
            )
            .Select(item => item.Operation)
            .ToListAsync(cancellationToken);

        return await coordinator.CommitAsync(
            db,
            lease,
            utcNow,
            windowEnd,
            release: true,
            async token =>
            {
                foreach (var operation in operations)
                {
                    var backlog = activeOperations.Count(item => item == operation);
                    await aggregateStore.UpsertInCurrentTransactionAsync(
                        db,
                        new PerformanceMetricRecord(
                            PerformanceMetricNames.HqSyncBacklog,
                            options.BackendProjectCode,
                            options.DefaultEnvironment,
                            "operational-run",
                            backlog,
                            windowStart,
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["operation"] = operation,
                            }
                        ),
                        "global-hq-backlog",
                        token
                    );
                }
            },
            cancellationToken
        );
    }

    private static DateTime FloorToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
}
