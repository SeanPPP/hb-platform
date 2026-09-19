-- 商品经营分析预计算快照：分店 × 商品的整页计算结果，由 ProductMovementReportSnapshotWorker 按门店刷新。
-- 只新增两张独立表，不修改任何既有表或索引；脚本可重复执行。
-- 执行：sqlcmd -I -b -C（含 CHECK 约束与筛选条件，必须 QUOTED_IDENTIFIER ON）。
-- 回退：禁用/回滚 API 后执行 ProductMovementReportSnapshot.Rollback.sql；读取端找不到就绪快照会自动回到实时计算。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'目标数据库必须为 HBweb。', 1;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       OBJECT_ID(N'dbo.ProductMovementReportSnapshotRun', N'U') AS ExistingRunTable,
       OBJECT_ID(N'dbo.ProductMovementReportSnapshot', N'U') AS ExistingSnapshotTable;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ProductMovementReportSnapshotRun', N'U') IS NULL
BEGIN
    -- 每次按「业务日期 + 门店」生成一批快照；读取端只用规则版本一致、状态 Ready 的最新一批。
    CREATE TABLE dbo.ProductMovementReportSnapshotRun (
        RunId uniqueidentifier NOT NULL CONSTRAINT PK_PMRSnapshotRun PRIMARY KEY,
        AsOfDate date NOT NULL,
        StoreCode nvarchar(50) NOT NULL,
        RuleVersion int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RowCount] int NOT NULL CONSTRAINT DF_PMRSnapshotRun_RowCount DEFAULT (0),
        SalesStatLastUpdate datetime NULL,
        StartedAtUtc datetime2 NOT NULL,
        CompletedAtUtc datetime2 NULL,
        LastError nvarchar(2000) NULL,
        CONSTRAINT CK_PMRSnapshotRun_Status CHECK ([Status] IN (N'Building', N'Ready', N'Failed')),
        CONSTRAINT CK_PMRSnapshotRun_Ready CHECK ([Status] <> N'Ready' OR CompletedAtUtc IS NOT NULL)
    );
    CREATE INDEX IX_PMRSnapshotRun_Lookup
        ON dbo.ProductMovementReportSnapshotRun (AsOfDate, RuleVersion, [Status], StoreCode, CompletedAtUtc)
        INCLUDE (SalesStatLastUpdate, [RowCount]);
END;

IF OBJECT_ID(N'dbo.ProductMovementReportSnapshot', N'U') IS NULL
BEGIN
    -- 列与实时查询 #FinalRows 同名同精度；含除法的三列存实时查询输出时的 CAST 结果，避免二次舍入。
    CREATE TABLE dbo.ProductMovementReportSnapshot (
        RunId uniqueidentifier NOT NULL,
        BranchCode nvarchar(50) NOT NULL,
        StoreName nvarchar(100) NULL,
        ProductCode nvarchar(50) NOT NULL,
        ProductName nvarchar(255) NULL,
        Barcode nvarchar(100) NULL,
        ImageUrl nvarchar(200) NULL,
        SalesQty30 int NOT NULL,
        SalesQty90 int NOT NULL,
        SalesQty180 int NOT NULL,
        DailySalesQty30 decimal(18, 2) NULL,
        SalesAmount90Aud decimal(38, 4) NOT NULL,
        GrossProfit90Aud decimal(38, 4) NULL,
        GrossMarginRate90 decimal(18, 4) NULL,
        LastSaleDate datetime NULL,
        NoSaleDays int NULL,
        PurchaseQty180 decimal(38, 4) NOT NULL,
        EstimatedRemainingQty decimal(38, 4) NOT NULL,
        EstimatedCoverDays decimal(18, 2) NULL,
        DataCredibility nvarchar(10) NOT NULL,
        DataExceptionFlag nvarchar(1000) NOT NULL,
        SystemSuggestion nvarchar(20) NOT NULL,
        StoreManagerAction nvarchar(200) NOT NULL,
        ActionPriority int NOT NULL,
        -- 本行在日统计里的最后更新时间；无销售的行为 NULL，读取时再用本次查询范围的最大值补齐。
        RowSalesStatLastUpdate datetime NULL,
        CONSTRAINT PK_PMRSnapshot PRIMARY KEY (RunId, BranchCode, ProductCode)
    );
END;

COMMIT;

SELECT
    (SELECT COUNT_BIG(*) FROM dbo.ProductMovementReportSnapshotRun) AS RunCount,
    (SELECT COUNT_BIG(*) FROM dbo.ProductMovementReportSnapshot) AS SnapshotRowCount;
