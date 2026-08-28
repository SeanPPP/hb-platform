using System.Collections.Concurrent;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>
    /// 本地进货单推送 HQ 的并发锁。进程内锁覆盖 SQLite 等测试方言，
    /// SQL Server 再用 transaction-owned applock 跨进程/节点串行化同一单据。
    /// </summary>
    internal static class LocalSupplierInvoiceHqSyncMutationLock
    {
        private const string ResourcePrefix = "HB:LocalSupplierInvoiceHqSync:";
        private const int LockTimeoutMilliseconds = 10_000;
        private static readonly ConcurrentDictionary<string, ProcessLockEntry> ProcessLocks = new(
            StringComparer.Ordinal
        );

        internal static async Task<LocalSupplierInvoiceHqSyncProcessLockScope> AcquireAsync(
            string? invoiceGuid
        )
        {
            var normalizedInvoiceKey = NormalizeInvoiceKey(invoiceGuid);
            while (true)
            {
                var processLock = ProcessLocks.GetOrAdd(
                    normalizedInvoiceKey,
                    _ => new ProcessLockEntry()
                );
                if (!processLock.TryAddReference())
                {
                    // 已退休的条目可能仍在字典中等待精确移除；重试后会取得新条目。
                    continue;
                }

                try
                {
                    await processLock.Semaphore.WaitAsync();
                    return new LocalSupplierInvoiceHqSyncProcessLockScope(
                        normalizedInvoiceKey,
                        processLock
                    );
                }
                catch
                {
                    ReleaseProcessLock(normalizedInvoiceKey, processLock);
                    throw;
                }
            }
        }

        internal static string NormalizeInvoiceKey(string? invoiceGuid)
        {
            if (string.IsNullOrWhiteSpace(invoiceGuid))
            {
                throw new InvalidOperationException("推送 HQ 的进货单 GUID 不能为空。");
            }

            // 同一业务 GUID 不因大小写或首尾空格生成不同锁；每次只锁一张单据，锁顺序固定为该规范化键。
            return invoiceGuid.Trim().ToUpperInvariant();
        }

        internal static async Task AcquireDatabaseAsync(
            ISqlSugarClient db,
            string normalizedInvoiceKey
        )
        {
            if (db.Ado.Transaction == null)
            {
                throw new InvalidOperationException("HQ 进货单业务锁必须在数据库事务内获取。");
            }

            // SQLite 测试环境没有 sp_getapplock；进程锁仍会保护其事务内重读与写入。
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
                    @LockTimeout = @LockTimeout;
                SELECT @Result;
                """,
                new SugarParameter("@Resource", ResourcePrefix + normalizedInvoiceKey),
                new SugarParameter("@LockTimeout", LockTimeoutMilliseconds)
            );
            if (result < 0)
            {
                throw new LocalSupplierInvoiceHqSyncLockException(normalizedInvoiceKey, result);
            }
        }

        internal static void ReleaseProcessLock(string normalizedInvoiceKey, ProcessLockEntry processLock)
        {
            if (!processLock.ReleaseReference())
            {
                return;
            }

            // 只移除当前条目，避免旧 scope 在同键新锁创建后误删新锁。
            ((ICollection<KeyValuePair<string, ProcessLockEntry>>)ProcessLocks).Remove(
                new KeyValuePair<string, ProcessLockEntry>(normalizedInvoiceKey, processLock)
            );
        }

        /// <summary>
        /// 引用计数覆盖等待者与持有者。最后一个引用退休条目后才能从字典移除，
        /// 因此不会让仍在等待的调用切换到另一把同键锁。
        /// </summary>
        internal sealed class ProcessLockEntry
        {
            private readonly object _syncRoot = new();
            private int _referenceCount;
            private bool _retired;

            internal SemaphoreSlim Semaphore { get; } = new(1, 1);

            internal bool TryAddReference()
            {
                lock (_syncRoot)
                {
                    if (_retired)
                    {
                        return false;
                    }

                    _referenceCount++;
                    return true;
                }
            }

            internal bool ReleaseReference()
            {
                lock (_syncRoot)
                {
                    if (_referenceCount <= 0)
                    {
                        throw new InvalidOperationException("HQ 进货单进程锁引用计数失衡。");
                    }

                    _referenceCount--;
                    if (_referenceCount != 0)
                    {
                        return false;
                    }

                    _retired = true;
                    return true;
                }
            }
        }
    }

    /// <summary>using 作用域保证异常、回滚和连接错误路径都会释放进程内互斥。</summary>
    internal sealed class LocalSupplierInvoiceHqSyncProcessLockScope : IDisposable
    {
        private LocalSupplierInvoiceHqSyncMutationLock.ProcessLockEntry? _processLock;

        internal LocalSupplierInvoiceHqSyncProcessLockScope(
            string normalizedInvoiceKey,
            LocalSupplierInvoiceHqSyncMutationLock.ProcessLockEntry processLock
        )
        {
            NormalizedInvoiceKey = normalizedInvoiceKey;
            _processLock = processLock;
        }

        internal string NormalizedInvoiceKey { get; }

        public void Dispose()
        {
            var processLock = Interlocked.Exchange(ref _processLock, null);
            if (processLock == null)
            {
                return;
            }

            // 先交给已有等待者，再在最后一个引用释放时退休并清理字典条目。
            processLock.Semaphore.Release();
            LocalSupplierInvoiceHqSyncMutationLock.ReleaseProcessLock(
                NormalizedInvoiceKey,
                processLock
            );
        }
    }

    internal sealed class LocalSupplierInvoiceHqSyncLockException : Exception
    {
        internal LocalSupplierInvoiceHqSyncLockException(string normalizedInvoiceKey, int resultCode)
            : base($"HQ 进货单业务锁获取失败: {normalizedInvoiceKey} (result={resultCode})")
        {
            NormalizedInvoiceKey = normalizedInvoiceKey;
            ResultCode = resultCode;
        }

        internal string NormalizedInvoiceKey { get; }
        internal int ResultCode { get; }
    }
}
