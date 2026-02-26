# 订单明细页功能增强计划：批量修改状态与点击切换状态

根据用户需求，将扩展现有的批量修改功能以支持商品状态更新，并实现状态列的点击切换功能。

## 需求分析

1.  **工具栏批量修改功能**：
    *   目前的“批量修改”仅支持数量和价格。需增加“状态 (Status)”选项，允许批量上架/下架商品。
    *   这实际上是修改商品基础数据 (`Product.IsActive`)，而非订单明细数据，但用户希望在订单详情页操作。

2.  **状态列点击改变状态**：
    *   点击表格中的状态标签 (Active/Inactive)，直接切换该商品的状态。

## 修改计划

### 后端 (.NET)

1.  **DTO 修改 (`StoreOrderDtos.cs`)**:
    *   新增 `UpdateProductStatusDto` (用于单个更新)。
    *   新增 `BatchUpdateProductStatusDto` (用于批量更新，包含 `ProductCodes` 和 `IsActive`)。

2.  **Service 修改 (`IStoreOrderReactService` & `StoreOrderReactService`)**:
    *   实现 `UpdateProductStatusAsync`: 更新 `Product` 表的 `IsActive` 字段。
    *   实现 `BatchUpdateProductStatusAsync`: 批量更新 `Product` 表的 `IsActive` 字段。

3.  **Controller 修改 (`ReactStoreOrderController`)**:
    *   新增 `POST product/status` 接口。
    *   新增 `POST product/batch-status` 接口。

### 前端 (React)

1.  **API Service (`storeOrder.ts`)**:
    *   添加 `updateProductStatus` 和 `batchUpdateProductStatus` 方法。

2.  **UI 修改 (`OrderDetails/index.tsx`)**:
    *   **状态列**: 将 `Tag` 包裹在 `Popconfirm` (或直接点击切换，视交互偏好而定，通常建议加确认) 中。点击后调用 `updateProductStatus` 并刷新列表。
    *   **批量修改 Modal**:
        *   在 `Radio.Group` 中添加 `Status` 选项。
        *   当选择 `Status` 时，输入框变为 `Select` (Active / Inactive) 或 `Radio.Group` (Active / Inactive)。
        *   提交时根据类型调用不同的批量接口（数量/价格调原有接口，状态调新接口）。

## 验证计划

*   验证点击状态列能否成功切换状态并刷新。
*   验证批量修改能否成功将多个商品状态更新为 Active 或 Inactive。
