using SqlSugar;

namespace BlazorApp.Api.Services.Logging;

/// <summary>
/// 中心日志显式结构升级。已有表默认不会由 CodeFirst 自动同步，因此新增列和索引必须在启动时幂等补齐。
/// </summary>
public static class ApplicationLogSchemaMigrator
{
    private const string TableName = "ApplicationLog";

    public static Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        if (!TableExists(db))
        {
            logger.LogWarning("中心日志表不存在，跳过增量结构升级；后续建表流程会按最新模型创建");
            return Task.CompletedTask;
        }

        switch (db.CurrentConnectionConfig.DbType)
        {
            case DbType.SqlServer:
                db.Ado.ExecuteCommand(SqlServerMigrationSql);
                break;
            case DbType.PostgreSQL:
                db.Ado.ExecuteCommand(PostgreSqlMigrationSql);
                break;
            case DbType.Sqlite:
                EnsureSqliteSchema(db);
                break;
            default:
                throw new NotSupportedException(
                    $"中心日志结构升级暂不支持数据库类型 {db.CurrentConnectionConfig.DbType}"
                );
        }

        logger.LogInformation("中心日志 WPF 维度列与幂等索引已确认");
        return Task.CompletedTask;
    }

    private static bool TableExists(ISqlSugarClient db)
    {
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            // SqlSugar 的 DbMaintenance 元数据会受同进程其他客户端缓存影响；SQLite 直接查系统表更可靠。
            return db.Ado.GetInt(
                "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
                new SugarParameter("@tableName", TableName)
            ) > 0;
        }

        return db.DbMaintenance.IsAnyTable(TableName);
    }

    private static void EnsureSqliteSchema(ISqlSugarClient db)
    {
        // SqlSugar 的列元数据在同一客户端内可能缓存；直接读取 PRAGMA 才能保证重复执行看到最新结构。
        var existingColumns = db
            .Ado.GetDataTable($"PRAGMA table_info([{TableName}])")
            .Rows.Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]) ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientEventId"] = "TEXT NULL",
            ["StoreCode"] = "TEXT NULL",
            ["DeviceCode"] = "TEXT NULL",
            ["AppVersion"] = "TEXT NULL",
        };

        foreach (var column in columns.Where(column => !existingColumns.Contains(column.Key)))
            db.Ado.ExecuteCommand($"ALTER TABLE [{TableName}] ADD COLUMN [{column.Key}] {column.Value}");

        db.Ado.ExecuteCommand(
            "CREATE UNIQUE INDEX IF NOT EXISTS [IX_ApplicationLog_ProjectCode_ClientEventId] "
                + "ON [ApplicationLog] ([ProjectCode], [ClientEventId]) WHERE [ClientEventId] IS NOT NULL"
        );
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS [IX_ApplicationLog_StoreCode_TimestampUtc] "
                + "ON [ApplicationLog] ([StoreCode], [TimestampUtc])"
        );
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS [IX_ApplicationLog_DeviceCode_TimestampUtc] "
                + "ON [ApplicationLog] ([DeviceCode], [TimestampUtc])"
        );
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS [IX_ApplicationLog_InstanceId] "
                + "ON [ApplicationLog] ([InstanceId])"
        );
    }

    private const string SqlServerMigrationSql = """
        IF COL_LENGTH(N'dbo.ApplicationLog', N'ClientEventId') IS NULL
            ALTER TABLE [dbo].[ApplicationLog] ADD [ClientEventId] uniqueidentifier NULL;
        IF COL_LENGTH(N'dbo.ApplicationLog', N'StoreCode') IS NULL
            ALTER TABLE [dbo].[ApplicationLog] ADD [StoreCode] nvarchar(80) NULL;
        IF COL_LENGTH(N'dbo.ApplicationLog', N'DeviceCode') IS NULL
            ALTER TABLE [dbo].[ApplicationLog] ADD [DeviceCode] nvarchar(120) NULL;
        IF COL_LENGTH(N'dbo.ApplicationLog', N'AppVersion') IS NULL
            ALTER TABLE [dbo].[ApplicationLog] ADD [AppVersion] nvarchar(60) NULL;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_ApplicationLog_ProjectCode_ClientEventId'
              AND [object_id] = OBJECT_ID(N'dbo.ApplicationLog')
        )
            CREATE UNIQUE INDEX [IX_ApplicationLog_ProjectCode_ClientEventId]
            ON [dbo].[ApplicationLog] ([ProjectCode], [ClientEventId])
            WHERE [ClientEventId] IS NOT NULL;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_ApplicationLog_StoreCode_TimestampUtc'
              AND [object_id] = OBJECT_ID(N'dbo.ApplicationLog')
        )
            CREATE INDEX [IX_ApplicationLog_StoreCode_TimestampUtc]
            ON [dbo].[ApplicationLog] ([StoreCode], [TimestampUtc]);

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_ApplicationLog_DeviceCode_TimestampUtc'
              AND [object_id] = OBJECT_ID(N'dbo.ApplicationLog')
        )
            CREATE INDEX [IX_ApplicationLog_DeviceCode_TimestampUtc]
            ON [dbo].[ApplicationLog] ([DeviceCode], [TimestampUtc]);

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_ApplicationLog_InstanceId'
              AND [object_id] = OBJECT_ID(N'dbo.ApplicationLog')
        )
            CREATE INDEX [IX_ApplicationLog_InstanceId]
            ON [dbo].[ApplicationLog] ([InstanceId]);
        """;

    private const string PostgreSqlMigrationSql = """
        ALTER TABLE "ApplicationLog" ADD COLUMN IF NOT EXISTS "ClientEventId" uuid NULL;
        ALTER TABLE "ApplicationLog" ADD COLUMN IF NOT EXISTS "StoreCode" varchar(80) NULL;
        ALTER TABLE "ApplicationLog" ADD COLUMN IF NOT EXISTS "DeviceCode" varchar(120) NULL;
        ALTER TABLE "ApplicationLog" ADD COLUMN IF NOT EXISTS "AppVersion" varchar(60) NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ApplicationLog_ProjectCode_ClientEventId"
        ON "ApplicationLog" ("ProjectCode", "ClientEventId")
        WHERE "ClientEventId" IS NOT NULL;

        CREATE INDEX IF NOT EXISTS "IX_ApplicationLog_StoreCode_TimestampUtc"
        ON "ApplicationLog" ("StoreCode", "TimestampUtc");

        CREATE INDEX IF NOT EXISTS "IX_ApplicationLog_DeviceCode_TimestampUtc"
        ON "ApplicationLog" ("DeviceCode", "TimestampUtc");

        CREATE INDEX IF NOT EXISTS "IX_ApplicationLog_InstanceId"
        ON "ApplicationLog" ("InstanceId");
        """;
}
