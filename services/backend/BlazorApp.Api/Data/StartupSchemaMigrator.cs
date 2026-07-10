using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    public static class StartupSchemaMigrator
    {
        public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
        {
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
            await EnsureServiceApiTokenSchemaAsync(db, logger);
            await EnsureWpfAppReleaseSchemaAsync(db, logger);
            await EnsureWarehouseOrderCartOwnerSchemaAsync(db, logger);
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
END;";

            // 关键逻辑：老表只补 nullable 关联列，StoreCode 继续保留兼容显示；有效条码唯一性按 UserGUID 和 UserBarcode 双重兜底。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("CashRegisterUsers.UserGUID 列与有效条码唯一索引检查完成");
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
    }
}
