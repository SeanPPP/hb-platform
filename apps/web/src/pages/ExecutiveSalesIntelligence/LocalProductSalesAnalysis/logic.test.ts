import {
  applyCandidateSelection,
  applyLocalProductSalesAnalysisBootstrapResult,
  applyLocalProductSalesAnalysisSectionResult,
  buildBrisbaneDefaultRange,
  buildLocalProductSalesAnalysisBootstrapRequest,
  canSetCurrentProduct,
  clearLocalProductSalesAnalysisDetailSections,
  clearLocalProductSalesAnalysisSectionError,
  createEmptyLocalProductSalesAnalysisState,
  createIncludedSelection,
  createLatestRequestGuard,
  createPageRequestTimeout,
  getDateRangeError,
  getCurrentProductAfterCancellation,
  isSelected,
  PAGE_BOOTSTRAP_TIMEOUT_SECONDS,
  setLocalProductSalesAnalysisSectionError,
} from './logic'

function equal<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function deepEqual(actual: unknown, expected: unknown, message: string) {
  equal(JSON.stringify(actual), JSON.stringify(expected), message)
}

const range = buildBrisbaneDefaultRange(30, new Date('2026-08-19T04:00:00.000Z'))
deepEqual(range, { startDate: '2026-07-20', endDate: '2026-08-18' }, '默认范围必须是 Brisbane 昨天向前 30 天')
equal(getDateRangeError('2025-08-18', '2026-08-18', '2026-08-18'), undefined, '366 天的含首尾范围必须允许')
equal(getDateRangeError('2025-08-17', '2026-08-18', '2026-08-18'), '参数错误：日期范围不能超过 366 天', '超过 366 天必须给局部参数错误')
equal(getDateRangeError('2026-08-18', '2026-08-19', '2026-08-18'), '参数错误：日期范围截至 Brisbane 昨天', '今天和未来必须禁止')

let selection = createIncludedSelection(['P1'])
selection = applyCandidateSelection(selection, 'P2', true)
equal(isSelected(selection, 'P1'), true, '跨页已选商品必须保留')
equal(isSelected(selection, 'P2'), true, '当前页选择必须加入')
selection = applyCandidateSelection(selection, 'P1', false)
equal(isSelected(selection, 'P1'), false, '取消当前商品必须移出选择')
equal(canSetCurrentProduct(selection, 'P1'), false, '未勾选候选行不能设为当前商品')
equal(canSetCurrentProduct(selection, 'P2'), true, '已勾选候选行可以设为当前商品')

const current = { productCode: 'P2', productName: '跨页商品' }
equal(getCurrentProductAfterCancellation(current, [{ productCode: 'P3', productName: '首项商品' }], false)?.productCode, 'P2', '未取消时必须保留跨页快照')
equal(getCurrentProductAfterCancellation(current, [{ productCode: 'P3', productName: '首项商品' }], true)?.productCode, 'P3', '仅取消当前商品时迁移到 summary 首项')

const bootstrapFilter = { startDate: '2026-07-20', endDate: '2026-08-18', keyword: '玩具' }
const bootstrapPages = { candidatePageNumber: 1, candidatePageSize: 20, summaryPageNumber: 1, summaryPageSize: 50 }

// 挂载/查询/重置：自动选首项、不强制刷新、不带旧选择与旧当前商品
const firstScreenRequest = buildLocalProductSalesAnalysisBootstrapRequest({
  filter: bootstrapFilter, autoSelectFirst: true, forceRefresh: false, ...bootstrapPages,
})
deepEqual(firstScreenRequest, { filter: bootstrapFilter, autoSelectFirst: true, forceRefresh: false, ...bootstrapPages }, '首屏/查询 bootstrap 必须省略旧选择并自动选中首项')
equal(JSON.stringify(firstScreenRequest).includes('selection'), false, '首屏/查询不得携带旧选择')
equal(JSON.stringify(firstScreenRequest).includes('currentProductCode'), false, '首屏/查询不得携带旧当前商品')

// 刷新：保留原选择/当前商品、不自动选首项、强制刷新
const carriedSelection = { mode: 'included' as const, includedProductCodes: ['LP-1'], excludedProductCodes: [] }
const refreshRequest = buildLocalProductSalesAnalysisBootstrapRequest({
  filter: bootstrapFilter, selection: carriedSelection, currentProductCode: 'LP-1', autoSelectFirst: false, forceRefresh: true, ...bootstrapPages,
})
deepEqual(refreshRequest, { filter: bootstrapFilter, selection: carriedSelection, currentProductCode: 'LP-1', autoSelectFirst: false, forceRefresh: true, ...bootstrapPages }, '刷新必须携带原选择与当前商品且不自动选首项')

// 尊重用户主动清空：刷新携带空选择且不自动选首项
const clearedSelection = { mode: 'included' as const, includedProductCodes: [], excludedProductCodes: [] }
const clearedRequest = buildLocalProductSalesAnalysisBootstrapRequest({
  filter: bootstrapFilter, selection: clearedSelection, autoSelectFirst: false, forceRefresh: true, ...bootstrapPages,
})
deepEqual(clearedRequest, { filter: bootstrapFilter, selection: clearedSelection, autoSelectFirst: false, forceRefresh: true, ...bootstrapPages }, '刷新必须尊重用户主动清空选择')

