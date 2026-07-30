using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>
/// Mobile iOS 与 iPad 更新发布事实、投放策略的独立启动迁移。
/// 不修改 MobileAppBuild、MobileAppOtaUpdate 或 WPF 更新表。
/// </summary>
public static class AppUpdatePolicySchemaMigrator
{
    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        const string sql = """
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @AppUpdatePolicySchemaLockResult int;
    EXEC @AppUpdatePolicySchemaLockResult = sys.sp_getapplock
        @Resource = N'AppUpdatePolicy_Schema_Initialization',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 30000;
    IF @AppUpdatePolicySchemaLockResult < 0
        THROW 51061, 'Unable to acquire app update policy schema lock.', 1;

    IF OBJECT_ID(N'[dbo].[IosAppStoreRelease]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[IosAppStoreRelease] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_IosAppStoreRelease] PRIMARY KEY,
            [App] nvarchar(40) NOT NULL,
            [AppStoreId] nvarchar(32) NOT NULL,
            [BundleIdentifier] nvarchar(200) NOT NULL,
            [Version] nvarchar(64) NOT NULL,
            [BuildNumber] nvarchar(64) NOT NULL,
            [Storefront] nvarchar(8) NOT NULL,
            [AppStoreUrl] nvarchar(2048) NOT NULL,
            [AppleVerifiedAtUtc] datetime2 NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_IosAppStoreRelease_IsDeleted] DEFAULT(0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_IosAppStoreRelease_App_Storefront_Version_Build'
          AND [object_id] = OBJECT_ID(N'[dbo].[IosAppStoreRelease]')
    )
        CREATE UNIQUE INDEX [UX_IosAppStoreRelease_App_Storefront_Version_Build]
            ON [dbo].[IosAppStoreRelease]([App], [Storefront], [Version], [BuildNumber])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[MobileIosNativeUpdatePolicy]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[MobileIosNativeUpdatePolicy] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileIosNativeUpdatePolicy] PRIMARY KEY,
            [PolicyKey] nvarchar(40) NOT NULL,
            [ReleaseId] uniqueidentifier NULL,
            [MinimumSupportedVersion] nvarchar(64) NULL,
            [ReleaseMessage] nvarchar(1000) NULL,
            [Enabled] bit NOT NULL,
            [PolicyVersion] bigint NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_MobileIosNativeUpdatePolicy_IsDeleted] DEFAULT(0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_MobileIosNativeUpdatePolicy_PolicyKey'
          AND [object_id] = OBJECT_ID(N'[dbo].[MobileIosNativeUpdatePolicy]')
    )
        CREATE UNIQUE INDEX [UX_MobileIosNativeUpdatePolicy_PolicyKey]
            ON [dbo].[MobileIosNativeUpdatePolicy]([PolicyKey])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosIpadNativeUpdatePolicy]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosIpadNativeUpdatePolicy] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosIpadNativeUpdatePolicy] PRIMARY KEY,
            [PolicyKey] nvarchar(40) NOT NULL,
            [ReleaseId] uniqueidentifier NULL,
            [MinimumSupportedVersion] nvarchar(64) NULL,
            [MinimumSupportedBuildNumber] int NULL,
            [ReleaseMessage] nvarchar(1000) NULL,
            [TargetScope] nvarchar(16) NOT NULL,
            [Enabled] bit NOT NULL,
            [PolicyVersion] bigint NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosIpadNativeUpdatePolicy_IsDeleted] DEFAULT(0),
            CONSTRAINT [CK_PosIpadNativeUpdatePolicy_TargetScope]
                CHECK ([TargetScope] IN (N'all', N'stores'))
        );
    END;

    IF COL_LENGTH(N'[dbo].[PosIpadNativeUpdatePolicy]', N'MinimumSupportedBuildNumber') IS NULL
        ALTER TABLE [dbo].[PosIpadNativeUpdatePolicy]
            ADD [MinimumSupportedBuildNumber] int NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadNativeUpdatePolicy_PolicyKey'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadNativeUpdatePolicy]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadNativeUpdatePolicy_PolicyKey]
            ON [dbo].[PosIpadNativeUpdatePolicy]([PolicyKey])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosIpadNativeUpdatePolicyTarget]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosIpadNativeUpdatePolicyTarget] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosIpadNativeUpdatePolicyTarget] PRIMARY KEY,
            [PolicyId] uniqueidentifier NOT NULL,
            [StoreGuid] nvarchar(100) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosIpadNativeUpdatePolicyTarget_IsDeleted] DEFAULT(0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadNativeUpdatePolicyTarget_Policy_Store'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadNativeUpdatePolicyTarget]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadNativeUpdatePolicyTarget_Policy_Store]
            ON [dbo].[PosIpadNativeUpdatePolicyTarget]([PolicyId], [StoreGuid])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosIpadOtaRelease]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosIpadOtaRelease] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosIpadOtaRelease] PRIMARY KEY,
            [Environment] nvarchar(32) NOT NULL,
            [UpdateGroupId] nvarchar(120) NOT NULL,
            [IosUpdateId] nvarchar(120) NOT NULL,
            [Channel] nvarchar(120) NOT NULL,
            [RuntimeVersion] nvarchar(120) NOT NULL,
            [GitCommitHash] nvarchar(120) NULL,
            [DashboardUrl] nvarchar(2048) NULL,
            [PublishedAtUtc] datetime2 NOT NULL,
            [IsRollback] bit NOT NULL,
            [RollbackOfReleaseId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosIpadOtaRelease_IsDeleted] DEFAULT(0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRelease_Environment_Group'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRelease]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRelease_Environment_Group]
            ON [dbo].[PosIpadOtaRelease]([Environment], [UpdateGroupId])
            WHERE [IsDeleted] = 0;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRelease_Environment_IosUpdate'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRelease]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRelease_Environment_IosUpdate]
            ON [dbo].[PosIpadOtaRelease]([Environment], [IosUpdateId])
            WHERE [IsDeleted] = 0;

    IF EXISTS (
        SELECT 1
        FROM [dbo].[PosIpadOtaRelease]
        WHERE [IsDeleted] = 0
        GROUP BY [Environment], [Channel]
        HAVING COUNT_BIG(*) > 1
    )
        THROW 51062, 'Duplicate production iPad OTA release channels exist.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRelease_Environment_Channel'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRelease]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRelease_Environment_Channel]
            ON [dbo].[PosIpadOtaRelease]([Environment], [Channel])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosIpadOtaRollout]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosIpadOtaRollout] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosIpadOtaRollout] PRIMARY KEY,
            [Environment] nvarchar(32) NOT NULL,
            [ReleaseId] uniqueidentifier NOT NULL,
            [ForceUpdate] bit NOT NULL,
            [TargetScope] nvarchar(16) NOT NULL,
            [ReleaseMessage] nvarchar(1000) NULL,
            [Enabled] bit NOT NULL,
            [PolicyVersion] bigint NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosIpadOtaRollout_IsDeleted] DEFAULT(0),
            CONSTRAINT [CK_PosIpadOtaRollout_TargetScope]
                CHECK ([TargetScope] IN (N'all', N'stores'))
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRollout_Environment_Active'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRollout]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRollout_Environment_Active]
            ON [dbo].[PosIpadOtaRollout]([Environment])
            WHERE [Enabled] = 1 AND [IsDeleted] = 0;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRollout_Environment_PolicyVersion'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRollout]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRollout_Environment_PolicyVersion]
            ON [dbo].[PosIpadOtaRollout]([Environment], [PolicyVersion])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosIpadOtaRolloutTarget]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosIpadOtaRolloutTarget] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosIpadOtaRolloutTarget] PRIMARY KEY,
            [RolloutId] uniqueidentifier NOT NULL,
            [StoreGuid] nvarchar(100) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosIpadOtaRolloutTarget_IsDeleted] DEFAULT(0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosIpadOtaRolloutTarget_Rollout_Store'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosIpadOtaRolloutTarget]')
    )
        CREATE UNIQUE INDEX [UX_PosIpadOtaRolloutTarget_Rollout_Store]
            ON [dbo].[PosIpadOtaRolloutTarget]([RolloutId], [StoreGuid])
            WHERE [IsDeleted] = 0;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

        await db.Ado.ExecuteCommandAsync(sql);
        logger.LogInformation("Mobile iOS 与 iPad 更新策略表检查完成");
    }
}
