我将修复地址显示问题。通过查看后端模型 `Store.cs`，确认字段名为 `Address`（大写开头）。然而，前端 API 响应通常会自动转为小写驼峰命名 (`address`)，或者保持原样。我将在前端代码中同时兼容 `Address` 和 `address` 两种写法，以确保正确获取。

### 执行计划

1.  **修复发票页面 (`Invoice/index.tsx`)**:
    *   在 `getUsedBranches` 的回调逻辑中，获取地址时尝试同时读取 `branch.address` 和 `branch.Address`。
    *   同样处理电话字段，尝试 `contactPhone` (前端DTO可能定义的名称) 和 `Phone` (后端实体名称) 以及 `branch.phone`。

2.  **更新订单详情页 (`OrderDetails/index.tsx`)**:
    *   修改 `fetchOrder` 函数，引入 `getUsedBranches` 获取店铺详情。
    *   在状态中添加 `storeAddress` 字段。
    *   在 `ProDescriptions` 中添加 "Store Address" 显示项。

### 验证方式
*   代码修改后，检查字段访问逻辑是否覆盖了大小写两种情况。
