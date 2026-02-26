## 问题
- 条码检测 `DetectBarcodeAsync` 中直接在 `Select` 使用导航属性 `m.Product.ProductName`，SqlSugar生成SQL引用列名 `Product`，导致“列名 'Product' 无效”的SQL错误。

## 方案
- 在查询 `StoreMultiCodeProduct` 时显式左联 `Product` 表，通过 `m.ProductCode == p.ProductCode` 关联，使用 `p.ProductName` 获取名称。
- 替换原有 `Select(m => new { m.ProductCode, Name = m.Product... })` 为 `Select((m,p) => new { m.ProductCode, Name = p.ProductName })`。
- 其余逻辑保持不变：合并 `list1` 与 `list2` 的 `ProductCode` 和名称、去重、统计匹配数与是否超过2。

## 验证
- 条码检测接口执行无SQL错误，返回结果包含来自多码表关联商品的名称，匹配数统计正确。