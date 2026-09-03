using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 货柜明细与主表汇总写入锁。普通写入先取得共享总闸，再按稳定顺序取得货柜独占锁；
/// 全量替换只取得独占总闸。锁归属当前数据库事务，事务结束时由 SQL Server 自动释放。
/// </summary>
internal static class ContainerMutationLock
{
    internal const string BusyErrorCode = "CONTAINER_DETAIL_BUSY";
    private const string GateResource = "HB:ContainerMutation:Gate";
    private const string ContainerResourcePrefix = "HB:ContainerMutation:Container:";
    private const int LockTimeoutMilliseconds = 5_000;

    internal static Task<ContainerMutationLockScope> AcquireAllAsync(ISqlSugarClient db) =>
        AcquireAsync(db, Array.Empty<string?>(), lockAll: true);

    internal static Task<ContainerMutationLockScope> AcquireContainersAsync(
        ISqlSugarClient db,
        IEnumerable<string?> containerCodes
    ) => AcquireAsync(db, containerCodes, lockAll: false);

    private static async Task<ContainerMutationLockScope> AcquireAsync(
        ISqlSugarClient db,
        IEnumerable<string?> containerCodes,
        bool lockAll
    )
    {
        if (db.Ado.Transaction == null)
        {
            throw new InvalidOperationException("货柜明细业务锁必须在数据库事务内获取");
        }

        var normalizedCodes = NormalizeContainerCodes(containerCodes);
        if (!lockAll && normalizedCodes.Count == 0)
        {
            throw new ArgumentException("按货柜获取业务锁时，货柜编码不能为空", nameof(containerCodes));
        }

        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            await AcquireDatabaseResourceAsync(
                db,
                GateResource,
                lockAll ? "Exclusive" : "Shared"
            );
            if (!lockAll)
            {
                foreach (var containerCode in normalizedCodes)
                {
                    await AcquireDatabaseResourceAsync(
                        db,
                        ContainerResourcePrefix + containerCode,
                        "Exclusive"
                    );
                }
            }
        }