// 页面专用 8 秒安全超时
equal(PAGE_BOOTSTRAP_TIMEOUT_SECONDS, 8, '页面安全超时必须是 8 秒')
{
  const timeout = createPageRequestTimeout(0.01)
  await new Promise((resolve) => setTimeout(resolve, 30))
  equal(timeout.signal.aborted, true, '超过安全超时必须中止请求')
  equal(timeout.signal.reason?.name, 'AbortError', '超时中止必须是 AbortError')
  timeout.clear()
}
{
  const timeout = createPageRequestTimeout(0.01)
  timeout.clear()
  await new Promise((resolve) => setTimeout(resolve, 30))
  equal(timeout.signal.aborted, false, '成功后清理超时不得再中止请求')
}
{
  const timeout = createPageRequestTimeout(8)
  timeout.abort()
  equal(timeout.signal.aborted, true, '发起新请求必须立即中止旧请求')
  timeout.clear()
}

// 竞态旧 bootstrap 响应不得提交
const bootstrapGuard = createLatestRequestGuard()
const staleBootstrap = bootstrapGuard.next()
const latestBootstrap = bootstrapGuard.next()
equal(bootstrapGuard.isCurrent(staleBootstrap), false, '竞态旧 bootstrap 响应不得提交')
equal(bootstrapGuard.isCurrent(latestBootstrap), true, '最新 bootstrap 响应必须可提交')

// 原子提交契约：一次调用替换全部数据分段
const options = { warehouseCategories: [{ guid: 'cat-1', name: '玩具' }], suppliers: [] }
const candidates = { items: [{ productCode: 'LP-1', productName: '本地玩具' }], total: 1, pageNumber: 1, pageSize: 20 }
const currentProduct = { productCode: 'LP-1', productName: '本地玩具' }
const summary = {
  totals: { purchaseQuantity: 8, purchaseAmount: 50, netSalesQuantity: -2, netSalesAmount: -12, sellThroughRate: null },
  items: [{ productCode: 'LP-1', suppliers: [], purchaseQuantity: 8, purchaseAmount: 0, netSalesQuantity: 0, netSalesAmount: 0, sellThroughRate: null }],
  total: 1, pageNumber: 1, pageSize: 20,
}
const invoiceDetails = { items: [{ detailGuid: 'D1', invoiceNo: 'INV-1', quantity: 3, purchasePrice: 2.5, amount: 7.5 }], total: 1, pageNumber: 1, pageSize: 20 }
const productDaily = [{ date: '2026-08-18', purchaseQuantity: 3, purchaseAmount: 7.5, netSalesQuantity: -1, netSalesAmount: -4, averageUnitPrice: null }]
const branches = [{ branchCode: 'S1', branchName: '布里斯班店', netSalesQuantity: 0, netSalesAmount: 0, averageUnitPrice: null }]

const previous = createEmptyLocalProductSalesAnalysisState()
const committed = applyLocalProductSalesAnalysisBootstrapResult({
  options, candidates, effectiveSelection: carriedSelection, currentProduct, summary, invoiceDetails, productDaily, branches, partial: true, sectionErrors: { summary: '汇总加载失败' },
}, previous)
equal(committed === previous, false, '原子提交必须返回新状态对象')
equal(committed.options.warehouseCategories[0]?.guid, 'cat-1', '原子提交必须替换 options')
equal(committed.candidates?.items[0]?.productCode, 'LP-1', '原子提交必须替换 candidates')
equal(committed.effectiveSelection.includedProductCodes[0], 'LP-1', '原子提交必须替换 effectiveSelection')
equal(committed.currentProduct?.productCode, 'LP-1', '原子提交必须替换 currentProduct')
equal(committed.summary?.totals.netSalesQuantity, -2, '原子提交必须替换 summary')
equal(committed.invoiceDetails?.items[0]?.invoiceNo, 'INV-1', '原子提交必须替换 invoiceDetails')
equal(committed.productDaily[0]?.date, '2026-08-18', '原子提交必须替换 productDaily')
equal(committed.branches[0]?.branchCode, 'S1', '原子提交必须替换 branches')
equal(committed.partial, true, '原子提交必须保留 partial 标记')
equal(committed.sectionErrors.summary, '汇总加载失败', '原子提交必须保留分段错误')
equal(previous.candidates, null, '原子提交不得改动旧状态对象')

// 切换商品只清空下游分段，保留候选与选项
const switched = clearLocalProductSalesAnalysisDetailSections(committed)
equal(switched.candidates?.items[0]?.productCode, 'LP-1', '切换商品必须保留候选')
equal(switched.options.warehouseCategories[0]?.guid, 'cat-1', '切换商品必须保留选项')
equal(switched.summary, null, '切换商品必须清空旧汇总')
equal(switched.invoiceDetails, null, '切换商品必须清空旧明细')
equal(switched.productDaily.length, 0, '切换商品必须清空旧趋势')
equal(switched.branches.length, 0, '切换商品必须清空旧分店排行')
equal(switched.sectionErrors.summary, undefined, '切换商品必须清空分段错误')

// 分段重试只替换目标分段并清除对应错误
const sectionRetry = applyLocalProductSalesAnalysisSectionResult(switched, 'summary', summary)
equal(sectionRetry.summary?.totals.purchaseAmount, 50, '分段重试必须只替换目标分段')
equal(sectionRetry.sectionErrors.summary, undefined, '分段重试成功必须清除对应错误')
equal(sectionRetry.candidates?.items[0]?.productCode, 'LP-1', '分段重试不得影响其它分段')
const sectionError = setLocalProductSalesAnalysisSectionError(sectionRetry, 'branches', '分店加载失败')
equal(sectionError.sectionErrors.branches, '分店加载失败', '分段失败必须写入对应错误')
const clearedError = clearLocalProductSalesAnalysisSectionError(sectionError, 'branches')
equal(clearedError.sectionErrors.branches, undefined, '清除分段错误必须只清除目标键')

console.log('LocalProductSalesAnalysis.logic.test: ok')
