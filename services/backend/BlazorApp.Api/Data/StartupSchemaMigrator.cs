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
            await EnsureMobileAppBuildSchemaAsync(db, logger);
            await EnsureServiceApiTokenSchemaAsync(db, logger);
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
    }
}
