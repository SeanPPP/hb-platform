import type { ApiResponse } from '../types/api'
import request from '../utils/request'

export type SalesStatisticStatus = 'Queued' | 'Running' | 'Pending' | 'Fresh' | 'Stale' | 'Failed'

export interface SalesStatisticStateQuery {
  statisticType?: string
  startDate?: string
  endDate?: string
  status?: SalesStatisticStatus | ''
}

export interface SalesStatisticRefreshState {
  statisticType: string
  date: string
  status: SalesStatisticStatus | string
  lastSourceUploadTime?: string
  sourceTimeZone?: string
  lastAggregatedAtUtc?: string
  lastCheckedAtUtc?: string
  errorMessage?: string
  jobId?: string
  requestedAtUtc?: string
  startedAtUtc?: string
  completedAtUtc?: string
}

export interface ProductStoreDailyStatisticSummary extends SalesStatisticRefreshState {
  recordCount: number
  totalQuantity: number
  totalAmount: number
  grossProfit?: number | null
  reconciliationStatus?: string
  salesReconciliationStatus?: string
  productTotalAmount?: number
  storeTotalAmount?: number
  amountDifference?: number
  productTotalQuantity?: number
  storeTotalQuantity?: number
  quantityDifference?: number
  unmatchedSupplierAmount?: number
  unmatchedSupplierQuantity?: number
  unmatchedSupplierProductCount?: number
}

export interface JobTriggerResponse {
  success?: boolean
  message?: string
  jobId?: string
  status?: string
  submittedDates?: string[]
  skippedDates?: string[]
}

const PRODUCT_STORE_DAILY_BASE = '/api/StatisticsJobTrigger/product-store-daily'

