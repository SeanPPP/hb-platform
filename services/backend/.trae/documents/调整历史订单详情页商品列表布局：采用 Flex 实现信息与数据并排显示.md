我将调整“历史订单详情页”中商品列表的布局，使其更加紧凑。

### 1. 修改 StoreHistoryOrders/Detail.tsx

*   **当前布局**：
    *   左侧：图片
    *   右侧：
        *   行 1：商品名称
        *   行 2：Item No
        *   行 3：Grid (价格 | 订货数 | 发货数)

*   **目标布局**：
    *   将右侧内容整合，使“商品信息”和“价格数量信息”在视觉上并排（如果空间允许）或者结构更清晰。
    *   由于移动端屏幕宽度有限，完全“平行”可能导致拥挤。
    *   **优化方案**：
        *   将 `Item No` 移到商品名称下方。
        *   取消灰色的 Grid 背景块，减少垂直间距。
        *   尝试将价格和数量信息放在商品名称的**右侧**（类似于表格行），或者让它们紧凑排列。

    **根据用户描述“从上下改成平行”**，我理解为希望左边是商品基本信息（名、号），右边是数据（价、量）。

    **新布局设计 (Flex Row)**：
    *   **左侧 (Flex: 1)**：商品名称 + Item No
    *   **右侧 (Flex: 0 0 auto)**：
        *   价格 (Import Price)
        *   数量 (Order / Sent)

    或者更具体的结构：
    ```tsx
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
       {/* 左侧：名称和编号 */}
       <div style={{ flex: 1, marginRight: 8 }}>
          <Text ellipsis={{ tooltip: item.productName }} style={{ display: 'block', fontWeight: 500 }}>{item.productName}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{item.itemNumber}</Text>
       </div>
       
       {/* 右侧：数据列 */}
       <div style={{ textAlign: 'right', minWidth: 80 }}>
          <div><Text strong>${(item.oemPrice || 0).toFixed(2)}</Text></div>
          <div style={{ fontSize: 12, marginTop: 4 }}>
             Order: {item.quantity}
          </div>
          <div style={{ fontSize: 12 }}>
             Sent: <Text type={...}>{item.allocQuantity || '-'}</Text>
          </div>
       </div>
    </div>
    ```

    用户可能希望三个指标（价格、订货、发货）也是平行的？
    如果是这样，移动端宽度可能不够。
    
    让我们尝试将 Grid 块上移，与商品名称并排？这不太合理。
    
    **另一种理解**：
    用户可能指的是我刚才做的 Grid 块（价格、数量）是在商品描述（名称/编号）的**下方**。用户希望这部分内容能移到右侧，或者和名称在同一水平线上。
    
    考虑到移动端适配，我会采用 **Flex 布局**，左侧放（图片+名称+编号），右侧放（价格+数量）。

### 2. 执行计划
修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StoreHistoryOrders\Detail.tsx`：

1.  移除 `description` 中的 Grid 块。
2.  重写 `renderItem` 的内容。
3.  不再使用 `List.Item.Meta` 的标准结构，而是自定义 `List.Item` 的子元素布局。
4.  **布局结构**：
    *   `<div style={{ display: 'flex' }}>`
        *   `<Image ... />` (左侧图片)
        *   `<div style={{ flex: 1, marginLeft: 12 }}>` (中间：名称/编号)
        *   `<div style={{ textAlign: 'right', marginLeft: 8 }}>` (右侧：价格/数量)

这样就实现了“平行”展示，不仅节省垂直空间，也符合移动端列表的常见设计。

**确认细节**：
*   **左侧**：图片
*   **中间**：商品名称 (加粗, 换行), Item No (灰色小字)
*   **右侧**：
    *   Import Price (加粗)
    *   Order: X
    *   Sent: Y (带颜色)

这种布局符合“平行”的要求。
