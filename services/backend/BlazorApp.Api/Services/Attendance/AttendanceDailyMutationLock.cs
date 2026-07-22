using SqlSugar;

namespace BlazorApp.Api.Services.Attendance;

internal static class AttendanceDailyMutationLock
{
    private static readonly object ProcessLockGate = new();
    private static readonly Dictionary<string, LockEntry> ProcessLocks = new(StringComparer.Ordinal);

    internal static string BuildResource(string userGuid, string storeCode, DateTime workDate) =>
        // 同一员工同一工作日跨门店的写操作必须串行，门店仅保留在调用签名中兼容现有入口。
        $"attendance-day:{userGuid.Trim()}:{workDate:yyyyMMdd}"
            .ToLowerInvariant();

    internal static int GetProcessReferenceCount(string resource)
    {
        lock (ProcessLockGate)
        {
            return ProcessLocks.TryGetValue(resource, out var entry) ? entry.ReferenceCount : 0;
        }
    }

    internal static async ValueTask<IAsyncDisposable> AcquireProcessAsync(string resource)
    {
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
            await entry.Semaphore.WaitAsync();
            return new Releaser(resource, entry);
        }
        catch
        {
            ReleaseReference(resource, entry, releaseSemaphore: false);
            throw;
        }
    }

    internal static async Task AcquireDatabaseAsync(ISqlSugarClient db, string resource)
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
                @LockTimeout = 10000;
            SELECT @Result;
            """,
            new SugarParameter("@Resource", resource));
        if (result < 0)
        {
            throw new InvalidOperationException("获取员工当日考勤写锁失败，请稍后重试");
        }
    }

    private static void ReleaseReference(string resource, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }
        lock (ProcessLockGate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0
                && ProcessLocks.TryGetValue(resource, out var current)
                && ReferenceEquals(current, entry))
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
