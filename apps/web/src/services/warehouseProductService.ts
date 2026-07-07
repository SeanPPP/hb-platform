import type { ApiResponse } from '../types/api'
import type { SyncResult } from '../types/container'
import request from '../utils/request'
import {
  HqProductSyncPollingCancelledError,
  HqProductSyncPollingTimeoutError,
  createHqSyncJobPoller,
  type HqProductSyncPollingOptions,
} from './productHqSyncPolling'

const API_BASE = '/api/react/v1/product-warehouse'
const LOCAL_SUPPLIER_API_BASE = '/api/react/v1/local-suppliers'

export interface DomesticProductNotInWarehouseItem {
  productCode: string
  productName: string
  englishName?: string
  itemNumber: string
  barcode?: string
  productImage?: string
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

export interface CreateSingleSetDetailInput {
  productCode: string
  quantity: number
  itemNumber?: string
  barcode?: string
  purchasePrice?: number
  retailPrice?: number
}

export interface CreateSingleMultiCodeDetailInput {
  barcode?: string
  retailPrice?: number
  purchasePrice?: number
  discountRate?: number
  autoPricing?: boolean
  isSpecialProduct?: boolean
  isActive?: boolean
}

export interface CreateSingleStorePriceInput {
  storeCode: string
  purchasePrice?: number
  retailPrice?: number
  discountRate?: number
  autoPricing?: boolean
  isSpecialProduct?: boolean
  isActive?: boolean
}

export interface CreateSingleWarehouseProductPayload {
  productType: 0 | 1 | 2
  itemNumber?: string
  barcode?: string
  chineseName: string
  englishName?: string
  productSpecification?: string
  domesticPrice?: number
  oemPrice: number
  importPrice: number
  volume?: number
  packingQuantity?: number
  middlePackQuantity?: number
  packingSize?: string
  material?: string
  remarks?: string
  categoryGuid?: string
  supplierCode: string
  isActive: boolean
  imageUrl?: string
  setType?: 1 | 2 | 3
  setItems?: CreateSingleSetDetailInput[]
  multiCodeItems?: CreateSingleMultiCodeDetailInput[]
  storePrices?: CreateSingleStorePriceInput[]
}

export interface CreateSingleWarehouseProductResponse {
  success: boolean
  message?: string
  productCode?: string
  itemNumber?: string
  barcode?: string
  barcodeExists?: boolean
  warnings?: string[]
}

export interface WarehouseProductListItem {
  id: string
  rowNumber?: number
  productCode: string
  name: string
  nameEn?: string
  itemNumber: string
  barcode?: string
  locationCodes?: string[]
  locationBarcodes?: string[]
  categoryName?: string
  warehouseCategoryGUID?: string
  categoryPath?: string
  domesticSupplierName?: string
  domesticSupplierCode?: string
  localSupplierName?: string
  localSupplierCode?: string
  domesticPrice?: number
  labelPrice?: number
  importPrice?: number
  volume?: number
  isVolumeFallback?: boolean
  packingQty?: number
  isPackingQtyFallback?: boolean
  minOrderQuantity?: number
  productType: 0 | 1 | 2
  productImage?: string
  isActive: boolean
  createdAt?: string
  updatedAt?: string
  updatedBy?: string
  middlePackQty?: number
}

export interface WarehouseProductsTableQuery {
  page: number
  pageSize: number
  searchText?: string
  supplierCode?: string
  filters?: Record<string, string[]>
  categoryFilter?: 'all' | 'uncategorized'
  categoryGuid?: string
  uncategorizedOnly?: boolean
  productType?: 0 | 1 | 2
  isActive?: boolean
  sortField?: string
  sortOrder?: 'ascend' | 'descend'
}

export interface WarehouseProductsTableResult {
  items: WarehouseProductListItem[]
  total: number
  page: number
  pageSize: number
}

export type WarehouseProductHqSyncJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface WarehouseProductHqSyncJobRequest {
  operationId: string
}

export interface WarehouseProductHqSyncJobResult {
  jobId: string
  operationId?: string
  status: WarehouseProductHqSyncJobStatus
  isDuplicateRequest?: boolean
  createdAt?: string
  completedAt?: string
  expiresAt?: string
  message?: string
  Message?: string
  isSuccess?: boolean
  IsSuccess?: boolean
  result?: SyncResult
  addedCount?: number
  AddedCount?: number
  updatedCount?: number
  UpdatedCount?: number
  errorCount?: number
  ErrorCount?: number
}

export type WarehouseProductHqSyncPollingOptions = HqProductSyncPollingOptions
export { HqProductSyncPollingCancelledError, HqProductSyncPollingTimeoutError }

export interface UpdateWarehouseProductFullPayload {
  productName?: string
  englishName?: string
  productSpecification?: string
  material?: string
  remark?: string
  packingQuantity?: number
  unitVolume?: number
  grossWeight?: number
  packingSize?: string
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  isActive: boolean
  productImage?: string
  productType?: 0 | 1 | 2
  middlePackQuantity?: number
  isAutoPricing?: boolean
  warehouseCategoryGUID?: string
  supplierCode?: string
  localSupplierCode?: string
}

export interface BatchToggleWarehouseProductsActivePayload {
  productCodes: string[]
  isActive: boolean
}

export interface DetectionItem {
  ProductCode?: string
  ItemNumber?: string
  Barcode?: string
  SupplierCode?: string
  supplierCode?: string
}

export interface DetectionResult {
  ProductCode?: string
  ItemNumber?: string
  SupplierCode?: string
  Barcode?: string
  Exists?: boolean
  MatchType?: string
  LocalProductCode?: string
  DomesticProductCode?: string
  HasProductCodeConflict?: boolean
  ConflictReason?: string
  exists?: boolean
  matchType?: string
  localProductCode?: string
  domesticProductCode?: string
  hasProductCodeConflict?: boolean
  conflictReason?: string
  ProductName?: string
  EnglishName?: string
  WarehouseDomesticPrice?: number
  WarehouseOEMPrice?: number
  WarehouseImportPrice?: number
  WarehouseVolume?: number
  PackingQuantity?: number
  DomesticPrice?: number
  DomesticOEMPrice?: number
  DomesticImportPrice?: number
  WarehouseIsActive?: boolean
  productCode?: string
  itemNumber?: string
  supplierCode?: string
  barcode?: string
  productName?: string
  englishName?: string
  warehouseDomesticPrice?: number
  warehouseOEMPrice?: number
  warehouseImportPrice?: number
  warehouseVolume?: number
  packingQuantity?: number
  domesticPrice?: number
  domesticOEMPrice?: number
  domesticImportPrice?: number
  importPrice?: number
}

export interface WarehouseProductBatchCreateItem {
  ProductCode?: string
  ItemNumber?: string
  Barcode?: string
  ChineseName?: string
  EnglishName?: string
  DomesticPrice?: number
  OEMPrice?: number
  ImportPrice?: number
  Volume?: number
  ImageUrl?: string
  IsSetProduct?: boolean
}

export interface WarehouseProductBatchUpdateItem {
  ProductCode?: string
  ItemNumber?: string
  DomesticPrice?: number
  OEMPrice?: number
  ImportPrice?: number
  Volume?: number
  PackingQuantity?: number
  MinOrderQuantity?: number
  IsActive?: boolean
}

export interface WarehouseProductBatchUpdateOptions {
  syncStorePurchasePrice?: boolean
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
  FailedCount?: number
  failed?: number
  Failed?: number
  errors?: string[]
  Errors?: string[]
  results?: Array<{ productCode: string; success: boolean; message?: string }>
  message?: string
}

interface WarehouseImportListQuery {
  page?: number
  pageSize?: number
  globalSearch?: string
  filters?: Record<string, string[]>
}

interface WarehouseTableResponseRaw {
  success?: boolean
  data?: unknown
  total?: number
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

function ensureApiSuccess(success?: boolean, message?: string, fallback?: string) {
  if (success === false) {
    throw new Error(message || fallback || '请求失败')
  }
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

function toNumber(value: unknown) {
  if (typeof value === 'number') {
    return value
  }

  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value)
    return Number.isNaN(parsed) ? undefined : parsed
  }

