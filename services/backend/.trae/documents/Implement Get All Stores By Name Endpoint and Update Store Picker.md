**实施计划**

1. **后端修改**

   * **接口**: 更新 `BlazorApp.Api/Services/IStoreService.cs`，添加 `GetAllStoresByNameAsync` 方法定义。

   * **服务**: 在 `BlazorApp.Api/Services/StoreService.cs` 中实现 `GetAllStoresByNameAsync` 方法。

     * 查询 `Store` 表。

     * 过滤条件：`IsDeleted == false`。

     * 排序：按 `StoreName` 升序排列。

     * 返回 `StoreDto` 列表。

   * **控制器**: 在 `BlazorApp.Api/Controllers/StoresController.cs` 中添加新的 GET 端点 `all-by-name`，调用上述服务方法。

2. **前端修改**

   * **服务**: 更新 `ReactUmi/my-app/src/services/storeService.ts`，添加 `getAllStoresByName` 函数，调用 `/api/Stores/all-by-name`。

   * **组件**: 更新 `ReactUmi/my-app/src/pages/StoreOrder/components/StorePickerModal.tsx`：

     * 将 `getStores` 替换为 `getAllStoresByName`。

     * 实现前端过滤逻辑：根据“关键字”（分店名称/代码）和“状态”（激活/未激活）过滤 API 返回的完整列表。

     * 将过滤后的数据传给 `ProTable` 显示。

3. **验证**

   * 验证后端能否成功编译。

   * 验证前端 `StorePickerModal` 是否能正确加载所有分店，按名称排序，并支持本地过滤（搜索/状态）。

