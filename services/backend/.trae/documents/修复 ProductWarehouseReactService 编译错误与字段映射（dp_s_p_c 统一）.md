问题概览

* 现有方法 `GetAntdTableDataAsync` 在重构后仍残留旧字段引用，导致编译错误：
  - 误用 `DomesticProduct.CategoryName`（应为 `WarehouseCategory.CategoryName`）
  - 误用 `WarehouseCategory.SupplierName/SupplierCode`（应为 `ChinaSupplier.SupplierName/SupplierCode`）
  - 误用 `Product.EnglishProductName/HBProductNo`（应为 `DomesticProduct.EnglishProductName/HBProductNo`）
  - 过滤/排序中的字段与联表角色未统一（dp/s/p/c）
  - 若干 `Contains` 的空值警告（在比较前需判空或改为数值比较）

根因分析

* 重构为“WarehouseProduct → DomesticProduct → ChinaSupplier + Product → WarehouseCategory”后，部分排序/过滤/投影仍沿用旧的 `p` 或错误的 `c` 字段，造成类型不匹配。
* 分类名来自 `Product.WarehouseCategoryGUID → WarehouseCategory.CategoryName`；供应商来自 `DomesticProduct.SupplierCode → ChinaSupplier`；英文名/货号/条码/图片来自 `DomesticProduct`。

修复方案（统一字段映射）

1) 联表链路（保持不变）：
- `WarehouseProduct w`
- `LeftJoin DomesticProduct dp on dp.ProductCode == w.ProductCode`
- `LeftJoin ChinaSupplier s on dp.SupplierCode == s.SupplierCode`
- `LeftJoin Product p on p.ProductCode == w.ProductCode`
- `LeftJoin WarehouseCategory c on p.WarehouseCategoryGUID == c.CategoryGUID`

2) 全局搜索与列过滤（行号约 447-512）：
- `productName → dp.ProductName`
- `nameEn → dp.EnglishProductName`
- `itemNumber → dp.HBProductNo`
- `barcode → dp.Barcode`
- `categoryName → c.CategoryName`
- `supplierName/supplierCode → s.SupplierName/s.SupplierCode`
- 数值列（`domesticPrice/importPrice/oemPrice/volume`）保持 `w.*`
- `productType → dp.ProductType`

3) 排序（520-547）：
- 将 `productname/name`、`nameEn`、`itemNumber`、`barcode` 排序改为 `dp.*`
- `categoryName` 用 `c.CategoryName`
- `supplierName/supplierCode` 用 `s.*`
- 数值列/`createdAt/updatedAt` 用 `w.*`

4) Select 投影（556-575）：
- `ProductName = dp.ProductName`
- `EnglishName = dp.EnglishProductName`
- `ItemNumber = dp.HBProductNo`
- `Barcode = dp.Barcode`
- `CategoryName = c.CategoryName`
- `SupplierName = s.SupplierName`
- `SupplierCode = s.SupplierCode`
- `ProductImage = dp.ProductImage`
- 其余价格、体积、状态、时间使用 `w.*`

5) 空值警告（494,497,500,503,510）：
- 在 `values.Contains(...)` 前判空，例如：`w.DomesticPrice != null && values.Contains(w.DomesticPrice.Value.ToString())`
- 更优做法：对数值列改为范围或等值比较，避免字符串 Contains：
  - `if (decimal.TryParse(values.FirstOrDefault(), out var v)) query = query.Where(w => w.DomesticPrice == v);`

6) 编译与验证：
- `dotnet build` 验证通过
- 前端现有字段与排序/过滤键对应：`itemNumber/barcode/productName/supplierName/supplierCode/...` 运行正常

实施步骤

* 按上述 2)-4) 在 `BlazorApp.Api/Services/React/ProductWarehouseReactService.cs` 替换排序、过滤、Select 中的字段引用为 dp/s/p/c 正确映射。
* 补充数值过滤的判空或等值比较以消除警告。
* 重新编译并联调前端表格过滤与排序。