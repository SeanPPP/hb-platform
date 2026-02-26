## 原因
- 国际化字典缺少键 `menu.posAdmin.localSupplierInvoiceCreate`，导致 React Intl 报告缺失并回退默认文案。

## 修改
- 在 `src/locales/zh-CN.ts` 与 `src/locales/en-US.ts` 的菜单区域添加该键：
  - zh-CN：`'menu.posAdmin.localSupplierInvoiceCreate': '新建进货单'`
  - en-US：`'menu.posAdmin.localSupplierInvoiceCreate': 'Create Local Supplier Invoice'`

## 验证
- 启动后无缺失键警告；界面对应位置显示正确文案。

## 影响范围
- 仅增加国际化字典键值，不影响功能逻辑。