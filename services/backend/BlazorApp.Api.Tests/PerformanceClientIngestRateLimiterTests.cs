using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PerformanceClientIngestRateLimiterTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"performance-rate-limit-{Guid.NewGuid():N}.db"
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
    public async Task 限流按项目和认证Subject哈希隔离且不保存原始身份()
    {
        var options = Options.Create(
            new PerformanceMetricsOptions
            {
                ClientRequestsPerMinute = 1,
                ProjectRequestsPerMinute = 2,
                ClientEventsPerMinute = 10,
                ProjectEventsPerMinute = 20,
                ClientBytesPerMinute = 1024,
                ProjectBytesPerMinute = 2048,
            }
        );
        var limiter = new PerformanceClientIngestRateLimiter(_db, options);
        var now = new DateTime(2026, 8, 25, 6, 0, 30, DateTimeKind.Utc);

        Assert.True((await limiter.TryConsumeAsync("hbweb_rv", "user:subject-a", 5, 500, now)).Allowed);
        var sameSubject = await limiter.TryConsumeAsync(
            "hbweb_rv",
            "user:subject-a",
            1,
            10,
            now.AddSeconds(1)
        );
        Assert.False(sameSubject.Allowed);
        Assert.InRange(sameSubject.RetryAfterSeconds, 1, 60);

        // 两个认证主体即使位于同一代理/NAT 后，也必须拥有独立客户端预算。
        Assert.True((await limiter.TryConsumeAsync("hbweb_rv", "user:subject-b", 5, 500, now)).Allowed);
        Assert.False((await limiter.TryConsumeAsync("hbweb_rv", "user:subject-c", 1, 10, now)).Allowed);

        var rows = await _db.Queryable<PerformanceIngestRateWindow>().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(
            rows,
            row => row.ClientKeyHash.Contains("subject", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task 公开预算耗尽不阻断可信命名空间且可信项目总预算仍生效()
    {
        var options = Options.Create(
            new PerformanceMetricsOptions
            {
                ClientRequestsPerMinute = 10,
                ProjectRequestsPerMinute = 2,
                ClientEventsPerMinute = 100,
                ProjectEventsPerMinute = 100,
                ClientBytesPerMinute = 10_000,
                ProjectBytesPerMinute = 10_000,
            }
        );
        var limiter = new PerformanceClientIngestRateLimiter(_db, options);
        var now = new DateTime(2026, 8, 25, 7, 0, 0, DateTimeKind.Utc);

        Assert.True((await limiter.TryConsumeAsync("hbweb_rv:public", "ip:8.8.8.8", 1, 100, now)).Allowed);
        Assert.True((await limiter.TryConsumeAsync("hbweb_rv:public", "ip:8.8.4.4", 1, 100, now)).Allowed);
        Assert.False((await limiter.TryConsumeAsync("hbweb_rv:public", "ip:1.1.1.1", 1, 100, now)).Allowed);

        Assert.True((await limiter.TryConsumeAsync("hbweb_rv:trusted", "web:user-a", 1, 100, now)).Allowed);
        Assert.True((await limiter.TryConsumeAsync("hbweb_rv:trusted", "web:user-b", 1, 100, now)).Allowed);
        Assert.False((await limiter.TryConsumeAsync("hbweb_rv:trusted", "web:user-c", 1, 100, now)).Allowed);

        var projectRows = await _db
            .Queryable<PerformanceIngestRateWindow>()
            .Where(item => item.ClientKeyHash == "project")
            .OrderBy(item => item.ProjectCode)
            .ToListAsync();
        Assert.Equal(["hbweb_rv:public", "hbweb_rv:trusted"], projectRows.Select(item => item.ProjectCode));
        Assert.All(projectRows, row => Assert.Equal(2, row.RequestCount));
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
