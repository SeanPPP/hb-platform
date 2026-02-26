# 商品批量更新 - 货号备选匹配功能

> 📦 当商品编码匹配不到时，自动使用货号进行备选匹配  
> 🆕 更新日期：2025-10-03  
> ✅ 状态：已实现

---

## 📋 功能概述

在批量更新仓库商品信息时，系统现在支持**商品编码和货号双重匹配机制**：

### 匹配策略

1. **优先匹配**：使用商品编码（ProductCode）匹配仓库商品
2. **备选匹配**：如果商品编码匹配不到，自动使用货号（ItemNumber）进行匹配
3. **智能查询**：系统会同时查询两种匹配方式，确保最大匹配率

### 应用场景

| 场景 | 说明 | 解决方案 |
|------|------|---------|
| **商品编码变更** | 商品编码在系统中发生变化 | 使用货号匹配 |
| **数据迁移** | 从旧系统迁移，商品编码不一致 | 使用货号匹配 |
| **批量导入** | Excel 导入时商品编码可能有误 | 使用货号备选 |
| **正常更新** | 商品编码正确 | 使用商品编码匹配 |

---

## 🔧 实现细节

### 修改文件

**1. DTO 文件：** `BlazorApp.Shared/DTOs/ProductSyncDTOs.cs`  
**2. 服务文件：** `BlazorApp.Api/Services/ProductSyncService.cs`

### 实现流程

```
第1步：收集商品编码和货号
       ↓
第2步：使用导航属性一次性查询 WarehouseProduct + Product
       - 按商品编码匹配
       - 按货号匹配（通过 Product 导航属性）
       ↓
第3步：建立两个映射字典：
       - 商品编码 → WarehouseProduct
       - 货号 → 商品编码
       ↓
第4步：遍历更新项：
       - 优先使用商品编码匹配
       - 匹配不到则使用货号匹配
       ↓
第5步：批量更新数据
```

---

## 💻 核心代码

### 1. DTO 增加货号字段

**文件：** `BlazorApp.Shared/DTOs/ProductSyncDTOs.cs`  
**位置：** 第 207-210 行

```csharp
/// <summary>
/// 货号（可选，用于商品编码匹配不到时的备选匹配）
/// </summary>
public string? ItemNumber { get; set; }
```

### 2. 使用导航属性一次性查询

**文件：** `BlazorApp.Api/Services/ProductSyncService.cs`  
**位置：** 第 148-164 行

```csharp
// 🔥 使用导航属性一次性查询 WarehouseProduct 和关联的 Product
var allWarehouseProducts = await _db.Queryable<WarehouseProduct>()
    .Includes(w => w.Product) // 使用导航属性加载关联的 Product
    .Where(w => productCodes.Contains(w.ProductCode) || 
               (itemNumbers.Any() && w.Product != null && w.Product.ItemNumber != null && itemNumbers.Contains(w.Product.ItemNumber)))
    .ToListAsync();

_logger.LogInformation("查询到 {Count} 个仓库商品", allWarehouseProducts.Count);

// 转换为字典，方便快速查找
var warehouseDictByCode = allWarehouseProducts.ToDictionary(w => w.ProductCode);

// 建立货号到商品编码的映射字典（直接从导航属性获取）
var itemNumberToProductCodeDict = allWarehouseProducts
    .Where(w => w.Product != null && !string.IsNullOrEmpty(w.Product.ItemNumber))
    .GroupBy(w => w.Product!.ItemNumber!)
    .ToDictionary(g => g.Key, g => g.First().ProductCode);
```

**优势：**
- ✅ **一次查询**：通过导航属性一次性加载关联数据，减少数据库往返
- ✅ **代码简洁**：不需要手动联表和合并结果
- ✅ **性能优化**：SqlSugar 自动优化导航属性查询

### 3. 智能匹配逻辑

**位置：** 第 196-213 行

```csharp
foreach (var item in request.Items)
{
    WarehouseProduct? warehouse = null;
    string matchType = "商品编码";

    // 🔥 优先使用商品编码匹配
    if (!warehouseDictByCode.TryGetValue(item.ProductCode, out warehouse))
    {
        // 🔥 如果商品编码匹配不到，尝试使用货号匹配
        if (!string.IsNullOrEmpty(item.ItemNumber) && 
            itemNumberToProductCodeDict.TryGetValue(item.ItemNumber, out var matchedProductCode) &&
            warehouseDictByCode.TryGetValue(matchedProductCode, out warehouse))
        {
            matchType = "货号";
            _logger.LogInformation("商品编码 {RequestProductCode} 未找到，使用货号 {ItemNumber} 匹配到仓库商品 {WarehouseProductCode}", 
                item.ProductCode, item.ItemNumber, warehouse.ProductCode);
        }
    }

    // 使用实际匹配到的商品编码进行后续更新
    if (warehouse != null)
    {
        warehouse.ImportPrice = item.ImportPrice ?? warehouse.ImportPrice;
        warehouse.OEMPrice = item.OEMPrice ?? warehouse.OEMPrice;
        // ... 其他更新逻辑
        
        // 🔥 使用 warehouse.ProductCode（实际商品编码）而不是 item.ProductCode
        productCodesWithImportPrice.Add(warehouse.ProductCode);
    }
}
```

---

## 📊 API 使用示例

### 请求格式

**接口：** `POST /api/ProductSync/batch-update-warehouse-products`

```json
{
  "items": [
    {
      "productCode": "PROD-001",
      "itemNumber": "HB001",
      "domesticPrice": 10.00,
      "importPrice": 12.50,
      "oemPrice": 15.99,
      "volume": 0.05,
      "isActive": true
    },
    {
      "productCode": "PROD-WRONG",
      "itemNumber": "HB002",
      "importPrice": 8.50,
      "oemPrice": 10.99,
      "isActive": true
    }
  ]
}
```

