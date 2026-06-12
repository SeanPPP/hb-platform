## 原因判断
- 代码已使用 `headerRenderer`，但不同版本的 react-data-grid（RDG）对列头渲染属性有差异：新版通常使用 `renderHeaderCell` 而不是 `headerRenderer`。
- 表头高度与布局需加载 RDG 官方样式并设置 CSS 变量 `--rdg-header-row-height` 才会生效。

## 解决方案
1) 兼容列头渲染属性
- 在 `DetailDataGrid.tsx` 的列映射中，将 `headerRenderer` 改为 `renderHeaderCell`（保留同样的 JSX 输入框）。
- 同时保留标题与输入框的组合，确保每列（除图片/序号）都能显示输入框。

2) 强制表头行高
- 已设置 `headerRowHeight={56}`；再以 `style={{ ['--rdg-header-row-height']: '56px' }}` 双重保障。

3) 样式加载
- 已在 `src/app.ts` 引入 `react-data-grid/lib/styles.css`；确保 RDG 的默认样式与表头布局生效。

4) 兜底外部过滤行（若仍不可见）
- 在表格上方单独渲染一行“外部过滤栏”，为各列生成对应输入框，复用现有 `filters` 状态与过滤逻辑；图片/序号不显示输入框。

## 验证
- 打开明细页，列头下方出现输入框；输入关键字或数值区间即时过滤；点击列头可排序。