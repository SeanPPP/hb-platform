## 目标
- 基于 `BlazorApp.Shared.Models.StoreRetailPrice`，在后端新增 React 专用控制器与服务层，提供完整 CRUD 与批量操作（使用事务）。
- 支持服务端过滤与排序：按分店、供应商、商品信息进行精准筛选与排序；支持 React-Data-Grid 的 `GridRequestDto`。
- 在 React 前端新增服务文件与类型定义（ReactDTO），统一通过 `/api/react/v1/store-retail-prices` 路由访问。

## 现有约束与风格
- React 控制器目录与路由风格：`BlazorApp.Api/Controllers/React/React*.cs`，路由形如 `/api/react/v1/...`（参考 `ReactProductSetCodesController.cs:12`）。
- React 服务目录：`BlazorApp.Api/Services/React/*.cs` 与接口 `BlazorApp.Api/Interfaces/React/*.cs`，DI 在 `Program.cs:259-268` 注册。
- React 前端服务风格：`ReactUmi/my-app/src/services/*.ts`，使用 `@/utils/request` 封装，响应结构 `{ success, data, message }`（参考 `ReactSuppliersController.cs:41-53`）。
- 服务端分页/过滤/排序 DTO：`BlazorApp.Shared/DTOs/GridRequestDto.cs` 与 `GridResponseDto<T>` 可直接复用。

## 后端改动
- 新增接口：`d:\Development\cline\blazor\BlazorApp.Api\Interfaces\React\IStoreRetailPriceReactService.cs`
  - 方法：
    - `Task<GridResponseDto<StoreRetailPriceListDto>> GetGridDataAsync(GridRequestDto request)`
    - `Task<ApiResponse<StoreRetailPriceDetailDto>> GetByUuidAsync(string uuid)`
    - `Task<ApiResponse<StoreRetailPriceDetailDto>> CreateAsync(CreateStoreRetailPriceDto dto, string createdBy)`
    - `Task<ApiResponse<StoreRetailPriceDetailDto>> UpdateAsync(string uuid, UpdateStoreRetailPriceDto dto, string updatedBy)`
    - `Task<ApiResponse<bool>> DeleteAsync(string uuid, string updatedBy)`（软删除，`BaseEntity.IsDeleted`）
    - `Task<ApiResponse<BatchResultDto>> BatchUpsertAsync(List<StoreRetailPriceUpsertItemDto> items, string updatedBy)`（事务）
    - `Task<ApiResponse<bool>> BatchDeleteAsync(List<string> uuids, string updatedBy)`（事务，软删除）

- 新增服务实现：`d:\Development\cline\blazor\BlazorApp.Api\Services\React\StoreRetailPriceReactService.cs`
  - 依赖：`SqlSugarContext`, `ILogger`, 可选 `IMapper`（如仅 Select 投影则可不必）。
  - Grid 查询：
    - 基础查询：`Queryable<StoreRetailPrice>`，`LeftJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)`、`LeftJoin<ChinaSupplier>((p, sup) => p.SupplierCode == sup.SupplierCode)`、`LeftJoin<Store>((p, st) => p.StoreCode == st.StoreCode)`，并 `Where(p => !p.IsDeleted)`。
    - 全局搜索：对 `StoreCode/StoreName/SupplierCode/SupplierName/ProductCode/ProductName` 做 OR 模糊匹配。
    - 列过滤：复用 `GridRequestDto.FilterModel`，实现 `ApplyAgGridFilters`（参考 `DomesticProductReactService.cs:157-187` 等），支持 text/number/set。
      - text 列：`storeCode, storeName, supplierCode, supplierName, productCode, productName`
      - number 列：`purchasePrice, storeRetailPriceValue, discountRate`
      - set/boolean：`isActive, isAutoPricing`
    - 排序：实现 `ApplyAgGridSorts`，支持上述字段；默认按 `UpdatedAt desc`。
    - 选择与分页：投影为 `StoreRetailPriceListDto`，`Skip(StartRow) + Take(PageSize)`。
  - 单项 CRUD：
    - Create：校验 `StoreCode/ProductCode/SupplierCode` 存在；写入 `CreatedAt/CreatedBy`，默认 `IsActive=true`。
    - Update：根据 `uuid` 更新字段并写 `UpdatedAt/UpdatedBy`。
    - Delete：软删除并写更新时间与人。
  - 批量操作（事务）：
    - `BatchUpsertAsync`：`db.Ado.BeginTranAsync()` 包裹；每项若含 `UUID` 则更新，否则以 `StoreCode+ProductCode+SupplierCode` 查找存在则更新，否则插入；统计 `updated/inserted/failed`。
    - `BatchDeleteAsync`：`db.Ado.BeginTranAsync()` 包裹，批量按 `UUID` 软删除。

- 新增控制器：`d:\Development\cline\blazor\BlazorApp.Api\Controllers\React\ReactStoreRetailPricesController.cs`
  - `[Route("api/react/v1/store-retail-prices")]`，`[Authorize]`，角色建议 `Admin,WarehouseManager`。
  - 端点：
    - `POST /grid` → `GridRequestDto`，返回 `{ success, data: { items, total }, message }`（参考 `ReactProductSetCodesController.cs:25-37`）。
    - `GET /{uuid}` → 详情。
    - `POST /` → 创建。
    - `PUT /{uuid}` → 更新。
    - `DELETE /{uuid}` → 删除（软删）。
    - `POST /batch-upsert` → 批量新增/更新（事务）。
    - `DELETE /batch-delete` → 批量删除（事务）。

