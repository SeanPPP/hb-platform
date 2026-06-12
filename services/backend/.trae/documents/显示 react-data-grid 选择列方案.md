## 问题
- 已启用行选择数据（selectedRows/onSelectedRowsChange），但未显示复选框列，需要显式添加选择列。

## 方案
- 在通用组件 `DetailDataGrid` 中引入 `SelectColumn` 并作为首列插入到传入列前：
  - `import { DataGrid, type Column, SelectColumn } from 'react-data-grid'`
  - `columns={[SelectColumn, ...rdgColumns]}`
- 保持现有选中集 `selectedRowKeys`/变更回调逻辑不变。

## 验证
- 打开分店进货单明细页，表格左侧显示复选框选择列；批量操作可基于选择行正常使用。