using SqlSugar;

namespace BlazorApp.Api.Services.React;

public static class EmergencyLoginGrantSchemaMigrator
{
    private const string EnsureTableSql = """
        IF OBJECT_ID(N'[dbo].[POSM_EmergencyLoginGrant]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_EmergencyLoginGrant] (
                [GrantId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_POSM_EmergencyLoginGrant] PRIMARY KEY,
                [StoreCode] NVARCHAR(50) NOT NULL,
                [BusinessDate] DATE NOT NULL,
                [KeyId] NVARCHAR(32) NOT NULL,
                [PermissionProfile] NVARCHAR(32) NOT NULL,
                [IssuedBy] NVARCHAR(128) NOT NULL,
                [IssuedReason] NVARCHAR(200) NOT NULL,
                [IssuedAtUtc] DATETIME2(7) NOT NULL,
                [NotBeforeUtc] DATETIME2(7) NOT NULL,
                [ExpiresAtUtc] DATETIME2(7) NOT NULL,
                [RevokedAtUtc] DATETIME2(7) NULL,
                [RevokedBy] NVARCHAR(128) NULL,
                [RevokedReason] NVARCHAR(200) NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL
            );
        END;
        """;

    private const string EnsureActiveGrantIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'UX_POSM_EmergencyLoginGrant_StoreDate_Active'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_EmergencyLoginGrant]')
        )
        BEGIN
            -- 关键逻辑：数据库约束兜住并发签发，同门店同一业务日只能有一张未撤销授权。
            CREATE UNIQUE INDEX [UX_POSM_EmergencyLoginGrant_StoreDate_Active]
            ON [dbo].[POSM_EmergencyLoginGrant]([StoreCode], [BusinessDate])
            WHERE [RevokedAtUtc] IS NULL;
        END;
        """;

    private const string EnsureExpiresIndexSql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_POSM_EmergencyLoginGrant_ExpiresAtUtc'
              AND object_id = OBJECT_ID(N'[dbo].[POSM_EmergencyLoginGrant]')
        )
        BEGIN
            CREATE INDEX [IX_POSM_EmergencyLoginGrant_ExpiresAtUtc]
            ON [dbo].[POSM_EmergencyLoginGrant]([ExpiresAtUtc]);
        END;
        """;

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
        [EnsureTableSql, EnsureActiveGrantIndexSql, EnsureExpiresIndexSql];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        foreach (var sql in SqlScriptsForTests)
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }

        logger.LogInformation("POSM 紧急登录授权摘要表结构检查完成");
    }
}
