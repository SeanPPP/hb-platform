import type {
  LocalSupplierProductSalesAnalysisBranch,
  LocalSupplierProductSalesAnalysisBootstrap,
  LocalSupplierProductSalesAnalysisCandidate,
  LocalSupplierProductSalesAnalysisDaily,
  LocalSupplierProductSalesAnalysisFilter,
  LocalSupplierProductSalesAnalysisInvoiceDetail,
  LocalSupplierProductSalesAnalysisOptions,
  LocalSupplierProductSalesAnalysisPaged,
  LocalSupplierProductSalesAnalysisRequest,
  LocalSupplierProductSalesAnalysisSectionErrors,
  LocalSupplierProductSalesAnalysisSelection,
  LocalSupplierProductSalesAnalysisSummary,
} from '../../../types/localSupplierProductSalesAnalysis'

export interface LocalProductSalesAnalysisDateRange { startDate: string; endDate: string }

/** 页面专用安全超时：bootstrap 与分段重试统一 8 秒。 */
export const PAGE_BOOTSTRAP_TIMEOUT_SECONDS = 8

export type LocalProductSalesAnalysisSectionKey = keyof LocalSupplierProductSalesAnalysisSectionErrors

/** 一次 bootstrap 响应原子提交后的页面数据快照。 */
export interface LocalProductSalesAnalysisBootstrapState {
  options: LocalSupplierProductSalesAnalysisOptions
  candidates: LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisCandidate> | null
  effectiveSelection: LocalSupplierProductSalesAnalysisSelection
  currentProduct: LocalSupplierProductSalesAnalysisCandidate | null
  summary: LocalSupplierProductSalesAnalysisSummary | null
  invoiceDetails: LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisInvoiceDetail> | null
  productDaily: LocalSupplierProductSalesAnalysisDaily[]
  branches: LocalSupplierProductSalesAnalysisBranch[]
  sectionErrors: LocalSupplierProductSalesAnalysisSectionErrors
  partial: boolean
}

export interface PageRequestTimeout {
  signal: AbortSignal
  clear: () => void
  abort: () => void
}

function brisbaneDateParts(now: Date) {
  const parts = new Intl.DateTimeFormat('en-AU', {
    timeZone: 'Australia/Brisbane', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now)
  const value = (type: string) => parts.find((part) => part.type === type)?.value ?? ''
  return { year: value('year'), month: value('month'), day: value('day') }
}

function formatUtcDate(date: Date) {
  return date.toISOString().slice(0, 10)
}

// 先按 Brisbane 取自然日，再回推，避免运行环境时区影响默认“昨天”。
export function buildBrisbaneDefaultRange(days: number, now = new Date()): LocalProductSalesAnalysisDateRange {
  const { year, month, day } = brisbaneDateParts(now)
  const end = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day) - 1))
  const start = new Date(end)
  start.setUTCDate(start.getUTCDate() - Math.max(1, days) + 1)
  return { startDate: formatUtcDate(start), endDate: formatUtcDate(end) }
}

function toUtcDay(value: string) {
  const date = new Date(`${value}T00:00:00.000Z`)
  return Number.isNaN(date.getTime()) ? null : date
}

export function getDateRangeError(startDate: string, endDate: string, brisbaneYesterday: string): string | undefined {
  const start = toUtcDay(startDate); const end = toUtcDay(endDate); const yesterday = toUtcDay(brisbaneYesterday)
  if (!start || !end || !yesterday || start > end) return '参数错误：开始日期不能晚于结束日期'
  if (end > yesterday) return '参数错误：日期范围截至 Brisbane 昨天'
  if ((end.getTime() - start.getTime()) / 86_400_000 > 365) return '参数错误：日期范围不能超过 366 天'
  return undefined
}

export function createIncludedSelection(productCodes: string[] = []): LocalSupplierProductSalesAnalysisSelection {
  return { mode: 'included', includedProductCodes: [...new Set(productCodes)], excludedProductCodes: [] }
}

