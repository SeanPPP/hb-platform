import type { BatchDetail, BatchInfo, BatchListParams, BatchProductItem, CreateBatchRequest, CreateBatchResponse, PrefixCodeListParams, PrefixCodeResponse, SetProductTemplateDetail, SetProductTemplatePayload, SetProductTemplateSubItem, SetProductTemplateSummary, UpdatePriceItem } from '../types/domesticProductCreation'
import request from '../utils/request'

const API_BASE = '/api/v1/domestic-product-creation'

type ApiOperationResult<T> = { success: boolean; data?: T; message?: string }

function unwrapApiOperation<T>(response: any): ApiOperationResult<T> {
  const outer = response?.data ?? response
  if (outer?.success !== undefined) {
    return { success: Boolean(outer.success), data: outer.data, message: outer.message }
  }
  if (response?.success !== undefined) {
    return { success: Boolean(response.success), data: response.data, message: response.message }
  }
  return { success: true, data: outer }
}

function transformSetProductTemplateSubItem(raw: Record<string, unknown>, index: number): SetProductTemplateSubItem {
  return {
    productName: String(raw.productName ?? raw.subItemProductName ?? ''),
    privateLabelPrice: Number(raw.privateLabelPrice ?? 0),
    sortOrder: Number(raw.sortOrder ?? raw.sequence ?? index + 1),
  }
}

function transformSetProductTemplateSummary(raw: Record<string, unknown>): SetProductTemplateSummary {
  return {
    templateId: String(raw.templateId ?? raw.id ?? raw.templateGuid ?? ''),
    supplierCode: String(raw.supplierCode ?? ''),
    templateName: String(raw.templateName ?? raw.name ?? ''),
    setProductName: String(raw.setProductName ?? raw.productName ?? ''),
    isEnabled: raw.isEnabled == null ? Boolean(raw.isActive ?? true) : Boolean(raw.isEnabled),
    setQuantity: Number(raw.setQuantity ?? raw.subItemCount ?? raw.itemCount ?? (Array.isArray(raw.subItems) ? raw.subItems.length : 0)),
    updatedAt: raw.updatedAt ? String(raw.updatedAt) : raw.updatedTime ? String(raw.updatedTime) : undefined,
  }
}

function transformSetProductTemplateDetail(raw: Record<string, unknown>): SetProductTemplateDetail {
  const subItems = Array.isArray(raw.subItems) ? raw.subItems : Array.isArray(raw.items) ? raw.items : []
  return {
    ...transformSetProductTemplateSummary(raw),
    subItems: subItems.map((item: Record<string, unknown>, index: number) => transformSetProductTemplateSubItem(item, index)),
  }
}

export async function getSetProductTemplates(supplierCode: string, includeInactive = false): Promise<ApiOperationResult<SetProductTemplateSummary[]>> {
  const response: any = await request(`${API_BASE}/templates`, {
    method: 'GET',
    params: { supplierCode, includeInactive },
  })
  const result = unwrapApiOperation<unknown>(response)
  if (!result.success) return { success: false, data: [], message: result.message }
  const rawItems = Array.isArray(result.data)
    ? result.data
    : Array.isArray((result.data as any)?.items)
      ? (result.data as any).items
      : []
  return {
    success: true,
    data: rawItems.map((item: Record<string, unknown>) => transformSetProductTemplateSummary(item)),
    message: result.message,
  }
}

export async function getSetProductTemplate(templateId: string, supplierCode: string): Promise<ApiOperationResult<SetProductTemplateDetail>> {
  const response: any = await request(`${API_BASE}/templates/${encodeURIComponent(templateId)}`, {
    method: 'GET',
    params: { supplierCode },
  })
  const result = unwrapApiOperation<Record<string, unknown>>(response)
  return result.success && result.data
    ? { success: true, data: transformSetProductTemplateDetail(result.data), message: result.message }
    : { success: false, message: result.message || '加载套装模板失败' }
}

export async function createSetProductTemplate(payload: SetProductTemplatePayload): Promise<ApiOperationResult<SetProductTemplateDetail>> {
  const response: any = await request(`${API_BASE}/templates`, { method: 'POST', data: payload })
  const result = unwrapApiOperation<Record<string, unknown>>(response)
  return result.success
    ? { success: true, data: result.data ? transformSetProductTemplateDetail(result.data) : undefined, message: result.message }
    : { success: false, message: result.message || '保存套装模板失败' }
}

