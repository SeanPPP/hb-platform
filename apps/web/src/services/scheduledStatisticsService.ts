import type { ApiResponse } from '../types/api'
import request, { RequestError } from '../utils/request'

const API_BASE = '/api/StatisticsJobTrigger'

export interface DateTaskRequest {
  date: string
}

export interface StoreStatisticsRequest extends DateTaskRequest {
  branchCodes?: string[]
}

export interface SupplierStatisticsRequest extends DateTaskRequest {
  supplierCodes?: string[]
}

export interface StoreSupplierStatisticsRequest extends DateTaskRequest {
  branchCodes?: string[]
  supplierCodes?: string[]
}

export interface DateRangeTaskRequest {
  startDate: string
  endDate: string
}

export interface BatchStoreStatisticsRequest extends DateRangeTaskRequest {
  branchCodes?: string[]
}

export interface BatchSupplierStatisticsRequest extends DateRangeTaskRequest {
  supplierCodes?: string[]
}

export interface BatchStoreSupplierStatisticsRequest extends DateRangeTaskRequest {
  branchCodes?: string[]
  supplierCodes?: string[]
}

export interface BatchHourlyStatisticsRequest extends DateRangeTaskRequest {
  hour?: number | null
}

export interface BatchFullRefreshConcurrentRequest extends DateRangeTaskRequest {
  maxConcurrency: number
}

export interface ScheduledStatisticsJobResult {
  success?: boolean
  message?: string
  jobId?: string
  date?: string
  branches?: string[]
  suppliers?: string[]
  totalDays?: number
  processedDays?: number
  failedDates?: string[]
  skippedDates?: string[]
}

function normalizeResult(payload: ApiResponse<ScheduledStatisticsJobResult> | ScheduledStatisticsJobResult) {
  if (payload && typeof payload === 'object' && 'data' in payload) {
    return (payload as ApiResponse<ScheduledStatisticsJobResult>).data as ScheduledStatisticsJobResult
  }
  return payload as ScheduledStatisticsJobResult
}

function postJob(path: string, payload: object = {}) {
  return request
    .post<ApiResponse<ScheduledStatisticsJobResult> | ScheduledStatisticsJobResult>(
      `${API_BASE}${path}`,
      payload,
    )
    .then(normalizeResult)
}

export function triggerStoreStatistics(payload: StoreStatisticsRequest) {
  return postJob('/trigger-store', payload)
}

export function batchUpdateStoreStatistics(payload: BatchStoreStatisticsRequest) {
  return postJob('/batch-update-store', payload)
}

export function triggerSupplierStatistics(payload: SupplierStatisticsRequest) {
  return postJob('/trigger-supplier', payload)
}

export function batchUpdateSupplierStatistics(payload: BatchSupplierStatisticsRequest) {
  return postJob('/batch-update-supplier', payload)
}

export function triggerStoreSupplierStatistics(payload: StoreSupplierStatisticsRequest) {
  return postJob('/trigger-store-supplier', payload)
}

export function batchUpdateStoreSupplierStatistics(payload: BatchStoreSupplierStatisticsRequest) {
  return postJob('/batch-update-store-supplier', payload)
}

export function triggerDailyStatistics(payload: DateTaskRequest) {
  return postJob('/trigger-daily', payload)
}

export function batchUpdateDailyStatistics(payload: DateRangeTaskRequest) {
  return postJob('/batch-update-daily', payload)
}

export function batchUpdateHourlyStatistics(payload: BatchHourlyStatisticsRequest) {
  return postJob('/batch-update-hourly', payload)
}

export function triggerFullRefreshCurrentDay() {
  return postJob('/trigger-full-refresh-current-day')
}

export function triggerFullRefreshPreviousAndCurrentDay() {
  return postJob('/trigger-full-refresh')
}

export function batchFullRefreshConcurrent(payload: BatchFullRefreshConcurrentRequest) {
  return postJob('/batch-full-refresh-concurrent', payload)
}

export function isScheduledStatisticsJobFailure(result: ScheduledStatisticsJobResult) {
  return result.success === false
}

export function getScheduledStatisticsActionErrorMessage(error: unknown) {
  if (
    error instanceof RequestError &&
    error.payload &&
    typeof error.payload === 'object' &&
    'message' in error.payload &&
    typeof error.payload.message === 'string' &&
    error.payload.message.trim()
  ) {
    return error.payload.message
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message
  }

  return '统计任务触发失败'
}
