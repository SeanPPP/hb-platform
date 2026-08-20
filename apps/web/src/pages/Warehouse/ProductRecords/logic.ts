import type {
  ProductSalesAnalysisScope,
  ProductSalesAnalysisSelection,
} from '../../../types/productSalesAnalysis'
import type {
  WarehouseProductAllocationBranch,
  WarehouseProductAllocationSummary,
  WarehouseProductRecordSortDirection,
} from '../../../types/warehouseProductRecords'

export interface ProductRecordsDateRange {
  startDate: string
  endDate: string
}

const dateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})$/

const brisbaneDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function formatBrisbaneDate(date: Date): string {
  const parts = brisbaneDateFormatter.formatToParts(date).reduce<Record<string, string>>((result, part) => {
    if (part.type !== 'literal') {
      result[part.type] = part.value
    }
    return result
  }, {})
  return `${parts.year}-${parts.month}-${parts.day}`
}

function parseDateOnly(date: string): Date | null {
  const match = dateOnlyPattern.exec(date.trim())
  if (!match) {
    return null
  }

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const parsed = new Date(Date.UTC(year, month - 1, day))
  if (
    parsed.getUTCFullYear() !== year
    || parsed.getUTCMonth() !== month - 1
    || parsed.getUTCDate() !== day
  ) {
    return null
  }
  return parsed
}

function formatDateOnly(date: Date): string {
  return [
    String(date.getUTCFullYear()).padStart(4, '0'),
    String(date.getUTCMonth() + 1).padStart(2, '0'),
    String(date.getUTCDate()).padStart(2, '0'),
  ].join('-')
}

function shiftDateOnly(date: string, days: number): string {
  const parsed = parseDateOnly(date)
  if (!parsed) {
    return date
  }
  return formatDateOnly(new Date(parsed.getTime() + days * 24 * 60 * 60 * 1000))
}

export function buildBrisbaneDateRange(days: number, now = new Date()): ProductRecordsDateRange {
  const endDate = formatBrisbaneDate(now)
  return {
    startDate: shiftDateOnly(endDate, -(days - 1)),
    endDate,
  }
}

export function getDateRangeError(
  startDate: string,
  endDate: string,
  now = new Date(),
): string | null {
  const start = parseDateOnly(startDate)
  const end = parseDateOnly(endDate)
  const today = parseDateOnly(formatBrisbaneDate(now))

  if (!start || !end || !today) {
    return '日期格式无效'
  }
  if (start.getTime() > end.getTime()) {
    return '开始日期不能晚于结束日期'
  }
  if (end.getTime() > today.getTime()) {
    return '结束日期不能晚于今天'
  }
  const dayDiff = Math.round((end.getTime() - start.getTime()) / (24 * 60 * 60 * 1000))
  if (dayDiff > 365) {
    return '日期范围不能超过 366 天'
  }
  return null
}

// 货柜状态：0草稿 1已确认 2已装柜 3运输中 4已到港 5已清关 6已完成 7已取消
export const CONTAINER_STATUS_VALUES = [0, 1, 2, 3, 4, 5, 6, 7] as const

export function getDefaultContainerStatuses(): number[] {
  // 空数组不显式限定状态，由后端应用“全部非取消（含未知状态）”默认规则。
  return []
}

export function buildSalesSelection(productCode: string): ProductSalesAnalysisSelection {
  return {
    mode: 'included',
    includedProductCodes: [productCode],
    excludedProductCodes: [],
  }
}

export function buildSalesScope(productCode: string): ProductSalesAnalysisScope {
  return {
    mode: 'currentProduct',
    productCode,
  }
}

export function buildAllocationQuery(startDate: string, endDate: string) {
  return { startDate, endDate }
}

interface TablePaginationLike {
  current?: number
  pageSize?: number
}

interface TableSorterLike {
  field?: string | number
  order?: 'ascend' | 'descend' | null
}

export function mapContainerTableChangeToQuery(
  pagination: TablePaginationLike,
  sorter: TableSorterLike,
) {
  const hasActiveSort = sorter.order === 'ascend' || sorter.order === 'descend'
  const allowedSortFields = new Set([
    'effectiveArrivalDate',
    'loadingDate',
    'containerNumber',
    'status',
    'loadingQuantity',
  ])
  const requestedSortField = typeof sorter.field === 'string' ? sorter.field : ''
  const hasAllowedActiveSort = hasActiveSort && allowedSortFields.has(requestedSortField)
  return {
    pageNumber: pagination.current ?? 1,
    pageSize: pagination.pageSize ?? 20,
    sortBy: hasAllowedActiveSort
      ? requestedSortField
      : 'effectiveArrivalDate',
    sortDirection: (hasAllowedActiveSort && sorter.order === 'ascend' ? 'asc' : 'desc') as WarehouseProductRecordSortDirection,
  }
}

export function sumAllocationBranchAmounts(
  branches: readonly WarehouseProductAllocationBranch[],
): Pick<WarehouseProductAllocationSummary, 'allocationQuantity' | 'allocationAmount'> {
  return branches.reduce(
    (total, branch) => ({
      allocationQuantity: total.allocationQuantity + branch.allocationQuantity,
      allocationAmount: total.allocationAmount + branch.allocationAmount,
    }),
    { allocationQuantity: 0, allocationAmount: 0 },
  )
}

export function filterAllocationBranches(
  branches: readonly WarehouseProductAllocationBranch[],
  keyword: string,
): WarehouseProductAllocationBranch[] {
  const query = keyword.trim().toLowerCase()
  if (!query) {
    return [...branches]
  }
  return branches.filter((branch) => {
    return (
      branch.storeCode.toLowerCase().includes(query)
      || (branch.storeName ?? '').toLowerCase().includes(query)
    )
  })
}

export function formatQuantity(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return '-'
  }
  return value.toLocaleString('en-AU', { maximumFractionDigits: 2 })
}

export function formatAustralianCurrency(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return '-'
  }
  return value.toLocaleString('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

export function formatChineseCurrency(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return '-'
  }
  return value.toLocaleString('zh-CN', {
    style: 'currency',
    currency: 'CNY',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

export function formatAveragePrice(
  quantity: number,
  averageUnitPrice: number | null | undefined,
): string {
  // 净数量为零或后端未返回均价时，均视为“无均价”，显示 --。
  if (quantity === 0 || averageUnitPrice == null || !Number.isFinite(averageUnitPrice)) {
    return '--'
  }
  return formatAustralianCurrency(averageUnitPrice)
}

export function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError'
}

export function getContainerDetailPath(containerCode: string): string {
  return `/warehouse/container/detail/${encodeURIComponent(containerCode)}`
}
