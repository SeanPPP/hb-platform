using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    public static class StartupSchemaMigrator
    {
        public static Task EnsurePosmAsync(ISqlSugarClient posmDb, ILogger logger) =>
            InstallmentOrderSchemaMigrator.EnsureAsync(posmDb, logger);

        public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
        {
            // 即使通用迁移仅在 SQL Server 执行，也必须先验证 Preorder 的 Provider 契约，禁止 early return 静默放行未知数据库。
            PreorderSchemaBootstrap.EnsureSupportedProvider(db.CurrentConnectionConfig.DbType);
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            // 关键位置：统一串起所有启动兜底迁移，避免 Program.cs 只接入其中一条补列链路。
            await LocalSupplierInvoiceStartupSchemaMigrator.EnsureAsync(db, logger);
            await EnsureCashRegisterUsersSchemaAsync(db, logger);
            await EnsureMobileAppBuildSchemaAsync(db, logger);
            await EnsureMobileAppDeviceStatusSchemaAsync(db, logger);
            await EnsureAttendanceLocationSchemaAsync(db, logger);
            await EnsureAttendanceQrSchemaAsync(db, logger);
            await EnsureServiceApiTokenSchemaAsync(db, logger);
            await EnsureWpfAppReleaseSchemaAsync(db, logger);
            await EnsureWarehouseOrderCartOwnerSchemaAsync(db, logger);
            await EnsureEmployeeProfileImageSchemaAsync(db, logger);
            await EnsureEmployeeProfileSensitiveChangeSchemaAsync(db, logger);
            await EnsureUserStorePosPermissionSchemaAsync(db, logger);
            await EnsurePreorderSchemaAsync(db, logger);
        }

        private static async Task EnsureEmployeeProfileSensitiveChangeSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[EmployeeProfile]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.EmployeeProfile', 'SensitiveRevision') IS NULL
        ALTER TABLE [dbo].[EmployeeProfile] ADD [SensitiveRevision] int NOT NULL CONSTRAINT [DF_EmployeeProfile_SensitiveRevision] DEFAULT(0);
    IF COL_LENGTH('dbo.EmployeeProfile', 'IdentityType') IS NULL
        ALTER TABLE [dbo].[EmployeeProfile] ADD [IdentityType] nvarchar(50) NULL;
END;
IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeProfileSensitiveChangeRequest] (
        [RequestId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EmployeeProfileSensitiveChangeRequest] PRIMARY KEY,
        [UserGUID] nvarchar(50) NOT NULL,
        [BankBsb] nvarchar(20) NULL,
        [BankAccountNumber] nvarchar(50) NULL,
        [SuperannuationCompanyName] nvarchar(200) NULL,
        [SuperannuationCompanyCode] nvarchar(100) NULL,
        [SuperannuationAccountNumber] nvarchar(100) NULL,
        [IdentityType] nvarchar(50) NULL,
        [IdentityId] nvarchar(100) NULL,
        [IdentityPhotoObjectKey] nvarchar(500) NULL,
        [RemoveIdentityPhoto] bit NOT NULL CONSTRAINT [DF_EmployeeProfileSensitiveChangeRequest_RemoveIdentityPhoto] DEFAULT(0),
        [ChangedFieldsJson] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [BaseSensitiveRevision] int NOT NULL,
        [SubmittedAt] datetime2 NOT NULL,
        [SubmittedBy] nvarchar(100) NULL,
        [ReviewedAt] datetime2 NULL,
        [ReviewedBy] nvarchar(100) NULL,
        [ReviewReason] nvarchar(1000) NULL,
        [SupersededAt] datetime2 NULL,
        [SupersededBy] nvarchar(100) NULL
    );
END;
IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeProfileSensitiveChangeRequest', 'RemoveIdentityPhoto') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeProfileSensitiveChangeRequest]
        ADD [RemoveIdentityPhoto] bit NOT NULL
            CONSTRAINT [DF_EmployeeProfileSensitiveChangeRequest_RemoveIdentityPhoto] DEFAULT(0) WITH VALUES;
END;
IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeProfileSensitiveChangeRequest', 'ChangedFieldsJson') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeProfileSensitiveChangeRequest]
        ADD [ChangedFieldsJson] nvarchar(1000) NULL;
END;
IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE [name] = 'UX_EmployeeProfileSensitiveChangeRequest_User_Pending'
         AND [object_id] = OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]')
   )
BEGIN
    -- Pending=0；过滤唯一索引是并发请求下“每人最多一条待审”的数据库最终约束。
    CREATE UNIQUE INDEX [UX_EmployeeProfileSensitiveChangeRequest_User_Pending]
        ON [dbo].[EmployeeProfileSensitiveChangeRequest]([UserGUID])
        WHERE [Status] = 0;
END;
IF OBJECT_ID(N'[dbo].[EmployeeImageUploadTickets]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeImageUploadTickets', 'SensitiveChangeRequestId') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [SensitiveChangeRequestId] int NULL;
END;
""";

            try
            {
                await db.Ado.ExecuteCommandAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "初始化员工敏感资料审批结构失败");
                throw;
            }
        }

        private static async Task EnsureAttendanceQrSchemaAsync(ISqlSugarClient db, ILogger logger)
        {
            const string sql = """
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @AttendanceQrSchemaLockResult int;
    EXEC @AttendanceQrSchemaLockResult = sys.sp_getapplock
        @Resource = N'AttendancePosQrKey_Schema_Initialization',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 30000;

    IF @AttendanceQrSchemaLockResult < 0
    BEGIN
        THROW 51010, N'无法获取考勤二维码结构初始化锁。', 1;
    END;

IF OBJECT_ID(N'[dbo].[AttendancePosQrKey]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendancePosQrKey] (
        [Kid] nvarchar(64) NOT NULL CONSTRAINT [PK_AttendancePosQrKey] PRIMARY KEY,
        [Algorithm] nvarchar(20) NOT NULL,
        [ProtectedKey] nvarchar(max) NOT NULL,
        [StoreCode] nvarchar(50) NOT NULL,
        [DeviceCode] nvarchar(50) NOT NULL,
        [HardwareId] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RegisteredAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL
    );
END;

IF COL_LENGTH('dbo.AttendancePunch', 'QrTokenId') IS NULL
    ALTER TABLE [dbo].[AttendancePunch] ADD [QrTokenId] nvarchar(50) NULL;
IF COL_LENGTH('dbo.AttendancePunch', 'PosDeviceCode') IS NULL
    ALTER TABLE [dbo].[AttendancePunch] ADD [PosDeviceCode] nvarchar(50) NULL;
IF COL_LENGTH('dbo.AttendancePunch', 'SigningKeyId') IS NULL
    ALTER TABLE [dbo].[AttendancePunch] ADD [SigningKeyId] nvarchar(64) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_AttendancePosQrKey_ActiveDevice'
      AND [object_id] = OBJECT_ID(N'[dbo].[AttendancePosQrKey]')
)
BEGIN
    ;WITH [ActiveDeviceRows] AS (
        SELECT
            [Kid],
            ROW_NUMBER() OVER (
                PARTITION BY [DeviceCode]
                ORDER BY [RegisteredAtUtc] DESC, [Kid] DESC
            ) AS [RowNumber]
        FROM [dbo].[AttendancePosQrKey] WITH (TABLOCKX, HOLDLOCK)
        WHERE [Status] = N'Active'
    )
    UPDATE [dbo].[AttendancePosQrKey]
    SET [Status] = N'Revoked', [RevokedAtUtc] = SYSUTCDATETIME()
    WHERE [Kid] IN (
        SELECT [Kid] FROM [ActiveDeviceRows] WHERE [RowNumber] > 1
    );

    CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveDevice]
    ON [dbo].[AttendancePosQrKey]([DeviceCode])
    WHERE [Status] = N'Active';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_AttendancePosQrKey_ActiveHardware'
      AND [object_id] = OBJECT_ID(N'[dbo].[AttendancePosQrKey]')
)
BEGIN
    ;WITH [ActiveHardwareRows] AS (
        SELECT
            [Kid],
            ROW_NUMBER() OVER (
                PARTITION BY [HardwareId]
                ORDER BY [RegisteredAtUtc] DESC, [Kid] DESC
            ) AS [RowNumber]
        FROM [dbo].[AttendancePosQrKey] WITH (TABLOCKX, HOLDLOCK)
        WHERE [Status] = N'Active'
    )
    UPDATE [dbo].[AttendancePosQrKey]
    SET [Status] = N'Revoked', [RevokedAtUtc] = SYSUTCDATETIME()
    WHERE [Kid] IN (
        SELECT [Kid] FROM [ActiveHardwareRows] WHERE [RowNumber] > 1
    );

    CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveHardware]
    ON [dbo].[AttendancePosQrKey]([HardwareId])
    WHERE [Status] = N'Active';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_AttendancePunch_User_QrTokenId'
      AND [object_id] = OBJECT_ID(N'[dbo].[AttendancePunch]')
)
BEGIN
    -- 关键逻辑：QrTokenId 可能在本事务前段刚新增，动态 SQL 会在列存在后才编译索引语句。
    EXEC(N'CREATE UNIQUE INDEX [UX_AttendancePunch_User_QrTokenId]
    ON [dbo].[AttendancePunch]([UserGuid], [QrTokenId])
    WHERE [QrTokenId] IS NOT NULL;');
END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
""";

            // 关键逻辑：只追加表、列和过滤唯一索引，旧打卡记录保持 NULL，不参与幂等约束。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("考勤二维码密钥与打卡审计结构检查完成");
        }

        private static async Task EnsureUserStorePosPermissionSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[HBwebSysUserStorePosPermissions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HBwebSysUserStorePosPermissions] (
        [Id] nvarchar(50) NOT NULL CONSTRAINT [PK_HBwebSysUserStorePosPermissions] PRIMARY KEY,
        [UserGuid] nvarchar(50) NOT NULL,
        [StoreGuid] nvarchar(50) NOT NULL,
        [PermissionCode] nvarchar(100) NOT NULL,
        [IsGranted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL CONSTRAINT [DF_HBwebSysUserStorePosPermissions_IsDeleted] DEFAULT(0)
    );
END;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = 'IX_UserStorePosPermission_Scope_Unique'
      AND [object_id] = OBJECT_ID(N'[dbo].[HBwebSysUserStorePosPermissions]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserStorePosPermission_Scope_Unique]
        ON [dbo].[HBwebSysUserStorePosPermissions]([UserGuid], [StoreGuid], [PermissionCode]);
END;
""";

            try
            {
                // 权限覆盖表和唯一索引必须同批兜底，保证 PUT 快照可安全重试。
                await db.Ado.ExecuteCommandAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "初始化用户分店 POS 权限覆盖表失败");
                throw;
            }
        }

        private static async Task EnsureEmployeeProfileImageSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[EmployeeProfile]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeProfile', 'IdentityPhotoObjectKey') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeProfile]
    ADD [IdentityPhotoObjectKey] nvarchar(500) NULL;
