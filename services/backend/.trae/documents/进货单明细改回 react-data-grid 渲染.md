## 原因与目标
- 目标：将进货单明细改回使用 `react-data-grid` 的通用适配组件 `src/components/DetailDataGrid.tsx`，避免 antd Table，保留横向滚动同步与虚拟化性能。
- 现状：页面已切到 `PurchaseDetailDataGrid`（基于 react-open-source-grid）。用户希望统一回到 `react-data-grid` 方案；项目中已存在 `DetailDataGrid`，且支持将 antd 的列定义映射为 RDG 列（筛选/排序/冻结列/选择列）。

## 实施方案
1. 页面替换
- 在 `src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx`：
  - 替换组件引入为 `DetailDataGrid`。
  - 用现有 `columns`（`ColumnsType<LocalSupplierInvoiceItemDto>`）传入 `DetailDataGrid` 的 `columns`。
  - 传入 `rows={items}`；`rowKeyGetter={(row)=>row.detailGUID}`；`selectedRowKeys` 与 `onSelectedRowKeysChange` 保持现有逻辑；`height='calc(100vh - 220px)'`；`rowHeight=48`。
2. 渲染与交互保持
- 图片、检测结果、条码匹配、自动定价、特殊商品、定价浮率、新自动零售价、操作等列的 `render` 已在 antd 列中实现，`DetailDataGrid` 的 `renderCell` 将直接复用它们，交互与状态更新保持不变。
- 选择列使用 RDG 的 `SelectColumn`；排序与筛选由 `DetailDataGrid` 内部管理（支持数值范围表达式与文本包含）。
3. 性能与滚动
- RDG 内置行虚拟化，横向滚动时表头与数据同步；固定左列通过 `fixed: 'left'` 映射为 `frozen`（右固定暂不支持，列顺序保持以便操作列在右侧）。

## 验证要点
- 横向滚动时列头与数据联动；左侧固定列正常。
- 选择、编辑（`InputNumber`/`Switch`）、筛选与排序正常；批量与保存逻辑不受影响。

## 变更范围与风险
- 仅替换明细渲染组件与调用参数，业务逻辑与状态管理不变；风险低。