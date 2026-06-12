## 修复 ChinaSupplierCode 未填充问题

### 问题分析
用户指出 `SyncPosmProductSupplierMappingsAsync` 方法中 `ChinaSupplierCode` 没有被正确填充。

在当前代码中：
```csharp
                        if (product.LocalSupplierCode == "200")
                        {
                            var warehouseProduct = await _localContext
                                .Db.Queryable<WarehouseProduct>()
                                .Includes(wp => wp.DomesticProduct)
                                .Where(wp => wp.ProductCode == product.ProductCode && !wp.IsDeleted)
                                .FirstAsync();
                            
                            // ...
```
这段代码在一个 `foreach` 循环中，对每个 `LocalSupplierCode == "200"` 的商品都执行了一次数据库查询 (`await ... FirstAsync()`)。这有两个问题：
1.  **性能极差**：如果有一万个商品，就会执行一万次数据库查询，极慢。
2.  **数据填充逻辑依赖**：如果 `warehouseProduct` 没查到（例如 `ProductCode` 匹配问题），或者 `DomesticProduct` 导航属性加载失败，或者 `chinaSuppliers` 字典里没这个 code，`ChinaSupplierCode` 就会保持 `null`。

虽然性能差，但逻辑上应该能工作，前提是数据都能匹配上。用户反馈“都是空的”，说明匹配环节出了问题。

**可能的原因**：
1.  `chinaSuppliers` 字典构建时可能遗漏了某些供应商。
2.  `WarehouseProduct` 查询时 `wp.ProductCode == product.ProductCode` 匹配失败（虽然我们刚才确认了应该用 ProductCode）。
3.  最可能的原因：**在循环内查询数据库是不可靠且极慢的**，而且可能存在上下文并发问题（尽管这里是顺序执行）。

### 优化方案
将循环内的逐条查询改为**批量内存匹配**。

### 实施计划
1.  **批量加载 `WarehouseProduct`**：
    *   在循环开始前，一次性加载所有 `LocalSupplierCode == "200"` 的商品对应的 `WarehouseProduct` 及其 `DomesticProduct`。
    *   构建一个 `Dictionary<string, string>`，键为 `ProductCode`，值为 `ChinaSupplierCode`。

2.  **移除循环内的数据库查询**：
    *   在 `foreach` 循环中，直接从上述字典中获取 `ChinaSupplierCode`。

3.  **重构代码逻辑**：
    *   先筛选出 supplier 200 的 product codes。
    *   批量查询 `WarehouseProduct`。
    *   批量查询 `ChinaSupplier`（这一步现有的代码已经做了，但可以优化）。
    *   构建映射字典。
    *   遍历所有 products，直接赋值。

### 代码逻辑预览
```csharp
// 1. 获取所有 200 号供应商的 ProductCode
var productCodes200 = products.Where(p => p.LocalSupplierCode == "200").Select(p => p.ProductCode).ToList();

// 2. 批量查询 WarehouseProduct 并包含 DomesticProduct
var warehouseProducts = await _localContext.Db.Queryable<WarehouseProduct>()
    .Includes(wp => wp.DomesticProduct)
    .Where(wp => productCodes200.Contains(wp.ProductCode) && !wp.IsDeleted)
    .ToListAsync();

// 3. 构建 ProductCode -> ChinaSupplierCode 的字典
var productChinaSupplierDict = new Dictionary<string, string>();
foreach (var wp in warehouseProducts) {
    if (wp.DomesticProduct?.SupplierCode != null) {
        productChinaSupplierDict[wp.ProductCode] = wp.DomesticProduct.SupplierCode;
    }
}

// 4. 遍历赋值
foreach (var product in products) {
    // ...
    if (product.LocalSupplierCode == "200") {
        if (productChinaSupplierDict.TryGetValue(product.ProductCode, out var code)) {
             mapping.ChinaSupplierCode = code;
        }
    }
    // ...
}
```
这样不仅修复了潜在的数据获取问题，还将性能提升了几个数量级。