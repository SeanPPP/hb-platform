using SqlSugar;

namespace BlazorApp.Api.Services.React;

public static class PaymentTerminalSettingsSchemaMigrator
{
    internal const string SchemaLockSql = """
        SET XACT_ABORT ON;
        DECLARE @SchemaLockResult INT;
        EXEC @SchemaLockResult = sys.sp_getapplock
            @Resource = N'Hbpos.LinklyCloud.Schema.v2',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 60000;
        IF @SchemaLockResult < 0
            THROW 51000, 'Could not acquire the shared Linkly Cloud schema lock.', 1;
        """;

    private const string EnsureSquareTokenTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_SquareToken]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_SquareToken] (
                [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POSM_SquareToken] PRIMARY KEY,
                [Environment] NVARCHAR(32) NOT NULL,
                [AccessToken] NVARCHAR(2048) NOT NULL,
                [IsEnabled] BIT NOT NULL CONSTRAINT [DF_POSM_SquareToken_IsEnabled] DEFAULT (0),
                [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_POSM_SquareToken_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                [UpdatedBy] NVARCHAR(128) NULL,
                CONSTRAINT [CK_POSM_SquareToken_Environment] CHECK ([Environment] IN (N'Production', N'Sandbox'))
            );
        END;
        """;

    private const string EnsureSquareTokenEnabledIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'UX_POSM_SquareToken_Environment_Enabled'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_SquareToken]')
        )
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_SquareToken_Environment_Enabled]
            ON [dbo].[POSM_SquareToken]([Environment])
            WHERE [IsEnabled] = 1;
        END;
        """;

    private const string EnsureLinklyCredentialTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudCredential]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklyCloudCredential] (
                [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POSM_LinklyCloudCredential] PRIMARY KEY,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [Environment] NVARCHAR(32) NOT NULL CONSTRAINT [DF_POSM_LinklyCloudCredential_Environment] DEFAULT (N'Production'),
                [Username] NVARCHAR(256) NOT NULL,
                [Password] NVARCHAR(256) NOT NULL,
                [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_POSM_LinklyCloudCredential_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                [UpdatedBy] NVARCHAR(128) NULL,
                CONSTRAINT [CK_POSM_LinklyCloudCredential_Environment] CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [UX_POSM_LinklyCloudCredential_StoreCode_Environment] UNIQUE ([StoreCode], [Environment])
            );
        END;
        """;

    private const string EnsureLinklyEnvironmentColumnSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudCredential]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_LinklyCloudCredential', N'Environment') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential]
                    ADD [Environment] NVARCHAR(32) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudCredential_Environment] DEFAULT (N'Production') WITH VALUES;
            END
        END;
        """;

    private const string NormalizeLinklyEnvironmentColumnSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudCredential]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_LinklyCloudCredential', N'Environment') IS NOT NULL
        BEGIN
            UPDATE [dbo].[POSM_LinklyCloudCredential]
            SET [Environment] = N'Production'
            WHERE NULLIF(LTRIM(RTRIM([Environment])), N'') IS NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudCredential]', N'U')
                  AND [name] = N'Environment'
                  AND [is_nullable] = 1)
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential]
                    ALTER COLUMN [Environment] NVARCHAR(32) NOT NULL;
            END;
        END;
        """;

    private const string EnsureLinklyCredentialConstraintsSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudCredential]', N'U') IS NOT NULL
        BEGIN
            IF OBJECT_ID(N'[dbo].[DF_POSM_LinklyCloudCredential_Environment]', N'D') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential]
                    ADD CONSTRAINT [DF_POSM_LinklyCloudCredential_Environment]
                    DEFAULT (N'Production') FOR [Environment];
            END;

            IF OBJECT_ID(N'[dbo].[UX_POSM_LinklyCloudCredential_StoreCode]', N'UQ') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential]
                    DROP CONSTRAINT [UX_POSM_LinklyCloudCredential_StoreCode];
            END;

            IF OBJECT_ID(N'[dbo].[CK_POSM_LinklyCloudCredential_Environment]', N'C') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential] WITH CHECK
                    ADD CONSTRAINT [CK_POSM_LinklyCloudCredential_Environment]
                    CHECK ([Environment] IN (N'Production', N'Sandbox'));
            END;

            IF OBJECT_ID(N'[dbo].[UX_POSM_LinklyCloudCredential_StoreCode_Environment]', N'UQ') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_LinklyCloudCredential]
                    ADD CONSTRAINT [UX_POSM_LinklyCloudCredential_StoreCode_Environment]
                    UNIQUE ([StoreCode], [Environment]);
            END;
        END;
        """;

    private const string EnsureLinklyBackendSessionDependencySql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudBackendSession]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklyCloudBackendSession] (
                [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POSM_LinklyCloudBackendSession] PRIMARY KEY,
                [Environment] NVARCHAR(32) NOT NULL,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [DeviceCode] NVARCHAR(64) NOT NULL,
                [TerminalId] UNIQUEIDENTIFIER NULL,
                [SessionId] NVARCHAR(64) NOT NULL,
                [Status] NVARCHAR(32) NOT NULL,
                [TxnRef] NVARCHAR(16) NULL,
                [TransactionSuccess] BIT NULL,
                [OperationType] NVARCHAR(32) NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_OperationType] DEFAULT (N'Transaction'),
                [OperationSuccess] BIT NULL,
                [SettlementData] NVARCHAR(MAX) NULL,
                [SettlementReceiptTexts] NVARCHAR(MAX) NULL,
                [ResponseCode] NVARCHAR(32) NULL,
                [ResponseText] NVARCHAR(512) NULL,
                [RecoveryAction] NVARCHAR(64) NULL,
                [DisplayText] NVARCHAR(512) NULL,
                [DisplayLines] NVARCHAR(MAX) NULL,
                [CancelKeyFlag] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_CancelKeyFlag] DEFAULT (0),
                [OKKeyFlag] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_OKKeyFlag] DEFAULT (0),
                [AcceptYesKeyFlag] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_AcceptYesKeyFlag] DEFAULT (0),
                [DeclineNoKeyFlag] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_DeclineNoKeyFlag] DEFAULT (0),
                [AuthoriseKeyFlag] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_AuthoriseKeyFlag] DEFAULT (0),
                [InputType] NVARCHAR(64) NULL,
                [GraphicCode] NVARCHAR(64) NULL,
                [ReceiptText] NVARCHAR(MAX) NULL,
                [RecoveryCount] INT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_RecoveryCount] DEFAULT (0),
                [ReceiptPrintedAt] DATETIME2(7) NULL,
                [ClientAcknowledgedAt] DATETIME2(7) NULL,
                [LastHttpStatus] INT NULL,
                [IsActive] BIT NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_IsActive] DEFAULT (0),
                [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_POSM_LinklyCloudBackendSession_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [CK_POSM_LinklyCloudBackendSession_Environment] CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [UX_POSM_LinklyCloudBackendSession_Scope] UNIQUE ([Environment], [StoreCode], [DeviceCode], [SessionId])
            );
        END;

        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudBackendSession]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_LinklyCloudBackendSession', N'TerminalId') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudBackendSession] ADD [TerminalId] UNIQUEIDENTIFIER NULL;

            IF COL_LENGTH(N'dbo.POSM_LinklyCloudBackendSession', N'ClientAcknowledgedAt') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudBackendSession] ADD [ClientAcknowledgedAt] DATETIME2(7) NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudBackendSession]', N'U')
                  AND [name] = N'IX_POSM_LinklyCloudBackendSession_DeviceRecovery')
            BEGIN
                CREATE INDEX [IX_POSM_LinklyCloudBackendSession_DeviceRecovery]
                    ON [dbo].[POSM_LinklyCloudBackendSession]
                        ([Environment], [StoreCode], [DeviceCode], [IsActive], [Status], [ClientAcknowledgedAt])
                    INCLUDE ([UpdatedAt]);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudBackendSession]', N'U')
                  AND [name] = N'IX_POSM_LinklyCloudBackendSession_TerminalRecovery')
            BEGIN
                CREATE INDEX [IX_POSM_LinklyCloudBackendSession_TerminalRecovery]
                    ON [dbo].[POSM_LinklyCloudBackendSession]
                        ([Environment], [StoreCode], [TerminalId], [IsActive], [Status], [ClientAcknowledgedAt])
                    INCLUDE ([UpdatedAt])
                    WHERE [TerminalId] IS NOT NULL;
            END;
        END;
        """;

    private const string EnsureLinklyTerminalTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklyCloudTerminal] (
                [TerminalId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_POSM_LinklyCloudTerminal] PRIMARY KEY,
                [Environment] NVARCHAR(32) NOT NULL,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [LaneNo] INT NOT NULL,
                [DisplayName] NVARCHAR(128) NOT NULL,
                [Username] NVARCHAR(128) NOT NULL,
                [Password] NVARCHAR(2048) NOT NULL,
                [Secret] NVARCHAR(2048) NULL,
                [CredentialProtectionVersion] TINYINT NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudTerminal_CredentialProtectionVersion] DEFAULT (1),
                [PosId] NVARCHAR(64) NULL,
                [PairingState] NVARCHAR(32) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudTerminal_PairingState] DEFAULT (N'Unpaired'),
                [PairingAttemptId] UNIQUEIDENTIFIER NULL,
                [PairingLeaseExpiresAt] DATETIME2(7) NULL,
                [LastHealthStatus] NVARCHAR(32) NULL,
                [LastHealthAt] DATETIME2(7) NULL,
                [CreatedAt] DATETIME2(7) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudTerminal_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                [UpdatedAt] DATETIME2(7) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudTerminal_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                [CreatedBy] NVARCHAR(128) NULL,
                [UpdatedBy] NVARCHAR(128) NULL,
                CONSTRAINT [CK_POSM_LinklyCloudTerminal_Environment]
                    CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [CK_POSM_LinklyCloudTerminal_PairingState]
                    CHECK ([PairingState] IN (N'Unpaired', N'Ready', N'Unknown', N'NeedsRepair')),
                CONSTRAINT [CK_POSM_LinklyCloudTerminal_CredentialProtectionVersion]
                    CHECK ([CredentialProtectionVersion] IN (0, 1)),
                CONSTRAINT [UX_POSM_LinklyCloudTerminal_Scope_LaneNo]
                    UNIQUE ([Environment], [StoreCode], [LaneNo]),
                CONSTRAINT [UX_POSM_LinklyCloudTerminal_Scope_Username]
                    UNIQUE ([Environment], [StoreCode], [Username]),
                CONSTRAINT [UX_POSM_LinklyCloudTerminal_Scope_DisplayName]
                    UNIQUE ([Environment], [StoreCode], [DisplayName])
            );
        END;
        """;

    private const string EnsureLinklyTerminalLeaseColumnsSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_LinklyCloudTerminal', N'PairingAttemptId') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal] ADD [PairingAttemptId] UNIQUEIDENTIFIER NULL;

            IF COL_LENGTH(N'dbo.POSM_LinklyCloudTerminal', N'PairingLeaseExpiresAt') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal] ADD [PairingLeaseExpiresAt] DATETIME2(7) NULL;
        END;
        """;

    private const string EnsureLinklyTerminalCredentialProtectionSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]', N'U') IS NOT NULL
        BEGIN
            -- 历史行一律标为 version 0；禁止在 SQL 迁移中读取、复制或自动重写可能的明文凭据。
            IF COL_LENGTH(N'dbo.POSM_LinklyCloudTerminal', N'CredentialProtectionVersion') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal]
                    ADD [CredentialProtectionVersion] TINYINT NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudTerminal_CredentialProtectionVersion]
                    DEFAULT (0) WITH VALUES;

            IF COL_LENGTH(N'dbo.POSM_LinklyCloudTerminal', N'Password') < 4096
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal]
                    ALTER COLUMN [Password] NVARCHAR(2048) NOT NULL;

            IF COL_LENGTH(N'dbo.POSM_LinklyCloudTerminal', N'Secret') < 4096
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal]
                    ALTER COLUMN [Secret] NVARCHAR(2048) NULL;

            IF OBJECT_ID(N'[dbo].[CK_POSM_LinklyCloudTerminal_CredentialProtectionVersion]', N'C') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudTerminal] WITH CHECK
                    ADD CONSTRAINT [CK_POSM_LinklyCloudTerminal_CredentialProtectionVersion]
                    CHECK ([CredentialProtectionVersion] IN (0, 1));
        END;
        """;

    private const string EnsureLinklyTerminalIndexesSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]', N'U') IS NOT NULL
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_POSM_LinklyCloudTerminal_Scope_LaneNo'
                  AND [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]'))
            BEGIN
                CREATE UNIQUE INDEX [UX_POSM_LinklyCloudTerminal_Scope_LaneNo]
                ON [dbo].[POSM_LinklyCloudTerminal]([Environment], [StoreCode], [LaneNo]);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_POSM_LinklyCloudTerminal_Scope_Username'
                  AND [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]'))
            BEGIN
                CREATE UNIQUE INDEX [UX_POSM_LinklyCloudTerminal_Scope_Username]
                ON [dbo].[POSM_LinklyCloudTerminal]([Environment], [StoreCode], [Username]);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_POSM_LinklyCloudTerminal_Scope_DisplayName'
                  AND [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudTerminal]'))
            BEGIN
                CREATE UNIQUE INDEX [UX_POSM_LinklyCloudTerminal_Scope_DisplayName]
                ON [dbo].[POSM_LinklyCloudTerminal]([Environment], [StoreCode], [DisplayName]);
            END;
        END;
        """;

    private const string EnsureLinklyDeviceSelectionTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudDeviceSelection]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklyCloudDeviceSelection] (
                [Environment] NVARCHAR(32) NOT NULL,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [DeviceCode] NVARCHAR(64) NOT NULL,
                [TerminalId] UNIQUEIDENTIFIER NOT NULL,
                [Revision] BIGINT NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudDeviceSelection_Revision] DEFAULT (1),
                [UpdatedAt] DATETIME2(7) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudDeviceSelection_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                [UpdatedBy] NVARCHAR(128) NULL,
                CONSTRAINT [PK_POSM_LinklyCloudDeviceSelection]
                    PRIMARY KEY ([Environment], [StoreCode], [DeviceCode]),
                CONSTRAINT [CK_POSM_LinklyCloudDeviceSelection_Environment]
                    CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [FK_POSM_LinklyCloudDeviceSelection_Terminal]
                    FOREIGN KEY ([TerminalId]) REFERENCES [dbo].[POSM_LinklyCloudTerminal]([TerminalId])
            );
        END;
        """;

    private const string EnsureLinklyDeviceSelectionTerminalUniqueIndexSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudDeviceSelection]', N'U') IS NOT NULL
           AND NOT EXISTS (
               SELECT 1 FROM sys.indexes
               WHERE [name] = N'UX_POSM_LinklyCloudDeviceSelection_Scope_Terminal'
                 AND [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklyCloudDeviceSelection]')
           )
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM [dbo].[POSM_LinklyCloudDeviceSelection]
                GROUP BY [Environment], [StoreCode], [TerminalId]
                HAVING COUNT(*) > 1
            )
                THROW 51004, 'Duplicate Linkly Cloud terminal assignments must be resolved before migration.', 1;

            CREATE UNIQUE INDEX [UX_POSM_LinklyCloudDeviceSelection_Scope_Terminal]
                ON [dbo].[POSM_LinklyCloudDeviceSelection] ([Environment], [StoreCode], [TerminalId]);
        END;
        """;

    private const string EnsureLinklyConfigurationModeTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudConfigurationMode]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklyCloudConfigurationMode] (
                [Environment] NVARCHAR(32) NOT NULL,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [Mode] NVARCHAR(16) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudConfigurationMode_Mode] DEFAULT (N'Legacy'),
                [LegacyPairingAttemptId] UNIQUEIDENTIFIER NULL,
                [LegacyPairingLeaseExpiresAt] DATETIME2(7) NULL,
                [UpdatedAt] DATETIME2(7) NOT NULL
                    CONSTRAINT [DF_POSM_LinklyCloudConfigurationMode_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                [UpdatedBy] NVARCHAR(128) NULL,
                CONSTRAINT [PK_POSM_LinklyCloudConfigurationMode]
                    PRIMARY KEY ([Environment], [StoreCode]),
                CONSTRAINT [CK_POSM_LinklyCloudConfigurationMode_Environment]
                    CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [CK_POSM_LinklyCloudConfigurationMode_Mode]
                    CHECK ([Mode] IN (N'Legacy', N'Draft', N'Active'))
            );
        END;
        """;

    private const string EnsureLinklyConfigurationModeLegacyPairingLeaseColumnsSql = """
        IF OBJECT_ID(N'[dbo].[POSM_LinklyCloudConfigurationMode]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_LinklyCloudConfigurationMode', N'LegacyPairingAttemptId') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudConfigurationMode]
                    ADD [LegacyPairingAttemptId] UNIQUEIDENTIFIER NULL;

            IF COL_LENGTH(N'dbo.POSM_LinklyCloudConfigurationMode', N'LegacyPairingLeaseExpiresAt') IS NULL
                ALTER TABLE [dbo].[POSM_LinklyCloudConfigurationMode]
                    ADD [LegacyPairingLeaseExpiresAt] DATETIME2(7) NULL;
        END;
        """;

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
    [
        EnsureSquareTokenTableSql,
        EnsureSquareTokenEnabledIndexSql,
        EnsureLinklyCredentialTableSql,
        EnsureLinklyEnvironmentColumnSql,
        NormalizeLinklyEnvironmentColumnSql,
        EnsureLinklyCredentialConstraintsSql,
        EnsureLinklyBackendSessionDependencySql,
        EnsureLinklyTerminalTableSql,
        EnsureLinklyTerminalLeaseColumnsSql,
        EnsureLinklyTerminalCredentialProtectionSql,
        EnsureLinklyTerminalIndexesSql,
        EnsureLinklyDeviceSelectionTableSql,
        EnsureLinklyDeviceSelectionTerminalUniqueIndexSql,
        EnsureLinklyConfigurationModeTableSql,
        EnsureLinklyConfigurationModeLegacyPairingLeaseColumnsSql,
    ];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        await db.Ado.BeginTranAsync();
        try
        {
            // Web 与 POS API 都会确保 Linkly 表；共用事务级应用锁，保证首次并发启动时只有一个建表者。
            await db.Ado.ExecuteCommandAsync(SchemaLockSql);
            foreach (var sql in SqlScriptsForTests)
            {
                await db.Ado.ExecuteCommandAsync(sql);
            }

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        logger.LogInformation("POSM 支付终端配置表结构检查完成");
    }
}
