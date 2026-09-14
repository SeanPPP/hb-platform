-- 建立可恢复的分时发布状态和不可变版本表；默认不会改写原 HourlySalesStatistic。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'分时回填发布迁移只允许在 HBweb 主库执行', 1;

BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource=N'HBWeb:SchemaMigration:Main',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
IF @lockResult < 0 THROW 51001, N'主库迁移锁繁忙', 1;

IF OBJECT_ID(N'dbo.HourlySalesBackfillBatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HourlySalesBackfillBatch (
        Id uniqueidentifier NOT NULL PRIMARY KEY, StartDate datetime2(7) NOT NULL,
        EndDate datetime2(7) NOT NULL, RuleVersion nvarchar(40) NOT NULL,
        Status nvarchar(32) NOT NULL, RequestedBy nvarchar(100) NOT NULL,
        AppliedBy nvarchar(100) NULL, RolledBackBy nvarchar(100) NULL,
        CreatedAtUtc datetime2(7) NOT NULL, UpdatedAtUtc datetime2(7) NOT NULL,
        Error nvarchar(1000) NULL
    );
    CREATE INDEX IX_HourlySalesBackfillBatch_Status
        ON dbo.HourlySalesBackfillBatch(Status, CreatedAtUtc);
END;

IF OBJECT_ID(N'dbo.HourlySalesBackfillDay', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HourlySalesBackfillDay (
        BatchId uniqueidentifier NOT NULL, Date datetime2(7) NOT NULL,
        Status nvarchar(32) NOT NULL, SourceHash nvarchar(64) NULL,
        BeforeHash nvarchar(64) NULL, AfterHash nvarchar(64) NULL,
        BeforeJson nvarchar(max) NULL, CandidateJson nvarchar(max) NULL,
        SourceStatusJson nvarchar(max) NULL,
        ExpectedAmount decimal(18,2) NOT NULL, CandidateAmount decimal(18,2) NOT NULL,
        ExpectedOrderCount int NOT NULL, CandidateOrderCount int NOT NULL,
        [RowCount] int NOT NULL, UpdatedAtUtc datetime2(7) NOT NULL,
        Error nvarchar(1000) NULL,
        CONSTRAINT PK_HourlySalesBackfillDay PRIMARY KEY (BatchId, Date),
        CONSTRAINT FK_HourlySalesBackfillDay_Batch FOREIGN KEY (BatchId)
            REFERENCES dbo.HourlySalesBackfillBatch(Id)
    );
    CREATE INDEX IX_HourlySalesBackfillDay_Certified
        ON dbo.HourlySalesBackfillDay(Date, Status, UpdatedAtUtc);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HourlySalesBackfillDay')
      AND name = N'UX_HourlySalesBackfillDay_OneAppliedPerDate')
    CREATE UNIQUE INDEX UX_HourlySalesBackfillDay_OneAppliedPerDate
        ON dbo.HourlySalesBackfillDay(Date) WHERE Status = N'Applied';

IF OBJECT_ID(N'dbo.HourlySalesBackfillPublishedRow', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HourlySalesBackfillPublishedRow (
        BatchId uniqueidentifier NOT NULL, Date datetime2(7) NOT NULL,
        Hour int NOT NULL, BranchCode nvarchar(100) NOT NULL,
        BranchName nvarchar(100) NULL, TotalAmount decimal(18,2) NOT NULL,
        TotalQuantity int NOT NULL, OrderCount int NOT NULL,
        CustomerCount int NOT NULL, AverageOrderValue decimal(18,2) NOT NULL,
        PublishedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT PK_HourlySalesBackfillPublishedRow
            PRIMARY KEY (BatchId, Date, Hour, BranchCode),
        CONSTRAINT FK_HourlySalesBackfillPublishedRow_Day
            FOREIGN KEY (BatchId, Date)
            REFERENCES dbo.HourlySalesBackfillDay(BatchId, Date)
    );
    CREATE INDEX IX_HourlySalesBackfillPublishedRow_DateBatch
        ON dbo.HourlySalesBackfillPublishedRow(Date, BatchId, Hour, BranchCode);
END;

-- 已发布行按 BatchId 保留为不可变版本。修正必须创建新批次并原子切换 Day manifest，
-- 禁止人工或旁路任务在复核间隔内悄悄改变报表正在读取的数据。
EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_HourlySalesBackfillPublishedRow_Immutable
ON dbo.HourlySalesBackfillPublishedRow
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
        THROW 51004, N''分时发布版本不可直接修改或删除，请回滚指针后创建新版本'', 1;
END;');

-- Applied manifest 整日选择发布版本；即使发布版本是真零或已标记 source-drift，
-- 也不回退原表。这样报表不会把旧写入器产生的局部数据与发布版本混合。
EXEC(N'CREATE OR ALTER VIEW dbo.HourlySalesReadStatistic
AS
    SELECT published.[Date], published.[Hour], published.BranchCode,
        published.BranchName, published.TotalAmount, published.TotalQuantity,
        CAST(published.OrderCount AS int) AS OrderCount,
        published.CustomerCount, published.AverageOrderValue,
        published.PublishedAtUtc AS UpdateTime
    FROM dbo.HourlySalesBackfillPublishedRow published
    INNER JOIN dbo.HourlySalesBackfillDay manifest
        ON manifest.BatchId = published.BatchId
        AND manifest.[Date] = published.[Date]
        AND manifest.[Status] = N''Applied''
        AND (manifest.Error IS NULL OR LTRIM(RTRIM(manifest.Error)) = N'''')
    INNER JOIN dbo.HourlySalesBackfillBatch batch
        ON batch.Id = manifest.BatchId
        AND batch.RuleVersion = N''hourly-posm-hbsales-v1''
    UNION ALL
    SELECT legacy.[Date], legacy.[Hour], legacy.BranchCode,
        legacy.BranchName, legacy.TotalAmount, legacy.TotalQuantity,
        legacy.OrderCount, legacy.CustomerCount, legacy.AverageOrderValue,
        legacy.UpdateTime
    FROM dbo.HourlySalesStatistic legacy
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.HourlySalesBackfillDay manifest
        WHERE manifest.[Date] = legacy.[Date]
          AND manifest.[Status] = N''Applied''
    );');

IF OBJECT_ID(N'dbo.HourlySalesReadStatistic', N'V') IS NULL
    THROW 51003, N'分时统一读取视图创建失败', 1;

IF OBJECT_ID(N'dbo.TR_HourlySalesBackfillPublishedRow_Immutable', N'TR') IS NULL
   OR EXISTS (SELECT 1 FROM sys.triggers
       WHERE object_id = OBJECT_ID(N'dbo.TR_HourlySalesBackfillPublishedRow_Immutable')
         AND is_disabled = 1)
    THROW 51005, N'分时发布版本不可变围栏创建失败', 1;

COMMIT TRANSACTION;
-- 发布表是不可变版本；Day 的 Applied 唯一索引是当前日期指针，回滚只切换该指针。
