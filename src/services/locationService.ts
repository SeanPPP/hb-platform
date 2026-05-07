import type { ApiResponse } from '../types/api'
import type {
  CreateLocationParams,
  LocationFilterParams,
  LocationItem,
  LocationListResponse,
  UpdateLocationParams,
} from '../types/location'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/locations'

interface LocationListApiPayload {
  items?: LocationItem[]
  total?: number
  pageNumber?: number
  pageSize?: number
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
    sortDirection: params.sortDirection || 'asc',
    filters: params.filters,
  })

  const data = unwrapApiData(response)
  return {
    items: data?.items ?? [],
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
