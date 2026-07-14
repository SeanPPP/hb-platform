using SqlSugar;

namespace BlazorApp.Api.Services.React;

public static class EmergencyLoginKeySchemaMigrator
{
    private const string EnsureKeyTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKey]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_EmergencyLoginKey] (
                [KeyId] NVARCHAR(32) NOT NULL CONSTRAINT [PK_POSM_EmergencyLoginKey] PRIMARY KEY,
                [Status] NVARCHAR(16) NOT NULL,
                [PublicKeyPem] NVARCHAR(2048) NOT NULL,
                [PublicKeyFingerprint] CHAR(64) NOT NULL,
                [ProtectedPrivateKey] NVARCHAR(MAX) NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL,
                [CreatedBy] NVARCHAR(128) NOT NULL,
                [CreatedReason] NVARCHAR(200) NOT NULL,
                [ActivatedAtUtc] DATETIME2(7) NULL,
                [ActivatedBy] NVARCHAR(128) NULL,
                [RetiredAtUtc] DATETIME2(7) NULL,
                [RetiredBy] NVARCHAR(128) NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL,
                CONSTRAINT [CK_POSM_EmergencyLoginKey_Status]
                    CHECK ([Status] IN (N'Staged', N'Active', N'Retiring', N'Retired'))
            );
        END;
        """;

    private const string EnsureKeyIndexesSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'UX_POSM_EmergencyLoginKey_OneStaged'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKey]')
        )
        BEGIN
            -- 关键逻辑：过滤唯一索引从数据库层兜住并发生成和激活。
            CREATE UNIQUE INDEX [UX_POSM_EmergencyLoginKey_OneStaged]
            ON [dbo].[POSM_EmergencyLoginKey]([Status])
            WHERE [Status] = N'Staged';
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'UX_POSM_EmergencyLoginKey_OneActive'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKey]')
        )
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_EmergencyLoginKey_OneActive]
            ON [dbo].[POSM_EmergencyLoginKey]([Status])
            WHERE [Status] = N'Active';
        END;
        """;

    private const string EnsureStateTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKeySetState]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_EmergencyLoginKeySetState] (
                [StateId] TINYINT NOT NULL CONSTRAINT [PK_POSM_EmergencyLoginKeySetState] PRIMARY KEY,
                [Version] BIGINT NOT NULL,
                [ActiveKeyId] NVARCHAR(32) NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL,
                CONSTRAINT [CK_POSM_EmergencyLoginKeySetState_Singleton] CHECK ([StateId] = 1)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM [dbo].[POSM_EmergencyLoginKeySetState] WHERE [StateId] = 1)
        BEGIN
            INSERT INTO [dbo].[POSM_EmergencyLoginKeySetState]
                ([StateId], [Version], [ActiveKeyId], [UpdatedAtUtc])
            VALUES (1, 0, NULL, SYSUTCDATETIME());
        END;
        """;

    private const string EnsureDeviceSyncTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKeyDeviceSync]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_EmergencyLoginKeyDeviceSync] (
                [DeviceRegistrationId] INT NOT NULL,
                [KeySetVersion] BIGINT NOT NULL,
                [KeyId] NVARCHAR(32) NOT NULL,
                [AcknowledgedAtUtc] DATETIME2(7) NOT NULL,
                [LastSeenAtUtc] DATETIME2(7) NULL,
                CONSTRAINT [PK_POSM_EmergencyLoginKeyDeviceSync]
                    PRIMARY KEY ([DeviceRegistrationId], [KeySetVersion])
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_POSM_EmergencyLoginKeyDeviceSync_VersionKey'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKeyDeviceSync]')
        )
        BEGIN
            CREATE INDEX [IX_POSM_EmergencyLoginKeyDeviceSync_VersionKey]
            ON [dbo].[POSM_EmergencyLoginKeyDeviceSync]([KeySetVersion], [KeyId]);
        END;
        """;

    private const string EnsureAuditTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_EmergencyLoginKeyAudit]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_EmergencyLoginKeyAudit] (
                [AuditId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POSM_EmergencyLoginKeyAudit] PRIMARY KEY,
                [KeyId] NVARCHAR(32) NULL,
                [Action] NVARCHAR(32) NOT NULL,
                [Actor] NVARCHAR(128) NOT NULL,
                [Reason] NVARCHAR(200) NOT NULL,
                [ExpectedVersion] BIGINT NOT NULL,
                [ResultVersion] BIGINT NOT NULL,
                [Details] NVARCHAR(MAX) NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL
            );
        END;
        """;

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
    [
        EnsureKeyTableSql,
        EnsureKeyIndexesSql,
        EnsureStateTableSql,
        EnsureDeviceSyncTableSql,
        EnsureAuditTableSql,
    ];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        foreach (var sql in SqlScriptsForTests)
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }

        logger.LogInformation("POSM 紧急登录中央密钥表结构检查完成");
    }
}
