import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import type { WarehouseProductFlowFilter, WarehouseProductFlowPeriods, WarehouseProductFlowSupplierOption } from '../../../types/warehouseProductFlowAnalysis'

export interface WarehouseProductFlowDateRange {
  startDate: string
  endDate: string
}

export interface WarehouseProductFlowSelectOption {
  label: string
  value: string
  searchTerms: string[]
}

function normalizeSearchText(value: string | undefined): string {
  return value?.trim().replace(/\s+/g, ' ').toLocaleLowerCase() ?? ''
}

function matchesSearch(searchTerms: readonly string[], input: string): boolean {
  const query = normalizeSearchText(input)
  return !query || searchTerms.some((term) => normalizeSearchText(term).includes(query))
}

const brisbaneDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane', year: 'numeric', month: '2-digit', day: '2-digit',
})

function formatBrisbaneDate(now: Date): string {
  const parts = brisbaneDateFormatter.formatToParts(now).reduce<Record<string, string>>((result, part) => {
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

function subtractCalendarMonths(date: string, months: number): string {
  const source = new Date(`${date}T00:00:00Z`)
  const targetMonth = source.getUTCMonth() - months
  const targetYear = source.getUTCFullYear() + Math.floor(targetMonth / 12)
  const normalizedMonth = ((targetMonth % 12) + 12) % 12
  const lastDay = new Date(Date.UTC(targetYear, normalizedMonth + 1, 0)).getUTCDate()
  const day = Math.min(source.getUTCDate(), lastDay)
  return new Date(Date.UTC(targetYear, normalizedMonth, day)).toISOString().slice(0, 10)
}

function naturalMonthRange(months: number, now: Date): WarehouseProductFlowDateRange {
  const endDate = shiftDate(formatBrisbaneDate(now), -1)
  // 完整自然月滚动口径：结束日次日向前减 N 个月，首尾均包含。
  const startDate = subtractCalendarMonths(shiftDate(endDate, 1), months)
  return { startDate, endDate }
}

export function buildWarehouseProductFlowDefaultPeriods(now = new Date()): WarehouseProductFlowPeriods {
  return {
    containerPeriod: naturalMonthRange(12, now),
    orderShipmentPeriod: naturalMonthRange(6, now),
    salesPeriod: naturalMonthRange(6, now),
  }
}

export function isValidWarehouseProductFlowRange(range: readonly Date[], now = new Date()): boolean {
  const [start, end] = range
  const yesterday = shiftDate(formatBrisbaneDate(now), -1)
  if (!start || !end) return false
  const startDate = formatBrisbaneDate(start)
  const endDate = formatBrisbaneDate(end)
  if (startDate > endDate || endDate > yesterday) return false
  const dayCount = Math.round((Date.parse(`${endDate}T00:00:00Z`) - Date.parse(`${startDate}T00:00:00Z`)) / 86_400_000) + 1
  return dayCount <= 366
}

export function createWarehouseProductFlowFilter(keyword: string, warehouseCategoryGuids: string[], supplierCodes: string[], documentKeyword: string): WarehouseProductFlowFilter {
  return {
    keyword: keyword.trim() || undefined,
    warehouseCategoryGuids,
    supplierCodes,
    documentKeyword: documentKeyword.trim() || undefined,
  }
}

export function buildWarehouseProductFlowCategoryOptions(nodes: readonly WarehouseCategoryNode[]): WarehouseProductFlowSelectOption[] {
  const visit = (items: readonly WarehouseCategoryNode[], ancestors: readonly WarehouseCategoryNode[], depth: number): WarehouseProductFlowSelectOption[] => items.flatMap((node) => {
    const displayName = node.chineseName || node.categoryName
    const chinesePath = [...ancestors, node].map((item) => item.chineseName || item.categoryName).join(' ')
    const englishPath = [...ancestors, node].map((item) => item.categoryName).join(' ')
    const option: WarehouseProductFlowSelectOption = {
      label: `${'— '.repeat(depth)}${displayName}`,
      value: node.categoryGUID,
      // 搜索保留中英文、完整祖先路径和 GUID，使匹配父分类时子分类也可见。
      searchTerms: [node.categoryGUID, node.chineseName || '', node.categoryName, chinesePath, englishPath],
    }
    return [option, ...visit(node.children || [], [...ancestors, node], depth + 1)]
  })
  return visit(nodes, [], 0)
}

export function filterWarehouseProductFlowCategoryOptions(options: readonly WarehouseProductFlowSelectOption[], input: string): WarehouseProductFlowSelectOption[] {
  return options.filter((option) => matchesSearch(option.searchTerms, input))
}

function supplierMatchRank(supplier: WarehouseProductFlowSupplierOption, input: string): number | null {
  const query = normalizeSearchText(input)
  if (!query) return 0
  const terms = [supplier.code, supplier.name].map(normalizeSearchText).filter(Boolean)
  if (terms.some((term) => term === query)) return 0
  if (terms.some((term) => term.startsWith(query))) return 1
  return terms.some((term) => term.includes(query)) ? 2 : null
}

export function filterWarehouseProductFlowSupplierOptions(suppliers: readonly WarehouseProductFlowSupplierOption[], input: string): WarehouseProductFlowSelectOption[] {
  return suppliers.flatMap((supplier) => {
    const rank = supplierMatchRank(supplier, input)
    return rank === null ? [] : [{ supplier, rank }]
  }).sort((left, right) => left.rank - right.rank || left.supplier.code.localeCompare(right.supplier.code)).map(({ supplier }) => ({
    label: supplier.name ? `${supplier.code} · ${supplier.name}` : supplier.code,
    value: supplier.code,
    searchTerms: [supplier.code, supplier.name || ''],
  }))
}
