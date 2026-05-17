import type { ApiResponse, PagedResult } from '../types/api'
import type {
  PermissionCategoryDto,
  RoleDetailDto,
  RoleDto,
  RoleOptionDto,
  RolePermissionAssignmentDto,
  RoleQueryDto,
  RoleUserDto,
  SysPermissionDto,
  CreateSysPermissionDto,
  UpdateRoleDto,
} from '../types/role'
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

/** Get all permissions grouped by category */
export async function getPermissions(): Promise<PermissionCategoryDto[]> {
  const response = await request.get<ApiResponse<PermissionCategoryDto[]>>('/api/Roles/permissions')
  return unwrapApiData(response) ?? []
}

/** Get a role's current permission codes */
export async function getRolePermissions(guid: string): Promise<string[]> {
  const response = await request.get<ApiResponse<string[]>>(`/api/Roles/guid/${guid}/permissions`)
  return unwrapApiData(response) ?? []
}

/** Assign permissions to a role (replaces all existing) */
export async function assignPermissionsToRole(
  guid: string,
  dto: RolePermissionAssignmentDto,
): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(`/api/Roles/guid/${guid}/permissions`, dto)
  return unwrapApiData(response)
}

export async function getSysPermissions(): Promise<SysPermissionDto[]> {
  const response = await request.get<ApiResponse<SysPermissionDto[]>>('/api/Roles/sys-permissions')
  return unwrapApiData(response) ?? []
}

export async function createPermission(dto: CreateSysPermissionDto): Promise<SysPermissionDto[]> {
  const response = await request.post<ApiResponse<SysPermissionDto[]>>('/api/Roles/permissions', dto)
  return unwrapApiData(response) ?? []
}

export async function deletePermission(code: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`/api/Roles/permissions/${encodeURIComponent(code)}`)
  return unwrapApiData(response)
}

export async function getPermissionRoles(code: string): Promise<RoleOptionDto[]> {
  const response = await request.get<ApiResponse<RoleOptionDto[]>>(`/api/Roles/permissions/${encodeURIComponent(code)}/roles`)
  return unwrapApiData(response) ?? []
}

export async function assignRolesToPermission(code: string, roleGuids: string[]): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(`/api/Roles/permissions/${encodeURIComponent(code)}/roles`, roleGuids)
  return unwrapApiData(response)
}
