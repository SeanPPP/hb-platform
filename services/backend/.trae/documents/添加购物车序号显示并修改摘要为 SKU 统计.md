# 修改购物车显示：添加序号与 SKU 统计

## 问题分析
用户提出了两个修改请求：
1.  **LayoutCartDrawer (购物车抽屉)**: 商品列表每一项前面显示序号 (index)。
2.  **CartHeaderSummary (顶部摘要)**: 将 "Units: X" 改为 "SKU: Y"，即显示商品种类数量而不是总件数。

## 实施计划

### 1. 修改 `src/components/LayoutCartDrawer/index.tsx`
*   在 `List` 的 `renderItem` 方法中，获取 `index` 参数。
*   在 `List.Item.Meta` 的 `avatar` 属性前，或 `title` 前，添加序号显示。
*   考虑到美观，可以将序号放在 `Avatar` 左侧，作为一个小的数字标签。

### 2. 修改 `src/components/CartHeaderSummary.tsx`
*   将 "Units:" 标签改为 "SKU:"。
*   显示的值从 `cart.totalQuantity` 改为 `cart.items.length`（即商品行数/种类数）。
    *   注意：`StoreOrderCartDto` 接口定义中并没有直接的 `totalSKU` 字段，但 `items` 数组的长度即为 SKU 数量。需要确认 `cart` 对象中是否包含完整的 `items` 列表（通常 Header 摘要可能只包含统计数字，不包含详情列表）。
    *   检查 `StoreOrderCartDto`：包含 `items: StoreOrderCartItemDto[]`。
    *   在 `StoreLayout.tsx` 中，`fetchCart` 获取的是完整购物车数据，所以 `cart.items` 应该是可用的。
    *   如果 `items` 为空（例如后端为了性能只返回了统计头），我们需要检查 `cart` 对象的来源。`StoreLayout` 调用的是 `getActiveCart`，它通常返回完整详情。

## 预期结果
1.  购物车抽屉中的商品列表会有 1, 2, 3... 的序号。
2.  顶部导航栏的购物车摘要显示 SKU 数量而非总件数。
