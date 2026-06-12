-- =====================================================
-- StoreRetailPrice HQ 全量同步影子表基础设施
-- 目标:
-- 1. 使用 StoreRetailPrice_Shadow 承接约 1000w 行 HQ 全量装载
-- 2. 装载失败、校验失败时不影响正式表 StoreRetailPrice
-- 3. 仅在最后切换阶段使用 sp_getapplock + sp_rename 做极短事务
-- 4. 保留 StoreRetailPrice_Backup_* 备份表，便于快速回滚
-- 适用数据库: SQL Server
-- =====================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- =====================================================
-- 1. 同步运行日志表
-- =====================================================
IF OBJECT_ID(N'dbo.StoreRetailPriceSyncRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoreRetailPriceSyncRun
    (
        SyncRunId BIGINT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
        SyncScope NVARCHAR(100) NOT NULL CONSTRAINT DF_StoreRetailPriceSyncRun_SyncScope DEFAULT (N'HQ_FULL_SYNC'),
        Status NVARCHAR(50) NOT NULL,
        TriggeredBy NVARCHAR(128) NULL,
        StartedAt DATETIME2(0) NOT NULL CONSTRAINT DF_StoreRetailPriceSyncRun_StartedAt DEFAULT (SYSUTCDATETIME()),
        ShadowPreparedAt DATETIME2(0) NULL,
        ValidationCompletedAt DATETIME2(0) NULL,
        SwapStartedAt DATETIME2(0) NULL,
        SwapCompletedAt DATETIME2(0) NULL,
        CompletedAt DATETIME2(0) NULL,
        SourceRowCount BIGINT NULL,
        ShadowRowCount BIGINT NULL,
        DuplicateGroupCount BIGINT NULL,
        DuplicateRowCount BIGINT NULL,
        BackupTableName SYSNAME NULL,
        ErrorMessage NVARCHAR(4000) NULL,
        Notes NVARCHAR(4000) NULL
    );

    CREATE INDEX IX_StoreRetailPriceSyncRun_Status_StartedAt
        ON dbo.StoreRetailPriceSyncRun(Status, StartedAt DESC);
END;
GO

-- =====================================================
-- 2. 创建/重建影子表
-- 说明:
-- - 使用 SELECT TOP (0) INTO 从正式表复制列结构
-- - SELECT INTO 不会复制原表触发器、默认约束和非主键索引，因此脚本会补齐核心主键/索引
-- - 如果正式表后续新增额外约束或触发器，请同步扩展本脚本
-- - 全量装载、重复校验都发生在影子表
-- - 此步骤失败不会影响正式表
-- =====================================================
CREATE OR ALTER PROCEDURE dbo.usp_StoreRetailPriceShadow_Prepare
    @SyncRunId BIGINT OUTPUT,
    @TriggeredBy NVARCHAR(128) = NULL,
    @DropExistingShadow BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.StoreRetailPrice', N'U') IS NULL
    BEGIN
        THROW 50001, N'正式表 dbo.StoreRetailPrice 不存在，无法创建影子表。', 1;
    END;

    IF @SyncRunId IS NULL
    BEGIN
        INSERT INTO dbo.StoreRetailPriceSyncRun
        (
            Status,
            TriggeredBy,
            Notes
        )
        VALUES
        (
            N'PreparingShadow',
            @TriggeredBy,
            N'开始准备影子表。此阶段仅操作 StoreRetailPrice_Shadow，失败不会影响正式表。'
        );

        SET @SyncRunId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.StoreRetailPriceSyncRun
        SET Status = N'PreparingShadow',
            TriggeredBy = COALESCE(@TriggeredBy, TriggeredBy),
            ErrorMessage = NULL,
            Notes = N'重新准备影子表。此阶段仅操作 StoreRetailPrice_Shadow，失败不会影响正式表。'
        WHERE SyncRunId = @SyncRunId;
    END;

    BEGIN TRY
        IF OBJECT_ID(N'dbo.StoreRetailPrice_Shadow', N'U') IS NOT NULL AND @DropExistingShadow = 1
        BEGIN
            DROP TABLE dbo.StoreRetailPrice_Shadow;
        END;

        IF OBJECT_ID(N'dbo.StoreRetailPrice_Shadow', N'U') IS NOT NULL
        BEGIN
            THROW 50002, N'影子表 dbo.StoreRetailPrice_Shadow 已存在。如需重建，请传入 @DropExistingShadow = 1。', 1;
        END;

        -- 复制列结构。装载数据前影子表为空，因此这里不会触碰正式表数据。
        SELECT TOP (0) *
        INTO dbo.StoreRetailPrice_Shadow
        FROM dbo.StoreRetailPrice;

        -- 影子表主键与查询索引显式补齐，避免切换后缺少核心约束。
        IF COL_LENGTH(N'dbo.StoreRetailPrice_Shadow', N'UUID') IS NULL
        BEGIN
            THROW 50003, N'影子表缺少 UUID 列，无法创建主键。', 1;
        END;

        -- 主键约束名在 SQL Server schema 内唯一。影子表切换为正式表后会保留约束名，
        -- 因此这里使用 SyncRunId 生成唯一名称，保证连续多轮全量同步不会撞名。
        DECLARE @ShadowPrimaryKeyName SYSNAME =
            N'PK_StoreRetailPrice_Shadow_' + CAST(@SyncRunId AS NVARCHAR(20));
        DECLARE @CreatePrimaryKeySql NVARCHAR(MAX) =
            N'ALTER TABLE dbo.StoreRetailPrice_Shadow ADD CONSTRAINT '
            + QUOTENAME(@ShadowPrimaryKeyName)
            + N' PRIMARY KEY CLUSTERED (UUID);';

        EXEC sys.sp_executesql @CreatePrimaryKeySql;

        CREATE INDEX IX_StoreRetailPrice_Shadow_BusinessKey
            ON dbo.StoreRetailPrice_Shadow(StoreCode, ProductCode, SupplierCode)
            INCLUDE (UUID, UpdatedAt, IsDeleted);

        CREATE INDEX IX_StoreRetailPrice_Shadow_ProductCode_StoreCode
            ON dbo.StoreRetailPrice_Shadow(ProductCode, StoreCode)
            INCLUDE (SupplierCode, StoreRetailPriceValue, PurchasePrice, DiscountRate, IsDeleted);

        CREATE INDEX IX_StoreRetailPrice_Shadow_StoreCode
            ON dbo.StoreRetailPrice_Shadow(StoreCode);

        CREATE INDEX IX_StoreRetailPrice_Shadow_SupplierCode
            ON dbo.StoreRetailPrice_Shadow(SupplierCode);

        CREATE INDEX IX_StoreRetailPrice_Shadow_PurchasePrice
            ON dbo.StoreRetailPrice_Shadow(PurchasePrice);

        CREATE INDEX IX_StoreRetailPrice_Shadow_StoreRetailPriceValue
            ON dbo.StoreRetailPrice_Shadow(StoreRetailPriceValue);

        CREATE INDEX IX_StoreRetailPrice_Shadow_DiscountRate
            ON dbo.StoreRetailPrice_Shadow(DiscountRate);

        CREATE INDEX IX_StoreRetailPrice_Shadow_IsActive
            ON dbo.StoreRetailPrice_Shadow(IsActive);

        CREATE INDEX IX_StoreRetailPrice_Shadow_IsAutoPricing
            ON dbo.StoreRetailPrice_Shadow(IsAutoPricing);

        CREATE INDEX IX_StoreRetailPrice_Shadow_Status
            ON dbo.StoreRetailPrice_Shadow(IsDeleted, IsActive, IsAutoPricing)
            INCLUDE (StoreCode, ProductCode, SupplierCode);

        UPDATE dbo.StoreRetailPriceSyncRun
        SET Status = N'ShadowPrepared',
            ShadowPreparedAt = SYSUTCDATETIME(),
            Notes = N'影子表已创建，可安全执行 HQ 全量装载。此时正式表仍保持在线。'
        WHERE SyncRunId = @SyncRunId;
    END TRY
    BEGIN CATCH
        UPDATE dbo.StoreRetailPriceSyncRun
        SET Status = N'PrepareFailed',
            CompletedAt = SYSUTCDATETIME(),
            ErrorMessage = ERROR_MESSAGE(),
            Notes = N'影子表准备失败。正式表 StoreRetailPrice 未被切换，不受影响。'
        WHERE SyncRunId = @SyncRunId;

        THROW;
    END CATCH;
END;
GO

-- =====================================================
-- 3. 影子表业务键重复校验
-- 说明:
-- - 默认按未删除数据校验 StoreCode + ProductCode + SupplierCode
-- - 校验失败时仅标记本次同步失败，不会触碰正式表
-- =====================================================
CREATE OR ALTER PROCEDURE dbo.usp_StoreRetailPriceShadow_Validate
    @SyncRunId BIGINT,
    @SourceRowCount BIGINT = NULL,
    @PreviewRows INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.StoreRetailPrice_Shadow', N'U') IS NULL
    BEGIN
        THROW 50004, N'影子表 dbo.StoreRetailPrice_Shadow 不存在，请先执行 dbo.usp_StoreRetailPriceShadow_Prepare。', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.StoreRetailPriceSyncRun
        WHERE SyncRunId = @SyncRunId
    )
    BEGIN
        THROW 50012, N'同步运行日志不存在，请使用 dbo.usp_StoreRetailPriceShadow_Prepare 创建新的 SyncRunId。', 1;
    END;

    DECLARE @ShadowRowCount BIGINT;
    DECLARE @DuplicateGroupCount BIGINT;
    DECLARE @DuplicateRowCount BIGINT;

    SELECT @ShadowRowCount = COUNT_BIG(*)
    FROM dbo.StoreRetailPrice_Shadow;

    ;WITH DuplicateGroups AS
    (
        SELECT
            StoreCode,
            ProductCode,
            SupplierCode,
            COUNT_BIG(*) AS RowCount
        FROM dbo.StoreRetailPrice_Shadow
        WHERE ISNULL(IsDeleted, 0) = 0
        GROUP BY
            StoreCode,
            ProductCode,
            SupplierCode
        HAVING COUNT_BIG(*) > 1
    )
    SELECT
        @DuplicateGroupCount = COUNT_BIG(*),
        @DuplicateRowCount = ISNULL(SUM(RowCount), 0)
    FROM DuplicateGroups;

    UPDATE dbo.StoreRetailPriceSyncRun
    SET Status = CASE WHEN @DuplicateGroupCount > 0 THEN N'ValidationFailed' ELSE N'Validated' END,
        SourceRowCount = COALESCE(@SourceRowCount, SourceRowCount),
        ShadowRowCount = @ShadowRowCount,
        DuplicateGroupCount = @DuplicateGroupCount,
        DuplicateRowCount = @DuplicateRowCount,
        ValidationCompletedAt = SYSUTCDATETIME(),
        CompletedAt = CASE WHEN @DuplicateGroupCount > 0 THEN SYSUTCDATETIME() ELSE CompletedAt END,
        ErrorMessage = CASE
            WHEN @DuplicateGroupCount > 0
                THEN N'影子表存在重复业务键（StoreCode + ProductCode + SupplierCode），禁止切换。'
            ELSE NULL
        END,
        Notes = CASE
            WHEN @DuplicateGroupCount > 0
                THEN N'业务键校验失败，仅影子表本次同步失败，正式表 StoreRetailPrice 保持不变。'
            ELSE N'影子表业务键校验通过，可进入短事务切换阶段。'
        END
    WHERE SyncRunId = @SyncRunId;

    IF @DuplicateGroupCount > 0
    BEGIN
        ;WITH DuplicateGroups AS
        (
            SELECT
                StoreCode,
                ProductCode,
                SupplierCode,
                COUNT_BIG(*) AS RowCount
            FROM dbo.StoreRetailPrice_Shadow
            WHERE ISNULL(IsDeleted, 0) = 0
            GROUP BY
                StoreCode,
                ProductCode,
                SupplierCode
            HAVING COUNT_BIG(*) > 1
        )
        SELECT TOP (@PreviewRows)
            StoreCode,
            ProductCode,
            SupplierCode,
            RowCount
        FROM DuplicateGroups
        ORDER BY RowCount DESC, StoreCode, ProductCode, SupplierCode;

        THROW 50005, N'影子表业务键校验失败，请清理重复数据后再执行切换。', 1;
    END;
