/*
  HQ/本地分店价格同步性能索引。
  用法：发布前由具备 DDL 权限的账号在对应 SQL Server 数据库执行；脚本可重复执行。
  注意：HQ 表索引在 HQ 数据库执行，本地表索引在本地业务数据库执行。
*/

-- 本地业务数据库：本地 -> HQ 源游标读取
IF OBJECT_ID(N'[dbo].[StoreRetailPrice]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_LocalRetail_SourceCursor'
         AND object_id = OBJECT_ID(N'[dbo].[StoreRetailPrice]')
   )
BEGIN
    CREATE INDEX [IX_SPT_LocalRetail_SourceCursor]
        ON [dbo].[StoreRetailPrice] ([StoreCode], [IsDeleted], [ProductCode], [UUID]);
END;

IF OBJECT_ID(N'[dbo].[StoreMultiCodeProduct]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_LocalMulti_SourceCursor'
         AND object_id = OBJECT_ID(N'[dbo].[StoreMultiCodeProduct]')
   )
BEGIN
    CREATE INDEX [IX_SPT_LocalMulti_SourceCursor]
        ON [dbo].[StoreMultiCodeProduct] ([StoreCode], [IsDeleted], [ProductCode], [MultiCodeProductCode], [UUID]);
END;

-- HQ 数据库：HQ -> 本地源分页、本地 -> HQ 目标匹配
IF OBJECT_ID(N'[dbo].[DIC_商品零售价表]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_HqRetail_SourceStoreStatusId'
         AND object_id = OBJECT_ID(N'[dbo].[DIC_商品零售价表]')
   )
BEGIN
    CREATE INDEX [IX_SPT_HqRetail_SourceStoreStatusId]
        ON [dbo].[DIC_商品零售价表] ([H分店代码], [H使用状态], [ID]);
END;

IF OBJECT_ID(N'[dbo].[DIC_商品零售价表]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_HqRetail_TargetStoreProduct'
         AND object_id = OBJECT_ID(N'[dbo].[DIC_商品零售价表]')
   )
BEGIN
    CREATE INDEX [IX_SPT_HqRetail_TargetStoreProduct]
        ON [dbo].[DIC_商品零售价表] ([H分店代码], [H商品编码]);
END;

IF OBJECT_ID(N'[dbo].[DIC_分店一品多码表]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_HqMulti_SourceStoreStatusId'
         AND object_id = OBJECT_ID(N'[dbo].[DIC_分店一品多码表]')
   )
BEGIN
    CREATE INDEX [IX_SPT_HqMulti_SourceStoreStatusId]
        ON [dbo].[DIC_分店一品多码表] ([H分店代码], [H使用状态], [ID]);
END;

IF OBJECT_ID(N'[dbo].[DIC_分店一品多码表]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_SPT_HqMulti_TargetStoreProductMulti'
         AND object_id = OBJECT_ID(N'[dbo].[DIC_分店一品多码表]')
   )
BEGIN
    CREATE INDEX [IX_SPT_HqMulti_TargetStoreProductMulti]
        ON [dbo].[DIC_分店一品多码表] ([H分店代码], [H商品编码], [H多码商品编码]);
END;
