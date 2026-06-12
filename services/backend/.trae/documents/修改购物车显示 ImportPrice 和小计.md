# 修改购物车商品价格显示

用户希望在 `CartDrawer.tsx` 中修改商品价格的显示方式：
1.  **不再显示零售价**（目前显示的是 `item.price`，这通常是零售价或默认单价）。
2.  **改为显示 `ImportPrice`**。
3.  **显示小计**。

## 1. 现状分析

查看 `CartDrawer.tsx` 的代码：

```tsx
<Text style={{ fontSize: '12px' }}>${item.price.toFixed(2)} x {item.quantity}</Text>
```

和

```tsx
<Text strong>${item.amount.toFixed(2)}</Text>
```

这里 `item` 是 `StoreOrderCartItemDto` 类型。

我们需要确认 `StoreOrderCartItemDto` 中是否包含 `importPrice` 字段。
如果后端 API 已经返回了 `importPrice`，我们直接使用。
如果没有，需要检查后端 `StoreOrderService` (API) 和 `StoreOrderCartDto` 定义。

根据 `StoreOrderProductDto`（从其他文件推断），通常会有 `oemPrice`, `domesticPrice`, `importPrice`。
购物车项 `StoreOrderCartItemDto` 可能需要增加 `importPrice` 字段。

## 2. 检查 `StoreOrderCartItemDto` 定义

我需要查看 `src/services/storeOrder.ts` 来确认 `StoreOrderCartItemDto` 的定义。

假设 `item.price` 目前是单价。如果 `ImportPrice` 是成本价或其他价格，我们需要确认后端是否在 `GetCart` 时返回了这个字段。

## 3. 修改计划

1.  **确认前端接口**: 查看 `src/services/storeOrder.ts`。
2.  **确认后端 API**: 如果前端接口没有 `importPrice`，需要修改后端 `StoreOrderReactService.cs` 的 `GetCartAsync` 方法，将 `ImportPrice` 映射到 DTO。
3.  **修改前端显示**: 在 `CartDrawer.tsx` 中将 `item.price` 替换为 `item.importPrice` (如果存在) 或修改字段映射。

**注意**: 用户提到的 `ImportPrice` 是 `WarehouseProduct` 的字段。当添加到购物车时，购物车项（CartItem）通常会保存当时的单价。
如果购物车中的 `UnitPrice` (`item.price`) 已经是我们要显示的价格（例如对于分店订货，单价就是 ImportPrice），那么只需要改 label？
但用户明确说“不要显示零售价 改为显示 ImportPrice”。这暗示现在的 `item.price` 可能是零售价，或者用户想看另一个价格字段。

通常分店订货的价格（Cost）就是 `ImportPrice`（或 `DomesticPrice`/`OEMPrice` 取决于配置）。
如果现在的 `item.price` 就是订货价（Cost），那么可能只是措辞问题。
但如果现在的 `item.price` 是 RRP（零售价），那我们需要显示 Cost。

让我们先检查 `StoreOrderCartItemDto` 的定义。

**后端 CartItem 定义**:
`d:\Development\cline\blazor\BlazorApp.Shared\Models\HBweb\CartItem.cs`
```csharp
public decimal UnitPrice { get; set; } // 单价
```

在 `CartDrawer.tsx` 中：
```tsx
${item.price.toFixed(2)} x {item.quantity}
```

如果用户想要显示 `ImportPrice`，我们需要知道这个 `ImportPrice` 是来自商品表的原始字段，还是购物车中保存的字段。
通常购物车保存的是 `UnitPrice`。

**假设场景**: 分店订货时，`UnitPrice` **应该**就是订货价（Cost）。
如果用户说“改为显示 ImportPrice”，可能是指目前的 `UnitPrice` 取值不对，或者想额外显示一个参考字段。
或者，目前的 `item.price` 在前端被映射为了 `OEMPrice`？

查看 `src/services/storeOrder.ts` 会有帮助。

**行动计划**:
1.  读取 `src/services/storeOrder.ts`。
2.  修改 `CartDrawer.tsx`，将显示逻辑改为 `ImportPrice`。
    *   如果 DTO 里没有，我需要在后端添加。
    *   如果 DTO 里有（比如 `importPrice`），直接用。

**临时假设**: `StoreOrderCartItemDto` 可能没有 `importPrice`。
我们需要在后端 `GetCart` 时，把 `WarehouseProduct.ImportPrice` 带出来。

## 4. 执行步骤

1.  读取 `src/services/storeOrder.ts` 确认 DTO。
2.  读取 `BlazorApp.Api/Services/React/StoreOrderReactService.cs` (如果存在) 或 `CartService` 确认数据来源。
3.  如果需要，修改后端 DTO 和映射逻辑。
4.  修改前端 `CartDrawer.tsx`。

用户给出的代码片段：
```tsx
<Text style={{ fontSize: '12px' }}>${item.price.toFixed(2)} x {item.quantity}</Text>
```
用户想要：
```tsx
<Text style={{ fontSize: '12px' }}>Import: ${item.importPrice.toFixed(2)} x {item.quantity}</Text>
<Text strong>Subtotal: ${(item.importPrice * item.quantity).toFixed(2)}</Text>
```
（具体的布局样式要调整）

先看 DTO。
