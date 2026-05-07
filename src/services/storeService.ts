import type { ApiResponse, PagedResult } from '../types/api'
import type { StoreDto, StoreQueryDto } from '../types/store'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

export async function getStores(params: StoreQueryDto): Promise<PagedResult<StoreDto>> {
  const response = await request.get<ApiResponse<PagedResult<StoreDto>>>('/api/stores', {
    params: params as Record<string, unknown>,
  })
  return unwrapPagedResult(response)
}

export async function getStoreByGuid(guid: string): Promise<StoreDto> {
  const response = await request.get<ApiResponse<StoreDto>>(`/api/stores/guid/${guid}`)
  return unwrapApiData(response)
}
