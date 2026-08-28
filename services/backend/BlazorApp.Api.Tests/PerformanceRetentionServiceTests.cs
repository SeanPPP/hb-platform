using System.Text.Json;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceRetentionServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"performance-retention-{Guid.NewGuid():N}.db"
    );
    private readonly SqlSugarClient _db;

    public PerformanceRetentionServiceTests()
    {
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(
            typeof(PerformanceMetricSample),
            typeof(PerformanceMetricBucket),
            typeof(PerformanceMetricDailyAggregate),
            typeof(PerformanceOperationalRun),
            typeof(PerformanceOperationalRunTransitionOutbox),
            typeof(PerformanceReleaseEvent),
            typeof(PerformanceIngestRateWindow)
        );
    }

    [Fact]
    public async Task 留存任务_原始30天_桶90天_日汇总与运行发布13个月且幂等()
    {
        var now = new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);
        await _db.Insertable(new[]
        {
            Sample(now.AddDays(-31)),
            Sample(now.AddDays(-2)),
        }).ExecuteCommandAsync();
        var histogram = PerformanceHistogram.Create();
        histogram.Record(10);
        histogram.Record(20);
        var dimensionsHash = new string('a', 64);
        await _db.Insertable(new[]
        {
            Bucket(now.AddDays(-91), "instance-a", dimensionsHash, histogram),
            Bucket(now.AddDays(-91).AddMinutes(5), "instance-b", dimensionsHash, histogram),
            Bucket(now.AddDays(-2), "instance-a", dimensionsHash, histogram),
        }).ExecuteCommandAsync();
        await _db.Insertable(
            new PerformanceOperationalRun
            {
                ExternalRunId = "old",
                Category = "background",
                Operation = "old",
                Status = "success",
                Environment = "Production",
                Source = "backend",
                QueuedAtUtc = now.AddMonths(-14),
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new PerformanceReleaseEvent
            {
                Id = Guid.NewGuid(),
                Action = "deploy",
                Status = "accepted",
                Environment = "Production",
                Component = "backend",
                Commit = "abc",
                StartedAtUtc = now.AddMonths(-14),
                CompletedAtUtc = now.AddMonths(-14),
                Source = "test",
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new PerformanceOperationalRunTransitionOutbox
            {
                ExternalRunId = "old-dead-letter",
                Category = "background",
                Operation = "old",
                Status = "failure",
                OccurredAtUtc = now.AddMonths(-14),
                NextAttemptAtUtc = now.AddMonths(-14),
                DeadLetteredAtUtc = now.AddMonths(-14),
                CreatedAt = now.AddMonths(-14),
            },
            new PerformanceOperationalRunTransitionOutbox
            {
                ExternalRunId = "pending-must-survive",
                Category = "background",
                Operation = "pending",
                Status = "queued",
                OccurredAtUtc = now.AddMonths(-14),
                NextAttemptAtUtc = now.AddMinutes(1),
                CreatedAt = now.AddMonths(-14),
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new PerformanceIngestRateWindow
            {
                ProjectCode = "web",
                ClientKeyHash = "old",
                WindowStartUtc = now.AddDays(-3),
                RequestCount = 1,
            },
            new PerformanceIngestRateWindow
            {
                ProjectCode = "web",
                ClientKeyHash = "current",
                WindowStartUtc = now.AddMinutes(-1),
                RequestCount = 1,
            },
        }).ExecuteCommandAsync();

        var options = new PerformanceMetricsOptions();
        await PerformanceRetentionService.RunOnceAsync(_db, options, now);
        await PerformanceRetentionService.RunOnceAsync(_db, options, now);

        Assert.Single(await _db.Queryable<PerformanceMetricSample>().ToListAsync());
        Assert.Single(await _db.Queryable<PerformanceMetricBucket>().ToListAsync());
        var daily = Assert.Single(
            await _db.Queryable<PerformanceMetricDailyAggregate>().ToListAsync()
        );
        Assert.Equal(4, daily.SampleCount);
        Assert.Equal(60, daily.SumValue);
        Assert.Equal(0, await _db.Queryable<PerformanceOperationalRun>().CountAsync());
        Assert.Equal(0, await _db.Queryable<PerformanceReleaseEvent>().CountAsync());
        var pendingOutbox = Assert.Single(
            await _db.Queryable<PerformanceOperationalRunTransitionOutbox>().ToListAsync()
        );
        Assert.Equal("pending-must-survive", pendingOutbox.ExternalRunId);
        Assert.Single(await _db.Queryable<PerformanceIngestRateWindow>().ToListAsync());
    }

    private static PerformanceMetricSample Sample(DateTime observedAt) =>
        new()
        {
            EventId = Guid.NewGuid(),
            ProjectCode = "web",
            Environment = "Production",
            SourceType = "client",
            MetricName = PerformanceMetricNames.WebTableRenderToPaint,
            ObservedAtUtc = observedAt,
            Value = 10,
            Unit = PerformanceMetricUnits.Milliseconds,
            Selector = "table",
            DimensionsHash = new string('b', 64),
            DimensionsJson = "{}",
        };

    private static PerformanceMetricBucket Bucket(
        DateTime observedAt,
        string instance,
        string dimensionsHash,
        PerformanceHistogram histogram
    ) =>
        new()
        {
            MetricName = PerformanceMetricNames.ApiRequestDuration,
            ProjectCode = "backend",
            Environment = "Production",
            SourceType = "api",
            InstanceId = instance,
            Selector = "api/products/{id}",
            DimensionsHash = dimensionsHash,
            DimensionsJson = "{\"route\":\"api/products/{id}\"}",
            WindowStartUtc = observedAt,
            SampleCount = 2,
            SumValue = 30,
            MinimumValue = 10,
            MaximumValue = 20,
            HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts),
            LastObservedAtUtc = observedAt,
        };

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
