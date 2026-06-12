-- 清理 StoreRetailPrice 表中的重复数据
-- 保留每个 StoreCode|ProductCode|SupplierCode 组合中最新更新的记录

-- 1. 首先查看重复数据
SELECT 
    StoreCode,
    ProductCode,
    SupplierCode,
    COUNT(*) as Count,
    MAX(UUID) as LatestUUID,
    MAX(UpdatedAt) as LatestUpdatedAt
FROM StoreRetailPrice
WHERE IsDeleted = 0
GROUP BY StoreCode, ProductCode, SupplierCode
HAVING COUNT(*) > 1
ORDER BY Count DESC;

-- 2. 查看将被标记删除的重复数据
SELECT srp.*
FROM StoreRetailPrice srp
INNER JOIN (
    SELECT 
        StoreCode,
        ProductCode,
        SupplierCode,
        MIN(UUID) as KeepUUID
    FROM StoreRetailPrice
    WHERE IsDeleted = 0
    GROUP BY StoreCode, ProductCode, SupplierCode
    HAVING COUNT(*) > 1
) dup ON srp.StoreCode = dup.StoreCode 
    AND srp.ProductCode = dup.ProductCode 
    AND srp.SupplierCode = dup.SupplierCode
    AND srp.UUID != dup.KeepUUID
WHERE srp.IsDeleted = 0
ORDER BY srp.StoreCode, srp.ProductCode, srp.SupplierCode;

-- 3. 执行删除操作 - 标记重复数据为已删除
-- 注意: 执行前请先确认上面的查询结果正确
UPDATE srp
SET srp.IsDeleted = 1,
    srp.UpdatedAt = GETUTCDATE(),
    srp.UpdatedBy = 'SYSTEM_CLEANUP'
FROM StoreRetailPrice srp
INNER JOIN (
    SELECT 
        StoreCode,
        ProductCode,
        SupplierCode,
        MIN(UUID) as KeepUUID
    FROM StoreRetailPrice
    WHERE IsDeleted = 0
    GROUP BY StoreCode, ProductCode, SupplierCode
    HAVING COUNT(*) > 1
) dup ON srp.StoreCode = dup.StoreCode 
    AND srp.ProductCode = dup.ProductCode 
    AND srp.SupplierCode = dup.SupplierCode
    AND srp.UUID != dup.KeepUUID
WHERE srp.IsDeleted = 0;

-- 4. 查看清理结果
SELECT 
    COUNT(*) as TotalRecords,
    SUM(CASE WHEN IsDeleted = 0 THEN 1 ELSE 0 END) as ActiveRecords,
    SUM(CASE WHEN IsDeleted = 1 THEN 1 ELSE 0 END) as DeletedRecords
FROM StoreRetailPrice;

-- 5. 验证是否还有重复数据
SELECT 
    StoreCode,
    ProductCode,
    SupplierCode,
    COUNT(*) as Count
FROM StoreRetailPrice
WHERE IsDeleted = 0
GROUP BY StoreCode, ProductCode, SupplierCode
HAVING COUNT(*) > 1;

-- 如果步骤 5 返回空结果,说明清理成功
