我将修改后端代码，以在订单详情 API 中返回分店地址，并更新前端显示位置。

### 执行计划

1.  **修改后端 DTO (`StoreOrderDtos.cs`)**:
    *   在 `StoreOrderCartDto` 类中添加 `public string? StoreAddress { get; set; }` 属性。

2.  **修改后端服务 (`StoreOrderReactService.cs`)**:
    *   在 `GetOrderDetailAsync` 方法中，查询 `Store` 表以获取对应的 `Address`。
    *   将获取到的地址赋值给 `StoreOrderCartDto` 的 `StoreAddress` 属性。

3.  **修改前端页面 (`OrderDetails/index.tsx`)**:
    *   移除之前添加的独立获取地址的逻辑（`getUsedBranches` 部分），改用 API 返回的 `storeAddress`。
    *   调整 `ProDescriptions` 的布局，将 `Store Address` 显示在 `Remarks` 之后（或者根据用户意图，可能是在 Remarks 下方占一行，或者在信息区域的最后）。用户说“在 Remarks 之后显示”，我将把它放在 Remarks 字段的后面。

### 验证方式
*   检查后端代码是否正确连接了 Store 表并赋值。
*   检查前端代码是否移除了冗余调用并正确展示了新字段。
