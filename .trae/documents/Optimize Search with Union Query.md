**实施计划：使用 Union 优化搜索查询**

1. **分析**:

   * 当前使用 `OR` 连接主表查询和子查询（`Contains` 或 `Any`）的方式，会导致数据库无法有效利用索引，通常会退化为全表扫描，导致超时。

   * 尤其是当 `OrderNo`（字符串索引）和子查询（复杂连接）混用时，数据库优化器很难生成高效的执行计划。

2. **优化策略**:

   * **核心思想**：拆分查询，分别利用索引，然后合并结果。

   * **方案**：使用 SQL 的 `UNION` 操作。

     * **查询 A**：仅在主表 `WareHouseOrder` 中搜索 `OrderNo` 或 `StoreCode` 包含关键字的记录。

     * **查询 B**：通过连接 `WareHouseOrder` -> `WareHouseOrderDetails` -> `Product`，搜索 `ItemNumber` 包含关键字的记录。

     * **合并**：`查询 A UNION 查询 B`。`UNION` 会自动去重，并且允许数据库引擎分别对两部分查询进行优化。

3. **实施步骤**:

   * 修改 `BlazorApp.Api/Services/React/StoreOrderReactService.cs` 中的 `GetOrderListAsync` 方法。

   * **重构查询构建逻辑**：

     * 如果存在 `Keyword`：

       * 构建 `q1`: `_db.Queryable<WareHouseOrder>().Where(...)` (匹配 OrderNo/StoreCode)。

       * 构建 `q2`: `_db.Queryable<WareHouseOrder>().InnerJoin(...).InnerJoin(...).Where(...)` (匹配 ItemNumber)。

       * 使用 `_db.Union(q1, q2)` 生成新的查询对象 `q`。

     * 如果不存在 `Keyword`：

       * `q = _db.Queryable<WareHouseOrder>()`。

     * **重要**：所有查询都必须包含基础的 `!IsDeleted` 过滤。

   * **应用后续过滤器**：

     * 在合并后的 `q` 上继续应用 `StoreCode`, `Status`, `Date` 等过滤器。SqlSugar 会将 `UNION` 结果作为子查询，在外层应用这些过滤条件。

   * **分页与排序**：

     * 保持原有的分页和排序逻辑不变，作用于最终的 `q`。

4. **验证**:

   * 验证代码编译通过。

   * 验证生成的 SQL 是否使用了 `UNION`。

   * 验证搜索性能是否显著提升（不再超时）。

   * 验证搜索结果准确性（应包含匹配订单号、分店代码或货号的所有订单）。

