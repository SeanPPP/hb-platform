using SqlSugar;

namespace BlazorApp.Api.Data;

public static class PreorderSchemaBootstrap
{
    public static async Task EnsureIndexesAsync(ISqlSugarClient db)
    {
        EnsureSupportedProvider(db.CurrentConnectionConfig.DbType);
        foreach (var sql in GetIndexStatements(db.CurrentConnectionConfig.DbType))
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }
    }

    public static void EnsureSupportedProvider(DbType dbType)
    {
        if (dbType is not (DbType.SqlServer or DbType.PostgreSQL or DbType.Sqlite))
        {
            // Preorder 依赖 Provider 专属的唯一索引、状态约束和过滤索引；未知 Provider 必须阻止启动。
            throw new InvalidOperationException(
                $"不支持的 Preorder Schema Provider: {dbType}"
            );
        }
    }

    private static IReadOnlyList<string> GetIndexStatements(DbType dbType) =>
        dbType switch
        {
            DbType.SqlServer => SqlServerStatements,
            DbType.PostgreSQL => PostgreSqlStatements,
            DbType.Sqlite => SqliteStatements,
            _ => throw new InvalidOperationException(
                $"不支持的 Preorder Schema Provider: {dbType}"
            ),
        };

    private static readonly string[] SqliteStatements =
    {
        // SQLite 无法对已有表直接 ADD CHECK，用同名触发器为旧库提供等价的写入约束。
        "CREATE TRIGGER IF NOT EXISTS \"CK_PreorderActivation_Status_Insert\" BEFORE INSERT ON \"PreorderActivation\" WHEN NEW.\"Status\" NOT IN ('Scheduled', 'Active', 'Closed', 'Cancelled') BEGIN SELECT RAISE(ABORT, 'CK_PreorderActivation_Status'); END",
        "CREATE TRIGGER IF NOT EXISTS \"CK_PreorderActivation_Status_Update\" BEFORE UPDATE OF \"Status\" ON \"PreorderActivation\" WHEN NEW.\"Status\" NOT IN ('Scheduled', 'Active', 'Closed', 'Cancelled') BEGIN SELECT RAISE(ABORT, 'CK_PreorderActivation_Status'); END",
        "DROP TRIGGER IF EXISTS \"CK_PreorderWarehouseOrder_Status_Insert\"",
        "DROP TRIGGER IF EXISTS \"CK_PreorderWarehouseOrder_Status_Update\"",
        "CREATE TRIGGER \"CK_PreorderWarehouseOrder_Status_Insert\" BEFORE INSERT ON \"PreorderWarehouseOrder\" WHEN NEW.\"Status\" NOT IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled') BEGIN SELECT RAISE(ABORT, 'CK_PreorderWarehouseOrder_Status'); END",
        "CREATE TRIGGER \"CK_PreorderWarehouseOrder_Status_Update\" BEFORE UPDATE OF \"Status\" ON \"PreorderWarehouseOrder\" WHEN NEW.\"Status\" NOT IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled') BEGIN SELECT RAISE(ABORT, 'CK_PreorderWarehouseOrder_Status'); END",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderTemplateItem_Template_Product\" ON \"PreorderTemplateItem\"(\"TemplateGuid\", \"ProductCode\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderTemplateStore_Template_Store\" ON \"PreorderTemplateStore\"(\"TemplateGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = 0",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderActivation_Template_Time\" ON \"PreorderActivation\"(\"TemplateGuid\", \"StartAtUtc\", \"EndAtUtc\", \"Status\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivation_Template_Period\" ON \"PreorderActivation\"(\"TemplateGuid\", \"PeriodNumber\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivation_Code\" ON \"PreorderActivation\"(\"ActivationCode\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivationItem_Activation_Product\" ON \"PreorderActivationItem\"(\"ActivationGuid\", \"ProductCode\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivationStore_Activation_Store\" ON \"PreorderActivationStore\"(\"ActivationGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = 0",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderActivationStore_StoreGuid\" ON \"PreorderActivationStore\"(\"StoreGuid\", \"ActivationGuid\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrder_Activation_Store\" ON \"PreorderWarehouseOrder\"(\"ActivationGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrder_OrderNo\" ON \"PreorderWarehouseOrder\"(\"OrderNo\") WHERE \"IsDeleted\" = 0",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrderItem_Order_Item\" ON \"PreorderWarehouseOrderItem\"(\"OrderGuid\", \"ActivationItemGuid\") WHERE \"IsDeleted\" = 0",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderWarehouseOrderItem_OrderGuid\" ON \"PreorderWarehouseOrderItem\"(\"OrderGuid\")",
    };

    private static readonly string[] PostgreSqlStatements =
    {
        "DO $preorder$ BEGIN IF to_regclass('\"PreorderActivation\"') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PreorderActivation_Status' AND conrelid = to_regclass('\"PreorderActivation\"')) THEN ALTER TABLE \"PreorderActivation\" ADD CONSTRAINT \"CK_PreorderActivation_Status\" CHECK (\"Status\" IN ('Scheduled', 'Active', 'Closed', 'Cancelled')); END IF; END; $preorder$;",
        "DO $preorder$ BEGIN IF to_regclass('\"PreorderWarehouseOrder\"') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PreorderWarehouseOrder_Status' AND conrelid = to_regclass('\"PreorderWarehouseOrder\"') AND pg_get_constraintdef(oid) LIKE '%ReturnedForRevision%') THEN ALTER TABLE \"PreorderWarehouseOrder\" DROP CONSTRAINT IF EXISTS \"CK_PreorderWarehouseOrder_Status\"; ALTER TABLE \"PreorderWarehouseOrder\" ADD CONSTRAINT \"CK_PreorderWarehouseOrder_Status\" CHECK (\"Status\" IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled')); END IF; END; $preorder$;",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderTemplateItem_Template_Product\" ON \"PreorderTemplateItem\"(\"TemplateGuid\", \"ProductCode\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderTemplateStore_Template_Store\" ON \"PreorderTemplateStore\"(\"TemplateGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = false",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderActivation_Template_Time\" ON \"PreorderActivation\"(\"TemplateGuid\", \"StartAtUtc\", \"EndAtUtc\", \"Status\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivation_Template_Period\" ON \"PreorderActivation\"(\"TemplateGuid\", \"PeriodNumber\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivation_Code\" ON \"PreorderActivation\"(\"ActivationCode\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivationItem_Activation_Product\" ON \"PreorderActivationItem\"(\"ActivationGuid\", \"ProductCode\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderActivationStore_Activation_Store\" ON \"PreorderActivationStore\"(\"ActivationGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = false",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderActivationStore_StoreGuid\" ON \"PreorderActivationStore\"(\"StoreGuid\", \"ActivationGuid\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrder_Activation_Store\" ON \"PreorderWarehouseOrder\"(\"ActivationGuid\", \"StoreGuid\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrder_OrderNo\" ON \"PreorderWarehouseOrder\"(\"OrderNo\") WHERE \"IsDeleted\" = false",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_PreorderWarehouseOrderItem_Order_Item\" ON \"PreorderWarehouseOrderItem\"(\"OrderGuid\", \"ActivationItemGuid\") WHERE \"IsDeleted\" = false",
        "CREATE INDEX IF NOT EXISTS \"IX_PreorderWarehouseOrderItem_OrderGuid\" ON \"PreorderWarehouseOrderItem\"(\"OrderGuid\")",
    };

    private static readonly string[] SqlServerStatements =
    {
        "IF OBJECT_ID(N'[dbo].[PreorderActivation]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreorderActivation_Status' AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderActivation]')) ALTER TABLE [dbo].[PreorderActivation] WITH CHECK ADD CONSTRAINT [CK_PreorderActivation_Status] CHECK ([Status] IN ('Scheduled', 'Active', 'Closed', 'Cancelled'))",
        "IF OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]', N'U') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreorderWarehouseOrder_Status' AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]') AND definition NOT LIKE '%ReturnedForRevision%') ALTER TABLE [dbo].[PreorderWarehouseOrder] DROP CONSTRAINT [CK_PreorderWarehouseOrder_Status]; IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreorderWarehouseOrder_Status' AND parent_object_id = OBJECT_ID(N'[dbo].[PreorderWarehouseOrder]')) ALTER TABLE [dbo].[PreorderWarehouseOrder] WITH CHECK ADD CONSTRAINT [CK_PreorderWarehouseOrder_Status] CHECK ([Status] IN ('Draft', 'ReturnedForRevision', 'Submitted', 'NoDemand', 'Processing', 'Completed', 'Cancelled')); END",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderTemplateItem_Template_Product' AND object_id = OBJECT_ID('PreorderTemplateItem')) CREATE UNIQUE INDEX [UX_PreorderTemplateItem_Template_Product] ON [PreorderTemplateItem]([TemplateGuid], [ProductCode]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderTemplateStore_Template_Store' AND object_id = OBJECT_ID('PreorderTemplateStore')) CREATE UNIQUE INDEX [UX_PreorderTemplateStore_Template_Store] ON [PreorderTemplateStore]([TemplateGuid], [StoreGuid]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderActivation_Template_Time' AND object_id = OBJECT_ID('PreorderActivation')) CREATE INDEX [IX_PreorderActivation_Template_Time] ON [PreorderActivation]([TemplateGuid], [StartAtUtc], [EndAtUtc], [Status])",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivation_Template_Period' AND object_id = OBJECT_ID('PreorderActivation')) CREATE UNIQUE INDEX [UX_PreorderActivation_Template_Period] ON [PreorderActivation]([TemplateGuid], [PeriodNumber]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivation_Code' AND object_id = OBJECT_ID('PreorderActivation')) CREATE UNIQUE INDEX [UX_PreorderActivation_Code] ON [PreorderActivation]([ActivationCode]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivationItem_Activation_Product' AND object_id = OBJECT_ID('PreorderActivationItem')) CREATE UNIQUE INDEX [UX_PreorderActivationItem_Activation_Product] ON [PreorderActivationItem]([ActivationGuid], [ProductCode]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderActivationStore_Activation_Store' AND object_id = OBJECT_ID('PreorderActivationStore')) CREATE UNIQUE INDEX [UX_PreorderActivationStore_Activation_Store] ON [PreorderActivationStore]([ActivationGuid], [StoreGuid]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderActivationStore_StoreGuid' AND object_id = OBJECT_ID('PreorderActivationStore')) CREATE INDEX [IX_PreorderActivationStore_StoreGuid] ON [PreorderActivationStore]([StoreGuid], [ActivationGuid])",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrder_Activation_Store' AND object_id = OBJECT_ID('PreorderWarehouseOrder')) CREATE UNIQUE INDEX [UX_PreorderWarehouseOrder_Activation_Store] ON [PreorderWarehouseOrder]([ActivationGuid], [StoreGuid]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrder_OrderNo' AND object_id = OBJECT_ID('PreorderWarehouseOrder')) CREATE UNIQUE INDEX [UX_PreorderWarehouseOrder_OrderNo] ON [PreorderWarehouseOrder]([OrderNo]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreorderWarehouseOrderItem_Order_Item' AND object_id = OBJECT_ID('PreorderWarehouseOrderItem')) CREATE UNIQUE INDEX [UX_PreorderWarehouseOrderItem_Order_Item] ON [PreorderWarehouseOrderItem]([OrderGuid], [ActivationItemGuid]) WHERE [IsDeleted] = 0",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreorderWarehouseOrderItem_OrderGuid' AND object_id = OBJECT_ID('PreorderWarehouseOrderItem')) CREATE INDEX [IX_PreorderWarehouseOrderItem_OrderGuid] ON [PreorderWarehouseOrderItem]([OrderGuid])",
    };
}
