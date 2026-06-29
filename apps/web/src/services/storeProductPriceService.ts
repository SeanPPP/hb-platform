import type { ApiResponse, PagedResult } from '../types/api'
import type {
  BatchUpdateStoreRetailPriceDto,
  CopyProgressDto,
  CopyStoreDataDto,
  CopyStoreDataResultDto,
  StoreProductPriceListDto,
  StoreProductPriceQueryDto,
  StorePriceTransferJobDto,
  StorePriceTransferRequest,
  StorePriceTransferResult,
  SyncFromHqRequest,
  SyncFromHqResult,
  SyncToOtherStoresDto,
} from '../types/storeProductPrice'
import request, { RequestError, unwrapApiData, unwrapPagedResult } from '../utils/request'

const API_BASE = '/api/react/v1/store-product-prices'

function assertApiSuccess<T>(response: ApiResponse<T>, fallbackMessage: string): void {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function readString(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = source[key]
    if (typeof value === 'string') return value
  }
  return undefined
}

function readBoolean(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = source[key]
    if (typeof value === 'boolean') return value
  }
  return undefined
}

function readNumber(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = source[key]
    if (typeof value === 'number') return value
  }
  return 0
}

function readStringArray(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = source[key]
    if (Array.isArray(value)) {
      return value.filter((item): item is string => typeof item === 'string')
    }
  }
  return []
}

function normalizeStorePriceTransferStatus(status: unknown, payload: unknown) {
  if (typeof status !== 'string') {
    // 后端任务 DTO 必须返回明确状态，缺失时直接暴露契约错误，避免页面轮询到超时。
    throw new RequestError('分店价格同步任务缺少状态', 200, payload)
  }

  switch (status.trim().toLowerCase()) {
    case 'running':
    case 'queued':
    case 'pending':
      return 'Running'
    case 'succeeded':
    case 'success':
    case 'completed':
      return 'Succeeded'
    case 'failed':
    case 'failure':
    case 'error':
      return 'Failed'
    default:
      // 未知状态必须显式暴露，避免前端误以为任务仍在执行中。
      throw new RequestError(`未知分店价格同步任务状态: ${status}`, 200, payload)
  }
}

function normalizeStorePriceTransferResult(value: unknown): StorePriceTransferResult {
  const raw = isRecord(value) ? value : {}
  return {
    totalProcessed: readNumber(raw, 'totalProcessed', 'TotalProcessed'),
    insertedCount: readNumber(raw, 'insertedCount', 'InsertedCount'),
    updatedCount: readNumber(raw, 'updatedCount', 'UpdatedCount'),
    skippedCount: readNumber(raw, 'skippedCount', 'SkippedCount'),
    failedCount: readNumber(raw, 'failedCount', 'FailedCount'),
    retailPriceInserted: readNumber(raw, 'retailPriceInserted', 'RetailPriceInserted'),
    retailPriceUpdated: readNumber(raw, 'retailPriceUpdated', 'RetailPriceUpdated'),
    retailPriceSkipped: readNumber(raw, 'retailPriceSkipped', 'RetailPriceSkipped'),
    multiCodeInserted: readNumber(raw, 'multiCodeInserted', 'MultiCodeInserted'),
    multiCodeUpdated: readNumber(raw, 'multiCodeUpdated', 'MultiCodeUpdated'),
    multiCodeSkipped: readNumber(raw, 'multiCodeSkipped', 'MultiCodeSkipped'),
    errors: readStringArray(raw, 'errors', 'Errors'),
  }
}

function normalizeStorePriceTransferJob(value: unknown, fallbackJobId = ''): StorePriceTransferJobDto {
  const data = unwrapApiData(value as ApiResponse<unknown> | unknown)
  const raw = isRecord(data) ? data : {}
  const resultValue = raw.result ?? raw.Result
  const errors = readStringArray(raw, 'errors', 'Errors')
  return {
    jobId: readString(raw, 'jobId', 'JobId') || fallbackJobId,
    operationId: readString(raw, 'operationId', 'OperationId'),
    status: normalizeStorePriceTransferStatus(raw.status ?? raw.Status, raw),
    isDuplicateRequest: readBoolean(raw, 'isDuplicateRequest', 'IsDuplicateRequest'),
    request: (raw.request ?? raw.Request) as StorePriceTransferRequest | undefined,
    result: isRecord(resultValue) ? normalizeStorePriceTransferResult(resultValue) : undefined,
    message: readString(raw, 'message', 'Message'),
    errors: errors.length > 0 ? errors : isRecord(resultValue) ? readStringArray(resultValue, 'errors', 'Errors') : [],
    createdAt: readString(raw, 'createdAt', 'CreatedAt'),
    startedAt: readString(raw, 'startedAt', 'StartedAt'),
    completedAt: readString(raw, 'completedAt', 'CompletedAt'),
    expiresAt: readString(raw, 'expiresAt', 'ExpiresAt'),
  }
}