### 响应示例

**成功场景（混合匹配）：**

```json
{
  "success": true,
  "message": "更新完成，成功: 2，失败: 0",
  "successCount": 2,
  "failedCount": 0,
  "errors": []
}
```

**部分失败场景：**

```json
{
  "success": false,
  "message": "更新完成，成功: 1，失败: 1",
  "successCount": 1,
  "failedCount": 1,
  "errors": [
    "商品编码 PROD-WRONG 和货号 HB999 在仓库中都不存在"
  ]
}
```

---

## 🎯 日志跟踪

### 查询阶段日志

```log
[Information] 开始批量更新仓库商品，共 2 个商品
[Information] 查询到 2 个仓库商品（按商品编码: 1，按货号: 1）
```

### 匹配阶段日志

```log
[Information] 商品编码 PROD-WRONG 未找到，使用货号 HB002 匹配到仓库商品 PROD-002
[Debug] 准备更新商品 PROD-002（通过货号匹配）的进货价为 8.50
```

### 更新完成日志

```log
[Debug] 批量更新WarehouseProduct完成，共 2 条
[Debug] 批量更新Product完成，共 2 条
[Debug] 批量更新StoreRetailPrice完成，共 4 条
[Information] 批量更新完成，成功: 2，失败: 0
```

---

## ✅ 测试验证

### 测试用例 1：商品编码正常匹配

**测试数据：**
```json
{
  "productCode": "PROD-001",  // ✅ 存在
  "itemNumber": "HB001",
  "importPrice": 12.50
}
```

**预期结果：**
- ✅ 使用商品编码 `PROD-001` 匹配成功
- ✅ 日志不显示货号匹配信息
- ✅ 更新成功

---

### 测试用例 2：商品编码不存在，使用货号匹配

**测试数据：**
```json
{
  "productCode": "PROD-WRONG",  // ❌ 不存在
  "itemNumber": "HB002",         // ✅ 存在，对应 PROD-002
  "importPrice": 8.50
}
```

**预期结果：**
- ✅ 商品编码 `PROD-WRONG` 匹配失败
- ✅ 使用货号 `HB002` 匹配到 `PROD-002`
- ✅ 日志显示：`使用货号 HB002 匹配到仓库商品 PROD-002`
- ✅ 更新成功，使用 `PROD-002` 更新数据

---

### 测试用例 3：商品编码和货号都不存在

**测试数据：**
```json
{
  "productCode": "PROD-WRONG",  // ❌ 不存在
  "itemNumber": "HB999",         // ❌ 不存在
  "importPrice": 8.50
}
```

**预期结果：**
- ❌ 商品编码 `PROD-WRONG` 匹配失败
- ❌ 货号 `HB999` 也匹配失败
- ❌ 错误消息：`商品编码 PROD-WRONG 和货号 HB999 在仓库中都不存在`
- ❌ 更新失败

---

### 测试用例 4：只提供商品编码，不提供货号

**测试数据：**
```json
{
  "productCode": "PROD-WRONG",  // ❌ 不存在
  "itemNumber": null,            // 未提供
  "importPrice": 8.50
}
```

**预期结果：**
- ❌ 商品编码 `PROD-WRONG` 匹配失败
- ❌ 无货号，无法进行备选匹配
- ❌ 错误消息：`商品编码 PROD-WRONG 在仓库中不存在`
- ❌ 更新失败

---

## 🚀 性能优化

### 优化点 1：批量查询
- ✅ 一次性查询所有需要的商品编码
- ✅ 一次性查询所有需要的货号
- ✅ 减少数据库查询次数

### 优化点 2：内存字典
- ✅ 使用字典进行 O(1) 查找
- ✅ 货号到商品编码的映射字典
- ✅ 商品编码到仓库商品的映射字典

### 优化点 3：去重处理
- ✅ 使用 `Union` + `GroupBy` 去除重复商品
- ✅ 确保每个商品只更新一次

---

## 📝 注意事项

### 1. 货号唯一性

⚠️ **重要提示：** 如果多个商品有相同的货号，系统会取第一个匹配的商品。

```csharp
.GroupBy(p => p.ItemNumber)
.ToDictionary(g => g.Key!, g => g.First()); // 取第一个
```

**建议：** 确保货号在系统中是唯一的，避免混淆。

### 2. 性能考虑

- 大批量更新（>1000条）时，建议分批处理
- 货号查询需要联表，会增加查询时间
- 建议在 `Product.ItemNumber` 字段上建立索引

### 3. 日志级别

- `Information`：货号匹配成功
- `Warning`：商品不存在
- `Debug`：详细更新信息

---

## 🔄 与其他功能的集成

### 货柜明细批量同步

这个功能与货柜明细中的批量商品同步完美配合：

1. **货柜检测商品** → 获取商品编码和货号
2. **调用批量更新 API** → 自动使用货号备选匹配
3. **更新成功** → 商品价格和状态同步到仓库

### 数据同步服务

与 `DataSyncService` 的商品编码修正功能协同工作：

1. **DataSyncService** → 修正商品编码（DIC 表为准）
2. **ProductSyncService** → 使用货号备选匹配
3. **确保数据一致性** → 无论商品编码是否变更

---

## 📞 技术支持

如有问题或建议，请联系开发团队。

**相关文档：**
- [商品数据同步功能](./DataSync-Product-Code-Correction-Feature.md)
- [货柜明细功能](./Container-Single-Product-README.md)
- [API 接口文档](../BlazorApp.Api/README.md)

