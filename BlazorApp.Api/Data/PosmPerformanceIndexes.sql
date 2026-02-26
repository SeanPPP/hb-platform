-- ============================================
-- POSM 数据库性能优化索引脚本
-- 用于优化销售仪表板查询性能
-- ============================================

USE [HBPOSM];
GO

PRINT '开始创建 POSM 性能优化索引...';
GO

-- ============================================
-- SalesOrder 表索引
-- ============================================

-- 订单时间索引（最常用的时间范围查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrder_OrderTime' AND object_id = OBJECT_ID('sales_order'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrder_OrderTime
    ON sales_order(OrderTime);
    PRINT '已创建 IX_SalesOrder_OrderTime';
END
GO

-- 订单时间 + 分店代码组合索引（支持时间范围+分店过滤的查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrder_OrderTime_BranchCode' AND object_id = OBJECT_ID('sales_order'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrder_OrderTime_BranchCode
    ON sales_order(OrderTime, BranchCode);
    PRINT '已创建 IX_SalesOrder_OrderTime_BranchCode';
END
GO

-- ============================================
-- SalesOrderDetail 表索引
-- ============================================

-- 订单 GUID 索引（主表关联查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_OrderGuid' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_OrderGuid
    ON sales_order_detail(OrderGuid);
    PRINT '已创建 IX_SalesOrderDetail_OrderGuid';
END
GO

-- 产品代码索引（分组查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_ProductCode' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_ProductCode
    ON sales_order_detail(ProductCode);
    PRINT '已创建 IX_SalesOrderDetail_ProductCode';
END
GO

-- 实际金额降序索引（排序查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_ActualAmount' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_ActualAmount
    ON sales_order_detail(ActualAmount DESC);
    PRINT '已创建 IX_SalesOrderDetail_ActualAmount';
END
GO

-- 产品代码 + 订单 GUID 组合索引（优化 JOIN 查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_ProductCode_OrderGuid' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_ProductCode_OrderGuid
    ON sales_order_detail(ProductCode, OrderGuid);
    PRINT '已创建 IX_SalesOrderDetail_ProductCode_OrderGuid';
END
GO

-- 折扣金额索引（用于计算折扣数量）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_DiscountAmount' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_DiscountAmount
    ON sales_order_detail(DiscountAmount);
    PRINT '已创建 IX_SalesOrderDetail_DiscountAmount';
END
GO

-- ============================================
-- PosmProductSupplierMapping 表索引
-- ============================================

-- 产品代码索引（主键已经存在，但创建覆盖索引）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PoSmProductSupplierMapping_ProductCode_Covering' AND object_id = OBJECT_ID('posm_product_supplier_mapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PoSmProductSupplierMapping_ProductCode_Covering
    ON posm_product_supplier_mapping(ProductCode)
    INCLUDE (LocalSupplierCode, ChinaSupplierCode, IsDeleted);
    PRINT '已创建 IX_PoSmProductSupplierMapping_ProductCode_Covering';
END
GO

-- 本地供应商代码索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PoSmProductSupplierMapping_LocalSupplierCode' AND object_id = OBJECT_ID('posm_product_supplier_mapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PoSmProductSupplierMapping_LocalSupplierCode
    ON posm_product_supplier_mapping(LocalSupplierCode)
    INCLUDE (ProductCode, ChinaSupplierCode, IsDeleted);
    PRINT '已创建 IX_PoSmProductSupplierMapping_LocalSupplierCode';
END
GO

-- 中国供应商代码索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PoSmProductSupplierMapping_ChinaSupplierCode' AND object_id = OBJECT_ID('posm_product_supplier_mapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PoSmProductSupplierMapping_ChinaSupplierCode
    ON posm_product_supplier_mapping(ChinaSupplierCode)
    INCLUDE (ProductCode, LocalSupplierCode, IsDeleted)
    WHERE ChinaSupplierCode IS NOT NULL;
    PRINT '已创建 IX_PoSmProductSupplierMapping_ChinaSupplierCode';
END
GO

-- IsDeleted 标记索引（过滤已删除记录）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PoSmProductSupplierMapping_IsDeleted' AND object_id = OBJECT_ID('posm_product_supplier_mapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PoSmProductSupplierMapping_IsDeleted
    ON posm_product_supplier_mapping(IsDeleted)
    INCLUDE (ProductCode, LocalSupplierCode, ChinaSupplierCode);
    PRINT '已创建 IX_PoSmProductSupplierMapping_IsDeleted';
END
GO

-- ============================================
-- 覆盖索引优化（常用查询的覆盖索引）
-- ============================================

-- SalesOrder 覆盖索引（避免回表查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrder_Covering' AND object_id = OBJECT_ID('sales_order'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrder_Covering
    ON sales_order(OrderTime, BranchCode)
    INCLUDE (OrderGuid, TotalAmount, DiscountAmount, ActualAmount);
    PRINT '已创建 IX_SalesOrder_Covering';
END
GO

-- SalesOrderDetail 覆盖索引（包含所有分组所需的字段）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesOrderDetail_Covering' AND object_id = OBJECT_ID('sales_order_detail'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalesOrderDetail_Covering
    ON sales_order_detail(ProductCode, OrderGuid)
    INCLUDE (ProductName, Quantity, Price, Subtotal, DiscountAmount, ActualAmount);
    PRINT '已创建 IX_SalesOrderDetail_Covering';
END
GO

PRINT '============================================';
PRINT 'POSM 性能优化索引创建完成！';
PRINT '============================================';
GO

-- 显示索引创建结果
SELECT 
    t.name AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    i.has_filter AS HasFilter,
    i.filter_definition AS FilterDefinition,
    p.rows AS TableRows
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
LEFT JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
WHERE t.name IN ('sales_order', 'sales_order_detail', 'posm_product_supplier_mapping')
    AND i.name LIKE 'IX_%'
ORDER BY t.name, i.name;
GO
