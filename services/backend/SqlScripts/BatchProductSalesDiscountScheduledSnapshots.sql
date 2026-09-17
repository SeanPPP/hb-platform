-- 折扣日统计：仅新增兼容字段、索引和独立日进度表；旧查询快照完整保留。
-- 前提：先执行 BatchProductSalesDiscountSnapshot.sql 安装基础表。
-- 部署前记录服务器/数据库/现有格式 1 行数，保留当前 API 镜像与配置。
-- 回退：禁用新任务并恢复旧 API；保留新增字段、进度和格式 2 数据，不需删表或恢复覆盖。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'目标数据库必须为 HBweb。', 1;
IF OBJECT_ID(N'dbo.BatchProductSalesDiscountSnapshot', N'U') IS NULL
    THROW 51001, N'请先安装 BatchProductSalesDiscountSnapshot.sql。', 1;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       COUNT_BIG(*) AS ExistingSnapshotCount FROM dbo.BatchProductSalesDiscountSnapshot;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.BatchProductSalesDiscountSnapshot', N'SnapshotFormat') IS NULL
    ALTER TABLE dbo.BatchProductSalesDiscountSnapshot ADD SnapshotFormat int NOT NULL
        CONSTRAINT DF_BatchSalesDiscount_SnapshotFormat DEFAULT (1) WITH VALUES;

IF OBJECT_ID(N'dbo.BatchProductSalesDiscountRefreshState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BatchProductSalesDiscountRefreshState (
        [Date] datetime2 NOT NULL CONSTRAINT PK_BatchSalesDiscountRefreshState PRIMARY KEY,
        [Status] nvarchar(20) NOT NULL,
        RuleVersion int NOT NULL,
        StatisticsVersion nvarchar(64) NOT NULL,
        SourceVersion nvarchar(64) NOT NULL,
        RequestedAtUtc datetime2 NOT NULL,
        NextAttemptAtUtc datetime2 NOT NULL,
        Attempts int NOT NULL,
        LeaseToken nvarchar(32) NULL,
        LeaseUntilUtc datetime2 NULL,
        CompletedAtUtc datetime2 NULL,
        LastCheckedAtUtc datetime2 NULL,
        LastError nvarchar(2000) NULL,
        SnapshotCount int NOT NULL,
        ReconcileRequested bit NOT NULL,
        CONSTRAINT CK_BatchSalesDiscountRefresh_Day CHECK ([Date] = CONVERT(date, [Date])),
        CONSTRAINT CK_BatchSalesDiscountRefresh_Attempts CHECK (Attempts >= 0),
        CONSTRAINT CK_BatchSalesDiscountRefresh_Fresh CHECK
            ([Status] <> N'Fresh' OR (CompletedAtUtc IS NOT NULL AND SourceVersion <> N'' AND StatisticsVersion <> N''))
    );
    CREATE INDEX IX_BatchSalesDiscountRefresh_Queue
        ON dbo.BatchProductSalesDiscountRefreshState ([Status], NextAttemptAtUtc, [Date])
        INCLUDE (Attempts, LeaseUntilUtc, LastCheckedAtUtc);
END;

-- 动态批次让 SQL Server 在新增列完成后解析索引，脚本可重复执行。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BatchProductSalesDiscountSnapshot')
    AND [name] = N'IX_BatchSalesDiscount_DailyProduct')
    EXEC(N'CREATE UNIQUE INDEX IX_BatchSalesDiscount_DailyProduct
        ON dbo.BatchProductSalesDiscountSnapshot (ProductCode, StartDate)
        INCLUDE (EndDate, SourceVersion, CompletedAtUtc, Status)
        WHERE SnapshotFormat = 2;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BatchProductSalesDiscountSnapshot')
    AND [name] = N'IX_BatchSalesDiscount_DailyPublish')
    EXEC(N'CREATE INDEX IX_BatchSalesDiscount_DailyPublish
        ON dbo.BatchProductSalesDiscountSnapshot (SnapshotFormat, StartDate);');
COMMIT;

SELECT [Status], COUNT_BIG(*) AS DayCount, MIN([Date]) AS EarliestDate,
       MAX([Date]) AS LatestDate FROM dbo.BatchProductSalesDiscountRefreshState GROUP BY [Status];
