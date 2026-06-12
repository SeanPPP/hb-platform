# 仓库商品 Table 接口查询超时优化方案

## 问题分析

### 错误信息
`SqlException: 执行超时已过期。完成操作之前已超时或服务器未响应。` (Error Number: -2)

出处在 `ProductWarehouseReactService.cs:1226`，即 `GetAntdTableDataAsync` 方法中的分页查询。

### 根本原因

查询涉及 **5 表 JOIN**：
```
WarehouseProduct (w)
  LEFT JOIN DomesticProduct (dp)   ON dp.ProductCode = w.ProductCode
  LEFT JOIN ChinaSupplier (s)      ON dp.SupplierCode = s.SupplierCode
  INNER JOIN Product (p)           ON p.ProductCode = w.ProductCode
  LEFT JOIN WarehouseCategory (c)  ON p.WarehouseCategoryGUID = c.CategoryGUID
```

**核心问题：所有关联字段均无索引，只有主键聚簇索引。**

| 表 | 缺索引字段 | 影响 |
|---|---|---|
| `Product` | `ProductCode` (非主键) | INNER JOIN 全表扫描 |
| `Product` | `WarehouseCategoryGUID` | LEFT JOIN 全表扫描 |
| `DomesticProduct` | `ProductCode` (是主键但GUID格式) | LEFT JOIN 效率低 |
| `DomesticProduct` | `SupplierCode` | LEFT JOIN 全表扫描 |
| `ChinaSupplier` | `SupplierCode` (非主键) | LEFT JOIN 全表扫描 |
| `WarehouseProduct` | `IsDeleted` | WHERE 全表扫描 |
| `Product` | `IsDeleted` | JOIN 条件全表扫描 |
| `Product` | `ProductName`, `EnglishName`, `ItemNumber`, `Barcode` | 全局搜索 LIKE 全表扫描 |

另外还有两个性能隐患：
1. **第 1224 行** `CountAsync()` 也走 5 表 JOIN，实际只需要 WarehouseProduct 行数
2. **第 1000-1030 行** 搜索过滤使用 `ToLower().Contains()` 翻译为 `LIKE '%xxx%'`，无法命中索引

## 优化方案

### 第一步：添加数据库索引（效果最大）

在实体类上使用 `[SugarIndex]` 添加索引：

**Product.cs**:
- `IX_Product_ProductCode` → `ProductCode ASC`
- `IX_Product_WarehouseCategoryGUID` → `WarehouseCategoryGUID ASC`
- `IX_Product_IsDeleted` → `IsDeleted`（可选，配合过滤）

**DomesticProduct.cs**:
- `IX_DomesticProduct_SupplierCode` → `SupplierCode ASC`

**ChinaSupplier.cs**:
- `IX_ChinaSupplier_SupplierCode` → `SupplierCode ASC`

**WarehouseCategory.cs**:
- `IX_WarehouseCategory_CategoryGUID` → `CategoryGUID ASC`（如非主键）

> 注意：需要确认数据库中是否已存在这些索引（可能通过 SQL 脚本手动创建过）。`[SugarIndex]` 仅在 `InitTables` 时自动创建。

### 第二步：优化 Count 查询（减少不必要的 JOIN）

将第 1224 行：
```csharp
var total = await query.Clone().CountAsync();
```
改为仅对 WarehouseProduct 单表计数，因为 WHERE 条件中只有 `w.IsDeleted` 是必选过滤（分类过滤也只涉及 Product 表）：
```csharp
var totalQuery = _context.Db.Queryable<WarehouseProduct>()
    .Where(w => !w.IsDeleted);
// 如果有分类过滤，需要加 JOIN Product
// ... 其他只涉及 w 表的过滤条件
var total = await totalQuery.CountAsync();
```

### 第三步：确保索引实际生效（验证步骤）

添加索引后，需要在实际数据库中确认索引已创建。可通过 SQL 查询验证：
```sql
SELECT i.name, t.name AS TableName
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name IN ('Product', 'DomesticProduct', 'ChinaSupplier', 'WarehouseCategory')
```

## 实施步骤

1. **检查数据库现有索引** — 确认哪些索引已存在
2. **在实体类添加 `[SugarIndex]` 特性** — Product、DomesticProduct、ChinaSupplier
3. **优化 Count 查询逻辑** — 避免不必要的 5 表 JOIN
4. **通过 SQL 手动创建索引**（如果 SqlSugar InitTables 不方便立即执行）
5. **验证查询性能** — 再次调用接口确认不再超时

## 风险评估

- 添加索引是只读兼容操作，不影响现有功能
- 索引会略微增加写入开销，但仓库商品表写入频率低，影响可忽略
- Count 优化不改变返回结果，只是减少 JOIN 开销
