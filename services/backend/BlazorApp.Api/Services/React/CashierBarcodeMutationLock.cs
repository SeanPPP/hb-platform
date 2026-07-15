using SqlSugar;

namespace BlazorApp.Api.Services.React;

internal static class CashierBarcodeMutationLock
{
    internal const string ResourceName = "CashierBarcodeMutation";

    internal static async Task AcquireAsync(ISqlSugarClient db)
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            // 关键逻辑：SQLite 测试库不支持 SQL Server applock，事务与唯一约束仍覆盖测试语义。
            return;
        }

        const string sql = """
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = @resource,
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 10000;
IF @lockResult < 0 THROW 51006, '获取收银条码全局写锁失败', 1;
""";

        // 关键逻辑：所有条码来源共用同一事务级锁，锁只会在提交或回滚时释放。
        await db.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@resource", ResourceName)
        );
    }
}
