using System.Threading;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// POSM 商品供应商映射同步的全局执行租约。
/// 全量和增量同步必须共用此租约，避免两个旧快照分别判断为插入而造成主键冲突。
/// </summary>
internal static class PosmProductSupplierMappingSyncLock
{
    internal const string ResourceName = "HB:PosmProductSupplierMappingSync";
    private const int LockTimeoutMilliseconds = 10_000;
    private static readonly SemaphoreSlim ProcessLease = new(1, 1);

    // 仅供受控自动化测试确认并发边界；生产默认 null，不参与任何同步行为。
    internal static Func<int, Task>? TestProbeAsync { get; set; }

    internal static async ValueTask<PosmProductSupplierMappingSyncLockScope> AcquireAsync(
        ISqlSugarClient db
    )
    {
        await InvokeTestProbeAsync(1);
        if (!await ProcessLease.WaitAsync(TimeSpan.FromMilliseconds(LockTimeoutMilliseconds)))
        {
            throw new PosmProductSupplierMappingSyncLockException(
                $"等待 POSM 商品供应商映射同步进程租约超过 {LockTimeoutMilliseconds}ms"
            );
        }

        var transactionStarted = false;
        try
        {
            // 关键逻辑：POSM 事务从源快照开始持续到写入提交，使跨实例 applock 覆盖完整读改写周期。
            await db.Ado.BeginTranAsync();
            transactionStarted = true;

            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                await db.Ado.ExecuteCommandAsync(
                    """
                    DECLARE @lockResult int;
                    EXEC @lockResult = sys.sp_getapplock
                        @Resource = @resource,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 10000;
                    IF @lockResult < 0 THROW 51037, '获取 POSM 商品供应商映射同步锁失败', 1;
                    """,
                    new SugarParameter("@resource", ResourceName)
                );
            }

            await InvokeTestProbeAsync(2);

            return new PosmProductSupplierMappingSyncLockScope(db);
        }
        catch
        {
            if (transactionStarted)
            {
                try
                {
                    await db.Ado.RollbackTranAsync();
                }
                catch
                {
                    // 原始锁或事务错误更能说明失败原因，回滚失败不覆盖它。
                }
            }

            ProcessLease.Release();
            throw;
        }
    }

    internal static void ReleaseProcessLease() => ProcessLease.Release();

    private static Task InvokeTestProbeAsync(int phase) => TestProbeAsync?.Invoke(phase) ?? Task.CompletedTask;
}

/// <summary>
/// 一个同步运行持有的 POSM 事务与进程租约；成功路径必须显式提交，其他路径在释放时回滚。
/// </summary>
internal sealed class PosmProductSupplierMappingSyncLockScope : IAsyncDisposable
{
    private readonly ISqlSugarClient _db;
    private bool _completed;
    private int _disposed;

    internal PosmProductSupplierMappingSyncLockScope(ISqlSugarClient db)
    {
        _db = db;
    }

    internal async Task CommitAsync()
    {
        ThrowIfDisposed();
        await _db.Ado.CommitTranAsync();
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_completed)
            {
                await _db.Ado.RollbackTranAsync();
            }
        }
        finally
        {
            // 关键逻辑：无论提交、回滚或异常，进程内租约都必须释放，避免后续同步永久阻塞。
            PosmProductSupplierMappingSyncLock.ReleaseProcessLease();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PosmProductSupplierMappingSyncLockScope));
        }
    }
}

internal sealed class PosmProductSupplierMappingSyncLockException : Exception
{
    internal PosmProductSupplierMappingSyncLockException(string message)
        : base(message) { }
}