END;
GO

-- =====================================================
-- 4. 短事务切换
-- 说明:
-- - 全量装载与校验均在事务外完成
-- - 事务内只做 applock + 表重命名，尽量缩短锁持有时间
-- - 切换成功后保留 StoreRetailPrice_Backup_* 作为回滚入口
-- =====================================================
CREATE OR ALTER PROCEDURE dbo.usp_StoreRetailPriceShadow_Swap
    @SyncRunId BIGINT,
    @LockTimeoutMs INT = 10000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.StoreRetailPrice', N'U') IS NULL
    BEGIN
        THROW 50006, N'正式表 dbo.StoreRetailPrice 不存在，无法执行切换。', 1;
    END;

    IF OBJECT_ID(N'dbo.StoreRetailPrice_Shadow', N'U') IS NULL
    BEGIN
        THROW 50007, N'影子表 dbo.StoreRetailPrice_Shadow 不存在，无法执行切换。', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.StoreRetailPriceSyncRun
        WHERE SyncRunId = @SyncRunId
          AND Status = N'Validated'
    )
    BEGIN
        THROW 50008, N'当前同步批次尚未通过校验，禁止执行切换。', 1;
    END;

    DECLARE @AppLockResult INT;
    DECLARE @BackupTableName SYSNAME =
        N'StoreRetailPrice_Backup_'
        + CONVERT(NVARCHAR(8), GETUTCDATE(), 112)
        + N'_'
        + CAST(@SyncRunId AS NVARCHAR(20));

    IF OBJECT_ID(N'dbo.' + @BackupTableName, N'U') IS NOT NULL
    BEGIN
        THROW 50009, N'备份表名称已存在，请检查 SyncRunId 是否重复使用。', 1;
    END;

    UPDATE dbo.StoreRetailPriceSyncRun
    SET Status = N'Swapping',
        SwapStartedAt = SYSUTCDATETIME(),
        BackupTableName = @BackupTableName,
        Notes = N'准备进入短事务切换。事务内只包含 applock 与表重命名。'
    WHERE SyncRunId = @SyncRunId;

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC @AppLockResult = sys.sp_getapplock
            @Resource = N'StoreRetailPrice_HQ_FULL_SYNC_SWAP',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = @LockTimeoutMs;

        IF @AppLockResult < 0
        BEGIN
            THROW 50010, N'获取 StoreRetailPrice 切换应用锁失败，已取消本次切换。', 1;
        END;

        -- 先把正式表改名为备份表，再把影子表切换为正式表。
        EXEC sys.sp_rename N'dbo.StoreRetailPrice', @BackupTableName;
        EXEC sys.sp_rename N'dbo.StoreRetailPrice_Shadow', N'StoreRetailPrice';

        COMMIT TRANSACTION;

        UPDATE dbo.StoreRetailPriceSyncRun
        SET Status = N'Swapped',
            SwapCompletedAt = SYSUTCDATETIME(),
            CompletedAt = SYSUTCDATETIME(),
            Notes = N'影子表已切换为正式表，原正式表已保留为 StoreRetailPrice_Backup_*。'
        WHERE SyncRunId = @SyncRunId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        UPDATE dbo.StoreRetailPriceSyncRun
        SET Status = N'SwapFailed',
            CompletedAt = SYSUTCDATETIME(),
            ErrorMessage = ERROR_MESSAGE(),
            Notes = N'切换失败时事务已回滚，正式表 StoreRetailPrice 继续提供服务。'
        WHERE SyncRunId = @SyncRunId;

        THROW;
    END CATCH;
