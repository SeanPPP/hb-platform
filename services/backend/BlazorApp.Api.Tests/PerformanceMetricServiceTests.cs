using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceMetricServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"performance-baseline-{Guid.NewGuid():N}.db"
    );
    private ISqlSugarClient _db = null!;
    private PerformanceMetricBuffer _buffer = null!;
    private PerformanceMetricService _service = null!;

    public async Task InitializeAsync()
    {
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        await PerformanceBaselineSchemaMigrator.EnsureAsync(
            _db,
            NullLogger.Instance
        );
        _buffer = new PerformanceMetricBuffer(
            Options.Create(
                new PerformanceMetricsOptions
                {
                    DefaultEnvironment = "Production",
                    InstanceId = "test-instance",
                }
            ),
            NullLogger<PerformanceMetricBuffer>.Instance
        );
        _service = new PerformanceMetricService(
            _db,
            _buffer,
            new PerformanceMetricAggregateStore(
                Options.Create(
                    new PerformanceMetricsOptions { InstanceId = "test-instance" }
                )
            ),
            Options.Create(new PerformanceMetricsOptions { DefaultEnvironment = "Production" }),
            NullLogger<PerformanceMetricService>.Instance
        );
    }

    [Fact]
    public async Task IngestAsync_重复事件幂等且汇总可计算分位数()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();
        var batch = Batch(
            eventId,
            PerformanceMetricNames.ApiRequestDuration,
            210,
            new Dictionary<string, string>
            {
                ["environment"] = "Production",
                ["route"] = "/api/products/{id}",
                ["method"] = "GET",
                ["statusClass"] = "2xx",
            },
            now
        );

        var first = await _service.IngestAsync("HBBBackend", "api", batch, now);
        Assert.Equal(1, await _db.Queryable<PerformanceMetricBucket>().CountAsync());
        Assert.Equal(0, _buffer.BufferedSeriesCount);
        var duplicate = await _service.IngestAsync("HBBBackend", "api", batch, now);
        var overview = await _service.GetOverviewAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = now.AddMinutes(-1),
                EndUtc = now.AddMinutes(1),
            },
            now.AddMinutes(1)
        );

        Assert.True(first.Success);
        Assert.Equal(1, first.Data!.AcceptedCount);
        Assert.True(duplicate.Success);
        Assert.Equal(1, duplicate.Data!.DuplicateCount);
        Assert.Equal(1, await _db.Queryable<PerformanceMetricSample>().CountAsync());
        var api = Assert.Single(overview.Api);
        Assert.Equal("GET /api/products/{id} 2xx", api.Selector);
        Assert.Equal(1, api.SampleCount);
        Assert.Equal(250, api.P95);
        Assert.Equal("insufficient", api.CoverageState);
    }

    [Fact]
    public async Task GetSeriesAsync_部分日窗口不读取边界日的整日汇总()
    {
        var firstDay = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        var histogram = PerformanceHistogram.Create();
        histogram.Record(100);
        var rows = Enumerable.Range(0, 3).Select(index =>
            new PerformanceMetricDailyAggregate
            {
                MetricName = PerformanceMetricNames.ApiRequestDuration,
                ProjectCode = "backend",
                Environment = "Production",
                SourceType = "api",
                Selector = "GET /api/products 2xx",
                DimensionsHash = new string('a', 64),
                DimensionsJson = "{}",
                DayUtc = firstDay.AddDays(index),
                SampleCount = 1,
                SumValue = 100,
                MinimumValue = 100,
                MaximumValue = 100,
                HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts),
                LastObservedAtUtc = firstDay.AddDays(index).AddHours(23),
            }
        ).ToList();
        await _db.Insertable(rows).ExecuteCommandAsync();

        var result = await _service.GetSeriesAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = firstDay.AddHours(12),
                EndUtc = firstDay.AddDays(2).AddHours(12),
            },
            firstDay.AddDays(2).AddHours(12)
        );

        var point = Assert.Single(result.Points);
        Assert.Equal(firstDay.AddDays(1), point.WindowStartUtc);
        Assert.Equal(24 * 60, point.BucketSizeMinutes);
    }

    [Fact]
    public async Task GetSeriesAsync_拒绝倒置或超过上限的查询窗口()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);

        var reversed = await Assert.ThrowsAsync<PerformanceSeriesQueryException>(() =>
            _service.GetSeriesAsync(
                new PerformanceOverviewQueryDto
                {
                    StartUtc = now,
                    EndUtc = now.AddMinutes(-1),
                },
                now
            )
        );
        var oversized = await Assert.ThrowsAsync<PerformanceSeriesQueryException>(() =>
            _service.GetSeriesAsync(
                new PerformanceOverviewQueryDto
                {
                    StartUtc = now.AddDays(-32),
                    EndUtc = now,
                },
                now
            )
        );

        Assert.Equal("PERFORMANCE_SERIES_INVALID_RANGE", reversed.ErrorCode);
        Assert.Equal("PERFORMANCE_SERIES_RANGE_TOO_LARGE", oversized.ErrorCode);
    }

    [Fact]
    public async Task GetOverviewAsync_拒绝超过三十一天的查询窗口()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);

        var oversized = await Assert.ThrowsAsync<PerformanceOverviewQueryException>(() =>
            _service.GetOverviewAsync(
                new PerformanceOverviewQueryDto
                {
                    StartUtc = now.AddDays(-32),
                    EndUtc = now,
                },
                now
            )
        );

        Assert.Equal("PERFORMANCE_OVERVIEW_RANGE_TOO_LARGE", oversized.ErrorCode);
    }

    [Fact]
    public async Task GetOverviewAsync_流式扫描必须在一致性事务内且不再分页()
    {
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        const int initialCount = 1_001;
        var rows = CreateSeriesBuckets(start, initialCount, ["instance-a"]);
        await InsertBucketsAsync(rows);
        var bucketSelectCount = 0;
        var bucketSelectStartedInTransaction = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                !sql.Contains("PerformanceMetricBucket", StringComparison.OrdinalIgnoreCase)
                || !sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }
            bucketSelectCount++;
            bucketSelectStartedInTransaction = _db.Ado.Transaction != null;
        };

        PerformanceOverviewDto overview;
        try
        {
            overview = await _service.GetOverviewAsync(
                new PerformanceOverviewQueryDto
                {
                    Environment = "Production",
                    StartUtc = start,
                    EndUtc = start.AddDays(7),
                },
                start.AddDays(7)
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.Equal(1, bucketSelectCount);
        Assert.True(bucketSelectStartedInTransaction);
        Assert.Equal(initialCount, Assert.Single(overview.Api).SampleCount);
    }

    [Fact]
    public void ConsistentReadIsolation_SQLServer必须使用Snapshot且测试库保持Serializable()
    {
        Assert.Equal(
            System.Data.IsolationLevel.Snapshot,
            PerformanceMetricService.ResolveConsistentReadIsolationLevel(DbType.SqlServer)
        );
        Assert.Equal(
            System.Data.IsolationLevel.Serializable,
            PerformanceMetricService.ResolveConsistentReadIsolationLevel(DbType.Sqlite)
        );
    }

    [Fact]
    public async Task GetSeriesAsync_跨实例源行超过上限但最终点数合格时应成功()
    {
        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        const int pointCount = 31 * 24 * 12;
        var rows = CreateSeriesBuckets(start, pointCount, ["instance-a", "instance-b"]);
        await InsertBucketsAsync(rows);

        var result = await _service.GetSeriesAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = start,
                EndUtc = start.AddDays(31),
            },
            start.AddDays(31)
        );

        Assert.Equal(pointCount, result.Points.Count);
        Assert.All(result.Points, point => Assert.Equal(2, point.SampleCount));
    }

    [Fact]
    public async Task GetSeriesAsync_最终聚合点超过一万时返回明确错误()
    {
        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        const int firstSelectorPoints = 31 * 24 * 12;
        const int secondSelectorPoints = 10_001 - firstSelectorPoints;
        var rows = CreateSeriesBuckets(start, firstSelectorPoints, ["instance-a"]);
        rows.AddRange(
            CreateSeriesBuckets(
                start,
                secondSelectorPoints,
                ["instance-a"],
                selector: "GET /api/orders 2xx",
                dimensionsHash: new string('b', 64)
            )
        );
        await InsertBucketsAsync(rows);

        var exception = await Assert.ThrowsAsync<PerformanceSeriesQueryException>(() =>
            _service.GetSeriesAsync(
                new PerformanceOverviewQueryDto
                {
                    Environment = "Production",
                    StartUtc = start,
                    EndUtc = start.AddDays(31),
                },
                start.AddDays(31)
            )
        );

        Assert.Equal("PERFORMANCE_SERIES_POINT_LIMIT_EXCEEDED", exception.ErrorCode);
    }

    [Fact]
    public async Task IngestAsync_显式空Events返回业务错误而不是抛出异常()
    {
        var result = await _service.IngestAsync(
            "hbweb_rv",
            "client",
            new PerformanceMetricBatchV1Dto { Events = null! },
            DateTime.UtcNow
        );

        Assert.False(result.Success);
        Assert.Equal("PERFORMANCE_METRIC_BATCH_INVALID", result.ErrorCode);
        Assert.Equal(0, result.Data!.RejectedCount);
    }

    [Fact]
    public async Task RecordReleaseEventAsync_仅AcceptedDeploy启动一次十四天观察周期()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var failed = Release(Guid.NewGuid(), "deploy", "failed", now);
        var accepted = Release(Guid.NewGuid(), "deploy", "accepted", now.AddMinutes(5));
        accepted.Environment = "production";

        Assert.True((await _service.RecordReleaseEventAsync(failed, now)).Success);
        Assert.Equal("not_started", (await _service.GetBaselineStatusAsync("Production")).State);
        Assert.True((await _service.RecordReleaseEventAsync(accepted, now.AddMinutes(5))).Success);
        Assert.True((await _service.RecordReleaseEventAsync(accepted, now.AddMinutes(5))).Success);

        var status = await _service.GetBaselineStatusAsync("Production");
        Assert.Equal("observing", status.State);
        Assert.Equal(now.AddMinutes(5), status.ObservationStartedAtUtc);
        Assert.Equal(now.AddDays(14).AddMinutes(5), status.ObservationEndsAtUtc);
        Assert.Equal(1, await _db.Queryable<PerformanceBaselineCycle>().CountAsync());
        Assert.Equal(2, await _db.Queryable<PerformanceReleaseEvent>().CountAsync());
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("source")]
    public async Task RecordReleaseEventAsync_拒绝缺少追溯字段的事件(string missingField)
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var request = Release(Guid.NewGuid(), "deploy", "accepted", now);
        if (missingField == "commit")
        {
            request.Commit = " ";
        }
        else
        {
            request.Source = " ";
        }

        var result = await _service.RecordReleaseEventAsync(request, now);

        Assert.False(result.Success);
        Assert.Equal("PERFORMANCE_RELEASE_EVENT_INVALID", result.ErrorCode);
        Assert.Equal(0, await _db.Queryable<PerformanceReleaseEvent>().CountAsync());
        Assert.Equal(0, await _db.Queryable<PerformanceBaselineCycle>().CountAsync());
    }

    [Fact]
    public async Task RecordReleaseEventAsync_事件已存在但周期缺失时可恢复观察起点()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var request = Release(Guid.NewGuid(), "deploy", "accepted", now);
        await _db.Insertable(
            new PerformanceReleaseEvent
            {
                Id = request.EventId,
                Action = request.Action,
                Status = request.Status,
                Environment = request.Environment,
                Component = request.Component,
                Commit = request.Commit,
                Version = request.Version,
                StartedAtUtc = request.StartedAtUtc,
                CompletedAtUtc = request.CompletedAtUtc,
                Source = request.Source,
            }
        ).ExecuteCommandAsync();

        var result = await _service.RecordReleaseEventAsync(request, now.AddHours(1));

        Assert.True(result.Success);
        var cycle = await _db.Queryable<PerformanceBaselineCycle>().SingleAsync();
        Assert.Equal("observing", cycle.State);
        Assert.Equal(now, cycle.ObservationStartedAtUtc);
    }

    [Fact]
    public async Task RecordReleaseEventAsync_重复Id载荷不一致返回冲突且不得启动观察周期()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();
        var persisted = Release(eventId, "deploy", "failed", now);
        var conflicting = Release(eventId, "deploy", "accepted", now);

        Assert.True((await _service.RecordReleaseEventAsync(persisted, now)).Success);

        var result = await _service.RecordReleaseEventAsync(conflicting, now.AddMinutes(1));

        Assert.False(result.Success);
        Assert.Equal("PERFORMANCE_RELEASE_EVENT_CONFLICT", result.ErrorCode);
        Assert.Equal(0, await _db.Queryable<PerformanceBaselineCycle>().CountAsync());
        var stored = await _db.Queryable<PerformanceReleaseEvent>().SingleAsync();
        Assert.Equal("failed", stored.Status);
    }

    [Fact]
    public async Task GetOverviewAsync_发布事件包含失败和验收记录并保留追溯字段()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var failedDeploy = Release(Guid.NewGuid(), "deploy", "failed", now);
        failedDeploy.Component = "backend";
        failedDeploy.Version = "release-failed";
        var acceptedDeploy = Release(
            Guid.NewGuid(),
            "deploy",
            "accepted",
            now.AddMinutes(1)
        );
        acceptedDeploy.Component = "web";
        acceptedDeploy.Version = "release-web";
        var acceptedRollback = Release(
            Guid.NewGuid(),
            "rollback",
            "accepted",
            now.AddMinutes(2)
        );
        acceptedRollback.Component = "backend";
        acceptedRollback.Version = "release-rollback";

        await _service.RecordReleaseEventAsync(failedDeploy, now);
        await _service.RecordReleaseEventAsync(acceptedDeploy, now.AddMinutes(1));
        await _service.RecordReleaseEventAsync(acceptedRollback, now.AddMinutes(2));

        var overview = await _service.GetOverviewAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = now.AddMinutes(-1),
                EndUtc = now.AddMinutes(3),
            },
            now.AddMinutes(3)
        );

        Assert.Equal(1, overview.AcceptedDeployments);
        Assert.Equal(1, overview.AcceptedRollbacks);
        Assert.Equal(3, overview.ReleaseEvents.Count);
        Assert.Equal("rollback", overview.ReleaseEvents[0].Action);
        Assert.Contains(
            overview.ReleaseEvents,
            item =>
                item.Status == "failed"
                && item.Component == "backend"
                && item.Commit == failedDeploy.Commit
                && item.Version == "release-failed"
                && item.Source == failedDeploy.Source
        );
    }

    [Fact]
    public async Task FreezeBaselineAsync_十四天前拒绝且不改变观察状态()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );

        var result = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(13)
        );

        Assert.False(result.Success);
        Assert.Equal("PERFORMANCE_BASELINE_WINDOW_INCOMPLETE", result.ErrorCode);
        Assert.Equal("observing", (await _service.GetBaselineStatusAsync("Production")).State);
    }

    [Fact]
    public async Task FreezeBaselineAsync_公开键样本不进入正式看板或冻结但认证来源样本可以()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var dimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["metricId"] = "products.table",
            ["outcome"] = "success",
        };
        for (var index = 0; index < 30; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "client",
                    50,
                    now.AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);
        var overview = await _service.GetOverviewAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = now,
                EndUtc = now.AddHours(1),
            },
            now.AddHours(1)
        );
        var series = await _service.GetSeriesAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = now,
                EndUtc = now.AddHours(1),
            },
            now.AddHours(1)
        );

        Assert.Empty(overview.WebAndPos);
        Assert.Empty(series.Points);

        var publicOnly = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );

        Assert.False(publicOnly.Success);
        Assert.Equal("PERFORMANCE_BASELINE_INSUFFICIENT", publicOnly.ErrorCode);
        Assert.Equal("observing", (await _service.GetBaselineStatusAsync("Production")).State);

        for (var index = 0; index < 30; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "web-baseline-manager",
                    75,
                    now.AddDays(16).AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var trusted = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(17)
        );
        var definition = await _db.Queryable<PerformanceBaselineDefinition>().SingleAsync();

        Assert.True(trusted.Success);
        Assert.Equal("frozen", trusted.Data!.State);
        Assert.Equal("qualified", definition.CoverageState);
        Assert.Equal(30, definition.SampleCount);
    }

    [Fact]
    public async Task FreezeBaselineAsync_低积压P95按真实计数生成阈值()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var values = new double[] { 0, 1, 2, 3, 3 };
        for (var index = 0; index < values.Length; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.HqSyncBacklog,
                    "backend",
                    "Production",
                    "operational-run",
                    values[index],
                    now.AddMinutes(index),
                    new Dictionary<string, string> { ["operation"] = "product-sync" }
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var frozen = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );
        var definition = await _db.Queryable<PerformanceBaselineDefinition>().SingleAsync();

        Assert.True(frozen.Success);
        Assert.Equal(5, definition.SampleCount);
        Assert.Equal(3, definition.P95);
        Assert.Equal(4, definition.WarningThreshold);
    }

    [Fact]
    public async Task FreezeBaselineAsync_并发冻结不会重复或覆盖定义()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var dimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["route"] = "/api/products/{id}",
            ["method"] = "GET",
            ["statusClass"] = "2xx",
        };
        for (var index = 0; index < 100; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.ApiRequestDuration,
                    "backend",
                    "Production",
                    "api",
                    100 + index,
                    now.AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var results = await Task.WhenAll(
            _service.FreezeBaselineAsync("Production", "admin-a", now.AddDays(15)),
            _service.FreezeBaselineAsync("Production", "admin-b", now.AddDays(15))
        );

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, await _db.Queryable<PerformanceBaselineDefinition>().CountAsync());
        var definition = await _db.Queryable<PerformanceBaselineDefinition>().SingleAsync();
        Assert.Equal("qualified", definition.CoverageState);
        Assert.Equal(100, definition.SampleCount);
    }

    [Fact]
    public async Task FreezeBaselineAsync_已冻结后仅补冻原数据不足指标且保持既有冻结值()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var apiDimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["route"] = "/api/products/{id}",
            ["method"] = "GET",
            ["statusClass"] = "2xx",
        };
        var webDimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["metricId"] = "products.table",
            ["outcome"] = "success",
        };
        for (var index = 0; index < 100; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.ApiRequestDuration,
                    "backend",
                    "Production",
                    "api",
                    100,
                    now.AddMinutes(index),
                    apiDimensions
                )
            );
        }
        for (var index = 0; index < 10; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "web-authenticated",
                    50,
                    now.AddMinutes(index),
                    webDimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var firstFreeze = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );
        var firstDefinitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .ToListAsync();
        var frozenApi = Assert.Single(
            firstDefinitions,
            item => item.MetricName == PerformanceMetricNames.ApiRequestDuration
        );
        var insufficientWeb = Assert.Single(
            firstDefinitions,
            item => item.MetricName == PerformanceMetricNames.WebTableRenderToPaint
        );
        Assert.True(firstFreeze.Success);
        Assert.Equal("qualified", frozenApi.CoverageState);
        Assert.Equal("insufficient", insufficientWeb.CoverageState);

        for (var index = 0; index < 100; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.ApiRequestDuration,
                    "backend",
                    "Production",
                    "api",
                    10_000,
                    now.AddDays(16).AddMinutes(index),
                    apiDimensions
                )
            );
        }
        for (var index = 0; index < 20; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "web-authenticated",
                    60,
                    now.AddDays(16).AddMinutes(index),
                    webDimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var secondFreeze = await _service.FreezeBaselineAsync(
            "Production",
            "admin-2",
            now.AddDays(17)
        );
        var finalDefinitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .ToListAsync();
        var finalApi = Assert.Single(
            finalDefinitions,
            item => item.MetricName == PerformanceMetricNames.ApiRequestDuration
        );
        var finalWeb = Assert.Single(
            finalDefinitions,
            item => item.MetricName == PerformanceMetricNames.WebTableRenderToPaint
        );

        Assert.True(secondFreeze.Success);
        Assert.Equal(frozenApi.P95, finalApi.P95);
        Assert.Equal(frozenApi.WarningThreshold, finalApi.WarningThreshold);
        Assert.Equal("qualified", finalWeb.CoverageState);
        Assert.Equal(30, finalWeb.SampleCount);
        Assert.Equal(1, await _db.Queryable<PerformanceBaselineCycle>().CountAsync());
    }

    [Fact]
    public async Task FreezeBaselineAsync_首轮全部不足后使用延长窗口继续候选观察()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var dimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["metricId"] = "products.table",
            ["outcome"] = "success",
        };
        for (var index = 0; index < 10; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "web-authenticated",
                    50,
                    now.AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var first = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );
        var observingCycle = await _db.Queryable<PerformanceBaselineCycle>().SingleAsync();

        Assert.False(first.Success);
        Assert.Equal("PERFORMANCE_BASELINE_INSUFFICIENT", first.ErrorCode);
        Assert.Equal("observing", observingCycle.State);
        Assert.NotNull(observingCycle.CandidateGeneratedAtUtc);

        for (var index = 0; index < 20; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.WebTableRenderToPaint,
                    "hbweb_rv",
                    "Production",
                    "web-authenticated",
                    60,
                    now.AddDays(16).AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var second = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(17)
        );
        var definition = await _db.Queryable<PerformanceBaselineDefinition>().SingleAsync();

        Assert.True(second.Success);
        Assert.Equal("frozen", second.Data!.State);
        Assert.Equal("qualified", definition.CoverageState);
        Assert.Equal(30, definition.SampleCount);
    }

    [Fact]
    public async Task FreezeBaselineAsync_Web确定性体积使用原始样本精确P95()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var apiDimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["route"] = "/api/products",
            ["method"] = "GET",
            ["statusClass"] = "2xx",
        };
        for (var index = 0; index < 100; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.ApiRequestDuration,
                    "backend",
                    "Production",
                    "api",
                    100,
                    now.AddMinutes(index),
                    apiDimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);

        var bundleEvents = Enumerable
            .Range(0, 30)
            .Select(index => new PerformanceMetricEventV1Dto
            {
                EventId = Guid.NewGuid(),
                Metric = PerformanceMetricNames.WebFirstScreenBytes,
                ObservedAt = now.AddHours(2).AddMinutes(index),
                Value = index == 29 ? 1_900_000 : 1_782_872,
                Unit = PerformanceMetricUnits.Bytes,
                Dimensions = new Dictionary<string, string>
                {
                    ["environment"] = "Production",
                    ["lane"] = "web",
                    ["outcome"] = "success",
                },
            })
            .ToList();
        var ingest = await _service.IngestAsync(
            "quality-ci",
            "ci",
            new PerformanceMetricBatchV1Dto { Events = bundleEvents },
            now.AddHours(3)
        );

        var frozen = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );
        var definition = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .Where(item => item.MetricName == PerformanceMetricNames.WebFirstScreenBytes)
            .SingleAsync();

        Assert.True(ingest.Success);
        Assert.True(frozen.Success);
        Assert.Equal(30, definition.SampleCount);
        Assert.Equal(1_782_872, definition.P95);
        Assert.Equal("web_bundle_hard", definition.GatePolicy);
    }

    [Fact]
    public async Task IngestAsync_冻结后返回稳定百分之二十采样率和慢事件阈值()
    {
        var now = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        await _service.RecordReleaseEventAsync(
            Release(Guid.NewGuid(), "deploy", "accepted", now),
            now
        );
        var dimensions = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            ["route"] = "/api/products/{id}",
            ["method"] = "GET",
            ["statusClass"] = "2xx",
        };
        for (var index = 0; index < 100; index++)
        {
            _buffer.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.ApiRequestDuration,
                    "backend",
                    "Production",
                    "api",
                    100,
                    now.AddMinutes(index),
                    dimensions
                )
            );
        }
        await _buffer.FlushAsync(_db);
        var frozen = await _service.FreezeBaselineAsync(
            "Production",
            "admin",
            now.AddDays(15)
        );

        var ingest = await _service.IngestAsync(
            "backend",
            "api",
            Batch(
                Guid.NewGuid(),
                PerformanceMetricNames.ApiRequestDuration,
                110,
                dimensions,
                now.AddDays(15)
            ),
            now.AddDays(15)
        );

        Assert.True(frozen.Success);
        Assert.Equal("frozen", ingest.Data!.BaselineState);
        Assert.Equal(1, ingest.Data.DefaultSampleRate);
        var policy = Assert.Single(ingest.Data.Policies);
        Assert.Equal("GET /api/products/{id} 2xx", policy.Selector);
        Assert.Equal(0.2, policy.SampleRate);
        Assert.Equal(120, policy.SlowThreshold);

        var insufficient = await _service.IngestAsync(
            "hbweb_rv",
            "client",
            Batch(
                Guid.NewGuid(),
                PerformanceMetricNames.WebTableRenderToPaint,
                50,
                new Dictionary<string, string>
                {
                    ["environment"] = "Production",
                    ["metricId"] = "products.insufficient",
                    ["outcome"] = "success",
                },
                now.AddDays(15)
            ),
            now.AddDays(15)
        );

        Assert.True(insufficient.Success);
        Assert.Equal(1, insufficient.Data!.DefaultSampleRate);
        Assert.Empty(insufficient.Data.Policies);
    }

    [Fact]
    public async Task GetSlowSqlAsync_窗口选择覆盖看板全局范围并按上下文分组()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        _buffer.Record(
            new PerformanceMetricRecord(
                PerformanceMetricNames.SqlCommandDuration,
                "backend",
                "Production",
                "sql",
                900,
                now.AddHours(-2),
                new Dictionary<string, string>
                {
                    ["databaseContext"] = "OldContext",
                    ["sqlFingerprint"] = "old",
                    ["sqlTemplate"] = "SELECT * FROM OLD_TABLE",
                }
            )
        );
        _buffer.Record(
            new PerformanceMetricRecord(
                PerformanceMetricNames.SqlCommandDuration,
                "backend",
                "Production",
                "sql",
                200,
                now.AddMinutes(-30),
                new Dictionary<string, string>
                {
                    ["databaseContext"] = "MainContext",
                    ["sqlFingerprint"] = "recent",
                    ["sqlTemplate"] = "SELECT * FROM RECENT_TABLE",
                }
            )
        );
        await _buffer.FlushAsync(_db);

        var rows = await _service.GetSlowSqlAsync(
            new PerformanceSlowSqlQueryDto
            {
                Environment = "Production",
                StartUtc = now.AddDays(-7),
                EndUtc = now,
                Window = "1h",
                SortBy = "total",
            },
            now
        );

        var row = Assert.Single(rows);
        Assert.Equal("MainContext", row.DatabaseContext);
        Assert.Equal("recent", row.Fingerprint);
    }

    [Fact]
    public async Task GetOverviewAsync_任务重试后只按最终运行状态计算成功失败率()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        await _db.Insertable(
            new PerformanceOperationalRun
            {
                ExternalRunId = "retried-job",
                Category = "hq",
                Operation = "product-sync",
                Status = "success",
                Attempt = 2,
                Environment = "Production",
                Source = "backend",
                QueuedAtUtc = now.AddSeconds(-6),
                StartedAtUtc = now.AddSeconds(-5),
                CompletedAtUtc = now,
                DurationMs = 5000,
            }
        ).ExecuteCommandAsync();

        var overview = await _service.GetOverviewAsync(
            new PerformanceOverviewQueryDto
            {
                Environment = "Production",
                StartUtc = now.AddMinutes(-1),
                EndUtc = now.AddMinutes(1),
            },
            now.AddMinutes(1)
        );

        var successRate = Assert.Single(
            overview.HqAndJobs,
            item => item.Metric == PerformanceMetricNames.HqSyncSuccessRate
        );
        var failureRate = Assert.Single(
            overview.HqAndJobs,
            item => item.Metric == PerformanceMetricNames.HqSyncFailureRate
        );
        var duration = Assert.Single(
            overview.HqAndJobs,
            item => item.Metric == PerformanceMetricNames.HqSyncDuration
        );
        Assert.Equal(1, successRate.SampleCount);
        Assert.Equal(1, successRate.Average);
        Assert.Equal(0, failureRate.Average);
        Assert.Equal(5000, duration.P95);
    }

    [Fact]
    public async Task SchemaMigrator_重复执行不删除现有样本且保持事件唯一索引()
    {
        var sample = new PerformanceMetricSample
        {
            EventId = Guid.NewGuid(),
            ProjectCode = "web",
            Environment = "Production",
            SourceType = "web",
            MetricName = PerformanceMetricNames.WebTableRenderToPaint,
            ObservedAtUtc = DateTime.UtcNow,
            Value = 10,
            Unit = PerformanceMetricUnits.Milliseconds,
            DimensionsHash = new string('a', 64),
        };
        await _db.Insertable(sample).ExecuteCommandAsync();

        await PerformanceBaselineSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);

        Assert.Equal(1, await _db.Queryable<PerformanceMetricSample>().CountAsync());
        var duplicate = new PerformanceMetricSample
        {
            EventId = sample.EventId,
            ProjectCode = sample.ProjectCode,
            Environment = sample.Environment,
            SourceType = sample.SourceType,
            MetricName = sample.MetricName,
            ObservedAtUtc = sample.ObservedAtUtc,
            Value = sample.Value,
            Unit = sample.Unit,
            DimensionsHash = sample.DimensionsHash,
        };
        await Assert.ThrowsAnyAsync<Exception>(
            () => _db.Insertable(duplicate).ExecuteCommandAsync()
        );

        var cycleId = Guid.NewGuid();
        var definition = new PerformanceBaselineDefinition
        {
            CycleId = cycleId,
            MetricName = PerformanceMetricNames.ApiRequestDuration,
            Selector = "GET /api/products 2xx",
            SampleCount = 100,
        };
        await _db.Insertable(definition).ExecuteCommandAsync();
        definition.Id = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(
            () => _db.Insertable(definition).ExecuteCommandAsync()
        );
    }

    [Fact]
    public void SchemaMigrator_SQLite样本索引覆盖保留和精确冻结查询()
    {
        var indexList = _db.Ado.GetDataTable("PRAGMA index_list('PerformanceMetricSample')");
        var indexNames = indexList.Rows
            .Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("IX_PerformanceMetricSample_ObservedAtUtc", indexNames);
        Assert.Contains("IX_PerformanceMetricSample_ExactWebBundle", indexNames);
        Assert.Equal(
            new[] { "ObservedAtUtc" },
            SqliteIndexColumns("IX_PerformanceMetricSample_ObservedAtUtc")
        );
        Assert.Equal(
            new[] { "Environment", "SourceType", "MetricName", "ObservedAtUtc" },
            SqliteIndexColumns("IX_PerformanceMetricSample_ExactWebBundle")
        );
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

    private static PerformanceMetricBatchV1Dto Batch(
        Guid eventId,
        string metric,
        double value,
        Dictionary<string, string> dimensions,
        DateTime observedAt
    ) =>
        new()
        {
            Events =
            [
                new PerformanceMetricEventV1Dto
                {
                    EventId = eventId,
                    Metric = metric,
                    ObservedAt = observedAt,
                    Value = value,
                    Unit = PerformanceMetricUnits.Milliseconds,
                    Dimensions = dimensions,
                },
            ],
        };

    private static List<PerformanceMetricBucket> CreateSeriesBuckets(
        DateTime start,
        int pointCount,
        IReadOnlyList<string> instanceIds,
        string selector = "GET /api/products 2xx",
        string? dimensionsHash = null
    )
    {
        var histogram = PerformanceHistogram.Create();
        histogram.Record(100);
        var histogramJson = JsonSerializer.Serialize(histogram.Counts);
        var rows = new List<PerformanceMetricBucket>(pointCount * instanceIds.Count);
        for (var point = 0; point < pointCount; point++)
        {
            foreach (var instanceId in instanceIds)
            {
                var observedAt = start.AddMinutes(point * 5);
                rows.Add(
                    new PerformanceMetricBucket
                    {
                        MetricName = PerformanceMetricNames.ApiRequestDuration,
                        ProjectCode = "backend",
                        Environment = "Production",
                        SourceType = "api",
                        InstanceId = instanceId,
                        Selector = selector,
                        DimensionsHash = dimensionsHash ?? new string('a', 64),
                        DimensionsJson = "{}",
                        WindowStartUtc = observedAt,
                        BucketSizeMinutes = 5,
                        SampleCount = 1,
                        SumValue = 100,
                        MinimumValue = 100,
                        MaximumValue = 100,
                        HistogramCountsJson = histogramJson,
                        LastObservedAtUtc = observedAt,
                    }
                );
            }
        }
        return rows;
    }

    private string[] SqliteIndexColumns(string indexName)
    {
        var table = _db.Ado.GetDataTable($"PRAGMA index_info('{indexName}')");
        return table.Rows
            .Cast<System.Data.DataRow>()
            .OrderBy(row => Convert.ToInt32(row["seqno"]))
            .Select(row => Convert.ToString(row["name"])!)
            .ToArray();
    }

    private async Task InsertBucketsAsync(IReadOnlyList<PerformanceMetricBucket> rows)
    {
        foreach (var chunk in rows.Chunk(500))
        {
            await _db.Insertable(chunk.ToList()).ExecuteCommandAsync();
        }
    }

    private static PerformanceReleaseEventRequestDto Release(
        Guid eventId,
        string action,
        string status,
        DateTime now
    ) =>
        new()
        {
            EventId = eventId,
            Action = action,
            Status = status,
            Environment = "Production",
            Component = "backend",
            Commit = "7f5f3ee",
            Version = "1.0.0",
            StartedAtUtc = now.AddMinutes(-1),
            CompletedAtUtc = now,
            Source = "test",
        };
}
