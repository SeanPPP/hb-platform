import type { ApiResponse } from '../types/api'
import type { CurrentUser, LoginRequest, SessionResponse } from '../types/auth'
import request, { unwrapApiData } from '../utils/request'

export async function login(payload: LoginRequest) {
  return request.post<ApiResponse<SessionResponse>>('/api/Auth/session/login', payload)
}

export async function logout() {
  return request.post<ApiResponse<object>>('/api/Auth/session/logout', {}, { skipAuthRedirect: true })
}

export async function getCurrentUser(): Promise<CurrentUser> {
  const response = await request.get<ApiResponse<CurrentUser>>('/api/Auth/current')
  return unwrapApiData(response)
}