- DI 注册：在 `d:\Development\cline\blazor\BlazorApp.Api\Program.cs` 增加
  - `builder.Services.AddScoped<IStoreRetailPriceReactService, StoreRetailPriceReactService>();`（同 `Program.cs:259-268` 风格）。

- 新增后端 DTO：`d:\Development\cline\blazor\BlazorApp.Shared\DTOs\StoreRetailPriceDtos.cs`
  - `StoreRetailPriceListDto`：用于 Grid 列表，含 `UUID, StoreCode, StoreName, SupplierCode, SupplierName, ProductCode, ProductName, PurchasePrice, StoreRetailPriceValue, DiscountRate, IsActive, IsAutoPricing, UpdatedAt`。
  - `StoreRetailPriceDetailDto`：在详情/创建更新返回，除上述增加 `CreatedAt, CreatedBy, UpdatedBy`。
  - `CreateStoreRetailPriceDto`：必填 `StoreCode, ProductCode, SupplierCode`，可选价格/状态。
  - `UpdateStoreRetailPriceDto`：同上但可选字段；
  - `StoreRetailPriceUpsertItemDto`：用于批量 upsert。
  - `BatchResultDto`：`Inserted, Updated, Failed, Errors`。
  - 说明：命名与风格对齐 `ReactTableDtos.cs` 与仓库现有 DTO。

## 前端改动
- 服务文件：`d:\Development\cline\blazor\ReactUmi\my-app\src\services\storeRetailPriceService.ts`
  - 方法：
    - `getGrid(data: GridRequestDto)` → `POST /api/react/v1/store-retail-prices/grid`
    - `getByUuid(uuid: string)` → `GET /api/react/v1/store-retail-prices/${uuid}`
    - `create(data: CreateStoreRetailPriceDto)` → `POST /api/react/v1/store-retail-prices`
    - `update(uuid: string, data: UpdateStoreRetailPriceDto)` → `PUT /api/react/v1/store-retail-prices/${uuid}`
    - `deleteByUuid(uuid: string)` → `DELETE /api/react/v1/store-retail-prices/${uuid}`
    - `batchUpsert(items: StoreRetailPriceUpsertItemDto[])` → `POST /api/react/v1/store-retail-prices/batch-upsert`
    - `batchDelete(uuids: string[])` → `DELETE /api/react/v1/store-retail-prices/batch-delete`
  - 响应类型：`ApiResponse<GridResponse<StoreRetailPriceListDto>>` / `ApiResponse<StoreRetailPriceDetailDto>` / `ApiResponse<boolean>` / `ApiResponse<BatchResultDto>`。

- 类型文件（ReactDTO）：`d:\Development\cline\blazor\ReactUmi\my-app\src\types\storeRetailPrice.ts`
  - 与后端 DTO 对齐的 TS 类型定义，供 `services` 与页面使用。

## 过滤与排序细节
- 过滤：
  - Text：`storeCode, storeName, supplierCode, supplierName, productCode, productName` 支持 `contains/equals/startswith/endswith/blank/notblank`。
  - Number：`purchasePrice, storeRetailPriceValue, discountRate` 支持 `equals/lessthan/greaterthan/inrange`（参考 `DomesticProductReactService.cs:304-336`）。
  - Boolean：`isActive, isAutoPricing` 支持 `equals`。
- 排序：支持上述列；默认 `updatedAt desc`（参考 `DomesticProductReactService.cs:389-417`）。

## 安全与一致性
- 全部端点使用 `[Authorize]`；写操作限定角色 `Admin,WarehouseManager`。
- 事务保证：批量接口统一 `BeginTran/Commit/Rollback`，错误日志记录并返回错误明细。
- 响应统一：`{ success, data, message }`；Grid 返回 `items/total`。

## 代码引用（用于对齐实现）
- Grid DTO：`BlazorApp.Shared/DTOs/GridRequestDto.cs:8-41, 93-146`
- React Grid 控制器响应：`BlazorApp.Api/Controllers/React/ReactProductSetCodesController.cs:25-37`
- 过滤与排序范式：`BlazorApp.Api/Services/React/DomesticProductReactService.cs:157-417`
- React 控制器返回结构：`BlazorApp.Api/Controllers/React/ReactSuppliersController.cs:41-53`
- DI 注册区：`BlazorApp.Api/Program.cs:259-268`
- 实体模型：`BlazorApp.Shared/Models/HBweb/StoreRetailPrice.cs:10-85` 与软删基类 `BaseEntity.cs:34-39`

## 交付与验证
- 后端：编译通过，Swagger 中出现新组 `store-retail-prices`；手动调用 `POST /grid` 验证过滤与排序；`batch-upsert` 与 `batch-delete` 事务成功并返回统计。
- 前端：`storeRetailPriceService.ts` 接口与 `types` 对齐，调用能正确携带 Token（`src/utils/request.ts`）。

## 下一步
- 按上述文件路径与接口定义实施编码；完成后我将运行本地验证（API 与前端调用），并提供示例调用参数与返回结果。