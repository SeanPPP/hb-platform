## 目标
- 新增“分店多码商品价格批量管理”页面，交互样式与现有“分店商品价格管理”一致。
- 支持批量编辑：一品多码零售价、进货价、折扣率%、自动定价、使用状态、特殊商品标记。
- 前后端接口与服务层齐全，包含网格查询、批量保存、特殊标记批量更新、按 UUID 增量刷新。

## 前端实现
### 新增类型声明（src/types/storeMultiCodePrice.ts）
- GridRequestDto/FilterModelDto/SortModelDto/GridResponse<T>（复用现有结构）。
- StoreMultiCodePriceListDto：uuid、storeCode/storeName、productCode/productName、itemNumber、multiBarcode、productImage、purchasePrice、multiCodeRetailPrice、discountRate%、isActive、isAutoPricing、isSpecialProduct、updatedBy/updatedAt。
- StoreMultiCodePriceUpsertItemDto：UUID、StoreCode、ProductCode、PurchasePrice、MultiCodeRetailPrice、DiscountRate、IsActive、IsAutoPricing。
- BatchResultDto、BatchUpdateSpecialRequestDto（与现有一致）。

### 新增服务层（src/services/storeMultiCodePriceService.ts）
- POST `/api/react/v1/store-multi-code-prices/grid` → getGrid
- POST `/api/react/v1/store-multi-code-prices/batch-upsert` → batchUpsert
- PUT `/api/react/v1/store-multi-code-prices/batch-special` → batchUpdateSpecial
- POST `/api/react/v1/store-multi-code-prices/batch-by-uuids` → batchByUuids
- GET `/api/Stores/active` → getActiveStores（复用）

### 新增页面（src/pages/PosAdmin/StoreMultiCodePrices/index.tsx）
- 样式、布局、工具栏、分页与“分店商品价格管理”一致。
- 差异：
  - 列“分店零售价”改为“一品多码零售价”；展示和编辑对应 `multiCodeRetailPrice`。
  - 展示多码条形码 `multiBarcode`；移除供应商列与筛选。
  - 过滤项包含分店、货号、关键词。
- 编辑逻辑
  - 折扣率在页面显示为百分比整数（10-90），保存时换算为 0.1-0.9。
  - `editedMap` 记录待保存字段，批量 upsert 时字段名映射为后端 DTO。
  - 特殊商品标记批量更新沿用现有实现（按产品编码聚合）。
- 批量修改功能与保存反馈信息一致，信息中显示新增/更新/失败计数。

## 后端实现
### 新增 DTO（BlazorApp.Shared/DTOs/StoreMultiCodePriceDtos.cs）
- List/Detail/Upsert/BatchResult/BatchUpdateSpecialRequest DTO，字段与前端类型相对应（命名 PascalCase）。

### 新增接口与服务
- 接口：`BlazorApp.Api/Interfaces/React/IStoreMultiCodePricesReactService.cs`
- 实现：`BlazorApp.Api/Services/React/StoreMultiCodePricesReactService.cs`
  - `GetGridAsync(GridRequestDto)`：
    - SqlSugar 查询 `StoreMultiCodeProduct`，连接 `Product`、`Store` 以取名称、图片。
    - 支持分页、排序、关键词与筛选（storeCode/itemNumber）。
    - 返回 `GridResponse<StoreMultiCodePriceListDto>`。
  - `BatchUpsertAsync(List<StoreMultiCodePriceUpsertItemDto>)`：
    - 根据 `UUID` 判断插入/更新；批量执行（Insertable/Updateable），统计结果。
    - 折扣率字段采用 0-1 范围。
  - `BatchUpdateSpecialAsync(BatchUpdateSpecialRequestDto)`：
    - 根据 `ProductCodes` 批量更新 `Product.IsSpecialProduct`。
  - `BatchByUuidsAsync(List<string>)`：
    - 返回指定 UUID 的最新列表数据，用于前端增量刷新。

### 新增控制器（BlazorApp.Api/Controllers/React/StoreMultiCodePricesController.cs）
- 路由：`/api/react/v1/store-multi-code-prices`
- 端点：`grid`、`batch-upsert`、`batch-special`、`batch-by-uuids`
- 返回统一 `ApiResponse<T>` 结构，保持与现有 React 控制器一致。

## 复用与风格
- 页面交互、提示、批量编辑与现有“分店商品价格管理”保持一致。
- 服务层、控制器风格与现有 React 控制器一致；SqlSugar 批量写入遵循之前的优化方式（分批 + Fastest<T> 可选）。

## 验证
- 构造 1000 条样例数据验证查询与分页性能。
- 批量保存 1k/10k 测试，核对返回计数与数据库实际。
- 前端交互验证：编辑单行、批量编辑、特殊商品标记更新、增量刷新。

## 交付物
- 前端：新类型、服务、页面三处新增文件，菜单入口可根据您的路由配置追加。
- 后端：DTO、接口、服务、控制器四处新增文件，并编译通过。

请确认按此方案实施，落地后会直接创建并接入上述文件与接口，实现端到端功能。