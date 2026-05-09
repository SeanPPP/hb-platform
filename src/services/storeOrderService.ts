import type { ApiResponse } from '../types/api'
import type {
  AddStoreOrderLinePayload,
  BatchAddStoreOrderLinePayload,
  PasteReplaceStoreOrderLinesPayload,
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
  StoreOrderBatchLookupItem,
  StoreOrderBatchLookupPayload,
  StoreOrderCart,
  StoreOrderListItem,
  StoreOrderListQuery,
  StoreOrderListResult,
  StoreOrderScanLookupResult,
  StoreOrderDynamicData,
  StoreOrderPasteTargetField,
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

function normalizeCart(payload: unknown): StoreOrderCart | null {
  const result = normalizeResult<Partial<StoreOrderCart> | null>(payload)
  if (!result) {
    return null
  }

  return {
    orderGUID: result.orderGUID ?? '',
    orderNo: result.orderNo,
    storeCode: result.storeCode,
    storeName: result.storeName,
    totalAmount: result.totalAmount ?? 0,
    totalQuantity: result.totalQuantity ?? 0,
    totalImportAmount: result.totalImportAmount ?? 0,
    totalVolume: result.totalVolume ?? 0,
    remarks: result.remarks,
    shippingFee: result.shippingFee,
    orderDate: result.orderDate,
    storeAddress: result.storeAddress,
    flowStatus: result.flowStatus,
    items: Array.isArray(result.items) ? result.items : [],
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

export async function batchLookupStoreOrderProducts(payload: StoreOrderBatchLookupPayload) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/products/batch-lookup`, {
    method: 'POST',
    data: payload,
  })

  return normalizeResult<StoreOrderBatchLookupItem[]>(response)
}

export async function lookupStoreOrderProductsByBarcode(barcode: string) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/products/scan-lookup`, {
    method: 'POST',
    data: { barcode },
  })

  const result = normalizeResult<Partial<StoreOrderScanLookupResult> | null>(response)

  return {
    barcode: result?.barcode ?? barcode,
    items: Array.isArray(result?.items) ? (result?.items as StoreOrderProductItem[]) : [],
  } satisfies StoreOrderScanLookupResult
}

export async function getStoreOrderProductsDynamicData(payload: {
  storeCode: string
  productCodes: string[]
}) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/dynamic-data`, {
    method: 'POST',
    data: payload,
  })

  const result = normalizeResult<StoreOrderDynamicData[] | null>(response)
  return Array.isArray(result) ? result : []
}

export async function getActiveStoreOrderCart(storeCode: string) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/cart/${storeCode}`, {
    method: 'GET',
  })

  return normalizeCart(response)
}

export async function addStoreOrderCartItem(payload: {
  storeCode: string
  productCode: string
  quantity: number
}) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/cart/add`, {
    method: 'POST',
    data: payload,
  })

  return normalizeCart(response)
}

export async function updateStoreOrderCartItem(payload: {
  storeCode: string
  productCode: string
  quantity: number
}) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/cart/update`, {
    method: 'POST',
    data: payload,
  })

  return normalizeCart(response)
}

export async function removeStoreOrderCartItem(payload: {
  storeCode: string
  detailGUID: string
}) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/cart/remove`, {
    method: 'POST',
    data: payload,
  })

  return normalizeCart(response)
}

export async function clearActiveStoreOrderCart(storeCode: string) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/cart/clear`, {
    method: 'POST',
    data: { storeCode },
  })

  return normalizeCart(response)
}

export async function submitActiveStoreOrder(payload: {
  storeCode: string
  remarks?: string
}) {
  const response = await request<ApiResponse<unknown> | unknown>(`${API_BASE}/submit`, {
    method: 'POST',
    data: payload,
  })

  return normalizeResult<CopyStoreOrderResult | string | null>(response)
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

export async function pasteReplaceStoreOrderLines(payload: PasteReplaceStoreOrderLinesPayload) {
  await request<ApiResponse<unknown> | unknown>(`${API_BASE}/line/paste-replace`, {
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

export type { StoreOrderPasteTargetField }
