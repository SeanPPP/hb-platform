## 问题原因分析
- DetailDataGrid 作为 antd Table → react-data-grid 的适配层，没有把 `rowIdx` 传入 antd 列的 `render(value, record, index)`，导致依赖 `index` 的序号/行号列显示不随当前可见顺序变化。
  - 证据：`src/components/DetailDataGrid.tsx:17-27` 中 `renderCell` 仅传 `(value, row)`，未传递第三参 index。
- 部分页面采用静态字段 `rowIndex` 预计算行号（如套装明细），在排序/过滤后不会更新，显示错误。
  - 证据：`src/pages/DomesticProducts/SetItemsModal.tsx:459-465` 先 `map` 出 `rowIndex` 再作为列显示；`554-561` 的 `'#'` 列没有使用 `rowIdx`。
- 正确做法：在 react-data-grid 中使用 `renderCell({ rowIdx }) => rowIdx + 1` 或把 `rowIdx` 作为第三参数传给旧的 antd `render`，保证行号随可见顺序（排序/过滤/虚拟滚动）动态更新。
  - 示例参考：`src/pages/ContainerDetail/index.tsx:468` 使用 `renderCell: ({ rowIdx }) => rowIdx + 1`，显示正确。

## 变更方案
1. DetailDataGrid 适配器增强：
   - 修改 `renderCell` 签名为 `({ row, rowIdx, column })`，并把 `rowIdx` 作为第三个参数传入 antd 列的 `render(value, record, index)`。
   - 为典型序号列提供内置兜底：当列的 `key/dataIndex` 是 `__index` 或标题为 `序号/#` 时，默认 `renderCell: ({ rowIdx }) => rowIdx + 1`，无需依赖数据中的静态字段。
   - 可选：把 antd 的 `fixed: 'left'|'right'` 映射为 react-data-grid 的 `frozen: true`，保证序号列冻结（有助于对齐行号体验）。

2. 套装明细 SetItemsModal 修正：
   - 移除静态 `rowsWithIndex`；DataGrid 的 `rows` 使用排序后的 `sortedItems` 即可。
   - 将 `'#'` 列改为 `renderCell: ({ rowIdx }) => rowIdx + 1`，并保留 `frozen: true` 与宽度配置。

3. 若业务存在分页延续行号的需求：
   - 在 DetailDataGrid 增加可选 `rowIndexOffset` Prop（默认 0）；当外部传入分页信息时显示为 `rowIdx + 1 + rowIndexOffset`。当前仅在实际需要时启用，避免复杂度增加。

## 影响范围与兼容性
- 不改变数据结构；仅改渲染逻辑，兼容现有 antd 列定义与 `columns.render`。
- 行号将随排序/过滤/拖拽列重排与虚拟滚动动态更新，符合预期。
- 其他列渲染不受影响；若有列的 `render` 依赖第三参 `index`，此次增强将恢复其预期行为。

## 验证步骤
- 在进货单明细页与套装明细弹窗：
  - 滚动表格、按不同列排序/过滤，检查行号是否始终与可见顺序一致。
  - 选择/编辑单元格，确认行号列冻结显示正确。
- 对比 `ContainerDetail` 页行为，确保一致性。

## 代码改动要点（拟议）
- `src/components/DetailDataGrid.tsx`
  - `renderCell` 传递 `rowIdx`：`col.render(value, row, rowIdx)`。
  - 对 `__index/序号/#` 列提供默认 `rowIdx + 1`。
  - 可选：映射 `fixed` → `frozen`。
- `src/pages/DomesticProducts/SetItemsModal.tsx`
  - 删除 `rowsWithIndex`，改用 `sortedItems`。
  - `'#'` 列改为 `renderCell: ({ rowIdx }) => rowIdx + 1`。

如确认以上方案，我将按上述文件位置提交最小必要改动，并进行页面内联验证。