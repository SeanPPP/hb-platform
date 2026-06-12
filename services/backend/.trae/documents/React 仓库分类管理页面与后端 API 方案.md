## 目标与范围

* 在 `d:\Development\cline\blazor\ReactUmi\my-app` 新增“仓库分类管理”页面：树形展示分类，完成分类增删改查，支持与商品的分类批量关联/取消关联。

* 后端在 `BlazorApp.Api.Controllers.React` 下新增 React 专用分类控制器与服务层，按照现有 React 控制器模式与原有服务解耦。

* 分类树数据基于 `WarehouseCategory` 自引用结构，商品分类关系使用 `Product.WarehouseCategoryGUID` 字段实现（`BlazorApp.Shared.Models\HBweb\Product.cs`:100）。

## 后端设计

* 控制器：新增 `ReactWarehouseCategoriesController`

  * 路由前缀：`/api/react/v1/warehouse-categories`

  * 启用 `[Authorize]`，返回格式与现有 React 控制器保持一致（参考 `BlazorApp.Api.Controllers.React\ReactDomesticProductsController.cs`:13-16, 47-57）。

* 服务接口与实现：新增到 `BlazorApp.Api.Interfaces.React` 与 `BlazorApp.Api.Services.React`

  * `IWarehouseCategoryReactService`：定义分类树、CRUD、分页、批量移动/启用、排序、商品关联批量操作等方法。

  * `WarehouseCategoryReactService`：使用 `SqlSugarContext` 数据访问（参考 `BlazorApp.Api.Data\SqlSugarContext.cs`:64-76），复用 AutoMapper 映射（参考 `BlazorApp.Api.Mappings.Profiles\WarehouseMappingProfile.cs`:21-49）。

* DTO 与映射：复用现有 DTO

  * 分类：`WarehouseCategoryDto`、`CreateWarehouseCategoryDto`、`UpdateWarehouseCategoryDto`（`BlazorApp.Shared.DTOs\WarehouseCategoryDto.cs`）。

  * 分页返回：复用 `PagedResult<T>`（已在服务中使用，参考 `BlazorApp.Api.Services\WarehouseCategoryService.cs`:87-140）。

  * 批量操作新增 DTO：

    * `BatchMoveCategoriesDto { List<string> CategoryGuids; string? NewParentGuid }`

    * `BatchToggleActiveDto { List<string> CategoryGuids; bool IsActive }`

    * `BatchSortRequestDto { List<{ string CategoryGuid; int SortOrder }>} `&#x20;

    * `BatchAssignProductsRequestDto { string CategoryGuid; List<string> ProductCodes }`

    * `BatchUnassignProductsRequestDto { List<string> ProductCodes }`

* 删除约束与校验：

  * 不允许删除存在子类或存在商品的分类（复用逻辑 `BlazorApp.Api.Services\WarehouseCategoryService.cs`:230-236）。

  * 更新时禁止自指向父类与环形依赖（`WarehouseCategoryService.cs`:195-201）。

* 接口列表：

  * `GET /tree`：返回整棵分类树 `WarehouseCategoryDto[]`（含 `Children`）。

  * `GET /list`：支持名称、启用状态、父类、排序、分页查询，返回 `PagedResult<WarehouseCategoryDto>`。

  * `POST /`：创建分类，入参 `CreateWarehouseCategoryDto`。

  * `PUT /{categoryGuid}`：更新分类，入参 `UpdateWarehouseCategoryDto`。

  * `DELETE /{categoryGuid}`：删除分类，含约束校验。

  * `POST /batch/move`：批量移动分类到新父类。

  * `POST /batch/toggle-active`：批量启用/停用分类。

  * `POST /batch/sort`：批量更新排序。

  * `GET /{categoryGuid}/products`：查询该分类下商品（分页、关键字、库存过滤等，复用 `WarehouseProductFilterDto`）。

  * `POST /{categoryGuid}/products/batch-assign`：按商品编码批量设置 `Product.WarehouseCategoryGUID = categoryGuid`。

  * `POST /products/batch-unassign`：批量清除商品分类。

