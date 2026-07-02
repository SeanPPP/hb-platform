using SqlSugar;

namespace BlazorApp.Api.Services.React;

public static class PaymentTerminalSettingsSchemaMigrator
{
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

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
    [
        EnsureSquareTokenTableSql,
        EnsureSquareTokenEnabledIndexSql,
        EnsureLinklyCredentialTableSql,
        EnsureLinklyEnvironmentColumnSql,
        NormalizeLinklyEnvironmentColumnSql,
        EnsureLinklyCredentialConstraintsSql,
    ];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        foreach (var sql in SqlScriptsForTests)
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }

        logger.LogInformation("POSM 支付终端配置表结构检查完成");
    }
}
