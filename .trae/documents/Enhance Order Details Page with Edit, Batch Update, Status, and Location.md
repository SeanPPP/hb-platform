# 订单明细页功能增强计划 (修订版)

根据用户需求及对 LocationCode 来源的明确，制定以下计划。

## 需求分析与修改点

### 1. Import Price 可编辑
*   **后端**:
    *   `StoreOrderDtos.cs`: `UpdateOrderLineDto` 增加 `ImportPrice` (nullable decimal)。
    *   `StoreOrderReactService.cs`: `UpdateOrderLineAsync` 支持更新 `ImportPrice`，并重新计算 `ImportAmount`。
*   **前端**:
    *   `storeOrder.ts`: 更新接口。
    *   `OrderDetails/index.tsx`: 将 `Import Price` 列改为 `InputNumber`，支持编辑。

### 2. 添加选择列
*   **前端**: `OrderDetails/index.tsx` 中 `ProTable` 启用 `rowSelection`。

### 3. 工具栏添加批量修改功能
*   **后端**:
    *   `StoreOrderDtos.cs`: 新增 `BatchUpdateOrderLineDto` (包含 `OrderGUID`, `Items`: `{ ProductCode, Quantity?, ImportPrice? }`)。
    *   `StoreOrderReactService.cs`: 实现 `BatchUpdateOrderLineAsync`。
    *   `ReactStoreOrderController.cs`: 添加 `batch-update` 接口。
*   **前端**:
    *   `OrderDetails/index.tsx`: 添加批量修改按钮及 Modal，支持批量设置配货数量或进货价格。

### 4. 添加状态列与上架/下架功能
*   **后端**:
    *   `StoreOrderDtos.cs`: `StoreOrderCartItemDto` 增加 `IsActive`。
    *   `StoreOrderReactService.cs`: `GetOrderDetailAsync` 填充 `IsActive`。
    *   `ReactProductController.cs` (或 StoreOrderController): 确认有切换商品状态的接口（通常是更新 Product 表）。
*   **前端**:
    *   `OrderDetails/index.tsx`: 添加 `Status` 列 (Tag) 和操作按钮 (上架/下架)，调用对应 API。

### 5. 添加 LocationCode 列 (配货位)
*   **数据来源**:
    *   关联 `ProductLocation` 表 (通过 `ProductCode`) 和 `Location` 表 (通过 `LocationGuid`)。
    *   **过滤条件**: `Location.LocationType == 1` (配货位)。
*   **后端**:
    *   `StoreOrderDtos.cs`: `StoreOrderCartItemDto` 增加 `LocationCode`。
    *   `StoreOrderReactService.cs`: 在 `GetOrderDetailAsync` 中，通过 `LeftJoin` 查询 `ProductLocation` 和 `Location`，并筛选 `LocationType == 1`。如果有多个，取第一个或拼接。
*   **前端**:
    *   `OrderDetails/index.tsx`: 添加 `Location` 列。

## 执行步骤

1.  **后端开发**:
    *   修改 DTOs。
    *   修改 `StoreOrderReactService`，实现 ImportPrice 更新、批量更新、LocationCode 查询 (Type=1)、IsActive 查询。
    *   添加/确认 Controller 接口。

2.  **前端开发**:
    *   更新 API 定义。
    *   修改页面组件，实现列显示、编辑、批量操作 UI。

## 验证
*   ImportPrice 编辑保存。
*   批量修改功能生效。
*   商品状态切换及显示。
*   LocationCode 正确显示配货位 (Type=1)。

