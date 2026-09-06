using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>
/// 商品 HQ 同步 outbox 的独立、可重复执行 schema 迁移。
/// </summary>
public static class ProductHqSyncOutboxSchemaMigrator
{
    internal const string SqlServerApplySql = """
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @ProductHqOutboxSchemaLockResult int;
EXEC @ProductHqOutboxSchemaLockResult = sys.sp_getapplock
    @Resource = N'ProductHqSyncOutbox_Schema_Initialization',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 30000;
IF @ProductHqOutboxSchemaLockResult < 0
    THROW 51070, N'Unable to acquire ProductHqSyncOutbox schema lock.', 1;

IF OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProductHqSyncOutbox] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ProductHqSyncOutbox] PRIMARY KEY,
        [OperationKey] nvarchar(200) NOT NULL,
        [OperationKind] nvarchar(80) NOT NULL,
        [ProductCode] nvarchar(100) NOT NULL,
        [ScopeKey] nvarchar(600) NOT NULL,
        [TargetStoreCodesJson] nvarchar(max) NOT NULL,
        [AuthorizedStoreCodesJson] nvarchar(max) NOT NULL,
        [FieldMaskJson] nvarchar(max) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [TombstonesJson] nvarchar(max) NOT NULL,
        [Source] nvarchar(100) NOT NULL,
        [RequestedByUserGuid] nvarchar(80) NULL,
        [RequestedByDeviceId] nvarchar(200) NULL,
        [Status] nvarchar(30) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [AttemptCount] int NOT NULL CONSTRAINT [DF_ProductHqSyncOutbox_AttemptCount] DEFAULT(0),
        [NextAttemptAtUtc] datetime2 NOT NULL,
        [LeaseOwner] nvarchar(200) NULL,
        [LeaseToken] uniqueidentifier NULL,
        [LeaseExpiresAtUtc] datetime2 NULL,
        [LastAttemptAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [LastErrorCode] nvarchar(120) NULL,
        [LastErrorMessage] nvarchar(500) NULL,
        [SupersededById] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ProductHqSyncOutbox_CreatedAt] DEFAULT(SYSUTCDATETIME()),
        [CreatedBy] nvarchar(255) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(255) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ProductHqSyncOutbox_IsDeleted] DEFAULT(0),
        CONSTRAINT [CK_ProductHqSyncOutbox_Status] CHECK (
            [Status] IN (N'pending', N'processing', N'retrying', N'succeeded', N'blocked', N'superseded')
        )
    );
END;

IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'RequestedByUserGuid') IS NULL
    ALTER TABLE [dbo].[ProductHqSyncOutbox] ADD [RequestedByUserGuid] nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'RequestedByDeviceId') IS NULL
    ALTER TABLE [dbo].[ProductHqSyncOutbox] ADD [RequestedByDeviceId] nvarchar(200) NULL;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'AuthorizedStoreCodesJson') IS NULL
    ALTER TABLE [dbo].[ProductHqSyncOutbox]
        ADD [AuthorizedStoreCodesJson] nvarchar(max) NOT NULL
            CONSTRAINT [DF_ProductHqSyncOutbox_AuthorizedStoreCodesJson] DEFAULT(N'null');

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_ProductHqSyncOutbox_OperationKey'
      AND [object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
)
    CREATE UNIQUE INDEX [UX_ProductHqSyncOutbox_OperationKey]
        ON [dbo].[ProductHqSyncOutbox]([OperationKey]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_ProductHqSyncOutbox_Due'
      AND [object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
)
    CREATE INDEX [IX_ProductHqSyncOutbox_Due]
        ON [dbo].[ProductHqSyncOutbox]([Status], [NextAttemptAtUtc], [LeaseExpiresAtUtc], [CreatedAt]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_ProductHqSyncOutbox_ProductScope'
      AND [object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
)
    CREATE INDEX [IX_ProductHqSyncOutbox_ProductScope]
        ON [dbo].[ProductHqSyncOutbox]([ProductCode], [ScopeKey], [OccurredAtUtc] DESC);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    internal const string SqlServerVerifySql = """
IF OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U') IS NULL
    THROW 51071, N'ProductHqSyncOutbox table is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'OperationKey') IS NULL
    THROW 51072, N'ProductHqSyncOutbox.OperationKey is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'PayloadJson') IS NULL
    THROW 51073, N'ProductHqSyncOutbox.PayloadJson is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'LeaseToken') IS NULL
    THROW 51074, N'ProductHqSyncOutbox.LeaseToken is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'SupersededById') IS NULL
    THROW 51075, N'ProductHqSyncOutbox.SupersededById is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'RequestedByUserGuid') IS NULL
    THROW 51079, N'ProductHqSyncOutbox.RequestedByUserGuid is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'RequestedByDeviceId') IS NULL
    THROW 51080, N'ProductHqSyncOutbox.RequestedByDeviceId is missing.', 1;
IF COL_LENGTH(N'dbo.ProductHqSyncOutbox', N'AuthorizedStoreCodesJson') IS NULL
    THROW 51081, N'ProductHqSyncOutbox.AuthorizedStoreCodesJson is missing.', 1;
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS [index]
    WHERE [index].[name] = N'UX_ProductHqSyncOutbox_OperationKey'
      AND [index].[is_unique] = 1
      AND [index].[object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
      AND (
          SELECT COUNT(1)
          FROM sys.index_columns AS [key]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] > 0
      ) = 1
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 1
            AND [column].[name] = N'OperationKey'
      )
)
    THROW 51076, N'ProductHqSyncOutbox operation key index is missing or invalid.', 1;
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS [index]
    WHERE [index].[name] = N'IX_ProductHqSyncOutbox_Due'
      AND [index].[is_unique] = 0
      AND [index].[object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
      AND (
          SELECT COUNT(1)
          FROM sys.index_columns AS [key]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] > 0
      ) = 4
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 1 AND [column].[name] = N'Status'
      )
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 2 AND [column].[name] = N'NextAttemptAtUtc'
      )
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 3 AND [column].[name] = N'LeaseExpiresAtUtc'
      )
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 4 AND [column].[name] = N'CreatedAt'
      )
)
    THROW 51077, N'ProductHqSyncOutbox due index is missing or invalid.', 1;
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS [index]
    WHERE [index].[name] = N'IX_ProductHqSyncOutbox_ProductScope'
      AND [index].[is_unique] = 0
      AND [index].[object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
      AND (
          SELECT COUNT(1)
          FROM sys.index_columns AS [key]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] > 0
      ) = 3
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 1 AND [column].[name] = N'ProductCode'
      )
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 2 AND [column].[name] = N'ScopeKey'
      )
      AND EXISTS (
          SELECT 1 FROM sys.index_columns AS [key]
          INNER JOIN sys.columns AS [column]
              ON [column].[object_id] = [key].[object_id]
             AND [column].[column_id] = [key].[column_id]
          WHERE [key].[object_id] = [index].[object_id]
            AND [key].[index_id] = [index].[index_id]
            AND [key].[key_ordinal] = 3
            AND [key].[is_descending_key] = 1
            AND [column].[name] = N'OccurredAtUtc'
      )
)
    THROW 51077, N'ProductHqSyncOutbox product scope index is missing or invalid.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE [name] = N'CK_ProductHqSyncOutbox_Status'
      AND [parent_object_id] = OBJECT_ID(N'[dbo].[ProductHqSyncOutbox]', N'U')
)
    THROW 51078, N'ProductHqSyncOutbox status constraint is missing.', 1;