function readString(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function readNumber(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readNullableNumber(value: unknown) {
  if (value === null) {
    return null
  }

  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function unwrapData<T>(payload: ApiResponse<T> | T): T {
  if (payload && typeof payload === 'object' && 'data' in payload) {
    return (payload as ApiResponse<T>).data as T
  }

  return payload as T
}

function normalizeState(raw: unknown): SalesStatisticRefreshState | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const date = readString(record.date ?? record.Date)
  const statisticType = readString(record.statisticType ?? record.StatisticType) ?? 'ProductStoreDaily'

  if (!date) {
    return null
  }

  return {
    statisticType,
    date,
    status: readString(record.status ?? record.Status) ?? 'Pending',
    lastSourceUploadTime: readString(record.lastSourceUploadTime ?? record.LastSourceUploadTime),
    sourceTimeZone: readString(record.sourceTimeZone ?? record.SourceTimeZone),
    lastAggregatedAtUtc: readString(record.lastAggregatedAtUtc ?? record.LastAggregatedAtUtc),
    lastCheckedAtUtc: readString(record.lastCheckedAtUtc ?? record.LastCheckedAtUtc),
    errorMessage: readString(record.errorMessage ?? record.ErrorMessage),
    jobId: readString(record.jobId ?? record.JobId),
    requestedAtUtc: readString(record.requestedAtUtc ?? record.RequestedAtUtc),
    startedAtUtc: readString(record.startedAtUtc ?? record.StartedAtUtc),
    completedAtUtc: readString(record.completedAtUtc ?? record.CompletedAtUtc),
  }
}

function normalizeSubmitResponse(raw: unknown): JobTriggerResponse {
  const record = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>
  const submittedDates = record.submittedDates ?? record.SubmittedDates
  const skippedDates = record.skippedDates ?? record.SkippedDates

  return {
    success: typeof (record.success ?? record.Success) === 'boolean'
      ? (record.success ?? record.Success) as boolean
      : undefined,
    message: readString(record.message ?? record.Message),
    jobId: readString(record.jobId ?? record.JobId),
    status: readString(record.status ?? record.Status),
    submittedDates: Array.isArray(submittedDates)
      ? submittedDates.map(readString).filter((item): item is string => Boolean(item))
      : [],
    skippedDates: Array.isArray(skippedDates)
      ? skippedDates.map(readString).filter((item): item is string => Boolean(item))
      : [],
  }
}

function normalizeSummary(raw: unknown): ProductStoreDailyStatisticSummary {
  const base = normalizeState(raw) ?? {
    statisticType: 'ProductStoreDaily',
    date: '',
    status: 'Pending',
  }
  const record = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>
  const grossProfitValue = Object.prototype.hasOwnProperty.call(record, 'grossProfit')
    ? record.grossProfit
    : record.GrossProfit

  return {
    ...base,
    recordCount: readNumber(record.recordCount ?? record.RecordCount),
    totalQuantity: readNumber(record.totalQuantity ?? record.TotalQuantity),
    totalAmount: readNumber(record.totalAmount ?? record.TotalAmount),
    grossProfit: readNullableNumber(grossProfitValue),
    reconciliationStatus: readString(record.reconciliationStatus ?? record.ReconciliationStatus),
    salesReconciliationStatus: readString(record.salesReconciliationStatus ?? record.SalesReconciliationStatus),
    productTotalAmount: readNullableNumber(record.productTotalAmount ?? record.ProductTotalAmount),
    storeTotalAmount: readNullableNumber(record.storeTotalAmount ?? record.StoreTotalAmount),
    amountDifference: readNullableNumber(record.amountDifference ?? record.AmountDifference),
    productTotalQuantity: readNullableNumber(record.productTotalQuantity ?? record.ProductTotalQuantity),
    storeTotalQuantity: readNullableNumber(record.storeTotalQuantity ?? record.StoreTotalQuantity),
    quantityDifference: readNullableNumber(record.quantityDifference ?? record.QuantityDifference),
    unmatchedSupplierAmount: readNullableNumber(record.unmatchedSupplierAmount ?? record.UnmatchedSupplierAmount),
    unmatchedSupplierQuantity: readNullableNumber(record.unmatchedSupplierQuantity ?? record.UnmatchedSupplierQuantity),
    unmatchedSupplierProductCount: readNullableNumber(record.unmatchedSupplierProductCount ?? record.UnmatchedSupplierProductCount),
  }
}

export async function getProductStoreDailyStatisticStates(query: SalesStatisticStateQuery = {}) {
  const response = await request<ApiResponse<unknown[]> | unknown[]>(`${PRODUCT_STORE_DAILY_BASE}/states`, {
    method: 'GET',
    params: {
      statisticType: query.statisticType ?? 'ProductStoreDaily',
      startDate: query.startDate,
      endDate: query.endDate,
      status: query.status,
    },
  })

  const rows = unwrapData(response)
  return Array.isArray(rows)
    ? rows.map(normalizeState).filter((item): item is SalesStatisticRefreshState => item !== null)
    : []
}

export async function getProductStoreDailyStatisticSummary(date: string) {
  const response = await request<ApiResponse<unknown> | unknown>(`${PRODUCT_STORE_DAILY_BASE}/${date}/summary`, {
    method: 'GET',
  })

  return normalizeSummary(unwrapData(response))
}

export async function recalculateProductStoreDaily(date: string) {
  const response = await request.post<ApiResponse<JobTriggerResponse> | JobTriggerResponse>(
    '/api/StatisticsJobTrigger/trigger-product-store-daily',
    { date },
  )
  return normalizeSubmitResponse(unwrapData(response))
}

export async function recalculateRecentProductStoreDaily(days = 7) {
  const response = await request.post<ApiResponse<JobTriggerResponse> | JobTriggerResponse>(
    '/api/StatisticsJobTrigger/recent-product-store-daily',
    { days },
  )
  return normalizeSubmitResponse(unwrapData(response))
}

export async function recalculateProductStoreDailyRange(startDate: string, endDate: string) {
  const response = await request.post<ApiResponse<JobTriggerResponse> | JobTriggerResponse>(
    '/api/StatisticsJobTrigger/batch-product-store-daily',
    { startDate, endDate },
  )
  return normalizeSubmitResponse(unwrapData(response))
}
