import type { BatchDetail, BatchInfo, BatchListParams, CreateBatchRequest, PrefixCodeListParams, PrefixCodeResponse, UpdatePriceItem } from '../types/domesticProductCreation'
import request from '../utils/request'

const API_BASE = '/api/v1/domestic-product-creation'

export async function createBatch(data: CreateBatchRequest): Promise<{ success: boolean; data?: BatchInfo; message?: string }> {
  const response: any = await request(`${API_BASE}/batch`, {
    method: 'POST',
    data,
  })
  const res = response?.data ?? response
  if (res?.success !== undefined) return res
  if (response?.success !== undefined) return response
  return { success: true, data: res }
}

function transformBatchInfo(raw: Record<string, unknown>): BatchInfo {
  return {
    batchNumber: String(raw.batchNumber ?? ''),
    supplierCode: String(raw.supplierCode ?? ''),
    supplierName: String(raw.supplierName ?? ''),
    prefixCode: raw.prefixCode ? String(raw.prefixCode) : undefined,
    normalCount: Number(raw.normalProductCount ?? raw.normalCount ?? 0),
    setCount: Number(raw.setProductCount ?? raw.setCount ?? 0),
    totalCount: Number(raw.totalCount ?? 0),
    createdAt: String(raw.createdTime ?? raw.createdAt ?? ''),
    createdBy: raw.createdBy ? String(raw.createdBy) : undefined,
  }
}

export async function getBatchList(params: BatchListParams): Promise<{ success: boolean; data?: { items: BatchInfo[]; total: number; page: number; pageSize: number }; message?: string }> {
  const response: any = await request(`${API_BASE}/batches`, {
    method: 'GET',
    params: params as unknown as Record<string, unknown>,
  })
  const outer = response?.data ?? response
  if (outer?.items) {
    return {
      success: true,
      data: {
        items: outer.items.map((item: Record<string, unknown>) => transformBatchInfo(item)),
        total: outer.total ?? outer.items.length,
        page: outer.page ?? params.page ?? 1,
        pageSize: outer.pageSize ?? params.pageSize ?? 20,
      },
    }
  }
  return { success: false, data: { items: [], total: 0, page: 1, pageSize: 20 } }
}

function transformBatchDetail(raw: Record<string, unknown>): BatchDetail {
  const items = Array.isArray(raw.items) ? raw.items : []
  return {
    batchNumber: String(raw.batchNumber ?? ''),
    supplierCode: String(raw.supplierCode ?? ''),
    supplierName: String(raw.supplierName ?? ''),
    prefixCode: raw.prefixCode ? String(raw.prefixCode) : undefined,
    normalCount: Number(raw.normalProductCount ?? raw.normalCount ?? 0),
    setCount: Number(raw.setProductCount ?? raw.setCount ?? 0),
    totalCount: Number(raw.totalCount ?? items.length),
    createdAt: String(raw.createdTime ?? raw.createdAt ?? ''),
    createdBy: raw.createdBy ? String(raw.createdBy) : undefined,
    items: items.map(transformBatchProductItem),
  }
}

function transformBatchProductItem(raw: Record<string, unknown>): BatchProductItem {
  return {
    itemNumber: String(raw.productCode ?? raw.itemNumber ?? ''),
    hbProductNo: String(raw.hbProductNo ?? raw.hBProductNo ?? ''),
    barcode: raw.barcode ? String(raw.barcode) : undefined,
    productName: String(raw.productName ?? ''),
    productType: Number(raw.productType ?? 0),
    privateLabelPrice: raw.privateLabelPrice != null ? Number(raw.privateLabelPrice) : undefined,
    setQuantity: raw.setQuantity != null ? Number(raw.setQuantity) : undefined,
    setPrice: raw.setPrice != null ? Number(raw.setPrice) : undefined,
    parentItemNumber: raw.parentProductCode ? String(raw.parentProductCode) : raw.parentItemNumber ? String(raw.parentItemNumber) : undefined,
    subItems: undefined,
  }
}

export async function getBatchDetail(batchNumber: string): Promise<{ success: boolean; data?: BatchDetail; message?: string }> {
  const response: any = await request(`${API_BASE}/batch/${batchNumber}`, {
    method: 'GET',
  })
  const outer = response?.data ?? response
  if (outer?.batchNumber || outer?.items) {
    return { success: true, data: transformBatchDetail(outer) }
  }
  if (response?.success === false) {
    return { success: false, message: response.message || '加载明细失败' }
  }
  return { success: false, message: '加载明细失败' }
}

export async function updatePrivateLabelPrice(batchNumber: string, items: UpdatePriceItem[]): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`${API_BASE}/batch/${batchNumber}/prices`, {
    method: 'PUT',
    data: { items },
  })
  return response?.data ?? response
}

export async function getActivePrefixes(supplierCode?: string): Promise<{ success: boolean; data: Array<{ prefixCode: string; prefixName: string; prefixDescription?: string }> }> {
  const params: Record<string, unknown> = { page: 1, pageSize: 100, isActive: true }
  if (supplierCode) params.supplierCode = supplierCode
  const response: any = await request('/api/v1/productprefixcodes', {
    method: 'GET',
    params,
  })
  const resData = response?.data ?? response
  if (resData?.items && Array.isArray(resData.items)) {
    return { success: true, data: resData.items }
  }
  if (resData?.data?.items && Array.isArray(resData.data.items)) {
    return { success: true, data: resData.data.items }
  }
  if (Array.isArray(resData?.data)) {
    return { success: true, data: resData.data }
  }
  if (Array.isArray(resData)) {
    return { success: true, data: resData }
  }
  return { success: false, data: [] }
}

export async function getPrefixCodeList(params: PrefixCodeListParams): Promise<PrefixCodeResponse> {
  const response: any = await request('/api/v1/productprefixcodes', {
    method: 'GET',
    params: params as Record<string, unknown>,
  })
  return response?.data ?? response
}

export async function createPrefixCode(data: { supplierCode: string; prefixName: string; prefixDescription?: string; isActive?: boolean; sortOrder?: number }): Promise<{ success: boolean; message?: string }> {
  const response: any = await request('/api/v1/productprefixcodes', {
    method: 'POST',
    data,
  })
  return response?.data ?? response
}

export async function updatePrefixCode(prefixCode: string, data: { prefixName: string; prefixDescription?: string; isActive?: boolean; sortOrder?: number }): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}`, {
    method: 'PUT',
    data,
  })
  return response?.data ?? response
}

export async function deletePrefixCode(prefixCode: string): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}`, {
    method: 'DELETE',
  })
  return response?.data ?? response
}

export async function togglePrefixCodeStatus(prefixCode: string, isActive: boolean): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}/status/${isActive}`, { method: 'PATCH' })
  return response?.data ?? response
}
