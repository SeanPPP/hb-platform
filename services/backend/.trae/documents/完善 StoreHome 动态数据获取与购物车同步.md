# 解决 StoreHome 和 StoreLayout 购物车同步问题

## 问题分析

用户提出了三个核心问题：
1.  **StoreLayout 购物车抽屉**: 点击购物车时，LayoutCartDrawer 没有显示当前分店的购物车信息。
2.  **StoreHome 购物车摘要**: 点击 Add 按钮后，StoreLayout 的购物车 Summary 没有更新。
3.  **StoreHome 商品卡片**: `StoreHome/components/ProductCard.tsx` 没有显示该商品在当前分店购物车中的数量。

### 原因分析

1.  **LayoutCartDrawer 问题**:
    *   在 `StoreLayout.tsx` 中，传递给 `LayoutCartDrawer` 的 `cart` 状态是 `StoreLayout` 自己维护的 `cart` 状态。
    *   当 `StoreOrder` 或 `StoreHome` 中添加商品时，如果只触发了 `refreshGlobalCart`（即 `StoreLayout` 的 `fetchCart`），理论上 `cart` 状态会更新。
    *   但如果 `LayoutCartDrawer` 没有正确接收或渲染，或者 `fetchCart` 没有正确获取到数据，就会导致显示为空。
    *   需要检查 `StoreLayout` 的 `fetchCart` 逻辑是否正确更新了 `cart` 状态。

2.  **StoreHome Add 按钮更新问题**:
    *   `StoreHome/index.tsx` 中的 `handleAddToCart` 已经调用了 `(window as any).refreshGlobalCart()`。
    *   如果 `StoreLayout` 的 Summary 没更新，可能是 `refreshGlobalCart` 调用时机或 `fetchCart` 内部逻辑问题。
    *   也有可能是 `StoreHome` 中的 `initialState.selectedStore` 没有正确获取，导致添加到了错误的分店（或者空分店）。

3.  **StoreHome 商品卡片数量显示问题**:
    *   `StoreHome/index.tsx` 获取商品列表时使用了 `getStoreOrderProducts`，这只返回商品基本信息。
    *   它**缺少**获取商品动态数据（购物车数量、最后订购时间等）的逻辑。`StoreOrder` 页面的 `ProductGrid` 组件有一个额外的 `fetchDynamicData` 步骤来获取这些信息。
    *   `StoreHome/index.tsx` 目前没有调用 `getProductsDynamicData`，也没有将 `dynamicData` 传递给 `ProductCard`。

## 实施计划

### 1. 完善 StoreHome 逻辑 (StoreHome/index.tsx)
*   **引入服务**: 引入 `getProductsDynamicData` 和 `StoreOrderDynamicDataDto`。
*   **添加状态**: 添加 `dynamicData` 状态 `Record<string, StoreOrderDynamicDataDto>`。
*   **获取动态数据**:
    *   在 `fetchProducts` 成功后，或者 `products` 更新后，调用 `fetchDynamicData`。
    *   `fetchDynamicData` 需要使用 `initialState.selectedStore.storeCode` 和当前商品列表的 ID。
*   **更新渲染**: 将对应的 `dynamicData` 传递给 `ProductCard` 组件。
*   **添加后刷新**: 在 `handleAddToCart` 成功后，除了刷新全局购物车，还需要重新调用 `fetchDynamicData` 以更新卡片上的“In Cart”数量。

### 2. 验证 StoreLayout 逻辑 (StoreLayout.tsx)
*   检查 `fetchCart` 函数。确认它在 `refreshGlobalCart` 被调用时能正确获取最新的 `selectedStore`。
*   由于 `fetchCart` 使用了 `initialState?.selectedStore`，在闭包中可能捕获了旧值。但 `fetchCart` 是在组件内部定义的，每次渲染都会重建吗？不，它是 `useEffect` 依赖的一部分。
*   **关键点**: `(window as any).refreshGlobalCart = fetchCart` 是在 `useEffect` 中赋值的，依赖于 `initialState?.selectedStore`。这意味着每次分店变化，全局函数都会更新，闭包问题应该不大。
*   **潜在问题**: 如果 `fetchCart` 内部依赖的 `initialState` 是旧的（虽然 `useModel` 返回的应该是最新的），可能会有问题。最好在 `fetchCart` 内部直接读取最新的 `initialState`（虽然无法直接读取，但可以通过 `useModel` 的返回值）。
*   **更稳妥的做法**: 确保 `fetchCart` 逻辑无误。

### 3. 解决 LayoutCartDrawer 问题
*   确保 `StoreLayout` 正确传递了 `cart` 对象给 `LayoutCartDrawer`。
*   如果 `StoreLayout` 的 `cart` 更新了，Drawer 应该能看到。
*   如果 Drawer 还是空的，可能是 `getActiveCart` 返回了空，或者 StoreCode 不匹配。

## 具体步骤

1.  **修改 `StoreHome/index.tsx`**:
    *   实现 `fetchDynamicData` 逻辑。
    *   在 `useEffect` 中监听 `products` 和 `initialState.selectedStore` 的变化来触发 `fetchDynamicData`。
    *   在 `handleAddToCart` 中调用 `fetchDynamicData`。
    *   传递 `dynamicData` 给 `ProductCard`。

2.  **无需修改 StoreLayout (如果没有发现逻辑错误)**:
    *   只要 `StoreHome` 正确更新了数据并调用了 `refreshGlobalCart`，`StoreLayout` 应该能正常工作。

## 预期结果
*   `StoreHome` 的商品卡片将显示 "In Cart: X" 标签。
*   在 `StoreHome` 添加商品后，卡片上的数量会更新。
*   `StoreLayout` 的购物车图标上的数字会更新。
*   打开右侧购物车抽屉能看到刚添加的商品。
