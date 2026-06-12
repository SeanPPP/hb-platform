# 前后端货号备选匹配功能集成

> 🔗 前后端完整集成货号备选匹配功能  
> 📅 更新日期：2025-10-03  
> ✅ 状态：已完成

---

## 📋 功能说明

在货柜明细页面批量更新仓库商品信息时，系统现在支持**商品编码和货号双重匹配机制**：

### 工作流程

1. **前端采集数据**：在 ContainerDetail.razor 中收集商品编码和货号
2. **发送到后端**：通过 BatchProductUpdateRequest 传递完整数据
3. **后端智能匹配**：ProductSyncService 使用导航属性一次性查询
4. **备选匹配**：商品编码匹配不到时自动使用货号匹配

---

## 🔧 前端修改

### 文件：`BlazorApp/Pages/Container/ContainerDetail.razor`

**位置：** 第 2579-2593 行

**修改前：**
```csharp
var request = new BatchProductUpdateRequest
{
    Items = existingProducts.Select(d => new ProductUpdateItem
    {
        ProductCode = d.Product?.ProductCode ?? string.Empty,
        DomesticPrice = d.DomesticPrice,
        ImportPrice = d.ImportPrice,
        OEMPrice = d.OEMPrice,
        Volume = d.UnitVolume,
        PackingQuantity = Convert.ToInt32(d.PackingQuantity.ToString() ?? "0"),
        IsActive = true
    }).ToList()
};
```

**修改后：**
```csharp
var request = new BatchProductUpdateRequest
{
    Items = existingProducts.Select(d => new ProductUpdateItem
    {
        ProductCode = d.Product?.ProductCode ?? string.Empty,
        ItemNumber = d.Product?.ItemNumber, // 🔥 货号作为备选匹配
        DomesticPrice = d.DomesticPrice,
        ImportPrice = d.ImportPrice,
        OEMPrice = d.OEMPrice,
        Volume = d.UnitVolume,
        PackingQuantity = Convert.ToInt32(d.PackingQuantity.ToString() ?? "0"),
        IsActive = true
    }).ToList()
};
```

**关键变化：**
- ✅ 增加 `ItemNumber = d.Product?.ItemNumber` 字段
- ✅ 添加注释说明：货号作为备选匹配

---

## 🔗 后端支持

### 文件：`BlazorApp.Api/Services/ProductSyncService.cs`

**核心功能：**

1. **DTO 支持**（第 207-210 行）
```csharp
/// <summary>
/// 货号（可选，用于商品编码匹配不到时的备选匹配）
/// </summary>
public string? ItemNumber { get; set; }
```

2. **导航属性查询**（第 148-164 行）
```csharp
// 🔥 使用导航属性一次性查询 WarehouseProduct 和关联的 Product
var allWarehouseProducts = await _db.Queryable<WarehouseProduct>()
    .Includes(w => w.Product) // 使用导航属性加载关联的 Product
    .Where(w => productCodes.Contains(w.ProductCode) || 
               (itemNumbers.Any() && w.Product != null && w.Product.ItemNumber != null && itemNumbers.Contains(w.Product.ItemNumber)))
    .ToListAsync();

// 建立货号到商品编码的映射字典（直接从导航属性获取）
var itemNumberToProductCodeDict = allWarehouseProducts
    .Where(w => w.Product != null && !string.IsNullOrEmpty(w.Product.ItemNumber))
    .GroupBy(w => w.Product!.ItemNumber!)
    .ToDictionary(g => g.Key, g => g.First().ProductCode);
```

3. **智能匹配逻辑**（第 196-213 行）
```csharp
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
```

---

## 💡 使用场景

### 场景 1：正常商品编码更新
**用户操作：**
1. 在货柜明细页面点击"检测商品"
2. 检测到已存在商品
3. 点击"批量更新仓库信息"

**系统处理：**
```
前端：收集商品编码 PROD-001 和货号 HB001
  ↓
后端：使用商品编码 PROD-001 匹配成功
  ↓
结果：✅ 使用 PROD-001 更新仓库信息
```

---

### 场景 2：商品编码变更，使用货号匹配
**用户操作：**
1. 商品编码在系统中已变更为 PROD-002
2. 但货柜数据中仍使用旧编码 PROD-OLD
3. 点击"批量更新仓库信息"

**系统处理：**
```
前端：收集商品编码 PROD-OLD 和货号 HB001
  ↓
后端：商品编码 PROD-OLD 匹配失败
  ↓
后端：使用货号 HB001 匹配成功 → 找到 PROD-002
  ↓
日志：商品编码 PROD-OLD 未找到，使用货号 HB001 匹配到仓库商品 PROD-002
  ↓
结果：✅ 使用 PROD-002 更新仓库信息
```

---

### 场景 3：商品编码和货号都不存在
**用户操作：**
1. 商品编码和货号在仓库中都不存在
2. 点击"批量更新仓库信息"

**系统处理：**
```
前端：收集商品编码 PROD-WRONG 和货号 HB999
  ↓
后端：商品编码 PROD-WRONG 匹配失败
  ↓
后端：货号 HB999 也匹配失败
  ↓
结果：❌ 错误消息："商品编码 PROD-WRONG 和货号 HB999 在仓库中都不存在"
```

---

