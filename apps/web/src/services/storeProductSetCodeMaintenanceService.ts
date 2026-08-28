import type { ApiResponse } from '../types/api'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/store-product-maintenance'

export type ProductCodeMode = 1 | 2

export type StoreProductMaintenanceDetail = {
  productCode: string
  productName: string
  productType?: number | null
}

export type StoreProductCodeRow = {
  setCodeId: string
  productCode: string
  barcode?: string | null
  purchasePrice?: number | null
  retailPrice?: number | null
  isActive: boolean
  setType: ProductCodeMode
}

export type StoreProductCodePage = {
  items: StoreProductCodeRow[]
  totalCount: number
  page: number
  pageSize: number
  hasMore: boolean
}

export type StoreProductSetCodeSnapshotItem = {
  setCodeId?: string
  barcode: string
  retailPrice?: number
  setType: ProductCodeMode
  isActive: boolean
}

export type SaveStoreProductSetCodeSnapshotRequest = {
  productCode: string
  storeCode: string
  expectedProductType?: number | null
  productType: ProductCodeMode
  expectedItems: StoreProductSetCodeSnapshotItem[]
  items: StoreProductSetCodeSnapshotItem[]
}

export type SaveStoreProductSetCodeSnapshotResult = {
  productCode: string
  storeCode: string
  productType: ProductCodeMode
  items: StoreProductCodeRow[]
}

type RawRecord = Record<string, unknown>

function toRecord(value: unknown): RawRecord {
  return value && typeof value === 'object' ? value as RawRecord : {}
}

function toNullableNumber(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) return value
  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
  }
  return null
}

function normalizeDetail(value: unknown): StoreProductMaintenanceDetail {
  const data = toRecord(value)
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ''),
    productName: String(data.productName ?? data.ProductName ?? ''),
    productType: toNullableNumber(data.productType ?? data.ProductType),
  }
}

function normalizeCodeRow(value: unknown, type: ProductCodeMode): StoreProductCodeRow {
  const data = toRecord(value)
  const responseSetType = toNullableNumber(data.setType ?? data.SetType)
  return {
    setCodeId: String(data.setCodeId ?? data.SetCodeId ?? ''),
    productCode: String(data.productCode ?? data.ProductCode ?? ''),
    barcode: (data.setBarcode ?? data.SetBarcode ?? data.barcode ?? data.Barcode) as string | null | undefined,
    purchasePrice: toNullableNumber(
      data.setPurchasePrice ?? data.SetPurchasePrice ?? data.purchasePrice ?? data.PurchasePrice,
    ),
    retailPrice: toNullableNumber(
      data.setRetailPrice ?? data.SetRetailPrice ?? data.retailPrice ?? data.RetailPrice,
    ),
    isActive: Boolean(data.isActive ?? data.IsActive ?? true),
    setType: responseSetType === 1 || responseSetType === 2 ? responseSetType : type,
  }
}

function normalizeSnapshotResult(value: unknown): SaveStoreProductSetCodeSnapshotResult {
  const data = toRecord(value)
  const rawProductType = toNullableNumber(data.productType ?? data.ProductType)
  if (rawProductType !== 1 && rawProductType !== 2) {
    throw new Error('商品条码快照返回了无效商品类型')
  }
  const rawItems = data.items ?? data.Items
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ''),
    storeCode: String(data.storeCode ?? data.StoreCode ?? ''),
    productType: rawProductType,
    items: Array.isArray(rawItems)
      ? rawItems.map((item) => normalizeCodeRow(item, rawProductType))
      : [],
  }
}

export async function getStoreProductMaintenanceDetail(
  productCode: string,
  storeCode?: string,
): Promise<StoreProductMaintenanceDetail> {
  const response = await request.get<ApiResponse<unknown>>(
    `${API_BASE}/${encodeURIComponent(productCode)}`,
    { params: { storeCode, includeCodes: false } },
  )
  return normalizeDetail(unwrapApiData(response))
}

export async function getStoreProductCodePage(
  productCode: string,
  options: {
    storeCode?: string
    type: ProductCodeMode
    page: number
    pageSize: number
  },
): Promise<StoreProductCodePage> {
  const response = await request.get<ApiResponse<unknown>>(
    `${API_BASE}/${encodeURIComponent(productCode)}/codes`,
    { params: options },
  )
  const data = toRecord(unwrapApiData(response))
  const rawItems = data.items ?? data.Items
  const page = Number(data.page ?? data.Page ?? options.page)
  const pageSize = Number(data.pageSize ?? data.PageSize ?? options.pageSize)
  const totalCount = Number(data.totalCount ?? data.TotalCount ?? 0)
  return {
    items: Array.isArray(rawItems) ? rawItems.map((item) => normalizeCodeRow(item, options.type)) : [],
    totalCount,
    page,
    pageSize,
    hasMore: Boolean(data.hasMore ?? data.HasMore ?? page * pageSize < totalCount),
  }
}

export async function updateStoreProductType(
  productCode: string,
  data: { productType: ProductCodeMode; storeCode?: string },
): Promise<void> {
  const response = await request.put<ApiResponse<unknown>>(
    `${API_BASE}/products/${encodeURIComponent(productCode)}/type`,
    data,
  )
  unwrapApiData(response)
}
export async function saveStoreProductSetCodeSnapshot(
  data: SaveStoreProductSetCodeSnapshotRequest,
): Promise<SaveStoreProductSetCodeSnapshotResult> {
  const response = await request.post<ApiResponse<unknown>>(
    `${API_BASE}/set-codes/save-snapshot`,
    data,
  )
  return normalizeSnapshotResult(unwrapApiData(response))
}

export async function createStoreProductSetCode(data: {
  productCode: string
  storeCode: string
  productType: ProductCodeMode
  barcode: string
  retailPrice?: number
  isActive: boolean
}): Promise<void> {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/set-codes`, data)
  unwrapApiData(response)
}

export async function updateStoreProductSetCode(
  setCodeId: string,
  data: { storeCode: string; barcode: string; retailPrice?: number; isActive: boolean },
): Promise<void> {
  const response = await request.put<ApiResponse<unknown>>(
    `${API_BASE}/set-codes/${encodeURIComponent(setCodeId)}`,
    data,
  )
  unwrapApiData(response)
}

export async function deleteStoreProductSetCode(setCodeId: string): Promise<void> {
  const response = await request.delete<ApiResponse<unknown>>(
    `${API_BASE}/set-codes/${encodeURIComponent(setCodeId)}`,
  )
  unwrapApiData(response)
}
