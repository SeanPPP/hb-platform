-- ================================================
-- 仓库商品查询性能优化索引
-- 用于提高WarehouseProduct表的查询效率
-- ================================================

-- 商品名称搜索索引（支持模糊查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_ProductName 
ON WarehouseProduct (ProductName);

-- 商品编码索引（唯一性和查询）
CREATE UNIQUE INDEX IF NOT EXISTS IX_WarehouseProduct_ProductCode 
ON WarehouseProduct (ProductCode);

-- 条码索引（支持条码查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_Barcode 
ON WarehouseProduct (Barcode) 
WHERE Barcode IS NOT NULL;

-- 品牌索引（支持品牌过滤）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_Brand 
ON WarehouseProduct (Brand) 
WHERE Brand IS NOT NULL;

-- 分类GUID索引（支持分类过滤）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_CategoryGUID 
ON WarehouseProduct (CategoryGUID) 
WHERE CategoryGUID IS NOT NULL;

-- 状态索引（支持启用/禁用过滤）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_IsActive 
ON WarehouseProduct (IsActive);

-- 库存数量索引（支持库存范围查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_StockQuantity 
ON WarehouseProduct (StockQuantity) 
WHERE StockQuantity IS NOT NULL;

-- 价格索引（支持价格范围查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_DomesticPrice 
ON WarehouseProduct (DomesticPrice) 
WHERE DomesticPrice IS NOT NULL;

CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_OEMPrice 
ON WarehouseProduct (OEMPrice) 
WHERE OEMPrice IS NOT NULL;

CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_ImportPrice 
ON WarehouseProduct (ImportPrice) 
WHERE ImportPrice IS NOT NULL;

-- 库存预警复合索引（优化库存预警查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_StockAlert 
ON WarehouseProduct (IsActive, StockQuantity, StockAlertQuantity) 
WHERE StockQuantity IS NOT NULL AND StockAlertQuantity IS NOT NULL;

-- 时间索引（支持按创建时间排序）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_CreatedAt 
ON WarehouseProduct (CreatedAt);

CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_UpdatedAt 
ON WarehouseProduct (UpdatedAt);

-- 复合索引（优化常用查询组合）
-- 分类+状态复合索引
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_Category_Active 
ON WarehouseProduct (CategoryGUID, IsActive) 
WHERE CategoryGUID IS NOT NULL;

-- 品牌+状态复合索引
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_Brand_Active 
ON WarehouseProduct (Brand, IsActive) 
WHERE Brand IS NOT NULL;

-- 关键字搜索复合索引（优化多字段搜索）
CREATE INDEX IF NOT EXISTS IX_WarehouseProduct_Search 
ON WarehouseProduct (ProductName, ProductCode, Barcode, Brand, IsActive);

-- ================================================
-- ProductLocation表索引（用于多仓库查询）
-- ================================================

-- 商品编码索引
CREATE INDEX IF NOT EXISTS IX_ProductLocation_ProductCode 
ON ProductLocation (ProductCode);

-- 位置GUID索引
CREATE INDEX IF NOT EXISTS IX_ProductLocation_LocationGuid 
ON ProductLocation (LocationGuid) 
WHERE LocationGuid IS NOT NULL;

-- 复合索引（优化商品-位置关联查询）
CREATE INDEX IF NOT EXISTS IX_ProductLocation_Product_Location 
ON ProductLocation (ProductCode, LocationGuid) 
WHERE LocationGuid IS NOT NULL;

-- ================================================
-- WarehouseCategory表索引（用于分类查询）
-- ================================================

-- 父级GUID索引（支持层级查询）
CREATE INDEX IF NOT EXISTS IX_WarehouseCategory_ParentGUID 
ON WarehouseCategory (ParentGUID) 
WHERE ParentGUID IS NOT NULL;

-- 状态+父级复合索引
CREATE INDEX IF NOT EXISTS IX_WarehouseCategory_Parent_Active 
ON WarehouseCategory (ParentGUID, IsActive);

-- 排序字段索引
CREATE INDEX IF NOT EXISTS IX_WarehouseCategory_SortOrder 
ON WarehouseCategory (SortOrder) 
WHERE SortOrder IS NOT NULL;

-- 分类名称索引（支持分类名称搜索）
CREATE INDEX IF NOT EXISTS IX_WarehouseCategory_CategoryName 
ON WarehouseCategory (CategoryName);

-- ================================================
-- 查询性能统计视图（可选）
-- ================================================

-- 创建视图用于快速获取商品统计信息
CREATE VIEW IF NOT EXISTS V_WarehouseProductStats AS
SELECT 
    COUNT(*) as TotalProducts,
    COUNT(CASE WHEN IsActive = 1 THEN 1 END) as ActiveProducts,
    COUNT(CASE WHEN IsActive = 0 THEN 1 END) as InactiveProducts,
    COALESCE(SUM(StockQuantity), 0) as TotalStock,
    COALESCE(SUM(StockValue), 0) as TotalStockValue,
    COUNT(CASE WHEN StockQuantity <= StockAlertQuantity 
               AND StockQuantity IS NOT NULL 
               AND StockAlertQuantity IS NOT NULL 
               THEN 1 END) as StockAlertCount,
    COUNT(CASE WHEN StockQuantity <= 0 
               AND StockQuantity IS NOT NULL 
               THEN 1 END) as OutOfStockCount
FROM WarehouseProduct;

-- 按分类统计视图
CREATE VIEW IF NOT EXISTS V_WarehouseProductStatsByCategory AS
SELECT 
    wc.CategoryGUID,
    wc.CategoryName,
    COUNT(wp.ProductCode) as ProductCount,
    COUNT(CASE WHEN wp.IsActive = 1 THEN 1 END) as ActiveProductCount,
    COALESCE(SUM(wp.StockQuantity), 0) as TotalStock,
    COALESCE(SUM(wp.StockValue), 0) as TotalStockValue
FROM WarehouseCategory wc
LEFT JOIN WarehouseProduct wp ON wc.CategoryGUID = wp.CategoryGUID
WHERE wc.IsActive = 1
GROUP BY wc.CategoryGUID, wc.CategoryName;

-- ================================================
-- 性能优化建议
-- ================================================

-- 1. 定期更新统计信息
-- ANALYZE TABLE WarehouseProduct;
-- ANALYZE TABLE ProductLocation;
-- ANALYZE TABLE WarehouseCategory;

-- 2. 监控慢查询
-- 建议在应用层记录执行时间超过100ms的查询

-- 3. 分页查询优化
-- 使用LIMIT + OFFSET时，建议限制OFFSET的最大值
-- 对于大数据量，建议使用基于游标的分页

-- 4. 全文搜索
-- 如果需要更强大的搜索功能，考虑使用SQLite的FTS扩展
-- CREATE VIRTUAL TABLE WarehouseProduct_fts USING fts5(ProductName, ProductCode, Barcode, Brand);

-- 5. 缓存策略
-- 对于分类树等相对静态的数据，建议在应用层进行缓存
-- 对于统计数据，可以考虑定时更新缓存

-- ================================================
-- 索引维护建议
-- ================================================

-- 定期检查索引使用情况（SQLite没有直接的统计信息，需要通过查询计划分析）
-- EXPLAIN QUERY PLAN SELECT * FROM WarehouseProduct WHERE ProductName LIKE '%test%';

-- 对于不常用的索引，考虑删除以减少写入开销
-- DROP INDEX IF EXISTS IX_IndexName;

-- 在大量数据插入前，考虑临时删除索引，插入完成后重建
-- 这样可以提高批量插入的性能
