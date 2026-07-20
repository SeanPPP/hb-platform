import type { ApiResponse, PagedResult } from '../types/api'
import type { AccessControl } from '../types/auth'
import { P } from '../types/permissions'
import type {
  PreorderActivationDetail,
  PreorderActivationPayload,
  PreorderActivationStoresPayload,
  PreorderActivationStatistics,
  PreorderActivationSummary,
  PreorderActiveResult,
  PreorderDraftPayload,
  PreorderResolvedItem,
  PreorderSubmitPayload,
  PreorderTemplateDetail,
  PreorderTemplatePayload,
  PreorderTemplateSummary,
  PreorderWarehouseOrderSummary,
  PreorderOrderStatus,
  PreorderPasteRow,
} from '../types/preorder'
import request, { RequestError, unwrapApiData } from '../utils/request'
import { createKeyedSingleFlight } from './preorderSingleFlight'

const ADMIN_BASE = '/api/react/v1/preorders/admin'
const SHOP_BASE = '/api/react/v1/preorders'
const activePreorderSingleFlight = createKeyedSingleFlight<string, PreorderActiveResult>()
const activePreorderMutationGenerations = new Map<string, number>()

function getActivePreorderMutationGeneration(storeCode: string) {
  return activePreorderMutationGenerations.get(storeCode) ?? 0
}

export function advanceActivePreorderFreshEpoch(storeCode: string) {
  // 只有确认终态后才推进门店 epoch；响应丢失后的 detail reconciliation 也必须走同一 fresh lane。
  const nextGeneration = getActivePreorderMutationGeneration(storeCode) + 1
  activePreorderMutationGenerations.set(storeCode, nextGeneration)
  return nextGeneration
}

function getPreorderErrorCode(error: unknown) {
  if (!(error instanceof RequestError) || error.status !== 409) return false
  const payload = error.payload as { code?: string; errorCode?: string; data?: { code?: string; errorCode?: string } } | undefined
  return payload?.code ?? payload?.errorCode ?? payload?.data?.code ?? payload?.data?.errorCode
}

export function isPreorderRequiredError(error: unknown) {
  const code = getPreorderErrorCode(error)
  return code === 'PREORDER_REQUIRED' || (
    error instanceof RequestError &&
    error.status === 409 &&
    error.message.includes('PREORDER_REQUIRED')
  )
}

export function isPreorderDraftConflictError(error: unknown) {
  const code = getPreorderErrorCode(error)
  return code === 'PREORDER_DRAFT_CONFLICT' || (
    error instanceof RequestError &&
    error.status === 409 &&
    error.message.includes('PREORDER_DRAFT_CONFLICT')
  )
}

export function isPreorderStatusTransitionConflictError(error: unknown) {
  const code = getPreorderErrorCode(error)
  return code === 'PREORDER_INVALID_STATUS_TRANSITION' || (
    error instanceof RequestError &&
    error.status === 409 &&
    error.message.includes('PREORDER_INVALID_STATUS_TRANSITION')
  )
}

export function isPreorderActivationStoresChangedError(error: unknown) {
  const code = getPreorderErrorCode(error)
  return code === 'PREORDER_ACTIVATION_STORES_CHANGED' || (
    error instanceof RequestError &&
    error.status === 409 &&
    error.message.includes('PREORDER_ACTIVATION_STORES_CHANGED')
  )
}

function unwrapList<T>(payload: ApiResponse<T[] | PagedResult<T>> | T[] | PagedResult<T>): T[] {
  const data = unwrapApiData(payload)
  return Array.isArray(data) ? data : data?.items ?? []
}

interface ApiActivationSummary extends Omit<PreorderActivationSummary, 'sequenceNumber' | 'activationNumber' | 'submittedCount' | 'noDemandCount' | 'pendingCount' | 'cancelledCount'> {
  periodNumber: number
  activationCode: string
  respondedStoreCount: number
}

interface ApiResolvedResult {
  rows: Array<Omit<PreorderResolvedItem, 'valid'> & { status: string }>
}

interface ApiPreorderActiveResult {
  normalOrderBlocked?: unknown
  activations?: ApiActivationSummary[] | null
}

function normalizeActivation(row: ApiActivationSummary): PreorderActivationSummary {
  return {
    ...row,
    sequenceNumber: row.periodNumber,
    activationNumber: row.activationCode,
    submittedCount: row.respondedStoreCount,
    noDemandCount: 0,
    pendingCount: Math.max(0, row.targetStoreCount - row.respondedStoreCount),
    cancelledCount: 0,
  }
}