export async function updateSetProductTemplate(templateId: string, supplierCode: string, payload: SetProductTemplatePayload): Promise<ApiOperationResult<SetProductTemplateDetail>> {
  const response: any = await request(`${API_BASE}/templates/${encodeURIComponent(templateId)}`, {
    method: 'PUT',
    params: { supplierCode },
    data: payload,
  })
  const result = unwrapApiOperation<Record<string, unknown>>(response)
  return result.success
    ? { success: true, data: result.data ? transformSetProductTemplateDetail(result.data) : undefined, message: result.message }
    : { success: false, message: result.message || '更新套装模板失败' }
}

export async function deactivateSetProductTemplate(templateId: string, supplierCode: string): Promise<ApiOperationResult<void>> {
  const response: any = await request(`${API_BASE}/templates/${encodeURIComponent(templateId)}/deactivate`, {
    method: 'POST',
    params: { supplierCode },
  })
  const result = unwrapApiOperation<void>(response)
  return result.success
    ? { success: true, message: result.message }
    : { success: false, message: result.message || '停用套装模板失败' }
}

function transformCreateBatchResponse(raw: Record<string, unknown>): CreateBatchResponse {
  return {
    batchNumber: String(raw.batchNumber ?? ''),
    totalCreated: Number(raw.totalCreated ?? raw.totalCount ?? 0),
    normalProductCount: Number(raw.normalProductCount ?? raw.normalCount ?? 0),
    setProductCount: Number(raw.setProductCount ?? raw.setCount ?? 0),
  }
}

export async function createBatch(data: CreateBatchRequest): Promise<{ success: boolean; data?: CreateBatchResponse; message?: string }> {
  const response: any = await request(`${API_BASE}/batch`, {
    method: 'POST',
    data,
  })
  const res = response?.data ?? response
  if (res?.success !== undefined) {
    return {
      success: Boolean(res.success),
      data: res.data ? transformCreateBatchResponse(res.data) : undefined,
      message: res.message,
    }
  }
  if (response?.success !== undefined) {
    return {
      success: Boolean(response.success),
      data: response.data ? transformCreateBatchResponse(response.data) : undefined,
      message: response.message,
    }
  }
  return { success: true, data: res ? transformCreateBatchResponse(res) : undefined }
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
    barcode: raw.barcode ? String(raw.barcode) : '',
    productName: String(raw.productName ?? ''),
    productType: Number(raw.productType ?? 0),
    privateLabelPrice: raw.privateLabelPrice != null ? Number(raw.privateLabelPrice) : undefined,
    setQuantity: raw.setQuantity != null ? Number(raw.setQuantity) : undefined,
    setPrice: raw.setPrice != null ? Number(raw.setPrice) : undefined,
    parentItemNumber: raw.parentHBProductNo
      ? String(raw.parentHBProductNo)
      : raw.parentProductCode
        ? String(raw.parentProductCode)
        : raw.parentItemNumber
          ? String(raw.parentItemNumber)
          : undefined,
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
    // 页面统一使用详情模型的 itemNumber；API 契约字段名为 productCode。
    data: {
      items: items.map((item) => ({
        productCode: item.itemNumber,
        privateLabelPrice: item.privateLabelPrice,
      })),
    },
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
  return response
}

export async function createPrefixCode(data: { supplierCode: string; prefixName: string; prefixDescription?: string; isActive?: boolean; sortOrder?: number }): Promise<{ success: boolean; message?: string }> {
  const response: any = await request('/api/v1/productprefixcodes', {
    method: 'POST',
    data,
  })
  return response
}

export async function updatePrefixCode(prefixCode: string, data: { prefixName: string; prefixDescription?: string; isActive?: boolean; sortOrder?: number }): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}`, {
    method: 'PUT',
    data,
  })
  return response
}

export async function deletePrefixCode(prefixCode: string): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}`, {
    method: 'DELETE',
  })
  return response
}

export async function togglePrefixCodeStatus(prefixCode: string, isActive: boolean): Promise<{ success: boolean; message?: string }> {
  const response: any = await request(`/api/v1/productprefixcodes/${prefixCode}/status/${isActive}`, { method: 'PATCH' })
  return response
}
