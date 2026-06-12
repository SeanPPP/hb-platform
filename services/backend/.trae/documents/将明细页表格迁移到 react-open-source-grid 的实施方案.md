## 目标
- 用 `react-open-source-grid` 替换分店进货单明细页的 `react-data-grid`，保留现有功能：左侧选择列与固定列、右侧操作列、图片单元格、可编辑开关、前端排序与过滤、接近满屏高度。

## 依赖与样式
- 安装依赖：`react-open-source-grid`
- 全局样式引入：在应用入口添加其样式（若提供），保持与主题一致，例如 `theme="quartz"`

## 列与数据映射
- 行主键：将 `detailGUID` 映射到行的 `id` 字段，其他字段保持原键名（如 `itemNumber`、`productName`、`barcode`、`quantity`、`purchasePrice` 等）
- 列定义改为 `Column[]`：
  - 左侧固定：选择列（库内置）+ 图片列 + 货号列
  - 中间：名称、条码、数量、上次进货价、进货价、金额、零售价、折扣率、定价浮率、新自动零售价
  - 右侧固定：检测结果、条码匹配、操作列
  - 可编辑：`isSpecialProduct` 改为 `editable: true` 并用自定义 `renderCell` 渲染开关（或使用库内置编辑）
  - 排序/过滤：为需要的列设置 `sortable: true`、`filterable: true`
- 图片列：使用 `renderCell` 输出 `<img>` 并控制大小与 `objectFit: 'contain'`
- 检测/匹配列：读取 `rowMeta` 合成显示 Tag 与查看按钮（在 `renderCell` 内触发现有弹窗）

## 事件与交互
- 选中行：使用 `onSelectionChange`→ 更新 `selectedRowKeys`（以 `id` 为准）
- 单元格编辑：`onCellEdit`→ 更新本地 `items` 与必要的 `rowMeta`
- 行点击/批量操作：保留现有按钮与逻辑（检测、自动定价、批量执行、保存更改）

## 布局与高度
- 容器高度：使用 `style={{ height: 'calc(100vh - 220px)' }}` 或组件提供的高度属性；
- 列固定：开启 `showColumnPinning` 并为对应列设置 pin（若库支持左右固定），否则通过列顺序与操作列置末尾实现近似右固定效果

## 代码组织
- 新建封装组件 `components/OpenSourceGrid.tsx`：
  - 接收当前页的 `items`、`rowMeta`、`selectedRowKeys` 等；
  - 生成 `columns: Column[]` 与 `rows: Row[]`；
  - 统一处理 `onSelectionChange`、`onCellEdit`、排序/过滤（如果需要手动干预）；
- 在 `PosAdmin/LocalSupplierInvoiceDetail/index.tsx` 用新组件替换 `DetailDataGrid`

## 验证
- 本地运行页面：
  - 左侧选择列与图片/货号固定；
  - 右侧出现检测结果、条码匹配、操作列；
  - 输入过滤与点击排序生效；
  - 切换“特殊商品”开关即时更新行数据；
  - 保存更改、批量执行等功能正常

## 兼容与回退
- 若部分特性在新库下体验不佳（如右固定或复杂筛选），保留旧组件以便快速切回；
- 封装组件内提供简单开关以切换底层 grid 实现。

## 交付
- 提交页面修改与新封装组件，保持原接口不变；
- 说明文件简述迁移差异与可用配置（主题、分页、列固定等）。