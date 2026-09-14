import type { BatchSalesDaily } from '../../../types/batchProductSalesAnalysis'

export type DiscountChartKind = 'regular' | 'discount' | 'unknown' | 'total'

export interface DiscountChartSegment {
  kind: DiscountChartKind
  value: number
  x: number
  y: number
  width: number
  height: number
}

export interface DiscountChartPoint {
  pending: boolean
  date: string
  quantity: number
  regularQuantity: number
  discountQuantity: number
  unknownQuantity: number
  originalPriceMin: number | null
  originalPriceMax: number | null
  discountPriceMin: number | null
  discountPriceMax: number | null
  x: number
  segments: DiscountChartSegment[]
}

export interface DiscountChartModel {
  width: number
  height: number
  zeroY: number
  plotLeft: number
  plotRight: number
  plotTop: number
  plotBottom: number
  minValue: number
  maxValue: number
  points: DiscountChartPoint[]
  ticks: Array<{ value: number; y: number }>
  xTicks: Array<{ date: string; x: number }>
}

const SERIES: Array<{ kind: DiscountChartKind; key: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity' }> = [
  { kind: 'regular', key: 'regularQuantity' },
  { kind: 'discount', key: 'discountQuantity' },
  { kind: 'unknown', key: 'unknownQuantity' },
]

function finite(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

function tickValues(min: number, max: number) {
  const span = Math.max(1, max - min)
  const rawStep = span / 4
  const power = 10 ** Math.floor(Math.log10(rawStep))
  const normalized = rawStep / power
  const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * power
  const start = Math.floor(min / step) * step
  const end = Math.ceil(max / step) * step
  const values: number[] = []
  for (let value = start; value <= end + step / 10; value += step) values.push(Number(value.toFixed(8)))
  return values
}

function xTickIndices(length: number) {
  if (length <= 6) return Array.from({ length }, (_, index) => index)
  const count = Math.min(7, length)
  return Array.from({ length: count }, (_, index) => Math.round((index * (length - 1)) / (count - 1)))
}

/** 生成有符号堆叠柱。各分类各自在零线两侧累计，因此退货不会被正销量抵消。 */
export function buildDiscountDailyChartModel(data: BatchSalesDaily[], width = 720, height = 248): DiscountChartModel {
  const plotLeft = 42
  const plotRight = width - 12
  const plotTop = 18
  const plotBottom = height - 32
  const normalized = data.map((item) => {
    const pending = item.metrics.discountStatus === 'pending'
    const regularQuantity = finite(item.metrics.regularQuantity)
    const discountQuantity = finite(item.metrics.discountQuantity)
    const unknownQuantity = finite(item.metrics.unknownQuantity)
    return {
      pending,
      date: item.date,
      quantity: finite(item.metrics.quantity),
      regularQuantity,
      discountQuantity,
      unknownQuantity,
      originalPriceMin: item.metrics.originalPriceMin,
      originalPriceMax: item.metrics.originalPriceMax,
      discountPriceMin: item.metrics.discountPriceMin,
      discountPriceMax: item.metrics.discountPriceMax,
      positive: (pending ? [finite(item.metrics.quantity)] : [regularQuantity, discountQuantity, unknownQuantity]).filter((value) => value > 0).reduce((sum, value) => sum + value, 0),
      negative: (pending ? [finite(item.metrics.quantity)] : [regularQuantity, discountQuantity, unknownQuantity]).filter((value) => value < 0).reduce((sum, value) => sum + value, 0),
    }
  })
  const minValue = Math.min(0, ...normalized.map((point) => point.negative))
  const maxValue = Math.max(0, ...normalized.map((point) => point.positive), 1)
  const values = tickValues(minValue, maxValue)
  const axisMin = values[0] ?? minValue
  const axisMax = values[values.length - 1] ?? maxValue
  const scale = (plotBottom - plotTop) / Math.max(1, axisMax - axisMin)
  const y = (value: number) => plotBottom - (value - axisMin) * scale
  const zeroY = y(0)
  const slotWidth = (plotRight - plotLeft) / Math.max(1, normalized.length)
  const barWidth = Math.max(0.5, Math.min(28, slotWidth * 0.62))

  const points = normalized.map((point, index) => {
    const x = plotLeft + slotWidth * index + (slotWidth - barWidth) / 2
    let positiveBase = 0
    let negativeBase = 0
    const series: Array<{ kind: DiscountChartKind; key: 'quantity' | 'regularQuantity' | 'discountQuantity' | 'unknownQuantity' }> = point.pending ? [{ kind: 'total', key: 'quantity' }] : SERIES
    const segments = series.map(({ kind, key }) => {
      const value = point[key]
      if (!value) return { kind, value, x, y: zeroY, width: barWidth, height: 0 }
      if (value > 0) {
        const next = positiveBase + value
        const segment = { kind, value, x, y: y(next), width: barWidth, height: Math.abs(y(next) - y(positiveBase)) }
        positiveBase = next
        return segment
      }
      const next = negativeBase + value
      const segment = { kind, value, x, y: y(negativeBase), width: barWidth, height: Math.abs(y(next) - y(negativeBase)) }
      negativeBase = next
      return segment
    })
    return { ...point, x: x + barWidth / 2, segments }
  })

  return {
    width, height, zeroY, plotLeft, plotRight, plotTop, plotBottom,
    minValue: axisMin,
    maxValue: axisMax,
    points,
    ticks: values.map((value) => ({ value, y: y(value) })),
    xTicks: xTickIndices(points.length).map((index) => ({ date: points[index].date, x: points[index].x })),
  }
}
