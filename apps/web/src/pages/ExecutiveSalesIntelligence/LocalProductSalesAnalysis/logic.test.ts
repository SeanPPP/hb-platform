import {
  applyCandidateSelection,
  buildBrisbaneDefaultRange,
  canSetCurrentProduct,
  createForceRefreshConsumer,
  createLatestRequestGuard,
  createIncludedSelection,
  getDateRangeError,
  getCurrentProductAfterCancellation,
  isSelected,
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

const candidateGuard = createLatestRequestGuard()
const summaryGuard = createLatestRequestGuard()
const candidateToken = candidateGuard.next()
const staleCandidateToken = candidateGuard.next()
equal(candidateGuard.isCurrent(candidateToken), false, '候选筛选竞态必须丢弃旧响应')
equal(candidateGuard.isCurrent(staleCandidateToken), true, '候选最新响应必须可提交')
equal(summaryGuard.isCurrent(summaryGuard.next()), true, '汇总 guard 必须与候选 guard 独立')

const refresh = createForceRefreshConsumer()
refresh.request(['candidates', 'summary'])
equal(refresh.consume('candidates'), true, '刷新候选请求只能消费一次 forceRefresh')
equal(refresh.consume('candidates'), false, '同一刷新轮不得重复消费候选 forceRefresh')
equal(refresh.consume('summary'), true, '不同请求 key 各自消费一次')
equal(refresh.consume('detail'), false, '未申请的请求不得绕过缓存')

console.log('LocalProductSalesAnalysis.logic.test: ok')
