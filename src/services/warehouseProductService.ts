import type { ApiResponse } from '../types/api'
import request from '../utils/request'

const API_BASE = '/api/react/v1/product-warehouse'
const LOCAL_SUPPLIER_API_BASE = '/api/react/v1/local-suppliers'

export interface DomesticProductNotInWarehouseItem {
  productCode: string
  productName: string
  englishName?: string
  itemNumber: string
  barcode?: string
  productType: number
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  volume?: number
  supplierName?: string
  supplierId?: number
  hasSetProducts: boolean
  hasMultiCodes: boolean
}

export interface NonHotbargainProductNotInWarehouseItem {
  productCode: string
  itemNumber: string
  barcode?: string
  productName: string
  englishName?: string
  productType: number
  purchasePrice?: number
  retailPrice?: number
  localSupplierCode?: string
  localSupplierName?: string
  productImage?: string
}

export interface LocalSupplierOption {
  localSupplierCode: string
  name: string
}

export interface WarehouseImportListResult<T> {
  success: boolean
  data: T[]
  total: number
}

export interface WarehouseImportActionResult {
  success: boolean
  successCount?: number
  failedCount?: number
  errors?: string[]
  results?: Array<{ productCode: string; success: boolean; message?: string }>
  message?: string
}

interface WarehouseImportListQuery {
  page?: number
  pageSize?: number
  globalSearch?: string
  filters?: Record<string, string[]>
}

export interface ImportFromDomesticItem {
  productCode: string
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  volume?: number
}

export interface ImportFromDomesticPayload {
  items: ImportFromDomesticItem[]
  syncBranchPrice?: boolean
  syncMultiCodePrice?: boolean
}

interface ImportFromDomesticRequestBody {
  productCodes: string[]
  syncStorePrices: boolean
  syncMultiCodes: boolean
  priceOverrides?: Record<
    string,
    {
      domesticPrice?: number
      oemPrice: number
      importPrice: number
      volume?: number
    }
  >
}

function unwrapResponse<T>(response: unknown, emptyData: T): T {
  if (response && typeof response === 'object') {
    if ('data' in response && (response as { data?: T }).data !== undefined) {
      return (response as { data: T }).data
    }

    return response as T
  }

  return emptyData
}

function unwrapListResponse<T>(response: unknown): WarehouseImportListResult<T> {
  const raw = response as
    | {
        success?: boolean
        data?: unknown
        total?: number
        Success?: boolean
        Data?: unknown
        Total?: number
      }
    | undefined

  // Some endpoints return { success, data: [], total } directly,
  // while others may be wrapped as { data: { success, data: [], total } }.
  const result =
    raw && (typeof raw.success === 'boolean' || typeof raw.Success === 'boolean' || 'total' in raw || 'Total' in raw)
      ? raw
      : ((raw?.data ?? raw?.Data ?? response) as
          | {
              success?: boolean
              data?: T[]
              total?: number
              Success?: boolean
              Data?: T[]
              Total?: number
            }
          | undefined)

  return {
    success: result?.success ?? result?.Success ?? false,
    data: Array.isArray(result?.data) ? result.data : Array.isArray(result?.Data) ? result.Data : [],
    total: result?.total ?? result?.Total ?? 0,
  }
}

function buildImportFromDomesticBody(payload: ImportFromDomesticPayload): ImportFromDomesticRequestBody {
  const priceOverrides: ImportFromDomesticRequestBody['priceOverrides'] = {}

  payload.items.forEach((item) => {
    const hasOverride =
      item.domesticPrice !== undefined ||
      item.oemPrice !== undefined ||
      item.importPrice !== undefined ||
      item.volume !== undefined

    if (!hasOverride) {
      return
    }

    priceOverrides[item.productCode] = {
      domesticPrice: item.domesticPrice,
      oemPrice: item.oemPrice ?? 0,
      importPrice: item.importPrice ?? 0,
      volume: item.volume,
    }
  })

  return {
    productCodes: payload.items.map((item) => item.productCode),
    syncStorePrices: payload.syncBranchPrice ?? false,
    syncMultiCodes: payload.syncMultiCodePrice ?? false,
    priceOverrides: Object.keys(priceOverrides).length ? priceOverrides : undefined,
  }
}

export async function getDomesticProductsNotInWarehouse(
  query: WarehouseImportListQuery,
): Promise<WarehouseImportListResult<DomesticProductNotInWarehouseItem>> {
  const response = await request<unknown>(`${API_BASE}/domestic-not-in-warehouse`, {
    method: 'POST',
    data: query,
  })

  return unwrapListResponse<DomesticProductNotInWarehouseItem>(response)
}

export async function importFromDomestic(
  payload: ImportFromDomesticPayload,
): Promise<WarehouseImportActionResult> {
  const response = await request<unknown>(`${API_BASE}/import-from-domestic`, {
    method: 'POST',
    data: buildImportFromDomesticBody(payload),
  })

  return unwrapResponse(response, { success: false })
}

export async function getNonHotbargainProductsNotInWarehouse(
  query: WarehouseImportListQuery,
): Promise<WarehouseImportListResult<NonHotbargainProductNotInWarehouseItem>> {
  const response = await request<unknown>(`${API_BASE}/non-hb-not-in-warehouse`, {
    method: 'POST',
    data: query,
  })

  return unwrapListResponse<NonHotbargainProductNotInWarehouseItem>(response)
}

export async function importNonHotbargainProducts(productCodes: string[]): Promise<WarehouseImportActionResult> {
  const response = await request<unknown>(`${API_BASE}/import-non-hb`, {
    method: 'POST',
    data: { productCodes },
  })

  return unwrapResponse(response, { success: false })
}

export async function getActiveLocalSuppliers(): Promise<LocalSupplierOption[]> {
  const response = await request<ApiResponse<LocalSupplierOption[]> | LocalSupplierOption[]>(
    `${LOCAL_SUPPLIER_API_BASE}/active`,
  )

  return unwrapResponse(response, [])
}
