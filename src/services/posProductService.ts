import type { ApiResponse, PagedResult } from '../types/api'
import type {
  BatchUpdatePosProductDto,
  HqProductFullSyncJobRequest,
  HqProductIncrementalSyncRequest,
  HqProductIncrementalSyncJobRequest,
  HqProductSyncJobResult,
  HqProductSyncJobStatus,
  HqProductSyncResult,
  PosProductDto,
  PosProductFilterParams,
  ProductStoreRecordDto,
  PushProductsToHqRequest,
  PushProductsToHqResult,
  SyncSelectedProductsFromHqRequest,
  SyncProductsToStoresRequest,
  SyncProductsToStoresResult,
} from '../types/posProduct'
import request, { RequestError, unwrapApiData, unwrapPagedResult } from '../utils/request'
import {
  HqProductSyncPollingTimeoutError,
  createProductHqSyncJobPoller,
  type HqProductSyncPollingOptions,
} from './productHqSyncPolling'

const API_BASE = '/api/react/v1/products'
const SYNC_API_BASE = '/api/react/v1/sync'

export { HqProductSyncPollingTimeoutError, createProductHqSyncJobPoller }
export type { HqProductSyncPollingOptions }

const activeHqProductSyncJobs = new Map<string, Promise<HqProductSyncResult>>()

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function assertApiSuccess<T>(response: ApiResponse<T>, fallbackMessage: string): void {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }
}

function withOperationId<T extends { operationId?: string }>(data: T | undefined, prefix: string): T & { operationId: string } {
  return {
    ...data,
    // 缺省时使用稳定前缀，避免时间戳破坏后端幂等。
    operationId: data?.operationId || prefix,
  } as T & { operationId: string }
}

function pickDefinedHqProductSyncFields(raw: Record<string, unknown>): Partial<HqProductSyncResult> {
  const fields: Partial<HqProductSyncResult> = {}
  if (raw.productsAdded !== undefined || raw.addedCount !== undefined) {
    fields.productsAdded = Number(raw.productsAdded ?? raw.addedCount ?? 0)
  }
  if (raw.productsUpdated !== undefined || raw.updatedCount !== undefined) {
    fields.productsUpdated = Number(raw.productsUpdated ?? raw.updatedCount ?? 0)
  }
  if (raw.productsDeleted !== undefined || raw.productsSoftDeleted !== undefined || raw.deletedCount !== undefined) {
    fields.productsDeleted = Number(raw.productsDeleted ?? raw.productsSoftDeleted ?? raw.deletedCount ?? 0)
  }
  if (raw.productSetCodesCreated !== undefined || raw.productSetCodesAdded !== undefined) {
    fields.productSetCodesCreated = Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0)
  }
  if (raw.productSetCodesDeleted !== undefined || raw.productSetCodesSoftDeleted !== undefined) {
    fields.productSetCodesDeleted = Number(raw.productSetCodesDeleted ?? raw.productSetCodesSoftDeleted ?? 0)
  }

  const fieldNames: Array<keyof HqProductSyncResult> = [
    'addedCount',
    'updatedCount',
    'deletedCount',
    'totalCount',
    'errorCount',
    'totalHqProducts',
    'totalLocalProducts',
    'productsAdded',
    'productsUpdated',
    'productsDeleted',
    'productsSoftDeleted',
    'storeRetailPricesCreated',
    'storeRetailPricesDeleted',
    'productSetCodesCreated',
    'productSetCodesAdded',
    'productSetCodesUpdated',
    'productSetCodesDeleted',
    'productSetCodesSoftDeleted',
    'storeMultiCodesCreated',
    'storeMultiCodesDeleted',
    'durationMs',
  ]

  fieldNames.forEach((fieldName) => {
    if (raw[fieldName] !== undefined) {
      ;(fields as Record<string, unknown>)[fieldName] = raw[fieldName]
    }
  })

  return fields
}

