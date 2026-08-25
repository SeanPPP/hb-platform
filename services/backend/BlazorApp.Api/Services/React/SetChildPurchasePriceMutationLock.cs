using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 套装子项成本写入锁。所有会改变主成本、子项零售价或套装关系的事务，
/// 必须先获取总闸，再按主商品编码稳定排序获取业务键锁。
/// </summary>
internal static class SetChildPurchasePriceMutationLock
{
    internal const string BusyErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY";
    private const string GateResource = "HB:SetChildPurchasePrice:Gate";
    private const string ProductResourcePrefix = "HB:SetChildPurchasePrice:Product:";
    private const int LockTimeoutMilliseconds = 10_000;

    internal static Task<SetChildPurchasePriceLockScope> AcquireAllAsync(ISqlSugarClient db) =>
        AcquireAsync(db, Array.Empty<string?>(), lockAll: true);

    internal static Task<SetChildPurchasePriceLockScope> AcquireProductsAsync(
        ISqlSugarClient db,
        IEnumerable<string?> productCodes
    ) => AcquireAsync(db, productCodes, lockAll: false);

    private static async Task<SetChildPurchasePriceLockScope> AcquireAsync(
        ISqlSugarClient db,
        IEnumerable<string?> productCodes,
        bool lockAll
    )
    {
        if (db.Ado.Transaction == null)
        {
            throw new InvalidOperationException("套装子项成本业务锁必须在数据库事务内获取");
        }

        var normalizedCodes = NormalizeProductCodes(productCodes);
        if (!lockAll && normalizedCodes.Count == 0)
        {
            throw new ArgumentException("按商品获取套装子项成本锁时，商品编码不能为空", nameof(productCodes));
        }

        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            await AcquireDatabaseResourceAsync(db, GateResource, lockAll ? "Exclusive" : "Shared");
            if (!lockAll)
            {
                foreach (var productCode in normalizedCodes)
                {
                    await AcquireDatabaseResourceAsync(
                        db,
                        ProductResourcePrefix + productCode,
                        "Exclusive"
                    );
                }
            }
        }

        return new SetChildPurchasePriceLockScope(db, lockAll, normalizedCodes);
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
        catch (Exception ex) when (TryResolveAcquireConflictResultCode(ex, out _))
        {
            TryResolveAcquireConflictResultCode(ex, out var resultCode);
            throw new SetChildPurchasePriceLockException(resource, resultCode, ex);
        }
        if (result < 0)
        {
            throw new SetChildPurchasePriceLockException(resource, result);
        }
    }

    internal static bool TryResolveConflictResultCode(Exception? exception, out int resultCode)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SetChildPurchasePriceLockException lockException)
            {
                resultCode = lockException.ResultCode;
                return true;
            }

            if (current is Microsoft.Data.SqlClient.SqlException sqlException)
            {
                if (
                    TryResolveSqlConflictResultCode(
                        sqlException.Number,
                        includeCommandTimeout: false,
                        out resultCode
                    )
                )
                {
                    return true;
                }
            }
        }

        resultCode = 0;
        return false;
    }

    /// <summary>
    /// 只有执行 sp_getapplock 的阶段才把取消和命令超时解释为业务锁冲突；
    /// 普通查询中的相同异常必须保留原始语义。
    /// </summary>
    private static bool TryResolveAcquireConflictResultCode(
        Exception? exception,
        out int resultCode
    )
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SetChildPurchasePriceLockException lockException)
            {
                resultCode = lockException.ResultCode;
                return true;
            }

            if (current is OperationCanceledException)
            {
                resultCode = -2;
                return true;
            }

            if (current is Microsoft.Data.SqlClient.SqlException sqlException)
            {
                if (
                    TryResolveSqlConflictResultCode(
                        sqlException.Number,
                        includeCommandTimeout: true,
                        out resultCode
                    )
                )
                {
                    return true;
                }
            }
        }

        resultCode = 0;
        return false;
    }

    /// <summary>
    /// SQL -2 只有发生在 sp_getapplock 调用内部时才属于业务锁冲突；
    /// 普通数据库命令超时必须保留原始错误语义。
    /// </summary>
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

    /// <summary>
    /// 从事务或 ORM 包装异常中提取业务锁冲突，供普通业务入口统一返回 409。
    /// </summary>
    internal static bool TryResolveConflict(
        Exception? exception,
        out SetChildPurchasePriceLockException? conflict
    )
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SetChildPurchasePriceLockException lockException)
            {
                conflict = lockException;
                return true;
            }
        }

        if (TryResolveConflictResultCode(exception, out var resultCode))
        {
            conflict = new SetChildPurchasePriceLockException("unknown", resultCode, exception);
            return true;
        }

        conflict = null;
        return false;
    }

    internal static List<string> NormalizeProductCodes(IEnumerable<string?> productCodes) =>
        productCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
}

internal sealed class SetChildPurchasePriceLockScope
{
    private readonly ISqlSugarClient _db;
    private readonly HashSet<string> _productCodes;

    internal SetChildPurchasePriceLockScope(
        ISqlSugarClient db,
        bool locksAllProducts,
        IEnumerable<string> productCodes
    )
    {
        _db = db;
        LocksAllProducts = locksAllProducts;
        _productCodes = productCodes.ToHashSet(StringComparer.Ordinal);
    }

    internal bool LocksAllProducts { get; }

    internal void EnsureCovers(ISqlSugarClient db, IEnumerable<string?> productCodes)
    {
        if (!ReferenceEquals(_db, db))
        {
            throw new InvalidOperationException("套装子项成本重算必须使用获取业务锁的同一数据库连接");
        }

        if (LocksAllProducts)
        {
            return;
        }

        var requestedCodes = SetChildPurchasePriceMutationLock.NormalizeProductCodes(productCodes);
        if (requestedCodes.Count == 0 || requestedCodes.Any(code => !_productCodes.Contains(code)))
        {
            throw new InvalidOperationException("套装子项成本业务锁未覆盖全部待重算商品");
        }
    }
}

internal sealed class SetChildPurchasePriceLockException : Exception
{
    internal SetChildPurchasePriceLockException(
        string resource,
        int resultCode,
        Exception? innerException = null
    )
        : base("套装子项成本正在被其他操作更新，请稍后重试", innerException)
    {
        Resource = resource;
        ResultCode = resultCode;
    }

    internal string Resource { get; }
    internal int ResultCode { get; }
}
