import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'

export interface RetailPriceChangesFilters {
  startDate: string
  endDate: string
  keyword: string
  onlyWithLocation: boolean
}

export interface RetailPriceChangesQuery extends Record<string, unknown> {
  startDate: string
  endDate: string
  keyword?: string
  onlyWithLocation: boolean
  pageNumber: number
  pageSize: number
}

export interface RetailPriceChangeItem {
  productCode: string
  itemNumber?: string
  barcode?: string
  productImage?: string
  latestRetailPrice: number | null
  lastPriceChangedAtUtc?: string
}

export interface RetailPriceChangesPagedResult {
  items: RetailPriceChangeItem[]
  total: number
  pageNumber: number
  pageSize: number
}

const brisbaneDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

export const RETAIL_PRICE_CHANGES_COLUMN_KEYS = [
  'image',
  'itemNumber',
  'barcode',
  'latestRetailPrice',
  'lastPriceChangedAtUtc',
] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function getBrisbaneDate(now: Date): string {
  const parts = brisbaneDateFormatter.formatToParts(now).reduce<Record<string, string>>((result, part) => {
    if (part.type !== 'literal') result[part.type] = part.value
    return result
  }, {})
  return `${parts.year}-${parts.month}-${parts.day}`
}

function getValue(record: Record<string, unknown>, ...keys: string[]): unknown {
  for (const key of keys) {
    if (record[key] !== undefined) return record[key]
  }
  return undefined
}

function getString(record: Record<string, unknown>, ...keys: string[]): string | undefined {
  const value = getValue(record, ...keys)
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed || undefined
}

function getNumber(record: Record<string, unknown>, ...keys: string[]): number | null {
  const value = getValue(record, ...keys)
  if (value === null || value === undefined || value === '') return null
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function getPositiveInteger(record: Record<string, unknown>, fallback: number, ...keys: string[]): number {
  const value = getNumber(record, ...keys)
  return value !== null && Number.isInteger(value) && value > 0 ? value : fallback
}

function getItems(payload: unknown): unknown[] {
  if (Array.isArray(payload)) return payload
  if (!isRecord(payload)) return []
  const items = getValue(payload, 'items', 'Items', 'records', 'Records', 'list', 'List')
  return Array.isArray(items) ? items : []
}

export function getBrisbaneMonthRange(now = new Date()) {
  const endDate = getBrisbaneDate(now)
  const year = Number(endDate.slice(0, 4))
  const month = Number(endDate.slice(5, 7))
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  return { startDate: `${endDate.slice(0, 7)}-01`, endDate: `${endDate.slice(0, 7)}-${String(lastDay).padStart(2, '0')}` }
}

export function buildRetailPriceChangesQuery(
  filters: RetailPriceChangesFilters,
  pageNumber: number,
  pageSize: number,
): RetailPriceChangesQuery {
  const keyword = filters.keyword.trim()
  return {
    startDate: filters.startDate,
    endDate: filters.endDate,
    ...(keyword ? { keyword } : {}),
    onlyWithLocation: filters.onlyWithLocation,
    pageNumber,
    pageSize,
  }
}

// 后端发布节奏可能不同，优先读取正式契约字段，并兼容旧信封和大小写字段。
export function normalizeRetailPriceChangesResponse(payload: unknown): RetailPriceChangesPagedResult {
  const outer = isRecord(payload) ? payload : {}
  const data = getValue(outer, 'data', 'Data')
  const body = data === undefined || data === null ? payload : data
  const paging = isRecord(body) ? body : outer
  const rawItems = getItems(body)

  return {
    items: rawItems.flatMap((item) => {
      if (!isRecord(item)) return []
      const productCode = getString(item, 'productCode', 'ProductCode')
      if (!productCode) return []
      return [{
        productCode,
        itemNumber: getString(item, 'itemNumber', 'ItemNumber', 'productNo', 'ProductNo'),
        barcode: getString(item, 'barcode', 'Barcode'),
        productImage: getString(item, 'productImage', 'ProductImage', 'imageUrl', 'ImageUrl'),
        latestRetailPrice: getNumber(item, 'latestRetailPrice', 'LatestRetailPrice', 'retailPrice', 'RetailPrice', 'oemPrice', 'OEMPrice'),
        lastPriceChangedAtUtc: getString(item, 'lastPriceChangedAtUtc', 'LastPriceChangedAtUtc', 'lastRetailPriceChangedAt', 'LastRetailPriceChangedAt', 'retailPriceChangedAt', 'RetailPriceChangedAt'),
      }]
    }),
    total: getPositiveInteger(paging, rawItems.length, 'total', 'Total', 'totalCount', 'TotalCount'),
    pageNumber: getPositiveInteger(paging, 1, 'pageNumber', 'PageNumber', 'page', 'Page', 'pageIndex', 'PageIndex'),
    pageSize: getPositiveInteger(paging, rawItems.length || 50, 'pageSize', 'PageSize'),
  }
}

export function getRetailPriceChangesViewState(
  loading: boolean,
  error: unknown,
  itemCount: number,
): 'loading' | 'error' | 'empty' | 'table' {
  if (loading) return 'loading'
  if (error) return 'error'
  return itemCount ? 'table' : 'empty'
}

export function createRetailPriceChangesRequestCoordinator() {
  const latestRequestGuard = createLatestRequestGuard()
  let controller: AbortController | null = null

  return {
    start() {
      // 新筛选或翻页必须终止旧请求，并阻止旧 finally 改写最新 loading 状态。
      controller?.abort()
      const requestId = latestRequestGuard.begin()
      controller = new AbortController()
      return { requestId, signal: controller.signal }
    },
    isLatest(requestId: number) {
      return latestRequestGuard.isLatest(requestId)
    },
    dispose() {
      controller?.abort()
      controller = null
      latestRequestGuard.invalidate()
    },
  }
}
