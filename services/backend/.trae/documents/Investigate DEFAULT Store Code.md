# 调查 "DEFAULT" 分店代码来源

用户提供的日志显示，在未选择分店的情况下，添加购物车请求携带了 `storeCode: "DEFAULT"`。这与我们的代码逻辑（未选择分店应拦截请求）相矛盾。

**现象**:
1. 用户界面显示未选择分店。
2. 点击添加购物车。
3. 请求发送了，且 `storeCode` 为 `"DEFAULT"`。

**分析**:
在 `ProductGrid.tsx` 中，我们有明确的检查：
```typescript
if (!storeCode) {
  message.error('Please select a store first');
  return;
}
```
既然请求发出去了，说明 `storeCode` 在那一刻确实**不为空**，且值为 `"DEFAULT"`。

可能的解释：
1.  **代码中存在硬编码的 "DEFAULT"**: 可能在某个 Service 层或者组件的默认值中写死了 "DEFAULT"。
2.  **后端返回了 "DEFAULT"**: 获取分店列表时，可能包含了一个代码为 "DEFAULT" 的隐形分店，或者 `initialState` 初始化时被赋予了这个值。
3.  **服务层拦截**: `addToCart` 函数内部可能在 `storeCode` 为空时自动填充了 "DEFAULT"。

为了解决这个问题，我需要找到这个 "DEFAULT" 是哪里来的，并将其移除。

## 任务计划

1.  **全局搜索 "DEFAULT"**: 在 `src` 目录下搜索字符串 `"DEFAULT"`，查找是否有硬编码的默认值。
2.  **检查 `src/services/storeOrder.ts`**: 查看 `addToCart` 函数是否有默认参数处理。
3.  **检查 `src/pages/StoreOrder/components/ProductCard.tsx`**: 确认传递给父组件的参数是否正确。

一旦找到来源，我将修改代码，确保在未明确选择分店时，`storeCode` 保持为空，从而触发前端的验证拦截，而不是发送错误的请求。
