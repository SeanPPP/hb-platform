import type {
  ProductSalesAnalysisSelection,
  ProductSalesAnalysisSupplier,
  ProductSalesSummaryRow,
} from '../../../types/productSalesAnalysis'

export interface ProductSalesAnalysisDateRange {
  startDate: string
  endDate: string
}

export interface LatestRequestGuard {
  begin: () => number
  isLatest: (requestId: number) => boolean
  invalidate: () => void
}

export interface DailyChartPadding {
  left: number
  right: number
  top: number
  bottom: number
}

export interface DailyChartInputPoint {
  date: string
  quantity: number
  averageUnitPrice: number | null
}

export interface DailyChartBar {
  date: string
  quantity: number
  x: number
  y: number
  width: number
  height: number
}

export interface DailyChartAveragePoint {
  index: number
  date: string
  averageUnitPrice: number
  x: number
  y: number
}

export interface DailyChartXAxisTick {
  index: number
  date: string
  x: number
}

export interface DailyChartModel {
  width: number
  height: number
  plot: DailyChartPadding
  zeroY: number
  minValue: number
  maxValue: number
  averageMin: number | null
  averageMax: number | null
  bars: DailyChartBar[]
  averagePoints: DailyChartAveragePoint[]
  averageSegments: DailyChartAveragePoint[][]
  xAxisTicks: DailyChartXAxisTick[]
}

const brisbaneDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

const dateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})$/

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

  const timestamp = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))
  if (Number.isNaN(timestamp)) {
    return null
  }

  const parsed = new Date(timestamp)
  if (
    parsed.getUTCFullYear() !== Number(match[1])
    || parsed.getUTCMonth() !== Number(match[2]) - 1
    || parsed.getUTCDate() !== Number(match[3])
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

export function buildDateRange(days: number, now = new Date()): ProductSalesAnalysisDateRange {
  const endDate = shiftDateOnly(formatBrisbaneDate(now), -1)
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

function uniqueStrings(values: string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  values.forEach((value) => {
    if (value && !seen.has(value)) {
      seen.add(value)
      result.push(value)
    }
  })
  return result
}

export function createAllFilteredSelection(
  excludedProductCodes: string[] = [],
): ProductSalesAnalysisSelection {
  return {
    mode: 'allFiltered',
    includedProductCodes: [],
    excludedProductCodes: uniqueStrings(excludedProductCodes),
  }
}

export function createIncludedSelection(
  includedProductCodes: string[] = [],
): ProductSalesAnalysisSelection {
  return {
    mode: 'included',
    includedProductCodes: uniqueStrings(includedProductCodes),
    excludedProductCodes: [],
  }
}

export function clearProductSelection(): ProductSalesAnalysisSelection {
  return createIncludedSelection()
}

export function resetProductSelection(): ProductSalesAnalysisSelection {
  return createAllFilteredSelection()
}

export function toggleExcludedProduct(
  selection: ProductSalesAnalysisSelection,
  productCode: string,
  selected: boolean,
): ProductSalesAnalysisSelection {
  if (selection.mode !== 'allFiltered') {
    return selection
  }

  const excludedSet = new Set(selection.excludedProductCodes)
  if (selected) {
    excludedSet.delete(productCode)
  } else {
    excludedSet.add(productCode)
  }

  return {
    ...selection,
    excludedProductCodes: [...excludedSet],
  }
}

export function getSelectedProductCodes(
  selection: ProductSalesAnalysisSelection,
  candidateProductCodes: string[],
): string[] {
  if (selection.mode === 'included') {
    const includedSet = new Set(selection.includedProductCodes)
    return candidateProductCodes.filter((code) => includedSet.has(code))
  }

  const excludedSet = new Set(selection.excludedProductCodes)
  return candidateProductCodes.filter((code) => !excludedSet.has(code))
}

export function isProductSelectionEmpty(selection: ProductSalesAnalysisSelection): boolean {
  return selection.mode === 'included' && selection.includedProductCodes.length === 0
}

export function isProductSelected(
  selection: ProductSalesAnalysisSelection,
  productCode: string | null | undefined,
): boolean {
  if (!productCode) return false
  return selection.mode === 'included'
    ? selection.includedProductCodes.includes(productCode)
    : !selection.excludedProductCodes.includes(productCode)
}

const tableRowInteractiveSelector = [
  'a',
  'button',
  'input',
  'select',
  'textarea',
  '[role="button"]',
  '[role="link"]',
  '[tabindex]:not([tabindex="-1"])',
  '[data-row-click-ignore]',
].join(',')

export function shouldTriggerTableRowClick(target: unknown, currentTarget: unknown): boolean {
  if (!target || typeof target !== 'object') return true
  const closest = (target as { closest?: (selector: string) => unknown }).closest
  if (typeof closest !== 'function') return true

  const interactiveTarget = closest.call(target, tableRowInteractiveSelector)
  return interactiveTarget == null || interactiveTarget === currentTarget
}

export function resolveCurrentProductCode(
  currentProductCode: string | null | undefined,
  rows: readonly ProductSalesSummaryRow[],
): string | null {
  if (currentProductCode && rows.some((row) => row.productCode === currentProductCode)) {
    return currentProductCode
  }

  return rows[0]?.productCode ?? null
}

export function createLatestRequestGuard(): LatestRequestGuard {
  let latestRequestId = 0

  return {
    begin() {
      latestRequestId += 1
      return latestRequestId
    },
    isLatest(requestId) {
      return latestRequestId === requestId
    },
    invalidate() {
      latestRequestId += 1
    },
  }
}

export interface RequestInvalidationRef {
  controller: { current?: AbortController | null }
  guard: { current: LatestRequestGuard }
}

// 提交入口在清空状态前同步作废旧请求：先 abort 控制器，再让 guard 失效并清空引用，
// 避免 React effect cleanup 尚未运行时旧 Promise 通过 isLatest 回填过期数据。
export function invalidateRequests(refs: readonly RequestInvalidationRef[]): void {
  refs.forEach((ref) => {
    ref.controller.current?.abort()
    ref.controller.current = undefined
    ref.guard.current.invalidate()
  })
}

export interface ProductSalesAnalysisViewState {
  selection: ProductSalesAnalysisSelection
  summaryPage: number
  currentProductCode: string | null
  middleView: 'summary' | 'daily'
  selectedBranchCode: string | null
}

export type ProductSalesAnalysisViewAction =
  | { type: 'commitSelection'; selection: ProductSalesAnalysisSelection }
  | { type: 'commitFilter'; selection: ProductSalesAnalysisSelection }
  | { type: 'settleCurrentProduct'; summaryItems: readonly ProductSalesSummaryRow[] }
  | { type: 'setSummaryPage'; page: number }
  | { type: 'setCurrentProduct'; productCode: string }
  | { type: 'setMiddleView'; view: 'summary' | 'daily' }
  | { type: 'setSelectedBranch'; branchCode: string | null }

export function createProductSalesAnalysisViewState(
  selection: ProductSalesAnalysisSelection,
): ProductSalesAnalysisViewState {
  return {
    selection,
    summaryPage: 1,
    currentProductCode: null,
    middleView: 'summary',
    selectedBranchCode: null,
  }
}

function resetProductSalesAnalysisDrilldown(
  selection: ProductSalesAnalysisSelection,
): ProductSalesAnalysisViewState {
  return {
    selection,
    summaryPage: 1,
    currentProductCode: null,
    middleView: 'summary',
    selectedBranchCode: null,
  }
}

export function productSalesAnalysisViewReducer(
  state: ProductSalesAnalysisViewState,
  action: ProductSalesAnalysisViewAction,
): ProductSalesAnalysisViewState {
  switch (action.type) {
    case 'commitSelection':
    case 'commitFilter':
      return resetProductSalesAnalysisDrilldown(action.selection)
    case 'settleCurrentProduct': {
      if (isProductSelected(state.selection, state.currentProductCode)) {
        return state
      }

      const currentProductCode = resolveCurrentProductCode(
        null,
        action.summaryItems.filter((row) => isProductSelected(state.selection, row.productCode)),
      )
      return currentProductCode === state.currentProductCode
        ? state
        : { ...state, currentProductCode }
    }
    case 'setSummaryPage':
      return { ...state, summaryPage: action.page }
    case 'setCurrentProduct':
      return { ...state, currentProductCode: action.productCode }
    case 'setMiddleView':
      return { ...state, middleView: action.view }
    case 'setSelectedBranch':
      return { ...state, selectedBranchCode: action.branchCode }
    default:
      return state
  }
}

export function applyCandidateSelect(
  selection: ProductSalesAnalysisSelection,
  productCode: string,
  checked: boolean,
): ProductSalesAnalysisSelection {
  if (selection.mode === 'allFiltered') {
    return toggleExcludedProduct(selection, productCode, checked)
  }

  const nextCodes = checked
    ? Array.from(new Set([...selection.includedProductCodes, productCode]))
    : selection.includedProductCodes.filter((code) => code !== productCode)
  return createIncludedSelection(nextCodes)
}

export function applyCandidateSelectAll(
  selection: ProductSalesAnalysisSelection,
  changeCodes: readonly string[],
  checked: boolean,
): ProductSalesAnalysisSelection {
  if (selection.mode === 'allFiltered') {
    return changeCodes.reduce(
      (next, code) => toggleExcludedProduct(next, code, checked),
      selection,
    )
  }

  const nextCodes = checked
    ? Array.from(new Set([...selection.includedProductCodes, ...changeCodes]))
    : selection.includedProductCodes.filter((code) => !changeCodes.includes(code))
  return createIncludedSelection(nextCodes)
}

const audFormatter = new Intl.NumberFormat('en-AU', {
  style: 'currency',
  currency: 'AUD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

export function formatAud(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return '—'
  }

  return audFormatter.format(value)
}

export const MAX_X_AXIS_TICKS = 6

export function buildXAxisTickIndices(
  count: number,
  maxTicks: number = MAX_X_AXIS_TICKS,
): number[] {
  if (!Number.isFinite(count) || count <= 0) {
    return []
  }

  if (count <= maxTicks) {
    return Array.from({ length: count }, (_, index) => index)
  }

  const lastIndex = count - 1
  const step = lastIndex / (maxTicks - 1)
  const indices = new Set<number>()
  for (let tickIndex = 0; tickIndex < maxTicks; tickIndex += 1) {
    indices.add(Math.round(tickIndex * step))
  }
  return [...indices].sort((left, right) => left - right)
}

export function buildDailyChartModel(
  points: readonly DailyChartInputPoint[],
  width: number,
  height: number,
  padding: DailyChartPadding,
): DailyChartModel {
  const plotWidth = Math.max(0, width - padding.left - padding.right)
  const plotHeight = Math.max(0, height - padding.top - padding.bottom)
  const quantities = points.map((point) => point.quantity)
  const averages = points
    .map((point) => point.averageUnitPrice)
    .filter((value): value is number => value != null && Number.isFinite(value))

  const minValue = Math.min(0, ...quantities)
  const maxValue = Math.max(0, ...quantities)
  const valueRange = maxValue - minValue || 1
  const zeroY = padding.top + ((maxValue - 0) / valueRange) * plotHeight
  const averageMin = averages.length ? Math.min(...averages) : 0
  const averageMax = averages.length ? Math.max(...averages) : 0
  const averageRange = averageMax - averageMin

  const getX = (index: number) => {
    const step = points.length > 1 ? plotWidth / (points.length - 1) : 0
    return padding.left + (points.length > 1 ? index * step : plotWidth / 2)
  }

  const getQuantityY = (value: number) => padding.top + ((maxValue - value) / valueRange) * plotHeight
  const getAverageY = (value: number) => averageRange === 0
    ? padding.top + plotHeight / 2
    : padding.top + ((averageMax - value) / averageRange) * plotHeight
  const barWidth = Math.min(28, points.length > 1 ? plotWidth / (points.length - 1) * 0.62 : 20)

  const bars = points.map((point, index) => {
    const centerX = getX(index)
    const valueY = getQuantityY(point.quantity)
    const top = Math.min(zeroY, valueY)
    return {
      date: point.date,
      quantity: point.quantity,
      x: centerX - barWidth / 2,
      y: top,
      width: barWidth,
      height: Math.max(0, Math.abs(valueY - zeroY)),
    }
  })

  const averagePoints = points
    .map((point, index) => ({ point, index }))
    .filter(({ point }) => point.averageUnitPrice != null && Number.isFinite(point.averageUnitPrice))
    .map(({ point, index }) => {
      return {
        index,
        date: point.date,
        averageUnitPrice: point.averageUnitPrice as number,
        x: getX(index),
        y: getAverageY(point.averageUnitPrice as number),
      }
    })

  const averageSegments = averagePoints.reduce<DailyChartAveragePoint[][]>((segments, point) => {
    const current = segments[segments.length - 1]
    if (!current || current[current.length - 1]!.index + 1 !== point.index) {
      segments.push([point])
    } else {
      current.push(point)
    }
    return segments
  }, [])

  const xAxisTicks = buildXAxisTickIndices(points.length).map((index) => ({
    index,
    date: points[index]!.date,
    x: getX(index),
  }))

  return {
    width,
    height,
    plot: padding,
    zeroY,
    minValue,
    maxValue,
    averageMin: averages.length ? averageMin : null,
    averageMax: averages.length ? averageMax : null,
    bars,
    averagePoints,
    averageSegments,
    xAxisTicks,
  }
}

export function formatSupplierNames(suppliers: readonly ProductSalesAnalysisSupplier[]): string {
  return suppliers.map((supplier) => supplier.name || supplier.code).join('、') || '—'
}