        return new ContainerMutationLockScope(db, lockAll, normalizedCodes);
    }

    private static async Task AcquireDatabaseResourceAsync(
        ISqlSugarClient db,
        string resource,
        string lockMode
    )
    {
        int result;
        try
        {
            result = await db.Ado.SqlQuerySingleAsync<int>(
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = @LockMode,
                    @LockOwner = N'Transaction',
                    @LockTimeout = @LockTimeout;
                SELECT @Result;
                """,
                new SugarParameter("@Resource", resource),
                new SugarParameter("@LockMode", lockMode),
                new SugarParameter("@LockTimeout", LockTimeoutMilliseconds)
            );
        }
        catch (Exception exception) when (TryResolveAcquireConflictResultCode(exception, out _))
        {
            TryResolveAcquireConflictResultCode(exception, out var resultCode);
            throw new ContainerMutationLockException(resource, resultCode, exception);
        }

        if (result < 0)
        {
            throw new ContainerMutationLockException(resource, result);
        }
    }

    /// <summary>
    /// 从事务、ORM 或日志包装异常中提取可稳定映射为 409 的锁冲突。
    /// </summary>
    internal static bool TryResolveConflict(
        Exception? exception,
        out ContainerMutationLockException? conflict
    )
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is ContainerMutationLockException lockException)
            {
                conflict = lockException;
                return true;
            }

            if (current is ContainerMutationScopeChangedException scopeChangedException)
            {
                conflict = new ContainerMutationLockException(
                    "scope-changed",
                    -1,
                    scopeChangedException
                );
                return true;
            }

            if (
                current is Microsoft.Data.SqlClient.SqlException sqlException
                && TryResolveSqlConflictResultCode(
                    sqlException.Number,
                    includeCommandTimeout: false,
                    out var resultCode
                )
            )
            {
                conflict = new ContainerMutationLockException(
                    "unknown",
                    resultCode,
                    exception
                );
                return true;
            }
        }

        conflict = null;
        return false;
    }

    /// <summary>
    /// 只允许 SQL Server 1205 在回滚后完整重建一次事务；锁超时和普通命令超时不重试。
    /// </summary>
    internal static bool ShouldRetryDeadlock(Exception? exception, int completedRetryCount)
    {
        if (completedRetryCount != 0)
        {
            return false;
        }

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (
                current is Microsoft.Data.SqlClient.SqlException sqlException
                && ShouldRetryDeadlock(sqlException.Number, completedRetryCount)
            )
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldRetryDeadlock(int sqlErrorNumber, int completedRetryCount) =>
        completedRetryCount == 0 && sqlErrorNumber == 1205;

    /// <summary>
    /// 回滚已被 SQL Server 终止的事务可能再次抛异常；此时必须清除 ORM
    /// 持有的僵尸事务并关闭旧连接，使下一次 BeginTranAsync 真正建立新事务。
    /// </summary>
    internal static void ResetFailedTransaction(ISqlSugarClient db)
    {
        var failedTransaction = db.Ado.Transaction;
        db.Ado.Transaction = null;

        try
        {
            failedTransaction?.Dispose();
        }
        catch
        {
            // 原始并发异常必须保留；旧事务已从 ORM 状态解除。
        }

        try
        {
            db.Ado.Connection.Close();
        }
        catch
        {
            // 连接清理仅为最后保障，不得覆盖原始并发异常。
        }
    }

    private static bool TryResolveAcquireConflictResultCode(
        Exception? exception,
        out int resultCode
    )
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                resultCode = -2;
                return true;
            }

            if (
                current is Microsoft.Data.SqlClient.SqlException sqlException
                && TryResolveSqlConflictResultCode(
                    sqlException.Number,
                    includeCommandTimeout: true,
                    out resultCode
                )
            )
            {
                return true;
            }
        }

        resultCode = 0;
        return false;
    }

    internal static bool TryResolveSqlConflictResultCode(
        int sqlErrorNumber,
        bool includeCommandTimeout,
        out int resultCode
    )
    {
        resultCode = sqlErrorNumber switch
        {
            1205 => -3,
            1222 => -1,
            -2 when includeCommandTimeout => -1,
            _ => 0,
        };
        return resultCode < 0;
    }

    internal static List<string> NormalizeContainerCodes(IEnumerable<string?> containerCodes) =>
        containerCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
}

internal sealed class ContainerMutationLockScope
{
    private readonly ISqlSugarClient _db;
    private readonly HashSet<string> _containerCodeSet;

    internal ContainerMutationLockScope(
        ISqlSugarClient db,
        bool locksAllContainers,
        IEnumerable<string> containerCodes
    )
    {
        _db = db;
        LocksAllContainers = locksAllContainers;
        ContainerCodes = containerCodes.ToList();
        _containerCodeSet = ContainerCodes.ToHashSet(StringComparer.Ordinal);
    }

    internal bool LocksAllContainers { get; }
    internal IReadOnlyList<string> ContainerCodes { get; }

    internal void EnsureCovers(IEnumerable<string?> containerCodes) =>
        EnsureCovers(_db, containerCodes);

    internal void EnsureCovers(ISqlSugarClient db, IEnumerable<string?> containerCodes)
    {
        if (!ReferenceEquals(_db, db))
        {
            throw new InvalidOperationException("货柜明细写入必须使用获取业务锁的同一数据库连接");
        }

        if (LocksAllContainers)
        {
            return;
        }

        var requestedCodes = ContainerMutationLock.NormalizeContainerCodes(containerCodes);
        if (requestedCodes.Any(code => !_containerCodeSet.Contains(code)))
        {
            throw new ContainerMutationScopeChangedException(requestedCodes);
        }
    }
}

internal sealed class ContainerMutationScopeChangedException : Exception
{
    internal ContainerMutationScopeChangedException(IReadOnlyList<string> actualContainerCodes)
        : base("货柜明细所属货柜已变化，需要重新获取业务锁")
    {
        ActualContainerCodes = actualContainerCodes;
    }

    internal IReadOnlyList<string> ActualContainerCodes { get; }
}

internal sealed class ContainerMutationLockException : Exception
{
    internal ContainerMutationLockException(
        string resource,
        int resultCode,
        Exception? innerException = null
    )
        : base("同一货柜正在保存，请稍后重试", innerException)
    {
        Resource = resource;
        ResultCode = resultCode;
    }

    internal string Resource { get; }
    internal int ResultCode { get; }
}
