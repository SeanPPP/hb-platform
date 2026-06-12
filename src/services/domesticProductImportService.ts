import request from '../utils/request'

const API_BASE = '/api/react/v1/domestic-products'

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
