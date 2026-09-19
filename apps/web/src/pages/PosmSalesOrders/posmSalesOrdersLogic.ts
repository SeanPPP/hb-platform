import dayjs from 'dayjs'

import type {
  PosmSalesOrderQueryParams,
  PosmSalesOrderSortField,
  PosmSalesOrderSortState,
  PosmSalesOrderStatusSummary,
} from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

/** 日期区间上限（含首尾），与后端 PosmSalesOrderListRules.MaxRangeDays 一致。 */
export const MAX_RANGE_DAYS = 92

/** 件数、种数条件在全部分店时的区间上限，与后端 DetailAggregateAllStoresMaxDays 一致。 */
export const DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS = 7

export const DEFAULT_PAGE_SIZE = 50

/** 超过这个时长仍未返回时提示用户「数据量较大」，并允许取消。 */
export const SLOW_QUERY_NOTICE_MS = 3000

export type PosmSalesOrderDatePreset = 'today' | 'yesterday' | 'last7' | 'thisMonth'

export const DATE_PRESETS: PosmSalesOrderDatePreset[] = ['today', 'yesterday', 'last7', 'thisMonth']

/** 「更多筛选」里的条件：收银机、时段、实收金额、件数、种数。 */
export interface PosmSalesOrderMoreFilters {
  deviceCode?: string
  timeStart?: string
  timeEnd?: string
  actualPayMin?: number
  actualPayMax?: number
  quantityMin?: number
  quantityMax?: number
  skuCountMin?: number
  skuCountMax?: number
}

export interface PosmSalesOrderFilters extends PosmSalesOrderMoreFilters {
  startDate: string
  endDate: string
  branchCode: string
  keyword: string
  status: OrderType
}

export const DEFAULT_SORT: PosmSalesOrderSortState = { field: 'orderTime', direction: 'desc' }

export const EMPTY_MORE_FILTERS: PosmSalesOrderMoreFilters = {
  deviceCode: undefined,
  timeStart: undefined,
  timeEnd: undefined,
  actualPayMin: undefined,
  actualPayMax: undefined,
  quantityMin: undefined,
  quantityMax: undefined,
  skuCountMin: undefined,
  skuCountMax: undefined,
}

export function createDefaultFilters(today: string, branchCode = ''): PosmSalesOrderFilters {
  return {
    startDate: today,
    endDate: today,
    branchCode,
    keyword: '',
    status: OrderType.All,
    ...EMPTY_MORE_FILTERS,
  }
}

export function resolveDatePreset(preset: PosmSalesOrderDatePreset, today: string): [string, string] {
  const base = dayjs(today)
  switch (preset) {
    case 'yesterday': {
      const yesterday = base.subtract(1, 'day').format('YYYY-MM-DD')
      return [yesterday, yesterday]
    }
    case 'last7':
      return [base.subtract(6, 'day').format('YYYY-MM-DD'), today]
    case 'thisMonth':
      return [base.startOf('month').format('YYYY-MM-DD'), today]
    default:
      return [today, today]
  }
}

/** 当前区间正好是某个快捷项时高亮它；自定义区间不高亮任何快捷项。 */
export function detectDatePreset(startDate: string, endDate: string, today: string): PosmSalesOrderDatePreset | null {
  return (
    DATE_PRESETS.find((preset) => {
      const [start, end] = resolveDatePreset(preset, today)
      return start === startDate && end === endDate
    }) ?? null
  )
}

/** 区间天数含首尾：同一天为 1 天。 */
export function countDays(startDate: string, endDate: string): number {
  return dayjs(endDate).startOf('day').diff(dayjs(startDate).startOf('day'), 'day') + 1
}

export function needsDetailAggregates(filters: PosmSalesOrderMoreFilters): boolean {
  return [filters.quantityMin, filters.quantityMax, filters.skuCountMin, filters.skuCountMax].some(
    (value) => typeof value === 'number',
  )
}

