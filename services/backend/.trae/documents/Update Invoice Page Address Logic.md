我将更新发票页面的逻辑，以优先使用后端 API 返回的分店地址（`storeAddress`），这与我刚刚对后端所做的更改保持一致。

### 执行计划

1.  **修改 `Invoice/index.tsx`**:
    *   在 `fetchData` 函数中，检查 `orderData` 是否已包含 `storeAddress` 字段。
    *   如果 `storeAddress` 存在，直接使用它来设置 `currentAddress` 和 `store` 对象中的地址信息，从而避免不必要的额外 API 调用。
    *   保留现有的后备逻辑（调用 `getUsedBranches` 或 `getStores`），但将其作为第二选择。

### 验证方式
*   修改完成后，我会检查代码逻辑，确保发票页面能正确显示从订单详情 API 直接获取的地址。