END
IF OBJECT_ID(N'[dbo].[EmployeeImageUploadTickets]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeImageUploadTickets] (
        [PendingObjectKey] nvarchar(500) NOT NULL CONSTRAINT [PK_EmployeeImageUploadTickets] PRIMARY KEY,
        [UserGUID] nvarchar(50) NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileSize] bigint NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [Status] int NOT NULL CONSTRAINT [DF_EmployeeImageUploadTickets_Status] DEFAULT(0),
        [FinalObjectKey] nvarchar(500) NULL,
        [ProcessingStartedAt] datetime2 NULL,
        [PromotedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [FailedAt] datetime2 NULL,
        [StageChangedAt] datetime2 NULL,
        [PreviousObjectKey] nvarchar(500) NULL,
        [PreviousObjectCleanupStatus] int NOT NULL CONSTRAINT [DF_EmployeeImageUploadTickets_PreviousCleanup] DEFAULT(0),
        [PreviousObjectCleanupStartedAt] datetime2 NULL,
        [SensitiveChangeRequestId] int NULL
    );
    CREATE INDEX [IX_EmployeeImageUploadTickets_Expiry]
        ON [dbo].[EmployeeImageUploadTickets]([Status], [ExpiresAt]);
END
IF OBJECT_ID(N'[dbo].[EmployeeImageUploadTickets]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'Status') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [Status] int NOT NULL CONSTRAINT [DF_EmployeeImageUploadTickets_Status] DEFAULT(0);
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'FinalObjectKey') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [FinalObjectKey] nvarchar(500) NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'ProcessingStartedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [ProcessingStartedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'PromotedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [PromotedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'CompletedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [CompletedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'FailedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [FailedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'StageChangedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [StageChangedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'PreviousObjectKey') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [PreviousObjectKey] nvarchar(500) NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'PreviousObjectCleanupStatus') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [PreviousObjectCleanupStatus] int NOT NULL CONSTRAINT [DF_EmployeeImageUploadTickets_PreviousCleanup] DEFAULT(0);
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'PreviousObjectCleanupStartedAt') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [PreviousObjectCleanupStartedAt] datetime2 NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'SensitiveChangeRequestId') IS NULL
        ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [SensitiveChangeRequestId] int NULL;
    IF COL_LENGTH('dbo.EmployeeImageUploadTickets', 'Completed') IS NOT NULL
        EXEC(N'UPDATE [dbo].[EmployeeImageUploadTickets] SET [Status] = 3, [CompletedAt] = COALESCE([CompletedAt], [ExpiresAt]) WHERE [Completed] = 1 AND [Status] = 0');
    UPDATE [dbo].[EmployeeImageUploadTickets]
    SET [StageChangedAt] = COALESCE([StageChangedAt], [ProcessingStartedAt], [PromotedAt], [CompletedAt], [CreatedAt])
    WHERE [StageChangedAt] IS NULL;
    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = 'IX_EmployeeImageUploadTickets_Expiry'
          AND [object_id] = OBJECT_ID(N'[dbo].[EmployeeImageUploadTickets]')
    )
        DROP INDEX [IX_EmployeeImageUploadTickets_Expiry] ON [dbo].[EmployeeImageUploadTickets];
    CREATE INDEX [IX_EmployeeImageUploadTickets_Expiry]
        ON [dbo].[EmployeeImageUploadTickets]([Status], [ExpiresAt], [StageChangedAt]);