export function countMoreFilters(filters: PosmSalesOrderMoreFilters): number {
  let count = 0
  if (filters.deviceCode?.trim()) count++
  if (filters.timeStart || filters.timeEnd) count++
  if (typeof filters.actualPayMin === 'number' || typeof filters.actualPayMax === 'number') count++
  if (typeof filters.quantityMin === 'number' || typeof filters.quantityMax === 'number') count++
  if (typeof filters.skuCountMin === 'number' || typeof filters.skuCountMax === 'number') count++
  return count
}

export type PosmSalesOrderValidation =
  | { ok: true }
  | { ok: false; reason: 'rangeRequired' | 'rangeTooLong' | 'detailRangeTooLong' | 'invalidNumberRange' }

/**
 * 查询前的前端把关，与后端 PosmSalesOrderListRules 同口径：
 * 区间必填且不超过 92 天；件数/种数条件在全部分店时不超过 7 天；区间下限不能大于上限。
 */
export function validateFilters(filters: PosmSalesOrderFilters): PosmSalesOrderValidation {
  if (!filters.startDate || !filters.endDate || dayjs(filters.startDate).isAfter(dayjs(filters.endDate), 'day')) {
    return { ok: false, reason: 'rangeRequired' }
  }
  const days = countDays(filters.startDate, filters.endDate)
  if (days > MAX_RANGE_DAYS) return { ok: false, reason: 'rangeTooLong' }
  const pairs: [number | undefined, number | undefined][] = [
    [filters.actualPayMin, filters.actualPayMax],
    [filters.quantityMin, filters.quantityMax],
    [filters.skuCountMin, filters.skuCountMax],
  ]
  if (pairs.some(([min, max]) => typeof min === 'number' && typeof max === 'number' && min > max)) {
    return { ok: false, reason: 'invalidNumberRange' }
  }
  if (needsDetailAggregates(filters) && !filters.branchCode && days > DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS) {
    return { ok: false, reason: 'detailRangeTooLong' }
  }
  return { ok: true }
}

function trimmedOrUndefined(value?: string): string | undefined {
  const trimmed = value?.trim()
  return trimmed ? trimmed : undefined
}

export function buildListQuery(
  filters: PosmSalesOrderFilters,
  sort: PosmSalesOrderSortState,
  page: number,
  pageSize: number,
): PosmSalesOrderQueryParams {
  return {
    startDate: filters.startDate,
    endDate: filters.endDate,
    branchCode: trimmedOrUndefined(filters.branchCode),
    orderType: filters.status,
    keyword: trimmedOrUndefined(filters.keyword),
    deviceCodeKeyword: trimmedOrUndefined(filters.deviceCode),
    timeStart: filters.timeStart,
    timeEnd: filters.timeEnd,
    actualPayMin: filters.actualPayMin,
    actualPayMax: filters.actualPayMax,
    quantityMin: filters.quantityMin,
    quantityMax: filters.quantityMax,
    skuCountMin: filters.skuCountMin,
    skuCountMax: filters.skuCountMax,
    sortField: sort.field,
    sortDirection: sort.direction,
    pageNumber: page,
    pageSize,
  }
}

const SORTABLE_COLUMNS: Record<string, PosmSalesOrderSortField> = {
  time: 'orderTime',
  branch: 'branchCode',
  totalAmount: 'totalAmount',
  discountAmount: 'discountAmount',
  actualPay: 'actualPay',
}

/** 表头排序：取消排序时回到默认的时间倒序（最新在前）。 */
export function mapTableSort(columnKey: unknown, order: 'ascend' | 'descend' | null | undefined): PosmSalesOrderSortState {
  const field = SORTABLE_COLUMNS[String(columnKey ?? '')]
  if (!field || !order) return DEFAULT_SORT
  return { field, direction: order === 'ascend' ? 'asc' : 'desc' }
}

