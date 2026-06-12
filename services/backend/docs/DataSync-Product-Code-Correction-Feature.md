# 数据同步 - 商品编码修正功能

> 📦 当 DIC 表和 CPT 表中存在货号相同但商品编码不同的商品时，自动以 DIC 表的商品编码为准进行合并  
> 🆕 更新日期：2025-10-03  
> ✅ 状态：已实现

---

## 📋 功能概述

在从 HQ 数据库同步国内商品数据时，系统会遇到以下情况：

### 问题场景

1. **旧版表：** `DIC_商品信息字典表` - 包含历史商品数据
2. **新版表：** `CPT_DIC_商品信息字典表` - HBSales 和 HQ 的新版商品数据

**问题：** 同一个商品（货号相同）在两个表中可能有**不同的商品编码**

### 解决方案

✅ **以 DIC 表的商品编码为准**，自动修正 CPT 表中的商品编码

---

## 🔧 实现逻辑

### 数据流程

```
第1步：获取 DIC_商品信息字典表 数据
       ↓
       建立【货号 → 商品编码】映射表
       ↓
第2步：获取 HBSales 的 CPT_DIC_商品信息字典表 数据
       ↓
       检查货号映射，修正商品编码
       ↓
第3步：获取 HQ 的 CPT_DIC_商品信息字典表 数据
       ↓
       检查货号映射，修正商品编码
       ↓
第4步：合并数据（HQ 优先，但商品编码以 DIC 为准）
       ↓
       保留 HBSales 的装箱数和体积数据
       ↓
第5步：AutoMapper 批量转换
       ↓
第6步：获取现有本地商品数据
       ↓
第7步：分类插入和更新数据
       ↓
第8步：执行数据库操作
```

---

## 💻 核心代码

### 1. 建立货号到商品编码的映射

**文件：** `BlazorApp.Api/Services/DataSyncService.cs`  
**位置：** 第 1805-1819 行

```csharp
// 第1步：获取旧版DIC_商品信息字典表数据，建立货号到商品编码的映射
var dicProducts = await _hqContext.DIC_商品信息字典表Db
    .AsQueryable()
    .Where(p => !string.IsNullOrEmpty(p.H货号) && !string.IsNullOrEmpty(p.H商品编码))
    .ToListAsync();
_logger.LogInformation("从HQ获取到旧版DIC表 {Count} 条商品数据", dicProducts.Count);

// 建立货号到商品编码的映射表（用于处理货号相同但商品编码不同的情况）
var itemNumberToProductCodeMap = dicProducts
    .GroupBy(p => p.H货号)
    .ToDictionary(
        g => g.Key, 
        g => g.First().H商品编码  // 如果同一货号有多条记录，取第一条
    );
_logger.LogInformation("建立货号到商品编码映射表，共 {Count} 个货号", itemNumberToProductCodeMap.Count);
```

### 2. 修正 HBSales 数据的商品编码

**文件：** `BlazorApp.Api/Services/DataSyncService.cs`  
**位置：** 第 1846-1868 行

```csharp
// 先添加HBSales数据
foreach (var product in hbSalesProducts)
{
    if (!string.IsNullOrEmpty(product.商品编码))
    {
        // 🔥 检查是否需要根据货号修正商品编码
        var correctProductCode = product.商品编码;
        if (!string.IsNullOrEmpty(product.HB货号) && 
            itemNumberToProductCodeMap.TryGetValue(product.HB货号, out var dicProductCode))
        {
            if (dicProductCode != product.商品编码)
            {
                _logger.LogInformation("商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）", 
                    product.HB货号, product.商品编码, dicProductCode);
                correctProductCode = dicProductCode;
                product.商品编码 = dicProductCode;  // 修正商品编码
                productCodeCorrectionCount++;
            }
        }
        
        mergedProducts[correctProductCode] = product;
    }
}
```

### 3. 修正 HQ 数据的商品编码

**文件：** `BlazorApp.Api/Services/DataSyncService.cs`  
**位置：** 第 1870-1918 行