export async function getStoreProductPriceGrid(data: StoreProductPriceQueryDto) {
  const response = await request.post<ApiResponse<PagedResult<StoreProductPriceListDto>> | PagedResult<StoreProductPriceListDto>>(
    `${API_BASE}/grid`,
    data,
  )
  return unwrapPagedResult(response)
}

export async function batchUpdateStoreRetailPrices(data: BatchUpdateStoreRetailPriceDto): Promise<number> {
  const response = await request.post<ApiResponse<number>>(`${API_BASE}/batch-update`, data)
  return unwrapApiData(response)
}

export async function syncToOtherStores(data: SyncToOtherStoresDto): Promise<number> {
  const response = await request.post<ApiResponse<number>>(`${API_BASE}/sync-to-other-stores`, data)
  return unwrapApiData(response)
}

export async function copyStoreData(data: CopyStoreDataDto): Promise<CopyStoreDataResultDto> {
  const response = await request.post<ApiResponse<CopyStoreDataResultDto>>(`${API_BASE}/copy-store-data`, data)
  return unwrapApiData(response)
}

export function subscribeCopyProgress(
  params: {
    sourceStoreCode: string
    targetStoreCodes: string[]
    mode: string
    syncMultiCode: boolean
  },
  onProgress: (progress: CopyProgressDto) => void,
  onError: (error: Event) => void,
  onComplete: () => void,
): EventSource {
  const query = new URLSearchParams({
    sourceStoreCode: params.sourceStoreCode,
    targetStoreCodes: params.targetStoreCodes.join(','),
    mode: params.mode,
    syncMultiCode: String(params.syncMultiCode),
  })

  const baseUrl = (import.meta.env.VITE_API_BASE_URL || '').trim()
  const url = `${baseUrl}${API_BASE}/copy-store-data/stream?${query.toString()}`
  const eventSource = new EventSource(url, { withCredentials: true })

  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data) as CopyProgressDto
      onProgress(data)
    } catch {
      // ignore parse errors
    }
  }

  eventSource.onerror = (error) => {
    onError(error)
    eventSource.close()
  }

  const checkComplete = (event: MessageEvent) => {
    try {
      const data = JSON.parse(event.data) as CopyProgressDto
      if (data.eventType === 'completed' || data.eventType === 'error') {
        onComplete()
        eventSource.close()
      }
    } catch {
      // ignore
    }
  }
  eventSource.addEventListener('message', checkComplete)

  return eventSource
}

export async function syncFromHq(data: SyncFromHqRequest): Promise<SyncFromHqResult> {
  const response = await request.post<ApiResponse<SyncFromHqResult>>(`${API_BASE}/sync-from-hq`, data)

  // 这里显式校验业务成功标记，避免把失败响应误当成成功数据展示。
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || '从HQ同步失败', 200, response)
  }

  return unwrapApiData(response)
}

export async function startStorePriceTransferJob(
  data: StorePriceTransferRequest,
): Promise<StorePriceTransferJobDto> {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/store-price-transfer-jobs`, data)
  assertApiSuccess(response, '创建分店价格同步任务失败')
  return normalizeStorePriceTransferJob(response)
}

export async function getStorePriceTransferJob(jobId: string): Promise<StorePriceTransferJobDto> {
  const response = await request.get<ApiResponse<unknown>>(`${API_BASE}/store-price-transfer-jobs/${encodeURIComponent(jobId)}`)
  assertApiSuccess(response, '查询分店价格同步任务失败')
  return normalizeStorePriceTransferJob(response, jobId)
}
