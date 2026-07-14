-- HBPOS 完整目录下载性能索引。
-- 本脚本独立手工执行，不接入应用启动迁移；重复执行不会删除或重建现有索引。

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'[dbo].[Product]')
         AND name = N'IX_HbposCatalogDownload_Product_Cursor'
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HbposCatalogDownload_Product_Cursor]
        ON [dbo].[Product] ([IsDeleted], [IsActive], [ProductCode], [UUID])
        INCLUDE ([ProductName], [ItemNumber], [Barcode], [RetailPrice], [UpdatedAt], [CreatedAt], [ProductImage]);
END;

IF OBJECT_ID(N'[dbo].[StoreRetailPrice]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'[dbo].[StoreRetailPrice]')
         AND name = N'IX_HbposCatalogDownload_StoreRetailPrice_Cursor'
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HbposCatalogDownload_StoreRetailPrice_Cursor]
        ON [dbo].[StoreRetailPrice] ([StoreCode], [IsDeleted], [IsActive], [ProductCode], [UUID])
        INCLUDE ([StoreRetailPriceValue], [UpdatedAt], [CreatedAt], [DiscountRate], [IsSpecialProduct]);
END;
