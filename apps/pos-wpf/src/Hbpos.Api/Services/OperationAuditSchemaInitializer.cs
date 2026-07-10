using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IOperationAuditSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlSugarOperationAuditSchemaInitializer(HbposSqlSugarContext dbContext)
    : IOperationAuditSchemaInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 操作审计属于 POSM；由 Hbpos.Api 统一建表，避免多个服务竞争迁移所有权。
        var db = dbContext.PosmDb;
        db.CodeFirst.InitTables<PosOperationAudit, PosOperationAuditItem>();

        var sql = db.CurrentConnectionConfig.DbType switch
        {
            DbType.Sqlite => SqliteIndexSql,
            DbType.PostgreSQL => PostgreSqlIndexSql,
            _ => SqlServerIndexSql
        };
        await db.Ado.ExecuteCommandAsync(sql);
    }

    internal const string SqliteIndexSql = """
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_store_time
            ON pos_operation_audit(store_code, occurred_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_cashier_time
            ON pos_operation_audit(cashier_id, occurred_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_device_time
            ON pos_operation_audit(device_code, occurred_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_type_time
            ON pos_operation_audit(operation_type, occurred_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_outcome_time
            ON pos_operation_audit(outcome, occurred_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_order
            ON pos_operation_audit(order_guid);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_received
            ON pos_operation_audit(received_at_utc);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_item_product
            ON pos_operation_audit_item(product_code);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_item_reference
            ON pos_operation_audit_item(reference_code);
        CREATE INDEX IF NOT EXISTS IX_pos_operation_audit_item_lookup
            ON pos_operation_audit_item(lookup_code);
        """;

    internal const string PostgreSqlIndexSql = """
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_store_time"
            ON "pos_operation_audit" ("store_code", "occurred_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_cashier_time"
            ON "pos_operation_audit" ("cashier_id", "occurred_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_device_time"
            ON "pos_operation_audit" ("device_code", "occurred_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_type_time"
            ON "pos_operation_audit" ("operation_type", "occurred_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_outcome_time"
            ON "pos_operation_audit" ("outcome", "occurred_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_order"
            ON "pos_operation_audit" ("order_guid");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_received"
            ON "pos_operation_audit" ("received_at_utc");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_item_product"
            ON "pos_operation_audit_item" ("product_code");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_item_reference"
            ON "pos_operation_audit_item" ("reference_code");
        CREATE INDEX IF NOT EXISTS "IX_pos_operation_audit_item_lookup"
            ON "pos_operation_audit_item" ("lookup_code");
        """;

    internal const string SqlServerIndexSql = """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_store_time' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_store_time] ON [dbo].[pos_operation_audit]([store_code], [occurred_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_cashier_time' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_cashier_time] ON [dbo].[pos_operation_audit]([cashier_id], [occurred_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_device_time' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_device_time] ON [dbo].[pos_operation_audit]([device_code], [occurred_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_type_time' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_type_time] ON [dbo].[pos_operation_audit]([operation_type], [occurred_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_outcome_time' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_outcome_time] ON [dbo].[pos_operation_audit]([outcome], [occurred_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_order' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_order] ON [dbo].[pos_operation_audit]([order_guid]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_received' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit]'))
            CREATE INDEX [IX_pos_operation_audit_received] ON [dbo].[pos_operation_audit]([received_at_utc]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_item_product' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit_item]'))
            CREATE INDEX [IX_pos_operation_audit_item_product] ON [dbo].[pos_operation_audit_item]([product_code]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_item_reference' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit_item]'))
            CREATE INDEX [IX_pos_operation_audit_item_reference] ON [dbo].[pos_operation_audit_item]([reference_code]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pos_operation_audit_item_lookup' AND object_id = OBJECT_ID(N'[dbo].[pos_operation_audit_item]'))
            CREATE INDEX [IX_pos_operation_audit_item_lookup] ON [dbo].[pos_operation_audit_item]([lookup_code]);
        """;
}