""";

    public static async Task EnsureAsync(
        ISqlSugarClient db,
        ILogger logger,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keepConnectionOpen = IsInMemorySqlite(db.CurrentConnectionConfig.ConnectionString);
            var connectionWasOpen =
                db.Ado.Connection.State == System.Data.ConnectionState.Open;
            var originalAutoClose = db.CurrentConnectionConfig.IsAutoCloseConnection;
            db.CurrentConnectionConfig.IsAutoCloseConnection = false;
            if (!connectionWasOpen)
            {
                db.Ado.Connection.Open();
            }

            var ownsTransaction = db.Ado.Transaction == null;
            if (ownsTransaction)
            {
                await db.Ado.BeginTranAsync();
            }

            try
            {
                // 建表与建索引必须固定在同一物理连接；内存库还需保持该连接直至 client 释放。
                db.CodeFirst.InitTables<ProductHqSyncOutbox>();
                await db.Ado.ExecuteCommandAsync(
                    """
CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProductHqSyncOutbox_OperationKey"
    ON "ProductHqSyncOutbox"("OperationKey");
CREATE INDEX IF NOT EXISTS "IX_ProductHqSyncOutbox_Due"
    ON "ProductHqSyncOutbox"("Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc", "CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_ProductHqSyncOutbox_ProductScope"
    ON "ProductHqSyncOutbox"("ProductCode", "ScopeKey", "OccurredAtUtc" DESC);
"""
                );
                if (ownsTransaction)
                {
                    await db.Ado.CommitTranAsync();
                }
            }
            catch
            {
                if (ownsTransaction)
                {
                    try
                    {
                        await db.Ado.RollbackTranAsync();
                    }
                    catch
                    {
                        // 保留原始 schema 异常。
                    }
                }
                throw;
            }
            finally
            {
                if (!keepConnectionOpen)
                {
                    db.CurrentConnectionConfig.IsAutoCloseConnection = originalAutoClose;
                    if (!connectionWasOpen && db.Ado.Transaction == null)
                    {
                        db.Ado.Connection.Close();
                    }
                }
            }
            logger.LogInformation("SQLite 商品 HQ 同步 outbox schema 检查完成");
            return;
        }

        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            throw new NotSupportedException("ProductHqSyncOutbox 仅支持 SQL Server 与 SQLite");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await db.Ado.ExecuteCommandAsync(SqlServerApplySql);
        logger.LogInformation("SQL Server 商品 HQ 同步 outbox schema 检查完成");
    }

    public static async Task VerifyAsync(
        ISqlSugarClient db,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        cancellationToken.ThrowIfCancellationRequested();
        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            await db.Ado.ExecuteCommandAsync(SqlServerVerifySql);
            return;
        }
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            var tableCount = await db.Ado.GetIntAsync(
                "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'ProductHqSyncOutbox'"
            );
            var actorColumnCount = await db.Ado.GetIntAsync(
                "SELECT COUNT(1) FROM pragma_table_info('ProductHqSyncOutbox') WHERE name IN ('RequestedByUserGuid', 'RequestedByDeviceId', 'AuthorizedStoreCodesJson')"
            );
            if (tableCount != 1 || actorColumnCount != 3)
            {
                throw new InvalidOperationException("ProductHqSyncOutbox schema 不完整");
            }

            await VerifySqliteIndexAsync(
                db,
                "UX_ProductHqSyncOutbox_OperationKey",
                expectedUnique: true,
                "OperationKey:0"
            );
            await VerifySqliteIndexAsync(
                db,
                "IX_ProductHqSyncOutbox_Due",
                expectedUnique: false,
                "Status:0,NextAttemptAtUtc:0,LeaseExpiresAtUtc:0,CreatedAt:0"
            );
            await VerifySqliteIndexAsync(
                db,
                "IX_ProductHqSyncOutbox_ProductScope",
                expectedUnique: false,
                "ProductCode:0,ScopeKey:0,OccurredAtUtc:1"
            );
            return;
        }
        throw new NotSupportedException("ProductHqSyncOutbox 仅支持 SQL Server 与 SQLite");
    }

    private static async Task VerifySqliteIndexAsync(
        ISqlSugarClient db,
        string indexName,
        bool expectedUnique,
        string expectedSignature
    )
    {
        var uniqueFlag = expectedUnique ? 1 : 0;
        var signatureCount = await db.Ado.GetIntAsync(
            $"SELECT COUNT(1) FROM pragma_index_list('ProductHqSyncOutbox') "
                + $"WHERE name = '{indexName}' AND \"unique\" = {uniqueFlag}"
        );
        var actualSignature = await db.Ado.GetStringAsync(
            $"SELECT COALESCE(group_concat(name || ':' || \"desc\", ','), '') FROM ("
                + $"SELECT name, \"desc\" FROM pragma_index_xinfo('{indexName}') "
                + "WHERE \"key\" = 1 ORDER BY seqno)"
        );
        if (
            signatureCount != 1
            || !string.Equals(actualSignature, expectedSignature, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException($"ProductHqSyncOutbox 必要索引 {indexName} 缺失或签名错误");
        }
    }

    private static bool IsInMemorySqlite(string? connectionString) =>
        connectionString?.Contains(":memory:", StringComparison.OrdinalIgnoreCase) == true
        || connectionString?.Contains("mode=memory", StringComparison.OrdinalIgnoreCase) == true;
}
