import type { ApiResponse, PagedResult } from '../types/api'
import type { RoleOptionDto } from '../types/role'
import type {
  UpdateUserDto,
  UserDetailDto,
  UserDto,
  UserQueryDto,
  UserRoleAssignmentDto,
  UserStoreAssignmentDto,
  UserStoreDto,
} from '../types/user'
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

export async function updateUser(guid: string, payload: UpdateUserDto): Promise<UserDetailDto> {
  const response = await request.put<ApiResponse<UserDetailDto>>(`/api/Users/guid/${guid}`, payload)
  return unwrapApiData(response)
}

export async function getUserRoles(guid: string): Promise<RoleOptionDto[]> {
  const response = await request.get<ApiResponse<RoleOptionDto[]>>(`/api/Users/guid/${guid}/roles`)
  return unwrapApiData(response) ?? []
}

export async function assignRolesToUser(guid: string, payload: UserRoleAssignmentDto): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(`/api/Users/guid/${guid}/roles`, {
    RoleGuids: payload.roleGuids,
  })
  return unwrapApiData(response)
}

export async function assignStoresToUser(guid: string, payload: UserStoreAssignmentDto[]): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(
    `/api/Users/guid/${guid}/stores`,
    payload.map((item) => ({
      StoreGUID: item.storeGUID,
      AccessLevel: item.accessLevel ?? 'ReadWrite',
      IsPrimary: item.isPrimary ?? false,
    })),
  )
  return unwrapApiData(response)
}
