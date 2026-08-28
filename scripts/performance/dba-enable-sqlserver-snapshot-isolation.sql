/*
  性能基线 SQL Server Snapshot 一次性 DBA 脚本。

  门禁：
  1. 仅由 DBA 在维护窗口、确认备份与 tempdb/version store 容量后手工执行。
  2. 必须从目标业务数据库连接执行；脚本只修改当前数据库。
  3. 不使用 ROLLBACK IMMEDIATE，不会主动终止现有事务；若长事务阻塞应停止并另行处置。
  4. 本脚本不启用 READ_COMMITTED_SNAPSHOT，普通 READ COMMITTED 行为保持不变。
*/

SET NOCOUNT ON;

IF @@TRANCOUNT <> 0
BEGIN
    THROW 51020, '请在显式事务外执行 Snapshot isolation DBA 脚本', 1;
END;

DECLARE @DatabaseName sysname = DB_NAME();
IF @DatabaseName IS NULL OR DB_ID() IN (1, 2, 3, 4)
BEGIN
    THROW 51021, '必须连接到目标业务数据库，禁止修改系统数据库', 1;
END;

IF EXISTS (
    SELECT 1
    FROM sys.databases
    WHERE [name] = @DatabaseName AND snapshot_isolation_state = 1
)
BEGIN
    SELECT
        [name],
        snapshot_isolation_state_desc,
        is_read_committed_snapshot_on
    FROM sys.databases
    WHERE [name] = @DatabaseName;
    RETURN;
END;

DECLARE @Sql nvarchar(max) =
    N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' SET ALLOW_SNAPSHOT_ISOLATION ON';
EXEC sys.sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.databases
    WHERE [name] = @DatabaseName AND snapshot_isolation_state = 1
)
BEGIN
    THROW 51022, 'ALLOW_SNAPSHOT_ISOLATION 未进入 ON 状态', 1;
END;

SELECT
    [name],
    snapshot_isolation_state_desc,
    is_read_committed_snapshot_on
FROM sys.databases
WHERE [name] = @DatabaseName;
