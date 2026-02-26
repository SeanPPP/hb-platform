## 目标
- 进货单列表与明细页面全部改用 Ant Design Table，移除 react-data-grid。
- 当数据量较大时启用虚拟滚动以提升渲染性能。

## 技术方案
- 新增通用组件 `VirtualTable`：基于 Antd Table + react-window 自定义 `components.body`，实现仅渲染可视行。
- 使用阈值控制：当行数超过阈值（如 500）时切换到虚拟滚动；否则使用标准 Table。
- 保留现有列配置、交互逻辑（查看、删除、图片渲染、标签等）。

## 具体改动
1) 新增组件
- 文件：`src/components/VirtualTable.tsx`
- 功能：
  - 接收 `columns`, `dataSource`, `rowKey`, `height` 等参数
  - 内部用 `react-window` 的 `FixedSizeList` 渲染 Table body
  - 兼容 Antd 的选择、排序、滚动、默认列属性等常用特性

2) 列表页替换
- 文件：`src/pages/PosAdmin/LocalSupplierInvoices/index.tsx`
- 变更：
  - 移除 `VirtualInvoiceGrid` 引用和使用
  - 使用 `VirtualTable`：当 `total > 500` 时传入相同 `columns`、`rows` 和 `onView/onDelete` 的操作列
  - 其他逻辑（筛选、分页、排序、按钮）保持不变

3) 明细页替换
- 文件：`src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx`
- 变更：
  - 移除 `VirtualInvoiceDetailGrid`
  - 使用 `VirtualTable` 渲染明细行，保留图片、数值格式化、Tag 展示
  - 性能模式提示保留，切换到虚拟滚动实现

4) 清理旧组件
- 文件：`src/components/VirtualInvoiceGrid.tsx`、`src/components/VirtualInvoiceDetailGrid.tsx`
- 操作：删除或标记为废弃以避免误用

5) 依赖
- 新增 `react-window`（生产依赖）。不改动其他依赖。

## 验证
- 启动开发服务，打开列表与明细页面：
  - 大数据量下滚动顺畅、无 “Element type is invalid” 错误
  - 查看/删除操作与导航保持一致
- 类型检查通过，无新的隐式 any 或签名不匹配问题

若确认，我将按上述步骤实现替换，并进行性能与功能验证。