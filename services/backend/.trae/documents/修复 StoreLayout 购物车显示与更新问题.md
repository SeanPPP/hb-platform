# 解决购物车数据为空及图标显示问题

## 问题定位
用户截图显示购物车摘要为 0，且分店已选择。这表明 `StoreLayout` 已获取到购物车状态，但数据为空。
可能原因：
1.  **数据获取失败**: `getActiveCart` 返回的数据结构可能与预期不符，导致解析失败。
2.  **Badge 缺失**: `StoreLayout` 的购物车图标确实缺少 `Badge` 组件，导致无法在图标上直接看到数量。

## 实施计划

### 1. 增强 StoreLayout 调试与显示 (StoreLayout.tsx)
*   **添加 Badge**: 在 `ShoppingCartOutlined` 图标上包裹 `Badge` 组件，显示 `cartSummary.count`。
*   **添加调试日志**: 在 `fetchCart` 函数中打印：
    *   当前的 `selectedStore.storeCode`。
    *   `getActiveCart` 的原始返回结果。
    *   解析后的 `cartData`。
    这将帮助我们确认是 API 返回了空数据，还是前端解析逻辑有误。

### 2. 检查数据解析逻辑
*   对比 `StoreOrder` 和 `StoreLayout` 的数据解析逻辑。
*   如果日志显示 API 返回了数据但前端没解析到，将修正解析逻辑（例如 `(res as any).data || res` 的兼容性处理）。

### 3. 验证 StoreHome 交互
*   确保 `StoreHome` 添加商品后，`refreshGlobalCart` 被正确调用。

## 预期结果
*   修改后，购物车图标右上角将显示红色数字徽标（Badge）。
*   通过控制台日志，我们可以明确知道为什么购物车数据为 0（是 API 返回 0 还是前端没读到）。
*   如果只是解析逻辑问题，修正后摘要和抽屉将立即显示正确数据。
