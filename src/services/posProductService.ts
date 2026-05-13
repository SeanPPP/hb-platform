import type { ApiResponse } from '../types/api'
import type {
  BatchUpdatePosProductDto,
  HqProductSyncResult,
  PosProductDto,
  PosProductFilterParams,
  SyncProductsToStoresRequest,
  SyncProductsToStoresResult,
} from '../types/posProduct'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/products'

export async function getProducts(params: PosProductFilterParams) {
  const sortOrderMap: Record<string, string> = { ascend: 'asc', descend: 'desc' }
  const payload: Record<string, unknown> = {
    pageNumber: params.pageIndex,
    pageSize: params.pageSize,
    search: params.keyword || undefined,
    localSupplierCode: params.supplierCode || undefined,
    productCategoryGUIDs: params.categoryGuid ? [params.categoryGuid] : undefined,
    isActive: params.isActive,
    isSpecialProduct: params.isSet,
    sortBy: params.sortBy || undefined,
    sortOrder: params.sortOrder ? sortOrderMap[params.sortOrder] || params.sortOrder : undefined,
  }
  const response = await request.post<Record<string, unknown>>(`${API_BASE}/list`, payload)
  const raw = response as Record<string, unknown>
  const items = (Array.isArray(raw.data) ? raw.data : Array.isArray(raw) ? raw : []) as PosProductDto[]
  const total = (raw.total as number) ?? items.length
  return { items, total }
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

export async function syncProductsFromHq(): Promise<HqProductSyncResult> {
  const response = await request.post<ApiResponse<HqProductSyncResult>>(`${API_BASE}/sync-from-hq`)
  return unwrapApiData(response)
}
