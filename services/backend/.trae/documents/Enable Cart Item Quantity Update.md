**用户需求**:
购物车中可以修改商品数量，并更新。

**分析**:
1.  **前端 (`CartDrawer.tsx`)**:
    *   目前使用 `InputNumber` 但没有绑定 `onChange` 事件，或者没有实现更新逻辑。
    *   需要实现 `handleUpdateQuantity` 方法。
    *   需要调用后端 API 来更新数量。

2.  **后端 (`ReactStoreOrderController.cs` & `StoreOrderReactService.cs`)**:
    *   目前 `AddToCart` 逻辑是 **累加** (`detail.Quantity += request.Quantity`)。
    *   我们需要一个能 **设置** 数量的接口，或者修改 `AddToCart` 来支持覆盖。
    *   查看 `CartController` 有 `UpdateCartItemQuantity`，但它是基于 `CartItemGUID` 的。
    *   `ReactStoreOrderController` 也有 `AddToCart`，使用的是 `ProductCode`。
    *   为了保持一致性和简单性，最好在 `ReactStoreOrderController` 中添加一个 `UpdateCartItem` 接口，或者复用 `AddToCart` 但增加一个模式（增量 vs 覆盖）。
    *   或者，直接使用 `AddToCart` 并在前端计算差值？但这会有并发问题。
    *   最佳方案：在 `StoreOrderReactService` 中添加 `UpdateCartItemQuantityAsync` 方法，并在 `ReactStoreOrderController` 中暴露。

**方案**:

1.  **后端**:
    *   修改 `IStoreOrderReactService` 和 `StoreOrderReactService`，添加 `UpdateCartItemAsync` 方法。
        *   参数：`storeCode`, `productCode`, `quantity` (最终数量)。
        *   逻辑：查找对应明细，直接更新 Quantity、Amount、ImportAmount，然后更新主表总额。
    *   在 `ReactStoreOrderController` 添加 `POST cart/update` 接口。

2.  **前端**:
    *   修改 `src/services/storeOrder.ts`，添加 `updateCartItem` API 方法。
    *   修改 `CartDrawer.tsx`:
        *   在 `renderItem` 中，`InputNumber` 的 `onChange` 调用 `handleUpdateQuantity`。
        *   实现 `handleUpdateQuantity`，调用 `updateCartItem` API。
        *   更新成功后刷新购物车。

**详细步骤**:

1.  **后端 API**:
    *   `BlazorApp.Api/Interfaces/React/IStoreOrderReactService.cs`: 添加 `UpdateCartItemAsync`。
    *   `BlazorApp.Api/Services/React/StoreOrderReactService.cs`: 实现 `UpdateCartItemAsync`。
    *   `BlazorApp.Api/Controllers/React/ReactStoreOrderController.cs`: 添加 `UpdateCartItem` 端点。

2.  **前端 Service**:
    *   `src/services/storeOrder.ts`: 添加 `updateCartItem`。

3.  **前端 UI**:
    *   `CartDrawer.tsx`: 绑定 `InputNumber` 的 `onChange` 或 `onStep` (InputNumber onChange 可能会频繁触发，需要防抖或者只在 blur/enter 时触发？Antd InputNumber 还可以。)。
    *   为了用户体验，InputNumber 最好有防抖，或者使用 `onStep` 和 `onPressEnter`，或者简单的 `onChange` 加防抖。
    *   直接使用 `onChange` 配合 API 请求，只要不是极快输入，通常没问题。

**Update Plan**:
Let's implement a specific `UpdateCartItem` in the React service/controller stack to be clean.

*   **Request DTO**: We can reuse `AddToCartRequestDto` since it has `StoreCode`, `ProductCode`, `Quantity`. Just semantic difference: Quantity is the *new target quantity*, not delta.

**Implementation Details**:

*   **Backend**:
    *   Service: `UpdateCartItemAsync(AddToCartRequestDto request)`
        *   Find order (FlowStatus=0).
        *   Find detail by ProductCode.
        *   If not found -> Add new (Quantity = request.Quantity).
        *   If found -> Set Quantity = request.Quantity.
        *   Recalculate amounts.
        *   Update order total.

*   **Frontend**:
    *   `CartDrawer`:
        *   Replace pure text with `InputNumber`.
        *   `min={1}` (or 0 to remove?). Let's say min 1, delete button is separate.
        *   `onChange={(val) => handleUpdateQuantity(item.productCode, val)}`.

Let's do this.

