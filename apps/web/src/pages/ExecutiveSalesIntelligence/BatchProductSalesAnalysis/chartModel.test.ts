import type { BatchSalesDaily } from '../../../types/batchProductSalesAnalysis'

function assert(condition: unknown, message: string): asserts condition { if (!condition) throw new Error(message) }
function metrics(overrides: Partial<BatchSalesDaily['metrics']>): BatchSalesDaily['metrics'] {
  return { quantity: 0, regularQuantity: 0, discountQuantity: 0, unknownQuantity: 0, returnQuantity: 0, salesAmount: 0, discountStatus: 'complete', originalPriceMin: null, originalPriceMax: null, discountPriceMin: null, discountPriceMax: null, ...overrides }
}

const { buildDiscountDailyChartModel } = await import('./chartModel')
const chart = buildDiscountDailyChartModel([
  { date: '2026-09-01', metrics: metrics({ quantity: 8, regularQuantity: 5, discountQuantity: 3 }) },
  { date: '2026-09-02', metrics: metrics({ quantity: -4, regularQuantity: -1, discountQuantity: -3 }) },
  { date: '2026-09-03', metrics: metrics({ quantity: 0, unknownQuantity: 0 }) },
  { date: '2026-09-04', metrics: { ...metrics({ quantity: 2, discountQuantity: 2 }), regularQuantity: null } as unknown as BatchSalesDaily['metrics'] },
])

assert(chart.points.length === 4, '每个业务日都应有一个数据点')
assert(chart.points[0].segments[0].height > 0 && chart.points[0].segments[1].height > 0, '正价和折扣的正销量应分别堆叠')
assert(chart.points[0].segments[1].y < chart.points[0].segments[0].y, '正值后续分段应位于前一个正值之上')
assert(chart.points[1].segments[0].y === chart.zeroY, '负销量第一段应从零线开始')
assert(chart.points[1].segments[1].y > chart.zeroY, '负销量后续分段应在零线下继续堆叠')
assert(chart.minValue < 0 && chart.maxValue > 0, '正负销量同时存在时坐标轴必须覆盖零线两侧')
assert(chart.points[2].segments.every((segment) => segment.height === 0), '零量与缺失分类不应伪造可见柱')
assert(chart.points[3].regularQuantity === 0 && chart.points[3].segments[1].height > 0, '服务意外返回空分类时应按 0 处理且保留其余有效分类')
assert(buildDiscountDailyChartModel([]).points.length === 0, '空数据不应生成虚构日期或柱')

const annual = buildDiscountDailyChartModel(Array.from({ length: 366 }, (_, index) => ({
  date: new Date(Date.UTC(2024, 0, index + 1)).toISOString().slice(0, 10),
  metrics: metrics({ quantity: 1, regularQuantity: 1 }),
})))
assert(annual.points.every((point, index) => index === 0
  || annual.points[index - 1].segments[0].x + annual.points[index - 1].segments[0].width <= point.segments[0].x),
'全年每日柱应保持独立，不能因最小柱宽互相覆盖')
