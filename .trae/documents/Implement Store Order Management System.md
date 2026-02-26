我将更新计划，将前端开发部分调整为 React (Umi) 项目。

### 1. 后端开发 (.NET - BlazorApp.Api)
**目标**: 扩展 API 以支持订单列表和详情查询。
*   **修改 `IStoreOrderReactService` 接口**:
    *   添加 `GetOrderListAsync(StoreOrderListFilterDto filter)`。
    *   添加 `GetOrderDetailAsync(string orderGuid)`。
*   **修改 `StoreOrderReactService` 实现**:
    *   **查询列表**: 关联 `WareHouseOrder` 表，支持按分店(StoreCode)、日期范围、状态(FlowStatus)筛选，并分页。
    *   **查询详情**: 复用或适配现有的购物车逻辑，返回指定 OrderGUID 的完整信息（包含商品明细、价格、体积等）。
*   **修改 `ReactStoreOrderController`**:
    *   新增接口 `GET /api/react/v1/store-order/list`。
    *   新增接口 `GET /api/react/v1/store-order/detail/{orderGuid}`。

### 2. 共享层 (.NET - BlazorApp.Shared)
**目标**: 定义前后端交互的数据模型。
*   **修改 `StoreOrderDtos.cs`**:
    *   新增 `StoreOrderListFilterDto`: `StoreCode`, `StartDate`, `EndDate`, `Status`, `PageNumber`, `PageSize`。
    *   新增 `StoreOrderListItemDto`: `OrderGUID`, `OrderNo`, `StoreCode`, `OrderDate`, `FlowStatus`, `TotalAmount`, `TotalQuantity`。

### 3. 前端开发 (React - ReactUmi/my-app)
**目标**: 在 React 项目中实现订单管理界面。
*   **修改 `src/services/storeOrder.ts`**:
    *   添加 `getOrderList(params)` 方法调用后端列表接口。
    *   添加 `getOrderDetail(orderGuid)` 方法调用后端详情接口。
*   **创建订单列表页 `src/pages/StoreOrder/OrderList/index.tsx`**:
    *   **布局**: 使用左右分栏布局。
    *   **左侧侧边栏**: 显示分店列表（支持搜索）。
        *   *权限逻辑*: 管理员可选择任意分店；普通用户只能看到自己的分店。
    *   **右侧表格**: 使用 `ProTable` 展示订单列表，支持按状态和日期筛选。
    *   **交互**: 点击订单跳转到详情页。
*   **创建订单详情页 `src/pages/StoreOrder/OrderDetails/index.tsx`**:
    *   **展示**: 使用 `ProDescriptions` 展示订单基本信息，`ProTable` 展示商品明细。
    *   **权限与操作**:
        *   状态为 **购物车(0)** 且有权限时：显示“编辑”按钮（跳转或开启编辑模式）。
        *   其他状态：仅展示只读信息。
*   **路由配置**:
    *   在 `.umirc.ts` 中添加新路由：
        *   `/store-order/list`
        *   `/store-order/detail/:id`
