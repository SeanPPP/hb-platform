## 原因
- react-data-grid 的导入方式不当：`{ Column }` 应作为类型导入；默认导出需确保只使用 `import DataGrid from 'react-data-grid'`。
- 同样问题存在于 `VirtualInvoiceDetailGrid`。

## 修复
- 修改两个组件的导入：
  - `import DataGrid from 'react-data-grid'`
  - `import type { Column } from 'react-data-grid'`
- 其他逻辑不变。

## 验证
- 列表与明细页能正常渲染虚拟化表格；控制台不再出现 “Element type is invalid”。