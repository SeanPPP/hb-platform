import type { ApiResponse } from '../types/api'
import type {
  PosmSalesOrderDetailResponse,
  PosmSalesOrderListResult,
  PosmSalesOrderQueryParams,
} from '../types/posmSalesOrder'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/posm-sales-orders'

/** signal 用于换条件时取消上一次还没返回的查询，避免慢响应覆盖新结果。 */
export async function getSalesOrderList(
  params: PosmSalesOrderQueryParams,
  signal?: AbortSignal,
): Promise<PosmSalesOrderListResult> {
  const response = await request.post<ApiResponse<PosmSalesOrderListResult>>(
    `${API_BASE}/list`,
    params,
    { signal },
  )
  const result = unwrapApiData(response)
  return {
    items: result?.items ?? [],
    total: result?.total ?? 0,
    summary: result?.summary ?? [],
  }
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
