import type { ApiResponse, PagedResult } from '../types/api'
import type {
  ChinaSupplierItem,
  ChinaSupplierListParams,
  ChinaSupplierListResult,
  SaveChinaSupplierPayload,
  SyncChinaSupplierResult,
} from '../types/chinaSupplier'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

const API_BASE = '/api/v1/ChinaSuppliers'

type ChinaSupplierApiItem = Record<string, unknown>

function transformSupplier(raw: ChinaSupplierApiItem): ChinaSupplierItem {
  return {
    guid: String(raw.guid ?? raw.Guid ?? ''),
    supplierCode: String(raw.supplierCode ?? raw.SupplierCode ?? ''),
    supplierName: String(raw.supplierName ?? raw.SupplierName ?? ''),
    shopNumber: String(raw.shopNumber ?? raw.ShopNumber ?? '') || undefined,
    contactPerson: String(raw.contactPerson ?? raw.ContactPerson ?? '') || undefined,
    phone: String(raw.phone ?? raw.Phone ?? '') || undefined,
    email: String(raw.email ?? raw.Email ?? '') || undefined,
    storefrontPhoto: String(raw.storefrontPhoto ?? raw.StorefrontPhoto ?? '') || undefined,
    status: Number(raw.status ?? raw.Status ?? 0),
    remarks: String(raw.remarks ?? raw.Remarks ?? '') || undefined,
    createdAt: String(raw.fgC_CreateDate ?? raw.createdAt ?? raw.CreatedAt ?? '') || undefined,
    updatedAt: String(raw.fgC_LastModifyDate ?? raw.updatedAt ?? raw.UpdatedAt ?? '') || undefined,
  }
}

function normalizePagedSuppliers(
  payload: ApiResponse<PagedResult<ChinaSupplierApiItem>> | PagedResult<ChinaSupplierApiItem>,
  params: ChinaSupplierListParams,
): ChinaSupplierListResult {
  const result = unwrapPagedResult(payload)
  return {
    items: result.items
      .filter((item): item is ChinaSupplierApiItem => !!item && typeof item === 'object')
      .map(transformSupplier),
    total: result.total,
    page: result.page ?? params.page ?? 1,
    pageSize: result.pageSize ?? params.pageSize ?? 20,
  }
}

function toSavePayload(data: SaveChinaSupplierPayload) {
  return {
    supplierCode: data.supplierCode,
    supplierName: data.supplierName,
    shopNumber: data.shopNumber,
    contactPerson: data.contactPerson,
    phone: data.phone,
    email: data.email,
    storefrontPhoto: data.storefrontPhoto,
    status: data.status ?? 1,
    remarks: data.remarks,
  }
}

export async function getChinaSuppliers(params: ChinaSupplierListParams): Promise<ChinaSupplierListResult> {
  const response = await request.get<ApiResponse<PagedResult<ChinaSupplierApiItem>>>(API_BASE, {
    params: params as Record<string, unknown>,
  })
  return normalizePagedSuppliers(response, params)
}

export async function createChinaSupplier(data: SaveChinaSupplierPayload): Promise<ChinaSupplierItem> {
  const response = await request.post<ApiResponse<ChinaSupplierApiItem>>(API_BASE, toSavePayload(data))
  return transformSupplier(unwrapApiData(response) as ChinaSupplierApiItem)
}

export async function updateChinaSupplier(guid: string, data: SaveChinaSupplierPayload): Promise<ChinaSupplierItem> {
  const response = await request.put<ApiResponse<ChinaSupplierApiItem>>(`${API_BASE}/${guid}`, toSavePayload(data))
  return transformSupplier(unwrapApiData(response) as ChinaSupplierApiItem)
}

export async function deleteChinaSupplier(guid: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`${API_BASE}/${guid}`)
  return unwrapApiData(response)
}

export async function toggleChinaSupplierStatus(guid: string, status: number): Promise<ChinaSupplierItem> {
  const response = await request.patch<ApiResponse<ChinaSupplierApiItem>>(`${API_BASE}/${guid}/status/${status}`)
  return transformSupplier(unwrapApiData(response) as ChinaSupplierApiItem)
}

export async function checkSupplierCodeExists(supplierCode: string, excludeGuid?: string): Promise<boolean> {
  const response = await request.get<ApiResponse<boolean>>(`${API_BASE}/check-code/${encodeURIComponent(supplierCode)}`, {
    params: { excludeGuid },
  })
  return unwrapApiData(response)
}

export async function generateNextSupplierCode(): Promise<string> {
  const response = await request.get<ApiResponse<string>>(`${API_BASE}/generate-code`)
  return unwrapApiData(response)
}

export async function syncChinaSuppliersToHbSales(guids: string[]): Promise<SyncChinaSupplierResult> {
  const response = await request.post<ApiResponse<SyncChinaSupplierResult>>(`${API_BASE}/sync-to-hbsales`, guids)
  return unwrapApiData(response)
}

export async function getActiveChinaSuppliers(signal?: AbortSignal): Promise<ChinaSupplierItem[]> {
  const response = await request.get<ApiResponse<ChinaSupplierApiItem[]>>(`${API_BASE}/active`, {
    signal,
  })
  const data = unwrapApiData(response) ?? []
  return Array.isArray(data)
    ? data
        .filter((item): item is ChinaSupplierApiItem => !!item && typeof item === 'object')
        .map(transformSupplier)
    : []
}
