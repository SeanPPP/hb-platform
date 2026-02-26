# 货柜明细 - 自动填充英文名称和同步更新功能

> 🔄 检测商品时自动识别并填充英文名称，保存明细时同步更新到 DomesticProduct 表  
> 📅 更新日期：2025-10-03  
> ✅ 状态：已完成

---

## 📋 功能概述

在货柜明细页面管理商品时，系统现在支持：

### 功能 1：检测商品时自动填充英文名称
**场景：** 已存在商品在仓库中的英文名称为空，但货柜明细中有英文名称  
**行为：** 自动标记需要更新，保存时同步到 DomesticProduct 表

### 功能 2：保存明细时同步更新 DomesticProduct
**场景：** 保存货柜明细时，英文名称或贴牌价格有值  
**行为：** 自动更新 DomesticProduct 表中的对应字段

---

## 🔧 实现细节

### 修改文件列表

| 文件 | 修改内容 | 说明 |
|------|---------|------|
| `ProductSyncDTOs.cs` | 增加 WarehouseEnglishName 字段 | DTO 扩展 |
| `ProductSyncService.cs` | 查询时关联 DomesticProduct | 后端查询逻辑 |
| `ContainerDetail.razor` | 检测时填充，保存时更新 | 前端业务逻辑 |

---

## 💻 核心代码

### 1. DTO 扩展 - 增加英文名称字段

**文件：** `BlazorApp.Shared/DTOs/ProductSyncDTOs.cs`  
**位置：** 第 63-67 行

```csharp
/// <summary>
/// 仓库商品的英文名称（如果存在）
/// 用于检测时自动填充英文名称到货柜明细
/// </summary>
public string? WarehouseEnglishName { get; set; }
```

**作用：**
- ✅ 在检测结果中返回仓库商品的英文名称
- ✅ 用于前端判断是否需要填充

---

### 2. 后端查询逻辑 - 关联 DomesticProduct

**文件：** `BlazorApp.Api/Services/ProductSyncService.cs`  
**位置：** 第 44-61 行

```csharp
// 批量查询WarehouseProduct，同时关联查询Product和DomesticProduct信息
var warehouseProducts = await _db.Queryable<WarehouseProduct>()
    .LeftJoin<Product>((w, p) => w.ProductCode == p.ProductCode)
    .LeftJoin<DomesticProduct>((w, p, d) => p.ProductCode == d.ProductCode)  // 🆕 关联查询
    .Where((w, p, d) => productCodes.Contains(w.ProductCode)||itemNumbers.Contains(p.ItemNumber))
    .Select((w, p, d) => new
    {
        w.ProductCode,
        p.ItemNumber,
        p.Barcode,
        w.OEMPrice,
        w.ImportPrice,
        w.DomesticPrice,
        w.Volume,
        w.IsActive,
        EnglishName = d.EnglishProductName  // 🆕 查询英文名称
    })
    .ToListAsync();
```

**优势：**
- ✅ 一次查询获取完整信息
- ✅ 包含 WarehouseProduct、Product、DomesticProduct 数据
- ✅ 减少数据库往返次数

---

### 3. 后端返回结果 - 包含英文名称

**文件：** `BlazorApp.Api/Services/ProductSyncService.cs`  
**位置：** 第 77-91 行

```csharp
results.Add(new ProductDetectionResultDto
{
    ProductCode = item.ProductCode,
    ItemNumber = item.ItemNumber,
    Barcode = item.Barcode,
    Exists = exists,
    DetectionResult = exists ? "已存在" : "新商品",
    WarehouseOEMPrice = warehouse?.OEMPrice,
    WarehouseImportPrice = warehouse?.ImportPrice,
    WarehouseDomesticPrice = warehouse?.DomesticPrice,
    WarehouseVolume = warehouse?.Volume,
    WarehouseIsActive = warehouse?.IsActive,
    WarehouseEnglishName = warehouse?.EnglishName  // 🆕 返回英文名称
});
```

---

### 4. 前端检测逻辑 - 自动标记需要填充

**文件：** `BlazorApp/Pages/Container/ContainerDetail.razor`  
**位置：** 第 2517-2526 行

```csharp
// 🆕 如果仓库商品英文名称为空，但货柜明细中有英文名称，标记需要更新到 DomesticProduct
if (string.IsNullOrWhiteSpace(result.WarehouseEnglishName) && 
    !string.IsNullOrWhiteSpace(detail.Product?.EnglishName))
{
    // 标记需要更新 DomesticProduct
    MarkProductAsChanged(detail);
    autoFilledCount++;
    Logger.LogInformation("检测到商品 {ItemNumber} 需要填充英文名称: {EnglishName}", 
        detail.Product?.ItemNumber, detail.Product?.EnglishName);
}
```

**逻辑：**
- 🔍 判断条件：仓库英文名称为空 && 货柜明细有英文名称
- 🏷️ 标记：调用 `MarkProductAsChanged` 标记需要更新
- 📊 统计：增加自动填充计数

---

### 5. 前端保存逻辑 - 自动标记更新