export function tableSortOrder(
  sort: PosmSalesOrderSortState,
  columnKey: string,
): 'ascend' | 'descend' | null {
  return SORTABLE_COLUMNS[columnKey] === sort.field ? (sort.direction === 'asc' ? 'ascend' : 'descend') : null
}

/** 计入实收的状态：已支付，以及冲减实收的退款；已取消、待支付、分期不计入。 */
export const COUNTED_STATUSES = [OrderType.Paid, OrderType.Refunded]

/** 汇总条上始终显示的状态卡片；待支付、分期只在有数据时出现。 */
const ALWAYS_SHOWN_STATUSES = [OrderType.Paid, OrderType.Refunded, OrderType.Cancelled]
const OPTIONAL_STATUSES = [OrderType.Pending, OrderType.Installment]

export interface PosmSalesOrderStatusCard {
  status: OrderType
  orderCount: number
  /** 该状态的实收合计（总额 − 折扣）。 */
  actualAmount: number
  counted: boolean
}

export interface PosmSalesOrderSummaryView {
  allCount: number
  /** 净实收：已支付实收 + 退款（负数）实收。 */
  netAmount: number
  cards: PosmSalesOrderStatusCard[]
  discountTotal: number
  /** 客单价：已支付实收 ÷ 已支付单数；没有已支付订单时为 null。 */
  averageTicket: number | null
}

function roundMoney(value: number): number {
  return Math.round(value * 100) / 100
}

export function summarizeStatuses(summary: PosmSalesOrderStatusSummary[]): PosmSalesOrderSummaryView {
  const byStatus = new Map<number, PosmSalesOrderStatusSummary>()
  for (const row of summary) {
    if (row.status === null || row.status === undefined) continue
    byStatus.set(row.status, row)
  }
  const cardFor = (status: OrderType): PosmSalesOrderStatusCard => {
    const row = byStatus.get(status)
    return {
      status,
      orderCount: row?.orderCount ?? 0,
      actualAmount: roundMoney((row?.totalAmount ?? 0) - (row?.discountAmount ?? 0)),
      counted: COUNTED_STATUSES.includes(status),
    }
  }
  const cards = [
    ...ALWAYS_SHOWN_STATUSES.map(cardFor),
    ...OPTIONAL_STATUSES.map(cardFor).filter((card) => card.orderCount > 0),
  ]
  const counted = cards.filter((card) => card.counted)
  const paid = cards.find((card) => card.status === OrderType.Paid)
  const discountTotal = COUNTED_STATUSES.reduce((sum, status) => sum + (byStatus.get(status)?.discountAmount ?? 0), 0)
  return {
    allCount: summary.reduce((sum, row) => sum + row.orderCount, 0),
    netAmount: roundMoney(counted.reduce((sum, card) => sum + card.actualAmount, 0)),
    cards,
    discountTotal: roundMoney(discountTotal),
    averageTicket: paid && paid.orderCount > 0 ? roundMoney(paid.actualAmount / paid.orderCount) : null,
  }
}

