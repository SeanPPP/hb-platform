import type { ApiResponse } from '../types/api'
import type {
  PaymentTerminalSettingsDto,
  UpdateLinklyCredentialRequest,
  UpdateSquareTokenRequest,
} from '../types/paymentTerminalSettings'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/payment-terminal-settings'

export async function getPaymentTerminalSettings(storeCode?: string) {
  const response = await request.get<ApiResponse<PaymentTerminalSettingsDto>>(API_BASE, {
    params: { storeCode },
  })
  return unwrapApiData(response)
}

export async function saveSquareToken(payload: UpdateSquareTokenRequest, storeCode?: string) {
  const response = await request.put<ApiResponse<PaymentTerminalSettingsDto>>(`${API_BASE}/square`, payload, {
    params: { storeCode },
  })
  return unwrapApiData(response)
}

export async function saveLinklyCredential(payload: UpdateLinklyCredentialRequest) {
  const response = await request.put<ApiResponse<PaymentTerminalSettingsDto>>(`${API_BASE}/linkly`, payload)
  return unwrapApiData(response)
}
