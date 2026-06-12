**实施计划**

1. **需求分析**:

   * 用户希望在 `StoreOrderReactService.cs` 的 `GetOrderListAsync` 方法中增强“关键字”搜索功能。

   * 如果提供了 `filter.Keyword`，搜索不仅要匹配 `OrderNo`（订单号）和 `StoreCode`（分店代码），还要匹配订单中包含的商品的 `ItemNumber`（货号）。

   * 这需要将 `WareHouseOrder` 表与 `WareHouseOrderDetails` 表和 `Product` 表进行关联查询。

2. **后端修改**:

   * 修改 `BlazorApp.Api/Services/React/StoreOrderReactService.cs` 中的 `GetOrderListAsync` 方法。

   * 当前逻辑：`q = q.Where(o => o.OrderNo.Contains(keyword) || o.StoreCode.Contains(keyword));`

   * **新逻辑**:

     * 使用子查询（Subquery）来检查是否存在匹配货号的订单明细，这样可以避免直接 Join 导致的主表记录重复问题。

     * 筛选条件更新为：

       * `OrderNo` 包含关键字 或

       * `StoreCode` 包含关键字 或

       * 存在任意一条订单明细，其关联的 `Product` 的 `ItemNumber` 包含关键字。

   * **代码实现 (SqlSugar)**:

     * `q = q.Where(o => o.OrderNo.Contains(keyword) || o.StoreCode.Contains(keyword) || SqlFunc.Subqueryable<WareHouseOrderDetails>().InnerJoin<Product>((d, p) => d.ProductCode == p.ProductCode).Where((d, p) => d.OrderGUID == o.OrderGUID && p.ItemNumber.Contains(keyword)).Any());`

3. **验证**:

   * 确保代码编译通过。

   * 验证搜索逻辑：输入商品货号应能搜索到包含该商品的订单。