const moneyFormatter = new Intl.NumberFormat('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** 金额统一两位小数、千分位；负数用真正的减号，与货币符号的顺序为 −$5.00。 */
export function formatMoney(value: number | null | undefined): string {
  const amount = roundMoney(value ?? 0)
  const text = moneyFormatter.format(Math.abs(amount))
  return amount < 0 ? `−$${text}` : `$${text}`
}

export function orderActualAmount(totalAmount?: number, discountAmount?: number): number {
  return roundMoney((totalAmount ?? 0) - (discountAmount ?? 0))
}

/** 页面只显示订单号后 6 位；完整订单号在详情抽屉里显示并可复制。 */
export function shortOrderNo(orderGuid?: string): string {
  const value = orderGuid?.trim() ?? ''
  return value ? value.slice(-6).toUpperCase() : '-'
}

export type PaymentMethodKey = 'cash' | 'card' | 'voucher' | 'other'

export function paymentMethodKey(method: number): PaymentMethodKey {
  switch (method) {
    case 1:
      return 'cash'
    case 2:
      return 'card'
    case 3:
      return 'voucher'
    default:
      return 'other'
  }
}

export interface PosmSalesOrderFilterChip {
  key: string
  field: 'branch' | 'keyword' | 'device' | 'time' | 'actualPay' | 'quantity' | 'skuCount'
  value: string
  clear: Partial<PosmSalesOrderFilters>
}

function formatRange(min: number | undefined, max: number | undefined, format: (value: number) => string): string {
  if (typeof min === 'number' && typeof max === 'number') return `${format(min)} – ${format(max)}`
  if (typeof min === 'number') return `≥ ${format(min)}`
  return `≤ ${format(max as number)}`
}

/**
 * 已生效筛选条：日期与状态在筛选栏和汇总条上一眼可见，不重复成标签；
 * 其余条件（含收进「更多筛选」的）逐个列出并可单独移除。
 */
export function buildFilterChips(
  filters: PosmSalesOrderFilters,
  storeName: (code: string) => string,
): PosmSalesOrderFilterChip[] {
  const chips: PosmSalesOrderFilterChip[] = []
  if (filters.branchCode) {
    chips.push({ key: 'branch', field: 'branch', value: storeName(filters.branchCode), clear: { branchCode: '' } })
  }
  if (filters.keyword.trim()) {
    chips.push({ key: 'keyword', field: 'keyword', value: filters.keyword.trim(), clear: { keyword: '' } })
  }
  if (filters.deviceCode?.trim()) {
    chips.push({ key: 'device', field: 'device', value: filters.deviceCode.trim(), clear: { deviceCode: undefined } })
  }
  if (filters.timeStart || filters.timeEnd) {
    chips.push({
      key: 'time',
      field: 'time',
      value: `${filters.timeStart?.slice(0, 5) ?? '00:00'} – ${filters.timeEnd?.slice(0, 5) ?? '23:59'}`,
      clear: { timeStart: undefined, timeEnd: undefined },
    })
  }
  if (typeof filters.actualPayMin === 'number' || typeof filters.actualPayMax === 'number') {
    chips.push({
      key: 'actualPay',
      field: 'actualPay',
      value: formatRange(filters.actualPayMin, filters.actualPayMax, formatMoney),
      clear: { actualPayMin: undefined, actualPayMax: undefined },
    })
  }
  if (typeof filters.quantityMin === 'number' || typeof filters.quantityMax === 'number') {
    chips.push({
      key: 'quantity',
      field: 'quantity',
      value: formatRange(filters.quantityMin, filters.quantityMax, String),
      clear: { quantityMin: undefined, quantityMax: undefined },
    })
  }
  if (typeof filters.skuCountMin === 'number' || typeof filters.skuCountMax === 'number') {
    chips.push({
      key: 'skuCount',
      field: 'skuCount',
      value: formatRange(filters.skuCountMin, filters.skuCountMax, String),
      clear: { skuCountMin: undefined, skuCountMax: undefined },
    })
  }
  return chips
}

/** 数字输入：空值、非有限数一律视为未设置；件数、种数取整。 */
export function normalizeFilterNumber(value: string | number | null | undefined, integer: boolean): number | undefined {
  if (value === null || value === undefined || value === '') return undefined
  const normalized = Number(value)
  if (!Number.isFinite(normalized)) return undefined
  return integer ? Math.round(normalized) : normalized
}

/** 抽屉里 ↑↓ 切换订单：在当前页内移动，到头停住。 */
export function stepOrderIndex(current: number, delta: -1 | 1, length: number): number {
  if (length <= 0) return -1
  return Math.min(Math.max(current + delta, 0), length - 1)
}

export function isLatestRequest(requestId: number, latestRequestId: number): boolean {
  return requestId === latestRequestId
}
