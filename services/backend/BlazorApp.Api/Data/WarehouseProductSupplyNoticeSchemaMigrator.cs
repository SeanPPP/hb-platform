using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>
/// 仓库商品供货说明与分店关注的独立、可重复执行 schema 迁移。
/// </summary>
public static class WarehouseProductSupplyNoticeSchemaMigrator
{
    internal const string SqlServerApplySql = """
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @SupplyNoticeSchemaLockResult int;
EXEC @SupplyNoticeSchemaLockResult = sys.sp_getapplock
    @Resource = N'WarehouseProductSupplyNotice_Schema_Initialization',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 30000;
IF @SupplyNoticeSchemaLockResult < 0
    THROW 51091, N'Unable to acquire WarehouseProductSupplyNotice schema lock.', 1;

IF OBJECT_ID(N'[dbo].[WarehouseProductSupplyNotice]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WarehouseProductSupplyNotice] (
        [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WarehouseProductSupplyNotice] PRIMARY KEY,
        [ProductCode] nvarchar(50) NOT NULL,
        [SupplyPlan] nvarchar(20) NOT NULL,
        [ExpectedFrom] date NULL,
        [ExpectedTo] date NULL,
        [ExpectedPrecision] nvarchar(10) NOT NULL,
        [StoreFacingNote] nvarchar(500) NULL,
        [InternalNote] nvarchar(500) NULL,
        [Source] nvarchar(80) NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_WarehouseProductSupplyNotice_CreatedAtUtc] DEFAULT(SYSUTCDATETIME()),
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_WarehouseProductSupplyNotice_UpdatedAtUtc] DEFAULT(SYSUTCDATETIME()),
        [ClosedAtUtc] datetime2 NULL,
        [ClosedBy] nvarchar(100) NULL,
        CONSTRAINT [CK_WarehouseProductSupplyNotice_Plan] CHECK (
            [SupplyPlan] IN (N'WillRestock', N'Undecided', N'Seasonal', N'Discontinued')
        ),
        CONSTRAINT [CK_WarehouseProductSupplyNotice_Precision] CHECK (
            [ExpectedPrecision] IN (N'Unknown', N'Day', N'Range', N'Month')
        ),
        CONSTRAINT [CK_WarehouseProductSupplyNotice_ExpectedRange] CHECK (
            [ExpectedFrom] IS NULL OR [ExpectedTo] IS NULL OR [ExpectedFrom] <= [ExpectedTo]
        )
    );
END;

-- 同一商品最多一条未关闭说明：由过滤唯一索引在数据库层兜底，同时覆盖分店端按商品取当前说明的读取。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_WarehouseProductSupplyNotice_Open_Product'
      AND [object_id] = OBJECT_ID(N'[dbo].[WarehouseProductSupplyNotice]', N'U')
)
    CREATE UNIQUE INDEX [UX_WarehouseProductSupplyNotice_Open_Product]
        ON [dbo].[WarehouseProductSupplyNotice]([ProductCode])
        INCLUDE ([SupplyPlan], [ExpectedFrom], [ExpectedTo], [ExpectedPrecision], [UpdatedAtUtc])
        WHERE [ClosedAtUtc] IS NULL;

IF OBJECT_ID(N'[dbo].[StoreProductSupplyWatch]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StoreProductSupplyWatch] (
        [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_StoreProductSupplyWatch] PRIMARY KEY,
        [StoreCode] nvarchar(50) NOT NULL,
        [ProductCode] nvarchar(50) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_StoreProductSupplyWatch_CreatedAtUtc] DEFAULT(SYSUTCDATETIME()),
        [ClosedAtUtc] datetime2 NULL,
        [ClosedBy] nvarchar(100) NULL,
        [CloseReason] nvarchar(20) NULL,
        CONSTRAINT [CK_StoreProductSupplyWatch_Status] CHECK ([Status] IN (N'Watching', N'Closed'))
    );
END;

-- 同一「分店 + 商品」最多一条有效关注；重复点关注由唯一索引兜底为幂等。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'UX_StoreProductSupplyWatch_Watching_Store_Product'
      AND [object_id] = OBJECT_ID(N'[dbo].[StoreProductSupplyWatch]', N'U')
)
    CREATE UNIQUE INDEX [UX_StoreProductSupplyWatch_Watching_Store_Product]
        ON [dbo].[StoreProductSupplyWatch]([StoreCode], [ProductCode])
        WHERE [Status] = N'Watching';

-- 分店读取自己的关注列表与提醒汇总。SqlSugar 会把状态常量下发成参数，过滤索引用不上，
-- 所以这里另建一个不带过滤条件、分店打头的普通索引。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_StoreProductSupplyWatch_Store_Status'
      AND [object_id] = OBJECT_ID(N'[dbo].[StoreProductSupplyWatch]', N'U')
)
    CREATE INDEX [IX_StoreProductSupplyWatch_Store_Status]
        ON [dbo].[StoreProductSupplyWatch]([StoreCode], [Status])
        INCLUDE ([ProductCode], [CreatedAtUtc]);

-- 仓库侧按商品统计关注门店数（辅助安排补货）。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_StoreProductSupplyWatch_Product_Status'
      AND [object_id] = OBJECT_ID(N'[dbo].[StoreProductSupplyWatch]', N'U')
)
    CREATE INDEX [IX_StoreProductSupplyWatch_Product_Status]
        ON [dbo].[StoreProductSupplyWatch]([ProductCode], [Status]);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    // SQLite 的 AUTOINCREMENT 只允许用在 INTEGER PRIMARY KEY 上，CodeFirst 会把 long 主键建成 BIGINT 而失败，
    // 因此与 StorePriceUpdateTask 的测试建表一致，这里直接用原生语句。
    private const string SqliteApplySql = """
CREATE TABLE IF NOT EXISTS "WarehouseProductSupplyNotice" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ProductCode" TEXT NOT NULL,
    "SupplyPlan" TEXT NOT NULL,
    "ExpectedFrom" TEXT NULL,
    "ExpectedTo" TEXT NULL,
    "ExpectedPrecision" TEXT NOT NULL,
    "StoreFacingNote" TEXT NULL,
    "InternalNote" TEXT NULL,
    "Source" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedBy" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    "ClosedAtUtc" TEXT NULL,
    "ClosedBy" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_WarehouseProductSupplyNotice_Open_Product"
    ON "WarehouseProductSupplyNotice"("ProductCode")
    WHERE "ClosedAtUtc" IS NULL;
CREATE TABLE IF NOT EXISTS "StoreProductSupplyWatch" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "StoreCode" TEXT NOT NULL,
    "ProductCode" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "ClosedAtUtc" TEXT NULL,
    "ClosedBy" TEXT NULL,
    "CloseReason" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_StoreProductSupplyWatch_Watching_Store_Product"
    ON "StoreProductSupplyWatch"("StoreCode", "ProductCode")
    WHERE "Status" = 'Watching';
CREATE INDEX IF NOT EXISTS "IX_StoreProductSupplyWatch_Store_Status"
    ON "StoreProductSupplyWatch"("StoreCode", "Status");
CREATE INDEX IF NOT EXISTS "IX_StoreProductSupplyWatch_Product_Status"
    ON "StoreProductSupplyWatch"("ProductCode", "Status");
""";

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        try
        {
            if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                await db.Ado.ExecuteCommandAsync(SqliteApplySql);
                return;
            }

            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            await db.Ado.ExecuteCommandAsync(SqlServerApplySql);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WarehouseProductSupplyNotice schema 迁移失败");
            throw;
        }
    }
}