END;
GO

-- =====================================================
-- 5. 建议执行顺序
-- =====================================================
/*
DECLARE @SyncRunId BIGINT;

-- 步骤 1: 准备影子表
EXEC dbo.usp_StoreRetailPriceShadow_Prepare
    @SyncRunId = @SyncRunId OUTPUT,
    @TriggeredBy = N'HQ_FULL_SYNC';

SELECT @SyncRunId AS SyncRunId;

-- 步骤 2: 把 HQ 全量数据导入 dbo.StoreRetailPrice_Shadow
-- 说明:
-- - 建议使用 BULK INSERT、SSIS、bcp 或应用侧分批写入
-- - 如果这一步失败，仅影响影子表，本次同步可直接重做

-- 步骤 3: 校验影子表
EXEC dbo.usp_StoreRetailPriceShadow_Validate
    @SyncRunId = @SyncRunId,
    @SourceRowCount = NULL;

-- 步骤 4: 短事务切换
EXEC dbo.usp_StoreRetailPriceShadow_Swap
    @SyncRunId = @SyncRunId,
    @LockTimeoutMs = 10000;

-- 步骤 5: 查看同步日志
SELECT TOP (20) *
FROM dbo.StoreRetailPriceSyncRun
ORDER BY SyncRunId DESC;
*/
GO