function normalizeActivationDetail(row: ApiActivationSummary & Omit<PreorderActivationDetail, keyof PreorderActivationSummary>): PreorderActivationDetail {
  return {
    ...normalizeActivation(row),
    ...row,
    sequenceNumber: row.periodNumber,
    activationNumber: row.activationCode,
    items: (row.items ?? []).map((item, index) => ({ ...item, sortOrder: index })),
  }
}

export function normalizePreorderActiveResult(result: ApiPreorderActiveResult): PreorderActiveResult {
  return {
    // 门禁响应异常时必须 fail-closed；只有后端显式返回 false 才解锁普通订单提交。
    normalOrderBlocked: result.normalOrderBlocked === false ? false : true,
    activations: Array.isArray(result.activations) ? result.activations.map(normalizeActivation) : [],
  }
}

export function canBypassPreorderGate(
  access: Pick<AccessControl, 'isWarehouseStaffOnly' | 'canManageWarehouseOrders' | 'hasPermission'>,
) {
  // 仅使用登录态中的仓库身份/权限决定是否跳过客户端提示；最终授权仍由提交接口校验。
  return access.canManageWarehouseOrders || (
    access.isWarehouseStaffOnly && access.hasPermission(P.Orders.Create)
  )
}

export function resolveEffectivePreorderGateBlocked(
  preorderGateUnavailableOrBlocked: boolean,
  canBypass: boolean,
) {
  return !canBypass && preorderGateUnavailableOrBlocked
}

export async function getPreorderTemplates(): Promise<PreorderTemplateSummary[]> {
  const items: PreorderTemplateSummary[] = []
  let page = 1
  let total = 0
  do {
    const result = unwrapApiData<PagedResult<PreorderTemplateSummary>>(await request.get(`${ADMIN_BASE}/templates`, { params: { page, pageSize: 200 } }))
    items.push(...(result.items ?? []))
    total = result.total ?? items.length
    if (!result.items?.length) break
    page += 1
  } while (items.length < total)
  return items
}

export async function getPreorderTemplate(templateGuid: string, signal?: AbortSignal): Promise<PreorderTemplateDetail> {
  return unwrapApiData(await request.get(`${ADMIN_BASE}/templates/${templateGuid}`, { signal }))
}

export async function createPreorderTemplate(payload: PreorderTemplatePayload): Promise<PreorderTemplateDetail> {
  return unwrapApiData(await request.post(`${ADMIN_BASE}/templates`, payload))
}

export async function updatePreorderTemplate(templateGuid: string, payload: PreorderTemplatePayload): Promise<PreorderTemplateDetail> {
  return unwrapApiData(await request.put(`${ADMIN_BASE}/templates/${templateGuid}`, payload))
}

export async function resolvePreorderItems(rows: PreorderPasteRow[], signal?: AbortSignal): Promise<PreorderResolvedItem[]> {
  const result = unwrapApiData<ApiResolvedResult>(await request.post(`${ADMIN_BASE}/resolve-items`, { rows }, { signal }))
  return (result.rows ?? []).map((row) => ({ ...row, valid: row.status === 'Resolved' }))
}

export async function getTemplateActivations(templateGuid: string, signal?: AbortSignal): Promise<PreorderActivationSummary[]> {
  return unwrapList<ApiActivationSummary>(await request.get(`${ADMIN_BASE}/templates/${templateGuid}/activations`, { signal })).map(normalizeActivation)
}

export async function activatePreorderTemplate(templateGuid: string, payload: PreorderActivationPayload): Promise<PreorderActivationSummary> {
  return normalizeActivation(unwrapApiData(await request.post(`${ADMIN_BASE}/templates/${templateGuid}/activate`, payload)))
}

export async function getAdminPreorderActivation(activationGuid: string, signal?: AbortSignal): Promise<PreorderActivationDetail> {
  return normalizeActivationDetail(unwrapApiData(await request.get(`${ADMIN_BASE}/activations/${activationGuid}`, { signal })))
}

export async function updatePreorderActivationStores(
  activationGuid: string,
  payload: PreorderActivationStoresPayload,
  signal?: AbortSignal,
): Promise<PreorderActivationDetail> {
  return normalizeActivationDetail(unwrapApiData(await request.put(
    `${ADMIN_BASE}/activations/${activationGuid}/stores`,
    payload,
    { signal },
  )))
}

export async function closePreorderActivation(activationGuid: string, endAtUtc?: string): Promise<void> {
  await request.post(`${ADMIN_BASE}/activations/${activationGuid}/close`, { endAtUtc })
}

