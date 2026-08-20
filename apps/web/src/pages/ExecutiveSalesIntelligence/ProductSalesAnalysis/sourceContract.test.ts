import { readFileSync } from 'node:fs'
import { join } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const pageSource = readFileSync(
  join(process.cwd(), 'src/pages/ExecutiveSalesIntelligence/ProductSalesAnalysis/index.tsx'),
  'utf8',
)
const styleSource = readFileSync(
  join(process.cwd(), 'src/pages/ExecutiveSalesIntelligence/ProductSalesAnalysis/index.module.css'),
  'utf8',
)
const chartSource = readFileSync(
  join(process.cwd(), 'src/pages/ExecutiveSalesIntelligence/ProductSalesAnalysis/DailySalesChart.tsx'),
  'utf8',
)

assert(pageSource.includes('buildDateRange(30)'), '默认日期必须由 Brisbane 日期 helper 生成')
assert(pageSource.includes('buildDateRange(days)'), '7/30/90 天快捷范围必须由 Brisbane 日期 helper 生成')
assert(!pageSource.includes("dayjs().startOf('day')"), '不得按浏览器本地时区生成默认日期')

const leftPanelSource = pageSource.split('商品选择</h3>')[1]?.split('<PanelState')[0] || ''
assert(leftPanelSource.includes('货号 / 中英文名称 / 条码'), '商品关键词过滤必须位于左栏')
assert(leftPanelSource.includes('澳洲供应商'), '澳洲供应商过滤必须位于左栏')
assert(leftPanelSource.includes('国内供应商'), '国内供应商过滤必须位于左栏')

assert(pageSource.includes("type: 'settleCurrentProduct'"), '当前商品只能由新 summary 响应通过状态机建立')
assert(pageSource.includes('aria-label={`查看商品 ${product.productCode} 每日销量与均价`}'), '商品汇总应提供可聚焦钻取按钮')
assert(pageSource.includes('aria-label={`查看分店 ${branch.branchCode} 每日销量与均价`}'), '分店汇总应提供可聚焦钻取按钮')
assert(pageSource.includes('shouldTriggerTableRowClick(event.target, event.currentTarget)'), '整行点击应隔离行内交互控件')
assert(
  !pageSource.includes('}, [appliedFilter, selection, candidatePage, candidatePageSize, refreshVersion])'),
  '勾选商品不得重新请求与选择状态无关的候选列表',
)
assert(
  pageSource.includes('queryProductSalesCandidates(\n      appliedFilter,\n      resetProductSelection(),'),
  '候选查询应使用稳定的全筛选选择语义，避免无效缓存分裂',
)
assert(!pageSource.includes('sorter: true'), '未接入远程排序时不得显示无效排序控件')

assert(styleSource.includes('grid-template-columns: 320px minmax(380px, 1fr) 360px'), '1366px 桌面应保持三栏')
assert(styleSource.includes('@media (max-width: 1199px)'), '较窄桌面应将右栏下移')
assert(styleSource.includes('@media (max-width: 768px)'), '移动宽度应切换单栏')
const tabIndexCount = (chartSource.match(/tabIndex=\{0\}/g) || []).length
assert(tabIndexCount === 1, '整张 SVG 应只有一个键盘焦点')
assert(chartSource.includes('role="img"'), 'SVG 应保持图片角色')
assert(!/<rect[\s\S]*?tabIndex/.test(chartSource), '柱状条不得进入 Tab 顺序')
assert(!/<circle[\s\S]*?tabIndex/.test(chartSource), '均价点不得进入 Tab 顺序')
assert(chartSource.includes('xAxisTicks'), '图表应渲染 x 轴日期刻度')
assert(chartSource.includes('height?: number'), '图表应支持可选 height，现有调用无需传值')
assert(chartSource.includes('const CHART_HEIGHT = 260'), '图表默认高度必须保持 260')
assert(chartSource.includes('height = CHART_HEIGHT'), '未传 height 时必须回退到默认 260')
assert(
  chartSource.includes('buildDailyChartModel(data, CHART_WIDTH, height, CHART_PADDING)'),
  '图表模型必须按实际 height 计算',
)
assert(
  chartSource.includes('const plotBottom = height - CHART_PADDING.bottom'),
  'x 轴基线位置必须使用实际 height',
)
assert(
  chartSource.includes('viewBox={`0 0 ${CHART_WIDTH} ${height}`}'),
  'SVG viewBox 必须使用实际 height',
)
assert(
  chartSource.includes("style={{ width: '100%', height: 'auto'"),
  '显示高度必须由实际 height 驱动的 viewBox 等比决定',
)
assert(!pageSource.includes('recharts') && !pageSource.includes('echarts'), '页面不得新增图表依赖')
assert(pageSource.includes('useReducer'), '选择/筛选状态机应由 useReducer 统一管理')
assert(pageSource.includes('productSalesAnalysisViewReducer'), '选择/筛选状态机应由纯 reducer 驱动')
assert(pageSource.includes("type: 'commitSelection'"), '所有选择变更应统一走 commitSelection 入口')

const commitFiltersSource = pageSource.slice(
  pageSource.indexOf('const commitFilters'),
  pageSource.indexOf('const commitSelection'),
)
const commitSelectionSource = pageSource.slice(
  pageSource.indexOf('const commitSelection'),
  pageSource.indexOf('const applyQuickRange'),
)

const filterInvalidationNames = [
  'optionsAbortRef',
  'optionsGuardRef',
  'candidatesAbortRef',
  'candidatesGuardRef',
  'summaryAbortRef',
  'summaryGuardRef',
  'productDailyAbortRef',
  'productDailyGuardRef',
  'branchesAbortRef',
  'branchesGuardRef',
  'branchDailyAbortRef',
  'branchDailyGuardRef',
]
const selectionInvalidationNames = [
  'summaryAbortRef',
  'summaryGuardRef',
  'productDailyAbortRef',
  'productDailyGuardRef',
  'branchesAbortRef',
  'branchesGuardRef',
  'branchDailyAbortRef',
  'branchDailyGuardRef',
]

assert(commitFiltersSource.includes('invalidateRequests'), '提交筛选必须同步废弃旧请求')
assert(
  commitFiltersSource.indexOf('invalidateRequests')
    < commitFiltersSource.indexOf("dispatchView({ type: 'commitFilter'"),
  '提交筛选必须先废弃旧请求再清空状态',
)
filterInvalidationNames.forEach((name) => {
  assert(commitFiltersSource.includes(name), `提交筛选必须废弃 ${name} 对应请求`)
})

assert(commitSelectionSource.includes('invalidateRequests'), '提交选择必须同步废弃旧请求')
assert(
  commitSelectionSource.indexOf('invalidateRequests')
    < commitSelectionSource.indexOf("dispatchView({ type: 'commitSelection'"),
  '提交选择必须先废弃旧请求再清空状态',
)
selectionInvalidationNames.forEach((name) => {
  assert(commitSelectionSource.includes(name), `提交选择必须废弃 ${name} 对应请求`)
})

console.log('ProductSalesAnalysis.sourceContract.test: ok')
