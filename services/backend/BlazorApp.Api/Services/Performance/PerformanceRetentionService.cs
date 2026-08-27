using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PerformanceMetricsOptions _options;
    private readonly PerformanceCollectorCoordinator _coordinator;
    private readonly ILogger<PerformanceRetentionService> _logger;

    public PerformanceRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<PerformanceMetricsOptions> options,
        PerformanceCollectorCoordinator coordinator,
        ILogger<PerformanceRetentionService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            var now = DateTime.UtcNow;
            var today = now.Date;
            var lease = await _coordinator.TryAcquireAsync(
                db,
                "performance-retention",
                now,
                TimeSpan.FromHours(2),
                today.AddDays(-1),
                cancellationToken
            );
            if (lease == null)
            {
                return;
            }
            if (lease.CursorUtc >= today)
            {
                await _coordinator.ReleaseAsync(db, lease, now, cancellationToken);
                return;
            }
            try
            {
                await RunOnceAsync(db, _options, now, cancellationToken);
                await _coordinator.CommitAsync(
                    db,
                    lease,
                    DateTime.UtcNow,
                    today,
                    release: true,
                    _ => Task.CompletedTask,
                    cancellationToken
                );
            }
            catch
            {
                await _coordinator.ReleaseAsync(
                    db,
                    lease,
                    DateTime.UtcNow,
                    CancellationToken.None
                );
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "性能指标留存清理失败");
        }
    }

    internal static async Task RunOnceAsync(
        ISqlSugarClient db,
        PerformanceMetricsOptions options,
        DateTime utcNow,
        CancellationToken cancellationToken = default
    )
    {
        var rawCutoff = utcNow.AddDays(-Math.Clamp(options.RawSampleRetentionDays, 1, 365));
        var bucketCutoff = utcNow
            .AddDays(-Math.Clamp(options.BucketRetentionDays, 1, 730))
            .Date;
        var aggregateCutoff = utcNow
            .AddMonths(-Math.Clamp(options.AggregateRetentionMonths, 1, 60))
            .Date;

        await db
            .Deleteable<PerformanceMetricSample>()
            .Where(item => item.ObservedAtUtc < rawCutoff)
            .ExecuteCommandAsync(cancellationToken);
        await db
            .Deleteable<PerformanceIngestRateWindow>()
            .Where(item => item.WindowStartUtc < utcNow.AddDays(-2))
            .ExecuteCommandAsync(cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expiredBuckets = await db
                .Queryable<PerformanceMetricBucket>()
                .Where(item => item.WindowStartUtc < bucketCutoff)
                .OrderBy(item => item.WindowStartUtc)
                .Take(5000)
                .ToListAsync(cancellationToken);
            if (expiredBuckets.Count == 0)
            {
                break;
            }

            db.Ado.BeginTran();
            try
            {
                await MergeDailyAsync(db, expiredBuckets, cancellationToken);
                var ids = expiredBuckets.Select(item => item.Id).ToList();
                await db
                    .Deleteable<PerformanceMetricBucket>()
                    .Where(item => ids.Contains(item.Id))
                    .ExecuteCommandAsync(cancellationToken);
                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }

        await db
            .Deleteable<PerformanceMetricDailyAggregate>()
            .Where(item => item.DayUtc < aggregateCutoff)
            .ExecuteCommandAsync(cancellationToken);
        await db
            .Deleteable<PerformanceOperationalRun>()
            .Where(item => item.QueuedAtUtc < aggregateCutoff)
            .ExecuteCommandAsync(cancellationToken);
        await db
            .Deleteable<PerformanceOperationalRunTransitionOutbox>()
            .Where(item =>
                item.DeadLetteredAtUtc != null
                && item.DeadLetteredAtUtc < aggregateCutoff
            )
            .ExecuteCommandAsync(cancellationToken);
        await db
            .Deleteable<PerformanceReleaseEvent>()
            .Where(item => item.CompletedAtUtc < aggregateCutoff)
            .ExecuteCommandAsync(cancellationToken);
    }

    private static async Task MergeDailyAsync(
        ISqlSugarClient db,
        IReadOnlyCollection<PerformanceMetricBucket> buckets,
        CancellationToken cancellationToken
    )
    {
        var minimumDay = buckets.Min(item => item.WindowStartUtc.Date);
        var maximumDayExclusive = buckets.Max(item => item.WindowStartUtc.Date).AddDays(1);
        var existingRows = await db
            .Queryable<PerformanceMetricDailyAggregate>()
            .Where(item => item.DayUtc >= minimumDay && item.DayUtc < maximumDayExclusive)
            .ToListAsync(cancellationToken);
        var existingByKey = existingRows.ToDictionary(ToKey);

        foreach (var group in buckets.GroupBy(ToKey))
        {
            var incoming = Aggregate(group);
            if (!existingByKey.TryGetValue(group.Key, out var existing))
            {
                await db.Insertable(incoming).ExecuteCommandAsync(cancellationToken);
                existingByKey[group.Key] = incoming;
                continue;
            }

            var histogram = PerformanceHistogram.FromCounts(
                DeserializeCounts(existing.HistogramCountsJson),
                existing.MaximumValue
            );
            histogram.Merge(
                PerformanceHistogram.FromCounts(
                    DeserializeCounts(incoming.HistogramCountsJson),
                    incoming.MaximumValue
                )
            );
            existing.SampleCount += incoming.SampleCount;
            existing.SumValue += incoming.SumValue;
            existing.MinimumValue = Math.Min(existing.MinimumValue, incoming.MinimumValue);
            existing.MaximumValue = Math.Max(existing.MaximumValue, incoming.MaximumValue);
            existing.HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts);
            existing.LastObservedAtUtc = existing.LastObservedAtUtc > incoming.LastObservedAtUtc
                ? existing.LastObservedAtUtc
                : incoming.LastObservedAtUtc;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.Updateable(existing).ExecuteCommandAsync(cancellationToken);
        }
    }

    private static PerformanceMetricDailyAggregate Aggregate(
        IEnumerable<PerformanceMetricBucket> source
    )
    {
        var rows = source.ToList();
        var first = rows[0];
        var histogram = PerformanceHistogram.Create();
        foreach (var row in rows)
        {
            histogram.Merge(
                PerformanceHistogram.FromCounts(
                    DeserializeCounts(row.HistogramCountsJson),
                    row.MaximumValue
                )
            );
        }
        return new PerformanceMetricDailyAggregate
        {
            MetricName = first.MetricName,
            ProjectCode = first.ProjectCode,
            Environment = first.Environment,
            SourceType = first.SourceType,
            Selector = first.Selector,
            DimensionsHash = first.DimensionsHash,
            DimensionsJson = first.DimensionsJson,
            DayUtc = first.WindowStartUtc.Date,
            SampleCount = rows.Sum(item => item.SampleCount),
            SumValue = rows.Sum(item => item.SumValue),
            MinimumValue = rows.Min(item => item.MinimumValue),
            MaximumValue = rows.Max(item => item.MaximumValue),
            HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts),
            LastObservedAtUtc = rows.Max(item => item.LastObservedAtUtc),
        };
    }

    private static DailyKey ToKey(PerformanceMetricBucket row) =>
        new(
            row.MetricName,
            row.ProjectCode,
            row.Environment,
            row.SourceType,
            row.DimensionsHash,
            row.WindowStartUtc.Date
        );

    private static DailyKey ToKey(PerformanceMetricDailyAggregate row) =>
        new(
            row.MetricName,
            row.ProjectCode,
            row.Environment,
            row.SourceType,
            row.DimensionsHash,
            row.DayUtc.Date
        );

    private static IReadOnlyList<long> DeserializeCounts(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<long[]>(json ?? "[]") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record DailyKey(
        string MetricName,
        string ProjectCode,
        string Environment,
        string SourceType,
        string DimensionsHash,
        DateTime DayUtc
    );
}
