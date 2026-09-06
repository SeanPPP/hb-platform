import request from '../../../utils/request'
import type { ReportPeriod } from '../ReportWorkbench/logic'
import type { ReportSnapshot } from '../ReportWorkbench/useReportQuery'

export type SupplierKind = 'australia' | 'china'
export type ReportSection = 'suppliers' | 'branches' | 'products' | 'summary'
export interface SalesDetailRow {
  code: string
  name: string
  itemNumber?: string
  productImage?: string
  revenue: number
  compareRevenue: number | null
  quantity: number
  compareQuantity: number | null
  orderCount: number | null
  compareOrderCount: number | null
  averageTransaction: number | null
  compareAverageTransaction: number | null
  averageUnitPrice: number | null
  compareAverageUnitPrice: number | null
  grossProfit: number | null
  compareGrossProfit: number | null
  grossMarginRate: number | null
  compareGrossMarginRate: number | null
  share: number | null
  compareShare: number | null
  chinaShare: number | null
  compareChinaShare: number | null
}
export interface SalesDetailPage { rows: SalesDetailRow[]; total: number; summary?: SalesDetailRow }
export interface SalesDetailQuery extends ReportPeriod {
  kind: SupplierKind
  branchCodes?: string[]
  selectedBranchCode?: string
  selectedSupplierCode?: string
  selectedProductCode?: string
  search?: string
  pageIndex: number
  pageSize: number
}

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
}
function field(raw: Record<string, unknown>, key: string) { return raw[key] ?? raw[key[0].toUpperCase() + key.slice(1)] }
function nullable(value: unknown): number | null {
  if (typeof value !== 'number' && typeof value !== 'string') return null
  if (typeof value === 'string' && !value.trim()) return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}
export function normalizeSalesDetailRow(value: unknown): SalesDetailRow {
  const raw = record(value)
  const number = (key: string) => nullable(field(raw, key))
  const string = (key: string) => String(field(raw, key) ?? '')
  return { code: string('code'), name: string('name'), itemNumber: string('itemNumber'), productImage: string('productImage'),
    revenue: number('revenue') ?? 0, compareRevenue: number('compareRevenue'), quantity: number('quantity') ?? 0,
    compareQuantity: number('compareQuantity'), orderCount: number('orderCount'), compareOrderCount: number('compareOrderCount'),
    averageTransaction: number('averageTransaction'), compareAverageTransaction: number('compareAverageTransaction'),
    averageUnitPrice: number('averageUnitPrice'), compareAverageUnitPrice: number('compareAverageUnitPrice'),
    grossProfit: number('grossProfit'), compareGrossProfit: number('compareGrossProfit'),
    grossMarginRate: number('grossMarginRate'), compareGrossMarginRate: number('compareGrossMarginRate'),
    share: number('share'), compareShare: number('compareShare'), chinaShare: number('chinaShare'), compareChinaShare: number('compareChinaShare') }
}

/** 将每块候选的查询显式隔离，避免自身筛选条件收窄候选或分页触发全部面板重查。 */
export function sectionQuery(section: ReportSection, query: SalesDetailQuery) {
  const params: Record<string, unknown> = { ...query, section }
  if (section === 'suppliers') delete params.selectedSupplierCode
  if (section === 'branches') delete params.selectedBranchCode
  if (section === 'products') delete params.selectedProductCode
  if (section !== 'products' && section !== 'summary') delete params.search
  if (section !== 'products') { delete params.pageIndex; delete params.pageSize }
  return params
}

export async function fetchSalesDetailSection(section: ReportSection, query: SalesDetailQuery, signal: AbortSignal): Promise<ReportSnapshot<SalesDetailPage>> {
  const raw = record(await request<unknown>('/api/react/v1/dashboard/sales-detail-columns', {
    signal, params: sectionQuery(section, query),
  }))
  if (field(raw, 'success') === false) throw new Error(String(field(raw, 'message') || '报表加载失败'))
  const data = record(field(raw, 'data'))
  const rows = field(data, 'rows')
  const summary = field(data, 'summary')
  return { data: { rows: Array.isArray(rows) ? rows.map(normalizeSalesDetailRow) : [],
    total: nullable(field(data, 'total')) ?? 0, summary: summary ? normalizeSalesDetailRow(summary) : undefined },
    statisticStatus: String(field(raw, 'statisticStatus') ?? 'Pending'),
    statisticMessage: field(raw, 'statisticMessage') as string | undefined,
    statisticUpdatedAt: field(raw, 'statisticUpdatedAt') as string | undefined,
    cacheVersion: field(raw, 'cacheVersion') as string | undefined }
}
