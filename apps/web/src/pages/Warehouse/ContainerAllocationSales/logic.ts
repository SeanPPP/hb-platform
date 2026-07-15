import dayjs, { type Dayjs } from 'dayjs'
import type {
  ContainerAllocationSalesQuery,
  ContainerAllocationSalesSortDirection,
} from '../../../types/containerAllocationSales'

interface TablePaginationLike {
  current?: number
  pageSize?: number
}

interface TableSorterLike {
  field?: string | number
  order?: 'ascend' | 'descend' | null
}

export function buildRangeByWeeks(arrivalDate: string, weeks: number, today = dayjs()) {
  const start = dayjs(arrivalDate).startOf('day')
  const requestedEnd = start.add(weeks * 7 - 1, 'day')
  const end = requestedEnd.isAfter(today, 'day') ? today.startOf('day') : requestedEnd
  return {
    startDate: start.format('YYYY-MM-DD'),
    endDate: end.format('YYYY-MM-DD'),
  }
}

export function buildQuickRangeQuery(arrivalDate: string, weeks: number, today = dayjs()) {
  return {
    ...buildRangeByWeeks(arrivalDate, weeks, today),
    pageNumber: 1,
  }
}

export function buildBranchQuery(productCode: string, startDate: string, endDate: string) {
  return { productCode, startDate, endDate }
}

export function buildContainerAllocationSalesQuery(
  committed: ContainerAllocationSalesQuery,
  overrides: ContainerAllocationSalesQuery = {},
) {
  return { ...committed, ...overrides }
}

export function isCustomEndDateDisabled(current: Dayjs, arrivalDate: string, today = dayjs()) {
  const start = dayjs(arrivalDate).startOf('day')
  return current.isBefore(start, 'day') || current.isAfter(today, 'day')
}

export function mapTableChangeToQuery(
  pagination: TablePaginationLike,
  sorter: TableSorterLike,
) {
  return {
    pageNumber: pagination.current ?? 1,
    pageSize: pagination.pageSize ?? 20,
    sortBy: typeof sorter.field === 'string' ? sorter.field : 'productCode',
    sortDirection: (sorter.order === 'descend' ? 'desc' : 'asc') as ContainerAllocationSalesSortDirection,
  }
}

export function getGrossMarginDisplay(metric: {
  grossMarginRate: number | null
  isGrossMarginComplete: boolean | null
}) {
  if (metric.isGrossMarginComplete === false) return '成本缺失'
  if (metric.grossMarginRate == null) return '-'
  return `${(metric.grossMarginRate * 100).toFixed(2)}%`
}

export function formatAustralianCurrency(value: number | null | undefined) {
  if (value == null) return '-'
  return `$${value.toLocaleString('en-AU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`
}

interface ContainerAllocationSalesViewStateInput {
  canQuery: boolean
  total: number
  statisticStatus: string
  allocationQuantity: number
  salesQuantity: number | null
  search: string
}

interface ContainerAllocationSalesAutoLoadInput {
  active: boolean
  requestedContainerGuid: string
  loadedContainerGuid: string | null
}

export function shouldLoadContainerAllocationSales(input: ContainerAllocationSalesAutoLoadInput) {
  return Boolean(
    input.active &&
    input.requestedContainerGuid &&
    input.requestedContainerGuid !== input.loadedContainerGuid,
  )
}

export function getContainerAllocationSalesViewState(input: ContainerAllocationSalesViewStateInput) {
  if (!input.canQuery) {
    return {
      showTable: false,
      emptyDescription: null,
      showNoStatistics: false,
    }
  }

  if (input.total === 0) {
    return {
      showTable: false,
      emptyDescription: input.search.trim() ? '未找到匹配商品' : '暂无货柜商品',
      showNoStatistics: false,
    }
  }

  return {
    showTable: true,
    emptyDescription: null,
    // 统计未刷新时销售字段为空，不能把“未知”误判成“没有数据”。
    showNoStatistics:
      input.statisticStatus === 'Fresh' &&
      input.allocationQuantity === 0 &&
      (input.salesQuantity ?? 0) === 0,
  }
}
