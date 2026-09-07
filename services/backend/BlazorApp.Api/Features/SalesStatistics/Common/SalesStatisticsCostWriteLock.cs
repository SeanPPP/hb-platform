using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 商品成本写入的日期锁。锁由当前数据库事务持有，覆盖成本重读、统计替换及派生汇总。
/// </summary>
internal static class SalesStatisticsCostWriteLock
{
    private const int LockTimeoutMilliseconds = 10_000;

    internal static async Task AcquireAsync(ISqlSugarClient db, DateTime date)
    {
        if (db.Ado.Transaction == null)
            throw new InvalidOperationException("销售统计成本写入锁必须在数据库事务内获取");

        // 2025 的 POSM/HBSales 双源批次固定按 global -> date 顺序取两把锁，
        // 其他日期只取日期锁；所有入口必须遵守同一顺序，避免锁顺序死锁。
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            return;

        if (date.Year == 2025)
            await AcquireResourceAsync(db, "HB:SalesStatistics:CostWrite:2025-global");
        await AcquireResourceAsync(db, $"HB:SalesStatistics:CostWrite:date:{date.Date:yyyy-MM-dd}");
    }

    private static async Task AcquireResourceAsync(ISqlSugarClient db, string resource)
    {
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
            new SugarParameter("@Resource", resource),
            new SugarParameter("@LockTimeout", LockTimeoutMilliseconds));
        if (result < 0)
            throw new SalesStatisticsCostWriteLockException(resource, result);
    }

    internal static bool IsBusy(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SalesStatisticsCostWriteLockException)
                return true;
        }
        return false;
    }
}

internal sealed class SalesStatisticsCostWriteLockException : InvalidOperationException
{
    internal SalesStatisticsCostWriteLockException(string resource, int resultCode)
        : base($"销售统计成本写入锁获取失败，资源={resource}，结果码={resultCode}")
    {
        Resource = resource;
        ResultCode = resultCode;
    }

    internal string Resource { get; }
    internal int ResultCode { get; }
}