END
""";
            try
            {
                // 关键逻辑：仅补 nullable 列，不迁移历史公开证件照，确保启动迁移可重复执行。
                await db.Ado.ExecuteCommandAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "补充员工私有证件照对象键字段失败");
                throw;
            }
        }

        private static async Task EnsureWarehouseOrderCartOwnerSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string ensureColumnSql =
                @"
	IF OBJECT_ID('WareHouseOrder', 'U') IS NOT NULL
	BEGIN
	    IF COL_LENGTH('WareHouseOrder', 'CartOwnerUserGuid') IS NULL
	    BEGIN
	        ALTER TABLE [WareHouseOrder]
	        ADD [CartOwnerUserGuid] nvarchar(50) NULL;
	    END;

	    IF COL_LENGTH('WareHouseOrder', 'CartOwnerUserGuid') IS NULL
	    BEGIN
	        THROW 51000, 'WareHouseOrder.CartOwnerUserGuid migration failed', 1;
	    END;
	END;";

            // 关键逻辑：CartOwnerUserGuid 是运行时必需列，补列失败必须阻断启动，不能让接口带着缺列运行。
            await db.Ado.ExecuteCommandAsync(ensureColumnSql);

            const string ensureIndexSql =
                @"
	IF OBJECT_ID('WareHouseOrder', 'U') IS NOT NULL
	BEGIN
	    IF COL_LENGTH('WareHouseOrder', 'CartOwnerUserGuid') IS NOT NULL
	       AND NOT EXISTS (
	           SELECT *
	           FROM sys.indexes
	           WHERE name = 'IX_WareHouseOrder_CartScope'
	             AND object_id = OBJECT_ID('WareHouseOrder')
	       )
	    BEGIN
	        -- 关键逻辑：活动购物车按分店 + owner 隔离；NULL owner 保留原分店购物车语义。
	        CREATE NONCLUSTERED INDEX [IX_WareHouseOrder_CartScope]
	        ON [WareHouseOrder] ([StoreCode], [FlowStatus], [IsDeleted], [CartOwnerUserGuid]);
	    END;
	END;";

            try
            {
                await db.Ado.ExecuteCommandAsync(ensureIndexSql);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "仓库订货购物车归属索引迁移失败");
            }
        }

        private static async Task EnsureCashRegisterUsersSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string ensureEmployeeCashierTablesSql = @"
IF OBJECT_ID('EmployeeCashierBarcodes', 'U') IS NULL
BEGIN
    CREATE TABLE [EmployeeCashierBarcodes] (
        [HGUID] nvarchar(50) NOT NULL CONSTRAINT [PK_EmployeeCashierBarcodes] PRIMARY KEY,
        [UserGUID] nvarchar(50) NOT NULL,
        [Barcode] nvarchar(13) NOT NULL,
        [PrintCount] int NOT NULL CONSTRAINT [DF_EmployeeCashierBarcodes_PrintCount] DEFAULT(0),
        [Status] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(100) NULL
    );
    CREATE UNIQUE INDEX [IX_EmployeeCashierBarcodes_UserGUID_Active]
        ON [EmployeeCashierBarcodes]([UserGUID]) WHERE [Status] = 1;
    CREATE UNIQUE INDEX [IX_EmployeeCashierBarcodes_Barcode]
        ON [EmployeeCashierBarcodes]([Barcode]);
END;
IF OBJECT_ID('EmployeeCashierBarcodePrintAttempts', 'U') IS NULL
BEGIN
    CREATE TABLE [EmployeeCashierBarcodePrintAttempts] (
        [PrintAttemptId] uniqueidentifier NOT NULL CONSTRAINT [PK_EmployeeCashierBarcodePrintAttempts] PRIMARY KEY,
        [BarcodeHGUID] nvarchar(50) NOT NULL,
        [UserGUID] nvarchar(50) NOT NULL,
        [Barcode] nvarchar(13) NOT NULL,
        [CreatedAt] datetime2 NOT NULL
    );
END;";
            await db.Ado.ExecuteCommandAsync(ensureEmployeeCashierTablesSql);

            const string ensureReservationTableSql = @"
IF OBJECT_ID('CashierBarcodeReservations', 'U') IS NULL
BEGIN
    CREATE TABLE [CashierBarcodeReservations] (
        [Barcode] nvarchar(50) NOT NULL CONSTRAINT [PK_CashierBarcodeReservations] PRIMARY KEY,
        [CreatedAt] datetime2 NOT NULL,
        [OwnerType] nvarchar(20) NULL,
        [OwnerId] nvarchar(50) NULL
    );
END;
IF COL_LENGTH('CashierBarcodeReservations', 'OwnerType') IS NULL
    ALTER TABLE [CashierBarcodeReservations] ADD [OwnerType] nvarchar(20) NULL;
IF COL_LENGTH('CashierBarcodeReservations', 'OwnerId') IS NULL
    ALTER TABLE [CashierBarcodeReservations] ADD [OwnerId] nvarchar(50) NULL;

IF OBJECT_ID('CashRegisterUsers', 'U') IS NOT NULL
   AND COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [CashRegisterUsers]
        WHERE NULLIF(LTRIM(RTRIM([UserBarcode])), '') IS NOT NULL
        GROUP BY LTRIM(RTRIM([UserBarcode]))
        HAVING COUNT(DISTINCT ISNULL([HGUID], '')) > 1
    )
        THROW 51001, '历史 legacy 收银条码存在跨 owner 冲突，已阻断迁移', 1;

    IF OBJECT_ID('EmployeeCashierBarcodes', 'U') IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM [CashRegisterUsers] AS [legacy]
           INNER JOIN [EmployeeCashierBarcodes] AS [employee]
               ON [employee].[Barcode] = LTRIM(RTRIM([legacy].[UserBarcode]))
           WHERE NULLIF(LTRIM(RTRIM([legacy].[UserBarcode])), '') IS NOT NULL
       )
        THROW 51002, 'legacy 与 employee 收银条码存在跨表冲突，已阻断迁移', 1;

    IF EXISTS (
        SELECT 1
        FROM [CashRegisterUsers] AS [legacy]
        INNER JOIN [CashierBarcodeReservations] AS [reservation]
            ON [reservation].[Barcode] = LTRIM(RTRIM([legacy].[UserBarcode]))
        WHERE NULLIF(LTRIM(RTRIM([legacy].[UserBarcode])), '') IS NOT NULL
          AND (
              ISNULL([reservation].[OwnerType], '') <> 'legacy'
              OR (NULLIF([reservation].[OwnerId], '') IS NOT NULL
                  AND [reservation].[OwnerId] <> [legacy].[HGUID])
          )
    )
        THROW 51003, 'legacy 收银条码与已有占用 owner 冲突，已阻断迁移', 1;

    ;WITH [HistoricalBarcodes] AS (
        SELECT DISTINCT LTRIM(RTRIM([UserBarcode])) AS [Barcode], [HGUID] AS [OwnerId]
        FROM [CashRegisterUsers]
        WHERE NULLIF(LTRIM(RTRIM([UserBarcode])), '') IS NOT NULL
    )
    INSERT INTO [CashierBarcodeReservations] ([Barcode], [CreatedAt], [OwnerType], [OwnerId])
    SELECT [history].[Barcode], SYSUTCDATETIME(), 'legacy', [history].[OwnerId]
    FROM [HistoricalBarcodes] AS [history]
    WHERE NOT EXISTS (
        SELECT 1
        FROM [CashierBarcodeReservations] AS [reservation]
        WHERE [reservation].[Barcode] = [history].[Barcode]
    );
END;";
            // 关键逻辑：先回填独立占用表，历史表即使已有重复也不会影响后续新条码的永久唯一性。
            await db.Ado.ExecuteCommandAsync(ensureReservationTableSql);
            await db.Ado.ExecuteCommandAsync(@"
IF OBJECT_ID('EmployeeCashierBarcodes', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [EmployeeCashierBarcodes] AS [employee]
        INNER JOIN [CashierBarcodeReservations] AS [reservation]
            ON [reservation].[Barcode] = [employee].[Barcode]
        WHERE ISNULL([reservation].[OwnerType], '') <> 'employee'
           OR (NULLIF([reservation].[OwnerId], '') IS NOT NULL
               AND [reservation].[OwnerId] <> [employee].[HGUID])
    )
        THROW 51004, 'employee 收银条码与已有占用 owner 冲突，已阻断迁移', 1;

    INSERT INTO [CashierBarcodeReservations] ([Barcode], [CreatedAt], [OwnerType], [OwnerId])
    SELECT [employee].[Barcode], MIN([employee].[CreatedAt]), 'employee', MIN([employee].[HGUID])
    FROM [EmployeeCashierBarcodes] AS [employee]
    WHERE NOT EXISTS (
        SELECT 1 FROM [CashierBarcodeReservations] AS [reservation]
        WHERE [reservation].[Barcode] = [employee].[Barcode]
    )
    GROUP BY [employee].[Barcode];
END;");

            const string addUserGuidColumnSql =
                @"
IF OBJECT_ID('CashRegisterUsers', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('CashRegisterUsers', 'UserGUID') IS NULL
    BEGIN
        ALTER TABLE [CashRegisterUsers]
        ADD [UserGUID] nvarchar(50) NULL;
    END;
END;";

            // 关键位置：SQL Server 同一 batch 中 ADD 后再静态引用新列会先编译失败，补列必须先单独执行。
            await db.Ado.ExecuteCommandAsync(addUserGuidColumnSql);

            const string sql =
                @"
IF OBJECT_ID('CashRegisterUsers', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('CashRegisterUsers', 'UserGUID') IS NOT NULL
    BEGIN
        UPDATE [CashRegisterUsers]
        SET [UserGUID] = LTRIM(RTRIM([UserGUID]))
        WHERE [UserGUID] IS NOT NULL
          AND [UserGUID] <> LTRIM(RTRIM([UserGUID]));

        UPDATE [CashRegisterUsers]
        SET [UserGUID] = NULL
        WHERE [UserGUID] IS NOT NULL
          AND LTRIM(RTRIM([UserGUID])) = '';

        IF COL_LENGTH('CashRegisterUsers', 'OperatorUser') IS NOT NULL
           AND OBJECT_ID('User', 'U') IS NOT NULL
        BEGIN
            -- 关键位置：老数据若 OperatorUser 已经保存后台 UserGUID，则自动回填；姓名/旧编号不猜测，避免错误授权。
            UPDATE [cashier]
            SET [UserGUID] = [linkedUser].[UserGUID]
            FROM [CashRegisterUsers] AS [cashier]
            INNER JOIN [User] AS [linkedUser]
                ON [linkedUser].[UserGUID] = LTRIM(RTRIM([cashier].[OperatorUser]))
            WHERE NULLIF(LTRIM(RTRIM([cashier].[UserGUID])), '') IS NULL
              AND NULLIF(LTRIM(RTRIM([cashier].[OperatorUser])), '') IS NOT NULL
              AND [linkedUser].[IsActive] = 1
              AND ISNULL([linkedUser].[IsDeleted], 0) = 0;
        END;

        IF COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
        BEGIN
            UPDATE [CashRegisterUsers]
            SET [UserBarcode] = LTRIM(RTRIM([UserBarcode]))
            WHERE [UserBarcode] IS NOT NULL
              AND [UserBarcode] <> LTRIM(RTRIM([UserBarcode]));

            IF COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
            BEGIN
                -- 关键位置：空白条码不能继续保持有效，否则空输入可能命中历史脏数据；只失效记录以兼容旧表非空约束。
                UPDATE [CashRegisterUsers]
                SET [Status] = 0
                WHERE [Status] = 1
                  AND ([UserBarcode] IS NULL OR LTRIM(RTRIM([UserBarcode])) = '');
            END;
        END;

        IF COL_LENGTH('CashRegisterUsers', 'Id') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'LastModifyDate') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'CreateDate') IS NOT NULL
        BEGIN
            -- 关键位置：旧库可能缺少排序/状态列，只有列齐全时才做去重，避免启动迁移被极旧表结构阻断。
            ;WITH [CashRegisterUserActiveDuplicates] AS (
                SELECT
                    [Id],
                    ROW_NUMBER() OVER (PARTITION BY [UserGUID]
                        ORDER BY
                            [LastModifyDate] DESC,
                            [CreateDate] DESC,
                            [Id] DESC
                    ) AS [RowNumber]
                FROM [CashRegisterUsers]
                WHERE [Status] = 1
                  AND [UserGUID] IS NOT NULL
            )
            UPDATE [CashRegisterUsers]
            SET [Status] = 0
            WHERE [Id] IN (
                SELECT [Id]
                FROM [CashRegisterUserActiveDuplicates]
                WHERE [RowNumber] > 1
            );
        END;

        IF COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'Id') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'LastModifyDate') IS NOT NULL
           AND COL_LENGTH('CashRegisterUsers', 'CreateDate') IS NOT NULL
        BEGIN
            ;WITH [CashRegisterBarcodeActiveDuplicates] AS (
                SELECT
                    [Id],
                    ROW_NUMBER() OVER (PARTITION BY [UserBarcode]
                        ORDER BY
                            [LastModifyDate] DESC,
                            [CreateDate] DESC,
                            [Id] DESC
                    ) AS [RowNumber]
                FROM [CashRegisterUsers]
                WHERE [Status] = 1
                  AND NULLIF(LTRIM(RTRIM([UserBarcode])), '') IS NOT NULL
            )
            UPDATE [CashRegisterUsers]
            SET [Status] = 0
            WHERE [Id] IN (
                SELECT [Id]
                FROM [CashRegisterBarcodeActiveDuplicates]
                WHERE [RowNumber] > 1
            );
        END;
    END;

    IF OBJECT_ID('EmployeeCashierBarcodes', 'U') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM [CashRegisterUsers] AS [legacy]
           INNER JOIN [EmployeeCashierBarcodes] AS [employee]
               ON [legacy].[UserGUID] = [employee].[UserGUID]
           WHERE [legacy].[Status] = 1
             AND [employee].[Status] = 1
             AND NULLIF(LTRIM(RTRIM([legacy].[UserGUID])), '') IS NOT NULL
       )
        THROW 51005, '同一用户在 legacy 与 employee 表均有有效条码，已阻断迁移', 1;

    IF NOT EXISTS (
        SELECT *
        FROM sys.indexes
        WHERE name = 'IX_CashRegisterUsers_UserGUID_Active'
          AND object_id = OBJECT_ID('CashRegisterUsers')
    )
       AND COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'Id') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'LastModifyDate') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'CreateDate') IS NOT NULL
    BEGIN
        CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserGUID_Active]
        ON [CashRegisterUsers]([UserGUID])
        WHERE [Status] = 1 AND [UserGUID] IS NOT NULL;
    END;

    IF COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'Id') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'LastModifyDate') IS NOT NULL
       AND COL_LENGTH('CashRegisterUsers', 'CreateDate') IS NOT NULL
       AND NOT EXISTS (
            SELECT *
            FROM sys.indexes
            WHERE name = 'IX_CashRegisterUsers_UserBarcode_Active'
              AND object_id = OBJECT_ID('CashRegisterUsers')
        )
    BEGIN
        CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserBarcode_Active]
        ON [CashRegisterUsers]([UserBarcode])
        WHERE [Status] = 1 AND [UserBarcode] IS NOT NULL AND [UserBarcode] <> '';
    END;

    IF COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
       AND NOT EXISTS (
            SELECT * FROM sys.indexes
            WHERE name = 'IX_CashRegisterUsers_UserBarcode_AllHistory'
              AND object_id = OBJECT_ID('CashRegisterUsers')
       )
       AND NOT EXISTS (
            SELECT [UserBarcode]
            FROM [CashRegisterUsers]
            WHERE [UserBarcode] IS NOT NULL AND [UserBarcode] <> ''
            GROUP BY [UserBarcode]
            HAVING COUNT(*) > 1
       )
    BEGIN
        -- 历史条码禁止复用；旧库若已有重复则保留审计数据并跳过建索引，等待人工清理。
        CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserBarcode_AllHistory]
        ON [CashRegisterUsers]([UserBarcode])
        WHERE [UserBarcode] IS NOT NULL AND [UserBarcode] <> '';
    END;
END;";

            // 关键逻辑：老表只补 nullable 关联列；新库同时用有效用户索引和全历史条码索引兜底。
            await db.Ado.ExecuteCommandAsync(sql);
            var historicalDuplicateCount = await db.Ado.GetIntAsync(
                @"
IF OBJECT_ID('CashRegisterUsers', 'U') IS NOT NULL
   AND COL_LENGTH('CashRegisterUsers', 'UserBarcode') IS NOT NULL
BEGIN
    SELECT COUNT(*) FROM (
        SELECT [UserBarcode]
        FROM [CashRegisterUsers]
        WHERE [UserBarcode] IS NOT NULL AND [UserBarcode] <> ''
        GROUP BY [UserBarcode]
        HAVING COUNT(*) > 1
    ) AS [DuplicateBarcodes];
END
ELSE SELECT 0;"
            );
            if (historicalDuplicateCount > 0)
            {
                logger.LogWarning(
                    "CashRegisterUsers 存在 {DuplicateCount} 组历史重复条码，已保留审计数据并跳过全历史唯一索引",
                    historicalDuplicateCount
                );
            }
            logger.LogInformation("CashRegisterUsers.UserGUID 列与条码唯一索引检查完成");
        }

        private static async Task EnsureMobileAppBuildSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('MobileAppBuild', 'U') IS NULL
BEGIN
    CREATE TABLE [MobileAppBuild] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileAppBuild] PRIMARY KEY,
        [EasBuildId] nvarchar(120) NOT NULL,
        [AccountName] nvarchar(120) NOT NULL,
        [ProjectName] nvarchar(120) NOT NULL,
        [AppName] nvarchar(160) NULL,
        [Platform] nvarchar(30) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [BuildProfile] nvarchar(80) NOT NULL,
        [Distribution] nvarchar(80) NULL,
        [Channel] nvarchar(120) NULL,
        [RuntimeVersion] nvarchar(120) NULL,
        [AppVersion] nvarchar(80) NULL,
        [AppBuildVersion] nvarchar(80) NULL,
        [ArtifactUrl] nvarchar(1000) NOT NULL,
        [CosArtifactUrl] nvarchar(1000) NULL,
        [CosObjectKey] nvarchar(500) NULL,
        [CosMirroredAt] datetime2 NULL,
        [CosMirrorError] nvarchar(1000) NULL,
        [CosMirrorStatus] nvarchar(32) NOT NULL CONSTRAINT [DF_MobileAppBuild_CosMirrorStatus] DEFAULT('pending'),
        [CosMirrorAttempts] int NOT NULL CONSTRAINT [DF_MobileAppBuild_CosMirrorAttempts] DEFAULT(0),
        [CosMirrorLastAttemptAtUtc] datetime2 NULL,
        [BuildDetailsPageUrl] nvarchar(1000) NULL,
        [GitCommitHash] nvarchar(120) NULL,
        [GitCommitMessage] nvarchar(1000) NULL,
        [CompletedAt] datetime2 NULL,
        [ExpirationDate] datetime2 NULL,
        [ReceivedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppBuild_EasBuildId'
      AND object_id = OBJECT_ID('MobileAppBuild')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileAppBuild_EasBuildId]
    ON [MobileAppBuild]([EasBuildId]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppBuild_Profile_CompletedAt'
      AND object_id = OBJECT_ID('MobileAppBuild')
)
BEGIN
    CREATE INDEX [IX_MobileAppBuild_Profile_CompletedAt]
    ON [MobileAppBuild]([BuildProfile], [Platform], [Status], [CompletedAt]);
END;

IF OBJECT_ID('MobileAppOtaUpdate', 'U') IS NULL
BEGIN
    CREATE TABLE [MobileAppOtaUpdate] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileAppOtaUpdate] PRIMARY KEY,
        [UpdateGroupId] nvarchar(120) NOT NULL,
        [AndroidUpdateId] nvarchar(120) NULL,
        [Channel] nvarchar(120) NOT NULL,
        [Branch] nvarchar(120) NULL,
        [Platform] nvarchar(30) NOT NULL,
        [RuntimeVersion] nvarchar(120) NULL,
        [Message] nvarchar(1000) NULL,
        [GitCommitHash] nvarchar(120) NULL,
        [DashboardUrl] nvarchar(1000) NULL,
        [PublishedAt] datetime2 NOT NULL,
        [IsRollback] bit NOT NULL,
        [RollbackOfGroupId] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppOtaUpdate_Group_Platform'
      AND object_id = OBJECT_ID('MobileAppOtaUpdate')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileAppOtaUpdate_Group_Platform]
    ON [MobileAppOtaUpdate]([UpdateGroupId], [Platform]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppOtaUpdate_Channel_Runtime_PublishedAt'
      AND object_id = OBJECT_ID('MobileAppOtaUpdate')
)
BEGIN
    CREATE INDEX [IX_MobileAppOtaUpdate_Channel_Runtime_PublishedAt]
    ON [MobileAppOtaUpdate]([Channel], [RuntimeVersion], [PublishedAt]);
END;

IF OBJECT_ID('MobileAppBuild', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosArtifactUrl] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosObjectKey') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosObjectKey] nvarchar(500) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirroredAt') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirroredAt] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorError') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorError] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorStatus] nvarchar(32) NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorStatus] DEFAULT('pending');
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorAttempts') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorAttempts] int NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorAttempts] DEFAULT(0);
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorLastAttemptAtUtc') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorLastAttemptAtUtc] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NOT NULL
       AND COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE [MobileAppBuild]
            SET [CosMirrorStatus] = ''succeeded''
            WHERE [CosArtifactUrl] IS NOT NULL
              AND LTRIM(RTRIM([CosArtifactUrl])) <> ''''
              AND ([CosMirrorStatus] IS NULL OR [CosMirrorStatus] = ''pending'');
        ');
    END;
END;";

            // 关键位置：移动端自更新依赖构建表、OTA 表和 COS 状态字段，启动时补齐可降低旧库发布风险。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("移动端 APK 构建和 OTA 更新表检查完成");
        }

        private static async Task EnsureMobileAppDeviceStatusSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('MobileAppDeviceStatus', 'U') IS NULL
BEGIN
    CREATE TABLE [MobileAppDeviceStatus] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileAppDeviceStatus] PRIMARY KEY,
        [HardwareId] nvarchar(120) NOT NULL,
        [SystemDeviceNumber] nvarchar(120) NULL,
        [DeviceSystem] nvarchar(30) NULL,
        [Platform] nvarchar(30) NULL,
        [StoreCode] nvarchar(50) NULL,
        [AppVersion] nvarchar(80) NULL,
        [AppBuildVersion] nvarchar(80) NULL,
        [RuntimeVersion] nvarchar(120) NULL,
        [Channel] nvarchar(120) NULL,
        [UpdateId] nvarchar(120) NULL,
        [UpdateSource] nvarchar(40) NULL,
        [LastSeenAtUtc] datetime2 NOT NULL,
        [LastAuthMode] nvarchar(30) NOT NULL CONSTRAINT [DF_MobileAppDeviceStatus_LastAuthMode] DEFAULT('unknown'),
        [LastSeenUserGuid] nvarchar(50) NULL,
        [LastSeenUsername] nvarchar(100) NULL,
        [LastSeenUserFullName] nvarchar(160) NULL,
        [RegisteredDeviceId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('MobileAppDeviceStatus', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('MobileAppDeviceStatus', 'HardwareId') IS NULL
    BEGIN
        ALTER TABLE [MobileAppDeviceStatus] ADD [HardwareId] nvarchar(120) NULL;
    END;

    IF COL_LENGTH('MobileAppDeviceStatus', 'HardwareId') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE [MobileAppDeviceStatus]
            SET [HardwareId] = CONCAT(''legacy-'', CONVERT(nvarchar(36), [Id]))
            WHERE [HardwareId] IS NULL
               OR LTRIM(RTRIM([HardwareId])) = '''';
        ');
        ALTER TABLE [MobileAppDeviceStatus] ALTER COLUMN [HardwareId] nvarchar(120) NOT NULL;
    END;

    IF COL_LENGTH('MobileAppDeviceStatus', 'SystemDeviceNumber') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [SystemDeviceNumber] nvarchar(120) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'DeviceSystem') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [DeviceSystem] nvarchar(30) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'Platform') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [Platform] nvarchar(30) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'StoreCode') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [StoreCode] nvarchar(50) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'AppVersion') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [AppVersion] nvarchar(80) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'AppBuildVersion') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [AppBuildVersion] nvarchar(80) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'RuntimeVersion') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [RuntimeVersion] nvarchar(120) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'Channel') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [Channel] nvarchar(120) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'UpdateId') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [UpdateId] nvarchar(120) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'UpdateSource') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [UpdateSource] nvarchar(40) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'LastSeenAtUtc') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [LastSeenAtUtc] datetime2 NOT NULL DEFAULT(SYSUTCDATETIME());
    IF COL_LENGTH('MobileAppDeviceStatus', 'LastAuthMode') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [LastAuthMode] nvarchar(30) NOT NULL CONSTRAINT [DF_MobileAppDeviceStatus_LastAuthMode] DEFAULT('unknown');
    IF COL_LENGTH('MobileAppDeviceStatus', 'LastSeenUserGuid') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [LastSeenUserGuid] nvarchar(50) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'LastSeenUsername') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [LastSeenUsername] nvarchar(100) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'LastSeenUserFullName') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [LastSeenUserFullName] nvarchar(160) NULL;
    IF COL_LENGTH('MobileAppDeviceStatus', 'RegisteredDeviceId') IS NULL
        ALTER TABLE [MobileAppDeviceStatus] ADD [RegisteredDeviceId] int NULL;
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppDeviceStatus_HardwareId'
      AND object_id = OBJECT_ID('MobileAppDeviceStatus')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileAppDeviceStatus_HardwareId]
    ON [MobileAppDeviceStatus]([HardwareId]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppDeviceStatus_LastSeen'
      AND object_id = OBJECT_ID('MobileAppDeviceStatus')
)
BEGIN
    CREATE INDEX [IX_MobileAppDeviceStatus_LastSeen]
    ON [MobileAppDeviceStatus]([LastSeenAtUtc]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppDeviceStatus_System_LastSeen'
      AND object_id = OBJECT_ID('MobileAppDeviceStatus')
)
BEGIN
    CREATE INDEX [IX_MobileAppDeviceStatus_System_LastSeen]
    ON [MobileAppDeviceStatus]([DeviceSystem], [LastSeenAtUtc]);
END;";

            // 关键逻辑：App 设备在线状态只维护当前快照，HardwareId 唯一索引用来保证心跳幂等更新。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("Expo App 设备版本与在线快照表检查完成");
        }

        private static async Task EnsureAttendanceLocationSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('UserLoginDeviceRecord', 'U') IS NULL
BEGIN
    CREATE TABLE [UserLoginDeviceRecord] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserLoginDeviceRecord] PRIMARY KEY,
        [RecordGuid] nvarchar(50) NOT NULL,
        [UserGuid] nvarchar(50) NULL,
        [Username] nvarchar(100) NULL,
        [HardwareId] nvarchar(100) NULL,
        [SystemDeviceNumber] nvarchar(100) NULL,
        [DeviceSystem] nvarchar(30) NULL,
        [StoreCode] nvarchar(50) NULL,
        [LoginSource] nvarchar(30) NOT NULL,
        [LoginAtUtc] datetime2 NOT NULL,
        [LoginIp] nvarchar(50) NULL,
        [UserAgent] nvarchar(500) NULL,
        [LocationLatitude] float NULL,
        [LocationLongitude] float NULL,
        [LocationAccuracyMeters] float NULL,
        [LocationCapturedAtUtc] datetime2 NULL,
        [IsDeviceSwitched] bit NOT NULL CONSTRAINT [DF_UserLoginDeviceRecord_IsDeviceSwitched] DEFAULT(0),
        [IsCommonDevice] bit NOT NULL CONSTRAINT [DF_UserLoginDeviceRecord_IsCommonDevice] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('AttendanceLocationSample', 'U') IS NULL
BEGIN
    CREATE TABLE [AttendanceLocationSample] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceLocationSample] PRIMARY KEY,
        [SampleGuid] nvarchar(50) NOT NULL,
        [UserGuid] nvarchar(50) NOT NULL,
        [StoreCode] nvarchar(50) NULL,
        [HardwareId] nvarchar(100) NULL,
        [SystemDeviceNumber] nvarchar(100) NULL,
        [DeviceSystem] nvarchar(30) NULL,
        [EventType] nvarchar(30) NOT NULL,
        [LocationLatitude] float NOT NULL,
        [LocationLongitude] float NOT NULL,
        [LocationAccuracyMeters] float NULL,
        [LocationPermissionStatus] nvarchar(30) NULL,
        [LocationCapturedAtUtc] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('AttendancePunch', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('AttendancePunch', 'LocationLatitude') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationLatitude] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationLongitude') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationLongitude] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationAccuracyMeters') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationAccuracyMeters] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationPermissionStatus') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationPermissionStatus] nvarchar(30) NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationCapturedAtUtc') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationCapturedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_UserLoginDeviceRecord_User_LoginAt'
      AND object_id = OBJECT_ID('UserLoginDeviceRecord')
)
BEGIN
    CREATE INDEX [IX_UserLoginDeviceRecord_User_LoginAt]
    ON [UserLoginDeviceRecord]([UserGuid], [LoginAtUtc]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_UserLoginDeviceRecord_Hardware_LoginAt'
      AND object_id = OBJECT_ID('UserLoginDeviceRecord')
)
BEGIN
    CREATE INDEX [IX_UserLoginDeviceRecord_Hardware_LoginAt]
    ON [UserLoginDeviceRecord]([HardwareId], [LoginAtUtc]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_AttendanceLocationSample_Store_User_Captured'
      AND object_id = OBJECT_ID('AttendanceLocationSample')
)
BEGIN
    CREATE INDEX [IX_AttendanceLocationSample_Store_User_Captured]
    ON [AttendanceLocationSample]([StoreCode], [UserGuid], [LocationCapturedAtUtc]);
END;";

            // 关键位置：定位审计是 App 登录和考勤的合规记录，老库启动必须无损补齐。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("考勤定位和登录设备审计表检查完成");
        }

        private static async Task EnsureServiceApiTokenSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('ServiceApiToken', 'U') IS NULL
BEGIN
    CREATE TABLE [ServiceApiToken] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ServiceApiToken] PRIMARY KEY,
        [Name] nvarchar(120) NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [TokenPrefix] nvarchar(32) NOT NULL,
        [Scopes] nvarchar(500) NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedBy] nvarchar(120) NULL,
        [LastUsedAt] datetime2 NULL,
        [LastUsedIp] nvarchar(64) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_ServiceApiToken_TokenHash'
      AND object_id = OBJECT_ID('ServiceApiToken')
)
BEGIN
    CREATE UNIQUE INDEX [IX_ServiceApiToken_TokenHash]
    ON [ServiceApiToken]([TokenHash]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_ServiceApiToken_CreatedAt'
      AND object_id = OBJECT_ID('ServiceApiToken')
)
BEGIN
    CREATE INDEX [IX_ServiceApiToken_CreatedAt]
    ON [ServiceApiToken]([CreatedAt]);
END;";

            // 关键位置：OTA 自动发布脚本启动前依赖 service token 校验，表缺失时必须由应用启动自举。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("Service API Token 表检查完成");
        }

        private static async Task EnsureWpfAppReleaseSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('WpfAppRelease', 'U') IS NULL
BEGIN
    CREATE TABLE [WpfAppRelease] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_WpfAppRelease] PRIMARY KEY,
        [Channel] nvarchar(80) NOT NULL,
        [Version] nvarchar(80) NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [FileSize] bigint NOT NULL,
        [Sha256] nvarchar(128) NOT NULL,
        [DownloadUrl] nvarchar(1000) NOT NULL,
        [CosObjectKey] nvarchar(500) NOT NULL,
        [InstallerType] nvarchar(40) NOT NULL,
        [InstallerArguments] nvarchar(500) NULL,
        [ReleaseNotes] nvarchar(2000) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_WpfAppRelease_IsActive] DEFAULT(1),
        [PublishedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('WpfAppRelease', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('WpfAppRelease', 'Channel') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [Channel] nvarchar(80) NOT NULL CONSTRAINT [DF_WpfAppRelease_Channel] DEFAULT('production');
    END;

    IF COL_LENGTH('WpfAppRelease', 'Version') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [Version] nvarchar(80) NOT NULL CONSTRAINT [DF_WpfAppRelease_Version] DEFAULT('');
    END;

    IF COL_LENGTH('WpfAppRelease', 'FileName') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [FileName] nvarchar(260) NOT NULL CONSTRAINT [DF_WpfAppRelease_FileName] DEFAULT('');
    END;

    IF COL_LENGTH('WpfAppRelease', 'FileSize') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [FileSize] bigint NOT NULL CONSTRAINT [DF_WpfAppRelease_FileSize] DEFAULT(0);
    END;

    IF COL_LENGTH('WpfAppRelease', 'Sha256') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [Sha256] nvarchar(128) NOT NULL CONSTRAINT [DF_WpfAppRelease_Sha256] DEFAULT('');
    END;

    IF COL_LENGTH('WpfAppRelease', 'DownloadUrl') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [DownloadUrl] nvarchar(1000) NOT NULL CONSTRAINT [DF_WpfAppRelease_DownloadUrl] DEFAULT('');
    END;

    IF COL_LENGTH('WpfAppRelease', 'CosObjectKey') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [CosObjectKey] nvarchar(500) NOT NULL CONSTRAINT [DF_WpfAppRelease_CosObjectKey] DEFAULT('');
    END;

    IF COL_LENGTH('WpfAppRelease', 'InstallerType') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [InstallerType] nvarchar(40) NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'InstallerArguments') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [InstallerArguments] nvarchar(500) NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'ReleaseNotes') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [ReleaseNotes] nvarchar(2000) NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'IsActive') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [IsActive] bit NOT NULL CONSTRAINT [DF_WpfAppRelease_IsActive] DEFAULT(1);
    END;

    IF COL_LENGTH('WpfAppRelease', 'PublishedAt') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [PublishedAt] datetime2 NOT NULL CONSTRAINT [DF_WpfAppRelease_PublishedAt] DEFAULT(SYSUTCDATETIME());
    END;

    IF COL_LENGTH('WpfAppRelease', 'CreatedAt') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_WpfAppRelease_CreatedAt] DEFAULT(SYSUTCDATETIME());
    END;

    IF COL_LENGTH('WpfAppRelease', 'CreatedBy') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [CreatedBy] nvarchar(max) NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'UpdatedAt') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [UpdatedAt] datetime2 NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'UpdatedBy') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [UpdatedBy] nvarchar(max) NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'IsDeleted') IS NULL
    BEGIN
        ALTER TABLE [WpfAppRelease]
        ADD [IsDeleted] bit NULL;
    END;

    IF COL_LENGTH('WpfAppRelease', 'InstallerType') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE [WpfAppRelease]
            SET [InstallerType] = CASE
                WHEN LOWER(RIGHT(ISNULL([FileName], ''''), 4)) = ''.msi'' THEN ''msi''
                WHEN LOWER(RIGHT(ISNULL([FileName], ''''), 4)) = ''.exe'' THEN ''exe''
                ELSE ''exe''
            END
            WHERE [InstallerType] IS NULL
               OR LTRIM(RTRIM([InstallerType])) = '''';
        ');

        ALTER TABLE [WpfAppRelease]
        ALTER COLUMN [InstallerType] nvarchar(40) NOT NULL;
    END;
END;

-- 关键位置：旧库可能存在重复的 Channel+Version，先保留未删除、启用且较新的行，其余行统一失活，避免唯一索引阻断启动。
IF OBJECT_ID('WpfAppRelease', 'U') IS NOT NULL
BEGIN
    -- 关键位置：先把 WPF 发布历史数据规范成业务层可比较的渠道和版本，避免大小写和 v 前缀把重复版本漏过去。
    UPDATE [WpfAppRelease]
    SET [Channel] = CASE
            WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
            ELSE LOWER(LTRIM(RTRIM([Channel])))
        END,
        [Version] = CASE
            WHEN NULLIF(LTRIM(RTRIM([Version])), '') IS NULL THEN ''
            WHEN LEFT(LTRIM(RTRIM([Version])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([Version])), 2, 79)
            ELSE LTRIM(RTRIM([Version]))
        END
    WHERE ISNULL([Channel], '') <> CASE
            WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
            ELSE LOWER(LTRIM(RTRIM([Channel])))
        END
       OR ISNULL([Version], '') <> CASE
            WHEN NULLIF(LTRIM(RTRIM([Version])), '') IS NULL THEN ''
            WHEN LEFT(LTRIM(RTRIM([Version])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([Version])), 2, 79)
            ELSE LTRIM(RTRIM([Version]))
        END;

    ;WITH [WpfAppReleaseDuplicateRows] AS (
        SELECT
            [Id],
            ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel], [NormalizedVersion]
                ORDER BY
                    CASE WHEN ISNULL([IsDeleted], 0) = 0 THEN 0 ELSE 1 END,
                    CASE WHEN [IsActive] = 1 THEN 0 ELSE 1 END,
                    [CreatedAt] DESC,
                    [Id] DESC
            ) AS [RowNumber]
        FROM (
            SELECT
                [Id],
                CASE
                    WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
                    ELSE LOWER(LTRIM(RTRIM([Channel])))
                END AS [NormalizedChannel],
                CASE
                    WHEN NULLIF(LTRIM(RTRIM([Version])), '') IS NULL THEN ''
                    WHEN LEFT(LTRIM(RTRIM([Version])), 1) IN ('v', 'V')
                        THEN SUBSTRING(LTRIM(RTRIM([Version])), 2, 79)
                    ELSE LTRIM(RTRIM([Version]))
                END AS [NormalizedVersion],
                [IsActive],
                [CreatedAt],
                [IsDeleted]
            FROM [WpfAppRelease]
        ) AS [NormalizedWpfAppReleaseRows]
    )
    UPDATE [WpfAppRelease]
    SET [IsActive] = 0
    WHERE [Id] IN (
        SELECT [Id]
        FROM [WpfAppReleaseDuplicateRows]
        WHERE [RowNumber] > 1
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_WpfAppRelease_Channel_Version'
      AND object_id = OBJECT_ID('WpfAppRelease')
)
BEGIN
    -- 关键位置：WPF 发布唯一键只约束活跃版本，既保留历史重复脏数据审计线索，也避免旧库启动迁移失败。
    CREATE UNIQUE INDEX [IX_WpfAppRelease_Channel_Version]
    ON [WpfAppRelease]([Channel], [Version])
    WHERE [IsActive] = 1;
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_WpfAppRelease_Channel_PublishedAt'
      AND object_id = OBJECT_ID('WpfAppRelease')
)
BEGIN
    CREATE INDEX [IX_WpfAppRelease_Channel_PublishedAt]
    ON [WpfAppRelease]([Channel], [IsActive], [PublishedAt]);
END;

IF OBJECT_ID('WpfUpdatePolicy', 'U') IS NULL
BEGIN
    CREATE TABLE [WpfUpdatePolicy] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_WpfUpdatePolicy] PRIMARY KEY,
        [Channel] nvarchar(80) NOT NULL,
        [TargetVersion] nvarchar(80) NOT NULL,
        [MinimumSupportedVersion] nvarchar(80) NULL,
        [ForceUpdate] bit NOT NULL CONSTRAINT [DF_WpfUpdatePolicy_ForceUpdate] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('WpfUpdatePolicy', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('WpfUpdatePolicy', 'Channel') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [Channel] nvarchar(80) NOT NULL CONSTRAINT [DF_WpfUpdatePolicy_Channel] DEFAULT('production');
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'TargetVersion') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [TargetVersion] nvarchar(80) NOT NULL CONSTRAINT [DF_WpfUpdatePolicy_TargetVersion] DEFAULT('');
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'MinimumSupportedVersion') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [MinimumSupportedVersion] nvarchar(80) NULL;
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'MinimumSupportedVersion') IS NOT NULL
    BEGIN
        -- 关键位置：旧库可能把 MinimumSupportedVersion 建成 NOT NULL，规范化空白值前先统一放宽成可空，避免启动迁移写入 NULL 失败。
        ALTER TABLE [WpfUpdatePolicy]
        ALTER COLUMN [MinimumSupportedVersion] nvarchar(80) NULL;
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'ForceUpdate') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [ForceUpdate] bit NOT NULL CONSTRAINT [DF_WpfUpdatePolicy_ForceUpdate] DEFAULT(0);
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'CreatedAt') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_WpfUpdatePolicy_CreatedAt] DEFAULT(SYSUTCDATETIME());
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'CreatedBy') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [CreatedBy] nvarchar(max) NULL;
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'UpdatedAt') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [UpdatedAt] datetime2 NULL;
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'UpdatedBy') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [UpdatedBy] nvarchar(max) NULL;
    END;

    IF COL_LENGTH('WpfUpdatePolicy', 'IsDeleted') IS NULL
    BEGIN
        ALTER TABLE [WpfUpdatePolicy]
        ADD [IsDeleted] bit NULL;
    END;
END;

-- 关键位置：策略表每个 Channel 只能保留一条配置，启动迁移先保留未删除且最新的策略，再补唯一索引。
IF OBJECT_ID('WpfUpdatePolicy', 'U') IS NOT NULL
BEGIN
    -- 关键位置：策略表也要先按业务层语义规范化渠道和版本，避免引用不到已规范化的发布版本。
    UPDATE [WpfUpdatePolicy]
    SET [Channel] = CASE
            WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
            ELSE LOWER(LTRIM(RTRIM([Channel])))
        END,
        [TargetVersion] = CASE
            WHEN NULLIF(LTRIM(RTRIM([TargetVersion])), '') IS NULL THEN ''
            WHEN LEFT(LTRIM(RTRIM([TargetVersion])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([TargetVersion])), 2, 79)
            ELSE LTRIM(RTRIM([TargetVersion]))
        END,
        [MinimumSupportedVersion] = CASE
            WHEN NULLIF(LTRIM(RTRIM([MinimumSupportedVersion])), '') IS NULL THEN NULL
            WHEN LEFT(LTRIM(RTRIM([MinimumSupportedVersion])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([MinimumSupportedVersion])), 2, 79)
            ELSE LTRIM(RTRIM([MinimumSupportedVersion]))
        END
    WHERE ISNULL([Channel], '') <> CASE
            WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
            ELSE LOWER(LTRIM(RTRIM([Channel])))
        END
       OR ISNULL([TargetVersion], '') <> CASE
            WHEN NULLIF(LTRIM(RTRIM([TargetVersion])), '') IS NULL THEN ''
            WHEN LEFT(LTRIM(RTRIM([TargetVersion])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([TargetVersion])), 2, 79)
            ELSE LTRIM(RTRIM([TargetVersion]))
        END
       OR ISNULL([MinimumSupportedVersion], '') <> ISNULL(CASE
            WHEN NULLIF(LTRIM(RTRIM([MinimumSupportedVersion])), '') IS NULL THEN NULL
            WHEN LEFT(LTRIM(RTRIM([MinimumSupportedVersion])), 1) IN ('v', 'V')
                THEN SUBSTRING(LTRIM(RTRIM([MinimumSupportedVersion])), 2, 79)
            ELSE LTRIM(RTRIM([MinimumSupportedVersion]))
        END, '');

    ;WITH [WpfUpdatePolicyDuplicateRows] AS (
        SELECT
            [Id],
            ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel]
                ORDER BY
                    CASE WHEN ISNULL([IsDeleted], 0) = 0 THEN 0 ELSE 1 END,
                    [LastChangedAt] DESC,
                    [CreatedAt] DESC,
                    [Id] DESC
            ) AS [RowNumber]
        FROM (
            SELECT
                [Id],
                CASE
                    WHEN NULLIF(LTRIM(RTRIM([Channel])), '') IS NULL THEN 'production'
                    ELSE LOWER(LTRIM(RTRIM([Channel])))
                END AS [NormalizedChannel],
                ISNULL([UpdatedAt], [CreatedAt]) AS [LastChangedAt],
                [CreatedAt],
                [IsDeleted]
            FROM [WpfUpdatePolicy]
        ) AS [NormalizedWpfUpdatePolicyRows]
    )
    DELETE FROM [WpfUpdatePolicy]
    WHERE [Id] IN (
        SELECT [Id]
        FROM [WpfUpdatePolicyDuplicateRows]
        WHERE [RowNumber] > 1
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_WpfUpdatePolicy_Channel'
      AND object_id = OBJECT_ID('WpfUpdatePolicy')
)
BEGIN
    CREATE UNIQUE INDEX [IX_WpfUpdatePolicy_Channel]
    ON [WpfUpdatePolicy]([Channel]);
END;";

            // 关键位置：WPF 发布链路已经依赖校验、下载、安装器和强更字段，旧表也要在启动时补齐这些列。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("WPF 客户端发布与更新策略表检查完成");
        }

        private static async Task EnsurePreorderSchemaAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[PreorderTemplate]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderTemplate](
        [TemplateGuid] nvarchar(50) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [Revision] int NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderTemplate_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderTemplate] PRIMARY KEY ([TemplateGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderTemplateItem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderTemplateItem](
        [TemplateItemGuid] nvarchar(50) NOT NULL,
        [TemplateGuid] nvarchar(50) NOT NULL,
        [ProductCode] nvarchar(50) NOT NULL,
        [MinimumOrderQuantity] int NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderTemplateItem_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderTemplateItem] PRIMARY KEY ([TemplateItemGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderTemplateStore]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderTemplateStore](
        [TemplateStoreGuid] nvarchar(50) NOT NULL,
        [TemplateGuid] nvarchar(50) NOT NULL,
        [StoreGuid] nvarchar(50) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderTemplateStore_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderTemplateStore] PRIMARY KEY ([TemplateStoreGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderActivation]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderActivation](
        [ActivationGuid] nvarchar(50) NOT NULL,
        [TemplateGuid] nvarchar(50) NOT NULL,
        [TemplateNameSnapshot] nvarchar(150) NOT NULL,
        [PeriodNumber] int NOT NULL,
        [ActivationCode] nvarchar(80) NOT NULL,
        [SourceTemplateRevision] int NOT NULL,
        [StartAtUtc] datetime2 NOT NULL,
        [EndAtUtc] datetime2 NOT NULL,
        [EstimatedArrivalDate] date NULL,
        [Status] nvarchar(30) NOT NULL,
        [ClosedAtUtc] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderActivation_IsDeleted] DEFAULT(0),
        CONSTRAINT [CK_PreorderActivation_Status] CHECK ([Status] IN ('Scheduled', 'Active', 'Closed', 'Cancelled')),
        CONSTRAINT [PK_PreorderActivation] PRIMARY KEY ([ActivationGuid])
    );
END;

-- 预计到货日是纯业务日期；已有 PreorderActivation 表必须用可重复执行的追加式迁移补齐。
IF OBJECT_ID(N'[dbo].[PreorderActivation]', N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.PreorderActivation', N'EstimatedArrivalDate') IS NULL
    ALTER TABLE [dbo].[PreorderActivation] ADD [EstimatedArrivalDate] date NULL;

IF OBJECT_ID(N'[dbo].[PreorderActivationItem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderActivationItem](
        [ActivationItemGuid] nvarchar(50) NOT NULL,
        [ActivationGuid] nvarchar(50) NOT NULL,
        [ProductCode] nvarchar(50) NOT NULL,
        [ItemNumber] nvarchar(50) NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [ProductImage] nvarchar(500) NULL,
        [ImportPrice] decimal(18,4) NOT NULL,
        [RetailPrice] decimal(18,4) NOT NULL,
        [MinimumOrderQuantity] int NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderActivationItem_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderActivationItem] PRIMARY KEY ([ActivationItemGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderActivationStore]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderActivationStore](
        [ActivationStoreGuid] nvarchar(50) NOT NULL,
        [ActivationGuid] nvarchar(50) NOT NULL,
        [StoreGuid] nvarchar(50) NOT NULL,
        [StoreCode] nvarchar(50) NOT NULL,
        [StoreName] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderActivationStore_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderActivationStore] PRIMARY KEY ([ActivationStoreGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderWarehouseOrder](
        [OrderGuid] nvarchar(50) NOT NULL,
        [ActivationGuid] nvarchar(50) NOT NULL,
        [StoreGuid] nvarchar(50) NOT NULL,
        [StoreCode] nvarchar(50) NOT NULL,
        [StoreName] nvarchar(100) NOT NULL,
        [OrderNo] nvarchar(80) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [DraftRevision] int NOT NULL,
        [SubmittedByUserGuid] nvarchar(50) NULL,
        [SubmittedByName] nvarchar(150) NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [WarehouseNotes] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderWarehouseOrder_IsDeleted] DEFAULT(0),
        CONSTRAINT [CK_PreorderWarehouseOrder_Status] CHECK ([Status] IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled')),
        CONSTRAINT [PK_PreorderWarehouseOrder] PRIMARY KEY ([OrderGuid])
    );
END;

IF OBJECT_ID(N'[dbo].[PreorderWarehouseOrderItem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PreorderWarehouseOrderItem](
        [OrderItemGuid] nvarchar(50) NOT NULL,
        [OrderGuid] nvarchar(50) NOT NULL,
        [ActivationItemGuid] nvarchar(50) NOT NULL,
        [ProductCode] nvarchar(50) NOT NULL,
        [ItemNumber] nvarchar(50) NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [ProductImage] nvarchar(500) NULL,
        [PackCount] int NOT NULL,
        [MinimumOrderQuantity] int NOT NULL,
        [OrderedQuantity] int NOT NULL,
        [ImportPrice] decimal(18,4) NOT NULL,
        [RetailPrice] decimal(18,4) NOT NULL,
        [ImportAmount] decimal(18,4) NOT NULL,
        [RetailAmount] decimal(18,4) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(200) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PreorderWarehouseOrderItem_IsDeleted] DEFAULT(0),
        CONSTRAINT [PK_PreorderWarehouseOrderItem] PRIMARY KEY ([OrderItemGuid])
    );
END;

-- 已有表也必须补状态约束；若存在非法历史值则让启动迁移失败，避免门禁 fail-open。
IF OBJECT_ID(N'[dbo].[PreorderActivation]', N'U') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreorderActivation_Status' AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderActivation]'))
    ALTER TABLE [dbo].[PreorderActivation] WITH CHECK
        ADD CONSTRAINT [CK_PreorderActivation_Status] CHECK ([Status] IN ('Scheduled', 'Active', 'Closed', 'Cancelled'));

IF OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]', N'U') IS NOT NULL
BEGIN
    -- 已有旧约束不包含退回状态，必须原地升级，不能只影响新建数据库。
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = 'CK_PreorderWarehouseOrder_Status'
          AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]')
          AND definition NOT LIKE '%ReturnedForRevision%'
    )
        ALTER TABLE [dbo].[PreorderWarehouseOrder] DROP CONSTRAINT [CK_PreorderWarehouseOrder_Status];

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreorderWarehouseOrder_Status' AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]'))
        ALTER TABLE [dbo].[PreorderWarehouseOrder] WITH CHECK
            ADD CONSTRAINT [CK_PreorderWarehouseOrder_Status] CHECK ([Status] IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled'));
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderTemplateItem_Template_Product' AND object_id = OBJECT_ID('PreorderTemplateItem'))
    CREATE UNIQUE INDEX [UX_PreorderTemplateItem_Template_Product] ON [PreorderTemplateItem]([TemplateGuid], [ProductCode]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderTemplateStore_Template_Store' AND object_id = OBJECT_ID('PreorderTemplateStore'))
    CREATE UNIQUE INDEX [UX_PreorderTemplateStore_Template_Store] ON [PreorderTemplateStore]([TemplateGuid], [StoreGuid]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderActivation_Template_Time' AND object_id = OBJECT_ID('PreorderActivation'))
    CREATE INDEX [IX_PreorderActivation_Template_Time] ON [PreorderActivation]([TemplateGuid], [StartAtUtc], [EndAtUtc], [Status]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivation_Template_Period' AND object_id = OBJECT_ID('PreorderActivation'))
    CREATE UNIQUE INDEX [UX_PreorderActivation_Template_Period] ON [PreorderActivation]([TemplateGuid], [PeriodNumber]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivation_Code' AND object_id = OBJECT_ID('PreorderActivation'))
    CREATE UNIQUE INDEX [UX_PreorderActivation_Code] ON [PreorderActivation]([ActivationCode]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivationItem_Activation_Product' AND object_id = OBJECT_ID('PreorderActivationItem'))
    CREATE UNIQUE INDEX [UX_PreorderActivationItem_Activation_Product] ON [PreorderActivationItem]([ActivationGuid], [ProductCode]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivationStore_Activation_Store' AND object_id = OBJECT_ID('PreorderActivationStore'))
    CREATE UNIQUE INDEX [UX_PreorderActivationStore_Activation_Store] ON [PreorderActivationStore]([ActivationGuid], [StoreGuid]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderActivationStore_StoreCode' AND object_id = OBJECT_ID('PreorderActivationStore'))
    CREATE INDEX [IX_PreorderActivationStore_StoreCode] ON [PreorderActivationStore]([StoreCode], [ActivationGuid]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderActivationStore_StoreGuid' AND object_id = OBJECT_ID('PreorderActivationStore'))
    CREATE INDEX [IX_PreorderActivationStore_StoreGuid] ON [PreorderActivationStore]([StoreGuid], [ActivationGuid]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrder_Activation_Store' AND object_id = OBJECT_ID('PreorderWarehouseOrder'))
    CREATE UNIQUE INDEX [UX_PreorderWarehouseOrder_Activation_Store] ON [PreorderWarehouseOrder]([ActivationGuid], [StoreGuid]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrder_OrderNo' AND object_id = OBJECT_ID('PreorderWarehouseOrder'))
    CREATE UNIQUE INDEX [UX_PreorderWarehouseOrder_OrderNo] ON [PreorderWarehouseOrder]([OrderNo]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrderItem_Order_Item' AND object_id = OBJECT_ID('PreorderWarehouseOrderItem'))
    CREATE UNIQUE INDEX [UX_PreorderWarehouseOrderItem_Order_Item] ON [PreorderWarehouseOrderItem]([OrderGuid], [ActivationItemGuid]) WHERE [IsDeleted] = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderWarehouseOrderItem_OrderGuid' AND object_id = OBJECT_ID('PreorderWarehouseOrderItem'))
    CREATE INDEX [IX_PreorderWarehouseOrderItem_OrderGuid] ON [PreorderWarehouseOrderItem]([OrderGuid]);
""";

            // 关键位置：Preorder 是独立订单域，启动时必须同时具备模板、批次、订单和唯一约束。
            await db.Ado.ExecuteCommandAsync(sql);
            await PreorderSchemaBootstrap.EnsureIndexesAsync(db);
            logger.LogInformation("Preorder 独立订单表与索引检查完成");
        }
    }
}
