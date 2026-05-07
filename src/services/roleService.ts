import type { ApiResponse, PagedResult } from '../types/api'
import type { RoleDetailDto, RoleDto, RoleQueryDto } from '../types/role'
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
