using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

public static class InstallmentOrderSchemaMigrator
{
    private const string EnsureSqlServerCompatibilitySql = """
IF OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InstallmentOrder', 'PickedUpAt') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [PickedUpAt] datetime2 NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'PickedUpBy') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [PickedUpBy] nvarchar(100) NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'PickupNote') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupNote] nvarchar(500) NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'CancellationKind') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationKind] int NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'CancelledAt') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [CancelledAt] datetime2 NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'CancelledBy') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [CancelledBy] nvarchar(100) NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'CancellationReason') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationReason] nvarchar(500) NULL;
    IF COL_LENGTH('dbo.InstallmentOrder', 'CancellationIdempotencyKey') IS NULL
        ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationIdempotencyKey] nvarchar(100) NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'PickedUpAt' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [PickedUpAt] datetime2 NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'PickedUpBy' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [PickedUpBy] nvarchar(100) NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'PickupNote' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [PickupNote] nvarchar(500) NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'CancellationKind' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [CancellationKind] int NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'CancelledAt' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [CancelledAt] datetime2 NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'CancelledBy' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [CancelledBy] nvarchar(100) NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'CancellationReason' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [CancellationReason] nvarchar(500) NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]') AND [name] = N'CancellationIdempotencyKey' AND [is_nullable] = 0)
        ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [CancellationIdempotencyKey] nvarchar(100) NULL;
END;

IF OBJECT_ID(N'[dbo].[InstallmentPayment]', N'U') IS NOT NULL
   AND EXISTS (
       SELECT 1 FROM sys.columns
       WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentPayment]')
         AND [name] = N'CardTransactionsJson'
         AND ([system_type_id] <> TYPE_ID(N'nvarchar') OR [max_length] <> -1 OR [is_nullable] = 0)
   )
    ALTER TABLE [dbo].[InstallmentPayment] ALTER COLUMN [CardTransactionsJson] nvarchar(max) NULL;

IF OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InstallmentOrderLine_InstallmentGuid' AND [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrderLine]'))
        CREATE INDEX [IX_InstallmentOrderLine_InstallmentGuid] ON [dbo].[InstallmentOrderLine]([InstallmentGuid]);
END;

IF OBJECT_ID(N'[dbo].[InstallmentPayment]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InstallmentPayment_InstallmentGuid_RecordedAt' AND [object_id] = OBJECT_ID(N'[dbo].[InstallmentPayment]'))
        CREATE INDEX [IX_InstallmentPayment_InstallmentGuid_RecordedAt] ON [dbo].[InstallmentPayment]([InstallmentGuid], [RecordedAt]);
END;

IF OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InstallmentOrder_StoreCode_CreatedAt_InstallmentGuid' AND [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]'))
        CREATE INDEX [IX_InstallmentOrder_StoreCode_CreatedAt_InstallmentGuid] ON [dbo].[InstallmentOrder]([StoreCode], [CreatedAt], [InstallmentGuid]);
END;
""";

    private const string EnsureSqliteIndexesSql = """
CREATE INDEX IF NOT EXISTS "IX_InstallmentOrderLine_InstallmentGuid"
    ON "InstallmentOrderLine" ("InstallmentGuid");
CREATE INDEX IF NOT EXISTS "IX_InstallmentPayment_InstallmentGuid_RecordedAt"
    ON "InstallmentPayment" ("InstallmentGuid", "RecordedAt");
CREATE INDEX IF NOT EXISTS "IX_InstallmentOrder_StoreCode_CreatedAt_InstallmentGuid"
    ON "InstallmentOrder" ("StoreCode", "CreatedAt", "InstallmentGuid");
""";

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        try
        {
            // 关键逻辑：三表创建与补列都可重复执行，中央接口不再依赖 WPF 首次请求建表。
            db.CodeFirst.InitTables(
                typeof(InstallmentOrder),
                typeof(InstallmentOrderLine),
                typeof(InstallmentPayment)
            );
            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                await db.Ado.ExecuteCommandAsync(EnsureSqlServerCompatibilitySql);
            else if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                // 关键逻辑：开发与测试库同样要覆盖列表、详情行和付款时间线的查询路径。
                await db.Ado.ExecuteCommandAsync(EnsureSqliteIndexesSql);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "初始化分期订单表结构失败");
            throw;
        }
    }
}