export function isSelected(selection: LocalSupplierProductSalesAnalysisSelection, productCode: string) {
  return selection.mode === 'allFiltered'
    ? !selection.excludedProductCodes.includes(productCode)
    : selection.includedProductCodes.includes(productCode)
}

export function canSetCurrentProduct(selection: LocalSupplierProductSalesAnalysisSelection, productCode: string) {
  return isSelected(selection, productCode)
}

export function createLatestRequestGuard() {
  let version = 0
  return {
    next: () => ++version,
    isCurrent: (token: number) => token === version,
    invalidate: () => { version += 1 },
  }
}

export function applyCandidateSelection(
  selection: LocalSupplierProductSalesAnalysisSelection,
  productCode: string,
  checked: boolean,
): LocalSupplierProductSalesAnalysisSelection {
  if (selection.mode === 'allFiltered') {
    const excluded = new Set(selection.excludedProductCodes)
    if (checked) excluded.delete(productCode)
    else excluded.add(productCode)
    return { ...selection, excludedProductCodes: [...excluded] }
  }
  const included = new Set(selection.includedProductCodes)
  if (checked) included.add(productCode)
  else included.delete(productCode)
  return { ...selection, includedProductCodes: [...included] }
}

export function getCurrentProductAfterCancellation<T extends Pick<LocalSupplierProductSalesAnalysisCandidate, 'productCode'>>(
  currentProduct: T | null,
  summaryItems: T[],
  currentWasCancelled: boolean,
): T | null {
  if (!currentWasCancelled) return currentProduct
  return summaryItems[0] ?? null
}

export interface LocalProductSalesAnalysisBootstrapRequestContext {
  filter: LocalSupplierProductSalesAnalysisFilter
  selection?: LocalSupplierProductSalesAnalysisSelection
  currentProductCode?: string
  autoSelectFirst: boolean
  forceRefresh: boolean
  candidatePageNumber: number
  candidatePageSize: number
  summaryPageNumber: number
  summaryPageSize: number
}

/** 挂载/查询/重置（autoSelectFirst）与刷新（携带原选择/当前商品、forceRefresh）共用同一请求构造。 */
export function buildLocalProductSalesAnalysisBootstrapRequest(context: LocalProductSalesAnalysisBootstrapRequestContext): LocalSupplierProductSalesAnalysisRequest {
  return {
    filter: context.filter,
    ...(context.selection ? { selection: context.selection } : {}),
    currentProductCode: context.currentProductCode,
    autoSelectFirst: context.autoSelectFirst,
    forceRefresh: context.forceRefresh,
    candidatePageNumber: context.candidatePageNumber,
    candidatePageSize: context.candidatePageSize,
    summaryPageNumber: context.summaryPageNumber,
    summaryPageSize: context.summaryPageSize,
  }
}

/** 页面统一安全超时：超时中止为 AbortError；clear 用于成功后取消定时器。 */
export function createPageRequestTimeout(seconds = PAGE_BOOTSTRAP_TIMEOUT_SECONDS): PageRequestTimeout {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), Math.max(1, Math.round(seconds * 1000)))
  return {
    signal: controller.signal,
    clear: () => clearTimeout(timer),
    abort: () => { clearTimeout(timer); controller.abort() },
  }
}

export function createEmptyLocalProductSalesAnalysisState(): LocalProductSalesAnalysisBootstrapState {
  return {
    options: { warehouseCategories: [], suppliers: [] },
    candidates: null,
    effectiveSelection: createIncludedSelection(),
    currentProduct: null,
    summary: null,
    invoiceDetails: null,
    productDaily: [],
    branches: [],
    sectionErrors: {},
    partial: false,
  }
}

