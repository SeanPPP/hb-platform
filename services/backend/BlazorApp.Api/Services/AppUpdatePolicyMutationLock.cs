using SqlSugar;

namespace BlazorApp.Api.Services;

internal static class AppUpdatePolicyMutationLock
{
    internal static async Task AcquireAsync(ISqlSugarClient db, string resource)
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        // 关键位置：调用方必须已开启事务，让跨实例策略写入在重读和升版期间保持串行。
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
            new SugarParameter("@Resource", resource)
        );
        if (result < 0)
        {
            throw new InvalidOperationException("获取 App 更新策略写锁失败，请稍后重试");
        }
    }

    internal static bool IsUniqueConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (
                current is Microsoft.Data.SqlClient.SqlException
                {
                    Number: 2601 or 2627,
                }
                || current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("2601", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("2627", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}
