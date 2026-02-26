我将为您在购物车抽屉中实现订单提交（Checkout）和备注（Remarks）功能。

### 1. 修改 LayoutCartDrawer 组件
文件路径：`src/components/LayoutCartDrawer/index.tsx`

*   **引入必要组件和 API**：
    *   引入 `Input` 组件用于备注输入。
    *   引入 `submitOrder` API 用于提交订单。
    *   引入 `history` 用于跳转（可选，提交后跳转到历史订单）。

*   **添加状态管理**：
    *   `remarks`: string，用于绑定备注输入框。
    *   `submitting`: boolean，用于提交按钮的加载状态。

*   **添加备注输入框**：
    *   在底部的结算区域（Footer）中，在总金额下方添加一个文本域 `Input.TextArea`。
    *   设置 placeholder 为 "Add remarks (optional)..."。

*   **实现 Checkout 逻辑**：
    *   创建 `handleCheckout` 函数。
    *   调用 `submitOrder({ storeCode: cart.storeCode, remarks })`。
    *   提交成功后：
        1.  显示成功提示 "Order submitted successfully"。
        2.  刷新购物车数据（`refreshCart`）。
        3.  清空备注。
        4.  关闭抽屉。
        5.  跳转到历史订单页面 `/store/orders`，方便用户查看刚提交的订单状态。

### 2. 验证
*   确认备注可以输入。
*   确认点击 Checkout 按钮后能正确调用 API 并处理响应。
*   确认提交后购物车清空且页面跳转。