* 错误处理与日志：

  * 统一返回 `{ success, data?, message, errorCode? }`，参照现有 React 控制器返回形态（`ReactDomesticProductsController.cs`:47-64）。

## 前端设计

* 路由与菜单：在 `.umirc.ts` 中新增一级导航“仓库管理”，子菜单“分类管理”。

  * 参考现有分组结构（`ReactUmi\my-app\.umirc.ts`:123-148）。

* 页面结构：`src/pages/WarehouseCategories`

  * 左侧：`Tree` 展示分类树，支持选择、展开、右键菜单（新增子类、重命名、删除、启用/停用、移动）。

  * 右侧：`ProTable`/`EnhancedDataGrid` 展示该分类下商品，可搜索筛选。

  * 顶部工具栏：分类批量操作（移动、启用/停用、排序）、商品批量操作（批量关联/取消关联）。

* 主要交互：

  * 分类 CRUD：弹窗表单使用 `ModalForm`（`@ant-design/pro-components`），校验与后端一致。

  * 批量移动：支持选择多个分类节点，指定新的父类。

  * 批量启用/停用：批量切换 `IsActive`。

  * 批量排序：为选中节点设置 `SortOrder`。

  * 商品批量操作：在右侧表格勾选商品，批量关联到当前分类或取消关联。

* 服务封装：新增 `src/services/warehouseCategory.ts`

  * 方法：`getCategoryTree()`, `getCategoryList(filter)`, `createCategory(dto)`, `updateCategory(dto)`, `deleteCategory(guid)`, `batchMove(payload)`, `batchToggleActive(payload)`, `batchSort(payload)`, `getProductsByCategory(guid, filter)`, `batchAssignProducts(guid, productCodes)`, `batchUnassignProducts(productCodes)`。

  * 请求基地址：`/api/react/v1/warehouse-categories`，与后端保持一致。

* 体验优化：

  * 树数据懒加载与缓存；删除/移动操作二次确认；批量结果反馈统计。

  * 表格分页、关键字搜索、导出选中项。

## 数据与模型参考

* 分类实体：`BlazorApp.Shared.Models\HBweb\WarehouseCategory.cs`（自引用 `ParentGUID` 与 `Children` 导航，树形结构）。

* 商品实体：`BlazorApp.Shared.Models\HBweb\Product.cs`（分类字段 `WarehouseCategoryGUID`，导航 `WarehouseCategory`）。

* 服务与映射：

  * 分类服务示例：`BlazorApp.Api.Services\WarehouseCategoryService.cs`（已有 CRUD、分页、校验逻辑）。

  * Mapper 配置：`BlazorApp.Api.Mappings.Profiles\WarehouseMappingProfile.cs`（分类与商品映射）。

## 权限与安全

* 控制器统一加 `[Authorize]`；部分批量接口限制 `Roles = "Admin,WarehouseManager"`。

* 服务层统一校验空值与环形依赖，删除约束防止数据不一致。

## 验证与测试

* 后端：

  * 单元测试分类 CRUD 与删除约束；批量操作成功/失败路径；产品分类批量关联正确性。

* 前端：

  * 交互测试：树节点增删改、批量移动，表格批量关联/取消关联；错误提示与回滚。

  * 端到端：在开发代理（`.umirc.ts` 的 `/api -> :5001`）下联调。

## 交付物

* 后端：React 分类控制器与服务接口/实现、批量操作 DTO。

* 前端：新页面、路由与菜单、服务封装、分类树与商品表、批量操作交互。

## 风险与注意事项

* 删除前必须无子类且无商品（已有校验）；移动与排序需要事务保障一致性。

* 批量商品分类更新需考虑并发与行锁；建议分批提交并记录操作日志。

请确认以上方案，确认后我将按此方案创建后端控制器与服务，并实现前端页面与接口调用。
