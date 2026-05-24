import type { ApiResponse, PagedResult } from '../types/api'
import type { RoleOptionDto } from '../types/role'
import type {
  CreateUserDto,
  UpdateUserDto,
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
  isManageable?: boolean
  isPrimary?: boolean
}

const mapUserStore = (store: UserStoreApiDto): UserStoreDto => {
  const { isPrimary, isManageable, ...rest } = store
  return {
    ...rest,
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
    dto,
  )
  return unwrapApiData(response)
}
