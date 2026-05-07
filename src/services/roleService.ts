import type { ApiResponse, PagedResult } from '../types/api'
import type { RoleDetailDto, RoleDto, RoleOptionDto, RoleQueryDto, RoleUserDto, UpdateRoleDto } from '../types/role'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

export async function getRoles(params: RoleQueryDto): Promise<PagedResult<RoleDto>> {
  const response = await request.get<ApiResponse<PagedResult<RoleDto>>>('/api/Roles', {
    params: params as Record<string, unknown>,
  })
  return unwrapPagedResult(response)
}

export async function getRoleByGuid(guid: string): Promise<RoleDetailDto> {
  const response = await request.get<ApiResponse<RoleDetailDto>>(`/api/Roles/guid/${guid}`)
  return unwrapApiData(response)
}

export async function updateRole(guid: string, payload: UpdateRoleDto): Promise<RoleDetailDto> {
  const response = await request.put<ApiResponse<RoleDetailDto>>(`/api/Roles/guid/${guid}`, payload)
  return unwrapApiData(response)
}

export async function getActiveRoles(): Promise<RoleOptionDto[]> {
  const response = await request.get<ApiResponse<RoleOptionDto[]>>('/api/Roles/active')
  return unwrapApiData(response) ?? []
}

export async function getRoleUsers(guid: string, params?: RoleQueryDto): Promise<PagedResult<RoleUserDto>> {
  const response = await request.get<ApiResponse<PagedResult<RoleUserDto>>>(`/api/Roles/guid/${guid}/users`, {
    params: params as Record<string, unknown> | undefined,
  })
  return unwrapPagedResult(response)
}

export async function addUsersToRole(guid: string, userGuids: string[]): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(`/api/Roles/guid/${guid}/users`, userGuids)
  return unwrapApiData(response)
}

export async function removeUserFromRole(guid: string, userGuid: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`/api/Roles/guid/${guid}/users/${userGuid}`)
  return unwrapApiData(response)
}
