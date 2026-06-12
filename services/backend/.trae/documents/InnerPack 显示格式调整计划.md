# InnerPack 格式与分店名称显示调整计划 (修订版)

根据您的最新反馈，我们需要执行两个调整：
1.  **InnerPack 格式**: 数字后面添加 "pk"。
2.  **分店名称显示**: 修正页头分店显示逻辑，避免显示 "1017 (1017)" 这种重复代码的情况，应显示真正的分店名称。

## 1. InnerPack 格式调整
修改 `getInnerPack` 函数，在返回的数值后追加 " pk"。

## 2. 分店名称显示逻辑修正
当前显示逻辑是 `{storeName} ({order.storeCode})`。
如果 `storeName` 和 `order.storeCode` 相同（即未找到分店名称，使用了代码回退），则会导致显示如 "1017 (1017)"。

**修正方案**:
*   检查 `storeName` 是否等于 `order.storeCode`。
*   如果相等，只显示 `storeName` (即代码)。
*   如果不相等，显示 `Store Name (Store Code)`。
*   或者，更彻底地检查 `fetchData` 中的赋值逻辑，确保获取到真正的名称。如果在 `getStores` 中没找到，`setStoreName` 会被设为 `storeCode`，这就是原因。
*   **改进显示**: 在渲染时判断。
    ```tsx
    <div>
      <span className="label">Store:</span> 
      {storeName === order.storeCode ? storeName : `${storeName} (${order.storeCode})`}
    </div>
    ```

## 实施步骤

1.  **修改 `PickingList/index.tsx`**:
    *   更新 `getInnerPack` 函数添加 "pk"。
    *   更新 Header 中的 Store 显示逻辑。

是否确认执行此计划？