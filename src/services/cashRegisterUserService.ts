import type { ApiResponse } from '../types/api'
import type {
  CashRegisterUserDetailDto,
  CashRegisterUserListDto,
  CreateCashRegisterUserDto,
  UpdateCashRegisterUserDto,
} from '../types/cashRegisterUser'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/cash-register-users'

export async function getCashRegisterUserGrid(data: Record<string, unknown>) {
  const response = await request.post<ApiResponse<{ items: CashRegisterUserListDto[]; total: number }>>(
    `${API_BASE}/grid`,
    data,
  )
  return unwrapApiData(response)
}

export async function getCashRegisterUserByHGuid(hGuid: string): Promise<CashRegisterUserDetailDto> {
  const response = await request.get<ApiResponse<CashRegisterUserDetailDto>>(`${API_BASE}/${hGuid}`)
  return unwrapApiData(response)
}

export async function createCashRegisterUser(data: CreateCashRegisterUserDto): Promise<CashRegisterUserDetailDto> {
  const response = await request.post<ApiResponse<CashRegisterUserDetailDto>>(API_BASE, data)
  return unwrapApiData(response)
}

export async function updateCashRegisterUser(
  hGuid: string,
  data: UpdateCashRegisterUserDto,
): Promise<CashRegisterUserDetailDto> {
  const response = await request.put<ApiResponse<CashRegisterUserDetailDto>>(`${API_BASE}/${hGuid}`, data)
  return unwrapApiData(response)
}

export async function deleteCashRegisterUser(hGuid: string): Promise<void> {
  await request.delete(`${API_BASE}/${hGuid}`)
}

export async function batchDeleteCashRegisterUsers(hGuids: string[]): Promise<void> {
  await request.post(`${API_BASE}/batch-delete`, hGuids)
}
