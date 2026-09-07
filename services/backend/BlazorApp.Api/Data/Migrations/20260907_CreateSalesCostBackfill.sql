-- 仅在已核验并有恢复点的 Web 主库执行；不修改既有销售事实表。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'成本回填审计迁移只允许在 HBweb 主库执行', 1;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource=N'HBWeb:SchemaMigration:Main',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
IF @lockResult < 0 THROW 51001, N'主库迁移锁繁忙', 1;

IF OBJECT_ID(N'dbo.SalesCostBackfillBatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesCostBackfillBatch (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        StartDate datetime2(7) NOT NULL, EndDate datetime2(7) NOT NULL,
        RuleVersion nvarchar(40) NOT NULL, Status nvarchar(32) NOT NULL,
        RequestedBy nvarchar(100) NOT NULL, Automatic bit NOT NULL,
        AppliedBy nvarchar(100) NULL, RolledBackBy nvarchar(100) NULL,
        PreviewRetriedBy nvarchar(100) NULL,
        CreatedAtUtc datetime2(7) NOT NULL, UpdatedAtUtc datetime2(7) NOT NULL,
        Error nvarchar(1000) NULL
    );
    CREATE INDEX IX_SalesCostBackfillBatch_Status ON dbo.SalesCostBackfillBatch(Status, CreatedAtUtc);
END;
IF OBJECT_ID(N'dbo.SalesCostBackfillDay', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesCostBackfillDay (
        BatchId uniqueidentifier NOT NULL, Date datetime2(7) NOT NULL,
        Status nvarchar(32) NOT NULL, SourceHash nvarchar(64) NULL, SnapshotVersion nvarchar(64) NULL,
        FailedOperation nvarchar(32) NULL,
        BeforePublicationJson nvarchar(max) NULL, AfterPublicationJson nvarchar(max) NULL,
        CandidateCount int NOT NULL, UnresolvedCount int NOT NULL, AppliedCount int NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL, Error nvarchar(1000) NULL,
        CONSTRAINT PK_SalesCostBackfillDay PRIMARY KEY (BatchId, Date),
        CONSTRAINT FK_SalesCostBackfillDay_Batch FOREIGN KEY (BatchId) REFERENCES dbo.SalesCostBackfillBatch(Id)
    );
END;
IF OBJECT_ID(N'dbo.SalesCostBackfillItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesCostBackfillItem (
        Id uniqueidentifier NOT NULL PRIMARY KEY, BatchId uniqueidentifier NOT NULL,
        Date datetime2(7) NOT NULL, BranchCode nvarchar(50) NOT NULL,
        SupplierCode nvarchar(50) NOT NULL, ProductCode nvarchar(50) NOT NULL,
        Status nvarchar(32) NOT NULL, Reason nvarchar(200) NOT NULL,
        ConflictReason nvarchar(200) NULL,
        BeforeJson nvarchar(max) NOT NULL, ProposedJson nvarchar(max) NULL, AfterJson nvarchar(max) NULL,
        EvidenceJson nvarchar(max) NOT NULL, UpdatedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT FK_SalesCostBackfillItem_Day FOREIGN KEY (BatchId, Date) REFERENCES dbo.SalesCostBackfillDay(BatchId, Date),
        CONSTRAINT UQ_SalesCostBackfillItem_Target UNIQUE (BatchId, Date, BranchCode, SupplierCode, ProductCode)
    );
    CREATE INDEX IX_SalesCostBackfillItem_Page ON dbo.SalesCostBackfillItem(BatchId, Date, Status);
END;
COMMIT TRANSACTION;
-- 回滚应用时保留审计表；数据回滚必须使用批次 API 的比较后恢复流程。
