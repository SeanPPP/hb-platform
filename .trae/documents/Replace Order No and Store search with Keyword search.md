**实施计划**

1. **后端修改**

   * **更新 DTO**: 修改 `BlazorApp.Shared/DTOs/StoreOrderDtos.cs` 中的 `StoreOrderListFilterDto` 类。

     * 添加 `public string? Keyword { get; set; }` 属性。

   * **更新服务**: 修改 `BlazorApp.Api/Services/React/StoreOrderReactService.cs` 中的 `GetOrderListAsync` 方法。

     * 实现 `Keyword` 的搜索逻辑：如果提供了 `Keyword`，则筛选 `OrderNo` 包含该关键字 或 `StoreCode` 包含该关键字的订单。

2. **前端修改**

   * **更新组件**: 修改 `ReactUmi/my-app/src/pages/StoreOrder/OrderList/index.tsx`。

     * 将 `Order No` 列的 `hideInSearch` 设置为 `true`，隐藏原有的订单号搜索框。

     * 将 `Store` 列的 `hideInSearch` 设置为 `true`，隐藏原有的分店搜索框。

     * 添加一个新的 "Keyword" 列定义，仅用于搜索 (`hideInTable: true`)。

       * `dataIndex: 'keyword'`

       * `title: 'Keyword'` (显示为“关键字”)

       * `tooltip: 'Search by Order No or Store Code'`

     * 更新 `ProTable` 的 `request` 函数，将前端的 `keyword` 参数传递给后端 API。

3. **验证**

   * 验证界面上是否出现了新的“关键字”搜索框。

   * 验证原有的“Order No”和“Store”搜索框是否已消失。

   * 验证输入关键字后，是否能正确筛选出匹配订单号或分店代码的订单。

