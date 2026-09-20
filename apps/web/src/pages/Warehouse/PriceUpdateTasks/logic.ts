import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'
import type {
  StorePriceUpdateCompletionMode,
  StorePriceUpdateTask,
  StorePriceUpdateTaskProductRow,
  StorePriceUpdateTaskProductStore,
  StorePriceUpdateTaskStoreRow,
} from '../../../services/storePriceUpdateTaskService'

export type PriceUpdateTasksTab = 'by-store' | 'by-product' | 'tasks'
export type PriceUpdateTaskKindFilter = '' | 'PriceUpdate' | 'LabelOnly'
export type PriceUpdateTaskStatusFilter = 'Pending' | 'Completed' | 'All'

export const PRICE_UPDATE_TASKS_TABS: readonly PriceUpdateTasksTab[] = ['by-store', 'by-product', 'tasks']

export interface PriceUpdateTasksFilters {
  /** 本地日期 YYYY-MM-DD（含首尾两天）。 */
  startDate: string
  endDate: string
  storeCode: string
  kind: PriceUpdateTaskKindFilter
  initiatorName: string
  keyword: string
}

export interface PriceUpdateTasksInitialState {
  tab: PriceUpdateTasksTab
  keyword: string
  storeCode: string
  hqSyncFailedOnly: boolean
}

type Translate = (key: string, options?: Record<string, unknown>) => string

const I18N = 'warehouse.priceUpdateTasks'
const HOUR_MS = 60 * 60 * 1000
const DAY_MS = 24 * HOUR_MS

function pad(value: number) {
  return String(value).padStart(2, '0')
}

function toLocalDateString(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function parseLocalDate(value: string, endOfDay: boolean): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return null
  const [, year, month, day] = match
  return endOfDay
    ? new Date(Number(year), Number(month) - 1, Number(day), 23, 59, 59, 999)
    : new Date(Number(year), Number(month) - 1, Number(day), 0, 0, 0, 0)
}

