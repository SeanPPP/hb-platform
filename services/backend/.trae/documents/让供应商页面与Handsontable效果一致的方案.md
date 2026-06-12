## 问题定位
- 多码页面为 Handsontable 网格：`ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`（如 `import '@handsontable/react-wrapper'` 在 `3-5` 行）。
- 当前观感偏“原生”，列头与密度、主题与分组不够友好，缺少分组表头与视觉层次。

## 设计目标
- 更贴近官方示例的清晰分组与主题风格。
- 更紧凑的密度与对齐，表头更醒目，数值更易读。
- 保留现有筛选、排序、批量操作与分页逻辑。

## 实施要点
1. 列分组（`nestedHeaders`）
   - 按业务分组：商品信息（供应商/货号/主条码）、套装信息（套装货号/条码）、价格信息（进货价/零售价）、更新信息（日期/人）。
   - 在 `HotTable` 增加 `nestedHeaders`，对齐 demo 的多级表头视觉层次。
2. 列宽与冻结
   - 设置 `colWidths`（如 160/140/140/...），保证主列可读；
   - `fixedColumnsLeft: 2` 冻结前两列，滚动更稳。
3. 主题与密度
   - 保留 `className="ht-theme-default"`，新增页面局部样式覆盖：表头背景、边框色、行高与斑马纹；
   - 提供密度切换（紧凑/常规），通过切换容器类影响行高与字体大小。
4. 数值与日期渲染
   - `numericFormat: { pattern: '0,0.00' }` 提升可读性；
   - 为日期列添加渲染器格式化为 `YYYY-MM-DD`。
5. 排序与过滤体验
   - 开启 `columnSorting: true` 与 `multiColumnSorting: true`，保留 `afterColumnSort` 回写后端；
   - 保留 `filters/dropdownMenu/contextMenu` 组合，优化菜单项顺序。
6. 交互与可用性
   - 开启 `copyPaste: true`、`manualColumnResize: true`、`autoColumnSize: true`；
   - 鼠标悬停高亮当前行，提升定位感；
   - 选区样式更明显（覆盖 `.area` 样式）。

## 代码改动范围
- `ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`：新增 `nestedHeaders/colWidths/fixedColumnsLeft/columnSorting/multiColumnSorting/copyPaste` 配置与日期渲染器。
- `ReactUmi/my-app/src/pages/MultiCodeSets/index.less`：新增主题覆盖与密度类（表头背景、边框、斑马纹、选区、行高）。

## 验证
- 视觉：对比前后截图，检查分组表头、冻结列、斑马纹、密度切换是否生效。
- 功能：筛选/排序/分页与批量操作保持正常；编辑价格与批量更新验证无回归。

## 后续可选增强
- 工具栏增加主题/密度切换控件；
- 列配置记忆（localStorage）以保留用户列宽调整；
- 大数据优化：`viewportRowRenderingOffset` 与惰性渲染参数微调。

请确认是否按此方案实施，我将开始修改并验证效果。