```csharp
// 再用HQ数据覆盖（HQ数据优先级更高），但保留HBSales的装箱数和体积数据
foreach (var product in hqProducts)
{
    if (!string.IsNullOrEmpty(product.商品编码))
    {
        // 🔥 检查是否需要根据货号修正商品编码
        var correctProductCode = product.商品编码;
        if (!string.IsNullOrEmpty(product.HB货号) && 
            itemNumberToProductCodeMap.TryGetValue(product.HB货号, out var dicProductCode))
        {
            if (dicProductCode != product.商品编码)
            {
                _logger.LogInformation("商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）", 
                    product.HB货号, product.商品编码, dicProductCode);
                correctProductCode = dicProductCode;
                product.商品编码 = dicProductCode;  // 修正商品编码
                productCodeCorrectionCount++;
            }
        }
        
        // 如果已存在HBSales数据，保留其装箱数和体积字段
        if (mergedProducts.ContainsKey(correctProductCode))
        {
            var existingProduct = mergedProducts[correctProductCode];
            var preservedPackingQuantity = existingProduct.单件装箱数;
            var preservedUnitVolume = existingProduct.单件体积;
            
            // 用HQ数据覆盖
            mergedProducts[correctProductCode] = product;
            
            // 恢复HBSales的装箱数和体积数据（如果有值的话）
            if (preservedPackingQuantity.HasValue && preservedPackingQuantity.Value > 0)
            {
                mergedProducts[correctProductCode].单件装箱数 = preservedPackingQuantity;
                _logger.LogDebug("商品 {ProductCode} 保留HBSales装箱数: {PackingQuantity}", correctProductCode, preservedPackingQuantity);
            }
            if (preservedUnitVolume.HasValue && preservedUnitVolume.Value > 0)
            {
                mergedProducts[correctProductCode].单件体积 = preservedUnitVolume;
                _logger.LogDebug("商品 {ProductCode} 保留HBSales体积: {UnitVolume}", correctProductCode, preservedUnitVolume);
            }
        }
        else
        {
            // 如果不存在HBSales数据，直接使用HQ数据
            mergedProducts[correctProductCode] = product;
        }
    }
}

if (productCodeCorrectionCount > 0)
{
    _logger.LogInformation("✅ 根据DIC表货号映射，成功修正 {Count} 个商品的商品编码", productCodeCorrectionCount);
}
```

---

## 📊 数据表结构

### DIC_商品信息字典表（旧版）

| 字段名 | 类型 | 说明 |
|--------|------|------|
| `H货号` | string | 商品货号 |
| `H商品编码` | string | **商品编码（权威）** |
| `H商品名称` | string | 商品名称 |
| `H零售价` | decimal | 零售价格 |

### CPT_DIC_商品信息字典表（新版）

| 字段名 | 类型 | 说明 |
|--------|------|------|
| `HB货号` | string | 商品货号 |
| `商品编码` | string | 商品编码（**可能不一致**） |
| `中文名称` | string | 商品中文名称 |
| `英文名称` | string | 商品英文名称 |
| `进口价格` | decimal? | 进口价格 |
| `贴牌价格` | decimal? | 贴牌价格 |
| `单件装箱数` | decimal? | 装箱数量 |
| `单件体积` | decimal? | 单件体积 |

---

## 🎯 功能特性

### ✅ 自动修正

1. **货号匹配** - 通过货号（HB货号）匹配 DIC 表记录
2. **商品编码修正** - 自动将 CPT 表的商品编码修正为 DIC 表的商品编码
3. **日志记录** - 记录每一次修正操作，便于追踪

### ✅ 数据保留

1. **保留装箱数** - 从 HBSales 保留单件装箱数数据
2. **保留体积** - 从 HBSales 保留单件体积数据
3. **HQ 数据优先** - 其他字段以 HQ 数据为准

### ✅ 统计信息

```
日志示例：
- 从HQ获取到旧版DIC表 15,234 条商品数据
- 建立货号到商品编码映射表，共 15,234 个货号
- 商品编码修正: 货号 HB001 的商品编码从 P12345 修正为 P00001（DIC表）
- ✅ 根据DIC表货号映射，成功修正 523 个商品的商品编码
- 合并后共有 15,780 条唯一商品数据
```

