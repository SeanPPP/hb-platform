# 修复 StoreOrderReactService 编译错误

## 问题分析
1.  **查询结构错误**：`Select` 投影执行得太早。后续的 `Where` 和 `OrderBy` 子句试图在已投影的 `StoreOrderProductDto` 上使用 3 参数 lambda `(p, wp, wc)`，导致 `CS1593` 错误。
2.  **属性缺失错误**：`WareHouseOrder` 和 `WareHouseOrderDetails` 实体没有 `IsActive` 属性，导致 `CS0117` 错误。

## 解决方案

### 1. 重构 `GetPagedListAsync`
*   调整查询构建顺序：
    1.  定义带有 Joins 的基础查询。
    2.  在连接后的查询上应用 `Where` 过滤（分类、货号）。
    3.  在连接后的查询上应用 `OrderBy` 排序。
    4.  执行 `CountAsync` 获取总记录数。
    5.  应用 `Skip` 和 `Take` 进行分页。
    6.  最后 `Select` 投影为 `StoreOrderProductDto` 并执行 `ToListAsync`。

### 2. 修复实体属性使用
*   在 `AddToCartAsync` 中，移除创建 `WareHouseOrder` 和 `WareHouseOrderDetails` 对象时对 `IsActive` 属性的赋值。

## 实施细节
*   **文件**: `BlazorApp.Api/Services/React/StoreOrderReactService.cs`
*   **操作**: 使用修正后的逻辑重写该类。
