using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceBaselineCoreTests
{
    [Fact]
    public void MetricAccumulator_关闭快照后拒绝迟到记录和合并()
    {
        var observedAt = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var accumulator = new PerformanceMetricBuffer.MetricAccumulator();

        Assert.True(accumulator.TryRecord(12, observedAt, 1));
        var snapshot = accumulator.CloseAndSnapshot();

        Assert.Equal(1, snapshot.Count);
        Assert.False(accumulator.TryRecord(24, observedAt.AddSeconds(1), 1));
        Assert.False(accumulator.TryMerge(snapshot));
        Assert.Equal(1, accumulator.CloseAndSnapshot().Count);
    }

    [Fact]
    public void Histogram_按固定桶计算可合并分位数()
    {
        var first = PerformanceHistogram.Create();
        first.Record(8);
        first.Record(22);
        first.Record(49);

        var second = PerformanceHistogram.Create();
        second.Record(430);
        second.Record(880);

        first.Merge(second);

        Assert.Equal(5, first.Count);
        Assert.Equal(50, first.EstimatePercentile(0.50));
        Assert.Equal(1000, first.EstimatePercentile(0.95));
    }

    [Fact]
    public void Histogram_首次发布前契约精确区分低计数且保持延迟桶()
    {
        // 性能基线表尚未发布；这里固定首次发布的 counts 下标语义，避免低积压被 10 桶放大。
        Assert.Equal(new double[] { 0, 1, 2, 3, 5, 10 }, PerformanceHistogram.Boundaries.Take(6));

        foreach (var value in new double[] { 0, 1, 2, 3, 5 })
        {
            var histogram = PerformanceHistogram.Create();
            histogram.Record(value);
            Assert.Equal(value, histogram.EstimatePercentile(0.95));
        }

        var latency = PerformanceHistogram.Create();
        latency.Record(8);
        Assert.Equal(10, latency.EstimatePercentile(0.95));
    }

    [Fact]
    public void SqlFingerprint_不同字面量归并且不泄露原值()
    {
        const string firstSql =
            "SELECT * FROM Orders WHERE Id = 123 AND Customer = 'Alice Secret' -- private";
        const string secondSql =
            "SELECT * FROM Orders WHERE Id = 987 AND Customer = 'Bob Secret' -- another";

        var first = SqlPerformanceFingerprint.Create(firstSql);
        var second = SqlPerformanceFingerprint.Create(secondSql);

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(first.Template, second.Template);
        Assert.DoesNotContain("123", first.Template, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", first.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", first.Template, StringComparison.OrdinalIgnoreCase);
        Assert.True(first.Template.Length <= 500);
    }

    [Fact]
    public void SqlFingerprint_长模板哈希不因展示截断碰撞且清理PostgresDollarQuote()
    {
        var prefix = $"SELECT {new string('A', 600)} FROM Orders WHERE ";
        var first = SqlPerformanceFingerprint.Create(
            prefix + "ColumnOne = $$private-one$$"
        );
        var second = SqlPerformanceFingerprint.Create(
            prefix + "ColumnTwo = $tag$private-two$tag$"
        );
        var dollarQuoted = SqlPerformanceFingerprint.Create(
            "SELECT $$private-dollar-value$$::text"
        );

        Assert.NotEqual(first.Hash, second.Hash);
        Assert.Equal(first.Template, second.Template);
        Assert.DoesNotContain(
            "private-dollar-value",
            dollarQuoted.Template,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.True(first.Template.Length <= 500);
    }

    [Fact]
    public void SqlFingerprint_字符串内含注释标记时仍不泄露字面量()
    {
        var fingerprint = SqlPerformanceFingerprint.Create(
            "SELECT * FROM Orders WHERE Note = 'Alice -- private-tail' "
                + "AND Memo = 'Bob /* private-block */ tail' /* comment with 'quote' */"
        );

        Assert.DoesNotContain("Alice", fingerprint.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", fingerprint.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bob", fingerprint.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tail", fingerprint.Template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("comment", fingerprint.Template, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MetricBatch_只接受白名单指标单位和低基数维度()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var valid = new PerformanceMetricBatchV1Dto
        {
            SchemaVersion = 1,
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.WebTableRenderToPaint,
                    ObservedAt = now,
                    Value = 128.5,
                    Unit = PerformanceMetricUnits.Milliseconds,
                    Dimensions = new Dictionary<string, string>
                    {
                        ["metricId"] = "products.main",
                        ["route"] = "/products",
                        ["outcome"] = "success",
                    },
                },
            ],
        };

        Assert.Empty(PerformanceMetricBatchValidator.Validate(valid, now));

        valid.Events[0].Dimensions["orderId"] = "ORDER-SECRET";
        var errors = PerformanceMetricBatchValidator.Validate(valid, now);

        Assert.Contains(errors, error => error.Contains("orderId", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("ORDER-SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricBatch_按指标校验单位并按入口限制指标域()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var request = new PerformanceMetricBatchV1Dto
        {
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.SqlCommandDuration,
                    ObservedAt = now,
                    Value = 128,
                    Unit = PerformanceMetricUnits.Bytes,
                    Dimensions = new Dictionary<string, string>
                    {
                        ["databaseContext"] = "Main",
                        ["sqlFingerprint"] = "abc123",
                    },
                },
            ],
        };

        var errors = PerformanceMetricBatchValidator.Validate(request, now, "client");

        Assert.Contains(errors, error => error.Contains("单位", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("client", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricBatch_拒绝过期未来未知指标和非有限数值()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var request = new PerformanceMetricBatchV1Dto
        {
            SchemaVersion = 2,
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = "unknown.metric",
                    ObservedAt = now.AddMinutes(6),
                    Value = double.PositiveInfinity,
                    Unit = "seconds",
                },
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.PosColdStart,
                    ObservedAt = now.AddDays(-31),
                    Value = 1,
                    Unit = PerformanceMetricUnits.Milliseconds,
                },
            ],
        };

        var errors = PerformanceMetricBatchValidator.Validate(request, now);

        Assert.Contains(errors, error => error.Contains("schemaVersion", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unknown.metric", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("未来", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("30 天", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("有限数值", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricBatch_畸形空值返回校验错误而不是抛出异常()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var emptyEvents = new PerformanceMetricBatchV1Dto { Events = null! };
        var nullEvent = new PerformanceMetricBatchV1Dto { Events = [null!] };
        var nullDimensions = CreateValidMetricBatch(now);
        nullDimensions.Events[0].Dimensions = null!;
        var nullDimensionValue = CreateValidMetricBatch(now);
        nullDimensionValue.Events[0].Dimensions["metricId"] = null!;
        var nullMetricAndUnit = CreateValidMetricBatch(now);
        nullMetricAndUnit.Events[0].Metric = null!;
        nullMetricAndUnit.Events[0].Unit = null!;

        Assert.NotEmpty(PerformanceMetricBatchValidator.Validate(emptyEvents, now));
        Assert.NotEmpty(PerformanceMetricBatchValidator.Validate(nullEvent, now));
        Assert.NotEmpty(PerformanceMetricBatchValidator.Validate(nullDimensions, now));
        Assert.NotEmpty(PerformanceMetricBatchValidator.Validate(nullDimensionValue, now));
        Assert.NotEmpty(PerformanceMetricBatchValidator.Validate(nullMetricAndUnit, now));
    }

    [Fact]
    public void MetricBatch_Client按项目指标约束精确维度环境和值语法()
    {
        var now = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);
        var valid = new PerformanceMetricBatchV1Dto
        {
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.WebTableRenderToPaint,
                    ObservedAt = now,
                    Value = 12,
                    Unit = PerformanceMetricUnits.Milliseconds,
                    Dimensions = new Dictionary<string, string>
                    {
                        ["environment"] = "Production",
                        ["metricId"] = "warehouse.products.main",
                        ["outcome"] = "success",
                    },
                },
            ],
        };

        Assert.Empty(
            PerformanceMetricBatchValidator.Validate(valid, now, "client", "hbweb_rv")
        );

        valid.Events[0].Dimensions.Remove("environment");
        Assert.Contains(
            PerformanceMetricBatchValidator.Validate(valid, now, "client", "hbweb_rv"),
            error => error.Contains("environment", StringComparison.Ordinal)
        );

        valid.Events[0].Dimensions["environment"] = "Production";
        valid.Events[0].Dimensions["route"] = "/api/private";
        Assert.Contains(
            PerformanceMetricBatchValidator.Validate(valid, now, "client", "hbweb_rv"),
            error => error.Contains("route", StringComparison.Ordinal)
        );

        valid.Events[0].Dimensions.Remove("route");
        valid.Events[0].Dimensions["metricId"] = "Bearer-secret-value";
        Assert.Contains(
            PerformanceMetricBatchValidator.Validate(valid, now, "client", "hbweb_rv"),
            error => error.Contains("metricId", StringComparison.Ordinal)
        );

        var wrongPosProject = new PerformanceMetricBatchV1Dto
        {
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.PosColdStart,
                    ObservedAt = now,
                    Value = 500,
                    Unit = PerformanceMetricUnits.Milliseconds,
                    Dimensions = new Dictionary<string, string>
                    {
                        ["environment"] = "Production",
                        ["app"] = "pos-handheld",
                        ["version"] = "2.11.0",
                        ["channel"] = "production",
                        ["outcome"] = "success",
                    },
                },
            ],
        };
        Assert.Contains(
            PerformanceMetricBatchValidator.Validate(
                wrongPosProject,
                now,
                "client",
                "hbpos_ipad"
            ),
            error => error.Contains("app", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void MetricDimensions_Api按路由方法和最终状态分类生成选择器()
    {
        var dimensions = PerformanceMetricDimensions.Normalize(
            PerformanceMetricNames.ApiRequestDuration,
            new Dictionary<string, string>
            {
                ["route"] = "/api/products/{id}",
                ["method"] = "GET",
                ["statusClass"] = "2xx",
                ["instance"] = "api-02",
            }
        );

        Assert.Equal("GET /api/products/{id} 2xx", dimensions.Selector);
        Assert.DoesNotContain("api-02", dimensions.Selector, StringComparison.Ordinal);
    }

    private static PerformanceMetricBatchV1Dto CreateValidMetricBatch(DateTime observedAt)
    {
        return new PerformanceMetricBatchV1Dto
        {
            Events =
            [
                new()
                {
                    EventId = Guid.NewGuid(),
                    Metric = PerformanceMetricNames.WebTableRenderToPaint,
                    ObservedAt = observedAt,
                    Value = 1,
                    Unit = PerformanceMetricUnits.Milliseconds,
                    Dimensions = new Dictionary<string, string>
                    {
                        ["metricId"] = "products.main",
                    },
                },
            ],
        };
    }

    [Fact]
    public void MetricDimensions_Sql按数据库上下文和脱敏指纹生成选择器()
    {
        var dimensions = PerformanceMetricDimensions.Normalize(
            PerformanceMetricNames.SqlCommandDuration,
            new Dictionary<string, string>
            {
                ["databaseContext"] = "HqSqlSugarContext",
                ["sqlFingerprint"] = "abc123",
                ["sqlTemplate"] = "SELECT * FROM ORDERS WHERE ID = ?",
            }
        );

        Assert.Equal("HqSqlSugarContext:abc123", dimensions.Selector);
        Assert.DoesNotContain("SELECT", dimensions.Selector, StringComparison.Ordinal);
    }

    [Fact]
    public void BaselineThreshold_延迟积压和失败率按计划公式生成()
    {
        Assert.Equal(1200, PerformanceBaselineThreshold.LatencyWarning(1000));
        Assert.Equal(2, PerformanceBaselineThreshold.BacklogWarning(1));
        Assert.Equal(0.06, PerformanceBaselineThreshold.FailureRateWarning(0.05), 6);
        Assert.Equal(0.01, PerformanceBaselineThreshold.FailureRateWarning(0), 6);
        Assert.Equal(0.006, PerformanceBaselineThreshold.CrashRateWarning(0.005), 6);
        Assert.Equal(0.001, PerformanceBaselineThreshold.CrashRateWarning(0), 6);
    }

    [Theory]
    [InlineData("api/products/{id}", "GET", true)]
    [InlineData("api/products/{id}", "OPTIONS", false)]
    [InlineData("health", "GET", false)]
    [InlineData("swagger/{documentName}/swagger.json", "GET", false)]
    [InlineData("api/system/logs", "POST", false)]
    [InlineData("api/system/performance/client-batches", "POST", false)]
    public void ApiMetric_按路由模板和方法执行排除规则(
        string route,
        string method,
        bool expected
    )
    {
        Assert.Equal(expected, AspNetCoreRequestMetricListener.ShouldRecord(route, method));
    }

    [Theory]
    [InlineData("200", "2xx")]
    [InlineData("404", "4xx")]
    [InlineData("503", "5xx")]
    [InlineData(null, "unknown")]
    public void ApiMetric_只保留最终状态码类别(string? statusCode, string expected)
    {
        Assert.Equal(expected, AspNetCoreRequestMetricListener.ToStatusClass(statusCode));
    }

    [Fact]
    public void SqlAop_组合已有OnLogExecuted且排除自身指标表()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"performance-aop-{Guid.NewGuid():N}.db");
        var existingCallbackCount = 0;
        using var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
            }
        );
        db.Aop.OnLogExecuted = (_, _) => existingCallbackCount++;

        try
        {
            SqlPerformanceAttachmentService.Attach(db, "TestContext");
            db.Ado.ExecuteCommand("CREATE TABLE Example (Id INTEGER PRIMARY KEY)");

            Assert.Equal(1, existingCallbackCount);
            Assert.True(
                SqlPerformanceAttachmentService.IsSelfTelemetry(
                    "INSERT INTO HBwebPerformanceMetricBuckets (Id) VALUES (1)"
                )
            );
            Assert.True(
                SqlPerformanceAttachmentService.IsSelfTelemetry(
                    "UPDATE PerformanceIngestRateWindow SET RequestCount = 1"
                )
            );
            Assert.True(
                SqlPerformanceAttachmentService.IsSelfTelemetry(
                    "SELECT * FROM PerformanceCollectorState"
                )
            );
            Assert.False(SqlPerformanceAttachmentService.IsSelfTelemetry("SELECT * FROM Orders"));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void ApiMetric_附件下载响应按完成时头部排除()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers.ContentDisposition = "attachment; filename=report.xlsx";

        Assert.True(PerformanceMetricsEndpointExclusionMiddleware.IsAttachment(context.Response));
    }

    [Fact]
    public void UtcStorage_SQL返回的Unspecified保持UTC时钟值而不按服务器时区偏移()
    {
        var stored = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Unspecified);

        var normalized = PerformanceUtc.Normalize(stored);

        Assert.Equal(stored.Ticks, normalized.Ticks);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }

    [Fact]
    public void SqlServerMigration_启动只读验证SnapshotIsolation且不得执行AlterDatabase()
    {
        var sql = BlazorApp.Api.Data.PerformanceBaselineSchemaMigrator
            .ValidateSqlServerSnapshotIsolationSql;

        Assert.Contains("snapshot_isolation_state", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THROW", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER DATABASE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
