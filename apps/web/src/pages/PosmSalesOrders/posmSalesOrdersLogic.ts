import type {
  PosmSalesOrderColumnFilters,
  PosmSalesOrderQueryParams,
  PosmSalesOrderSortField,
  PosmSalesOrderSortState,
} from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

export interface PosmSalesOrderListQueryState {
  startDate: string
  endDate: string
  branchCode: string
  orderType: OrderType
  keyword: string
  page: number
  pageSize: number
  columnFilters: PosmSalesOrderColumnFilters
  sort: PosmSalesOrderSortState
}

export interface PosmSalesOrderTopColumnFilters {
  startDate: string
  endDate: string
  branchCode: string
}

export interface PosmSalesOrderTopFilterDraft extends PosmSalesOrderTopColumnFilters {
  orderType: OrderType
  keyword: string
}

export type PosmSalesOrderNumberRangeName =
  | 'skuCount'
  | 'itemCount'
  | 'totalAmount'
  | 'discountAmount'
  | 'actualPay'

export type PosmSalesOrderNumberRangeValidation =
  | { isValid: true }
  | { isValid: false; range: PosmSalesOrderNumberRangeName }

const SORT_FIELD_BY_COLUMN: Record<string, PosmSalesOrderSortField> = {
  orderGuid: 'orderGuid',
  branchCode: 'branchCode',
  branchName: 'branchCode',
  deviceCode: 'deviceCode',
  date: 'orderTime',
  time: 'orderTime',
  orderTime: 'orderTime',
  skuCount: 'skuCount',
  itemCount: 'itemCount',
  totalAmount: 'totalAmount',
  discountAmount: 'discountAmount',
  actualAmount: 'actualPay',
  actualPay: 'actualPay',
}

const NUMBER_RANGES: Array<{
  name: PosmSalesOrderNumberRangeName
  min: keyof PosmSalesOrderColumnFilters
  max: keyof PosmSalesOrderColumnFilters
}> = [
  { name: 'skuCount', min: 'skuCountMin', max: 'skuCountMax' },
  { name: 'itemCount', min: 'itemCountMin', max: 'itemCountMax' },
  { name: 'totalAmount', min: 'totalAmountMin', max: 'totalAmountMax' },
  { name: 'discountAmount', min: 'discountAmountMin', max: 'discountAmountMax' },
  { name: 'actualPay', min: 'actualPayMin', max: 'actualPayMax' },
]

function trimOrUndefined(value?: string): string | undefined {
  const normalized = value?.trim()
  return normalized || undefined
}

export function resolvePosmSalesOrderClientUtcOffsetMinutes(
  startDate: string | undefined,
  fallbackOffsetMinutes: number,
): number {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(startDate?.trim() ?? '')
  if (!match) return fallbackOffsetMinutes

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1) return fallbackOffsetMinutes
  const localMidnight = new Date(0)
  localMidnight.setFullYear(year, month - 1, day)
  localMidnight.setHours(0, 0, 0, 0)
  if (
    localMidnight.getFullYear() !== year ||
    localMidnight.getMonth() !== month - 1 ||
    localMidnight.getDate() !== day
  ) {
    return fallbackOffsetMinutes
  }

  // 以选中日期的当地零点取 offset，兼容存在夏令时的浏览器时区。
  return -localMidnight.getTimezoneOffset()
}

export function createPosmSalesOrderColumnFilterDraft(
  applied: PosmSalesOrderColumnFilters,
  changes: Partial<PosmSalesOrderColumnFilters> = {},
): PosmSalesOrderColumnFilters {
  return { ...applied, ...changes }
}

export function normalizePosmSalesOrderFilterNumber(
  value: string | number | null | undefined,
  integer: boolean,
): number | undefined {
  if (value === null || value === undefined || value === '') return undefined
  const normalized = Number(value)
  if (!Number.isFinite(normalized)) return undefined
  return integer ? Math.round(normalized) : normalized
}

export function mapPosmSalesOrderSortState(
  columnKey?: React.Key,
  order?: 'ascend' | 'descend' | null,
): PosmSalesOrderSortState {
  const field = SORT_FIELD_BY_COLUMN[String(columnKey ?? '')] ?? 'orderTime'
  return { field, direction: order === 'descend' ? 'desc' : 'asc' }
}

export function validatePosmSalesOrderNumberRanges(
  filters: PosmSalesOrderColumnFilters,
): PosmSalesOrderNumberRangeValidation {
  for (const range of NUMBER_RANGES) {
    const min = filters[range.min]
    const max = filters[range.max]
    if (typeof min === 'number' && typeof max === 'number' && min > max) {
      return { isValid: false, range: range.name }
    }
  }
  return { isValid: true }
}

