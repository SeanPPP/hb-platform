using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>
/// 网站会话向浏览器扩展授权的一次性 PKCE grant 表迁移。
/// </summary>
public static class BrowserExtensionSessionGrantSchemaMigrator
{
    private const string EnsureTableSql = """
        SET XACT_ABORT ON;
        BEGIN TRY
            BEGIN TRANSACTION;
            DECLARE @BrowserExtensionGrantSchemaLockResult int;
            EXEC @BrowserExtensionGrantSchemaLockResult = sys.sp_getapplock
                @Resource = N'BrowserExtensionSessionGrant_Schema_Initialization',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 30000;
            IF @BrowserExtensionGrantSchemaLockResult < 0
                THROW 51063, 'Unable to acquire browser extension grant schema lock.', 1;

            IF OBJECT_ID(N'[dbo].[BrowserExtensionSessionGrant]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[BrowserExtensionSessionGrant] (
                    [GrantId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_BrowserExtensionSessionGrant] PRIMARY KEY,
                    [CodeHash] NVARCHAR(64) NOT NULL,
                    [CodeChallenge] NVARCHAR(43) NOT NULL,
                    [State] NVARCHAR(128) NOT NULL,
                    [ParentSessionId] NVARCHAR(100) NOT NULL,
                    [UserGuid] NVARCHAR(100) NOT NULL,
                    [ClientId] NVARCHAR(64) NOT NULL,
                    [IssuedAtUtc] DATETIME2(7) NOT NULL,
                    [ExpiresAtUtc] DATETIME2(7) NOT NULL,
                    [ConsumedAtUtc] DATETIME2(7) NULL
                );
            END;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;

    private const string EnsureCodeHashIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'UX_BrowserExtensionSessionGrant_CodeHash'
              AND object_id = OBJECT_ID(N'[dbo].[BrowserExtensionSessionGrant]')
        )
        BEGIN
            CREATE UNIQUE INDEX [UX_BrowserExtensionSessionGrant_CodeHash]
            ON [dbo].[BrowserExtensionSessionGrant]([CodeHash]);
        END;
        """;

    private const string EnsureExpiresIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_BrowserExtensionSessionGrant_ExpiresAtUtc'
              AND object_id = OBJECT_ID(N'[dbo].[BrowserExtensionSessionGrant]')
        )
        BEGIN
            CREATE INDEX [IX_BrowserExtensionSessionGrant_ExpiresAtUtc]
            ON [dbo].[BrowserExtensionSessionGrant]([ExpiresAtUtc]);
        END;
        """;

    private const string EnsureParentSessionIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_BrowserExtensionSessionGrant_ParentSessionId'
              AND object_id = OBJECT_ID(N'[dbo].[BrowserExtensionSessionGrant]')
        )
        BEGIN
            CREATE INDEX [IX_BrowserExtensionSessionGrant_ParentSessionId]
            ON [dbo].[BrowserExtensionSessionGrant]([ParentSessionId], [ExpiresAtUtc]);
        END;
        """;

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
        [EnsureTableSql, EnsureCodeHashIndexSql, EnsureExpiresIndexSql, EnsureParentSessionIndexSql];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        foreach (var sql in SqlScriptsForTests)
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }

        logger.LogInformation("浏览器扩展一次性会话授权表结构检查完成");
    }
}
