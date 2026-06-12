# InnerPack 计算逻辑调整计划

根据您的要求，我们需要修改 `InnerPack` 的计算逻辑：优先使用 `Order Qty`，如果 `Order Qty` 为空或 0，则使用 `Send Qty` (即 `allocQuantity`) 进行计算。

## 逻辑说明

**当前逻辑**:
`InnerPack = Order Qty / Min Order Qty`

**新逻辑**:
1.  获取计算基数 `baseQty`。
2.  如果 `Order Qty > 0`，则 `baseQty = Order Qty`。
3.  否则，`baseQty = Send Qty` (即 `allocQuantity`)。
4.  `InnerPack = baseQty / Min Order Qty`。

## 实施步骤

1.  **修改 `PickingList/index.tsx`**:
    *   更新 `getInnerPack` 函数或在渲染时调整传入参数。
    *   为了保持函数纯粹性，我将修改 `getInnerPack` 函数，使其接收两个数量参数 (`orderQty` 和 `sendQty`)，并在内部进行判断。

    ```typescript
    const getInnerPack = (orderQty: number, sendQty: number, minQty: number) => {
      if (!minQty || minQty === 0) return '-';
      
      const baseQty = (orderQty && orderQty > 0) ? orderQty : (sendQty || 0);
      
      if (baseQty === 0) return '-'; // 如果两个都为0，显示 - 或 0

      const packs = baseQty / minQty;
      return Number.isInteger(packs) ? packs : packs.toFixed(1);
    };
    ```

    *   更新 JSX 调用处：
        ```tsx
        {getInnerPack(item.quantity, item.allocQuantity || 0, item.minOrderQuantity)}
        ```

是否确认执行此计划？