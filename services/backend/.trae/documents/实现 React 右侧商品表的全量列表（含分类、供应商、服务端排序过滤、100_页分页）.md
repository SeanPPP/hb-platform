实施范围

* 后端新增 Antd Table 列表接口，数据源为所有商品（WarehouseProduct 关联 Product），不与左侧联动
* 表格上方增加树形分类选择器（多选、包含子分类）作为过滤条件传入接口
* 为列表数据增加 SupplierCode 字段，同时返回 SupplierName

接口与路由

* 控制器：`BlazorApp.Api/Controllers/React/ReactProductWarehouseController.cs`
  * 新增：`[HttpPost("table")]` → `POST /api/react/v1/product-warehouse/table`（Authorize 与现有保持一致）
  * 入参：`ReactTableRequestDto`（Antd Table 请求体）
  * 出参：`ReactTableResponseDto<WarehouseProductReactListDto>`（或复用 `GridResponseDto<T>`）

DTO 设计

* `ReactTableRequestDto`
  * `int Page = 1`、`int PageSize = 100`
  * `string? SortBy`、`string SortOrder = "ascend" | "descend"`
  * `string? GlobalSearch`
  * `Dictionary<string, string[]>? Filters`（列过滤，例如：`{"categoryName": ["玩具"], "supplierName": ["义乌A店"], "itemNumber": ["HB001"]}`）
  * `List<string>? CategoryGuids`（树形分类多选）
  * `bool IncludeSubCategories = true`

* `WarehouseProductReactListDto`
  * `string ProductCode`（前端隐藏）
  * `string? ProductName`、`string? EnglishName`、`string? ItemNumber`、`string? Barcode`
  * `string? CategoryName`（来自 `WarehouseCategory`）
  * `string? SupplierName`、`string? SupplierCode`
  * `decimal? DomesticPrice`、`decimal? OEMPrice`、`decimal? ImportPrice`、`decimal? Volume`
  * `bool IsActive`、`DateTime UpdatedAt`、`string? ProductImage`

* `ReactTableResponseDto<T>` 或复用 `GridResponseDto<T>`：`Items`、`Total`

服务实现

* 位置：`BlazorApp.Api/Services/React/ProductWarehouseReactService.cs`
* 新增方法：`Task<ReactTableResponseDto<WarehouseProductReactListDto>> GetAntdTableDataAsync(ReactTableRequestDto request)`
* 查询构建：
  1. 基础：`_db.Queryable<WarehouseProduct>()`
  2. 左联：`Product`（`wp.ProductCode == p.ProductCode`）、`WarehouseCategory`（`p.WarehouseCategoryGUID == c.CategoryGUID`）
  3. 供应商名称与代码：
     - 主路径：左联 `ChinaSupplier`（`p.LocalSupplierCode == s.SupplierCode`）取 `SupplierName/SupplierCode`
     - 兜底：子查询 `DomesticProduct`（`HBProductNo == p.ItemNumber` 或 `Barcode == p.Barcode`）再联 `ChinaSupplier` 取 `SupplierName/SupplierCode`
  4. 分类过滤（树形多选，含子类）：
     - 如 `request.CategoryGuids?.Any()` 为真：展开所有子类 GUID（复用 `WarehouseProductService.GetCategoryAndSubCategories` 的递归逻辑，或在本服务中实现等价方法），按 `p.WarehouseCategoryGUID in (展开集合)` 过滤
  5. 全局搜索（OR）：`p.ProductName`、`p.ItemNumber`、`p.Barcode`、`c.CategoryName`、`SupplierName`（非空 Contains）
  6. 列过滤（AND）：根据 `Filters` 键映射到字段
     - 文本：`name/productName → p.ProductName`，`nameEn → p.EnglishName`，`itemNumber → p.ItemNumber`，`barcode → p.Barcode`，`categoryName → c.CategoryName`，`supplierName → s.SupplierName`
     - 数值：`domesticPrice → wp.DomesticPrice`，`oemPrice → wp.OEMPrice`，`importPrice → wp.ImportPrice`，`volume → wp.Volume`
     - 布尔/集合：`isActive → wp.IsActive`，`productType → p.ProductType`
  7. 排序：`SortBy` + `SortOrder` 映射到字段；默认 `UpdatedAt desc`
  8. 统计总数：复制过滤条件的 `CountAsync`
  9. 分页：`Skip((Page-1)*PageSize)` + `Take(PageSize)`；`Select` 投影为 `WarehouseProductReactListDto`

控制器调用

* 在 `ReactProductWarehouseController` 的 `Table` 动作中调用服务：
  * 校验请求体 → 调用 `GetAntdTableDataAsync` → `Ok({ success: true, data: items, total })`

前端联动（简述）

* 树形分类数据源：`GET /api/react/v1/warehouse-categories/tree`；多选勾选后将 `CategoryGuids[]` 与 `IncludeSubCategories=true` 传入表格接口
* Antd Table：
  * 隐藏列 `productCode`
  * 显示 `categoryName`、`supplierName`、`supplierCode`
  * `onChange` 将 `pagination/sorter/filters` 映射到 `ReactTableRequestDto`
  * 默认分页大小 `100`

验证用例

1. 多选分类（含子类）过滤生效，结果与左侧无联动
2. `SupplierName/SupplierCode` 在三种来源场景（LocalSupplierCode、DomesticProduct 兜底、缺省）显示正确
3. 列过滤与排序在服务端生效，总数与分页一致
4. 隐藏 `productCode` 列，其他列正确显示

性能与安全

* 仅选择必要字段投影；分页+排序优先使用已有索引（参考 `BlazorApp.Api/Data/WarehouseProductIndexes.sql`）
* 保持 `[Authorize]` 与现有 React 控制器一致，避免未授权访问

如确认该方案，我将据此新增控制器动作、DTO 与服务方法实现，并提交可运行的后端接口，随后提供前端调用示例（Antd Table 请求与列定义）。