function parseUtc(value: string | null | undefined): Date | null {
  if (!value) return null
  // 后端 DateTime 序列化可能不带 Z；契约约定均为 UTC，缺时区标记时按 UTC 解析，避免被当成本地时间。
  const normalized = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`
  const date = new Date(normalized)
  return Number.isNaN(date.getTime()) ? null : date
}

/** 默认近 7 天（含今天），按浏览器本地时区。 */
export function getDefaultPriceUpdateTasksRange(now = new Date()) {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6)
  return { startDate: toLocalDateString(start), endDate: toLocalDateString(now) }
}

export function createDefaultPriceUpdateTasksFilters(now = new Date()): PriceUpdateTasksFilters {
  return { ...getDefaultPriceUpdateTasksRange(now), storeCode: '', kind: '', initiatorName: '', keyword: '' }
}

/** 供仓库商品保存提示、指标卡等链接跳转时通过 URL query 初始化页签与筛选。 */
export function parsePriceUpdateTasksSearch(search: string): PriceUpdateTasksInitialState {
  const params = new URLSearchParams(search)
  const tab = params.get('tab') as PriceUpdateTasksTab | null
  return {
    tab: tab && PRICE_UPDATE_TASKS_TABS.includes(tab) ? tab : 'by-store',
    keyword: (params.get('keyword') ?? '').trim(),
    storeCode: (params.get('storeCode') ?? '').trim(),
    hqSyncFailedOnly: params.get('hqSyncFailedOnly') === 'true',
  }
}

/** 公共筛选 → 接口 query；空值不传，日期转成本地整日对应的 UTC 区间。 */
export function buildPriceUpdateTasksQuery(filters: PriceUpdateTasksFilters): Record<string, unknown> {
  const from = parseLocalDate(filters.startDate, false)
  const to = parseLocalDate(filters.endDate, true)
  const keyword = filters.keyword.trim()
  const initiatorName = filters.initiatorName.trim()
  return {
    ...(from ? { fromUtc: from.toISOString() } : {}),
    ...(to ? { toUtc: to.toISOString() } : {}),
    ...(filters.storeCode ? { storeCode: filters.storeCode } : {}),
    ...(filters.kind ? { kind: filters.kind } : {}),
    ...(initiatorName ? { initiatorName } : {}),
    ...(keyword ? { keyword } : {}),
  }
}

export function resolveInitiatorName(name: string | null | undefined, t: Translate): string {
  const trimmed = (name ?? '').trim()
  if (!trimmed) return '--'
  return trimmed.toLowerCase() === 'system' ? t(`${I18N}.initiator.system`) : trimmed
}

const KNOWN_INITIATOR_SOURCES = new Set([
  'WarehouseProducts',
  'MobileWarehouse',
  'BatchUpdate',
  'WarehouseAutoSync',
  'LocalSupplierInvoice',
  'DomesticImport',
  'StoreOrderImportPriceVariance',
])

/** 来源代码 → 展示名；未知代码原样显示，避免后端新增来源时页面出现空白。 */
export function resolveInitiatorSourceLabel(
  source: string | null | undefined,
  reference: string | null | undefined,
  t: Translate,
): string {
  const code = (source ?? '').trim()
  if (!code) return '--'
  if (code === 'StoreSync') {
    const store = (reference ?? '').trim()
    return store ? t(`${I18N}.sources.StoreSyncWithStore`, { store }) : t(`${I18N}.sources.StoreSync`)
  }
  if (code.startsWith('DataSync')) return t(`${I18N}.sources.DataSync`)
  if (KNOWN_INITIATOR_SOURCES.has(code)) return t(`${I18N}.sources.${code}`)
  return t(`${I18N}.sources.${resolveSourceFamily(code)}`)
}

/** 审计来源代码种类多且会增加：按前缀归到业务大类，识别不了的归为「其它入口」。 */
function resolveSourceFamily(code: string): string {
  if (code.startsWith('Container') || code.startsWith('YiwuContainer')) return 'Container'
  if (code.startsWith('Domestic')) return 'DomesticProduct'
  if (code === 'NonDomesticImport') return 'NonDomesticImport'
  if (code.startsWith('LocalSupplierInvoice')) return 'LocalSupplierInvoice'
  if (code.startsWith('StoreOrder')) return 'StoreOrder'
  if (code.startsWith('ProductLegacy') || code.toLowerCase() === 'legacy') return 'LegacyApi'
  return 'Other'
}

export function formatMoney(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '--' : `$${value.toFixed(2)}`
}

function formatPercentNumber(rate: number) {
  return String(Math.round(rate * 10000) / 100)
}

/** 折扣为减免比例 0~1：null = 未设置，0 = 明确无折扣。 */
export function formatDiscount(rate: number | null | undefined, t: Translate): string {
  if (rate === null || rate === undefined || !Number.isFinite(rate)) return '--'
  return rate <= 0 ? t(`${I18N}.discount.none`) : t(`${I18N}.discount.off`, { percent: formatPercentNumber(rate) })
}

export function formatPriceWithDiscount(price: number | null | undefined, rate: number | null | undefined, t: Translate): string {
  const hasRate = rate !== null && rate !== undefined && Number.isFinite(rate)
  return hasRate ? `${formatMoney(price)} · ${formatDiscount(rate, t)}` : formatMoney(price)
}

/** 任务明细「价格变化」：需改价 = 本店 → 仓库；待换标签 = 标签（旧）→ 现价。 */
export function getTaskPriceChange(task: StorePriceUpdateTask, t: Translate) {
  if (task.kind === 'LabelOnly') {
    return {
      fromLabel: t(`${I18N}.priceChange.shelf`),
      from: formatPriceWithDiscount(task.shelfRetailPrice, task.shelfDiscountRate, t),
      toLabel: t(`${I18N}.priceChange.current`),
      to: formatPriceWithDiscount(task.storeRetailPrice, task.storeDiscountRate, t),
      strikeFrom: true,
    }
  }
  return {
    fromLabel: t(`${I18N}.priceChange.store`),
    from: formatPriceWithDiscount(task.storeRetailPrice, task.storeDiscountRate, t),
    toLabel: t(`${I18N}.priceChange.warehouse`),
    to: formatPriceWithDiscount(task.targetRetailPrice, task.targetDiscountRate, t),
    strikeFrom: false,
  }
}

function distinctFinite(values: (number | null | undefined)[]): number[] {
  return [...new Set(values.filter((value): value is number => value !== null && value !== undefined && Number.isFinite(value)))]
}

/**
 * 分店同步：分店价已被改成来源分店的价格，任务只剩换标签，变更应显示分店现价；
 * 行上的 targetRetailPrice 是仓库零售价，与这次同步无关。优先取仍待换标签的分店，全部完成后取全部分店。
 */
function getStoreSyncChangeLines(
  stores: StorePriceUpdateTaskProductRow['stores'],
  t: Translate,
): string[] | null {
  const labelOnly = stores.filter((store) => store.state === 'LabelOnly')
  const pool = labelOnly.length ? labelOnly : stores
  const prices = distinctFinite(pool.map((store) => store.storeRetailPrice))
  if (!prices.length) return null
  const lines = [t(`${I18N}.change.retailPrice`, { value: prices.map((price) => formatMoney(price)).join(' / ') })]
  const discounts = distinctFinite(pool.map((store) => store.storeDiscountRate)).filter((rate) => rate > 0)
  if (discounts.length) {
    lines.push(t(`${I18N}.change.discount`, { value: discounts.map((rate) => formatDiscount(rate, t)).join(' / ') }))
  }
  return lines
}

/** 按商品页签「变更」列：零售价与建议折扣目标可能同时存在；分店同步改为显示同步后的分店价。 */
export function getProductChangeLines(
  row: Pick<StorePriceUpdateTaskProductRow, 'targetRetailPrice' | 'targetDiscountRate'>
    & Partial<Pick<StorePriceUpdateTaskProductRow, 'initiatorSource' | 'stores'>>,
  t: Translate,
): string[] {
  if (row.initiatorSource === 'StoreSync' && row.stores?.length) {
    const storeSyncLines = getStoreSyncChangeLines(row.stores, t)
    if (storeSyncLines) return storeSyncLines
  }
  const lines: string[] = []
  if (row.targetRetailPrice !== null && row.targetRetailPrice !== undefined) {
    lines.push(t(`${I18N}.change.retailPrice`, { value: formatMoney(row.targetRetailPrice) }))
  }
  if (row.targetDiscountRate !== null && row.targetDiscountRate !== undefined) {
    lines.push(t(`${I18N}.change.discount`, { value: formatDiscount(row.targetDiscountRate, t) }))
  }
  return lines.length ? lines : ['--']
}

export function getCompletionRateColor(rate: number): 'red' | 'orange' | 'green' {
  if (rate < 0.6) return 'red'
  return rate < 0.85 ? 'orange' : 'green'
}

export function toPercent(rate: number | null | undefined): number {
  if (rate === null || rate === undefined || !Number.isFinite(rate)) return 0
  return Math.round(Math.min(1, Math.max(0, rate)) * 100)
}

/** 最久未处理天数；无未完成任务返回 null。 */
export function getPendingAgeDays(oldestPendingAtUtc: string | null | undefined, now = new Date()): number | null {
  const date = parseUtc(oldestPendingAtUtc)
  if (!date) return null
  return Math.max(0, Math.floor((now.getTime() - date.getTime()) / DAY_MS))
}

export function formatPendingAge(days: number | null, t: Translate): string {
  if (days === null) return '--'
  return days === 0 ? t(`${I18N}.age.today`) : t(`${I18N}.age.days`, { count: days })
}

/** "已等待 N 小时/天"：不足 1 天按小时，最少显示 1 小时。 */
export function formatWaiting(initiatedAtUtc: string | null | undefined, now: Date, t: Translate): string {
  const date = parseUtc(initiatedAtUtc)
  if (!date) return ''
  const elapsed = Math.max(0, now.getTime() - date.getTime())
  if (elapsed >= DAY_MS) return t(`${I18N}.waiting.days`, { count: Math.floor(elapsed / DAY_MS) })
  return t(`${I18N}.waiting.hours`, { count: Math.max(1, Math.floor(elapsed / HOUR_MS)) })
}

export function formatRelativeTime(value: string | null | undefined, now: Date, t: Translate): string {
  const date = parseUtc(value)
  if (!date) return '--'
  const elapsed = Math.max(0, now.getTime() - date.getTime())
  if (elapsed < 60 * 1000) return t(`${I18N}.relative.justNow`)
  if (elapsed < HOUR_MS) return t(`${I18N}.relative.minutes`, { count: Math.floor(elapsed / 60000) })
  if (elapsed < DAY_MS) return t(`${I18N}.relative.hours`, { count: Math.floor(elapsed / HOUR_MS) })
  return t(`${I18N}.relative.days`, { count: Math.floor(elapsed / DAY_MS) })
}

/** 浏览器本地时区的 YYYY-MM-DD HH:mm。 */
export function formatLocalDateTime(value: string | null | undefined): string {
  const date = parseUtc(value)
  if (!date) return '--'
  return `${toLocalDateString(date)} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** 各店进度分段条：Skipped（特殊商品未建任务）不占格，也不计入分母。 */
export function buildStoreProgressSegments(stores: StorePriceUpdateTaskProductStore[]) {
  return stores
    .filter((store) => store.state !== 'Skipped')
    .map((store) => ({ storeCode: store.storeCode, storeName: store.storeName || store.storeCode, state: store.state }))
}

export function getCompletionModeLabel(mode: StorePriceUpdateCompletionMode | string | null | undefined, t: Translate): string {
  switch (mode) {
    case 'Printed': return t(`${I18N}.completionMode.Printed`)
    case 'MarkedReplaced': return t(`${I18N}.completionMode.MarkedReplaced`)
    case 'KeptStorePrice': return t(`${I18N}.completionMode.KeptStorePrice`)
    case 'PriceAligned': return t(`${I18N}.completionMode.PriceAligned`)
    default: return mode ? String(mode) : ''
  }
}

/** 展开行里每家分店小卡的一行说明。 */
export function describeProductStore(store: StorePriceUpdateTaskProductStore, now: Date, t: Translate): string {
  switch (store.state) {
    case 'Completed':
      return [
        resolveInitiatorName(store.completedBy, t),
        formatLocalDateTime(store.completedAtUtc),
        getCompletionModeLabel(store.completionMode, t),
      ].filter((part) => part && part !== '--').join(' · ') || '--'
    case 'LabelOnly':
      return t(`${I18N}.storeCard.labelOnly`)
    case 'PriceUpdate': {
      const waiting = formatWaiting(store.initiatedAtUtc, now, t)
      const price = t(`${I18N}.storeCard.priceUpdate`, { price: formatMoney(store.storeRetailPrice) })
      return waiting ? `${price} · ${waiting}` : price
    }
    default:
      return t(`${I18N}.storeCard.skipped`)
  }
}

/** 标签状态：完成方式 + 打印次数（多次打印才显示次数）。 */
export function describeLabelStatus(task: Pick<StorePriceUpdateTask, 'status' | 'kind' | 'completionMode' | 'labelPrintCount'>, t: Translate): string {
  if (task.status === 'Completed') {
    const mode = getCompletionModeLabel(task.completionMode, t) || '--'
    return task.labelPrintCount > 1 ? `${mode} ×${task.labelPrintCount}` : mode
  }
  if (task.status === 'Cancelled') return '--'
  return task.kind === 'LabelOnly' ? t(`${I18N}.labelStatus.pending`) : t(`${I18N}.labelStatus.waitingPrice`)
}

export function getHqSyncStatusMeta(status: string | null | undefined, t: Translate): { color: string; label: string } | null {
  const code = (status ?? '').trim().toLowerCase()
  if (!code) return null
  const colors: Record<string, string> = {
    pending: 'default', processing: 'processing', retrying: 'warning', succeeded: 'success', blocked: 'error', superseded: 'default',
  }
  return colors[code] ? { color: colors[code], label: t(`${I18N}.hqSync.${code}`) } : { color: 'default', label: String(status) }
}

// ---------- CSV 导出 ----------

export function formatCsvCell(value: unknown): string {
  if (value === null || value === undefined) return ''
  let text = String(value)
  // 防 CSV 公式注入：品名等自由文本以 = + - @ 开头时，Excel 会当公式执行。
  if (/^[=+\-@\t\r]/.test(text) && !/^-?\d+(\.\d+)?%?$/.test(text)) text = `'${text}`
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

/** 带 BOM，Excel 才能正确识别 UTF-8 中文。 */
export function buildCsvContent(rows: unknown[][]): string {
  return `﻿${rows.map((row) => row.map(formatCsvCell).join(',')).join('\r\n')}\r\n`
}

export function buildPriceUpdateTasksCsvFileName(prefix: string, tab: PriceUpdateTasksTab, filters: Pick<PriceUpdateTasksFilters, 'startDate' | 'endDate'>) {
  return `${prefix}-${tab}-${filters.startDate}_${filters.endDate}.csv`
}

export function buildByStoreCsvRows(rows: StorePriceUpdateTaskStoreRow[], now: Date, t: Translate): unknown[][] {
  return [
    [
      t(`${I18N}.columns.storeCode`), t(`${I18N}.columns.storeName`), t(`${I18N}.columns.pending`),
      t(`${I18N}.kind.PriceUpdate`), t(`${I18N}.kind.LabelOnly`), t(`${I18N}.columns.completed`),
      t(`${I18N}.columns.completionRate`), t(`${I18N}.columns.oldestPending`), t(`${I18N}.columns.lastCompletedBy`), t(`${I18N}.columns.lastCompletedAt`),
    ],
    ...rows.map((row) => [
      row.storeCode, row.storeName ?? '', row.pendingCount, row.pendingPriceUpdateCount, row.pendingLabelOnlyCount, row.completedCount,
      `${toPercent(row.completionRate)}%`, formatPendingAge(getPendingAgeDays(row.oldestPendingAtUtc, now), t),
      row.lastCompletedBy ? resolveInitiatorName(row.lastCompletedBy, t) : '', row.lastCompletedAtUtc ? formatLocalDateTime(row.lastCompletedAtUtc) : '',
    ]),
  ]
}

export function buildByProductCsvRows(rows: StorePriceUpdateTaskProductRow[], t: Translate): unknown[][] {
  const stateNames = (row: StorePriceUpdateTaskProductRow, state: StorePriceUpdateTaskProductStore['state']) =>
    row.stores.filter((store) => store.state === state).map((store) => store.storeName || store.storeCode).join(' / ')
  return [
    [
      t(`${I18N}.columns.itemNumber`), t(`${I18N}.columns.productName`), t(`${I18N}.columns.change`), t(`${I18N}.columns.changeCount`),
      t(`${I18N}.columns.initiator`), t(`${I18N}.columns.source`), t(`${I18N}.columns.initiatedAt`), t(`${I18N}.columns.progress`),
      t(`${I18N}.state.PriceUpdate`), t(`${I18N}.state.LabelOnly`), t(`${I18N}.state.Skipped`),
    ],
    ...rows.map((row) => [
      row.itemNumber ?? '', row.productName ?? '', getProductChangeLines(row, t).join('; '), row.changeCount,
      resolveInitiatorName(row.initiatorName, t), resolveInitiatorSourceLabel(row.initiatorSource, row.initiatorReference, t),
      formatLocalDateTime(row.initiatedAtUtc), `${row.completedStoreCount}/${row.storeCount}`,
      stateNames(row, 'PriceUpdate'), stateNames(row, 'LabelOnly'), stateNames(row, 'Skipped'),
    ]),
  ]
}

export function buildTasksCsvRows(rows: StorePriceUpdateTask[], hqSyncEnabled: boolean, t: Translate): unknown[][] {
  return [
    [
      t(`${I18N}.columns.itemNumber`), t(`${I18N}.columns.productName`), t(`${I18N}.columns.barcode`), t(`${I18N}.columns.storeCode`), t(`${I18N}.columns.storeName`),
      t(`${I18N}.columns.kind`), t(`${I18N}.columns.priceFrom`), t(`${I18N}.columns.priceTo`), t(`${I18N}.columns.initiator`), t(`${I18N}.columns.source`),
      t(`${I18N}.columns.initiatedAt`), t(`${I18N}.columns.status`), t(`${I18N}.columns.completedBy`), t(`${I18N}.columns.completedAt`), t(`${I18N}.columns.labelStatus`),
      ...(hqSyncEnabled ? [t(`${I18N}.columns.hqSync`)] : []),
    ],
    ...rows.map((task) => {
      const change = getTaskPriceChange(task, t)
      return [
        task.itemNumber ?? '', task.productName ?? '', task.barcode ?? '', task.storeCode, task.storeName ?? '',
        t(`${I18N}.kind.${task.kind}`), `${change.fromLabel} ${change.from}`, `${change.toLabel} ${change.to}`,
        resolveInitiatorName(task.initiatorName, t), resolveInitiatorSourceLabel(task.initiatorSource, task.initiatorReference, t),
        formatLocalDateTime(task.initiatedAtUtc), t(`${I18N}.status.${task.status}`),
        task.completedBy ? resolveInitiatorName(task.completedBy, t) : '', task.completedAtUtc ? formatLocalDateTime(task.completedAtUtc) : '',
        describeLabelStatus(task, t),
        ...(hqSyncEnabled ? [getHqSyncStatusMeta(task.hqSyncStatus, t)?.label ?? ''] : []),
      ]
    }),
  ]
}

/** 导出需要当前筛选下的全部分页；设置页数上限防止异常 total 造成无限翻页。 */
export async function collectAllPages<T>(
  loadPage: (page: number, pageSize: number) => Promise<{ items: T[]; total: number }>,
  pageSize = 200,
  maxPages = 100,
): Promise<T[]> {
  const all: T[] = []
  for (let page = 1; page <= maxPages; page += 1) {
    const result = await loadPage(page, pageSize)
    all.push(...result.items)
    if (!result.items.length || all.length >= result.total) break
  }
  return all
}

export function createPriceUpdateTasksRequestCoordinator() {
  const latestRequestGuard = createLatestRequestGuard()
  let controller: AbortController | null = null
  return {
    start() {
      // 切换筛选/页签/翻页时终止旧请求，并阻止旧 finally 改写最新 loading 状态。
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
