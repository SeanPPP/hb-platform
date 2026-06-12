# 解决分店选择器与 StoreHome 组件重构计划

## 问题分析
1.  **StoreCode 错误**: 当前 `StoreHome/index.tsx` 在添加到购物车时，尝试从 `localStorage` 获取 `selectedStoreCode`，如果获取不到则使用了硬编码值（可能导致 "DEFAULT" 或 "STORE001"）。实际上应该直接使用全局状态 `initialState.selectedStore` 中的 `storeCode`。
2.  **组件复用问题**: `StoreHome` 目前复用了 `StoreOrder` 模块的 `ProductCard` 组件。根据要求，需要将 `StoreHome` 独立出来，使用自己的组件副本。

## 实施步骤

### 1. 重构组件 (StoreHome)
*   在 `src/pages/StoreHome` 下创建 `components` 目录。
*   复制 `src/pages/StoreOrder/components/ProductCard.tsx` 到 `src/pages/StoreHome/components/ProductCard.tsx`。
*   修改 `src/pages/StoreHome/index.tsx` 引用新的 `ProductCard` 组件。

### 2. 修正 StoreCode 逻辑 (StoreHome)
*   修改 `src/pages/StoreHome/index.tsx` 中的 `handleAddToCart` 函数。
*   **移除** 对 `localStorage` 的依赖。
*   **改为** 从 `initialState?.selectedStore?.storeCode` 获取。
*   如果未选择分店（即 `selectedStore` 为空），则传递空字符串 `""` 或 `null` 给 `addToCart` 接口，符合"add 请求 中 storeCode 也该是 空的"的要求。

### 3. 验证 StoreLayout (StoreLayout)
*   确认 `StoreLayout.tsx` 正确维护了 `initialState.selectedStore`。
*   无需修改 `StoreLayout.tsx` 的逻辑，因为它是正确更新全局状态的，问题主要在于 `StoreHome` 没有正确消费这个状态。

## 预期结果
*   当分店选择器为空时，点击“添加到购物车”，请求体中的 `storeCode` 将为空。
*   `StoreHome` 将拥有独立的 UI 组件，不再依赖 `StoreOrder` 目录下的组件。