---

## 🔍 使用场景

### 场景 1：历史数据迁移

**背景：** 系统从旧版 DIC 表迁移到新版 CPT 表

**问题：** 
- DIC 表：货号 `HB001` → 商品编码 `P00001`
- CPT 表：货号 `HB001` → 商品编码 `P12345` ❌

**解决：** 
- 系统自动检测货号匹配
- 修正 CPT 表的商品编码为 `P00001` ✅

### 场景 2：数据一致性维护

**背景：** 多个系统维护商品数据，商品编码不一致

**问题：**
- DIC 表（权威）：`HB002` → `P00002`
- HBSales CPT：`HB002` → `P23456` ❌
- HQ CPT：`HB002` → `P34567` ❌

**解决：**
- 两个 CPT 表的数据都修正为 `P00002` ✅
- 保留 HBSales 的装箱数和体积
- 其他数据以 HQ 为准

---

## 📝 日志示例

### 正常修正日志

```
[Information] 开始从HQ同步国内商品数据（新版本：增量更新模式）
[Information] 从HQ获取到旧版DIC表 15234 条商品数据
[Information] 建立货号到商品编码映射表，共 15234 个货号
[Information] 从HBSales获取到 15780 条商品数据
[Information] 从HQ获取到 15890 条商品数据
[Information] 商品编码修正: 货号 HB001 的商品编码从 P12345 修正为 P00001（DIC表）
[Information] 商品编码修正: 货号 HB002 的商品编码从 P23456 修正为 P00002（DIC表）
[Information] 商品编码修正: 货号 HB003 的商品编码从 P34567 修正为 P00003（DIC表）
...
[Information] ✅ 根据DIC表货号映射，成功修正 523 个商品的商品编码
[Information] 合并后共有 15890 条唯一商品数据，已保留HBSales的装箱数和体积数据
[Information] AutoMapper批量转换完成，共转换 15890 个商品（包含图片URL智能处理）
```

---

## 🎨 优化建议

### 性能优化

1. **批量查询** - 使用 `Where` 条件过滤空货号和空商品编码
2. **字典映射** - 使用 `Dictionary` 提升查找性能（O(1)）
3. **日志优化** - 修正日志使用 `Information` 级别，详细日志使用 `Debug` 级别

### 数据质量

1. **去重处理** - 同一货号多条记录时，取第一条
2. **空值过滤** - 过滤空货号和空商品编码的记录
3. **统计报告** - 记录修正次数，便于数据质量监控

---

## ✅ 测试验证

### 测试步骤

1. **准备测试数据**
   ```sql
   -- DIC 表
   INSERT INTO DIC_商品信息字典表 (H货号, H商品编码) VALUES ('TEST001', 'P00001');
   
   -- CPT 表（商品编码不同）
   INSERT INTO CPT_DIC_商品信息字典表 (HB货号, 商品编码) VALUES ('TEST001', 'P99999');
   ```

2. **执行数据同步**
   - 调用 `SyncDomesticProductsFromHQAsync()` 方法

3. **验证结果**
   ```sql
   -- 检查 DomesticProduct 表中的数据
   SELECT ProductCode, ItemNumber FROM DomesticProduct WHERE ItemNumber = 'TEST001';
   -- 预期：ProductCode = 'P00001'（DIC表的商品编码）
   ```

4. **检查日志**
   ```
   [Information] 商品编码修正: 货号 TEST001 的商品编码从 P99999 修正为 P00001（DIC表）
   [Information] ✅ 根据DIC表货号映射，成功修正 1 个商品的商品编码
   ```

---

## 📚 相关文档

- **数据同步服务：** `BlazorApp.Api/Services/DataSyncService.cs`
- **数据模型：** `BlazorApp.Shared/Models/HqEntities/`

---

**实现日期：** 2025-10-03  
**实现人：** AI Assistant  
**版本：** v1.0

