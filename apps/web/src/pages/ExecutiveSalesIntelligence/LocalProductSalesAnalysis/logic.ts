import type {
  LocalSupplierProductSalesAnalysisCandidate,
  LocalSupplierProductSalesAnalysisSelection,
} from '../../../types/localSupplierProductSalesAnalysis'

export interface LocalProductSalesAnalysisDateRange { startDate: string; endDate: string }

function brisbaneDateParts(now: Date) {
  const parts = new Intl.DateTimeFormat('en-AU', {
    timeZone: 'Australia/Brisbane', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now)
  const value = (type: string) => parts.find((part) => part.type === type)?.value ?? ''
  return { year: value('year'), month: value('month'), day: value('day') }
}

function formatUtcDate(date: Date) {
  return date.toISOString().slice(0, 10)
}

// 先按 Brisbane 取自然日，再回推，避免运行环境时区影响默认“昨天”。
export function buildBrisbaneDefaultRange(days: number, now = new Date()): LocalProductSalesAnalysisDateRange {
  const { year, month, day } = brisbaneDateParts(now)
  const end = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day) - 1))
  const start = new Date(end)
  start.setUTCDate(start.getUTCDate() - Math.max(1, days) + 1)
  return { startDate: formatUtcDate(start), endDate: formatUtcDate(end) }
}

function toUtcDay(value: string) {
  const date = new Date(`${value}T00:00:00.000Z`)
  return Number.isNaN(date.getTime()) ? null : date
}

export function getDateRangeError(startDate: string, endDate: string, brisbaneYesterday: string): string | undefined {
  const start = toUtcDay(startDate); const end = toUtcDay(endDate); const yesterday = toUtcDay(brisbaneYesterday)
  if (!start || !end || !yesterday || start > end) return '参数错误：开始日期不能晚于结束日期'
  if (end > yesterday) return '参数错误：日期范围截至 Brisbane 昨天'
  if ((end.getTime() - start.getTime()) / 86_400_000 > 365) return '参数错误：日期范围不能超过 366 天'
  return undefined
}

export function createIncludedSelection(productCodes: string[] = []): LocalSupplierProductSalesAnalysisSelection {
  return { mode: 'included', includedProductCodes: [...new Set(productCodes)], excludedProductCodes: [] }
}

export function isSelected(selection: LocalSupplierProductSalesAnalysisSelection, productCode: string) {
  return selection.mode === 'allFiltered'
    ? !selection.excludedProductCodes.includes(productCode)
    : selection.includedProductCodes.includes(productCode)
}

export function canSetCurrentProduct(selection: LocalSupplierProductSalesAnalysisSelection, productCode: string) {
  return isSelected(selection, productCode)
}

export function createForceRefreshConsumer() {
  const pending = new Set<string>()
  return {
    request: (keys: string[]) => { pending.clear(); keys.forEach((key) => pending.add(key)) },
    consume: (key: string) => {
      if (!pending.has(key)) return false
      pending.delete(key)
      return true
    },
  }
}

export function applyCandidateSelection(
  selection: LocalSupplierProductSalesAnalysisSelection,
  productCode: string,
  checked: boolean,
): LocalSupplierProductSalesAnalysisSelection {
  if (selection.mode === 'allFiltered') {
    const excluded = new Set(selection.excludedProductCodes)
    if (checked) excluded.delete(productCode)
    else excluded.add(productCode)
    return { ...selection, excludedProductCodes: [...excluded] }
  }
  const included = new Set(selection.includedProductCodes)
  if (checked) included.add(productCode)
  else included.delete(productCode)
  return { ...selection, includedProductCodes: [...included] }
}

export function getCurrentProductAfterCancellation<T extends Pick<LocalSupplierProductSalesAnalysisCandidate, 'productCode'>>(
  currentProduct: T | null,
  summaryItems: T[],
  currentWasCancelled: boolean,
): T | null {
  if (!currentWasCancelled) return currentProduct
  return summaryItems[0] ?? null
}

export function createLatestRequestGuard() {
  let version = 0
  return {
    next: () => ++version,
    isCurrent: (token: number) => token === version,
    invalidate: () => { version += 1 },
  }
}

export function formatAud(value: number | null | undefined) {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(value)
}

export function toFlowTrendData(data: Array<{ date: string; purchaseQuantity?: number; netSalesQuantity: number; netSalesAmount: number; averageUnitPrice: number | null }>) {
  return data.map((item) => ({
    date: item.date,
    inboundQuantity: item.purchaseQuantity ?? 0,
    shippedQuantity: 0,
    netSalesQuantity: item.netSalesQuantity,
    netSalesAmount: item.netSalesAmount,
    averageUnitPrice: item.averageUnitPrice,
  }))
}