export async function cancelPreorderActivation(activationGuid: string): Promise<void> {
  await request.post(`${ADMIN_BASE}/activations/${activationGuid}/cancel`, {})
}

export async function getPreorderActivationStatistics(activationGuid: string, signal?: AbortSignal): Promise<PreorderActivationStatistics> {
  const statistics = unwrapApiData(await request.get<ApiResponse<{
    targetStoreCount: number
    submittedCount: number
    noDemandCount: number
    processingCount: number
    completedCount: number
    cancelledCount: number
    pendingCount: number
    storeProductQuantities: PreorderActivationStatistics['storeProductQuantities']
    products: Array<PreorderActivationStatistics['products'][number] & { orderingStoreCount: number }>
    orders: PreorderActivationStatistics['orders']
    pendingStores: PreorderActivationStatistics['pendingStores']
  }>>(`${ADMIN_BASE}/activations/${activationGuid}/statistics`, { signal }))
  return {
    targetStoreCount: statistics.targetStoreCount,
    submittedCount: statistics.submittedCount,
    noDemandCount: statistics.noDemandCount,
    processingCount: statistics.processingCount,
    completedCount: statistics.completedCount,
    cancelledCount: statistics.cancelledCount,
    pendingCount: statistics.pendingCount,
    products: (statistics.products ?? []).map((item) => ({
      ...item,
      orderedStoreCount: item.orderingStoreCount,
    })),
    orders: statistics.orders ?? [],
    pendingStores: statistics.pendingStores ?? [],
    storeProductQuantities: statistics.storeProductQuantities ?? [],
  }
}

export async function getPreorderActivationOrders(activationGuid: string, signal?: AbortSignal): Promise<PreorderWarehouseOrderSummary[]> {
  return unwrapList(await request.get(`${ADMIN_BASE}/activations/${activationGuid}/orders`, { signal }))
}

export async function updatePreorderOrderStatus(
  orderGuid: string,
  status: PreorderOrderStatus,
  warehouseNotes: string | undefined,
  expectedStatus: PreorderOrderStatus,
  expectedDraftRevision: number,
): Promise<void> {
  await request.patch(`${ADMIN_BASE}/orders/${orderGuid}/status`, {
    status,
    warehouseNotes,
    expectedStatus,
    expectedDraftRevision,
  })
}

export async function downloadPreorderActivationExport(activationGuid: string): Promise<void> {
  const baseUrl = (import.meta.env.VITE_API_BASE_URL || '').trim()
  const response = await fetch(`${baseUrl}${ADMIN_BASE}/activations/${activationGuid}/export`, { credentials: 'include' })
  if (!response.ok) throw new Error(`导出失败 (${response.status})`)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `preorder-${activationGuid}.xlsx`
  anchor.click()
  URL.revokeObjectURL(url)
}

export async function getActivePreorders(
  storeCode: string,
  signal?: AbortSignal,
  onRequestStart?: () => void,
): Promise<PreorderActiveResult> {
  const mutationGeneration = getActivePreorderMutationGeneration(storeCode)
  return activePreorderSingleFlight.run(storeCode, async () => {
    onRequestStart?.()
    const result = unwrapApiData<ApiPreorderActiveResult>(await request.get(`${SHOP_BASE}/active`, { params: { storeCode }, signal }))
    return normalizePreorderActiveResult(result)
  }, mutationGeneration)
}

export async function getShopPreorderActivation(activationGuid: string, storeCode: string, signal?: AbortSignal): Promise<PreorderActivationDetail> {
  return normalizeActivationDetail(unwrapApiData(await request.get(`${SHOP_BASE}/activations/${activationGuid}`, { params: { storeCode }, signal })))
}

export async function saveShopPreorderDraft(activationGuid: string, payload: PreorderDraftPayload): Promise<PreorderActivationDetail> {
  return normalizeActivationDetail(unwrapApiData(await request.put(`${SHOP_BASE}/activations/${activationGuid}/draft`, payload)))
}

export function createPreorderSubmissionId() {
  return typeof globalThis.crypto?.randomUUID === 'function'
    ? globalThis.crypto.randomUUID()
    : `preorder-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

export async function submitShopPreorder(
  activationGuid: string,
  payload: PreorderSubmitPayload,
  submissionId?: string,
): Promise<PreorderWarehouseOrderSummary> {
  const result = unwrapApiData<PreorderWarehouseOrderSummary>(await request.post(
    `${SHOP_BASE}/activations/${activationGuid}/submit`,
    payload,
    submissionId ? { headers: { 'X-Preorder-Submission-Id': submissionId } } : undefined,
  ))
  return result
}
