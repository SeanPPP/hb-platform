import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const base = join(process.cwd(), 'src/pages/ExecutiveSalesIntelligence/LocalProductSalesAnalysis')
const pagePath = join(base, 'index.tsx')
const cssPath = join(base, 'index.module.css')
const logicPath = join(base, 'logic.ts')

if (!existsSync(pagePath) || !existsSync(cssPath) || !existsSync(logicPath)) {
  throw new Error('本地商品分析页面、响应式样式与页面逻辑尚未实现')
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function count(source: string, token: string) {
  return source.split(token).length - 1
}

function block(source: string, start: string, end: string) {
  const startIndex = source.indexOf(start)
  assert(startIndex >= 0, `缺少区块起点：${start}`)
  const endIndex = source.indexOf(end, startIndex + start.length)
  assert(endIndex >= 0, `缺少区块终点：${end}`)
  return source.slice(startIndex, endIndex)
}

const page = readFileSync(pagePath, 'utf8')
const css = readFileSync(cssPath, 'utf8')
const logic = readFileSync(logicPath, 'utf8')

// 交互与请求隔离
for (const token of [
  'queryLocalSupplierProductSalesAnalysisBootstrap',
  'queryLocalSupplierProductSalesAnalysisCandidates',
  'queryLocalSupplierProductSalesAnalysisSummary',
  'queryLocalSupplierProductSalesAnalysisProductDaily',
  'queryLocalSupplierProductSalesAnalysisInvoiceDetails',
  'queryLocalSupplierProductSalesAnalysisBranches',
  'queryLocalSupplierProductSalesAnalysisBranchDaily',
  'bootstrapGuardRef',
  'createPageRequestTimeout',
  'PAGE_BOOTSTRAP_TIMEOUT_SECONDS',
  'autoSelectFirst',
  'candidatePageNumber',
  'candidatePageSize',
  'summaryPageNumber',
  'summaryPageSize',
  'forceRefresh',
  'disabledDate',
  'getDateRangeError',
  'clearAnalysisState',
  'canSetCurrentProduct',
  'ProductImage',
  'FlowTrendChart',
  '本地进货单明细',
  '授权分店销量排行',
  '全选筛选结果',
  '清空选择',
  'getCurrentProductAfterCancellation',
  'loadCandidatePage',
  'retrySection',
  'loadBranchDaily',
]) {
  assert(page.includes(token), `页面缺少必要交互或请求隔离：${token}`)
}
assert(logic.includes('AbortController'), '页面安全超时必须基于 AbortController')

// 首屏/查询/重置/刷新只走唯一 bootstrap 通道
assert(count(page, 'queryLocalSupplierProductSalesAnalysisBootstrap(') === 1, 'bootstrap 请求只能有一个调用点')
for (const token of [
  'queryLocalSupplierProductSalesAnalysisCandidates(',
  'queryLocalSupplierProductSalesAnalysisSummary(',
  'queryLocalSupplierProductSalesAnalysisProductDaily(',
  'queryLocalSupplierProductSalesAnalysisInvoiceDetails(',
  'queryLocalSupplierProductSalesAnalysisBranches(',
  'queryLocalSupplierProductSalesAnalysisBranchDaily(',
  'getLocalSupplierProductSalesAnalysisOptions(',
]) {
  assert(count(page, token) === 1, `分段接口只能用于重试/分页/钻取单点调用：${token}`)
}
const mountBlock = block(page, 'useEffect(() => {', '}, [runBootstrap])')
assert(mountBlock.includes("runBootstrap('bootstrap')"), '挂载必须恰好发起一次 bootstrap')
assert(!mountBlock.includes('queryLocalSupplierProductSalesAnalysis'), '挂载不得再走分段接口')
const applyBlock = block(page, 'const applyFilters', 'const resetFilters')
assert(applyBlock.includes("runBootstrap('bootstrap')"), '查询必须走 bootstrap')
assert(!applyBlock.includes('queryLocalSupplierProductSalesAnalysis'), '查询不得再走分段接口')
const resetBlock = block(page, 'const resetFilters', 'const setRangeDays')
assert(resetBlock.includes("runBootstrap('bootstrap')"), '重置必须走 bootstrap')
const refreshBlock = block(page, 'const refresh', 'const retryBootstrap')
assert(refreshBlock.includes("runBootstrap('refresh')"), '刷新必须走 bootstrap')
assert(!refreshBlock.includes('queryLocalSupplierProductSalesAnalysis'), '刷新不得叠加分段请求')

// 分页只请求 candidates；普通选择/商品切换复用分段 API；分店钻取仍 branch-daily。
const paginationBlock = block(page, 'const loadCandidatePage', 'const retrySection')
assert(paginationBlock.includes('queryLocalSupplierProductSalesAnalysisCandidates('), '候选分页必须只请求 candidates')
assert(!paginationBlock.includes('queryLocalSupplierProductSalesAnalysisBootstrap('), '候选分页不得再请求 bootstrap')
assert(paginationBlock.includes("setLoadPhase('idle')"), '候选分页取代 bootstrap 时必须收口旧加载态')
const branchBlock = block(page, 'const loadBranchDaily', 'const detailColumns')
assert(branchBlock.includes('queryLocalSupplierProductSalesAnalysisBranchDaily('), '分店钻取必须仍走 branch-daily')
assert(branchBlock.includes('setBranchDailyError(undefined)'), '分店钻取重试成功前后必须清除旧错误')
const retryBlock = block(page, 'const retrySection', 'const detailColumns')
assert(retryBlock.includes('requestAnalysisSection('), '分段重试必须统一走可取消的分段请求协调器')
const sectionRouterBlock = block(page, 'function queryAnalysisSection', 'function PanelState')
for (const token of [
  'getLocalSupplierProductSalesAnalysisOptions(',
  'queryLocalSupplierProductSalesAnalysisSummary(',
  'queryLocalSupplierProductSalesAnalysisInvoiceDetails(',
  'queryLocalSupplierProductSalesAnalysisProductDaily(',
  'queryLocalSupplierProductSalesAnalysisBranches(',
]) {
  assert(sectionRouterBlock.includes(token), `分段请求协调器必须保留对应 API：${token}`)
}
const selectionBlock = block(page, 'const updateCandidate', 'const selectAllFiltered')
assert(selectionBlock.includes('loadSummaryForSelection(next)'), '普通勾选变化必须只刷新汇总分段')
assert(selectionBlock.includes('loadCurrentProductSections(fallback, next)'), '取消当前商品且本页有替代项时必须并行刷新当前商品分段')
assert(selectionBlock.includes("runBootstrap('switch'"), '仅跨页找不到替代项时允许 bootstrap 解析有效当前商品')
assert(
  page.includes("const migrateCurrentOnSummary = mode === 'switch' && migrateCurrentOnSummaryRef.current")
    && page.includes('migrateCurrentOnSummaryRef.current = false'),
  '跨页当前商品迁移意图必须绑定单次 bootstrap，不能残留到后续 refresh',
)
assert(page.includes('loadCurrentProductSections(candidate, selectionRef.current)'), '普通商品点击不得重复加载候选和选项')

// 查询/重置统一骨架；刷新保留旧数据并显示刷新态
assert(page.includes("const analysisLoading = loadPhase === 'bootstrap' || loadPhase === 'switch'"), '查询/切换必须统一骨架状态')
assert(page.includes('<Skeleton active'), '统一加载态必须使用卡片骨架而非无限转圈')
assert((page.match(/loading=\{analysisLoading/g) || []).length >= 5, '汇总/当前商品/分店等原有卡片必须统一骨架')
assert(page.includes("loading={loadPhase === 'bootstrap' || candidatePaging}"), '候选卡片查询时必须骨架且分页可独立转圈')
assert(page.includes("loading={loadPhase === 'refresh'}"), '刷新按钮必须显示刷新态')
assert(page.includes('analysis.candidates !== null && !analysis.candidates.items.length'), '候选只有在真实加载过且为空时才显示暂无数据')
assert(page.includes('analysis.sectionErrors.summary'), '汇总分段失败必须按卡片显示错误')
assert(page.includes('analysis.sectionErrors.invoiceDetails') && page.includes('analysis.sectionErrors.productDaily'), '当前商品分段失败必须按卡片显示错误')
assert(page.includes('analysis.sectionErrors.branches'), '分店分段失败必须按卡片显示错误')

// 8 秒超时与竞态
assert(page.includes('createPageRequestTimeout(PAGE_BOOTSTRAP_TIMEOUT_SECONDS)'), '页面请求必须使用统一安全超时')
assert(page.includes('bootstrapGuardRef.current.isCurrent(token)'), 'bootstrap 竞态旧响应必须被 guard 丢弃')
assert(page.includes('请求超时'), '超时必须给出可重试提示')

// 分店钻取键盘可聚焦按钮
if (!page.includes('className={styles.branchButton}') || page.includes('onClick: () => setSelectedBranchCode')) {
  throw new Error('分店钻取必须提供键盘可聚焦按钮')
}

if (page.includes('forceRefreshEpoch') || page.includes('setTimeout(() => setForceRefresh')) {
  throw new Error('刷新不得因重置状态再触发第二轮请求')
}

const clearStart = page.indexOf('const clearAnalysisState')
const clearEnd = page.indexOf('const applyFilters', clearStart)
const clearBlock = page.slice(clearStart, clearEnd)
for (const token of ['setBranchDaily([])', 'setSelectedBranchCode(undefined)', 'setBranchDailyError(undefined)', 'setBranchDailyLoading(false)']) {
  if (!clearBlock.includes(token)) throw new Error(`新筛选必须立即清空分店日趋势遗留：${token}`)
}

for (const token of ['29fr', '43fr', '28fr', '@media (max-width: 1199px)', '@media (max-width: 768px)']) {
  if (!css.includes(token)) throw new Error(`页面缺少响应式三栏契约：${token}`)
}

const prohibited = ['Pend' + 'ing', 'Fail' + 'ed', '水' + '位', '对' + '账', '统计' + '状态']
const displaySource = page.split('forceRefreshPending').join('')
if (prohibited.some((token) => displaySource.includes(token))) {
  throw new Error('页面源码不得暴露内部状态文案')
}

console.log('LocalProductSalesAnalysis.sourceContract.test: ok')
