-- ProductPricePerformanceIndexes.sql
-- 价格查询性能优化索引
-- 创建日期: 2025-02-16

-- ========================================
-- Product 表索引
-- ========================================

-- 复合索引: 支持常用过滤条件和排序
CREATE INDEX IF NOT EXISTS IX_Product_UpdatedAt_IsActive_IsSpecialProduct
ON Product(UpdatedAt DESC, IsActive, IsSpecialProduct);

-- 复合索引: 支持搜索字段(ProductName, ProductCode, ItemNumber, Barcode)
CREATE INDEX IF NOT EXISTS IX_Product_Search_Fields
ON Product(ProductName, ProductCode, ItemNumber, Barcode);

-- 复合索引: 支持供应商和类别过滤
CREATE INDEX IF NOT EXISTS IX_Product_LocalSupplier_Category
ON Product(LocalSupplierCode, WarehouseCategoryGUID, ProductType);

-- 价格索引: 支持价格区间过滤
CREATE INDEX IF NOT EXISTS IX_Product_Prices
ON Product(PurchasePrice, RetailPrice);

-- 更新人索引
CREATE INDEX IF NOT EXISTS IX_Product_UpdatedBy
ON Product(UpdatedBy);

-- ========================================
-- StoreRetailPrice 表索引
-- ========================================

-- 主索引: 支持与 Product 的 JOIN
CREATE INDEX IF NOT EXISTS IX_StoreRetailPrice_ProductCode_StoreCode
ON StoreRetailPrice(ProductCode, StoreCode);

-- 价格索引: 支持价格区间过滤
CREATE INDEX IF NOT EXISTS IX_StoreRetailPrice_Prices
ON StoreRetailPrice(StoreRetailPriceValue, PurchasePrice, DiscountRate);

-- 状态索引: 支持状态过滤
CREATE INDEX IF NOT EXISTS IX_StoreRetailPrice_Status
ON StoreRetailPrice(IsActive, IsAutoPricing);

-- ========================================
-- Store 表索引
-- ========================================

-- StoreCode 和 StoreName 索引
CREATE INDEX IF NOT EXISTS IX_Store_StoreCode_Name
ON Store(StoreCode, StoreName);

-- ========================================
-- HBLocalSupplier 表索引
-- ========================================

-- LocalSupplierCode 和 Name 索引
CREATE INDEX IF NOT EXISTS IX_HBLocalSupplier_Code_Name
ON HBLocalSupplier(LocalSupplierCode, Name);

-- ========================================
-- 查询验证索引是否创建成功
-- ========================================

-- 查看 Product 表索引
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.name = 'Product'
    AND i.name LIKE 'IX_Product_%'
GROUP BY i.name, i.type_desc
ORDER BY i.name;

-- 查看 StoreRetailPrice 表索引
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.name = 'StoreRetailPrice'
    AND i.name LIKE 'IX_StoreRetailPrice_%'
GROUP BY i.name, i.type_desc
ORDER BY i.name;

-- 查看 Store 表索引
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.name = 'Store'
    AND i.name LIKE 'IX_Store_%'
GROUP BY i.name, i.type_desc
ORDER BY i.name;

-- 查看 HBLocalSupplier 表索引
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.name = 'HBLocalSupplier'
    AND i.name LIKE 'IX_HBLocalSupplier_%'
GROUP BY i.name, i.type_desc
ORDER BY i.name;

-- ========================================
-- 索引使用情况统计
-- ========================================

-- 查看索引使用情况
SELECT 
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.user_updates,
    s.last_user_seek,
    s.last_user_scan
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats s ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE OBJECT_NAME(i.object_id) IN ('Product', 'StoreRetailPrice', 'Store', 'HBLocalSupplier')
    AND i.name LIKE 'IX_%'
ORDER BY OBJECT_NAME(i.object_id), i.name;

-- ========================================
-- 删除索引(如需回滚)
-- ========================================

/*
-- DROP Product 表索引
DROP INDEX IF EXISTS IX_Product_UpdatedAt_IsActive_IsSpecialProduct ON Product;
DROP INDEX IF EXISTS IX_Product_Search_Fields ON Product;
DROP INDEX IF EXISTS IX_Product_LocalSupplier_Category ON Product;
DROP INDEX IF EXISTS IX_Product_Prices ON Product;
DROP INDEX IF EXISTS IX_Product_UpdatedBy ON Product;

-- DROP StoreRetailPrice 表索引
DROP INDEX IF EXISTS IX_StoreRetailPrice_ProductCode_StoreCode ON StoreRetailPrice;
DROP INDEX IF EXISTS IX_StoreRetailPrice_Prices ON StoreRetailPrice;
DROP INDEX IF EXISTS IX_StoreRetailPrice_Status ON StoreRetailPrice;

-- DROP Store 表索引
DROP INDEX IF EXISTS IX_Store_StoreCode_Name ON Store;

-- DROP HBLocalSupplier 表索引
DROP INDEX IF EXISTS IX_HBLocalSupplier_Code_Name ON HBLocalSupplier;
*/
