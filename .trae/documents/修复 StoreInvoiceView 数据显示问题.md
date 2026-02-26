## 修复 StoreInvoiceView 页面数据不显示问题

**问题原因：**
组件未接收 props 参数，导致从 TabLayout 传递的 `invoiceGuid` 无法获取

**修复方案：**
修改 `StoreInvoiceView/index.tsx` 组件：
1. 添加 Props 接口定义
2. 接收 `invoiceGuid` 作为 props 参数
3. 支持 props 和 URL 参数两种获取方式（与 LocalSupplierInvoiceDetail 保持一致）

**修改代码：**
```typescript
interface Props { invoiceGuid?: string }

export default function StoreInvoiceViewPage(props: Props) {
  const p = useParams<{ invoiceGuid: string }>()
  const invoiceGuid = props.invoiceGuid || p.invoiceGuid
```
