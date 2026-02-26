# 实现分店订货商品列表的递归分类过滤

用户之前请求“分类过滤 需要 包括 所有子分类商品”，我已经在 `WarehouseCategoryReactService.cs` 中修改了 `GetProductsByCategoryAsync` 方法，实现了递归过滤。

然而，分店订货页面（Store Order）可能使用的是不同的控制器和方法。

## 1. 现状分析

*   **前端页面**: `src/pages/StoreOrder/index.tsx` (主入口) -> `ProductGrid.tsx`。
*   **前端 API 调用**: `getStoreOrderProducts` 调用 `POST /api/react/v1/store-order/products`。
*   **后端控制器**: `BlazorApp.Api/Controllers/React/ReactStoreOrderController.cs`。
*   **后端服务**: `IStoreOrderReactService` (可能是 `StoreOrderReactService.cs`)。

**问题**: 之前的修改是在 `WarehouseCategoryReactService` 中，而 `StoreOrder` 页面可能使用的是 `StoreOrderReactService`。因此，之前的修改没有生效。

## 2. 解决方案

我们需要找到 `StoreOrderReactService.cs` 并修改其商品查询逻辑，使其支持递归分类过滤。

### 步骤

1.  **查找服务实现**: 找到 `StoreOrderReactService.cs`。
2.  **检查查询逻辑**: 查看 `GetPagedListAsync` (对应 `GetProducts` 接口) 如何处理 `CategoryGUID` 过滤。
3.  **应用递归逻辑**:
    *   如果它直接调用 `_context.WarehouseProductDb` 查询，我们需要复制 `GetAllSubCategoryIds` 逻辑过来，或者注入 `IWarehouseCategoryReactService` 来获取子分类。
    *   为了代码复用，最好是调用已有的服务，或者把递归逻辑提取到公共帮助类中。但考虑到上下文，在 Service 中实现私有辅助方法是最快的。

### 详细实现计划

1.  读取 `BlazorApp.Api/Services/React/StoreOrderReactService.cs`。
2.  在 `StoreOrderReactService` 中添加 `GetAllSubCategoryIds` 和 `GetSubCategoriesRecursive` 方法（或者如果能注入 `IWarehouseCategoryReactService` 更好，但要注意循环依赖）。
    *   简单起见，我会在 `StoreOrderReactService` 中重新实现这两个辅助方法，因为它们逻辑简单且依赖 `WarehouseCategoryDb`。
3.  修改 `GetPagedListAsync` 方法：
    ```csharp
    if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
    {
        var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
        query = query.Where(w =>
            SqlSugar.SqlFunc.Subqueryable<Product>()
                .Where(p => p.ProductCode == w.ProductCode && categoryIds.Contains(p.WarehouseCategoryGUID))
                .Any()
        );
    }
    ```

## 3. 验证计划

*   **代码修改**：修改 `StoreOrderReactService.cs`。
*   **验证**：前端 Store Order 页面选择一个父分类，确认能看到子分类的商品。

我将先读取 `StoreOrderReactService.cs` 确认当前实现。
