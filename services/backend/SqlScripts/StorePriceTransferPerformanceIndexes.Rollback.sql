/*
  HQ/本地分店价格同步性能索引回滚脚本。
  用法：按需在对应 SQL Server 数据库执行；脚本可重复执行。
*/

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_LocalRetail_SourceCursor'
      AND object_id = OBJECT_ID(N'[dbo].[StoreRetailPrice]')
)
BEGIN
    DROP INDEX [IX_SPT_LocalRetail_SourceCursor] ON [dbo].[StoreRetailPrice];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_LocalMulti_SourceCursor'
      AND object_id = OBJECT_ID(N'[dbo].[StoreMultiCodeProduct]')
)
BEGIN
    DROP INDEX [IX_SPT_LocalMulti_SourceCursor] ON [dbo].[StoreMultiCodeProduct];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_HqRetail_SourceStoreStatusId'
      AND object_id = OBJECT_ID(N'[dbo].[DIC_商品零售价表]')
)
BEGIN
    DROP INDEX [IX_SPT_HqRetail_SourceStoreStatusId] ON [dbo].[DIC_商品零售价表];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_HqRetail_TargetStoreProduct'
      AND object_id = OBJECT_ID(N'[dbo].[DIC_商品零售价表]')
)
BEGIN
    DROP INDEX [IX_SPT_HqRetail_TargetStoreProduct] ON [dbo].[DIC_商品零售价表];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_HqMulti_SourceStoreStatusId'
      AND object_id = OBJECT_ID(N'[dbo].[DIC_分店一品多码表]')
)
BEGIN
    DROP INDEX [IX_SPT_HqMulti_SourceStoreStatusId] ON [dbo].[DIC_分店一品多码表];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_SPT_HqMulti_TargetStoreProductMulti'
      AND object_id = OBJECT_ID(N'[dbo].[DIC_分店一品多码表]')
)
BEGIN
    DROP INDEX [IX_SPT_HqMulti_TargetStoreProductMulti] ON [dbo].[DIC_分店一品多码表];
END;
