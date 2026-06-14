import type { ApiResponse, PagedResult } from '../types/api'
import type { RoleOptionDto } from '../types/role'
import type {
  CreateUserDto,
  UpdateUserDto,
  UserPermissionAssignmentDto,
  UserPermissionStateDto,
  UpdateUserPasswordDto,
  UserDetailDto,
  UserDto,
  UserQueryDto,
  UserRoleAssignmentDto,
  UserStoreAssignmentDto,
  UserStoreDto,
} from '../types/user'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

type UserStoreApiDto = Omit<UserStoreDto, 'isManageable'> & {
  IsActive?: boolean
  isManageable?: boolean
  isPrimary?: boolean
}

const mapUserStore = (store: UserStoreApiDto): UserStoreDto => {
  const { IsActive, isPrimary, isManageable, ...rest } = store
  return {
    ...rest,
    // 用户关联分店可能已停用，前端仍保留状态给详情展示和后续判断。
    isActive: rest.isActive ?? IsActive,
    isManageable: isManageable ?? isPrimary ?? false,
  }
}

export async function getUsers(params: UserQueryDto): Promise<PagedResult<UserDto>> {
  const response = await request.get<ApiResponse<PagedResult<UserDto>>>('/api/Users/optimized', {
    params: params as Record<string, unknown>,
  })
  return unwrapPagedResult(response)
}

export async function createUser(payload: CreateUserDto): Promise<UserDto> {
  const response = await request.post<ApiResponse<UserDto>>('/api/Users', {
    Username: payload.username,
    Email: payload.email,
    Password: payload.password,
    PasswordFormat: payload.passwordFormat,
    FullName: payload.fullName ?? null,
    IsActive: payload.isActive ?? true,
    RoleGuids: payload.roleGuids ?? [],
    StoreGuids: payload.storeGuids ?? [],
  })
  return unwrapApiData(response)
}

export async function getUserByGuid(guid: string): Promise<UserDetailDto> {
  const response = await request.get<ApiResponse<UserDetailDto>>(`/api/Users/guid/${guid}`)
  return unwrapApiData(response)
}

export async function getUserStores(guid: string): Promise<UserStoreDto[]> {
  const response = await request.get<ApiResponse<UserStoreApiDto[]>>(`/api/Users/guid/${guid}/stores`)
  return (unwrapApiData(response) ?? []).map(mapUserStore)
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

export async function getUserPermissionState(guid: string): Promise<UserPermissionStateDto> {
  const response = await request.get<ApiResponse<UserPermissionStateDto>>(
    `/api/Users/guid/${guid}/permissions/state`,
  )
  return unwrapApiData(response)
}

export async function assignPermissionsToUser(
  guid: string,
  payload: UserPermissionAssignmentDto,
): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(
    `/api/Users/guid/${guid}/permissions`,
    { permissions: payload.permissions },
  )
  return unwrapApiData(response)
}

export async function assignStoresToUser(guid: string, payload: UserStoreAssignmentDto[]): Promise<boolean> {
  const response = await request.post<ApiResponse<boolean>>(
    `/api/Users/guid/${guid}/stores`,
    payload.map((item) => ({
      StoreGUID: item.storeGUID,
      AccessLevel: item.accessLevel ?? 'ReadWrite',
      IsPrimary: item.isManageable ?? false,
    })),
  )
  return unwrapApiData(response)
}

export async function updateUserPassword(guid: string, dto: UpdateUserPasswordDto): Promise<boolean> {
  const response = await request.put<ApiResponse<boolean>>(
    `/api/Users/guid/${guid}/password`,
    {
      NewPassword: dto.newPassword,
      PasswordFormat: dto.passwordFormat,
      ForcePasswordChange: dto.forcePasswordChange,
    },
  )
  return unwrapApiData(response)
}
