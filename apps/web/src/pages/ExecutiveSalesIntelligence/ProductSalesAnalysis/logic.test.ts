import type { ProductSalesSummaryRow } from '../../../types/productSalesAnalysis'
import type { DailyChartInputPoint, RequestInvalidationRef } from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

function assertOk(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const originalNumberFormat = Intl.NumberFormat
let numberFormatConstructionCount = 0
const trackingNumberFormat = new Proxy(originalNumberFormat, {
  construct(target, args, newTarget) {
    numberFormatConstructionCount += 1
    return Reflect.construct(target, args, newTarget)
  },
})

Object.defineProperty(Intl, 'NumberFormat', {
  configurable: true,
  writable: true,
  value: trackingNumberFormat,
})

const {
  buildDateRange,
  buildDailyChartModel,
  buildXAxisTickIndices,
  clearProductSelection,
  createAllFilteredSelection,
  createIncludedSelection,
  createLatestRequestGuard,
  createProductSalesAnalysisViewState,
  formatAud,
  getDateRangeError,
  getSelectedProductCodes,
  isProductSelected,
  MAX_X_AXIS_TICKS,
  applyCandidateSelect,
  applyCandidateSelectAll,
  invalidateRequests,
  productSalesAnalysisViewReducer,
  resetProductSelection,
  resolveCurrentProductCode,
  toggleExcludedProduct,
} = await import('./logic')

assertEqual(numberFormatConstructionCount, 1, 'AUD 格式化器应在模块加载时只创建一次')
assertEqual(formatAud(1234.5), '$1,234.50', '金额应按澳元格式显示')
assertEqual(formatAud(null), '—', '空金额应显示破折号')
assertEqual(formatAud(undefined), '—', '未定义金额应显示破折号')
assertEqual(formatAud(Number.NaN), '—', '非法数值应显示破折号')
assertEqual(numberFormatConstructionCount, 1, '重复格式化不得重复创建 Intl.NumberFormat')

Object.defineProperty(Intl, 'NumberFormat', {
  configurable: true,
  writable: true,
  value: originalNumberFormat,
})

const fixedNow = new Date('2026-08-18T04:30:00Z')
assertDeepEqual(
  buildDateRange(30, fixedNow),
  { startDate: '2026-07-19', endDate: '2026-08-17' },
  '默认 30 天应截至昨天并向前推 29 天',
)
assertDeepEqual(
  buildDateRange(7, fixedNow),
  { startDate: '2026-08-11', endDate: '2026-08-17' },
  '7 天快捷范围应截至昨天',
)

assertEqual(getDateRangeError('2026-08-01', '2026-08-18', fixedNow), null, '历史范围应合法')
assertEqual(getDateRangeError('2026-08-18', '2026-08-01', fixedNow), '开始日期不能晚于结束日期', '开始日期晚于结束日期应拒绝')
assertOk(getDateRangeError('2026-08-01', '2026-08-19', fixedNow)?.length, '结束日期晚于今天应拒绝')
assertOk(getDateRangeError('2025-08-17', '2026-08-18', fixedNow)?.length, '超过 366 天应拒绝')
assertEqual(getDateRangeError('2025-08-18', '2026-08-18', fixedNow), null, '包含今天在内的 366 天应允许')
assertOk(getDateRangeError('2026-02-31', '2026-03-05', fixedNow)?.length, '不存在的日期应拒绝')

const allFilteredSelection = createAllFilteredSelection(['P2'])
assertDeepEqual(allFilteredSelection, {
  mode: 'allFiltered',
  includedProductCodes: [],
  excludedProductCodes: ['P2'],
}, '默认全选语义应为 allFiltered 并保存排除商品')
assertDeepEqual(
  getSelectedProductCodes(allFilteredSelection, ['P1', 'P2', 'P3']),
  ['P1', 'P3'],
  'allFiltered 跨页候选应排除已取消商品',
)

const recheckedSelection = toggleExcludedProduct(allFilteredSelection, 'P2', true)
assertDeepEqual(
  getSelectedProductCodes(recheckedSelection, ['P1', 'P2', 'P3']),
  ['P1', 'P2', 'P3'],
  '重新勾选排除商品应从 excludedProductCodes 移除',
)

const excludedAgain = toggleExcludedProduct(allFilteredSelection, 'P1', false)
assertDeepEqual(
  getSelectedProductCodes(excludedAgain, ['P1', 'P2', 'P3']),
  ['P3'],
  '跨页继续取消商品应累积到 excludedProductCodes',
)
assertDeepEqual(
  excludedAgain.excludedProductCodes,
  ['P2', 'P1'],
  '排除列表不得原地修改旧 selection',
)

const clearedSelection = clearProductSelection()
assertDeepEqual(clearedSelection, {
  mode: 'included',
  includedProductCodes: [],
  excludedProductCodes: [],
}, '清空选择应切到 included 且不选任何商品')
assertDeepEqual(
  getSelectedProductCodes(clearedSelection, ['P1', 'P2']),
  [],
  '清空后选择器应无已选商品',
)

const includedSelection = createIncludedSelection(['P1', 'P4'])
assertDeepEqual(
  getSelectedProductCodes(includedSelection, ['P1', 'P2', 'P3']),
  ['P1'],
  'included 模式应只返回当前页中真实勾选的商品',
)
assertEqual(isProductSelected(allFilteredSelection, 'P1'), true, 'allFiltered 应保留未排除商品')
assertEqual(isProductSelected(allFilteredSelection, 'P2'), false, 'allFiltered 应识别已排除商品')
assertEqual(isProductSelected(includedSelection, 'P4'), true, 'included 应识别跨页已选商品')
assertEqual(isProductSelected(includedSelection, 'P2'), false, 'included 应拒绝未圈定商品')
assertDeepEqual(
  resetProductSelection(),
  createAllFilteredSelection(),
  '提交新日期或过滤后应重置为 allFiltered',
)

assertEqual(
  resolveCurrentProductCode('P-OLD', [
    { productCode: 'P2' },
    { productCode: 'P3' },
  ] as ProductSalesSummaryRow[]),
  'P2',
  '当前商品已不在汇总时应迁移到第一条已选汇总',
)
assertEqual(
  resolveCurrentProductCode('P3', [
    { productCode: 'P2' },
    { productCode: 'P3' },
  ] as ProductSalesSummaryRow[]),
  'P3',
  '当前商品仍在汇总时应保持',
)
assertEqual(
  resolveCurrentProductCode('P-OLD', [] as ProductSalesSummaryRow[]),
  null,
  '无汇总数据时不应保留失效当前商品',
)

const guard = createLatestRequestGuard()
const firstRequest = guard.begin()
const secondRequest = guard.begin()
assertEqual(guard.isLatest(firstRequest), false, '新请求开始后旧请求必须失效')
assertEqual(guard.isLatest(secondRequest), true, '最新请求应允许写入状态')
guard.invalidate()
assertEqual(guard.isLatest(secondRequest), false, '提交新过滤或刷新后过期请求必须失效')

const invalidationControllers = [new AbortController(), new AbortController()]
const firstInvalidationGuard = createLatestRequestGuard()
const secondInvalidationGuard = createLatestRequestGuard()
const firstInvalidationId = firstInvalidationGuard.begin()
const secondInvalidationId = secondInvalidationGuard.begin()
const invalidationRefs: RequestInvalidationRef[] = [
  { controller: { current: invalidationControllers[0] }, guard: { current: firstInvalidationGuard } },
  { controller: { current: invalidationControllers[1] }, guard: { current: secondInvalidationGuard } },
]

invalidateRequests(invalidationRefs)
assertOk(invalidationControllers[0].signal.aborted, '提交入口应同步 abort 第一个控制器')
assertOk(invalidationControllers[1].signal.aborted, '提交入口应同步 abort 第二个控制器')
assertEqual(invalidationRefs[0].controller.current, undefined, '废弃后应清空第一个控制器引用')
assertEqual(invalidationRefs[1].controller.current, undefined, '废弃后应清空第二个控制器引用')
assertEqual(firstInvalidationGuard.isLatest(firstInvalidationId), false, '提交入口应让第一个 guard 失效')
assertEqual(secondInvalidationGuard.isLatest(secondInvalidationId), false, '提交入口应让第二个 guard 失效')

const chart = buildDailyChartModel(
  [
    { date: '2026-08-01', quantity: 10, averageUnitPrice: 12.5 },
    { date: '2026-08-02', quantity: -4, averageUnitPrice: null },
    { date: '2026-08-03', quantity: 8, averageUnitPrice: 9 },
  ],
  300,
  200,
  { left: 30, right: 10, top: 20, bottom: 30 },
)
assertOk(chart.bars.length === 3, '应生成三个数量柱')
assertOk(Number.isFinite(chart.zeroY), '应计算零线 Y 坐标')
assertOk(chart.bars[0].y < chart.zeroY, '正数量柱应位于零线上方')
assertOk(chart.bars[1].y === chart.zeroY && chart.bars[1].height > 0, '负数量柱应从零线向下延伸')
assertEqual(chart.averagePoints.length, 2, '均价为空的数据点应跳过')
assertEqual(chart.averagePoints[0].averageUnitPrice, 12.5, '均价折线点应保留均价')
assertEqual(chart.averagePoints[1].date, '2026-08-03', '非空均价点应保留日期')
assertOk(
  Math.abs(chart.averagePoints[1].y - 170) < 0.001,
  '均价折线应使用独立价格轴，最低均价应落在绘图区底部',
)

assertDeepEqual(buildXAxisTickIndices(1), [0], '单日应只显示一个日期刻度')
assertDeepEqual(buildXAxisTickIndices(4), [0, 1, 2, 3], '少于 6 天应全显示')
const longTickIndices = buildXAxisTickIndices(366)
assertEqual(longTickIndices.length, MAX_X_AXIS_TICKS, '366 天最多只显示 6 个刻度')
assertEqual(longTickIndices[0], 0, '刻度必须包含首日')
assertEqual(longTickIndices[longTickIndices.length - 1], 365, '刻度必须包含末日')

const longDates: DailyChartInputPoint[] = []
const longCursor = new Date(Date.UTC(2025, 7, 18))
for (let index = 0; index < 366; index += 1) {
  longDates.push({
    date: longCursor.toISOString().slice(0, 10),
    quantity: index % 7,
    averageUnitPrice: index % 3 === 0 ? null : index,
  })
  longCursor.setUTCDate(longCursor.getUTCDate() + 1)
}
const longChart = buildDailyChartModel(
  longDates,
  720,
  260,
  { left: 46, right: 16, top: 20, bottom: 34 },
)
assertEqual(longChart.xAxisTicks.length, MAX_X_AXIS_TICKS, '366 天图表应生成 6 个日期刻度')
assertEqual(longChart.xAxisTicks[0].index, 0, '刻度应含首日')
assertEqual(longChart.xAxisTicks[longChart.xAxisTicks.length - 1].index, 365, '刻度应含末日')
assertEqual(longChart.xAxisTicks[0].date, longDates[0].date, '刻度日期应保留原始日期')

const shortChart = buildDailyChartModel(
  [
    { date: '2026-08-01', quantity: 1, averageUnitPrice: 1 },
    { date: '2026-08-02', quantity: 2, averageUnitPrice: 2 },
    { date: '2026-08-03', quantity: 3, averageUnitPrice: 3 },
  ],
  300,
  200,
  { left: 30, right: 10, top: 20, bottom: 30 },
)
assertDeepEqual(
  shortChart.xAxisTicks.map((tick) => tick.index),
  [0, 1, 2],
  '少于 6 天应全部渲染为刻度',
)

assertDeepEqual(
  applyCandidateSelect(createIncludedSelection(['P1']), 'P2', true),
  createIncludedSelection(['P1', 'P2']),
  'included 模式勾选应追加商品',
)
assertDeepEqual(
  applyCandidateSelect(createIncludedSelection(['P1', 'P2']), 'P1', false),
  createIncludedSelection(['P2']),
  'included 模式取消勾选应移除商品',
)
assertDeepEqual(
  applyCandidateSelectAll(createIncludedSelection(['P1']), ['P2', 'P3'], true),
  createIncludedSelection(['P1', 'P2', 'P3']),
  'included 模式全选应合并变更行',
)

const viewWithDrilldown = {
  ...createProductSalesAnalysisViewState(createIncludedSelection(['P1'])),
  summaryPage: 2,
  currentProductCode: 'P1',
  middleView: 'daily' as const,
  selectedBranchCode: 'S1',
}
const committedSelection = productSalesAnalysisViewReducer(viewWithDrilldown, {
  type: 'commitSelection',
  selection: createIncludedSelection(['P2']),
})
assertDeepEqual(committedSelection, {
  selection: { mode: 'included', includedProductCodes: ['P2'], excludedProductCodes: [] },
  summaryPage: 1,
  currentProductCode: null,
  middleView: 'summary',
  selectedBranchCode: null,
}, 'P1 切到仅 P2 应清空当前商品、分店并回到汇总第 1 页')

const settled = productSalesAnalysisViewReducer(committedSelection, {
  type: 'settleCurrentProduct',
  summaryItems: [
    { productCode: 'P2' },
    { productCode: 'P3' },
  ] as ProductSalesSummaryRow[],
})
assertEqual(settled.currentProductCode, 'P2', '只有新 summary 响应应建立第一个已选商品为当前商品')
assertEqual(settled.summaryPage, 1, '建立当前商品不得改变分页')
assertEqual(settled.middleView, 'summary', '建立当前商品不得擅自切入日视图')

const committedFilter = productSalesAnalysisViewReducer(viewWithDrilldown, {
  type: 'commitFilter',
  selection: resetProductSelection(),
})
assertEqual(committedFilter.currentProductCode, null, '提交新筛选应清空当前商品')
assertEqual(committedFilter.summaryPage, 1, '提交新筛选应回到第 1 页')
assertEqual(committedFilter.middleView, 'summary', '提交新筛选应回到汇总视图')
assertEqual(committedFilter.selectedBranchCode, null, '提交新筛选应清空分店')

console.log('ProductSalesAnalysis.logic.test: ok')
