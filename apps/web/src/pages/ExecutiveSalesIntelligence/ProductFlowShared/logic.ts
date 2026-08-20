import type { WarehouseProductFlowDaily, WarehouseProductFlowSelection } from '../../../types/warehouseProductFlowAnalysis'

export interface WarehouseProductFlowDateRange {
  startDate: string
  endDate: string
}

export interface FlowChartBar {
  x: number
  y: number
  width: number
  height: number
  value: number
}

export interface FlowTrendChartModel {
  zeroY: number
  xAxisTicks: Array<{ index: number; date: string; x: number }>
  series: {
    inbound: FlowChartBar[]
    shipped: FlowChartBar[]
    netSales: FlowChartBar[]
    averageUnitPrice: Array<{ x: number; y: number; value: number; date: string }>
  }
}

const brisbaneDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function formatBrisbaneDate(date: Date): string {
  const parts = brisbaneDateFormatter.formatToParts(date).reduce<Record<string, string>>((result, part) => {
    if (part.type !== 'literal') result[part.type] = part.value
    return result
  }, {})
  return `${parts.year}-${parts.month}-${parts.day}`
}

function shiftDate(date: string, days: number): string {
  const result = new Date(`${date}T00:00:00Z`)
  result.setUTCDate(result.getUTCDate() + days)
  return result.toISOString().slice(0, 10)
}

function uniqueCodes(codes: readonly string[]): string[] {
  return [...new Set(codes.map((code) => code.trim()).filter(Boolean))]
}

export function buildFlowDateRange(days: number, now = new Date()): WarehouseProductFlowDateRange {
  const endDate = shiftDate(formatBrisbaneDate(now), -1)
  return { startDate: shiftDate(endDate, -(days - 1)), endDate }
}

export function createAllFilteredSelection(excludedProductCodes: readonly string[] = []): WarehouseProductFlowSelection {
  return { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: uniqueCodes(excludedProductCodes) }
}

export function createIncludedSelection(includedProductCodes: readonly string[] = []): WarehouseProductFlowSelection {
  return { mode: 'included', includedProductCodes: uniqueCodes(includedProductCodes), excludedProductCodes: [] }
}

export function isProductSelected(selection: WarehouseProductFlowSelection, productCode: string): boolean {
  return selection.mode === 'included'
    ? selection.includedProductCodes.includes(productCode)
    : !selection.excludedProductCodes.includes(productCode)
}

export function toggleProductSelection(
  selection: WarehouseProductFlowSelection,
  productCode: string,
  selected: boolean,
): WarehouseProductFlowSelection {
  if (selection.mode === 'allFiltered') {
    const excluded = new Set(selection.excludedProductCodes)
    if (selected) excluded.delete(productCode)
    else excluded.add(productCode)
    return createAllFilteredSelection([...excluded])
  }

  const included = new Set(selection.includedProductCodes)
  if (selected) included.add(productCode)
  else included.delete(productCode)
  return createIncludedSelection([...included])
}

export function selectFirstCandidate(
  selection: WarehouseProductFlowSelection,
  candidates: readonly { productCode: string }[],
): WarehouseProductFlowSelection {
  if (selection.mode === 'included' && selection.includedProductCodes.length === 0 && candidates[0]?.productCode) {
    return createIncludedSelection([candidates[0].productCode])
  }
  return selection
}

export function resolveCurrentProductCode(
  currentProductCode: string | null,
  selectedProductCodes: readonly string[],
): string | null {
  return currentProductCode && selectedProductCodes.includes(currentProductCode)
    ? currentProductCode
    : selectedProductCodes[0] ?? null
}

function buildTickIndices(length: number): number[] {
  if (length <= 6) return Array.from({ length }, (_, index) => index)
  return Array.from({ length: 6 }, (_, index) => Math.round(index * (length - 1) / 5))
}

export function buildFlowTrendChartModel(
  data: readonly WarehouseProductFlowDaily[],
  width: number,
  height: number,
): FlowTrendChartModel {
  const plot = { left: 42, right: 16, top: 18, bottom: 34 }
  const plotWidth = width - plot.left - plot.right
  const plotHeight = height - plot.top - plot.bottom
  const values = data.flatMap((item) => [item.inboundQuantity, item.shippedQuantity, item.netSalesQuantity])
  const minValue = Math.min(0, ...values)
  const maxValue = Math.max(0, ...values, 1)
  const range = maxValue - minValue || 1
  const valueToY = (value: number) => plot.top + ((maxValue - value) / range) * plotHeight
  const zeroY = valueToY(0)
  const groupWidth = data.length ? plotWidth / data.length : plotWidth
  const barWidth = Math.max(2, Math.min(10, (groupWidth - 6) / 3))
  const toBar = (value: number, index: number, offset: number): FlowChartBar => {
    const baseline = valueToY(0)
    const valueY = valueToY(value)
    return {
      x: plot.left + index * groupWidth + (groupWidth - barWidth * 3 - 4) / 2 + offset * (barWidth + 2),
      y: Math.min(valueY, baseline),
      width: barWidth,
      height: Math.max(0, Math.abs(valueY - baseline)),
      value,
    }
  }
  const prices = data.map((item) => item.averageUnitPrice).filter((value): value is number => value !== null)
  const priceMin = Math.min(...prices, 0)
  const priceMax = Math.max(...prices, 1)
  const priceRange = priceMax - priceMin || 1

  return {
    zeroY,
    xAxisTicks: buildTickIndices(data.length).map((index) => ({
      index,
      date: data[index].date,
      x: plot.left + index * groupWidth + groupWidth / 2,
    })),
    series: {
      inbound: data.map((item, index) => toBar(item.inboundQuantity, index, 0)),
      shipped: data.map((item, index) => toBar(item.shippedQuantity, index, 1)),
      netSales: data.map((item, index) => toBar(item.netSalesQuantity, index, 2)),
      averageUnitPrice: data.flatMap((item, index) => item.averageUnitPrice === null ? [] : [{
        x: plot.left + index * groupWidth + groupWidth / 2,
        y: plot.top + ((priceMax - item.averageUnitPrice) / priceRange) * plotHeight,
        value: item.averageUnitPrice,
        date: item.date,
      }]),
    },
  }
}
