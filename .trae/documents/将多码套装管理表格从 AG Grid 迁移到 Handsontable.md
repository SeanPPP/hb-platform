## 目标
- 替换 `MultiCodeSets` 页面中的 AG Grid 为 Handsontable（React 版），使用免费许可 `licenseKey: 'non-commercial-and-evaluation'`。
- 保持现有后端接口与字段映射不变，继续支持服务器端分页、排序、过滤与批量操作。

## 依赖与配置
- 安装依赖：`handsontable`、`@handsontable/react`
- 引入样式：`import 'handsontable/styles/handsontable.css'`
- 在 `HotTable` 组件上设置 `licenseKey="non-commercial-and-evaluation"`

## 页面改造（MultiCodeSets）
- 修改文件：`ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`
- 组件替换：
  - 用 `HotTable`（来自 `@handsontable/react`）替代 `AgGridReact`
  - 移除 AG Grid 的数据源与模型设置（serverSide/infinite），改为 Handsontable 的事件驱动加载
- 列定义：将现有 `columnDefs.tsx` 转换为 Handsontable `columns` 数组与 `colHeaders`（保持字段与标题一致：供应商、商品货号、主条码、套装货号、套装条码、进货价、零售价、更新日期、更新人）
- 表格功能：
  - 启用插件：`filters`、`dropdownMenu`、`multiColumnSorting`、`contextMenu`、`manualColumnResize`、`selection`
  - 行/列头：`rowHeaders: true`、`colHeaders: [...]`
  - 选择逻辑：使用 `hotInstance.getSelected()` 获取选区，将选中行转换为批量操作的记录集合
  - 价格编辑：为价格列设置 `type: 'numeric'` 与适当的 `numericFormat`
- 分页外置：
  - 使用 Ant `Pagination` 组件放在表格下方，维护 `currentPage`、`pageSize`
  - 每次分页、排序、过滤时调用后端 `getGridData(request)`，将结果写入 `data` 状态
- 请求参数构造：
  - 全局搜索：顶部 `Input.Search` 字段映射为 `globalSearch`
  - 排序：从 `afterColumnSort` 读取 `sortConfig`，映射为 `SortModelDto[]`（`{ colId, sort }`）
  - 过滤：从 `getPlugin('Filters')` 读取条件，转换为 `FilterModelDto` 字典（文本/数字/集合类型）
  - 分页：`startRow = (page-1)*pageSize`，`endRow = startRow + pageSize`
- 批量操作：沿用现有 `doBatchStatus`、`doBatchPrices`、`doBatchDelete`，通过选中行的数据集合生成请求

## 类型与适配
- 新增/更新类型文件：`ReactUmi/my-app/src/types/multiCodeSet.ts`
  - 增加 Handsontable 事件需要的本地类型（可选，或直接用 `any`）
- 删除或保留 `columnDefs.tsx`：若保留，将其改写导出 `colHeaders` 与 `columns`；若删除，在页面内内联定义列

## 事件与数据流
- 初次加载：`useEffect` 调用 `loadData({ page, pageSize, sortModel: [], filterModel: {} })`
- 排序：`afterColumnSort` → 重新请求数据
- 过滤：`afterFilter` → 重新请求数据
- 分页：Ant Pagination `onChange` → 重新请求数据
- 编辑价位：`afterChange` 针对价格列收集更改并触发批量更新（或保持按钮触发）

## 兼容性与权限
- 保持现有 `pos-admin/multi-code-sets` 路由与菜单配置不变
- 使用 `licenseKey: 'non-commercial-and-evaluation'`
- 仅 `Admin` 允许批量修改/删除；`Admin, WarehouseManager` 允许查询（后端已控制）

## 验证
- 启动开发服务，访问 `/pos-admin/multi-code-sets`
- 验证：
  - 搜索、排序、过滤、分页均通过后端返回数据更新表格
  - 选中行执行批量启用/禁用、批量更新价格、批量删除
  - 编辑价格后批量提交可成功

## 交付项
- 更新的 `MultiCodeSets` 页面（替换为 Handsontable）
- 列定义（Handsontable 格式）与分页条
- 保留后端服务与接口不变（无需调整服务层）