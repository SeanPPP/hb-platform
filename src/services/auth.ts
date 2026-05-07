import type { ApiResponse } from '../types/api'
import type { CurrentUser, LoginRequest, TokenResponse } from '../types/auth'
import request, { unwrapApiData } from '../utils/request'

export async function login(payload: LoginRequest) {
  return request.post<ApiResponse<TokenResponse>>('/api/Auth/login', payload)
}

export async function logout() {
  return request.post<ApiResponse<object>>('/api/Auth/logout', {}, { skipAuthRedirect: true })
}

export async function getCurrentUser(): Promise<CurrentUser> {
  const response = await request.get<ApiResponse<CurrentUser>>('/api/Auth/current')
  return unwrapApiData(response)
}
