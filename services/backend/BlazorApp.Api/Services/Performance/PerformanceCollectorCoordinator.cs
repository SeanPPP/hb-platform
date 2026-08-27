using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed record PerformanceCollectorLease(
    string CollectorKey,
    string Owner,
    DateTime? CursorUtc,
    TimeSpan LeaseDuration
);

/// <summary>使用 HBweb 数据库租约协调多 API 实例上的全局采集器。</summary>
public sealed class PerformanceCollectorCoordinator
{
    private readonly string _owner;

    public PerformanceCollectorCoordinator()
        : this($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}") { }

    internal PerformanceCollectorCoordinator(string owner)
    {
        _owner = Normalize(owner, 160, "unknown-instance");
    }

    public async Task<PerformanceCollectorLease?> TryAcquireAsync(
        ISqlSugarClient db,
        string collectorKey,
        DateTime utcNow,
        TimeSpan leaseDuration,
        DateTime? initialCursorUtc,
        CancellationToken cancellationToken = default
    )
    {
        collectorKey = Normalize(collectorKey, 160, "unknown-collector");
        utcNow = PerformanceUtc.Normalize(utcNow);
        leaseDuration = ClampLease(leaseDuration);
        initialCursorUtc = initialCursorUtc.HasValue
            ? PerformanceUtc.Normalize(initialCursorUtc.Value)
            : null;

        db.Ado.BeginTran();
        try
        {
            await AcquireSqlServerLockAsync(db, collectorKey, cancellationToken);
            var state = await db
                .Queryable<PerformanceCollectorState>()
                .Where(item => item.CollectorKey == collectorKey)
                .FirstAsync(cancellationToken);
            if (
                state != null
                && state.LeaseExpiresAtUtc > utcNow
                && !string.Equals(state.LeaseOwner, _owner, StringComparison.Ordinal)
            )
            {
                db.Ado.RollbackTran();
                return null;
            }

            if (state == null)
            {
                state = new PerformanceCollectorState
                {
                    CollectorKey = collectorKey,
                    CursorUtc = initialCursorUtc,
                    LeaseOwner = _owner,
                    LeaseExpiresAtUtc = utcNow.Add(leaseDuration),
                };
                await db.Insertable(state).ExecuteCommandAsync(cancellationToken);
            }
            else
            {
                state.CursorUtc ??= initialCursorUtc;
                state.LeaseOwner = _owner;
                state.LeaseExpiresAtUtc = utcNow.Add(leaseDuration);
                state.UpdatedAt = DateTime.UtcNow;
                await db.Updateable(state).ExecuteCommandAsync(cancellationToken);
            }
            db.Ado.CommitTran();
            return new PerformanceCollectorLease(
                collectorKey,
                _owner,
                state.CursorUtc,
                leaseDuration
            );
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<bool> CommitAsync(
        ISqlSugarClient db,
        PerformanceCollectorLease lease,
        DateTime utcNow,
        DateTime? cursorUtc,
        bool release,
        Func<CancellationToken, Task> persist,
        CancellationToken cancellationToken = default
    )
    {
        utcNow = PerformanceUtc.Normalize(utcNow);
        cursorUtc = cursorUtc.HasValue ? PerformanceUtc.Normalize(cursorUtc.Value) : null;
        db.Ado.BeginTran();
        try
        {
            await AcquireSqlServerLockAsync(db, lease.CollectorKey, cancellationToken);
            var state = await db
                .Queryable<PerformanceCollectorState>()
                .Where(item => item.CollectorKey == lease.CollectorKey)
                .FirstAsync(cancellationToken);
            if (
                state == null
                || !string.Equals(state.LeaseOwner, lease.Owner, StringComparison.Ordinal)
                || state.LeaseExpiresAtUtc <= utcNow
            )
            {
                db.Ado.RollbackTran();
                return false;
            }

            await persist(cancellationToken);
            if (cursorUtc.HasValue)
            {
                state.CursorUtc = cursorUtc;
                state.LastSucceededAtUtc = cursorUtc;
            }
            state.LeaseOwner = release ? null : lease.Owner;
            state.LeaseExpiresAtUtc = release ? null : utcNow.Add(lease.LeaseDuration);
            state.UpdatedAt = DateTime.UtcNow;
            await db.Updateable(state).ExecuteCommandAsync(cancellationToken);
            db.Ado.CommitTran();
            return true;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    public Task<bool> RenewAsync(
        ISqlSugarClient db,
        PerformanceCollectorLease lease,
        DateTime utcNow,
        CancellationToken cancellationToken = default
    ) =>
        CommitAsync(
            db,
            lease,
            utcNow,
            cursorUtc: null,
            release: false,
            _ => Task.CompletedTask,
            cancellationToken
        );

    public Task<bool> ReleaseAsync(
        ISqlSugarClient db,
        PerformanceCollectorLease lease,
        DateTime utcNow,
        CancellationToken cancellationToken = default
    ) => CommitAsync(
        db,
        lease,
        utcNow,
        lease.CursorUtc,
        release: true,
        _ => Task.CompletedTask,
        cancellationToken
    );

    private static async Task AcquireSqlServerLockAsync(
        ISqlSugarClient db,
        string collectorKey,
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
            new SugarParameter("@Resource", $"PerformanceCollector:{collectorKey}")
        );
        if (result < 0)
        {
            throw new InvalidOperationException("获取性能采集器数据库租约失败");
        }
    }

    private static TimeSpan ClampLease(TimeSpan value) =>
        TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 10, 7200));

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
