import type { ApiResponse } from '../types/api'
import request, { RequestError, unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/container-products/create-new-products'
const SUBMIT_CONTAINER_API_BASE = '/api/react/v1/container-products/submit-container'

export type ContainerProductCreationJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface ContainerProductCreationJobRequest {
  operationId: string
  containerGuid: string
  detailHguids: string[]
}

export interface ContainerSubmitJobRequest {
  operationId: string
  containerGuid: string
}

export interface ContainerProductCreationResultItem {
  productCode?: string
  itemNumber?: string
  detailHguid?: string
  reasonCode?: string
  message?: string
}

export interface ContainerProductCreationResult {
  createdCount: number
  updatedCount: number
  skippedCount: number
  failedCount: number
  containerCompleted: boolean
  created: ContainerProductCreationResultItem[]
  updated: ContainerProductCreationResultItem[]
  skipped: ContainerProductCreationResultItem[]
  errors: ContainerProductCreationResultItem[]
}

export interface ContainerProductCreationJob {
  jobId: string
  status: ContainerProductCreationJobStatus
  operationId?: string
  message?: string
  result: ContainerProductCreationResult
}

function assertApiSuccess<T>(response: ApiResponse<T>, fallbackMessage: string): void {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function readNumber(record: Record<string, unknown>, keys: string[]): number {
  for (const key of keys) {
    const value = record[key]
    if (value !== undefined && value !== null && value !== '') {
      const numericValue = Number(value)
      if (Number.isFinite(numericValue)) {
        return numericValue
      }
    }
  }
  return 0
}

function readArray<T>(record: Record<string, unknown>, keys: string[]): T[] {
  for (const key of keys) {
    const value = record[key]
    if (Array.isArray(value)) {
      return value as T[]
    }
  }
  return []
}

function readString(record: Record<string, unknown>, keys: string[]): string | undefined {
  for (const key of keys) {
    const value = record[key]
    if (typeof value === 'string') {
      return value
    }
  }
  return undefined
}

function normalizeResult(raw: Record<string, unknown>): ContainerProductCreationResult {
  const nested = isRecord(raw.result) ? raw.result : isRecord(raw.Result) ? raw.Result : raw
  const merged = { ...raw, ...nested }

  return {
    createdCount: readNumber(merged, ['createdCount', 'CreatedCount', 'created']),
    updatedCount: readNumber(merged, ['updatedCount', 'UpdatedCount', 'updated']),
    skippedCount: readNumber(merged, ['skippedCount', 'SkippedCount', 'skipped']),
    failedCount: readNumber(merged, ['failedCount', 'FailedCount', 'failed', 'Failed', 'errorCount', 'ErrorCount']),
    containerCompleted: Boolean(merged.containerCompleted ?? merged.ContainerCompleted ?? false),
    created: readArray<ContainerProductCreationResultItem>(merged, ['created', 'Created']),
    updated: readArray<ContainerProductCreationResultItem>(merged, ['updated', 'Updated']),
    skipped: readArray<ContainerProductCreationResultItem>(merged, ['skipped', 'Skipped']),
    errors: readArray<ContainerProductCreationResultItem>(merged, ['errors', 'Errors']),
  }
}

function normalizeJob(raw: unknown, fallbackJobId?: string): ContainerProductCreationJob {
  const record = isRecord(raw) ? raw : {}
  const status = readString(record, ['status', 'Status']) || 'Queued'

  if (!['Queued', 'Running', 'Succeeded', 'Failed'].includes(status)) {
    throw new RequestError(`未知创建新商品 job 状态：${status}`, 200, raw)
  }

  const jobId = readString(record, ['jobId', 'JobId']) || fallbackJobId || ''
  if (!jobId) {
    throw new RequestError('创建新商品 job 缺少 jobId', 200, raw)
  }

  return {
    jobId,
    status: status as ContainerProductCreationJobStatus,
    operationId: readString(record, ['operationId', 'OperationId']),
    message: readString(record, ['message', 'Message']),
    result: normalizeResult(record),
  }
}

export function buildContainerCreateProductsOperationId(containerGuid: string, detailHguids: string[]) {
  const normalizedDetails = detailHguids.map((value) => value.trim()).filter(Boolean).sort()
  const detailPart = normalizedDetails.length ? normalizedDetails.join(',') : 'empty'
  return `container-create-products:${containerGuid}:${detailPart}`
}

export function buildContainerSubmitOperationId(containerGuid: string) {
  return `submit-container:${containerGuid.trim()}`
}

export async function createContainerProductCreationJob(
  data: ContainerProductCreationJobRequest,
): Promise<ContainerProductCreationJob> {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/jobs`, data)
  assertApiSuccess(response, '创建新商品 job 失败')
  return normalizeJob(unwrapApiData(response))
}

export async function createContainerSubmitJob(
  data: ContainerSubmitJobRequest,
): Promise<ContainerProductCreationJob> {
  const response = await request.post<ApiResponse<unknown>>(`${SUBMIT_CONTAINER_API_BASE}/jobs`, {
    ...data,
    detailHguids: [],
    submitContainer: true,
  })
  assertApiSuccess(response, '提交货柜 job 失败')
  return normalizeJob(unwrapApiData(response))
}

export async function getContainerProductCreationJob(jobId: string): Promise<ContainerProductCreationJob> {
  const response = await request.get<ApiResponse<unknown>>(`${API_BASE}/jobs/${encodeURIComponent(jobId)}`)
  assertApiSuccess(response, '查询创建新商品 job 失败')
  return normalizeJob(unwrapApiData(response), jobId)
}

export async function waitForContainerProductCreationJob(
  jobId: string,
  options: {
    pollIntervalMs?: number
    timeoutMs?: number
  } = {},
): Promise<ContainerProductCreationJob> {
  const pollIntervalMs = options.pollIntervalMs ?? 2000
  const timeoutMs = options.timeoutMs ?? 10 * 60 * 1000
  const startedAt = Date.now()

  while (Date.now() - startedAt <= timeoutMs) {
    const job = await getContainerProductCreationJob(jobId)
    if (job.status === 'Succeeded' || job.status === 'Failed') {
      return job
    }

    // 轮询间隔固定，避免页面层复制后台 job 的等待逻辑。
    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs))
  }

  throw new RequestError('创建新商品 job 轮询超时', 200, { jobId })
}

export async function waitForContainerSubmitJob(
  jobId: string,
  options: {
    pollIntervalMs?: number
    timeoutMs?: number
  } = {},
): Promise<ContainerProductCreationJob> {
  return waitForContainerProductCreationJob(jobId, options)
}
