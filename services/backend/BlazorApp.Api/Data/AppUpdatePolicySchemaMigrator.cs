using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>
/// Mobile、iPad 与手持 POS 更新发布事实、投放策略的独立启动迁移。
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
            [MinimumSupportedBuildNumber] int NULL,
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

    IF COL_LENGTH(N'[dbo].[MobileIosNativeUpdatePolicy]', N'MinimumSupportedBuildNumber') IS NULL
        ALTER TABLE [dbo].[MobileIosNativeUpdatePolicy]
            ADD [MinimumSupportedBuildNumber] int NULL;

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

    IF OBJECT_ID(N'[dbo].[AppOtaRelease]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AppOtaRelease] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_AppOtaRelease] PRIMARY KEY,
            [ReleaseBatchId] uniqueidentifier NOT NULL,
            [AppKey] nvarchar(80) NOT NULL,
            [Environment] nvarchar(32) NOT NULL,
            [ClientChannel] nvarchar(120) NOT NULL,
            [ReleaseChannel] nvarchar(160) NOT NULL,
            [EasBranch] nvarchar(160) NOT NULL,
            [ProjectName] nvarchar(120) NOT NULL,
            [Platform] nvarchar(16) NOT NULL,
            [RuntimeVersion] nvarchar(120) NOT NULL,
            [UpdateGroupId] nvarchar(120) NOT NULL,
            [UpdateId] nvarchar(120) NOT NULL,
            [Message] nvarchar(1000) NULL,
            [GitCommitHash] nvarchar(120) NULL,
            [DashboardUrl] nvarchar(2048) NULL,
            [PublishedAtUtc] datetime2 NOT NULL,
            [IsRollback] bit NOT NULL,
            [RollbackOfReleaseId] uniqueidentifier NULL,
            [FactFingerprint] char(64) NOT NULL,
            [Legacy] bit NOT NULL,
            [RegistrationSource] nvarchar(64) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_AppOtaRelease_IsDeleted] DEFAULT(0),
            CONSTRAINT [CK_AppOtaRelease_AppKey]
                CHECK ([AppKey] IN (N'mobile', N'pos-handheld')),
            CONSTRAINT [CK_AppOtaRelease_Environment]
                CHECK (
                    ([AppKey] = N'mobile' AND [Environment] IN (N'production', N'preview'))
                    OR ([AppKey] = N'pos-handheld' AND [Environment] = N'production')
                ),
            CONSTRAINT [CK_AppOtaRelease_Platform]
                CHECK ([Platform] IN (N'android', N'ios')),
            CONSTRAINT [CK_AppOtaRelease_RollbackPair]
                CHECK (
                    ([IsRollback] = 1 AND [RollbackOfReleaseId] IS NOT NULL)
                    OR ([IsRollback] = 0 AND [RollbackOfReleaseId] IS NULL)
                )
        );
    END;

    IF OBJECT_ID(N'[dbo].[CK_AppOtaRelease_RollbackPair]', N'C') IS NULL
    BEGIN
        ALTER TABLE [dbo].[AppOtaRelease] WITH CHECK
            ADD CONSTRAINT [CK_AppOtaRelease_RollbackPair]
                CHECK (
                    ([IsRollback] = 1 AND [RollbackOfReleaseId] IS NOT NULL)
                    OR ([IsRollback] = 0 AND [RollbackOfReleaseId] IS NULL)
                );
        ALTER TABLE [dbo].[AppOtaRelease]
            CHECK CONSTRAINT [CK_AppOtaRelease_RollbackPair];
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_AppOtaRelease_App_Environment_Platform_UpdateId'
          AND [object_id] = OBJECT_ID(N'[dbo].[AppOtaRelease]')
    )
        CREATE UNIQUE INDEX [UX_AppOtaRelease_App_Environment_Platform_UpdateId]
            ON [dbo].[AppOtaRelease]([AppKey], [Environment], [Platform], [UpdateId])
            WHERE [IsDeleted] = 0;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_AppOtaRelease_App_Environment_Platform_GroupId'
          AND [object_id] = OBJECT_ID(N'[dbo].[AppOtaRelease]')
    )
        CREATE UNIQUE INDEX [UX_AppOtaRelease_App_Environment_Platform_GroupId]
            ON [dbo].[AppOtaRelease]([AppKey], [Environment], [Platform], [UpdateGroupId])
            WHERE [IsDeleted] = 0;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_AppOtaRelease_App_Platform_ReleaseChannel'
          AND [object_id] = OBJECT_ID(N'[dbo].[AppOtaRelease]')
    )
        -- fixed-channel legacy 事实允许历史复用；所有新发布的 release channel 永久唯一。
        CREATE UNIQUE INDEX [UX_AppOtaRelease_App_Platform_ReleaseChannel]
            ON [dbo].[AppOtaRelease]([AppKey], [Platform], [ReleaseChannel])
            WHERE [IsDeleted] = 0 AND [Legacy] = 0;

    IF OBJECT_ID(N'[dbo].[TR_AppOtaRelease_Immutable]', N'TR') IS NULL
        EXEC(N'
            CREATE TRIGGER [dbo].[TR_AppOtaRelease_Immutable]
            ON [dbo].[AppOtaRelease]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51065, ''AppOtaRelease is immutable.'', 1;
            END;
        ');

    IF OBJECT_ID(N'[dbo].[MobileOtaPolicy]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[MobileOtaPolicy] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileOtaPolicy] PRIMARY KEY,
            [Environment] nvarchar(32) NOT NULL,
            [Platform] nvarchar(16) NOT NULL,
            [Enabled] bit NOT NULL,
            [Required] bit NOT NULL,
            [TargetReleaseId] uniqueidentifier NULL,
            [TargetRuntimeVersion] nvarchar(120) NULL,
            [ReleaseMessage] nvarchar(1000) NULL,
            [PolicyVersion] bigint NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_MobileOtaPolicy_IsDeleted] DEFAULT(0),
            CONSTRAINT [CK_MobileOtaPolicy_Lane]
                CHECK (
                    [Environment] IN (N'production', N'preview')
                    AND [Platform] IN (N'android', N'ios')
                ),
            CONSTRAINT [CK_MobileOtaPolicy_Version] CHECK ([PolicyVersion] > 0),
            CONSTRAINT [CK_MobileOtaPolicy_Target]
                CHECK (
                    ([Enabled] = 0 AND [Required] = 0 AND [TargetReleaseId] IS NULL
                        AND [TargetRuntimeVersion] IS NULL AND [ReleaseMessage] IS NULL)
                    OR
                    ([Enabled] = 1 AND [TargetReleaseId] IS NOT NULL
                        AND [TargetRuntimeVersion] IS NOT NULL)
                )
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_MobileOtaPolicy_Environment_Platform'
          AND [object_id] = OBJECT_ID(N'[dbo].[MobileOtaPolicy]')
    )
        CREATE UNIQUE INDEX [UX_MobileOtaPolicy_Environment_Platform]
            ON [dbo].[MobileOtaPolicy]([Environment], [Platform])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[MobileOtaPolicyRevision]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[MobileOtaPolicyRevision] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileOtaPolicyRevision] PRIMARY KEY,
            [PolicyId] uniqueidentifier NOT NULL,
            [Environment] nvarchar(32) NOT NULL,
            [Platform] nvarchar(16) NOT NULL,
            [PolicyVersion] bigint NOT NULL,
            [Operation] nvarchar(16) NOT NULL,
            [SnapshotJson] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_MobileOtaPolicyRevision_IsDeleted] DEFAULT(0),
            CONSTRAINT [FK_MobileOtaPolicyRevision_Policy]
                FOREIGN KEY ([PolicyId]) REFERENCES [dbo].[MobileOtaPolicy]([Id]),
            CONSTRAINT [CK_MobileOtaPolicyRevision_Lane]
                CHECK (
                    [Environment] IN (N'production', N'preview')
                    AND [Platform] IN (N'android', N'ios')
                ),
            CONSTRAINT [CK_MobileOtaPolicyRevision_Version]
                CHECK ([PolicyVersion] > 0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_MobileOtaPolicyRevision_Lane_Version'
          AND [object_id] = OBJECT_ID(N'[dbo].[MobileOtaPolicyRevision]')
    )
        CREATE UNIQUE INDEX [UX_MobileOtaPolicyRevision_Lane_Version]
            ON [dbo].[MobileOtaPolicyRevision]([Environment], [Platform], [PolicyVersion]);

    IF OBJECT_ID(N'[dbo].[TR_MobileOtaPolicyRevision_AppendOnly]', N'TR') IS NULL
        EXEC(N'
            CREATE TRIGGER [dbo].[TR_MobileOtaPolicyRevision_AppendOnly]
            ON [dbo].[MobileOtaPolicyRevision]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51066, ''MobileOtaPolicyRevision is append-only.'', 1;
            END;
        ');

    IF OBJECT_ID(N'[dbo].[PosHandheldUpdatePolicy]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosHandheldUpdatePolicy] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosHandheldUpdatePolicy] PRIMARY KEY,
            [Lane] nvarchar(32) NOT NULL,
            [Enabled] bit NOT NULL,
            [Required] bit NOT NULL,
            [CandidateId] uniqueidentifier NULL,
            [CandidateFingerprint] nvarchar(64) NULL,
            [MinimumSupportedVersion] nvarchar(64) NULL,
            [MinimumSupportedBuildNumber] int NULL,
            [ReleaseMessage] nvarchar(1000) NULL,
            [PolicyVersion] bigint NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosHandheldUpdatePolicy_IsDeleted] DEFAULT(0),
            CONSTRAINT [CK_PosHandheldUpdatePolicy_Lane]
                CHECK ([Lane] IN (
                    N'android-native',
                    N'ios-native',
                    N'android-ota',
                    N'ios-ota'
                )),
            CONSTRAINT [CK_PosHandheldUpdatePolicy_Version]
                CHECK ([PolicyVersion] > 0),
            CONSTRAINT [CK_PosHandheldUpdatePolicy_Candidate]
                CHECK (
                    ([Enabled] = 0 AND [CandidateId] IS NULL AND [CandidateFingerprint] IS NULL)
                    OR
                    ([Enabled] = 1 AND [CandidateId] IS NOT NULL AND [CandidateFingerprint] IS NOT NULL)
                )
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosHandheldUpdatePolicy_Lane'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosHandheldUpdatePolicy]')
    )
        CREATE UNIQUE INDEX [UX_PosHandheldUpdatePolicy_Lane]
            ON [dbo].[PosHandheldUpdatePolicy]([Lane])
            WHERE [IsDeleted] = 0;

    IF OBJECT_ID(N'[dbo].[PosHandheldUpdatePolicyRevision]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[PosHandheldUpdatePolicyRevision] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_PosHandheldUpdatePolicyRevision] PRIMARY KEY,
            [PolicyId] uniqueidentifier NOT NULL,
            [Lane] nvarchar(32) NOT NULL,
            [PolicyVersion] bigint NOT NULL,
            [Action] nvarchar(16) NOT NULL,
            [SnapshotJson] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [UpdatedAt] datetime2 NULL,
            [UpdatedBy] nvarchar(max) NULL,
            [IsDeleted] bit NOT NULL CONSTRAINT [DF_PosHandheldUpdatePolicyRevision_IsDeleted] DEFAULT(0),
            CONSTRAINT [FK_PosHandheldUpdatePolicyRevision_Policy]
                FOREIGN KEY ([PolicyId]) REFERENCES [dbo].[PosHandheldUpdatePolicy]([Id]),
            CONSTRAINT [CK_PosHandheldUpdatePolicyRevision_Lane]
                CHECK ([Lane] IN (
                    N'android-native',
                    N'ios-native',
                    N'android-ota',
                    N'ios-ota'
                )),
            CONSTRAINT [CK_PosHandheldUpdatePolicyRevision_Version]
                CHECK ([PolicyVersion] > 0)
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'UX_PosHandheldUpdatePolicyRevision_Lane_Version'
          AND [object_id] = OBJECT_ID(N'[dbo].[PosHandheldUpdatePolicyRevision]')
    )
        CREATE UNIQUE INDEX [UX_PosHandheldUpdatePolicyRevision_Lane_Version]
            ON [dbo].[PosHandheldUpdatePolicyRevision]([Lane], [PolicyVersion]);

    IF OBJECT_ID(
        N'[dbo].[TR_PosHandheldUpdatePolicyRevision_AppendOnly]',
        N'TR'
    ) IS NULL
        EXEC(N'
            CREATE TRIGGER [dbo].[TR_PosHandheldUpdatePolicyRevision_AppendOnly]
            ON [dbo].[PosHandheldUpdatePolicyRevision]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51064, ''PosHandheldUpdatePolicyRevision is append-only.'', 1;
            END;
        ');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

        await db.Ado.ExecuteCommandAsync(sql);
        logger.LogInformation("Mobile、iPad 与手持 POS 更新策略表检查完成");
    }
}
