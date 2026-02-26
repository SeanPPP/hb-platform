## 目标
- 使用 `react-open-source-datagrid` 重写进货单明细表格，不使用 antd Table，保留/增强现有功能：虚拟化、列固定、筛选排序、单元格编辑、行选择、图片与状态渲染、批量操作对接现有业务逻辑。

## 兼容与映射
- 行数据：沿用 `LocalSupplierInvoiceItemDto`（src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx:23–25）。
- 选择状态：沿用 `selectedRowKeys`（index.tsx:25, 1127–1133）。
- 业务事件：保留 `onOpenMatchModal`、`onUpdateItem`、批量操作按钮等（index.tsx:1035–1116）。
- 列功能：迁移现有列配置（index.tsx:593–896）到 DataGrid 的 `columns`，将 antd 自定义渲染转换为 `renderCell`，可编辑列标记 `editable: true` 并用 `onCellEdit` 写回。

## 实施步骤
1. 依赖与样式
   - 安装 `react-open-source-datagrid` 并按文档导入样式（若需）：在根入口或组件内引入其 CSS；保持 Tailwind/主题为默认或 `Quartz` 风格。
2. 新建适配组件
   - 新建 `src/components/PurchaseDetailDataGrid.tsx`：
     - 接口：`items`, `rowMeta`, `selectedRowKeys`, `onSelectedRowKeysChange`, `onOpenMatchModal`, `onUpdateItem`, `height`。
     - 构造 `rows`（id=detailGUID），定义 `columns`：
       - `rowIndex`：序号（固定左侧）。
       - `productImage`：图片渲染（解析 URL，`Image` 预览）。
       - 文本列：`itemNumber`、`productName`、`barcode`（排序/筛选）。
       - 检测列：`supplierItemDetectResult`、`barcodeDetect`（`renderCell` 用 `Tag` 与查看按钮，支持固定右侧）。
       - 数值列：`quantity`、`lastPurchasePrice`、`purchasePrice`、`amount`、`retailPrice`（右对齐、格式化）。
       - 状态/编辑：
         - `autoPricing`：`renderCell` 渲染 `Switch`，触发定价计算与 `rowMeta` 更新（沿用 index.tsx:790–811 逻辑）。
         - `isSpecialProduct`：`Switch` 更新 `items`（index.tsx:812–824）。
         - `pricingFloatRate`：只读数值展示。
         - `newAutoRetailPrice`：`editable: true` 或 `renderCell` 为 `InputNumber`，`onCellEdit` 写回（index.tsx:846–879）。
         - `operation`：只读文本展示（或下拉编辑，后续可选）。
     - 启用虚拟化与列固定：`virtualScrollConfig={ { enabled: true, enableColumnVirtualization: false, overscanCount: 5 } }`，`pinnable` 用于固定关键列。
     - 选择回调：`onSelectionChange` → `onSelectedRowKeysChange`。
     - 编辑回调：`onCellEdit` → 调用 `onUpdateItem` 写回对应字段。
3. 页面替换
   - 在 `src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx` 用新组件替换 `OpenSourceGrid` 渲染（index.tsx:1124–1134），其余业务按钮与弹窗保持不变。
4. 验证
   - 横向滚动：列头与数据同步移动；左/右固定列生效。
   - 虚拟化：大数据量平滑滚动。
   - 编辑/筛选/排序：与现有逻辑一致，回写正确。
   - 选择与批量：选择、保存更改、价格分发功能不受影响。

## 风险与回退
- 风险低：仅替换表格渲染层，沿用同一数据与事件接口。
- 回退：保留旧组件 `OpenSourceGrid.tsx`，随时切换回原实现。必要时关闭列虚拟化确保表头同步。

## 交付
- 新组件文件、页面引用变更与必要的依赖/样式导入；附带使用说明与可扩展点（如列编辑下拉、更多主题）。