import request, { RequestError } from '../utils/request'

const API_BASE = '/api/react/v1/domestic-products'

export interface HbwebProductNameUpdateItem {
  ItemNumber: string
  ProductName: string
}

export interface UpdateHbwebProductNamesResult {
  updatedCount: number
  unchangedCount: number
  missingItemNumbers: string[]
  errors: string[]
}

export interface UpdateHbwebProductNamesResponse {
  success: boolean
  data?: UpdateHbwebProductNamesResult
  message?: string
  errorCode?: string
}

export async function batchDetectProducts(data: { SupplierCode: string; Products: Array<Record<string, unknown>> }): Promise<{ success: boolean; data: any[]; message?: string }> {
  const response: any = await request(`${API_BASE}/batch-detect`, {
    method: 'POST',
    data,
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}

export async function batchImportConfirm(data: Record<string, unknown>): Promise<{ success: boolean; data?: any; message?: string }> {
  const response: any = await request(`${API_BASE}/batch-import/confirm`, {
    method: 'POST',
    data,
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}

export async function batchUpdateDomesticProducts(data: { Products: Array<Record<string, unknown>> }): Promise<{ success: boolean; data?: any; message?: string }> {
  const response: any = await request(`${API_BASE}/batch-update`, {
    method: 'PUT',
    data,
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}

export async function updateHbwebProductNames(data: { Products: HbwebProductNameUpdateItem[] }): Promise<UpdateHbwebProductNamesResponse> {
  try {
    const response: any = await request(`${API_BASE}/product-master-names`, {
      method: 'PUT',
      data,
    })
    if (response && typeof response === 'object' && 'success' in response) return response
    return response?.data ?? response
  } catch (error) {
    if (error instanceof RequestError && error.payload && typeof error.payload === 'object' && 'success' in error.payload) {
      return error.payload as UpdateHbwebProductNamesResponse
    }
    throw error
  }
}

export async function fixProductImage(productCode: string, imageUrl: string): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`${API_BASE}/${encodeURIComponent(productCode)}/image`, {
    method: 'PATCH',
    data: { productImage: imageUrl },
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}

export async function syncToHBSales(productCodes: string[], includeImage: boolean): Promise<{ success: boolean; data?: any; message?: string }> {
  const response: any = await request(`${API_BASE}/sync-to-hbsales`, {
    method: 'POST',
    data: { productCodes, includeImage },
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}

export async function sendToHq(productCodes: string[]): Promise<{ success: boolean; data?: any; message?: string }> {
  const response: any = await request(`${API_BASE}/send-to-hq`, {
    method: 'POST',
    data: { productCodes },
  })
  if (response && typeof response === 'object' && 'success' in response) return response
  return response?.data ?? response
}
