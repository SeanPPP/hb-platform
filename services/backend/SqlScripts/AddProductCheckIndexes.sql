-- =====================================================
-- 性能优化索引迁移脚本
-- 用于 CheckProductsAsync 方法的性能优化
-- 执行前请确保备份数据库
-- =====================================================

-- PostgreSQL 版本
-- 如果使用 PostgreSQL，请执行以下语句：

-- Product 表索引
CREATE INDEX IF NOT EXISTS IX_Product_ItemNumber 
ON "Product"(ItemNumber) WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS IX_Product_Barcode 
ON "Product"(Barcode) WHERE "IsDeleted" = false AND Barcode IS NOT NULL;

CREATE INDEX IF NOT EXISTS IX_Product_LocalSupplierCode_ItemNumber 
ON "Product"("LocalSupplierCode", ItemNumber) WHERE "IsDeleted" = false;

-- StoreRetailPrice 复合索引
CREATE INDEX IF NOT EXISTS IX_StoreRetailPrice_StoreCode_ProductCode 
ON "StoreRetailPrice"("StoreCode", ProductCode) WHERE "IsDeleted" = false;

-- StoreMultiCodeProduct 复合索引
CREATE INDEX IF NOT EXISTS IX_StoreMultiCodeProduct_StoreCode_MultiBarcode 
ON "StoreMultiCodeProduct"("StoreCode", "MultiBarcode") WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS IX_StoreMultiCodeProduct_StoreCode_ProductCode_MultiCode
ON "StoreMultiCodeProduct"("StoreCode", "ProductCode", "MultiCodeProductCode") WHERE "IsDeleted" = false;

-- =====================================================
-- SQL Server 版本
-- 如果使用 SQL Server，请执行以下语句：

-- Product 表索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_ItemNumber' AND object_id = OBJECT_ID('Product'))
    CREATE INDEX IX_Product_ItemNumber ON [Product](ItemNumber) WHERE IsDeleted = 0;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_Barcode' AND object_id = OBJECT_ID('Product'))
    CREATE INDEX IX_Product_Barcode ON [Product](Barcode) WHERE IsDeleted = 0 AND Barcode IS NOT NULL;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_LocalSupplierCode_ItemNumber' AND object_id = OBJECT_ID('Product'))
    CREATE INDEX IX_Product_LocalSupplierCode_ItemNumber ON [Product](LocalSupplierCode, ItemNumber) WHERE IsDeleted = 0;

-- StoreRetailPrice 复合索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_StoreCode_ProductCode' AND object_id = OBJECT_ID('StoreRetailPrice'))
    CREATE INDEX IX_StoreRetailPrice_StoreCode_ProductCode ON [StoreRetailPrice](StoreCode, ProductCode) WHERE IsDeleted = 0;

-- StoreMultiCodeProduct 复合索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreMultiCodeProduct_StoreCode_MultiBarcode' AND object_id = OBJECT_ID('StoreMultiCodeProduct'))
    CREATE INDEX IX_StoreMultiCodeProduct_StoreCode_MultiBarcode ON [StoreMultiCodeProduct](StoreCode, MultiBarcode) WHERE IsDeleted = 0;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreMultiCodeProduct_StoreCode_ProductCode_MultiCode' AND object_id = OBJECT_ID('StoreMultiCodeProduct'))
    CREATE INDEX IX_StoreMultiCodeProduct_StoreCode_ProductCode_MultiCode ON [StoreMultiCodeProduct](StoreCode, ProductCode, MultiCodeProductCode) WHERE IsDeleted = 0;

-- =====================================================
-- 验证索引创建
-- PostgreSQL: SELECT indexname FROM pg_indexes WHERE tablename IN ('Product', 'StoreRetailPrice', 'StoreMultiCodeProduct');
-- SQL Server: SELECT name FROM sys.indexes WHERE object_id IN (OBJECT_ID('Product'), OBJECT_ID('StoreRetailPrice'), OBJECT_ID('StoreMultiCodeProduct'));
