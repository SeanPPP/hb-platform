import type { ApiResponse, PagedResult } from '../types/api'
import request, { unwrapApiData } from '../utils/request'

export interface StoreUserGridQuery {
  storeCode: string
  keyword?: string
  status?: number
}

export interface StoreUserListDto {
  userGuid: string
  username: string
  fullName?: string
  email?: string
  phone?: string
  status: number
  storeGuid: string
  storeCode: string
  storeName: string
  roleNames: string[]
  lastLoginTime?: string
  lastLoginIp?: string
  createdAt?: string
  updatedAt?: string
}

function normalizeStoreUser(raw: Record<string, unknown>): StoreUserListDto {
  return {
    userGuid: String(raw.userGuid ?? raw.UserGuid ?? raw.userGUID ?? raw.UserGUID ?? ''),
    username: String(raw.username ?? raw.Username ?? ''),
    fullName: typeof (raw.fullName ?? raw.FullName) === 'string' ? String(raw.fullName ?? raw.FullName) : undefined,
    email: typeof (raw.email ?? raw.Email) === 'string' ? String(raw.email ?? raw.Email) : undefined,
    phone: typeof (raw.phone ?? raw.Phone) === 'string' ? String(raw.phone ?? raw.Phone) : undefined,
    status: Number(raw.status ?? raw.Status ?? 0),
    storeGuid: String(raw.storeGuid ?? raw.StoreGuid ?? ''),
    storeCode: String(raw.storeCode ?? raw.StoreCode ?? ''),
    storeName: String(raw.storeName ?? raw.StoreName ?? ''),
    roleNames: Array.isArray(raw.roleNames) ? raw.roleNames.map(String) : Array.isArray(raw.RoleNames) ? raw.RoleNames.map(String) : [],
    lastLoginTime: typeof (raw.lastLoginTime ?? raw.LastLoginTime) === 'string' ? String(raw.lastLoginTime ?? raw.LastLoginTime) : undefined,
    lastLoginIp: typeof (raw.lastLoginIp ?? raw.LastLoginIp) === 'string' ? String(raw.lastLoginIp ?? raw.LastLoginIp) : undefined,
    createdAt: typeof (raw.createdAt ?? raw.CreatedAt) === 'string' ? String(raw.createdAt ?? raw.CreatedAt) : undefined,
    updatedAt: typeof (raw.updatedAt ?? raw.UpdatedAt) === 'string' ? String(raw.updatedAt ?? raw.UpdatedAt) : undefined,
  }
}

export async function getStoreUsersGrid(query: StoreUserGridQuery): Promise<PagedResult<StoreUserListDto>> {
  const response = await request.post<ApiResponse<PagedResult<Record<string, unknown>>> | PagedResult<Record<string, unknown>>>(
    '/api/react/v1/store-users/grid',
    {
      StoreCode: query.storeCode,
      Keyword: query.keyword,
      Status: query.status ?? 1,
    },
  )
  const data = unwrapApiData(response)
  return {
    items: (data.items ?? []).map(normalizeStoreUser),
    total: data.total ?? data.totalCount ?? 0,
    page: data.page ?? data.pageIndex ?? 1,
    pageSize: data.pageSize ?? data.items?.length ?? 0,
    totalPages: data.totalPages,
  }
}
