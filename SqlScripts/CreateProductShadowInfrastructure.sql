-- =====================================================
-- Product HQ 全量同步影子表基础设施
-- 目标:
-- 1. 全量同步只装载 Product 主表，不触碰 ProductSetCode、StoreRetailPrice、StoreMultiCodeProduct
-- 2. 使用 Product_Shadow 承接 HQ 全量数据，校验通过后快速切换
-- 3. 校验 ProductCode 非空，以及未删除 ProductCode 不能重复
-- 适用数据库: SQL Server
-- =====================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.ProductHqSyncRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductHqSyncRun
    (
        SyncRunId BIGINT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
        SyncScope NVARCHAR(100) NOT NULL CONSTRAINT DF_ProductHqSyncRun_SyncScope DEFAULT (N'HQ_FULL_SYNC'),
        Status NVARCHAR(50) NOT NULL,
        TriggeredBy NVARCHAR(128) NULL,
        StartedAt DATETIME2(0) NOT NULL CONSTRAINT DF_ProductHqSyncRun_StartedAt DEFAULT (SYSUTCDATETIME()),
        ShadowPreparedAt DATETIME2(0) NULL,
        ValidationCompletedAt DATETIME2(0) NULL,
        SwapStartedAt DATETIME2(0) NULL,
        SwapCompletedAt DATETIME2(0) NULL,
        CompletedAt DATETIME2(0) NULL,
        SourceRowCount BIGINT NULL,
        ShadowRowCount BIGINT NULL,
        InvalidProductCodeCount BIGINT NULL,
        DuplicateGroupCount BIGINT NULL,
        DuplicateRowCount BIGINT NULL,
        BackupTableName SYSNAME NULL,
        ErrorMessage NVARCHAR(4000) NULL,
        Notes NVARCHAR(4000) NULL
    );

    CREATE INDEX IX_ProductHqSyncRun_Status_StartedAt
        ON dbo.ProductHqSyncRun(Status, StartedAt DESC);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProductShadow_Prepare
    @SyncRunId BIGINT OUTPUT,
    @TriggeredBy NVARCHAR(128) = NULL,
    @DropExistingShadow BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
    BEGIN
        THROW 51001, N'正式表 dbo.Product 不存在，无法创建影子表。', 1;
    END;

    IF @SyncRunId IS NULL
    BEGIN
        INSERT INTO dbo.ProductHqSyncRun (Status, TriggeredBy, Notes)
        VALUES (N'PreparingShadow', @TriggeredBy, N'开始准备 Product 影子表。');

        SET @SyncRunId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.ProductHqSyncRun
        SET Status = N'PreparingShadow',
            TriggeredBy = COALESCE(@TriggeredBy, TriggeredBy),
            ErrorMessage = NULL,
            Notes = N'重新准备 Product 影子表。'
        WHERE SyncRunId = @SyncRunId;
    END;

    BEGIN TRY
        IF OBJECT_ID(N'dbo.Product_Shadow', N'U') IS NOT NULL AND @DropExistingShadow = 1
        BEGIN
            DROP TABLE dbo.Product_Shadow;
        END;

        IF OBJECT_ID(N'dbo.Product_Shadow', N'U') IS NOT NULL
        BEGIN
            THROW 51002, N'影子表 dbo.Product_Shadow 已存在。', 1;
        END;

        SELECT TOP (0) *
        INTO dbo.Product_Shadow
        FROM dbo.Product;

        IF COL_LENGTH(N'dbo.Product_Shadow', N'UUID') IS NULL
        BEGIN
            THROW 51003, N'影子表缺少 UUID 列，无法创建主键。', 1;
        END;

        DECLARE @ShadowPrimaryKeyName SYSNAME =
            N'PK_Product_Shadow_' + CAST(@SyncRunId AS NVARCHAR(20));
        DECLARE @CreatePrimaryKeySql NVARCHAR(MAX) =
            N'ALTER TABLE dbo.Product_Shadow ADD CONSTRAINT '
            + QUOTENAME(@ShadowPrimaryKeyName)
            + N' PRIMARY KEY CLUSTERED (UUID);';

        EXEC sys.sp_executesql @CreatePrimaryKeySql;

        CREATE INDEX IX_Product_Shadow_ProductCode
            ON dbo.Product_Shadow(ProductCode)
            INCLUDE (UUID, ProductName, ItemNumber, Barcode, UpdatedAt, IsDeleted);

        CREATE INDEX IX_Product_Shadow_Status
            ON dbo.Product_Shadow(IsDeleted, IsActive)
            INCLUDE (ProductCode, ProductName);

        CREATE INDEX IX_Product_Shadow_ItemNumber
            ON dbo.Product_Shadow(ItemNumber);

        CREATE INDEX IX_Product_Shadow_Barcode
            ON dbo.Product_Shadow(Barcode);

        UPDATE dbo.ProductHqSyncRun
        SET Status = N'ShadowPrepared',
            ShadowPreparedAt = SYSUTCDATETIME(),
            Notes = N'Product 影子表已创建，可执行 HQ 全量装载。'
        WHERE SyncRunId = @SyncRunId;
    END TRY
    BEGIN CATCH
        UPDATE dbo.ProductHqSyncRun
        SET Status = N'PrepareFailed',
            CompletedAt = SYSUTCDATETIME(),
            ErrorMessage = ERROR_MESSAGE()
        WHERE SyncRunId = @SyncRunId;

        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProductShadow_Validate
    @SyncRunId BIGINT,
    @SourceRowCount BIGINT = NULL,
    @PreviewRows INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.Product_Shadow', N'U') IS NULL
    BEGIN
        THROW 51004, N'影子表 dbo.Product_Shadow 不存在，请先执行 dbo.usp_ProductShadow_Prepare。', 1;
    END;

    DECLARE @ShadowRowCount BIGINT;
    DECLARE @InvalidProductCodeCount BIGINT;
    DECLARE @DuplicateGroupCount BIGINT;
    DECLARE @DuplicateRowCount BIGINT;

    SELECT @ShadowRowCount = COUNT_BIG(*)
    FROM dbo.Product_Shadow;

    SELECT @InvalidProductCodeCount = COUNT_BIG(*)
    FROM dbo.Product_Shadow
    WHERE NULLIF(LTRIM(RTRIM(ProductCode)), N'') IS NULL;

    ;WITH DuplicateGroups AS
    (
        SELECT ProductCode, COUNT_BIG(*) AS RowCount
        FROM dbo.Product_Shadow
        WHERE ISNULL(IsDeleted, 0) = 0
          AND NULLIF(LTRIM(RTRIM(ProductCode)), N'') IS NOT NULL
        GROUP BY ProductCode
        HAVING COUNT_BIG(*) > 1
    )
    SELECT
        @DuplicateGroupCount = COUNT_BIG(*),
        @DuplicateRowCount = ISNULL(SUM(RowCount), 0)
    FROM DuplicateGroups;

    UPDATE dbo.ProductHqSyncRun
    SET Status =
            CASE
                WHEN @InvalidProductCodeCount > 0 OR @DuplicateGroupCount > 0
                    THEN N'ValidationFailed'
                ELSE N'Validated'
            END,
        SourceRowCount = COALESCE(@SourceRowCount, SourceRowCount),
        ShadowRowCount = @ShadowRowCount,
        InvalidProductCodeCount = @InvalidProductCodeCount,
        DuplicateGroupCount = @DuplicateGroupCount,
        DuplicateRowCount = @DuplicateRowCount,
        ValidationCompletedAt = SYSUTCDATETIME(),
        ErrorMessage =
            CASE
                WHEN @InvalidProductCodeCount > 0
                    THEN N'Product_Shadow 存在空 ProductCode。'
                WHEN @DuplicateGroupCount > 0
                    THEN N'Product_Shadow 存在未删除 ProductCode 重复。'
                ELSE NULL
            END
    WHERE SyncRunId = @SyncRunId;

    IF @InvalidProductCodeCount > 0
    BEGIN
        SELECT TOP (@PreviewRows) *
        FROM dbo.Product_Shadow
        WHERE NULLIF(LTRIM(RTRIM(ProductCode)), N'') IS NULL;

        THROW 51005, N'Product_Shadow 校验失败：ProductCode 不能为空。', 1;
    END;

    IF @DuplicateGroupCount > 0
    BEGIN
        SELECT TOP (@PreviewRows) ProductCode, COUNT_BIG(*) AS RowCount
        FROM dbo.Product_Shadow
        WHERE ISNULL(IsDeleted, 0) = 0
        GROUP BY ProductCode
        HAVING COUNT_BIG(*) > 1
        ORDER BY RowCount DESC;

        THROW 51006, N'Product_Shadow 校验失败：未删除 ProductCode 重复。', 1;
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProductShadow_Swap
    @SyncRunId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.Product_Shadow', N'U') IS NULL
    BEGIN
        THROW 51007, N'影子表 dbo.Product_Shadow 不存在，无法切换。', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ProductHqSyncRun
        WHERE SyncRunId = @SyncRunId
          AND Status = N'Validated'
    )
    BEGIN
        THROW 51008, N'本次同步尚未通过校验，禁止切换。', 1;
    END;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'Product_HQ_FULL_SYNC_SWAP',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 10000;

    IF @LockResult < 0
    BEGIN
        THROW 51009, N'无法获取 Product 全量同步切换锁。', 1;
    END;

    DECLARE @BackupTableName SYSNAME =
        N'Product_Backup_' + FORMAT(SYSUTCDATETIME(), N'yyyyMMddHHmmss') + N'_' + CAST(@SyncRunId AS NVARCHAR(20));

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.ProductHqSyncRun
        SET Status = N'Swapping',
            SwapStartedAt = SYSUTCDATETIME(),
            BackupTableName = @BackupTableName
        WHERE SyncRunId = @SyncRunId;

        EXEC sys.sp_rename N'dbo.Product', @BackupTableName;
        EXEC sys.sp_rename N'dbo.Product_Shadow', N'Product';

        UPDATE dbo.ProductHqSyncRun
        SET Status = N'Succeeded',
            SwapCompletedAt = SYSUTCDATETIME(),
            CompletedAt = SYSUTCDATETIME(),
            Notes = N'Product 影子表切换完成。关联表未在本流程中处理。'
        WHERE SyncRunId = @SyncRunId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        UPDATE dbo.ProductHqSyncRun
        SET Status = N'SwapFailed',
            CompletedAt = SYSUTCDATETIME(),
            ErrorMessage = ERROR_MESSAGE()
        WHERE SyncRunId = @SyncRunId;

        THROW;
    END CATCH;
END;
GO
