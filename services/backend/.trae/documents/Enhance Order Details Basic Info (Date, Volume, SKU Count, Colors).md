我将对“订单详情”页面的“基本信息”部分进行多项增强，包括添加字段、支持编辑、优化统计显示以及视觉区分。

### 执行计划

1.  **后端修改**:
    *   **DTO 更新 (`StoreOrderDtos.cs`)**:
        *   `StoreOrderCartDto`: 确认包含 `OrderDate`，新增 `TotalAllocQuantity` (发货总数) 和 `TotalSKU` (SKU总数)。
        *   `UpdateOrderHeaderDto`: 新增 `OrderDate` 字段，用于保存修改后的日期。
    *   **服务更新 (`StoreOrderReactService.cs`)**:
        *   `GetOrderDetailAsync`: 计算 `TotalAllocQuantity` 和 `TotalSKU` 并赋值。
        *   `UpdateOrderHeaderAsync`: 实现更新 `OrderDate` 的逻辑。

2.  **前端修改 (`OrderDetails/index.tsx`)**:
    *   **状态管理**: 引入 `orderDate` 状态，并使用 Ant Design 的 `DatePicker` 组件进行绑定。
    *   **布局重构**: 将 `ProDescriptions` 拆分为几个带有不同背景色或边框颜色的独立区块，或者在同一个 `ProDescriptions` 中使用不同的 `labelStyle/contentStyle` 来区分。
    *   **字段展示**:
        *   **订单日期**: 可编辑模式下显示为日期选择器。
        *   **订单体积**: 显示 `TotalVolume`。
        *   **数量统计**: 分开显示 "Total Order Qty" (订货总数) 和 "Total Send Qty" (实发总数)。
        *   **SKU 统计**: 显示 "Total SKU" (商品品类/条目数)。
    *   **保存逻辑**: 更新 `handleUpdateHeader` 以包含日期数据。

### 验证方式
*   检查后端代码是否正确计算了新增的统计字段。
*   检查前端页面是否能正确显示并修改订单日期，且不同类别的基本信息有明显的颜色区分。
