## 目标

* 基于 `ProductSetCode` 表实现 React 前端的多码套装商品管理页面，支持服务器端分页、排序、过滤与批量操作。

* 后端新增 React 专用控制器与服务层，提供网格数据接口与批量操作接口。

* 定义必要的 React DTO/类型，以保证前后端字段一致与可维护。

## 现有基础与字段来源

* 表模型：`BlazorApp.Shared/Models/HBweb/ProductSetCode.cs`（套装货号、套装条码、采购价、零售价、数量、类型、是否启用、导航到 Product）

* 商品模型：`BlazorApp.Shared/Models/HBweb/Product.cs`（`ItemNumber` 商品货号、`Barcode` 主条码、`LocalSupplierCode` 本地供应商编码）

* 供应商模型：`BlazorApp.Shared/Models/HBweb/LocalSupplier.cs`（`HBLocalSupplier`，包含 `LocalSupplierCode` 与 `Name`）

* 基础审计字段：`BlazorApp.Shared/Models/HBweb/BaseEntity.cs` 提供 `UpdatedAt`、`UpdatedBy` 可直接在前端显示

* 通用网格请求/响应：`BlazorApp.Shared/DTOs/GridRequestDto.cs` 与 `GridResponseDto<T>` 可直接复用

* SqlSugar 上下文已暴露访问器：`BlazorApp.Api/Data/SqlSugarContext.cs` 包含 `ProductSetCodeDb`、`ProductDb`、`HBLocalSupplierDb`

## 后端接口设计

### 控制器

* 新增 `ReactProductSetCodesController`（路径：`BlazorApp.Api/Controllers/React/ReactProductSetCodesController.cs`）

* 路由前缀：`/api/react/v1/product-set-codes`

* 认证与授权：

  * 查询接口 `Authorize(Roles = "Admin,WarehouseManager")`

  * 修改/删除接口 `Authorize(Roles = "Admin")`

### 服务层

* 新增接口：`BlazorApp.Api/Interfaces/React/IProductSetCodeReactService.cs`

* 新增实现：`BlazorApp.Api/Services/React/ProductSetCodeReactService.cs`

* 依赖 `SqlSugarContext`，使用联表查询：`ProductSetCode` ←→ `Product`（`ProductCode`）←→ `HBLocalSupplier`（`LocalSupplierCode`）

### DTO（后端）

* 列表项 DTO：`ProductSetCodeGridDto`

  * `setCodeId`（主键）

  * `productCode`

  * `supplierCode`、`supplierName`

  * `itemNumber`（商品货号，来自 Product）

  * `barcode`（主条码，来自 Product）

  * `setItemNumber`（套装货号，来自 ProductSetCode）

  * `setBarcode`（套装条码，来自 ProductSetCode）

  * `setPurchasePrice`、`setRetailPrice`

  * `isActive`

  * `updatedAt`、`updatedBy`

* 批量操作请求 DTO：

  * `BatchUpdateStatusDto`：`ids: string[]`，`isActive: bool`

  * `BatchUpdatePricesDto`：`items: { id: string; setPurchasePrice?: number; setRetailPrice?: number; }[]`

  * `BatchDeleteRequestDto`（已有同名可复用或新建针对本资源的版本是react结尾）

### 网格数据接口

* `POST /grid`（请求体：`GridRequestDto`，响应体：`GridResponseDto<ProductSetCodeGridDto>`）

* 功能：

  * 全局搜索（OR）：`supplierName/supplierCode/itemNumber/barcode/setItemNumber/setBarcode`

  * 列过滤（AND）：映射 `FilterModel`（文本、数字、日期、集合）到 SqlSugar 条件

  * 排序：映射 `SortModel` 到相应字段

  * 分页：使用 `StartRow/EndRow/PageSize`

* 默认排序：`updatedAt desc`

### 批量操作接口

* `PUT /batch-status`：批量启用/禁用

* `PUT /batch-prices`：批量更新采购价/零售价

* `DELETE /batch-delete`：批量软删除（置 `IsDeleted=true`）

### 查询与映射要点

* 连接：

  * `ProductSetCode` join `Product` on `ProductSetCode.ProductCode == Product.ProductCode`

  * `Product` left join `HBLocalSupplier` on `Product.LocalSupplierCode == HBLocalSupplier.LocalSupplierCode`

* 字段映射：

  * 供应商显示：优先 `supplierName`（HBLocalSupplier.Name），同时保留 `supplierCode`

  * 商品货号：`Product.ItemNumber`（`BlazorApp.Shared/Models/HBweb/Product.cs`）

  * 主条码：`Product.Barcode`

  * 审计字段：`UpdatedAt/UpdatedBy`（`BlazorApp.Shared/Models/HBweb/BaseEntity.cs`）

## 前端实现（React Umi）

### 目录结构

* `ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`：主页面（AG Grid 服务端模式）

* `ReactUmi/my-app/src/pages/MultiCodeSets/columnDefs.tsx`：列定义与筛选器

* `ReactUmi/my-app/src/services/multiCodeSet.ts`：API 服务

* `ReactUmi/my-app/src/types/multiCodeSet.ts`：类型与DTO定义

### 列与功能

* 列展示：

  * `supplierName`（供应商）

  * `itemNumber`（商品货号）

  * `barcode`（主条码）

  * `setItemNumber`（套装货号）

  * `setBarcode`（套装条码）

  * `setPurchasePrice`（进货价）

  * `setRetailPrice`（零售价）

  * `updatedAt`（更新日期）

  * `updatedBy`（更新人）

* 支持：

  * 顶部全局搜索（OR）

  * 列头筛选（AND）

  * 服务器端排序/过滤/分页（复用 `GridRequestDto` 协议）

  * 批量操作（启用/禁用、批量更新价格、批量删除）

### 前端服务 API

* `getGridData(params: GridDataRequest)` → `POST /api/react/v1/product-set-codes/grid`

* `batchUpdateStatus(ids: string[], isActive: boolean)` → `PUT /api/react/v1/product-set-codes/batch-status`

* `batchUpdatePrices(items: { id: string; setPurchasePrice?: number; setRetailPrice?: number; }[])` → `PUT /api/react/v1/product-set-codes/batch-prices`

* `batchDelete(ids: string[])` → `DELETE /api/react/v1/product-set-codes/batch-delete`

## 权限与校验

* 所有接口需登录；删除与批量修改仅限 Admin 角色。

* 服务层对输入进行验证：

  * 价格非负、`SetQuantity > 0`、`SetType ∈ [1,3]`（可复用 `ValidateSetData()`）

  * 软删除统一通过 `IsDeleted` 字段

## 测试与验证

* 后端：

  * 使用模拟数据在 `SqlSugarContext` 进行联表查询验证与计数查询验证

  * 单元测试覆盖：过滤组合（AND）、全局搜索（OR）、排序映射与分页区间

* 前端：

  * 使用 AG Grid Server-Side Row Model，验证滚动分页、排序与筛选行为

  * 批量操作后刷新网格与选中项清理

## 交付内容

* 后端：控制器 + 服务 + DTO（React 专用）

* 前端：页面 + 列定义 + 服务 + 类型

* 文档：接口说明与前后端字段映射（放入现有 `REACT_API_IMPLEMENTATION.md` 类文档）

## 下一步

* 按上述方案创建后端文件与前端页面，实现并自测；如需供应商来源调整（本地/中国供应商），可在服务层切换联表至 `ChinaSupplier` 并映射 `SupplierName`。

