我将执行以下修改，以实现在新标签页打开配货单和发票，并移除这两个页面上的返回按钮。

### 执行计划

1.  **修改订单详情页 (`OrderDetails`) 的跳转逻辑**:
    *   文件: `src/pages/StoreOrder/OrderDetails/index.tsx`
    *   操作: 将 "Picking List" 和 "View Invoice" 按钮的点击行为由 `history.push` 改为 `window.open(..., '_blank')`，使其在新标签页打开。

2.  **移除配货单页 (`PickingList`) 的返回按钮**:
    *   文件: `src/pages/StoreOrder/PickingList/index.tsx`
    *   操作: 删除代码中的 "Back" 按钮组件。

3.  **移除发票页 (`Invoice`) 的返回按钮**:
    *   文件: `src/pages/StoreOrder/Invoice/index.tsx`
    *   操作: 删除代码中的 "Back" 按钮组件。

### 验证方式
*   修改完成后，我会检查代码以确保跳转逻辑正确且返回按钮已被移除。
