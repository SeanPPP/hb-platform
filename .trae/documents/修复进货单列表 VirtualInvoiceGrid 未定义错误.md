## 原因
- 列表页 `LocalSupplierInvoices/index.tsx` 使用了 `VirtualInvoiceGrid` 但顶部缺少导入语句，运行时报错 “VirtualInvoiceGrid is not defined”。

## 修复
- 在 `LocalSupplierInvoices/index.tsx` 顶部补充导入：`import VirtualInvoiceGrid from '@/components/VirtualInvoiceGrid'`
- 保持现有回调参数类型 `record: DataType` 不变，避免隐式 any。

## 验证
- 类型诊断无误；页面渲染条件 `total > 500` 时正常显示虚拟化表格；否则按原 `antd Table` 显示。

我将添加缺失的导入并重新诊断。