-- =====================================================
-- 6. StoreRetailPrice_Backup_* 回滚示例
-- 说明:
-- - 仅在切换成功后发现问题时使用
-- - 回滚同样只把 applock + 表重命名放进短事务
-- - 下面示例中的备份表名请替换为日志表里实际生成的名称
-- =====================================================
/*
DECLARE @BackupTableName SYSNAME = N'StoreRetailPrice_Backup_20260531_10001';
DECLARE @RollbackLiveName SYSNAME = N'StoreRetailPrice_RollbackFailed_20260531_10001';
DECLARE @Sql NVARCHAR(MAX);
DECLARE @AppLockResult INT;

BEGIN TRY
    BEGIN TRANSACTION;

    EXEC @AppLockResult = sys.sp_getapplock
        @Resource = N'StoreRetailPrice_HQ_FULL_SYNC_SWAP',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 10000;

    IF @AppLockResult < 0
    BEGIN
        THROW 50011, N'获取回滚应用锁失败，已取消回滚。', 1;
    END;

    EXEC sys.sp_rename N'dbo.StoreRetailPrice', @RollbackLiveName;

    SET @Sql = N'EXEC sys.sp_rename N''dbo.' + @BackupTableName + N''', N''StoreRetailPrice'';';
    EXEC sys.sp_executesql @Sql;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
*/
GO
