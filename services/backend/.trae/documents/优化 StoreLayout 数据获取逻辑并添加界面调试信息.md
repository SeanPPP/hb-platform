# 修复 StoreLayout 数据解析错误

## 问题定位
用户提供的日志非常关键：
```
StoreLayout.tsx:99 🛒 getActiveCart raw response: {success: true, data: {…}} 
StoreLayout.tsx:102 🛒 Parsed cart data: {orderGUID: '...', storeCode: '1033', ...} 
StoreLayout.tsx:112 🛒 Failed to get cart data or empty {orderGUID: '...', ...}
```

这说明：
1.  `getActiveCart` 返回的 `res` 是 `{success: true, data: {...}}`。
2.  `const apiResponse = (res as any).data || res;` 这一行逻辑将 `apiResponse` 解析成了 `res.data`，即 `{orderGUID: '...', ...}`（**这是购物车数据本身，而不是包含 success 字段的包装对象**）。
3.  接下来的判断 `if (apiResponse && apiResponse.success && apiResponse.data)` **失败了**！
    *   因为此时 `apiResponse` 是购物车数据对象，它里面**没有** `success` 字段，也**没有** `data` 字段。
    *   所以代码走到了 `else` 分支，打印了 `Failed to get cart data or empty`，并清空了购物车。

## 根本原因
`StoreLayout.tsx` 中的响应解析逻辑过度处理了。
`request` 拦截器似乎已经解包了一层（或者 `umi-request` 的行为），导致 `res` 本身就是 `{ success: true, data: {...} }`。
但是 `const apiResponse = (res as any).data || res;` 这一行：
*   如果 `res` 有 `data` 属性（它是购物车数据的一部分吗？不，购物车数据通常没有 `data` 属性）。
*   等等，日志显示 `raw response` 是 `{success: true, data: {…}}`。
*   那么 `(res as any).data` 就是购物车数据对象。
*   所以 `apiResponse` 变成了购物车数据对象。
*   然后检查 `apiResponse.success` -> `undefined` -> `false`。
*   于是判断失败。

## 修复方案
我们需要正确处理响应结构。
既然 `raw response` 是标准的 `{success: true, data: {...}}`，我们应该直接使用它，而不是尝试去取 `.data`（除非是 axios 的 response wrapper，但这里的 raw response 看起来已经是解包后的 body 了）。

**修正逻辑**:
直接检查 `res.success` 和 `res.data`。

```typescript
    try {
      const res = await getActiveCart(selectedStore.storeCode);
      console.log('🛒 getActiveCart raw response:', res);
      
      // 如果 res 本身包含 success 字段，说明它就是 API 响应体
      // 这里的逻辑有点混乱，我们统一一下：
      // 假设 res 就是 { success: boolean, data: any, message?: string }
      
      if (res && res.success && res.data) {
        const cartData = res.data as StoreOrderCartDto;
        setCart(cartData);
        setCartSummary({
          count: cartData.totalQuantity || 0,
          total: cartData.totalAmount || 0,
        });
      } else {
        // ...
      }
```

**为什么之前的代码 `const apiResponse = (res as any).data || res;` 会出错？**
因为 `res` 是 `{ success: true, data: {...} }`。
`(res as any).data` 存在，且值为购物车数据。
所以 `apiResponse` 被赋值为购物车数据。
然后检查 `apiResponse.success`，购物车数据里没有这个字段，所以失败。

## 实施步骤
修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\layouts\StoreLayout.tsx`。
简化并修正 `fetchCart` 中的响应解析逻辑。

## 预期结果
修改后，`fetchCart` 将正确识别成功响应，`setCart` 将被调用，购物车图标和抽屉将显示正确数据。
