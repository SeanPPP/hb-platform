## 修改计划

### 需要修改的文件
`d:\Development\cline\blazor\ReactUmi\my-app\src\pages\PosmSalesOrders\index.tsx`

### 具体修改内容

将第313-319行的硬编码中文文本替换为国际化调用：

**修改前：**
```jsx
<span>数量: {item.quantity}</span>
<span>单价: ${(item.unitPrice || 0).toFixed(2)}</span>
<span>折扣: ${(item.discountAmount || 0).toFixed(2)}</span>
<Text type="danger" strong>
  小计: ${(item.actualAmount || 0).toFixed(2)}
</Text>
```

**修改后：**
```jsx
<span>{intl.formatMessage({ id: 'posmSalesOrder.quantity' })}: {item.quantity}</span>
<span>{intl.formatMessage({ id: 'posmSalesOrder.unitPrice' })}: ${(item.unitPrice || 0).toFixed(2)}</span>
<span>{intl.formatMessage({ id: 'posmSalesOrder.discount' })}: ${(item.discountAmount || 0).toFixed(2)}</span>
<Text type="danger" strong>
  {intl.formatMessage({ id: 'posmSalesOrder.subtotal' })}: ${(item.actualAmount || 0).toFixed(2)}
</Text>
```

### 说明
国际化文件 `zh-CN.ts` 和 `en-US.ts` 中已包含所需的翻译键，无需额外修改。