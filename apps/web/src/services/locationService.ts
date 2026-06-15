import type { ApiResponse } from '../types/api'
import type {
  CreateLocationParams,
  LocationHqSyncResult,
  LocationHqSyncSummary,
  LocationFilterParams,
  LocationItem,
  LocationListResponse,
  UpdateLocationParams,
} from '../types/location'
import request, { RequestError, unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/locations'

interface LocationListApiPayload {
  items?: LocationItem[]
  total?: number
  pageNumber?: number
  pageSize?: number
}

interface RawLocationProduct extends Partial<LocationItem['products'][number]> {
  ProductBarcode?: string
  barcode?: string
  Barcode?: string
}

interface RawLocationItem extends Omit<LocationItem, 'products'> {
  products?: RawLocationProduct[]
  Products?: RawLocationProduct[]
}

function normalizeLocationProduct(raw: RawLocationProduct) {
  return {
    ...raw,
    // 兼容后端不同命名的商品条码字段，统一给页面消费。
    productBarcode: raw.productBarcode ?? raw.ProductBarcode ?? raw.barcode ?? raw.Barcode,
  }
}

function normalizeLocationItem(raw: RawLocationItem): LocationItem {
  const products = raw.products ?? raw.Products ?? []

  return {
    ...raw,
    products: products.map(normalizeLocationProduct),
  }
}

export async function getLocationList(params: LocationFilterParams): Promise<LocationListResponse> {
  const response = await request.post<ApiResponse<LocationListApiPayload>>(`${API_BASE}/list`, {
    LocationType: params.locationType ?? undefined,
    IsUsed: params.isUsed ?? undefined,
    LocationCode: params.locationCode || undefined,
    LocationBarcode: params.locationBarcode || undefined,
    UpdatedBy: params.updatedBy || undefined,
    Status: params.status ?? undefined,
    PageNumber: params.pageNumber || 1,
    PageSize: params.pageSize || 20,
    SortBy: params.sortBy || 'LocationCode',
    SortDirection: params.sortDirection || 'asc',
    filters: params.filters,
  })

  const data = unwrapApiData(response)
  return {
    items: (data?.items ?? []).map(normalizeLocationItem),
    total: data?.total ?? 0,
    pageNumber: data?.pageNumber ?? params.pageNumber ?? 1,
    pageSize: data?.pageSize ?? params.pageSize ?? 20,
  }
}

export async function createLocation(data: CreateLocationParams): Promise<LocationItem> {
  const response = await request.post<ApiResponse<LocationItem>>(API_BASE, {
    LocationCode: data.locationCode,
    LocationBarcode: data.locationBarcode,
    LocationType: data.locationType,
    Status: data.status,
  })

  return unwrapApiData(response)
}

export async function updateLocation(locationGuid: string, data: UpdateLocationParams): Promise<LocationItem> {
  const response = await request.put<ApiResponse<LocationItem>>(`${API_BASE}/${locationGuid}`, {
    LocationCode: data.locationCode,
    LocationBarcode: data.locationBarcode,
    LocationType: data.locationType,
    Status: data.status,
  })

  return unwrapApiData(response)
}

export async function deleteLocation(locationGuid: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`${API_BASE}/${locationGuid}`)
  return unwrapApiData(response)
}

function assertSyncSuccess(response: ApiResponse<LocationHqSyncResult>, fallbackMessage: string) {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }

  const result = unwrapApiData(response)
  if (result?.isSuccess === false || result?.IsSuccess === false) {
    throw new RequestError(result.message ?? result.Message ?? response.message ?? fallbackMessage, 200, response)
  }

  return result ?? {
    isSuccess: response.success ?? response.isSuccess,
    message: response.message,
  }
}

export async function syncLocationsFromHq(): Promise<LocationHqSyncSummary> {
  const locationResponse = await request.post<ApiResponse<LocationHqSyncResult>>(
    '/api/react/v1/sync/locations-incremental',
    {},
  )
  const locationResult = assertSyncSuccess(locationResponse, '从HQ同步货位失败')

  const productLocationResponse = await request.post<ApiResponse<LocationHqSyncResult>>(
    '/api/react/v1/sync/product-locations-incremental',
    {},
  )
  const productLocationResult = assertSyncSuccess(productLocationResponse, '从HQ同步商品货位失败')

  return {
    locationResult,
    productLocationResult,
  }
}
