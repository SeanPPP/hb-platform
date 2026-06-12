# 修改 ItemBarcodeService 查询使用 DomesticProduct 表

## 📋 需求说明

将 `ItemBarcodeService` 中查询已有货号和条码的逻辑，从 `Product` 表改为查询 `DomesticProduct` 表。

### 原因分析
- `DomesticProduct` 表是专门用于存储义乌采购的国内供应商商品信息
- 该表包含 `HBProductNo`（货号）和 `Barcode`（条形码）字段
- 从 `DomesticProduct` 表查询可以获取更准确的国内商品货号和条码信息

### 当前问题
- 所有查询都从 `Product` 表执行：`conn.Queryable<Product>()`
- 查询字段：`p.ItemNumber` 和 `p.Barcode`

### 目标
- 改为从 `DomesticProduct` 表执行查询：`conn.Queryable<DomesticProduct>()`
- 查询字段改为：`dp.HBProductNo`（货号）和 `dp.Barcode`（条形码）

## 🔧 修改方案

### 1. 修改 `GenerateItemNumberAndBarcodeAsync()` 方法 (L46-114)

**修改位置：** L52-63 和 L70-79

**修改前：**
```csharp
var existingItemNumbersTask = Task.Run(async () =>
{
    using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
    return await conn.Queryable<Product>()
        .Where(p =>
            !p.IsDeleted
            && p.ItemNumber != null
            && p.ItemNumber.StartsWith(supplierCode)
        )
        .Select(p => p.ItemNumber!)
        .ToListAsync();
});
```

**修改后：**
```csharp
var existingItemNumbersTask = Task.Run(async () =>
{
    using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
    return await conn.Queryable<DomesticProduct>()
        .Where(dp =>
            !dp.IsDeleted
            && dp.HBProductNo != null
            && dp.HBProductNo.StartsWith(supplierCode)
        )
        .Select(dp => dp.HBProductNo!)
        .ToListAsync();
});
```

**条码查询修改：**
```csharp
var existingBarcodesTask = Task.Run(async () =>
{
    using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
    return await conn.Queryable<DomesticProduct>()
        .Where(dp =>
            dp.Barcode != null && !dp.IsDeleted && dp.Barcode.StartsWith(barcodePrefix)
        )
        .Select(dp => dp.Barcode!)
        .ToListAsync();
});
```

### 2. 修改 `GenerateBatchItemNumbersAndBarcodesAsync()` 方法 (L125-207)

**修改位置：** L140-151 和 L157-166

**修改内容：** 同上，将 `Product` 改为 `DomesticProduct`，`p.ItemNumber` 改为 `dp.HBProductNo`

### 3. 修改 `GenerateSetItemNumberAndBarcodeAsync()` 方法 (L216-266)

**修改位置：** L221-230 和 L237-246

**修改内容：** 同上，将 `Product` 改为 `DomesticProduct`，`p.ItemNumber` 改为 `dp.HBProductNo`

### 4. 修改 `GenerateBatchSetItemNumbersAndBarcodesAsync()` 方法 (L275-342)

**修改位置：** L289-298 和 L305-314

**修改内容：** 同上，将 `Product` 改为 `DomesticProduct`，`p.ItemNumber` 改为 `dp.HBProductNo`

## 📝 字段映射对照

| 查询类型 | Product 表字段 | DomesticProduct 表字段 |
|---------|----------------|----------------------|
| 货号 | `ItemNumber` | `HBProductNo` |
| 条形码 | `Barcode` | `Barcode` |
| 删除标识 | `IsDeleted` | `IsDeleted` |

## ✅ 修改文件

- `d:\Development\cline\HBweb\backend\BlazorApp.Api\Services\ItemBarcodeService.cs`

## 🔍 验证计划

修改完成后需要验证：
1. 编译无错误
2. 所有查询都已从 `Product` 改为 `DomesticProduct`
3. 字段引用从 `p.ItemNumber` 改为 `dp.HBProductNo`
4. 条码查询字段保持 `dp.Barcode` 不变
