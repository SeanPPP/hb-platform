using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed record PerformanceIngestRateLimitResult(
    bool Allowed,
    int RetryAfterSeconds
);

/// <summary>数据库共享的固定一分钟预算，防止公开项目写入密钥被用于放大写入或污染覆盖率。</summary>
public sealed class PerformanceClientIngestRateLimiter
{
    private const string ProjectBudgetKey = "project";
    private readonly ISqlSugarClient _db;
    private readonly PerformanceMetricsOptions _options;

    public PerformanceClientIngestRateLimiter(
        SqlSugarContext context,
        IOptions<PerformanceMetricsOptions> options
    )
        : this(context.Db, options) { }

    internal PerformanceClientIngestRateLimiter(
        ISqlSugarClient db,
        IOptions<PerformanceMetricsOptions> options
    )
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<PerformanceIngestRateLimitResult> TryConsumeAsync(
        string projectCode,
        string clientIdentity,
        int eventCount,
        int byteCount,
        DateTime utcNow,
        CancellationToken cancellationToken = default
    )
    {
        projectCode = Normalize(projectCode, 80, "unknown");
        var clientHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(clientIdentity.Trim()))
        );
        var windowStart = FloorToMinute(PerformanceUtc.Normalize(utcNow));
        eventCount = Math.Max(0, eventCount);
        byteCount = Math.Max(0, byteCount);
        var retryAfter = Math.Clamp(
            60 - (int)(PerformanceUtc.Normalize(utcNow) - windowStart).TotalSeconds,
            1,
            60
        );

        _db.Ado.BeginTran();
        try
        {
            await AcquireSqlServerLockAsync(
                _db,
                $"PerformanceIngest:{projectCode}:{windowStart:yyyyMMddHHmm}",
                cancellationToken
            );
            var rows = await _db
                .Queryable<PerformanceIngestRateWindow>()
                .Where(item =>
                    item.ProjectCode == projectCode
                    && item.WindowStartUtc == windowStart
                    && (
                        item.ClientKeyHash == ProjectBudgetKey
                        || item.ClientKeyHash == clientHash
                    )
                )
                .ToListAsync(cancellationToken);
            var projectRow = rows.FirstOrDefault(item => item.ClientKeyHash == ProjectBudgetKey);
            var clientRow = rows.FirstOrDefault(item => item.ClientKeyHash == clientHash);

            if (
                WouldExceed(
                    projectRow,
                    eventCount,
                    byteCount,
                    _options.ProjectRequestsPerMinute,
                    _options.ProjectEventsPerMinute,
                    _options.ProjectBytesPerMinute
                )
                || WouldExceed(
                    clientRow,
                    eventCount,
                    byteCount,
                    _options.ClientRequestsPerMinute,
                    _options.ClientEventsPerMinute,
                    _options.ClientBytesPerMinute
                )
            )
            {
                _db.Ado.RollbackTran();
                return new PerformanceIngestRateLimitResult(false, retryAfter);
            }

            await AddAsync(
                projectRow,
                projectCode,
                ProjectBudgetKey,
                windowStart,
                eventCount,
                byteCount,
                cancellationToken
            );
            await AddAsync(
                clientRow,
                projectCode,
                clientHash,
                windowStart,
                eventCount,
                byteCount,
                cancellationToken
            );
            _db.Ado.CommitTran();
            return new PerformanceIngestRateLimitResult(true, retryAfter);
        }
        catch
        {
            _db.Ado.RollbackTran();
            throw;
        }
    }

    private async Task AddAsync(
        PerformanceIngestRateWindow? row,
        string projectCode,
        string clientKeyHash,
        DateTime windowStart,
        int eventCount,
        int byteCount,
        CancellationToken cancellationToken
    )
    {
        if (row == null)
        {
            await _db
                .Insertable(
                    new PerformanceIngestRateWindow
                    {
                        ProjectCode = projectCode,
                        ClientKeyHash = clientKeyHash,
                        WindowStartUtc = windowStart,
                        RequestCount = 1,
                        EventCount = eventCount,
                        ByteCount = byteCount,
                    }
                )
                .ExecuteCommandAsync(cancellationToken);
            return;
        }

        row.RequestCount++;
        row.EventCount += eventCount;
        row.ByteCount += byteCount;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(row).ExecuteCommandAsync(cancellationToken);
    }

    private static bool WouldExceed(
        PerformanceIngestRateWindow? row,
        int eventCount,
        int byteCount,
        int requestLimit,
        int eventLimit,
        int byteLimit
    ) =>
        (row?.RequestCount ?? 0) + 1 > Math.Clamp(requestLimit, 1, 1_000_000)
        || (row?.EventCount ?? 0) + eventCount > Math.Clamp(eventLimit, 1, 100_000_000)
        || (row?.ByteCount ?? 0) + byteCount > Math.Clamp(byteLimit, 1024, int.MaxValue);

    private static async Task AcquireSqlServerLockAsync(
        ISqlSugarClient db,
        string resource,
        CancellationToken cancellationToken
    )
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await db.Ado.SqlQuerySingleAsync<int>(
            """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 5000;
            SELECT @Result;
            """,
            new SugarParameter("@Resource", resource)
        );
        if (result < 0)
        {
            throw new InvalidOperationException("获取性能指标写入预算锁失败");
        }
    }

    private static DateTime FloorToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