export function syncTopFiltersToColumnFilters(
  current: PosmSalesOrderColumnFilters,
  top: PosmSalesOrderTopColumnFilters,
): PosmSalesOrderColumnFilters {
  return { ...current, startDate: top.startDate, endDate: top.endDate, branchCode: top.branchCode }
}

export function syncColumnFiltersToTopFilters(
  columns: PosmSalesOrderColumnFilters,
): PosmSalesOrderTopColumnFilters {
  return {
    startDate: columns.startDate ?? '',
    endDate: columns.endDate ?? '',
    branchCode: columns.branchCode ?? '',
  }
}

export function createResetPosmSalesOrderState(
  today: string,
  pageSize: number,
): PosmSalesOrderListQueryState {
  return {
    startDate: today,
    endDate: today,
    branchCode: '',
    orderType: OrderType.All,
    keyword: '',
    page: 1,
    pageSize,
    columnFilters: {
      startDate: today,
      endDate: today,
      branchCode: '',
      orderGuidKeyword: undefined,
      deviceCodeKeyword: undefined,
      timeStart: undefined,
      timeEnd: undefined,
      skuCountMin: undefined,
      skuCountMax: undefined,
      itemCountMin: undefined,
      itemCountMax: undefined,
      totalAmountMin: undefined,
      totalAmountMax: undefined,
      discountAmountMin: undefined,
      discountAmountMax: undefined,
      actualPayMin: undefined,
      actualPayMax: undefined,
    },
    sort: { field: 'orderTime', direction: 'asc' },
  }
}

export function applyPosmSalesOrderQueryChange(
  state: PosmSalesOrderListQueryState,
  changes: Partial<PosmSalesOrderListQueryState>,
): PosmSalesOrderListQueryState {
  return {
    ...state,
    ...changes,
    columnFilters: { ...state.columnFilters, ...changes.columnFilters },
    sort: changes.sort ?? state.sort,
    page: 1,
  }
}

export function applyPosmSalesOrderTopFilterDraft(
  state: PosmSalesOrderListQueryState,
  topDraft: PosmSalesOrderTopFilterDraft,
): PosmSalesOrderListQueryState {
  return applyPosmSalesOrderQueryChange(state, {
    ...topDraft,
    columnFilters: syncTopFiltersToColumnFilters(state.columnFilters, topDraft),
  })
}

export function applyPosmSalesOrderColumnFilterDraft(
  state: PosmSalesOrderListQueryState,
  columnDraft: PosmSalesOrderColumnFilters,
): PosmSalesOrderListQueryState {
  return applyPosmSalesOrderQueryChange(state, {
    ...syncColumnFiltersToTopFilters(columnDraft),
    columnFilters: columnDraft,
  })
}

export function isLatestPosmSalesOrderRequest(
  requestId: number,
  latestRequestId: number,
): boolean {
  return requestId === latestRequestId
}

export function buildPosmSalesOrderListQuery(
  state: PosmSalesOrderListQueryState,
  overrides: Partial<PosmSalesOrderListQueryState> = {},
): PosmSalesOrderQueryParams {
  const nextState = {
    ...state,
    ...overrides,
    columnFilters: { ...state.columnFilters, ...overrides.columnFilters },
    sort: overrides.sort ?? state.sort,
  }
  const filters = nextState.columnFilters
  const startDate = trimOrUndefined(nextState.startDate)

  return {
    startDate,
    endDate: trimOrUndefined(nextState.endDate),
    branchCode: trimOrUndefined(nextState.branchCode),
    orderType: nextState.orderType,
    keyword: trimOrUndefined(nextState.keyword),
    orderGuidKeyword: trimOrUndefined(filters.orderGuidKeyword),
    deviceCodeKeyword: trimOrUndefined(filters.deviceCodeKeyword),
    timeStart: filters.timeStart,
    timeEnd: filters.timeEnd,
    clientUtcOffsetMinutes: resolvePosmSalesOrderClientUtcOffsetMinutes(
      startDate,
      -new Date().getTimezoneOffset(),
    ),
    skuCountMin: filters.skuCountMin,
    skuCountMax: filters.skuCountMax,
    itemCountMin: filters.itemCountMin,
    itemCountMax: filters.itemCountMax,
    totalAmountMin: filters.totalAmountMin,
    totalAmountMax: filters.totalAmountMax,
    discountAmountMin: filters.discountAmountMin,
    discountAmountMax: filters.discountAmountMax,
    actualPayMin: filters.actualPayMin,
    actualPayMax: filters.actualPayMax,
    sortField: nextState.sort.field,
    sortDirection: nextState.sort.direction,
    pageNumber: nextState.page,
    pageSize: nextState.pageSize,
  }
}