function normalizePushProductsToHqResult(raw: PushProductsToHqResult): PushProductsToHqResult {
  // 后端统计字段可能逐步演进，前端统一兜底，保证结果弹窗始终可读。
  const payload = raw as PushProductsToHqResult & Record<string, unknown>
  const relationCount =
    Number(payload.productsAdded ?? 0) +
    Number(payload.productsUpdated ?? 0) +
    Number(payload.storeRetailPricesCreated ?? 0) +
    Number(payload.storeRetailPricesUpdated ?? 0) +
    Number(payload.productSetCodesCreated ?? payload.productSetCodesAdded ?? 0) +
    Number(payload.productSetCodesUpdated ?? 0) +
    Number(payload.storeMultiCodesCreated ?? 0) +
    Number(payload.storeMultiCodesUpdated ?? 0)
  const successCount = Number(payload.successCount ?? payload.pushedCount ?? payload.productsAdded ?? 0) +
    Number(payload.productsUpdated !== undefined && payload.successCount === undefined && payload.pushedCount === undefined ? payload.productsUpdated : 0)
  const failedCount = Number(payload.failedCount ?? payload.errorCount ?? 0)
  const totalCount = payload.totalCount === undefined ? successCount + failedCount : Number(payload.totalCount)

  return {
    ...raw,
    successCount,
    failedCount,
    totalCount,
    affectedRowCount: Number(payload.affectedRowCount ?? relationCount),
    errors: Array.isArray(raw.errors) ? raw.errors : [],
  }
}

export async function getProducts(params: PosProductFilterParams) {
  const sortOrderMap: Record<string, string> = { ascend: 'asc', descend: 'desc' }
  const payload: Record<string, unknown> = {
    pageNumber: params.pageIndex,
    pageSize: params.pageSize,
    search: params.keyword || undefined,
    localSupplierCode: params.supplierCode || undefined,
    productCategoryGUIDs: params.categoryGuid ? [params.categoryGuid] : undefined,
    isActive: params.isActive,
    isSet: params.isSet,
    sortBy: params.sortBy || undefined,
    sortOrder: params.sortOrder ? sortOrderMap[params.sortOrder] || params.sortOrder : undefined,
  }
  const response = await request.post<ApiResponse<PagedResult<PosProductDto>> | PagedResult<PosProductDto> | PosProductDto[]>(
    `${API_BASE}/list`,
    payload,
  )

  if (Array.isArray(response)) {
    return { items: response, total: response.length }
  }

  if (isRecord(response) && Array.isArray(response.data)) {
    return {
      items: response.data as PosProductDto[],
      total: Number(response.total ?? response.totalCount ?? response.data.length),
    }
  }

  const unwrapped = unwrapApiData(response)
  if (Array.isArray(unwrapped)) {
    return { items: unwrapped, total: unwrapped.length }
  }

  return unwrapPagedResult(unwrapped as PagedResult<PosProductDto>)
}

export { getProducts as getPosProducts }

export async function getProductById(productCode: string): Promise<PosProductDto> {
  const response = await request.get<ApiResponse<PosProductDto>>(`${API_BASE}/${productCode}`)
  return unwrapApiData(response)
}

export async function getProductByBarcode(barcode: string): Promise<PosProductDto> {
  const response = await request.get<ApiResponse<PosProductDto>>(`${API_BASE}/by-barcode/${barcode}`)
  return unwrapApiData(response)
}

export async function getProductStoreRecords(productCode: string): Promise<ProductStoreRecordDto[]> {
  const response = await request.get<ApiResponse<ProductStoreRecordDto[]>>(
    `${API_BASE}/${encodeURIComponent(productCode)}/store-records`,
  )
  return unwrapApiData(response) ?? []
}

export async function createProduct(data: Partial<PosProductDto> & Record<string, unknown>) {
  const response = await request.post<ApiResponse<PosProductDto>>(`${API_BASE}`, data)
  return unwrapApiData(response)
}

export async function updateProduct(productCode: string, data: Partial<PosProductDto>) {
  const response = await request.put<ApiResponse<PosProductDto>>(`${API_BASE}/${productCode}`, data)
  return unwrapApiData(response)
}

export async function batchUpdateProducts(items: BatchUpdatePosProductDto[]) {
  const response = await request.post<ApiResponse<{ successCount: number; failedCount: number; errors: string[] }>>(
    `${API_BASE}/batch-update`,
    { items },
  )
  return unwrapApiData(response)
}

export async function syncProductsToStores(syncRequest: SyncProductsToStoresRequest): Promise<SyncProductsToStoresResult> {
  const response = await request.post<ApiResponse<SyncProductsToStoresResult>>(
    `${API_BASE}/sync-to-stores`,
    syncRequest,
  )
  return unwrapApiData(response)
}

export async function pushProductsToHq(data: PushProductsToHqRequest): Promise<PushProductsToHqResult> {
  const response = await request.post<ApiResponse<PushProductsToHqResult>>(
    `${API_BASE}/push-to-hq`,
    data,
  )
  assertApiSuccess(response, '发送商品到 HQ 失败')
  return normalizePushProductsToHqResult(unwrapApiData(response))
}

