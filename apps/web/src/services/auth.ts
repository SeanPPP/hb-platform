import type { ApiResponse } from '../types/api'
import type { CurrentUser, LoginRequest, SessionResponse } from '../types/auth'
import type { UserStoreDto } from '../types/user'
import { getClientPublicIpHeaders } from '../utils/clientPublicIp'
import request, { unwrapApiData } from '../utils/request'

type CurrentUserStoreApiDto = Partial<UserStoreDto> & {
  StoreGUID?: string
  StoreName?: string
  StoreCode?: string
  IsActive?: boolean
  IsManageable?: boolean
  isPrimary?: boolean
  IsPrimary?: boolean
  AssignedAt?: string
}

type CurrentUserApiDto = Omit<CurrentUser, 'stores'> & {
  stores?: CurrentUserStoreApiDto[]
  Stores?: CurrentUserStoreApiDto[]
}

export async function login(payload: LoginRequest) {
  return request.post<ApiResponse<SessionResponse>>('/api/Auth/session/login', payload, {
    headers: await getClientPublicIpHeaders(),
  })
}

export async function logout() {
  return request.post<ApiResponse<object>>('/api/Auth/session/logout', {}, { skipAuthRedirect: true })
}

export async function refreshSession() {
  try {
    const response = await request.post<ApiResponse<SessionResponse>>(
      '/api/Auth/session/refresh',
      {},
      {
        headers: await getClientPublicIpHeaders(),
        skipAuthRedirect: true,
      },
    )
    return !!(response?.success ?? response?.data)
  } catch {
    return false
  }
}

export async function getCurrentUser(): Promise<CurrentUser> {
  const response = await request.get<ApiResponse<CurrentUserApiDto>>('/api/Auth/current')
  return normalizeCurrentUser(unwrapApiData(response))
}

export function normalizeCurrentUser(user: CurrentUserApiDto): CurrentUser {
  const stores = user.stores ?? user.Stores
  const { Stores: _stores, ...rest } = user

  return {
    ...rest,
    stores: stores?.map((store) => ({
      storeGUID: store.storeGUID ?? store.StoreGUID ?? '',
      storeName: store.storeName ?? store.StoreName ?? '',
      storeCode: store.storeCode ?? store.StoreCode ?? '',
      // 后端会返回关联分店启用状态；停用分店仍属于用户身份范围。
      isActive: store.isActive ?? store.IsActive,
      isManageable: store.isManageable ?? store.IsManageable ?? store.isPrimary ?? store.IsPrimary ?? false,
      assignedAt: store.assignedAt ?? store.AssignedAt ?? '',
    })),
  }
}