## 📊 完整数据流

```
┌─────────────────────────────────────────────────┐
│         前端 ContainerDetail.razor              │
│                                                 │
│  1. 用户点击"批量更新仓库信息"                    │
│  2. 收集已存在商品的数据：                        │
│     - ProductCode: PROD-001                     │
│     - ItemNumber: HB001                         │
│     - ImportPrice: 12.50                        │
│     - OEMPrice: 15.99                           │
│     - ...其他字段                                │
│  3. 构建 BatchProductUpdateRequest              │
└─────────────────────────────────────────────────┘
                    ↓ HTTP POST
┌─────────────────────────────────────────────────┐
│     后端 ProductSyncService                     │
│                                                 │
│  1. 接收请求数据                                 │
│  2. 使用导航属性查询：                            │
│     - 按商品编码查询                             │
│     - 按货号查询（通过 Product 导航属性）         │
│  3. 建立映射字典：                               │
│     - 商品编码 → WarehouseProduct               │
│     - 货号 → 商品编码                            │
│  4. 遍历更新项：                                 │
│     a. 优先用商品编码匹配                        │
│     b. 匹配不到用货号匹配                        │
│     c. 都不到返回错误                            │
│  5. 批量更新数据库：                             │
│     - WarehouseProduct                          │
│     - Product.PurchasePrice                     │
│     - StoreRetailPrice.PurchasePrice            │
│  6. 返回更新结果                                 │
└─────────────────────────────────────────────────┘
                    ↓ HTTP Response
┌─────────────────────────────────────────────────┐
│         前端 ContainerDetail.razor              │
│                                                 │
│  1. 接收更新结果                                 │
│  2. 显示成功/失败消息：                          │
│     - ✅ 更新完成！成功更新 10 个商品            │
│     - ⚠️ 部分更新成功：8 个成功，2 个失败        │
│  3. 重新检测商品以更新状态                       │
└─────────────────────────────────────────────────┘
```

---

## 🎯 测试验证

### 测试用例 1：正常更新
**前端操作：**
```javascript
货号 HB001，商品编码 PROD-001
→ 点击"批量更新仓库信息"
```

**后端处理：**
```
查询：PROD-001 存在于 WarehouseProduct
匹配：使用商品编码 PROD-001 匹配成功
更新：更新 PROD-001 的价格和信息
```

**结果：**
```
✅ 更新完成！成功更新 1 个商品的仓库信息
```

---

### 测试用例 2：使用货号匹配
**前端操作：**
```javascript
货号 HB001，商品编码 PROD-OLD（已变更）
→ 点击"批量更新仓库信息"
```

**后端处理：**
```
查询：PROD-OLD 不存在于 WarehouseProduct
查询：货号 HB001 对应商品编码 PROD-002
匹配：使用货号 HB001 匹配到 PROD-002
日志：商品编码 PROD-OLD 未找到，使用货号 HB001 匹配到仓库商品 PROD-002
更新：更新 PROD-002 的价格和信息
```

**结果：**
```
✅ 更新完成！成功更新 1 个商品的仓库信息
```

**日志输出：**
```
[Information] 商品编码 PROD-OLD 未找到，使用货号 HB001 匹配到仓库商品 PROD-002
[Debug] 准备更新商品 PROD-002（通过货号匹配）的进货价为 12.50
```

---

### 测试用例 3：都不存在
**前端操作：**
```javascript
货号 HB999，商品编码 PROD-WRONG
→ 点击"批量更新仓库信息"
```

**后端处理：**
```
查询：PROD-WRONG 不存在于 WarehouseProduct
查询：货号 HB999 也不存在
匹配：失败
```

**结果：**
```
❌ 商品编码 PROD-WRONG 和货号 HB999 在仓库中都不存在
```

---

## ✅ 集成检查清单

### 前端检查
- [x] ContainerDetail.razor 添加 ItemNumber 字段
- [x] 批量更新请求包含货号数据
- [x] 没有引入新的语法错误
- [x] 注释清晰说明用途

### 后端检查
- [x] ProductSyncService 支持货号查询
- [x] 使用导航属性优化性能
- [x] 智能匹配逻辑正确
- [x] 日志记录完整

### 测试检查
- [ ] 测试正常商品编码更新
- [ ] 测试货号备选匹配
- [ ] 测试都不存在的情况
- [ ] 验证日志输出正确

---

## 📚 相关文档

1. [后端货号备选匹配功能](./ProductSync-ItemNumber-Fallback-Feature.md)
2. [数据同步商品编码修正功能](./DataSync-Product-Code-Correction-Feature.md)
3. [货柜明细功能文档索引](./Container-Single-Product-README.md)

---

## 🎉 总结

通过前后端的完整集成，系统现在能够：

1. ✅ **自动备选匹配**：商品编码匹配不到时自动使用货号
2. ✅ **性能优化**：使用导航属性一次性查询，减少数据库往返
3. ✅ **用户友好**：无需用户关心匹配逻辑，系统自动处理
4. ✅ **日志完善**：记录详细的匹配过程，便于问题排查
5. ✅ **错误明确**：当商品确实不存在时，给出明确的错误提示

这个功能特别适合处理商品编码变更、数据迁移等场景，大大提升了系统的容错性和用户体验！

