-- 回退商品经营分析预计算快照。只删除本功能新增的两张表，不触碰任何既有表。
-- 前提：API 已回滚到不含 ProductMovementReportSnapshotWorker 的版本，或已停用计划任务；
-- 快照是可重建的派生数据，删除后报表自动回到实时计算，无需备份。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'目标数据库必须为 HBweb。', 1;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
-- 表可能已不存在（重复执行），用动态 SQL 避免执行期对象解析失败。
IF OBJECT_ID(N'dbo.ProductMovementReportSnapshotRun', N'U') IS NOT NULL
    EXEC(N'SELECT COUNT_BIG(*) AS RunCountBefore FROM dbo.ProductMovementReportSnapshotRun;');

BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ProductMovementReportSnapshot', N'U') IS NOT NULL
    DROP TABLE dbo.ProductMovementReportSnapshot;
IF OBJECT_ID(N'dbo.ProductMovementReportSnapshotRun', N'U') IS NOT NULL
    DROP TABLE dbo.ProductMovementReportSnapshotRun;
COMMIT;

SELECT OBJECT_ID(N'dbo.ProductMovementReportSnapshotRun', N'U') AS RunTableAfter,
       OBJECT_ID(N'dbo.ProductMovementReportSnapshot', N'U') AS SnapshotTableAfter;