function normalizeHqProductSyncResult(raw: HqProductSyncResult): HqProductSyncResult {
  const productsDeleted = raw.productsDeleted ?? raw.productsSoftDeleted ?? raw.deletedCount ?? 0
  const productSetCodesDeleted = raw.productSetCodesDeleted ?? raw.productSetCodesSoftDeleted ?? 0
  const productSetCodesCreated = raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0

  return {
    ...raw,
    productsAdded: raw.productsAdded ?? raw.addedCount ?? 0,
    productsUpdated: raw.productsUpdated ?? raw.updatedCount ?? 0,
    productsDeleted,
    productSetCodesCreated,
    productSetCodesDeleted,
    errors: raw.errors ?? [],
    durationMs: raw.durationMs ?? 0,
  }
}

function normalizeHqProductSyncJobStatus(
  status: unknown,
  success: unknown,
  payload: unknown,
): HqProductSyncJobStatus {
  if (typeof status === 'string') {
    switch (status.trim().toLowerCase()) {
      case 'queued':
      case 'pending':
        return 'Queued'
      case 'running':
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
        // 未知状态必须显式暴露，避免前端误判为仍在执行中。
        throw new RequestError(`未知同步任务状态: ${status}`, 200, payload)
    }
  }

  if (success === true) {
    return 'Succeeded'
  }

  if (success === false) {
    return 'Failed'
  }

  return 'Running'
}

function normalizeHqProductSyncJobResult(
  payload: unknown,
  fallbackJobId = '',
): HqProductSyncJobResult {
  const result = unwrapApiData(payload) as Record<string, unknown> | null
  const raw = isRecord(result) ? result : {}
  const nestedResult = isRecord(raw.result) ? raw.result : {}
  const success = typeof raw.success === 'boolean' ? raw.success : nestedResult.success
  const normalizedRawResult = pickDefinedHqProductSyncFields(raw)
  const normalizedNestedResult = isRecord(raw.result)
    ? normalizeHqProductSyncResult(nestedResult as HqProductSyncResult)
    : undefined

  return {
    ...normalizedRawResult,
    jobId: typeof raw.jobId === 'string' ? raw.jobId : fallbackJobId,
    status: normalizeHqProductSyncJobStatus(raw.status, success, raw),
    mode:
      raw.mode === 'Full' || raw.mode === 'Incremental'
        ? raw.mode
        : nestedResult.mode === 'Full' || nestedResult.mode === 'Incremental'
          ? nestedResult.mode
          : undefined,
    operationId: typeof raw.operationId === 'string' ? raw.operationId : undefined,
    success: typeof success === 'boolean' ? success : undefined,
    startDate: typeof raw.startDate === 'string' ? raw.startDate : undefined,
    result: normalizedNestedResult,
    message:
      typeof raw.message === 'string'
        ? raw.message
        : typeof nestedResult.message === 'string'
          ? nestedResult.message
          : undefined,
    errors: Array.isArray(raw.errors)
      ? raw.errors.filter((item): item is string => typeof item === 'string')
      : [],
  }
}

function buildHqProductSyncResult(job: HqProductSyncJobResult): HqProductSyncResult {
  if (job.result) {
    return normalizeHqProductSyncResult(job.result)
  }

  return normalizeHqProductSyncResult(job)
}

function resolveHqProductSyncJobResult(job: HqProductSyncJobResult): HqProductSyncResult {
  if (job.status === 'Succeeded') {
    return buildHqProductSyncResult(job)
  }

  if (job.status === 'Failed') {
    throw new RequestError(job.message || '商品同步任务执行失败', 200, job)
  }

  return buildHqProductSyncResult(job)
}

function normalizeUnknownStatusError(error: unknown): never {
  if (error instanceof RequestError && error.message.startsWith('未知同步任务状态')) {
    const status = error.message.split(':').slice(1).join(':').trim()
    throw new RequestError(`未知商品同步任务状态：${status}`, error.status, error.payload)
  }

  throw error
}

export function buildProductHqSyncOperationId(mode: 'full' | 'incremental', startDate?: string) {
  return `product-hq-sync:${mode}:${startDate || 'all'}`
}