/** 切换商品/筛选只清空下游分段，保留候选与选项。 */
export function clearLocalProductSalesAnalysisDetailSections(state: LocalProductSalesAnalysisBootstrapState): LocalProductSalesAnalysisBootstrapState {
  return { ...state, summary: null, invoiceDetails: null, productDaily: [], branches: [], sectionErrors: {} }
}

/** 原子提交契约：一次调用替换全部数据分段；未提供的分段保留旧值。 */
export function applyLocalProductSalesAnalysisBootstrapResult(
  result: LocalSupplierProductSalesAnalysisBootstrap,
  previous: LocalProductSalesAnalysisBootstrapState,
): LocalProductSalesAnalysisBootstrapState {
  return {
    options: result.options,
    candidates: result.candidates,
    effectiveSelection: result.effectiveSelection ?? previous.effectiveSelection,
    currentProduct: result.currentProduct === undefined ? previous.currentProduct : result.currentProduct,
    summary: result.summary === undefined ? previous.summary : result.summary,
    invoiceDetails: result.invoiceDetails === undefined ? previous.invoiceDetails : result.invoiceDetails,
    productDaily: result.productDaily === undefined ? previous.productDaily : result.productDaily ?? [],
    branches: result.branches === undefined ? previous.branches : result.branches ?? [],
    sectionErrors: result.sectionErrors ?? {},
    partial: result.partial === true,
  }
}

function withoutSectionError(errors: LocalSupplierProductSalesAnalysisSectionErrors, key: LocalProductSalesAnalysisSectionKey): LocalSupplierProductSalesAnalysisSectionErrors {
  const next = { ...errors }
  delete next[key]
  return next
}

/** 分段重试成功后只替换目标分段并清除对应错误，不影响其它分段。 */
export function applyLocalProductSalesAnalysisSectionResult(
  state: LocalProductSalesAnalysisBootstrapState,
  key: LocalProductSalesAnalysisSectionKey,
  data: unknown,
): LocalProductSalesAnalysisBootstrapState {
  const next: LocalProductSalesAnalysisBootstrapState = { ...state }
  switch (key) {
    case 'options':
      next.options = data as LocalSupplierProductSalesAnalysisOptions
      break
    case 'summary':
      next.summary = data as LocalSupplierProductSalesAnalysisSummary | null
      break
    case 'invoiceDetails':
      next.invoiceDetails = data as LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisInvoiceDetail> | null
      break
    case 'productDaily':
      next.productDaily = data as LocalSupplierProductSalesAnalysisDaily[]
      break
    case 'branches':
      next.branches = data as LocalSupplierProductSalesAnalysisBranch[]
      break
  }
  return { ...next, sectionErrors: withoutSectionError(next.sectionErrors, key) }
}

export function setLocalProductSalesAnalysisSectionError(
  state: LocalProductSalesAnalysisBootstrapState,
  key: LocalProductSalesAnalysisSectionKey,
  message: string,
): LocalProductSalesAnalysisBootstrapState {
  return { ...state, sectionErrors: { ...state.sectionErrors, [key]: message } }
}

export function clearLocalProductSalesAnalysisSectionError(
  state: LocalProductSalesAnalysisBootstrapState,
  key: LocalProductSalesAnalysisSectionKey,
): LocalProductSalesAnalysisBootstrapState {
  return { ...state, sectionErrors: withoutSectionError(state.sectionErrors, key) }
}

export function formatAud(value: number | null | undefined) {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(value)
}

export function toFlowTrendData(data: Array<{ date: string; purchaseQuantity?: number; netSalesQuantity: number; netSalesAmount: number; averageUnitPrice: number | null }>) {
  return data.map((item) => ({
    date: item.date,
    inboundQuantity: item.purchaseQuantity ?? 0,
    shippedQuantity: 0,
    netSalesQuantity: item.netSalesQuantity,
    netSalesAmount: item.netSalesAmount,
    averageUnitPrice: item.averageUnitPrice,
  }))
}