  return undefined
}

function toBoolean(value: unknown, fallback = false) {
  if (typeof value === 'boolean') {
    return value
  }

  if (typeof value === 'string') {
    if (value.toLowerCase() === 'true') {
      return true
    }
    if (value.toLowerCase() === 'false') {
      return false
    }
  }

  return fallback
}

function readString(...values: unknown[]) {
  for (const value of values) {
    if (typeof value === 'string') {
      const trimmed = value.trim()
      if (trimmed) {
        return trimmed
      }
      continue
    }

    if (typeof value === 'number') {
      return String(value)
    }
  }

  return undefined
}

function readRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function readStringArray(...values: unknown[]): string[] | undefined {
  for (const value of values) {
    if (Array.isArray(value)) {
      const items = value
        .map((item) => String(item ?? '').trim())
        .filter(Boolean)
      if (items.length) return items
    }
    if (typeof value === 'string' && value.trim()) {
      return value.split(',').map((item) => item.trim()).filter(Boolean)
    }
  }

  return undefined
}

function transformWarehouseProduct(raw: Record<string, unknown>): WarehouseProductListItem {
  const localSupplier = readRecord(raw.localSupplier ?? raw.LocalSupplier)

  return {
    id: readString(raw.productCode, raw.ProductCode, raw.id) ?? '',
    productCode: readString(raw.productCode, raw.ProductCode) ?? '',
    name: readString(raw.productName, raw.ProductName) ?? '',
    nameEn: readString(raw.englishName, raw.EnglishName),
    itemNumber: readString(raw.itemNumber, raw.ItemNumber) ?? '',
    barcode: readString(raw.barcode, raw.Barcode),
    locationCodes: readStringArray(raw.locationCodes, raw.LocationCodes, raw.locationCode, raw.LocationCode),
    locationBarcodes: readStringArray(raw.locationBarcodes, raw.LocationBarcodes),
    categoryName: readString(raw.categoryName, raw.CategoryName),
    warehouseCategoryGUID:
      readString(
        raw.warehouseCategoryGUID,
        raw.WarehouseCategoryGUID,
        raw.productCategoryGUID,
        raw.ProductCategoryGUID,
      ),
    categoryPath:
      readString(raw.categoryPath, raw.CategoryPath, raw.categoryFullPath, raw.CategoryFullPath),
    domesticSupplierName: readString(raw.domesticSupplierName, raw.DomesticSupplierName, raw.supplierName, raw.SupplierName),
    domesticSupplierCode: readString(raw.domesticSupplierCode, raw.DomesticSupplierCode, raw.supplierCode, raw.SupplierCode),
    localSupplierName: readString(
      raw.localSupplierName,
      raw.LocalSupplierName,
      localSupplier.localSupplierName,
      localSupplier.LocalSupplierName,
      localSupplier.name,
      localSupplier.Name,
    ),
    // 澳洲供应商保持独立读取，避免把国内 SupplierName 误显示到澳洲供应商列。
    localSupplierCode: readString(
      raw.localSupplierCode,
      raw.LocalSupplierCode,
      localSupplier.localSupplierCode,
      localSupplier.LocalSupplierCode,
      localSupplier.code,
      localSupplier.Code,
    ),
    domesticPrice: toNumber(raw.domesticPrice ?? raw.DomesticPrice),
    labelPrice: toNumber(raw.oemPrice ?? raw.OEMPrice),
    importPrice: toNumber(raw.importPrice ?? raw.ImportPrice),
    volume: toNumber(raw.volume ?? raw.Volume),
    isVolumeFallback: toBoolean(raw.isVolumeFallback ?? raw.IsVolumeFallback),
    packingQty: toNumber(raw.packingQuantity ?? raw.PackingQuantity),
    isPackingQtyFallback: toBoolean(raw.isPackingQuantityFallback ?? raw.IsPackingQuantityFallback),
    minOrderQuantity: toNumber(raw.minOrderQuantity ?? raw.MinOrderQuantity),
    productType: (toNumber(raw.productType ?? raw.ProductType) ?? 0) as 0 | 1 | 2,
    productImage: readString(raw.productImage, raw.ProductImage),
    isActive: toBoolean(raw.isActive ?? raw.IsActive, true),
    createdAt: readString(raw.createdAt, raw.CreatedAt),
    updatedAt: readString(raw.updatedAt, raw.UpdatedAt),
    updatedBy: readString(raw.updatedBy, raw.UpdatedBy),
    middlePackQty: toNumber(raw.middlePackQuantity ?? raw.MiddlePackQuantity),
  }
}

function normalizeWarehouseProductsTableResponse(
  payload: unknown,
  page: number,
  pageSize: number,
): WarehouseProductsTableResult {
  const result = payload as WarehouseTableResponseRaw | undefined
  const rawItems = Array.isArray(result?.data) ? result.data : []

  return {
    items: rawItems
      .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
      .map(transformWarehouseProduct)
      .map((item, index) => ({
        ...item,
        rowNumber: (page - 1) * pageSize + index + 1,
      })),
    total: typeof result?.total === 'number' ? result.total : 0,
    page,
    pageSize,
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

export async function createSingleWarehouseProduct(
  payload: CreateSingleWarehouseProductPayload,
): Promise<CreateSingleWarehouseProductResponse> {
  const response = await request<CreateSingleWarehouseProductResponse>(`${API_BASE}/create-single`, {
    method: 'POST',
    data: payload,
  })

  return response
}

export async function detectProducts(items: DetectionItem[]): Promise<DetectionResult[]> {
  const response = await request<unknown>(`${API_BASE}/detect`, {
    method: 'POST',
    data: { Items: items },
  })
  const result = unwrapResponse(response, { success: false, data: [] as DetectionResult[] })
  return Array.isArray((result as { data?: DetectionResult[] }).data)
    ? (result as { data: DetectionResult[] }).data
    : Array.isArray(result)
      ? (result as DetectionResult[])
      : []
}

export async function batchCreateProducts(items: WarehouseProductBatchCreateItem[]): Promise<WarehouseImportActionResult> {
  const response = await request<unknown>(`${API_BASE}/batch-create`, {
    method: 'POST',
    data: { Items: items },
  })
  const raw = response as { success?: boolean; isSuccess?: boolean; message?: string } | undefined
  ensureApiSuccess(raw?.success ?? raw?.isSuccess, raw?.message, '仓库批量创建失败')
  const result = unwrapResponse<WarehouseImportActionResult>(response, { success: false })
  ensureApiSuccess(result.success, result.message, '仓库批量创建失败')
  const failedCount = Number(result.failedCount ?? result.FailedCount ?? result.failed ?? result.Failed ?? 0)
  const errors = result.errors ?? result.Errors ?? []
  if (failedCount > 0) {
    throw new Error(result.message || errors.join('；') || '仓库批量创建部分失败')
  }
  return result
}

export async function batchUpdateWarehouseProducts(
  items: WarehouseProductBatchUpdateItem[],
  options: WarehouseProductBatchUpdateOptions = {},
): Promise<WarehouseImportActionResult> {
  const response = await request<unknown>(`${API_BASE}/batch-update`, {
    method: 'POST',
    data: {
      Items: items,
      ...(options.syncStorePurchasePrice === undefined ? {} : { SyncStorePurchasePrice: options.syncStorePurchasePrice }),
    },
  })
  const raw = response as { success?: boolean; isSuccess?: boolean; message?: string } | undefined
  ensureApiSuccess(raw?.success ?? raw?.isSuccess, raw?.message, '仓库批量更新失败')
  const result = unwrapResponse<WarehouseImportActionResult>(response, { success: false })
  ensureApiSuccess(result.success, result.message, '仓库批量更新失败')
  const failedCount = Number(result.failedCount ?? result.FailedCount ?? result.failed ?? result.Failed ?? 0)
  const errors = result.errors ?? result.Errors ?? []
  if (failedCount > 0) {
    throw new Error(result.message || errors.join('；') || '仓库批量更新部分失败')
  }
  return result
}

export async function getWarehouseProductsTable(
  query: WarehouseProductsTableQuery,
): Promise<WarehouseProductsTableResult> {
  const sanitizeFilters = (filters?: Record<string, unknown>): Record<string, string[]> | undefined => {
    const sanitizedEntries = Object.entries(filters ?? {}).flatMap(([field, values]) => {
      const validValues = Array.isArray(values)
        ? values.filter((value): value is string => typeof value === 'string' && value.trim().length > 0)
        : []

      return validValues.length > 0 ? [[field, validValues] as const] : []
    })

    return sanitizedEntries.length > 0 ? Object.fromEntries(sanitizedEntries) : undefined
  }

  const filters = sanitizeFilters({
    ...(query.filters ?? {}),
    ...(query.supplierCode ? { domesticSupplierCode: [query.supplierCode] } : {}),
    ...(query.productType !== undefined ? { productType: [String(query.productType)] } : {}),
    ...(query.isActive !== undefined ? { isActive: [String(query.isActive)] } : {}),
  })

  // 分类过滤继续走顶层字段，列头业务筛选统一进 Filters；保留 categoryFilter 兼容旧调用。
  const uncategorizedOnly = query.uncategorizedOnly === true || query.categoryFilter === 'uncategorized'

  const response = await request<unknown>(`${API_BASE}/table`, {
    method: 'POST',
    data: {
      Page: query.page,
      PageSize: query.pageSize,
      SortBy: query.sortField,
      SortOrder: query.sortOrder,
      GlobalSearch: query.searchText || undefined,
      Filters: filters,
      CategoryGuids: query.categoryGuid ? [query.categoryGuid] : undefined,
      IncludeSubCategories: true,
      UncategorizedOnly: query.categoryGuid ? false : uncategorizedOnly,
    },
  })

  return normalizeWarehouseProductsTableResponse(response, query.page, query.pageSize)
}

export async function syncWarehouseProductsFromHq(): Promise<SyncResult> {
  const response = await request.post<ApiResponse<SyncResult>>(`${API_BASE}/sync-from-hq`)
  const apiSuccess = response.success ?? response.isSuccess

  // 后端明确返回失败时，直接抛出后端 message，避免业务错误被静默吞掉。
  ensureApiSuccess(apiSuccess, response.message, '从HQ同步 WarehouseProduct 失败')
  const syncResult = response.data
  const syncSuccess = syncResult?.isSuccess ?? syncResult?.IsSuccess
  // 同步结果本身失败时也抛出，避免后续复用该 helper 时误判 resolved promise 为成功。
  ensureApiSuccess(syncSuccess, syncResult?.message ?? syncResult?.Message ?? response.message, '从HQ同步 WarehouseProduct 失败')

  return syncResult ?? {
    isSuccess: response.isSuccess ?? response.success,
    message: response.message,
  }
}

export function createWarehouseProductHqSyncJobPoller({
  jobId,
  getJob,
  ...options
}: WarehouseProductHqSyncPollingOptions & {
  jobId: string
  getJob: (jobId: string) => Promise<WarehouseProductHqSyncJobResult>
}) {
  return createHqSyncJobPoller<WarehouseProductHqSyncJobResult>({
    jobId,
    getJob,
    ...options,
  })
}

export async function createWarehouseProductHqSyncJob(
  payload: WarehouseProductHqSyncJobRequest,
): Promise<WarehouseProductHqSyncJobResult> {
  const response = await request.post<ApiResponse<WarehouseProductHqSyncJobResult>>(
    `${API_BASE}/sync-from-hq/jobs`,
    payload,
  )
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, '创建仓库商品 HQ 同步任务失败')
  return unwrapResponse(response.data, {
    jobId: '',
    status: 'Failed',
    message: response.message,
  })
}

export async function getWarehouseProductHqSyncJob(
  jobId: string,
): Promise<WarehouseProductHqSyncJobResult> {
  const response = await request.get<ApiResponse<WarehouseProductHqSyncJobResult>>(
    `${API_BASE}/sync-from-hq/jobs/${encodeURIComponent(jobId)}`,
  )
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, '查询仓库商品 HQ 同步任务失败')
  return unwrapResponse(response.data, {
    jobId,
    status: 'Failed',
    message: response.message,
  })
}

export async function updateWarehouseProductFull(
  productCode: string,
  payload: UpdateWarehouseProductFullPayload,
): Promise<{ success: boolean; message?: string }> {
  return request(`${API_BASE}/${productCode}/full-update`, {
    method: 'PUT',
    data: {
      ProductName: payload.productName,
      EnglishName: payload.englishName,
      ProductSpecification: payload.productSpecification,
      Material: payload.material,
      Remark: payload.remark,
      PackingQuantity: payload.packingQuantity,
      UnitVolume: payload.unitVolume,
      GrossWeight: payload.grossWeight,
      PackingSize: payload.packingSize,
      DomesticPrice: payload.domesticPrice,
      OEMPrice: payload.oemPrice,
      ImportPrice: payload.importPrice,
      IsActive: payload.isActive,
      ProductImage: payload.productImage,
      ProductType: payload.productType,
      MiddlePackQuantity: payload.middlePackQuantity,
      IsAutoPricing: payload.isAutoPricing,
      WarehouseCategoryGUID: payload.warehouseCategoryGUID,
      SupplierCode: payload.supplierCode,
      LocalSupplierCode: payload.localSupplierCode,
    },
  })
}

export async function batchToggleWarehouseProductsActive(
  payload: BatchToggleWarehouseProductsActivePayload,
): Promise<WarehouseImportActionResult> {
  const response = await request<unknown>(`${API_BASE}/batch-toggle-active`, {
    method: 'POST',
    data: payload,
  })

  return unwrapResponse(response, { success: false })
}

export async function bulkSetStatus(productCodes: string[], isActive: boolean): Promise<WarehouseImportActionResult> {
  return batchToggleWarehouseProductsActive({ productCodes, isActive })
}
