问题与根因

1) 字段错误引用
- 将 `CategoryName` 误指向 `DomesticProduct`；实际应为 `WarehouseCategory.CategoryName`
- 将 `SupplierName/SupplierCode` 误指向 `WarehouseCategory`；实际应为 `ChinaSupplier.SupplierName/SupplierCode`
- 将 `EnglishProductName/HBProductNo` 误指向 `Product`；实际应为 `DomesticProduct.EnglishProductName/HBProductNo`

2) 过滤/排序映射不统一
- 部分条件仍沿用旧的 `(w,p,c,s)` 角色，重构后应统一为 `(w,dp,s,p,c)`，对应字段：
  - 文本：`productName/name→dp.ProductName`、`nameEn→dp.EnglishProductName`、`itemNumber→dp.HBProductNo`、`barcode→dp.Barcode`、`categoryName→c.CategoryName`、`supplierName/Code→s.*`
  - 数值：`domesticPrice/importPrice/oemPrice/volume→w.*`
  - 时间：`createdAt/updatedAt→w.*`

3) 空值与数值过滤警告
- `values.Contains(w.DomesticPrice?.ToString())` 等可能传入 null；建议对数值解析为 `decimal` 做等值或范围比较，并在比较前判空。

修复计划

A. 统一联表链路（保持现状，确保顺序）：
- `WarehouseProduct w`
- `LeftJoin DomesticProduct dp on dp.ProductCode == w.ProductCode`
- `LeftJoin ChinaSupplier s on dp.SupplierCode == s.SupplierCode`
- `LeftJoin Product p on p.ProductCode == w.ProductCode`
- `LeftJoin WarehouseCategory c on p.WarehouseCategoryGUID == c.CategoryGUID`

B. 修正过滤/排序/搜索映射（GetAntdTableDataAsync 内）：
- 全局搜索：改为 `dp.ProductName/dp.EnglishProductName/dp.HBProductNo/dp.Barcode/c.CategoryName/s.SupplierName`
- 列过滤：将 `productName/name/nameEn/itemNumber/barcode/categoryName/supplierName/supplierCode` 分别映射到 `dp/c/s` 正确字段；数值列映射 `w.*`；`productType→dp.ProductType`
- 排序：将上述文本列映射到 `dp/c/s`，数值/时间映射到 `w.*`；保留默认 `updatedAt desc`

C. 修正 Select 投影：
- `ProductName=dp.ProductName`、`EnglishName=dp.EnglishProductName`、`ItemNumber=dp.HBProductNo`、`Barcode=dp.Barcode`
- `CategoryName=c.CategoryName`、`SupplierName=s.SupplierName`、`SupplierCode=s.SupplierCode`
- `ProductImage=dp.ProductImage`、价格/体积/状态/时间取 `w.*`

D. 数值过滤改造与判空：
- 对 `domesticPrice/importPrice/oemPrice/volume`，将单值过滤改为：
  - `if (decimal.TryParse(values.FirstOrDefault(), out var v)) query = query.Where(w => w.DomesticPrice != null && w.DomesticPrice == v)`；
- 若后续需要范围过滤，支持 `[min,max]` 两值解析。

E. 验证与整理：
- 修正后运行 `dotnet build`，确保错误消除，保留必要 using 与格式
- 前端现有排序/过滤键与后端字段一致，验证示例：货号/条码/名称/供应商/供应商代码/价格/创建/更新列排序与过滤

交付内容
- 更新 `BlazorApp.Api/Services/React/ProductWarehouseReactService.cs` 的 `GetAntdTableDataAsync` 方法，统一 dp/s/p/c 的字段引用
- 清除所有诊断中的错误项与空值警告（数值过滤判空/解析）
- 保留现有分页与分类多选（含子类）过滤逻辑