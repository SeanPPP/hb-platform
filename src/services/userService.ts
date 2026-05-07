import type { ApiResponse, PagedResult } from '../types/api'
import type { UserDetailDto, UserDto, UserQueryDto, UserStoreDto } from '../types/user'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

export async function getUsers(params: UserQueryDto): Promise<PagedResult<UserDto>> {
  const response = await request.get<ApiResponse<PagedResult<UserDto>>>('/api/Users', {
    params: params as Record<string, unknown>,
  })
  return unwrapPagedResult(response)
}

export async function getUserByGuid(guid: string): Promise<UserDetailDto> {
  const response = await request.get<ApiResponse<UserDetailDto>>(`/api/Users/guid/${guid}`)
  return unwrapApiData(response)
}

export async function getUserStores(guid: string): Promise<UserStoreDto[]> {
  const response = await request.get<ApiResponse<UserStoreDto[]>>(`/api/Users/guid/${guid}/stores`)
  return unwrapApiData(response) ?? []
}
