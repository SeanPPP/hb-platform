import type { ApiResponse } from '../types/api'
import type {
  AddStoreOrderLinePayload,
  BatchAddStoreOrderLinePayload,
  BatchUpdateStoreOrderLinePayload,
  BatchUpdateStoreOrderProductStatusPayload,
  CopyStoreOrderPayload,
  CopyStoreOrderResult,
  CreateStoreOrderPayload,
  RemoveStoreOrderLinePayload,
  SyncMissingStoreOrdersResult,
  StoreOrderBatchStatusUpdatePayload,
  StoreOrderBranchOption,
  StoreOrderDetail,
  StoreOrderListItem,
  StoreOrderListQuery,
  StoreOrderListResult,
  StoreOrderProductItem,
  StoreOrderProductListResult,
  StoreOrderProductQuery,
  StoreOrderStatusUpdatePayload,
  UpdateStoreOrderHeaderPayload,
  UpdateStoreOrderLinePayload,
  UpdateStoreOrderProductStatusPayload,
} from '../types/storeOrder'
import request from '../utils/request'

const API_BASE = '/api/react/v1/store-order'

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function unwrapEnvelope<T>(payload: unknown): T {
  let current = payload

  for (let depth = 0; depth < 3; depth += 1) {
    if (!isRecord(current) || !('data' in current)) {
      break
    }

    const keys = Object.keys(current)
    const looksLikeEnvelope =
      keys.includes('data') &&
      (keys.includes('success') ||
        keys.includes('isSuccess') ||
        keys.includes('message') ||
        keys.includes('errorCode') ||
        keys.includes('code'))

    if (!looksLikeEnvelope) {
      break
    }

    current = current.data
  }

  return current as T
}

function normalizePagedList<T>(payload: unknown): StoreOrderListResult {
  const result = unwrapEnvelope<{
    items?: T[]
    total?: number
    page?: number
    pageSize?: number
    pageNumber?: number
  }>(payload)

  return {
    items: (result?.items ?? []) as StoreOrderListItem[],
    total: result?.total ?? 0,
    page: result?.page ?? result?.pageNumber ?? 1,
    pageSize: result?.pageSize ?? 10,
  }
}

function normalizeProductPagedList(payload: unknown): StoreOrderProductListResult {
  const result = unwrapEnvelope<{
    items?: StoreOrderProductItem[]
    total?: number
    page?: number
    pageNumber?: number
    pageSize?: number
  }>(payload)

  return {
    items: result?.items ?? [],
    total: result?.total ?? 0,
    page: result?.page ?? result?.pageNumber ?? 1,
    pageSize: result?.pageSize ?? 24,
  }
}

function normalizeResult<T>(payload: unknown): T {
  return unwrapEnvelope<T>(payload)
}

export async function getStoreOrderList(query: StoreOrderListQuery) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/list`, {
    method: 'POST',
    data: query,
  })

  return normalizePagedList<StoreOrderListItem>(response)
}

export async function getUsedStoreOrderBranches() {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/used-branches`, {
    method: 'GET',
  })

  return normalizeResult<StoreOrderBranchOption[]>(response)
}

export async function getStoreOrderDetail(orderGuid: string) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/detail/${orderGuid}`, {
    method: 'GET',
  })

  return normalizeResult<StoreOrderDetail | null>(response)
}

export async function getStoreOrderProducts(query: StoreOrderProductQuery) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/products`, {
    method: 'POST',
    data: query,
  })

  return normalizeProductPagedList(response)
}

export async function createStoreOrder(payload: CreateStoreOrderPayload) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/create`, {
    method: 'POST',
    data: payload,
  })

  return normalizeResult<string>(response)
}

export async function copyStoreOrder(payload: CopyStoreOrderPayload) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/copy`, {
    method: 'POST',
    data: payload,
  })

  return normalizeResult<CopyStoreOrderResult | string>(response)
}

export async function updateStoreOrderStatus(payload: StoreOrderStatusUpdatePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/status`, {
    method: 'POST',
    data: payload,
  })
}

export async function batchUpdateStoreOrderStatus(payload: StoreOrderBatchStatusUpdatePayload) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/batch-status`, {
    method: 'POST',
    data: payload,
  })

  return normalizeResult<number | boolean | null>(response)
}

export async function deleteStoreOrder(orderGuid: string) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/${orderGuid}`, {
    method: 'DELETE',
  })
}

export async function addStoreOrderLine(payload: AddStoreOrderLinePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/add`, {
    method: 'POST',
    data: payload,
  })
}

export async function batchAddStoreOrderLines(payload: BatchAddStoreOrderLinePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/batch-add`, {
    method: 'POST',
    data: payload,
  })
}

export async function updateStoreOrderLine(payload: UpdateStoreOrderLinePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/update`, {
    method: 'POST',
    data: payload,
  })
}

export async function removeStoreOrderLine(payload: RemoveStoreOrderLinePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/remove`, {
    method: 'POST',
    data: payload,
  })
}

export async function batchUpdateStoreOrderLines(payload: BatchUpdateStoreOrderLinePayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/batch-update`, {
    method: 'POST',
    data: payload,
  })
}

export async function updateStoreOrderProductStatus(payload: UpdateStoreOrderProductStatusPayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/product/status`, {
    method: 'POST',
    data: payload,
  })
}

export async function batchUpdateStoreOrderProductStatus(payload: BatchUpdateStoreOrderProductStatusPayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/product/batch-status`, {
    method: 'POST',
    data: payload,
  })
}

export async function updateStoreOrderHeader(payload: UpdateStoreOrderHeaderPayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/header/update`, {
    method: 'POST',
    data: {
      ...payload,
      orderGuid: payload.orderGUID,
    },
  })
}

export async function completeStoreOrder(orderGuid: string) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/complete/${orderGuid}`, {
    method: 'POST',
  })
}

export async function startPickingStoreOrder(orderGuid: string) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/start-picking/${orderGuid}`, {
    method: 'POST',
  })
}

export async function syncMissingStoreOrders(storeCode?: string) {
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), 10 * 60 * 1000)

  try {
    const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/sync-missing-orders`, {
      method: 'POST',
      data: { storeCode },
      signal: controller.signal,
    })

    return normalizeResult<SyncMissingStoreOrdersResult>(response)
  } finally {
    clearTimeout(timeoutId)
  }
}
