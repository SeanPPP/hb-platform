改造目标

* 供应商链路：以 WarehouseProduct 的 ProductCode 关联 DomesticProduct，再以 DomesticProduct.SupplierCode 关联 ChinaSupplier，填充 SupplierName/SupplierCode。
* 分类链路：以 Product 的 WarehouseCategoryGUID 关联 WarehouseCategory，填充 CategoryName。
* 删除冗余：移除二次 ChinaSupplier 左联与旧的 LocalSupplierCode 供应商联表；不再使用 DomesticProduct 的 HBProductNo/Barcode 兜底方案以外的子查询。

具体改动（后端）

* 更新查询：
  - 基表：WarehouseProduct w
  - 左联：DomesticProduct dp（dp.ProductCode == w.ProductCode）
  - 左联：ChinaSupplier s（dp.SupplierCode == s.SupplierCode）
  - 左联：Product p（p.ProductCode == w.ProductCode）
  - 左联：WarehouseCategory c（p.WarehouseCategoryGUID == c.CategoryGUID）

* 过滤与排序映射：
  - 文本过滤：productName→dp.ProductName；nameEn→dp.EnglishProductName；itemNumber→dp.HBProductNo；barcode→dp.Barcode；categoryName→c.CategoryName；supplierName/Code→s.*
  - 数值过滤：domesticPrice/importPrice/oemPrice/volume→w.*
  - 排序键：同上所有列 + createdAt/updatedAt→w.CreatedAt/w.UpdatedAt
  - 分类多选过滤：按 p.WarehouseCategoryGUID in 展开集合

* 字段选择：
  - 图片：dp.ProductImage
  - 货号/条码/名称：dp.HBProductNo/dp.Barcode/dp.ProductName（英文名 dp.EnglishProductName）
  - 供应商：s.SupplierName/s.SupplierCode
  - 分类：c.CategoryName
  - 价格/体积/状态/时间：w.DomesticPrice/w.ImportPrice/w.OEMPrice/w.Volume/w.IsActive/w.CreatedAt/w.UpdatedAt

验证

* dotnet build 验证编译通过
* 使用现有前端接口 `POST /api/react/v1/product-warehouse/table` 验证筛选与排序；供应商、分类列正确填充。

如确认，我将按以上方案重构 `GetAntdTableDataAsync` 方法并完成编译与联调。