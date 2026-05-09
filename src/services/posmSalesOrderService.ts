import type { ApiResponse } from '../types/api'
import type {
  PosmSalesOrder,
  PosmSalesOrderDetailResponse,
  PosmSalesOrderQueryParams,
} from '../types/posmSalesOrder'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/posm-sales-orders'

export async function getSalesOrderList(
  params: PosmSalesOrderQueryParams,
): Promise<{ items: PosmSalesOrder[]; total: number }> {
  const response = await request.post<ApiResponse<{ items: PosmSalesOrder[]; total: number }>>(
    `${API_BASE}/list`,
    params,
  )
  return unwrapApiData(response)
}

export async function getSalesOrderDetail(orderGuid: string): Promise<PosmSalesOrderDetailResponse> {
  const response = await request.get<ApiResponse<PosmSalesOrderDetailResponse>>(`${API_BASE}/detail/${orderGuid}`)
  return unwrapApiData(response)
}

export function getTaxInvoicePdfUrl(orderGuid: string): string {
  const baseUrl = (import.meta.env.VITE_API_BASE_URL || '').trim()
  return `${baseUrl}${API_BASE}/tax-invoice/${orderGuid}`
}

export async function fetchTaxInvoicePdf(orderGuid: string): Promise<string> {
  const url = getTaxInvoicePdfUrl(orderGuid)
  const response = await fetch(url, { credentials: 'include' })
  if (!response.ok) {
    throw new Error(`获取发票失败 (${response.status})`)
  }
  const blob = await response.blob()
  return URL.createObjectURL(blob)
}
