我将通过以下步骤解决 `ProductCard.tsx` 中的类型错误问题：

### 1. 确认原因
Ant Design 5.x 的 `Typography.Text` 组件在类型定义上不支持 `rows` 属性（仅支持单行省略）。要实现多行省略，需要使用 `Typography.Paragraph` 组件。

### 2. 修改代码
我将修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StoreHome\components\ProductCard.tsx` 文件：

*   **引入 `Paragraph`**：从 `Typography` 中解构出 `Paragraph`。
    ```typescript
    const { Text, Title, Paragraph } = Typography;
    ```
*   **替换组件**：将报错的 `<Text>` 组件替换为 `<Paragraph>`。
*   **调整样式**：
    *   保留原有的 `ellipsis={{ tooltip: product.productName, rows: 2 }}` 配置。
    *   添加 `style={{ maxWidth: '70%', marginBottom: 0 }}`，确保消除 `Paragraph` 默认的底部边距，保持布局一致。

### 3. 验证
修改后，类型检查应通过，且页面上商品名称应正确显示为最多 2 行，超出部分显示省略号，并带有 Tooltip。
