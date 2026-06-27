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
            await EnsureWpfAppReleaseSchemaAsync(db, logger);
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
