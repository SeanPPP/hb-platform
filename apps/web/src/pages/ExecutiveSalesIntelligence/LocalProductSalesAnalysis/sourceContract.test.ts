import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const base = join(process.cwd(), 'src/pages/ExecutiveSalesIntelligence/LocalProductSalesAnalysis')
const pagePath = join(base, 'index.tsx')
const cssPath = join(base, 'index.module.css')

if (!existsSync(pagePath) || !existsSync(cssPath)) {
  throw new Error('本地商品分析页面与响应式样式尚未实现')
}

const page = readFileSync(pagePath, 'utf8')
const css = readFileSync(cssPath, 'utf8')

for (const token of ['queryLocalSupplierProductSalesAnalysisCandidates', 'queryLocalSupplierProductSalesAnalysisSummary', 'queryLocalSupplierProductSalesAnalysisProductDaily', 'queryLocalSupplierProductSalesAnalysisInvoiceDetails', 'queryLocalSupplierProductSalesAnalysisBranches', 'queryLocalSupplierProductSalesAnalysisBranchDaily', 'candidateGuardRef', 'selectedSummaryGuardRef', 'detailGuardRef', 'dailyGuardRef', 'branchGuardRef', 'forceRefreshPending', 'consumeForceRefresh', 'disabledDate', 'getDateRangeError', 'clearAnalysisState', 'canSetCurrentProduct', 'shouldSelectFirstCandidateRef', 'ProductImage', 'FlowTrendChart', '本地进货单明细', '授权分店销量排行', '全选筛选结果', '清空选择', 'getCurrentProductAfterCancellation']) {
  if (!page.includes(token)) throw new Error(`页面缺少必要交互或请求隔离：${token}`)
}
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
// forceRefreshPending 是单次缓存刷新内部键，不属于 UI 或状态提示文案。
const displaySource = page.split('forceRefreshPending').join('')
if (prohibited.some((token) => displaySource.includes(token))) {
  throw new Error('页面源码不得暴露内部状态文案')
}

console.log('LocalProductSalesAnalysis.sourceContract.test: ok')
