using Hbpos.Contracts.Installments;
using SqlSugar;

namespace Hbpos.Api.Services;

internal static class InstallmentMutationLock
{
    private const string RepaymentClaimTableName = "POSM_InstallmentRepaymentClaim";
    private const string CancelClaimTableName = "POSM_InstallmentCancelClaim";
    private static readonly object ProcessLockGate = new();
    private static readonly Dictionary<string, LockEntry> ProcessLocks = new(StringComparer.Ordinal);

    internal static string BuildResource(Guid installmentGuid) =>
        $"hbpos-installment:{installmentGuid:D}";

    internal static async ValueTask<IAsyncDisposable> AcquireProcessAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        var resource = BuildResource(installmentGuid);
        LockEntry entry;
        lock (ProcessLockGate)
        {
            if (!ProcessLocks.TryGetValue(resource, out entry!))
            {
                entry = new LockEntry();
                ProcessLocks.Add(resource, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(resource, entry);
        }
        catch
        {
            ReleaseReference(resource, entry, releaseSemaphore: false);
            throw;
        }
    }

    internal static async Task AcquireDatabaseAsync(
        ISqlSugarClient db,
        Guid installmentGuid)
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        var result = await db.Ado.SqlQuerySingleAsync<int>(
            """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            SELECT @Result;
            """,
            new SugarParameter("@Resource", BuildResource(installmentGuid)));
        if (result < 0)
        {
            throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.Busy,
                "Installment is busy with another repayment or lifecycle operation.");
        }
    }

    internal static async Task<InstallmentOrderEntity?> LockOrderAsync(
        ISqlSugarClient db,
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        var installmentGuidText = installmentGuid.ToString("D");
        var query = db.Queryable<InstallmentOrderEntity>()
            .Where(entity => entity.InstallmentGuid == installmentGuidText);
        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            query = query.With(SqlWith.UpdLock);
        }

        return await query.FirstAsync(cancellationToken);
    }

    internal static async Task EnsureNoBlockingClaimAsync(
        ISqlSugarClient db,
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // 旧测试库可只建立其中一张表；生产启动器会在接流量前强制建立两张独立 claim 表。
        if (db.DbMaintenance.IsAnyTable(RepaymentClaimTableName))
        {
            var repaymentReleased = InstallmentRepaymentClaimStatus.Released.ToString();
            var repaymentPrepared = InstallmentRepaymentClaimStatus.Prepared.ToString();
            await db.Updateable<InstallmentRepaymentClaimEntity>()
                .SetColumns(entity => entity.Status == repaymentReleased)
                .SetColumns(entity => entity.IsBlocking == false)
                .SetColumns(entity => entity.UpdatedAtUtc == now)
                .SetColumns(entity => entity.Revision == entity.Revision + 1)
                .Where(entity => entity.InstallmentGuid == installmentGuid)
                .Where(entity => entity.IsBlocking)
                .Where(entity => entity.Status == repaymentPrepared)
                .Where(entity => entity.ExpiresAtUtc != null && entity.ExpiresAtUtc <= now)
                .ExecuteCommandAsync(cancellationToken);

            var repaymentQuery = db.Queryable<InstallmentRepaymentClaimEntity>()
                .Where(entity => entity.InstallmentGuid == installmentGuid && entity.IsBlocking);
            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                repaymentQuery = repaymentQuery.With(SqlWith.UpdLock);
            }

            if (await repaymentQuery.FirstAsync(cancellationToken) is not null)
            {
                throw Busy();
            }
        }

        if (db.DbMaintenance.IsAnyTable(CancelClaimTableName))
        {
            var cancelReleased = InstallmentCancelClaimStatus.Released.ToString();
            var cancelPrepared = InstallmentCancelClaimStatus.Prepared.ToString();
            await db.Updateable<InstallmentCancelClaimEntity>()
                .SetColumns(entity => entity.Status == cancelReleased)
                .SetColumns(entity => entity.IsBlocking == false)
                .SetColumns(entity => entity.UpdatedAtUtc == now)
                .SetColumns(entity => entity.Revision == entity.Revision + 1)
                .Where(entity => entity.InstallmentGuid == installmentGuid)
                .Where(entity => entity.IsBlocking)
                .Where(entity => entity.Status == cancelPrepared)
                .Where(entity => entity.ExpiresAtUtc != null && entity.ExpiresAtUtc <= now)
                .ExecuteCommandAsync(cancellationToken);

            var cancelQuery = db.Queryable<InstallmentCancelClaimEntity>()
                .Where(entity => entity.InstallmentGuid == installmentGuid && entity.IsBlocking);
            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                cancelQuery = cancelQuery.With(SqlWith.UpdLock);
            }

            if (await cancelQuery.FirstAsync(cancellationToken) is not null)
            {
                throw Busy();
            }
        }
    }

    private static InstallmentRepaymentClaimException Busy() => new(
        InstallmentRepaymentClaimErrorCodes.Busy,
        "Installment already has an in-flight repayment or cancellation claim.");

    private static void ReleaseReference(string resource, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (ProcessLockGate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                ProcessLocks.TryGetValue(resource, out var current) &&
                ReferenceEquals(current, entry))
            {
                ProcessLocks.Remove(resource);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        internal int ReferenceCount { get; set; }
    }

    private sealed class Releaser(string resource, LockEntry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseReference(resource, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
