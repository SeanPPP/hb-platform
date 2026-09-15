import type { BatchSalesDaily } from '../../../types/batchProductSalesAnalysis'

function assert(condition: unknown, message: string): asserts condition { if (!condition) throw new Error(message) }
function metrics(overrides: Partial<BatchSalesDaily['metrics']>): BatchSalesDaily['metrics'] {
  return { quantity: 0, regularQuantity: 0, discountQuantity: 0, unknownQuantity: 0, returnQuantity: 0, salesAmount: 0, discountStatus: 'complete', originalPriceMin: null, originalPriceMax: null, discountPriceMin: null, discountPriceMax: null, ...overrides }
}

const { buildDiscountDailyChartModel } = await import('./chartModel')
const chart = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, salesAmount: 51.5, regularQuantity: 5, discountQuantity: 3 }) },
  { date: '2026-09-02', metrics: metrics({ quantity: -4, regularQuantity: -1, discountQuantity: -3 }) },
  { date: '2026-09-03', metrics: metrics({ quantity: 0, unknownQuantity: 0 }) },
  { date: '2026-09-04', metrics: { ...metrics({ quantity: 2, discountQuantity: 2 }), regularQuantity: null } as unknown as BatchSalesDaily['metrics'] },
])

assert(chart.points.length === 4, '每个业务日都应有一个数据点')
assert(chart.points[0].salesAmount === 51.5, '图表数据点必须保留每日销售额供悬浮提示显示')
assert(chart.points[0].segments[0].height > 0 && chart.points[0].segments[1].height > 0, '正价和折扣的正销量应分别堆叠')
assert(chart.points[0].segments[1].y < chart.points[0].segments[0].y, '正值后续分段应位于前一个正值之上')
assert(chart.points[1].segments[0].y === chart.zeroY, '负销量第一段应从零线开始')
assert(chart.points[1].segments[1].y > chart.zeroY, '负销量后续分段应在零线下继续堆叠')
assert(chart.minValue < 0 && chart.maxValue > 0, '正负销量同时存在时坐标轴必须覆盖零线两侧')
assert(chart.points[2].segments.every((segment) => segment.height === 0), '零量与缺失分类不应伪造可见柱')
assert(chart.points[3].regularQuantity === 0 && chart.points[3].segments[1].height > 0, '服务意外返回空分类时应按 0 处理且保留其余有效分类')
assert(buildDiscountDailyChartModel([]).points.length === 0, '空数据不应生成虚构日期或柱')

const acrossWeeks = buildDiscountDailyChartModel([
  { date: '2026-09-06', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-07', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-08', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-13', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-14', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
])
assert(acrossWeeks.weekDividers.length === 2, '跨自然周数据应在每个周边界生成分割线')
assert(acrossWeeks.weekDividers.map((divider) => divider.date).join(',') === '2026-09-07,2026-09-14', '完整日期数据应在周一的槽边界分割')
assert(acrossWeeks.weekDividers[0].x === acrossWeeks.plotLeft + (acrossWeeks.plotRight - acrossWeeks.plotLeft) / acrossWeeks.points.length, '周分割线应位于当前柱槽的左边界')

const skippedMonday = buildDiscountDailyChartModel([
  { date: '2026-09-06', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-08', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
])
assert(skippedMonday.weekDividers.length === 1 && skippedMonday.weekDividers[0].date === '2026-09-08', '缺少周一记录时，跨周的下一条记录仍应生成分割线')

const missingSameWeekDay = buildDiscountDailyChartModel([
  { date: '2026-09-08', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-10', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
])
assert(missingSameWeekDay.weekDividers.length === 0, '同一自然周内缺日不得生成分割线')

const mondayFirst = buildDiscountDailyChartModel([
  { date: '2026-09-07', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
  { date: '2026-09-08', metrics: metrics({ quantity: 1, regularQuantity: 1 }) },
])
assert(mondayFirst.weekDividers.length === 0, '首日为周一时不得重复绘制绘图区左边界')

const annual = buildDiscountDailyChartModel(Array.from({ length: 366 }, (_, index) => ({
  date: new Date(Date.UTC(2024, 0, index + 1)).toISOString().slice(0, 10),
  metrics: metrics({ quantity: 1, regularQuantity: 1 }),
})))
assert(annual.points.every((point, index) => index === 0
  || annual.points[index - 1].segments[0].x + annual.points[index - 1].segments[0].width <= point.segments[0].x),
'全年每日柱应保持独立，不能因最小柱宽互相覆盖')
assert(annual.weekDividers.length === 52, '全年数据应为每个非首日周一生成分割线')
assert(annual.weekDividers.every((divider, index) => divider.x > annual.plotLeft && divider.x < annual.plotRight
  && (index === 0 || annual.weekDividers[index - 1].x < divider.x)), '全年周分割线必须位于绘图区内且严格递增')

const pending = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, unknownQuantity: 8, discountStatus: 'pending' }) },
  { date: '2026-09-02', metrics: metrics({ quantity: -2, unknownQuantity: -2, discountStatus: 'pending' }) },
])
assert(pending.points.every((point) => point.pending && point.segments.length === 1 && point.segments[0].kind === 'total'), '待统计只能画总销量，不能伪装正价或未知分类')
assert(pending.points[1].segments[0].y === pending.zeroY && pending.points[1].segments[0].height > 0, '待统计负净销量仍在零线下显示')

const unavailable = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, salesAmount: 40, unknownQuantity: 8, discountStatus: 'unknown' }) },
], 720, 248, true)
assert(unavailable.points[0].pending && !unavailable.points[0].discountPending && unavailable.points[0].segments.length === 1,
  '折扣拆分终态不可用时只画可靠的总销量，且不得伪装成仍在等待')

const partial = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, regularQuantity: 4, discountQuantity: 3, unknownQuantity: 1, discountStatus: 'partial' }) },
])
assert(!partial.points[0].pending && partial.points[0].segments.length === 3,
  '部分分类已有证据时必须保留正价、折扣和未知分段')

const freshUnknown = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, unknownQuantity: 8, discountStatus: 'unknown' }) },
])
assert(!freshUnknown.points[0].pending && freshUnknown.points[0].segments[2].height > 0,
  'Fresh 快照中真实存在的未知价格成交必须保留灰色未知分类柱')

const partialCoverage = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 2, regularQuantity: 2 }) },
  { date: '2026-09-02', metrics: null },
  { date: '2026-09-03', metrics: metrics({ quantity: 0 }) },
])
assert(partialCoverage.points[1].unavailable && partialCoverage.points[1].segments.length === 0, '未完成日期必须保留日期轴断点，不能补零或画柱')
assert(partialCoverage.points[1].hitWidth > 0, '未完成日期即使没有柱形也必须保留可悬停和键盘聚焦的日期槽')
assert(!partialCoverage.points[2].unavailable && partialCoverage.points[2].segments.every((segment) => segment.height === 0), '已完成日期才可以作为真实零值')
