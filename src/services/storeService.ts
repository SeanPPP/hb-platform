import type { ApiResponse, PagedResult } from '../types/api'
import type {
  AddUserToStoreDto,
  StoreDetailDto,
  StoreDto,
  StoreQueryDto,
  StoreUserDto,
  StoreUserQueryDto,
  UpdateStoreDto,
} from '../types/store'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

export interface StoreOption {
  label: string
  value: string
}

export async function getStores(params: StoreQueryDto): Promise<PagedResult<StoreDto>> {
  const response = await request.get<ApiResponse<PagedResult<StoreDto>>>('/api/stores', {
    params: params as Record<string, unknown>,
  })
  return unwrapPagedResult(response)
}

export async function getActiveStores(): Promise<StoreOption[]> {
  const response = await request.get<ApiResponse<StoreDto[]> | StoreDto[]>('/api/stores/active')
  const stores = Array.isArray(response)
    ? response
    : Array.isArray(response.data)
      ? response.data
      : []

  return stores.map((store) => ({
    label: store.storeName || store.storeCode,
    value: store.storeCode,
  }))
}

export async function getStoreByGuid(guid: string): Promise<StoreDetailDto> {
  const response = await request.get<ApiResponse<StoreDetailDto>>(`/api/stores/guid/${guid}`)
  return unwrapApiData(response)
}

export async function updateStore(guid: string, payload: UpdateStoreDto): Promise<StoreDetailDto> {
  const response = await request.put<ApiResponse<StoreDetailDto>>(`/api/stores/guid/${guid}`, payload)
  return unwrapApiData(response)
}

export async function getStoreUsers(params: {
  storeGuid: string
  query?: StoreUserQueryDto
}): Promise<PagedResult<StoreUserDto>> {
  const response = await request.get<ApiResponse<PagedResult<StoreUserDto>>>(
    `/api/stores/guid/${params.storeGuid}/users`,
    {
      params: params.query as Record<string, unknown> | undefined,
    },
  )
  return unwrapPagedResult(response)
}

export async function addUserToStore(storeGuid: string, payload: AddUserToStoreDto): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(`/api/stores/guid/${storeGuid}/users`, {
    UserGUID: payload.userGUID,
    IsPrimary: payload.isPrimary ?? false,
  })
  return unwrapApiData(response)
}

export async function removeUserFromStore(storeGuid: string, userGuid: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`/api/stores/guid/${storeGuid}/users/${userGuid}`)
  return unwrapApiData(response)
}

export async function setPrimaryUser(storeGuid: string, userGuid: string, isPrimary: boolean): Promise<boolean> {
  const response = await request.put<ApiResponse<boolean>>(
    `/api/stores/guid/${storeGuid}/users/${userGuid}/primary`,
    isPrimary,
  )
  return unwrapApiData(response)
}
