# 添加订单明细运费显示与编辑功能

## 任务目标
在订单详情页面的 "Basic Info" 区域添加 "运费" (Shipping Fee) 的显示与编辑功能。

## 执行计划

### 1. 后端修改 (.NET)
*   **修改 DTO (`BlazorApp.Shared/DTOs/StoreOrderDtos.cs`)**:
    *   在 `StoreOrderCartDto` 中添加 `public decimal? ShippingFee { get; set; }`。
    *   在 `UpdateOrderHeaderDto` 中添加 `public decimal? ShippingFee { get; set; }`。
*   **修改服务 (`BlazorApp.Api/Services/React/StoreOrderReactService.cs`)**:
    *   在 `GetOrderDetailAsync` 方法中，将数据库实体 `WareHouseOrder.ShippingFee` 映射到 DTO。
    *   在 `UpdateOrderHeaderAsync` 方法中，接收并更新 `WareHouseOrder.ShippingFee` 字段到数据库。

### 2. 前端修改 (React)
*   **修改接口定义 (`src/services/storeOrder.ts`)**:
    *   更新 `StoreOrderCartDto` 接口，增加 `shippingFee?: number`。
    *   更新 `updateOrderHeader` 函数参数类型，增加 `shippingFee`。
*   **修改页面逻辑 (`src/pages/StoreOrder/OrderDetails/index.tsx`)**:
    *   在组件状态中添加 `shippingFee` 状态。
    *   在 `Basic Info` 的 `ProDescriptions` 中添加 "Shipping Fee" 显示项。
    *   实现编辑逻辑：
        *   当处于可编辑状态时，"Shipping Fee" 显示为数字输入框 (`InputNumber`)。
        *   修改保存按钮 (`Save`) 的逻辑，使其同时提交 `remarks` 和 `shippingFee`。

## 验证
*   确认后端编译通过。
*   确认前端页面能正确显示运费。
*   确认前端修改运费并保存后，数据能持久化到数据库。