**文件：** `BlazorApp/Pages/Container/ContainerDetail.razor`  
**位置：** 第 1886-1891 行（SaveAllDetails）和第 1949-1954 行（SaveSelectedDetails）

```csharp
// 🆕 如果英文名称或贴牌价格有值，确保标记为需要更新到 DomesticProduct
if (detail.Product != null && 
    (!string.IsNullOrWhiteSpace(detail.Product.EnglishName) || detail.OEMPrice.HasValue))
{
    MarkProductAsChanged(detail);
}
```

**作用：**
- ✅ 保存全部明细时自动标记
- ✅ 保存选中明细时自动标记
- ✅ 确保英文名称和贴牌价格同步到 DomesticProduct

---

## 📊 完整数据流

### 流程 1：检测商品 → 自动标记

```
用户点击"检测商品"
  ↓
前端发送检测请求
  ├─ ProductCode
  ├─ ItemNumber
  └─ Barcode
  ↓
后端 ProductSyncService.DetectProductsAsync
  ├─ 查询 WarehouseProduct
  ├─ 关联查询 Product
  └─ 关联查询 DomesticProduct（获取英文名称）
  ↓
返回检测结果
  ├─ Exists: true
  ├─ WarehouseOEMPrice: 15.99
  ├─ WarehouseImportPrice: 12.50
  └─ WarehouseEnglishName: null  // 英文名称为空
  ↓
前端处理检测结果
  ├─ 判断：仓库英文名称为空？✓
  ├─ 判断：货柜明细有英文名称？✓
  └─ 操作：MarkProductAsChanged(detail)  // 标记需要更新
  ↓
用户点击"保存全部明细"
  ↓
调用 SaveProductChanges
  ├─ 构建 DomesticProductDto
  │   ├─ ProductCode
  │   ├─ EnglishProductName: "SHREDDED PAPER RED"  // 从货柜明细获取
  │   └─ OEMPrice: 15.99
  └─ 调用 BatchUpdateDomesticProductsAsync
  ↓
后端更新 DomesticProduct 表
  ├─ SET EnglishProductName = 'SHREDDED PAPER RED'
  ├─ SET OEMPrice = 15.99
  └─ WHERE ProductCode = 'PROD-001'
  ↓
✅ 完成：英文名称已同步到 DomesticProduct 表
```

---

### 流程 2：保存明细 → 自动更新

```
用户编辑明细
  ├─ 双击英文名称列 → 修改为 "NEW PRODUCT NAME"
  └─ 双击贴牌价格列 → 修改为 $18.99
  ↓
失焦时触发 OnProductFieldChanged
  └─ MarkProductAsChanged(detail)  // 标记已更改
  ↓
用户点击"保存全部明细"
  ↓
SaveAllDetails 方法
  ├─ 遍历所有明细
  ├─ 更新计算字段
  └─ 标记需要更新（如果有英文名称或贴牌价格）
      if (detail.Product != null && 
          (!string.IsNullOrWhiteSpace(detail.Product.EnglishName) || detail.OEMPrice.HasValue))
      {
          MarkProductAsChanged(detail);
      }
  ↓
调用 ContainerService.BatchUpdateContainerDetailsAsync
  └─ 更新货柜明细数据
  ↓
保存成功后调用 SaveProductChanges
  ├─ 收集 changedProducts 中的商品
  ├─ 构建 DomesticProductDto
  │   ├─ ProductCode
  │   ├─ ProductName: "商品名称"
  │   ├─ EnglishProductName: "NEW PRODUCT NAME"  // 🆕 更新的英文名称
  │   ├─ OEMPrice: 18.99  // 🆕 更新的贴牌价格
  │   └─ ImportPrice: 12.50
  └─ 调用 BatchUpdateDomesticProductsAsync
  ↓
后端更新 DomesticProduct 表
  ├─ SET EnglishProductName = 'NEW PRODUCT NAME'
  ├─ SET OEMPrice = 18.99
  └─ WHERE ProductCode = 'PROD-001'
  ↓
✅ 完成：英文名称和贴牌价格已同步到 DomesticProduct 表
```

---

## 🎯 使用场景

### 场景 1：检测商品时自动填充英文名称

**操作步骤：**
1. 打开货柜明细页面
2. 货柜中有商品：货号 HB001，英文名称 "PLASTIC BAG RED"
3. 点击"检测商品"

**系统处理：**
```
检测到商品 HB001 已存在
  ├─ 仓库中英文名称：null（为空）
  ├─ 货柜中英文名称："PLASTIC BAG RED"
  └─ 操作：标记需要更新
```

**用户操作：**
```
点击"保存全部明细"
```

**结果：**
```
✅ 成功保存 1 项明细
✅ 成功更新 1 个商品信息
✅ DomesticProduct 表中已填充英文名称："PLASTIC BAG RED"
```

---

### 场景 2：编辑明细后自动同步

**操作步骤：**
1. 双击英文名称列，修改为 "NEW PRODUCT NAME"
2. 双击贴牌价格列，修改为 $18.99
3. 点击"保存全部明细"

