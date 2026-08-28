using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceCollectorCoordinatorTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"performance-coordinator-{Guid.NewGuid():N}.db"
    );
    private ISqlSugarClient _db = null!;

    public async Task InitializeAsync()
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
        await PerformanceBaselineSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);
    }

    [Fact]
    public async Task 多实例租约只允许一个收集器推进持久游标()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var initial = now.AddMinutes(-1);
        var firstCoordinator = new PerformanceCollectorCoordinator("instance-a");
        var secondCoordinator = new PerformanceCollectorCoordinator("instance-b");

        var first = await firstCoordinator.TryAcquireAsync(
            _db,
            "hq-backlog",
            now,
            TimeSpan.FromSeconds(45),
            initial
        );
        var blocked = await secondCoordinator.TryAcquireAsync(
            _db,
            "hq-backlog",
            now.AddSeconds(1),
            TimeSpan.FromSeconds(45),
            initial
        );

        Assert.NotNull(first);
        Assert.Null(blocked);
        var writes = 0;
        Assert.True(
            await firstCoordinator.CommitAsync(
                _db,
                first!,
                now.AddSeconds(2),
                now,
                release: true,
                _ =>
                {
                    writes++;
                    return Task.CompletedTask;
                }
            )
        );

        var next = await secondCoordinator.TryAcquireAsync(
            _db,
            "hq-backlog",
            now.AddSeconds(3),
            TimeSpan.FromSeconds(45),
            initial
        );
        Assert.NotNull(next);
        Assert.Equal(now, next!.CursorUtc);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Hq积压同一分钟跨实例只持久化一次全局样本()
    {
        var now = new DateTime(2026, 8, 25, 6, 1, 20, DateTimeKind.Utc);
        await _db.Insertable(
            new PerformanceOperationalRun
            {
                ExternalRunId = "hq-1",
                Category = "hq",
                Operation = "product-sync",
                Status = "running",
                Environment = "Production",
                Source = "backend",
                QueuedAtUtc = now.AddMinutes(-1),
            }
        ).ExecuteCommandAsync();
        var options = new PerformanceMetricsOptions
        {
            DefaultEnvironment = "Production",
            BackendProjectCode = "hb-backend",
            InstanceId = "test-instance",
        };
        var aggregateStore = new PerformanceMetricAggregateStore(Options.Create(options));

        Assert.True(
            await PerformanceBacklogSamplerService.SampleOnceAsync(
                _db,
                aggregateStore,
                new PerformanceCollectorCoordinator("instance-a"),
                options,
                now
            )
        );
        Assert.False(
            await PerformanceBacklogSamplerService.SampleOnceAsync(
                _db,
                aggregateStore,
                new PerformanceCollectorCoordinator("instance-b"),
                options,
                now.AddSeconds(5)
            )
        );

        var bucket = await _db.Queryable<PerformanceMetricBucket>()
            .Where(item => item.MetricName == PerformanceMetricNames.HqSyncBacklog)
            .SingleAsync();
        Assert.Equal(1, bucket.SampleCount);
        Assert.Equal(1, bucket.SumValue);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        return Task.CompletedTask;
    }
}
