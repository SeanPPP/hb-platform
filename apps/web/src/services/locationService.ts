import type { ApiResponse } from '../types/api'
import type {
  BatchUnbindLocationProductsResult,
  CreateLocationParams,
  LocationHqSyncResult,
  LocationHqSyncSummary,
  LocationFilterParams,
  LocationItem,
  LocationListResponse,
  LocationProductBinding,
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

export async function batchUnbindLocationProducts(
  bindings: LocationProductBinding[],
): Promise<BatchUnbindLocationProductsResult> {
  const results: Array<
    | { binding: LocationProductBinding; succeeded: true }
    | { binding: LocationProductBinding; succeeded: false; message: string }
  > = new Array(bindings.length)
  let nextIndex = 0

  const worker = async () => {
    while (nextIndex < bindings.length) {
      const currentIndex = nextIndex
      nextIndex += 1
      const binding = bindings[currentIndex]

      try {
        const response = await request.delete<ApiResponse<LocationItem>>(
          `${API_BASE}/${encodeURIComponent(binding.locationGuid)}/products/${encodeURIComponent(binding.productCode)}`,
        )
        unwrapApiData(response)
        results[currentIndex] = { binding, succeeded: true }
      } catch (error) {
        // 每项独立捕获错误，避免单个解绑失败中断整批请求。
        const message = error instanceof Error ? error.message : '解绑商品失败'
        results[currentIndex] = { binding, succeeded: false, message }
      }
    }
  }

  // 最多启动 5 个 worker，限制网络并发以保护浏览器连接池和后端服务。
  const workerCount = Math.min(5, bindings.length)
  await Promise.all(Array.from({ length: workerCount }, () => worker()))

  // 批量请求全部结束后统一汇总，调用方可分别反馈成功项和失败原因。
  return results.reduce<BatchUnbindLocationProductsResult>(
    (summary, result) => {
      if (result.succeeded) {
        summary.succeeded.push(result.binding)
      } else {
        summary.failed.push({ ...result.binding, message: result.message })
      }
      return summary
    },
    { succeeded: [], failed: [] },
  )
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
