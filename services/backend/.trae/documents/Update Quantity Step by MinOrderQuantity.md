**用户需求**:
数量加减按钮步长按照最小订货量，默认为1。

**分析**:
1.  **位置**:
    *   **ProductCard.tsx**: 这里的 `InputNumber` 用于添加到购物车时的数量选择。
    *   **CartDrawer.tsx**: 这里的 `InputNumber` 用于修改购物车中的数量。

2.  **当前实现**:
    *   `ProductCard.tsx`: `min={product.minOrderQuantity || 1}`，没有设置 `step`。
    *   `CartDrawer.tsx`: `min={1}`，没有设置 `step`，也没有获取 `minOrderQuantity` 信息。

3.  **问题**:
    *   `StoreOrderCartItemDto` 目前不包含 `MinOrderQuantity` 属性。我们需要在后端 DTO 中添加该属性，并在查询购物车时填充它。
    *   前端需要使用该属性设置 `InputNumber` 的 `step` 和 `min`。

**解决方案**:

1.  **后端修改**:
    *   `BlazorApp.Shared/DTOs/StoreOrderDtos.cs`: 在 `StoreOrderCartItemDto` 中添加 `MinOrderQuantity` 属性。
    *   `BlazorApp.Api/Services/React/StoreOrderReactService.cs`: 在 `GetActiveCartAsync` 方法中，从关联的 `WarehouseProduct` 获取 `MinOrderQuantity` 并赋值给 DTO。

2.  **前端修改**:
    *   `src/services/storeOrder.ts`: 更新 `StoreOrderCartItemDto` 接口定义。
    *   `ProductCard.tsx`: 设置 `InputNumber` 的 `step={product.minOrderQuantity || 1}`。
    *   `CartDrawer.tsx`: 设置 `InputNumber` 的 `step={item.minOrderQuantity || 1}` 和 `min={item.minOrderQuantity || 1}`。

**详细步骤**:

1.  修改后端 DTO `StoreOrderCartItemDto`。
2.  修改后端 Service `StoreOrderReactService.GetActiveCartAsync` 填充 `MinOrderQuantity`。
3.  修改前端 `storeOrder.ts` 类型定义。
4.  修改前端 `ProductCard.tsx` 的 InputNumber。
5.  修改前端 `CartDrawer.tsx` 的 InputNumber。

**Wait**: 用户说“默认为1”。这意味着如果 `MinOrderQuantity` 未设置或为0，则使用 1。这已在计划中。

**Refined Plan**:

1.  **Backend**: Add `MinOrderQuantity` to `StoreOrderCartItemDto` and populate it.
2.  **Frontend**: Update TS interface.
3.  **Frontend**: Update `ProductCard` to use `step`.
4.  **Frontend**: Update `CartDrawer` to use `step` and `min` from new property.

Let's start.

