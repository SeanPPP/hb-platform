import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

function assertOk(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const {
  buildFlowDateRange,
  buildFlowTrendChartModel,
  createAllFilteredSelection,
  createIncludedSelection,
  isProductSelected,
  resolveCurrentProductCode,
  selectFirstCandidate,
  toggleProductSelection,
} = await import('./logic')

const fixedNow = new Date('2026-08-18T04:30:00Z')
assertDeepEqual(
  buildFlowDateRange(30, fixedNow),
  { startDate: '2026-07-19', endDate: '2026-08-17' },
  '默认 30 天必须截至 Brisbane 昨天',
)

const allFiltered = createAllFilteredSelection(['P2'])
assertEqual(isProductSelected(allFiltered, 'P1'), true, 'allFiltered 应选中未排除的跨页商品')
assertEqual(isProductSelected(allFiltered, 'P2'), false, 'allFiltered 应排除取消选择的商品')
assertDeepEqual(
  toggleProductSelection(allFiltered, 'P3', false),
  createAllFilteredSelection(['P2', 'P3']),
  'allFiltered 取消商品应累积到 excludedProductCodes',
)
assertDeepEqual(
  toggleProductSelection(createIncludedSelection(['P1']), 'P2', true),
  createIncludedSelection(['P1', 'P2']),
  'included 模式应保留跨分页已选商品',
)
assertDeepEqual(
  selectFirstCandidate(createIncludedSelection(), [{ productCode: 'P9' }, { productCode: 'P8' }]),
  createIncludedSelection(['P9']),
  '首次候选响应必须显式选中排序第一项',
)
assertDeepEqual(
  selectFirstCandidate(createIncludedSelection(['P7']), [{ productCode: 'P9' }]),
  createIncludedSelection(['P7']),
  '已有选择时不得用后续候选响应覆盖选择',
)
assertEqual(
  resolveCurrentProductCode('P2', ['P1', 'P3']),
  'P1',
  '取消当前商品后应迁移到已选汇总第一项',
)

const guard = createLatestRequestGuard()
const oldRequest = guard.begin()
const currentRequest = guard.begin()
assertEqual(guard.isLatest(oldRequest), false, '较晚请求开始后旧响应必须丢弃')
assertEqual(guard.isLatest(currentRequest), true, '最新请求才可写入局部状态')

const chart = buildFlowTrendChartModel([
  { date: '2026-08-01', inboundQuantity: 12, shippedQuantity: 9, netSalesQuantity: 6, netSalesAmount: 60, averageUnitPrice: 10 },
  { date: '2026-08-02', inboundQuantity: 5, shippedQuantity: 7, netSalesQuantity: -2, netSalesAmount: -16, averageUnitPrice: 8 },
], 720, 260)
assertEqual(chart.xAxisTicks.length, 2, '短日期范围应保留每个日期刻度')
assertOk(chart.zeroY < 226, '存在负值时零线不能贴在绘图区底部')
assertOk(chart.series.netSales[1].y === chart.zeroY && chart.series.netSales[1].height > 0, '负净销量必须绘制在零线以下')

const longChart = buildFlowTrendChartModel(
  Array.from({ length: 30 }, (_, index) => ({
    date: `2026-07-${String(index + 1).padStart(2, '0')}`,
    inboundQuantity: index,
    shippedQuantity: index,
    netSalesQuantity: index,
    netSalesAmount: index * (index + 1),
    averageUnitPrice: index + 1,
  })),
  720,
  260,
)
assertEqual(longChart.xAxisTicks.length, 6, '趋势图最多应有六个均匀日期刻度')

console.log('warehouseProductFlow.logic.test: ok')
