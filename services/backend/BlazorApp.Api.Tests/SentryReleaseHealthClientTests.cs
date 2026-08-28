using System.Net;
using System.Text;
using System.Globalization;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SentryReleaseHealthClientTests
{
    private const string ReadOnlyToken = "sentry-read-only-token-never-log";
    private static readonly DateTimeOffset UtcNow = new(
        2026,
        8,
        25,
        6,
        0,
        0,
        TimeSpan.Zero
    );

    [Fact]
    public async Task Options_默认滚动回读二十四小时且示例配置一致()
    {
        Assert.Equal(24, new SentryReleaseHealthOptions().LookbackHours);

        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/appsettings.PerformanceMetrics.example.json"
            )
        );
        Assert.Contains("\"LookbackHours\": 24", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_成功拉取两个项目并转换为零到一的Ratio()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            var project = Assert.Single(query["project"]);
            var release = project == "hb-pos-ipad"
                ? "com.hbweb.posipad@0.2.0"
                : "com.hbweb.poshandheld@0.1.0";
            return JsonResponse(
                ReleaseHealthJson(
                    release,
                    crashFreePercent: 99.75,
                    sessions: 400,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                )
            );
        });
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        var options = CreateOptions();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, options, logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, item =>
        {
            Assert.Equal(0.9975, item.CrashFreeSessionRatio, 8);
            Assert.Equal(400, item.SessionCount);
            Assert.Equal("production", item.Environment);
            Assert.Equal("all", item.Dist);
            Assert.Equal(UtcNow.UtcDateTime, item.ObservedAtUtc);
            Assert.Equal(DateTimeKind.Utc, item.ObservedAtUtc.Kind);
        });
        Assert.Equal(
            ["hb-pos-handheld", "hb-pos-ipad"],
            snapshots.Select(item => item.Project).Order(StringComparer.Ordinal).ToArray()
        );
        Assert.Equal(TimeSpan.FromSeconds(options.HttpTimeoutSeconds), httpClient.Timeout);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal(ReadOnlyToken, request.AuthorizationParameter);
            Assert.DoesNotContain(ReadOnlyToken, request.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.Equal("us.sentry.io", request.Uri.Host);
            Assert.Equal(
                "/api/0/organizations/hot-bargain/sessions/",
                request.Uri.AbsolutePath
            );

            var query = ParseQuery(request.Uri);
            Assert.Equal(
                ["sum(session)", "crash_free_rate(session)"],
                query["field"]
            );
            Assert.Equal(["release", "environment"], query["groupBy"]);
            Assert.Equal("production", Assert.Single(query["environment"]));
            Assert.Equal("0", Assert.Single(query["includeSeries"]));
            Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(Assert.Single(query["start"])).Offset);
            Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(Assert.Single(query["end"])).Offset);
        });
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(ReadOnlyToken, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task SyncOnceAsync_调度频率不改变Sentry固定一小时数据窗口()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            return JsonResponse(
                ReleaseHealthJson(
                    "com.hbweb.pos@1.2.3",
                    crashFreePercent: 99.9,
                    sessions: 1000,
                    extraByProperty: "\"store\":\"secret-store\"",
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                )
            );
        });
        var options = CreateOptions();
        options.LookbackHours = 1;
        options.SyncIntervalMinutes = 15;
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            options,
            NullLogger<SentryReleaseHealthClient>.Instance
        );
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentry-sync-{Guid.NewGuid():N}.db");
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        try
        {
            await PerformanceBaselineSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            var aggregateStore = new PerformanceMetricAggregateStore(
                Options.Create(new PerformanceMetricsOptions { InstanceId = "test" })
            );
            Assert.Equal(
                1,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    client,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("instance-a"),
                    options,
                    UtcNow
                )
            );
            Assert.Equal(
                1,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    client,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("instance-b"),
                    options,
                    UtcNow.AddSeconds(5)
                )
            );

            var buckets = await db.Queryable<PerformanceMetricBucket>().ToListAsync();
            Assert.Equal(2, buckets.Count);
            Assert.All(buckets, metric =>
            {
                Assert.Equal(PerformanceMetricNames.SentryCrashFreeSession, metric.MetricName);
                Assert.Equal(1000, metric.SampleCount);
                Assert.Equal(999, metric.SumValue, 8);
                Assert.Equal("sentry-release-health", metric.SourceType);
                Assert.Equal("Production", metric.Environment);
                Assert.Equal(UtcNow.UtcDateTime, metric.LastObservedAtUtc);
                var dimensions = PerformanceMetricDimensions.Parse(metric.DimensionsJson);
                Assert.Equal(
                    ["dist", "environment", "project", "release"],
                    dimensions.Keys.Order(StringComparer.Ordinal).ToArray()
                );
                Assert.Equal(metric.ProjectCode, dimensions["project"]);
                Assert.Equal("Production", dimensions["environment"]);
                Assert.Equal("com.hbweb.pos@1.2.3", dimensions["release"]);
                Assert.Equal("all", dimensions["dist"]);
                Assert.DoesNotContain("store", dimensions.Keys);
            });
            Assert.Equal(4, handler.Requests.Count);
        }
        finally
        {
            db.Dispose();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SyncOnceAsync_成功窗口滚动回读时迟到Session替换快照而不累加()
    {
        var lateSessionsArrived = false;
        var lateWindowStart = UtcNow.AddHours(-3);
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            var start = DateTimeOffset.Parse(Assert.Single(query["start"]));
            var end = DateTimeOffset.Parse(Assert.Single(query["end"]));
            if (start != lateWindowStart)
            {
                return JsonResponse(EmptyReleaseHealthJson(start, end));
            }
            return JsonResponse(
                ReleaseHealthJson(
                    "late-session@1.0.0",
                    crashFreePercent: lateSessionsArrived ? 95 : 90,
                    sessions: lateSessionsArrived ? 120 : 100,
                    startUtc: start,
                    endUtc: end
                )
            );
        });
        var options = CreateOptions();
        options.LookbackHours = 3;
        options.SyncIntervalMinutes = 15;
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            options,
            NullLogger<SentryReleaseHealthClient>.Instance
        );
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentry-late-{Guid.NewGuid():N}.db");
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        try
        {
            await PerformanceBaselineSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            var aggregateStore = new PerformanceMetricAggregateStore(
                Options.Create(new PerformanceMetricsOptions { InstanceId = "test" })
            );
            Assert.Equal(
                3,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    client,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("first-instance"),
                    options,
                    UtcNow
                )
            );

            lateSessionsArrived = true;
            Assert.Equal(
                3,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    client,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("second-instance"),
                    options,
                    UtcNow.AddMinutes(15)
                )
            );

            var buckets = await db.Queryable<PerformanceMetricBucket>().ToListAsync();
            Assert.Equal(2, buckets.Count);
            Assert.All(buckets, bucket =>
            {
                Assert.Equal(120, bucket.SampleCount);
                Assert.Equal(114, bucket.SumValue, 8);
                Assert.Equal(0.95, bucket.SumValue / bucket.SampleCount, 8);
            });
            Assert.Equal(12, handler.Requests.Count);
        }
        finally
        {
            db.Dispose();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SyncOnceAsync_多窗口处理按可注入当前时间续租()
    {
        var options = CreateOptions();
        options.LookbackHours = 2;
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentry-clock-{Guid.NewGuid():N}.db");
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        DateTime? renewedLeaseExpiry = null;
        var requestCount = 0;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestCount++;
            if (requestCount == 3)
            {
                renewedLeaseExpiry = db
                    .Queryable<PerformanceCollectorState>()
                    .Single()
                    .LeaseExpiresAtUtc;
            }
            var query = ParseQuery(request.RequestUri!);
            return JsonResponse(
                ReleaseHealthJson(
                    "lease-clock@1.0.0",
                    100,
                    1,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                )
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            options,
            NullLogger<SentryReleaseHealthClient>.Instance
        );
        try
        {
            await PerformanceBaselineSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            var method = Assert.Single(
                typeof(SentryReleaseHealthSyncService).GetMethods(
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                ),
                candidate =>
                    candidate.Name == "SyncOnceAsync"
                    && candidate.GetParameters().Any(parameter => parameter.ParameterType == typeof(TimeProvider))
            );
            var aggregateStore = new PerformanceMetricAggregateStore(
                Options.Create(new PerformanceMetricsOptions { InstanceId = "test" })
            );
            var clock = new SequenceTimeProvider(
                UtcNow.AddMinutes(1),
                UtcNow.AddMinutes(2)
            );
            var task = Assert.IsType<Task<int>>(
                method.Invoke(
                    null,
                    [
                        db,
                        client,
                        aggregateStore,
                        new PerformanceCollectorCoordinator("clock-instance"),
                        options,
                        UtcNow,
                        clock,
                        CancellationToken.None,
                    ]
                )
            );

            Assert.Equal(2, await task);
            Assert.Equal(UtcNow.UtcDateTime.AddMinutes(4), renewedLeaseExpiry);
        }
        finally
        {
            db.Dispose();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SyncOnceAsync_单窗口慢分页期间逐页续租后仍可原子提交()
    {
        var options = CreateOptions();
        options.LookbackHours = 1;
        options.HttpTimeoutSeconds = 15;
        var clock = new MutableTimeProvider(UtcNow);
        var handler = new CaptureHttpMessageHandler(request =>
        {
            clock.Advance(TimeSpan.FromSeconds(70));
            var query = ParseQuery(request.RequestUri!);
            var project = Assert.Single(query["project"]);
            var start = DateTimeOffset.Parse(Assert.Single(query["start"]));
            var end = DateTimeOffset.Parse(Assert.Single(query["end"]));
            if (!query.ContainsKey("cursor"))
            {
                return JsonResponse(
                    ReleaseHealthJson(
                        $"{project}@1.0.0",
                        99.9,
                        100,
                        startUtc: start,
                        endUtc: end
                    ),
                    $"<{WithCursor(request.RequestUri!, $"{project}-next")}>; rel=\"next\"; results=\"true\""
                );
            }

            return JsonResponse(
                ReleaseHealthJson(
                    $"{project}@1.0.0",
                    99.9,
                    100,
                    startUtc: start,
                    endUtc: end
                )
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            options,
            NullLogger<SentryReleaseHealthClient>.Instance
        );
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentry-page-lease-{Guid.NewGuid():N}.db");
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        try
        {
            await PerformanceBaselineSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            var aggregateStore = new PerformanceMetricAggregateStore(
                Options.Create(new PerformanceMetricsOptions { InstanceId = "test" })
            );

            var processed = await SentryReleaseHealthSyncService.SyncOnceAsync(
                db,
                client,
                aggregateStore,
                new PerformanceCollectorCoordinator("slow-page-instance"),
                options,
                UtcNow,
                clock
            );

            Assert.Equal(1, processed);
            Assert.Equal(4, handler.Requests.Count);
            Assert.Equal(2, await db.Queryable<PerformanceMetricBucket>().CountAsync());
            var state = await db.Queryable<PerformanceCollectorState>().SingleAsync();
            Assert.Equal(UtcNow.UtcDateTime, state.CursorUtc);
            Assert.Null(state.LeaseOwner);
            Assert.Null(state.LeaseExpiresAtUtc);
        }
        finally
        {
            db.Dispose();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SyncOnceAsync_窗口失败不推进游标且重试不会形成缺口()
    {
        var options = CreateOptions();
        options.LookbackHours = 1;
        options.SyncIntervalMinutes = 60;
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentry-retry-{Guid.NewGuid():N}.db");
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        try
        {
            await PerformanceBaselineSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            var aggregateStore = new PerformanceMetricAggregateStore(
                Options.Create(new PerformanceMetricsOptions { InstanceId = "test" })
            );
            using var failedHttp = new HttpClient(
                new CaptureHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))
            );
            var failedClient = CreateClient(
                failedHttp,
                options,
                NullLogger<SentryReleaseHealthClient>.Instance
            );

            Assert.Equal(
                0,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    failedClient,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("failed-instance"),
                    options,
                    UtcNow
                )
            );
            var failedState = await db.Queryable<PerformanceCollectorState>().SingleAsync();
            Assert.Equal(UtcNow.UtcDateTime.AddHours(-1), failedState.CursorUtc);
            Assert.Equal(0, await db.Queryable<PerformanceMetricBucket>().CountAsync());

            using var successHttp = new HttpClient(
                new CaptureHttpMessageHandler(request =>
                {
                    var query = ParseQuery(request.RequestUri!);
                    return JsonResponse(
                        ReleaseHealthJson(
                            "retry@1.0.0",
                            100,
                            100,
                            startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                            endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                        )
                    );
                })
            );
            var successClient = CreateClient(
                successHttp,
                options,
                NullLogger<SentryReleaseHealthClient>.Instance
            );
            Assert.Equal(
                1,
                await SentryReleaseHealthSyncService.SyncOnceAsync(
                    db,
                    successClient,
                    aggregateStore,
                    new PerformanceCollectorCoordinator("retry-instance"),
                    options,
                    UtcNow.AddSeconds(5)
                )
            );
            var succeededState = await db.Queryable<PerformanceCollectorState>().SingleAsync();
            Assert.Equal(UtcNow.UtcDateTime, succeededState.CursorUtc);
        }
        finally
        {
            db.Dispose();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task FetchAsync_LinkNext分页时合并重复Selector并按Session加权()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            var project = Assert.Single(query["project"]);
            var start = DateTimeOffset.Parse(Assert.Single(query["start"]));
            var end = DateTimeOffset.Parse(Assert.Single(query["end"]));
            if (!query.ContainsKey("cursor"))
            {
                return JsonResponse(
                    ReleaseHealthJson("shared@1.0.0", 90, 100, startUtc: start, endUtc: end),
                    $"<{WithCursor(request.RequestUri!, $"{project}-next")}>; rel=\"next\"; results=\"true\""
                );
            }

            return JsonResponse(
                ReleaseHealthJson("shared@1.0.0", 100, 300, startUtc: start, endUtc: end)
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateOptions(),
            NullLogger<SentryReleaseHealthClient>.Instance
        );

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.True(result.Complete);
        Assert.Equal(2, result.Snapshots.Count);
        Assert.All(result.Snapshots, snapshot =>
        {
            Assert.Equal(400, snapshot.SessionCount);
            Assert.Equal(0.975, snapshot.CrashFreeSessionRatio, 8);
            Assert.Equal("shared@1.0.0", snapshot.Release);
        });
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("us.sentry.io", request.Uri.Host);
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal(ReadOnlyToken, request.AuthorizationParameter);
            Assert.DoesNotContain(ReadOnlyToken, request.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task FetchAsync_LinkNextResultsFalse时停止而不请求下一页()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            return JsonResponse(
                ReleaseHealthJson(
                    "single-page@1.0.0",
                    99,
                    100,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                ),
                "<https://invalid.example/ignored>; rel=\"next\"; results=\"false\""
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateOptions(),
            NullLogger<SentryReleaseHealthClient>.Instance
        );

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.True(result.Complete);
        Assert.Equal(2, result.Snapshots.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request => Assert.DoesNotContain("cursor=", request.Uri.Query, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task FetchAsync_恶意NextUrl会拒绝且不向外部发送Token()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            return JsonResponse(
                ReleaseHealthJson(
                    "unsafe-next@1.0.0",
                    99,
                    100,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                ),
                $"<https://attacker.example/api/0/organizations/hot-bargain/sessions/?token={ReadOnlyToken}>; rel=\"next\"; results=\"true\""
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateOptions(),
            NullLogger<SentryReleaseHealthClient>.Instance
        );

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.False(result.Complete);
        Assert.Empty(result.Snapshots);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("us.sentry.io", request.Uri.Host));
    }

    [Fact]
    public async Task FetchAsync_Next页超过安全上限时标记不完整()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            var page = query.TryGetValue("cursor", out var cursors)
                ? int.Parse(Assert.Single(cursors), CultureInfo.InvariantCulture)
                : 1;
            var response = JsonResponse(
                ReleaseHealthJson(
                    "many-pages@1.0.0",
                    99,
                    1,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                )
            );
            response.Headers.TryAddWithoutValidation(
                "Link",
                $"<{WithCursor(request.RequestUri!, (page + 1).ToString(CultureInfo.InvariantCulture))}>; rel=\"next\"; results=\"true\""
            );
            return response;
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateOptions(),
            NullLogger<SentryReleaseHealthClient>.Instance
        );

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.False(result.Complete);
        Assert.Empty(result.Snapshots);
        Assert.Equal(20, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_第二页失败时不返回虚假完整结果()
    {
        var handler = new CaptureHttpMessageHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            if (query.ContainsKey("cursor"))
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            return JsonResponse(
                ReleaseHealthJson(
                    "second-page-failure@1.0.0",
                    99,
                    100,
                    startUtc: DateTimeOffset.Parse(Assert.Single(query["start"])),
                    endUtc: DateTimeOffset.Parse(Assert.Single(query["end"]))
                ),
                $"<{WithCursor(request.RequestUri!, "next")}>; rel=\"next\"; results=\"true\""
            );
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateOptions(),
            NullLogger<SentryReleaseHealthClient>.Instance
        );

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.False(result.Complete);
        Assert.Empty(result.Snapshots);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_缺失Session或CrashFree数据时不返回虚假零值()
    {
        const string json = """
{
  "start": "2026-08-24T06:00:00Z",
  "end": "2026-08-25T06:00:00Z",
  "intervals": [],
  "groups": [
    {
      "by": { "release": "empty@1.0.0", "environment": "production" },
      "totals": { "sum(session)": 0, "crash_free_rate(session)": 0 },
      "series": {}
    },
    {
      "by": { "release": "missing@1.0.0", "environment": "production" },
      "totals": { "sum(session)": 12 },
      "series": {}
    },
    {
      "by": { "release": "fractional@1.0.0", "environment": "production" },
      "totals": { "sum(session)": 1.5, "crash_free_rate(session)": 99.9 },
      "series": {}
    }
  ],
  "query": ""
}
""";
        var handler = new CaptureHttpMessageHandler(_ => JsonResponse(json));
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, CreateOptions(), logger);

        var result = await client.FetchWindowAsync(UtcNow.AddHours(-24), UtcNow);

        Assert.False(result.Complete);
        Assert.Empty(result.Snapshots);
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FetchAsync_非成功状态只警告且不产生指标(HttpStatusCode statusCode)
    {
        var handler = new CaptureHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, CreateOptions(), logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Fact]
    public async Task FetchAsync_畸形Json只警告且不产生指标()
    {
        var handler = new CaptureHttpMessageHandler(_ => JsonResponse("{\"groups\":["));
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, CreateOptions(), logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Fact]
    public async Task FetchAsync_网络异常只警告且不泄露Token()
    {
        var handler = new CaptureHttpMessageHandler(_ =>
            throw new HttpRequestException($"transport failed: {ReadOnlyToken}")
        );
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, CreateOptions(), logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.DoesNotContain(ReadOnlyToken, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.Exception);
        });
    }

    [Fact]
    public async Task FetchAsync_响应体超过上限时只警告且不解析()
    {
        var options = CreateOptions();
        options.MaxResponseBodyBytes = 1024;
        var handler = new CaptureHttpMessageHandler(_ =>
            JsonResponse(new string('x', options.MaxResponseBodyBytes + 1))
        );
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, options, logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("invalid token")]
    public async Task FetchAsync_配置缺失或Token格式不安全时禁用且不发请求(string token)
    {
        var options = CreateOptions();
        options.ReadOnlyAuthToken = token;
        var handler = new CaptureHttpMessageHandler(_ =>
            throw new InvalidOperationException("配置缺失时不应发请求")
        );
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, options, logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.Empty(handler.Requests);
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public async Task FetchAsync_错误响应与日志均不泄露只读Token()
    {
        var handler = new CaptureHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = ReadOnlyToken,
                Content = new StringContent(ReadOnlyToken, Encoding.UTF8, "text/plain"),
            }
        );
        var logger = new RecordingLogger<SentryReleaseHealthClient>();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, CreateOptions(), logger);

        var snapshots = await client.FetchAsync(UtcNow);

        Assert.Empty(snapshots);
        Assert.All(
            handler.Requests,
            request => Assert.DoesNotContain(
                ReadOnlyToken,
                request.Uri.AbsoluteUri,
                StringComparison.Ordinal
            )
        );
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(ReadOnlyToken, StringComparison.Ordinal)
                || entry.Exception?.ToString().Contains(ReadOnlyToken, StringComparison.Ordinal) == true
        );
    }

    private static SentryReleaseHealthClient CreateClient(
        HttpClient httpClient,
        SentryReleaseHealthOptions options,
        ILogger<SentryReleaseHealthClient> logger
    ) => new(httpClient, Options.Create(options), logger);

    private static SentryReleaseHealthOptions CreateOptions() =>
        new()
        {
            Enabled = true,
            BaseUrl = "https://us.sentry.io/",
            OrganizationSlug = "hot-bargain",
            ReadOnlyAuthToken = ReadOnlyToken,
            Environment = "production",
            MetricEnvironment = "Production",
            LookbackHours = 24,
            SyncIntervalMinutes = 15,
            HttpTimeoutSeconds = 7,
            MaxResponseBodyBytes = 16 * 1024,
        };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根目录");
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private int _index;

        public override DateTimeOffset GetUtcNow()
        {
            var index = Math.Min(_index, values.Length - 1);
            _index++;
            return values[index];
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private static HttpResponseMessage JsonResponse(string json, string? link = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(link))
        {
            response.Headers.TryAddWithoutValidation("Link", link);
        }
        return response;
    }

    private static string ReleaseHealthJson(
        string release,
        double crashFreePercent,
        double sessions,
        string? extraByProperty = null,
        DateTimeOffset? startUtc = null,
        DateTimeOffset? endUtc = null
    )
    {
        var extra = string.IsNullOrWhiteSpace(extraByProperty)
            ? string.Empty
            : $", {extraByProperty}";
        var start = (startUtc ?? UtcNow.AddHours(-24)).ToUniversalTime().ToString("O");
        var end = (endUtc ?? UtcNow).ToUniversalTime().ToString("O");
        return $$"""
{
  "start": "{{start}}",
  "end": "{{end}}",
  "intervals": [],
  "groups": [
    {
      "by": {
        "release": "{{release}}",
        "environment": "production"{{extra}}
      },
      "totals": {
        "sum(session)": {{sessions}},
        "crash_free_rate(session)": {{crashFreePercent}}
      },
      "series": {}
    }
  ],
  "query": ""
}
""";
    }

    private static string EmptyReleaseHealthJson(DateTimeOffset start, DateTimeOffset end) =>
        $$"""
{
  "start": "{{start.ToUniversalTime():O}}",
  "end": "{{end.ToUniversalTime():O}}",
  "intervals": [],
  "groups": [],
  "query": ""
}
""";

    private static Dictionary<string, List<string>> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            if (!result.TryGetValue(key, out var values))
            {
                values = [];
                result[key] = values;
            }
            values.Add(value);
        }
        return result;
    }

    private static Uri WithCursor(Uri uri, string cursor)
    {
        var builder = new UriBuilder(uri);
        var query = builder
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var separator = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
                return !key.Equals("cursor", StringComparison.OrdinalIgnoreCase);
            });
        builder.Query = !query.Any()
            ? $"cursor={Uri.EscapeDataString(cursor)}"
            : $"{string.Join("&", query)}&cursor={Uri.EscapeDataString(cursor)}";
        return builder.Uri;
    }

    private sealed class CaptureHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory
    ) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        // 中文注释：复制请求元数据后再返回可编程响应，避免测试访问真实 Sentry。
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter
                )
            );
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter
    );

    private sealed class RecordingMetricRecorder : IPerformanceMetricRecorder
    {
        public List<PerformanceMetricRecord> Records { get; } = new();

        public void Record(PerformanceMetricRecord metric) => Records.Add(metric);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
