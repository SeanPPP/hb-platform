## 修复 TypeScript 错误

**错误 1：移除 rowHeight 属性**
- Ant Design Table 组件不支持 `rowHeight` 属性
- 删除 `rowHeight={60}` 配置

**错误 2：修复国际化参数**
- 标题使用正确的国际化参数化方式
- 修改标题使用 `localSupplierInvoiceDetail.detailsCount` 并传入 `count` 参数