export async function syncProductsFromHqFull(
  options?: HqProductSyncPollingOptions,
): Promise<HqProductSyncResult> {
  const operationId = buildProductHqSyncOperationId('full')
  const activeJob = activeHqProductSyncJobs.get(operationId)
  if (activeJob) {
    return activeJob
  }

  const jobPromise = (async () => {
    try {
      const job = await createProductHqSyncFullJob({ operationId })
      if (job.status === 'Queued' || job.status === 'Running') {
        const poller = createProductHqSyncJobPoller({
          jobId: job.jobId,
          getJob: getProductHqSyncJob,
          ...options,
        })
        return resolveHqProductSyncJobResult(await poller.promise)
      }
      return resolveHqProductSyncJobResult(job)
    } catch (error) {
      normalizeUnknownStatusError(error)
    } finally {
      activeHqProductSyncJobs.delete(operationId)
    }
  })()

  activeHqProductSyncJobs.set(operationId, jobPromise)
  return jobPromise
}

export async function syncProductsFromHqIncremental(
  data: HqProductIncrementalSyncRequest = {},
  options?: HqProductSyncPollingOptions,
): Promise<HqProductSyncResult> {
  const operationId = buildProductHqSyncOperationId('incremental', data.startDate)
  const activeJob = activeHqProductSyncJobs.get(operationId)
  if (activeJob) {
    return activeJob
  }

  const jobPromise = (async () => {
    try {
      const job = await createProductHqSyncIncrementalJob({ ...data, operationId })
      if (job.status === 'Queued' || job.status === 'Running') {
        const poller = createProductHqSyncJobPoller({
          jobId: job.jobId,
          getJob: getProductHqSyncJob,
          ...options,
        })
        return resolveHqProductSyncJobResult(await poller.promise)
      }
      return resolveHqProductSyncJobResult(job)
    } catch (error) {
      normalizeUnknownStatusError(error)
    } finally {
      activeHqProductSyncJobs.delete(operationId)
    }
  })()

  activeHqProductSyncJobs.set(operationId, jobPromise)
  return jobPromise
}

export async function syncProductsFromHq(): Promise<HqProductSyncResult> {
  const response = await request.post<ApiResponse<HqProductSyncResult>>(`${API_BASE}/sync-from-hq`)
  assertApiSuccess(response, 'HQ 商品同步失败')
  return normalizeHqProductSyncResult(unwrapApiData(response))
}

export async function syncSelectedProductsFromHq(
  data: SyncSelectedProductsFromHqRequest,
): Promise<HqProductSyncResult> {
  const response = await request.post<ApiResponse<HqProductSyncResult>>(
    `${API_BASE}/sync-selected-from-hq`,
    data,
  )
  assertApiSuccess(response, '选中商品 HQ 同步失败')
  return normalizeHqProductSyncResult(unwrapApiData(response))
}

export async function createProductHqSyncFullJob(
  data: HqProductFullSyncJobRequest,
): Promise<HqProductSyncJobResult> {
  const response = await request.post<ApiResponse<unknown>>(
    `${SYNC_API_BASE}/products/jobs`,
    withOperationId(data, buildProductHqSyncOperationId('full')),
  )
  assertApiSuccess(response, '创建商品 HQ 全量同步任务失败')
  return normalizeHqProductSyncJobResult(response)
}

export async function createProductHqSyncIncrementalJob(
  data: HqProductIncrementalSyncJobRequest,
): Promise<HqProductSyncJobResult> {
  const response = await request.post<ApiResponse<unknown>>(
    `${SYNC_API_BASE}/products-incremental/jobs`,
    withOperationId(data, buildProductHqSyncOperationId('incremental', data.startDate)),
  )
  assertApiSuccess(response, '创建商品 HQ 增量同步任务失败')
  return normalizeHqProductSyncJobResult(response)
}

export async function getProductHqSyncJob(jobId: string): Promise<HqProductSyncJobResult> {
  const response = await request.get<ApiResponse<unknown>>(`${SYNC_API_BASE}/products/jobs/${encodeURIComponent(jobId)}`)
  assertApiSuccess(response, '查询商品 HQ 同步任务失败')
  return normalizeHqProductSyncJobResult(response, jobId)
}

export const createProductFullHqSyncJob = createProductHqSyncFullJob
export const createProductIncrementalHqSyncJob = createProductHqSyncIncrementalJob
export const createHqProductFullSyncJob = createProductHqSyncFullJob
export const createHqProductIncrementalSyncJob = createProductHqSyncIncrementalJob
export const getHqProductSyncJob = getProductHqSyncJob
