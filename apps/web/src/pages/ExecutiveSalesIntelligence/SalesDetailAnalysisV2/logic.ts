import { completeSum, margin, normalizeKeyword, quickDateSelection, validPeriod, type DateSelection } from '../ReportWorkbench/logic'
import type { SalesDetailRow, SupplierKind } from './reportService'

export interface DetailSelection { supplier?: string; branch?: string; product?: string; keyword: string; page: number; pageSize: number }
export const emptySelection: DetailSelection = { keyword: '', page: 1, pageSize: 20 }

export function initialDetailState(search: string): { dates: DateSelection; kind: SupplierKind; selection: DetailSelection } {
  const params = new URLSearchParams(search)
  let dates = quickDateSelection('today')
  const start = params.get('startDate'), end = params.get('endDate')
  if (start && end && validPeriod(start, end)) dates = { ...dates, startDate: start, endDate: end, quick: 'custom' }
  if (['false', '0'].includes(params.get('compare') ?? '')) dates.compare = false
  if (params.get('compareMode') === 'ByDate') dates.compareMode = 'ByDate'
  return { dates, kind: params.get('kind') === 'china' ? 'china' : 'australia',
    selection: { ...emptySelection, branch: params.get('branch')?.trim() || undefined } }
}

export function selectDimension(state: DetailSelection, dimension: 'supplier' | 'branch' | 'product', code: string): DetailSelection {
  return { ...state, [dimension]: state[dimension] === code ? undefined : code, page: dimension === 'product' ? state.page : 1 }
}

export function applyKeyword(state: DetailSelection, keyword: string): DetailSelection {
  const normalized = normalizeKeyword(keyword)
  return normalized === state.keyword ? state : { ...state, keyword: normalized, product: undefined, page: 1 }
}

export function sumProductPage(rows: SalesDetailRow[]) {
  const revenue = rows.reduce((sum, row) => sum + row.revenue, 0)
  const compareRevenue = completeSum(rows.map(row => row.compareRevenue))
  const grossProfit = completeSum(rows.map(row => row.grossProfit))
  const compareGrossProfit = completeSum(rows.map(row => row.compareGrossProfit))
  return { revenue, compareRevenue, grossProfit, compareGrossProfit, grossMarginRate: margin(grossProfit, revenue),
    compareGrossMarginRate: compareRevenue == null ? null : margin(compareGrossProfit, compareRevenue) }
}

export function resizeColumns(widths: number[], divider: number, delta: number): number[] {
  const result = [...widths]
  const adjusted = Math.max(18 - result[divider], Math.min(result[divider + 1] - 18, delta))
  result[divider] += adjusted
  result[divider + 1] -= adjusted
  return result
}
