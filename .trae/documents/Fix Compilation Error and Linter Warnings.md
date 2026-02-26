我将修复最近修改文件中出现的编译错误和警告。

### 1. 修复 `BlazorApp.Api/Services/React/StoreOrderReactService.cs`
*   **修复编译错误**: 在 `GetOrderListAsync` 方法中，将 `Sum(Quantity)` 的 `decimal` 结果显式转换为 `int`。
    *   修改为: `TotalQuantity = (int)(SqlFunc.Subqueryable<WareHouseOrderDetails>()...Sum(d => d.Quantity) ?? 0)`
*   **修复警告**: 在调用 `Contains` 之前增加 `WarehouseCategoryGUID` 的非空检查。
    *   修改为: `q = q.Where((p, wp, wc) => p.WarehouseCategoryGUID != null && categoryIds.Contains(p.WarehouseCategoryGUID))`

### 2. 修复 `BlazorApp.Api/Controllers/React/ReactStoreOrderController.cs`
*   **修复格式警告**: 根据 Linter 建议调整代码格式（缩进、换行）。

### 3. 验证
*   确认修复后不再出现相关的编译错误和警告。
