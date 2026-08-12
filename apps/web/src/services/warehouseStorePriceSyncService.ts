import type { ApiResponse } from '../types/api'
import request, { unwrapApiData } from '../utils/request'
import {
  createHqSyncJobPoller,
  HqProductSyncPollingCancelledError,
  HqProductSyncPollingTimeoutError,
  type HqProductSyncPollingOptions,
} from './productHqSyncPolling'
import {
  isWarehouseStorePriceSyncTerminalStatus,
  normalizeWarehouseStorePriceSyncErrors,
  type WarehouseStorePriceSyncJob,
  type WarehouseStorePriceSyncJobStatus,
  type WarehouseStorePriceSyncPayload,
  type WarehouseStorePriceSyncResult,
  type WarehouseStorePriceSyncTargetStore,
} from '../pages/Warehouse/Products/warehouseStorePriceSync.logic'

const API_BASE = '/api/react/v1/product-warehouse/store-price-sync'
export const WAREHOUSE_STORE_PRICE_SYNC_POLL_TIMEOUT_MS = 35 * 60 * 1000

export type {
  WarehouseStorePriceSyncJob,
  WarehouseStorePriceSyncJobStatus,
  WarehouseStorePriceSyncPayload,
  WarehouseStorePriceSyncResult,
  WarehouseStorePriceSyncTargetStore,
}
export {
  HqProductSyncPollingCancelledError,
  HqProductSyncPollingTimeoutError,
}

function readRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function readString(...values: unknown[]) {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim()
  }
  return ''
}

function normalizeTargetStores(raw: unknown): WarehouseStorePriceSyncTargetStore[] {
  if (!Array.isArray(raw)) return []

  const seen = new Set<string>()
  return raw.reduce<WarehouseStorePriceSyncTargetStore[]>((result, item) => {
    const record = readRecord(item)
    const storeCode = readString(record.storeCode, record.StoreCode)
    if (!storeCode || seen.has(storeCode.toLowerCase())) return result

    seen.add(storeCode.toLowerCase())
    result.push({
      storeCode,
      storeName: readString(record.storeName, record.StoreName) || storeCode,
    })
    return result
  }, [])
}

const JOB_STATUSES: readonly WarehouseStorePriceSyncJobStatus[] = [
  'Pending',
  'Running',
  'Succeeded',
  'PartiallySucceeded',
  'Failed',
]

function normalizeJobStatus(value: unknown): WarehouseStorePriceSyncJobStatus {
  if (JOB_STATUSES.includes(value as WarehouseStorePriceSyncJobStatus)) {
    return value as WarehouseStorePriceSyncJobStatus
  }

  throw new Error(`未知的仓库价格同步任务状态: ${String(value ?? '')}`)
}

function normalizeResult(raw: unknown): Partial<WarehouseStorePriceSyncResult> | undefined {
  const record = readRecord(raw)
  if (!Object.keys(record).length) return undefined
  return {
    requestedProductCount: Number(record.requestedProductCount ?? 0),
    eligibleProductCount: Number(record.eligibleProductCount ?? 0),
    skippedProductCount: Number(record.skippedProductCount ?? 0),
    targetStoreCount: Number(record.targetStoreCount ?? 0),
    localCreatedCount: Number(record.localCreatedCount ?? 0),
    localUpdatedCount: Number(record.localUpdatedCount ?? 0),
    hqCreatedCount: Number(record.hqCreatedCount ?? 0),
    hqUpdatedCount: Number(record.hqUpdatedCount ?? 0),
    hqProvisionedProductCount: Number(record.hqProvisionedProductCount ?? 0),
    errors: normalizeWarehouseStorePriceSyncErrors(record.errors ?? record.Errors),
  }
}

function normalizeJob(raw: unknown, fallbackJobId = ''): WarehouseStorePriceSyncJob {
  const record = readRecord(raw)
  return {
    jobId: readString(record.jobId, record.JobId) || fallbackJobId,
    status: normalizeJobStatus(record.status ?? record.Status),
    isDuplicateRequest: record.isDuplicateRequest === true || record.IsDuplicateRequest === true,
    createdAt: readString(record.createdAt, record.CreatedAt) || undefined,
    completedAt: readString(record.completedAt, record.CompletedAt) || undefined,
    message: readString(record.message, record.Message) || undefined,
    result: normalizeResult(record.result ?? record.Result),
  }
}

export async function getWarehouseStorePriceSyncTargetStores() {
  const response = await request.get<ApiResponse<unknown>>(`${API_BASE}/target-stores`)
  return normalizeTargetStores(unwrapApiData(response))
}

export async function createWarehouseStorePriceSyncJob(
  payload: WarehouseStorePriceSyncPayload,
): Promise<WarehouseStorePriceSyncJob> {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/jobs`, payload)
  return normalizeJob(unwrapApiData(response))
}

export async function getWarehouseStorePriceSyncJob(
  jobId: string,
): Promise<WarehouseStorePriceSyncJob> {
  const response = await request.get<ApiResponse<unknown>>(
    `${API_BASE}/jobs/${encodeURIComponent(jobId)}`,
  )
  return normalizeJob(unwrapApiData(response), jobId)
}

export async function getAllWarehouseProductCount() {
  const response = await request.get<ApiResponse<unknown>>(`${API_BASE}/product-count`)
  const count = Number(unwrapApiData(response))
  if (!Number.isInteger(count) || count < 0) {
    throw new Error('仓库商品总数响应无效')
  }
  return count
}

export function createWarehouseStorePriceSyncJobPoller({
  jobId,
  getJob,
  ...options
}: HqProductSyncPollingOptions & {
  jobId: string
  getJob: (jobId: string) => Promise<WarehouseStorePriceSyncJob>
}) {
  const { timeoutMs = WAREHOUSE_STORE_PRICE_SYNC_POLL_TIMEOUT_MS, ...pollingOptions } = options
  return createHqSyncJobPoller<WarehouseStorePriceSyncJob>({
    jobId,
    getJob,
    isTerminalStatus: isWarehouseStorePriceSyncTerminalStatus,
    ...pollingOptions,
    // 分店价格更新允许后台批量任务运行更久，但不改变共享 poller 的默认超时。
    timeoutMs,
  })
}