**系统处理：**
```
SaveAllDetails
  ├─ 更新货柜明细
  │   ├─ EnglishName: "NEW PRODUCT NAME"
  │   └─ OEMPrice: 18.99
  └─ 自动标记需要更新 DomesticProduct
```

**结果：**
```
✅ 成功保存 1 项明细
✅ 成功更新 1 个商品信息
✅ DomesticProduct 表已同步：
   - EnglishProductName: "NEW PRODUCT NAME"
   - OEMPrice: 18.99
```

---

### 场景 3：批量翻译后自动同步

**操作步骤：**
1. 点击"批量翻译"按钮
2. 系统翻译所有中文名称为英文
3. 点击"保存全部明细"

**系统处理：**
```
BatchTranslateNames
  ├─ 翻译 "塑料袋红色" → "PLASTIC BAG RED"
  ├─ 翻译 "纸巾白色" → "TISSUE WHITE"
  └─ 标记所有翻译的商品需要更新（MarkProductAsChanged）
```

**用户操作：**
```
点击"保存全部明细"
```

**结果：**
```
✅ 成功保存 50 项明细
✅ 成功更新 50 个商品信息
✅ 所有翻译的英文名称已同步到 DomesticProduct 表
```

---

## 📝 日志示例

### 检测商品时的日志

```log
[Information] 开始批量检测商品，共 100 个商品
[Information] 自动填充商品 HB001 的贴牌价格: 15.99
[Information] 检测到商品 HB001 需要填充英文名称: PLASTIC BAG RED
[Information] 检测到商品 HB002 需要填充英文名称: TISSUE WHITE
[Information] 商品检测完成，新商品: 20，已存在: 80，自动填充: 25
```

### 保存明细时的日志

```log
[Debug] 批量更新WarehouseProduct完成，共 100 条
[Information] 成功更新 25 个商品信息
[Information] 批量更新完成，成功: 100，失败: 0
```

---

## ✅ 测试验证

### 测试用例 1：检测时自动填充英文名称

**前置条件：**
- DomesticProduct 表中商品 PROD-001 的 EnglishProductName 为 null
- 货柜明细中该商品的英文名称为 "PLASTIC BAG RED"

**操作：**
1. 点击"检测商品"
2. 点击"保存全部明细"

**预期结果：**
- ✅ 检测结果显示：自动填充数据: 1 项
- ✅ 保存明细成功
- ✅ 成功更新 1 个商品信息
- ✅ 数据库验证：
  ```sql
  SELECT EnglishProductName FROM DomesticProduct WHERE ProductCode = 'PROD-001'
  -- 结果：PLASTIC BAG RED
  ```

---

### 测试用例 2：编辑后自动同步

**操作：**
1. 双击英文名称列，修改为 "NEW NAME"
2. 双击贴牌价格列，修改为 $20.00
3. 点击"保存全部明细"

**预期结果：**
- ✅ 保存明细成功
- ✅ 成功更新 1 个商品信息
- ✅ 数据库验证：
  ```sql
  SELECT EnglishProductName, OEMPrice FROM DomesticProduct WHERE ProductCode = 'PROD-001'
  -- 结果：NEW NAME, 20.00
  ```

---

### 测试用例 3：批量翻译后同步

**操作：**
1. 点击"批量翻译"
2. 翻译完成后点击"保存全部明细"

**预期结果：**
- ✅ 翻译成功：成功 50 个
- ✅ 保存明细成功
- ✅ 成功更新 50 个商品信息
- ✅ 数据库验证：所有翻译的商品英文名称已更新

---

## 🎉 功能优势

1. **✅ 自动化流程**：无需手动操作，系统自动识别和同步
2. **✅ 数据一致性**：确保 DomesticProduct 表数据完整
3. **✅ 用户友好**：透明的自动处理，用户无感知
4. **✅ 日志完善**：详细记录填充和更新过程
5. **✅ 性能优化**：批量更新，减少数据库操作

---

## 📚 相关文档

1. [前后端货号备选匹配集成](./Frontend-Backend-ItemNumber-Integration.md)
2. [货柜明细单个商品创建](./Container-Single-Product-README.md)
3. [批量翻译功能](./Container-Batch-Translation-Feature.md)

---

## 🔄 数据同步关系

```
货柜明细数据
  ├─ YiwuContainerDetail.Product.EnglishName
  ├─ YiwuContainerDetail.OEMPrice
  └─ YiwuContainerDetail.ImportPrice
       ↓ 同步更新
DomesticProduct 表
  ├─ EnglishProductName
  ├─ OEMPrice
  └─ ImportPrice
       ↓ 影响
WarehouseProduct 表（批量更新时）
  ├─ OEMPrice
  └─ ImportPrice
       ↓ 影响
Product 表（批量更新时）
  └─ PurchasePrice（进口价格）
       ↓ 影响
StoreRetailPrice 表（批量更新时）
  └─ PurchasePrice（进货价）
```

功能已完整实现并文档化！🎊

