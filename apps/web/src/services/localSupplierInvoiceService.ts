import type { ApiResponse } from '../types/api'
import type {
  BatchExecuteActionsRequest,
  BatchExecuteActionsResult,
  BatchEditFields,
  BatchResultDto,
  CheckProductsJobResult,
  CheckInvoiceNoRequest,
  CheckInvoiceNoResponse,
  CheckProductsRequest,
  CheckProductsResponse,
  EnsureHqProductsRequest,
  EnsureHqProductsResult,
  GetInvoiceDetailResponse,
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceHqSyncRequest,
  LocalSupplierInvoiceHqSyncResult,
  LocalSupplierInvoiceImportConfirmRequest,
  LocalSupplierInvoiceImportConfirmResponse,
  LocalSupplierInvoiceImportPreviewResponse,
  LocalSupplierPurchaseSalesAnalysisQueryDto,
  LocalSupplierPurchaseSalesAnalysisResponseDto,
  LocalSupplierPurchaseSalesAnalysisRowDto,
  LocalSupplierPurchaseSalesAnalysisStoreOptionDto,
  LocalSupplierPurchaseSalesAnalysisSupplierOptionDto,
  LocalSupplierInvoiceSalesAnalysisItemDto,
  LocalSupplierInvoiceSalesAnalysisResponseDto,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  LocalSupplierInvoiceListDto,
  PasteDetailsRequest,
  PasteDetailsJobResult,
  ProductsByBarcodeResponse,
  UpdateHqProductsJobResult,
  UpdateHqProductsRequest,
  UpdateHqProductsResult,
  UpdateLastPurchasePricesRequest,
  UpdateLastPurchasePricesResult,
  UpdateInvoiceRequest,
  UpdateToStorePricesJobResult,
  UpdateToStorePricesResult,
  UpdateToStorePricesRequest,
} from '../types/localSupplierInvoice'
import request, { RequestError, unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/local-supplier-invoices'
const PURCHASE_SALES_ANALYSIS_API_BASE = `${API_BASE}/purchase-sales-analysis`
const PURCHASE_SALES_ANALYSIS_ALLOWED_PAGE_SIZES = new Set([50, 100, 200])

function readNumber(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readOptionalNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function readString(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function assertApiSuccess<T>(response: ApiResponse<T>, fallbackMessage: string): void {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }
}

function normalizeSalesAnalysisItem(raw: unknown): LocalSupplierInvoiceSalesAnalysisItemDto | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const detailGUID = readString(record.detailGUID ?? record.DetailGUID)
  if (!detailGUID) {
    return null
  }

  return {
    detailGUID,
    productCode: readString(record.productCode ?? record.ProductCode),
    itemNumber: readString(record.itemNumber ?? record.ItemNumber),
    barcode: readString(record.barcode ?? record.Barcode),
    productName: readString(record.productName ?? record.ProductName),
    productImage: readString(record.productImage ?? record.ProductImage),
    specification: readString(record.specification ?? record.Specification),
    unit: readString(record.unit ?? record.Unit),
    quantity: readOptionalNumber(record.quantity ?? record.Quantity) ?? undefined,
    purchasePrice: readOptionalNumber(record.purchasePrice ?? record.PurchasePrice) ?? undefined,
    retailPrice: readOptionalNumber(record.retailPrice ?? record.RetailPrice) ?? undefined,
    amount: readOptionalNumber(record.amount ?? record.Amount) ?? undefined,
    salesQty30: readNumber(record.salesQty30 ?? record.SalesQty30),
    salesQty60: readNumber(record.salesQty60 ?? record.SalesQty60),
    salesQty90: readNumber(record.salesQty90 ?? record.SalesQty90),
    previousPurchaseDate: readString(record.previousPurchaseDate ?? record.PreviousPurchaseDate) ?? null,
    previousToCurrentDays: readOptionalNumber(record.previousToCurrentDays ?? record.PreviousToCurrentDays),
    salesSincePreviousPurchase: readOptionalNumber(record.salesSincePreviousPurchase ?? record.SalesSincePreviousPurchase),
    salesSincePreviousPurchase30: readOptionalNumber(record.salesSincePreviousPurchase30 ?? record.SalesSincePreviousPurchase30),
    salesSincePreviousPurchase60: readOptionalNumber(record.salesSincePreviousPurchase60 ?? record.SalesSincePreviousPurchase60),
    salesSincePreviousPurchase90: readOptionalNumber(record.salesSincePreviousPurchase90 ?? record.SalesSincePreviousPurchase90),
    salesStatisticLastUpdate: readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
  }
}

function normalizeSalesAnalysisResponse(raw: unknown): LocalSupplierInvoiceSalesAnalysisResponseDto {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {}
  const items = Array.isArray(record.items ?? record.Items)
    ? ((record.items ?? record.Items) as unknown[])
        .map(normalizeSalesAnalysisItem)
        .filter((item): item is LocalSupplierInvoiceSalesAnalysisItemDto => item !== null)
    : []

  return {
    invoiceGUID: readString(record.invoiceGUID ?? record.InvoiceGUID) ?? '',
    invoiceNo: readString(record.invoiceNo ?? record.InvoiceNo),
    storeCode: readString(record.storeCode ?? record.StoreCode),
    storeName: readString(record.storeName ?? record.StoreName),
    supplierCode: readString(record.supplierCode ?? record.SupplierCode),
    supplierName: readString(record.supplierName ?? record.SupplierName),
    orderDate: readString(record.orderDate ?? record.OrderDate) ?? null,
    inboundDate: readString(record.inboundDate ?? record.InboundDate) ?? null,
    analysisDate: readString(record.analysisDate ?? record.AnalysisDate) ?? null,
    salesStatisticLastUpdate: readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
    items,
    calculationNote:
      readString(record.calculationNote ?? record.CalculationNote) ??
      '进货后30/60/90天销量从本次进货日期次日开始统计；上次到本次区间销量仍按历史区间显示。',
  }
}

function normalizePurchaseSalesAnalysisPageSize(value: unknown) {
  return typeof value === 'number' && PURCHASE_SALES_ANALYSIS_ALLOWED_PAGE_SIZES.has(value) ? value : 100
}

function normalizePurchaseSalesAnalysisRow(raw: unknown): LocalSupplierPurchaseSalesAnalysisRowDto | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const storeCode = readString(record.storeCode ?? record.StoreCode)
  const productCode = readString(record.productCode ?? record.ProductCode)
  const supplierCode = readString(record.supplierCode ?? record.SupplierCode)
  if (!storeCode || !productCode || !supplierCode) {
    return null
  }

  return {
    storeCode,
    storeName: readString(record.storeName ?? record.StoreName),
    productCode,
    itemNumber: readString(record.itemNumber ?? record.ItemNumber),
    barcode: readString(record.barcode ?? record.Barcode),
    productName: readString(record.productName ?? record.ProductName),
    productImage: readString(record.productImage ?? record.ProductImage),
    supplierCode,
    supplierName: readString(record.supplierName ?? record.SupplierName),
    latestPurchaseDate: readString(record.latestPurchaseDate ?? record.LatestPurchaseDate) ?? null,
    latestPurchaseQty: readOptionalNumber(record.latestPurchaseQty ?? record.LatestPurchaseQty),
    previousPurchaseDate: readString(record.previousPurchaseDate ?? record.PreviousPurchaseDate) ?? null,
    previousPurchaseQty: readOptionalNumber(record.previousPurchaseQty ?? record.PreviousPurchaseQty),
    purchaseIntervalDays: readOptionalNumber(record.purchaseIntervalDays ?? record.PurchaseIntervalDays),
    salesBetweenPurchases: readOptionalNumber(record.salesBetweenPurchases ?? record.SalesBetweenPurchases),
    salesQty30: readNumber(record.salesQty30 ?? record.SalesQty30),
    salesQty60: readNumber(record.salesQty60 ?? record.SalesQty60),
    salesQty90: readNumber(record.salesQty90 ?? record.SalesQty90),
    salesStatisticLastUpdate:
      readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
  }
}

function normalizePurchaseSalesAnalysisResponse(raw: unknown): LocalSupplierPurchaseSalesAnalysisResponseDto {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {}
  const items = Array.isArray(record.items ?? record.Items)
    ? ((record.items ?? record.Items) as unknown[])
        .map(normalizePurchaseSalesAnalysisRow)
        .filter((item): item is LocalSupplierPurchaseSalesAnalysisRowDto => item !== null)
    : []

  return {
    items,
    total: readNumber(record.total ?? record.Total),
    page: readNumber(record.page ?? record.Page, 1),
    pageSize: normalizePurchaseSalesAnalysisPageSize(record.pageSize ?? record.PageSize),
    salesStatisticLastUpdate:
      readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
    calculationNote:
      readString(record.calculationNote ?? record.CalculationNote) ??
      '进货按订单日期范围过滤、按进货发生日期汇总；最近一次后的30/60/90天销量从最近进货当天开始统计。',
  }
}

function normalizePurchaseSalesAnalysisStoreOptions(raw: unknown): LocalSupplierPurchaseSalesAnalysisStoreOptionDto[] {
  const items = Array.isArray(raw) ? raw : Array.isArray((raw as { data?: unknown[] } | null)?.data) ? (raw as { data: unknown[] }).data : []
  return items
    .map((item) => {
      if (!item || typeof item !== 'object') {
        return null
      }
      const record = item as Record<string, unknown>
      const label = readString(record.label ?? record.Label)
      const value = readString(record.value ?? record.Value)
      if (!label || !value) {
        return null
      }
      return { label, value }
    })
    .filter((item): item is LocalSupplierPurchaseSalesAnalysisStoreOptionDto => item !== null)
}

function normalizePurchaseSalesAnalysisSupplierOptions(raw: unknown): LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[] {
  return normalizePurchaseSalesAnalysisStoreOptions(raw)
}

function buildPurchaseSalesAnalysisQuery(query: LocalSupplierPurchaseSalesAnalysisQueryDto) {
  return {
    ...query,
    page: typeof query.page === 'number' && query.page > 0 ? query.page : 1,
    pageSize: normalizePurchaseSalesAnalysisPageSize(query.pageSize),
  }
}

export async function getInvoiceGrid(data: Record<string, unknown>) {
  const response = await request.post<ApiResponse<{ items: LocalSupplierInvoiceListDto[]; total: number; page?: number; pageSize?: number }>>(
    `${API_BASE}/grid`,
    data,
  )
  return unwrapApiData(response)
}

export async function getInvoice(invoiceGuid: string): Promise<LocalSupplierInvoiceDetailDto> {
  const response = await request.get<ApiResponse<LocalSupplierInvoiceDetailDto>>(`${API_BASE}/${invoiceGuid}`)
  return unwrapApiData(response)
}

export async function getInvoiceDetails(invoiceGuid: string): Promise<LocalSupplierInvoiceItemDto[]> {
  const response = await request.get<ApiResponse<LocalSupplierInvoiceItemDto[]>>(`${API_BASE}/${invoiceGuid}/details`)
  return unwrapApiData(response) ?? []
}

export async function getInvoiceDetail(invoiceGuid: string): Promise<GetInvoiceDetailResponse> {
  const response = await request.get<ApiResponse<GetInvoiceDetailResponse>>(`${API_BASE}/${invoiceGuid}/full`)
  return unwrapApiData(response)
}

export async function getInvoiceSalesAnalysis(
  invoiceGuid: string,
  signal?: AbortSignal,
): Promise<LocalSupplierInvoiceSalesAnalysisResponseDto> {
  const response = await request.get<ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>>(
    `${API_BASE}/${encodeURIComponent(invoiceGuid)}/sales-analysis`,
    { signal },
  )
  return normalizeSalesAnalysisResponse(unwrapApiData(response))
}

export async function getLocalSupplierPurchaseSalesAnalysis(
  query: LocalSupplierPurchaseSalesAnalysisQueryDto,
  signal?: AbortSignal,
): Promise<LocalSupplierPurchaseSalesAnalysisResponseDto> {
  const response = await request.get<
    ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto> | LocalSupplierPurchaseSalesAnalysisResponseDto
  >(PURCHASE_SALES_ANALYSIS_API_BASE, {
    params: buildPurchaseSalesAnalysisQuery(query) as Record<string, unknown>,
    signal,
  })

  return normalizePurchaseSalesAnalysisResponse(unwrapApiData(response))
}

export async function getLocalSupplierPurchaseSalesAnalysisStoreOptions(): Promise<LocalSupplierPurchaseSalesAnalysisStoreOptionDto[]> {
  const response = await request.get<
    ApiResponse<LocalSupplierPurchaseSalesAnalysisStoreOptionDto[]> | LocalSupplierPurchaseSalesAnalysisStoreOptionDto[]
  >(`${PURCHASE_SALES_ANALYSIS_API_BASE}/store-options`)
  return normalizePurchaseSalesAnalysisStoreOptions(unwrapApiData(response))
}

export async function getLocalSupplierPurchaseSalesAnalysisSupplierOptions(
  storeCode?: string,
): Promise<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[]> {
  const response = await request.get<
    ApiResponse<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[]> | LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[]
  >(`${PURCHASE_SALES_ANALYSIS_API_BASE}/supplier-options`, {
    params: storeCode ? { storeCode } : undefined,
  })
  return normalizePurchaseSalesAnalysisSupplierOptions(unwrapApiData(response))
}

export const __localSupplierInvoiceServiceTestOnly = {
  buildPurchaseSalesAnalysisQuery,
  normalizePurchaseSalesAnalysisResponse,
  normalizePurchaseSalesAnalysisStoreOptions,
  normalizePurchaseSalesAnalysisSupplierOptions,
  normalizePurchaseSalesAnalysisPageSize,
}

export async function createInvoice(data: {
  storeCode: string
  supplierCode: string
  invoiceNo: string
  orderDate?: string
  inboundDate?: string
  remarks?: string
}): Promise<string> {
  const response = await request.post<ApiResponse<string>>(API_BASE, data)
  return unwrapApiData(response)
}

export async function updateInvoice(invoiceGuid: string, data: UpdateInvoiceRequest): Promise<LocalSupplierInvoiceDetailDto> {
  const response = await request.put<ApiResponse<LocalSupplierInvoiceDetailDto>>(`${API_BASE}/${invoiceGuid}`, data)
  return unwrapApiData(response)
}

export async function deleteInvoice(invoiceGuid: string): Promise<void> {
  await request.delete(`${API_BASE}/${invoiceGuid}`)
}

export async function batchUpsertDetails(invoiceGuid: string, items: InvoiceDetailUpsertItemDto[]): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(
    `${API_BASE}/${invoiceGuid}/details/batch-upsert`,
    items,
  )
  assertApiSuccess(response, '保存明细失败')
  return unwrapApiData(response)
}

export async function deleteDetails(invoiceGuid: string, detailGuids: string[]): Promise<void> {
  await request.delete(`${API_BASE}/${invoiceGuid}/details`, { data: detailGuids })
}

export async function checkProducts(data: CheckProductsRequest): Promise<CheckProductsResponse> {
  const response = await request.post<ApiResponse<CheckProductsResponse>>(`${API_BASE}/check-products`, data)
  return unwrapApiData(response)
}

export async function startCheckProductsJob(data: CheckProductsRequest): Promise<CheckProductsJobResult> {
  const response = await request.post<ApiResponse<CheckProductsJobResult>>(`${API_BASE}/check-products/jobs`, data)
  assertApiSuccess(response, '创建商品检测任务失败')
  return unwrapApiData(response)
}

export async function getCheckProductsJob(invoiceGuid: string, jobId: string): Promise<CheckProductsJobResult> {
  const response = await request.get<ApiResponse<CheckProductsJobResult>>(
    `${API_BASE}/${invoiceGuid}/check-products/jobs/${encodeURIComponent(jobId)}`,
  )
  assertApiSuccess(response, '查询商品检测任务失败')
  return unwrapApiData(response)
}

export async function pasteDetails(data: PasteDetailsRequest): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/${data.invoiceGuid}/details/paste`, {
    mode: data.mode,
    items: data.items,
  })
  return unwrapApiData(response)
}

export async function startPasteDetailsJob(data: PasteDetailsRequest): Promise<PasteDetailsJobResult> {
  const response = await request.post<ApiResponse<PasteDetailsJobResult>>(`${API_BASE}/${data.invoiceGuid}/details/paste/jobs`, {
    mode: data.mode,
    items: data.items,
  })
  assertApiSuccess(response, '创建粘贴明细任务失败')
  return unwrapApiData(response)
}

export async function getPasteDetailsJob(invoiceGuid: string, jobId: string): Promise<PasteDetailsJobResult> {
  const response = await request.get<ApiResponse<PasteDetailsJobResult>>(
    `${API_BASE}/${invoiceGuid}/details/paste/jobs/${encodeURIComponent(jobId)}`,
  )
  assertApiSuccess(response, '查询粘贴明细任务失败')
  return unwrapApiData(response)
}

export async function batchUpdateDetailAction(
  invoiceGuid: string,
  detailGuids: string[],
  action: number,
): Promise<void> {
  const response = await request.put<ApiResponse<void>>(`${API_BASE}/${invoiceGuid}/details/batch-action`, { detailGuids, action })
  assertApiSuccess(response, '批量设置操作类型失败')
}

export async function updateDetailAction(
  invoiceGuid: string,
  detailGuid: string,
  action: number,
): Promise<void> {
  const response = await request.put<ApiResponse<void>>(`${API_BASE}/${invoiceGuid}/details/${detailGuid}/action`, { action })
  assertApiSuccess(response, '更新操作类型失败')
}

export async function updateToStorePrices(data: UpdateToStorePricesRequest): Promise<UpdateToStorePricesResult> {
  const response = await request.post<ApiResponse<UpdateToStorePricesResult>>(`${API_BASE}/update-to-store-prices`, data)
  assertApiSuccess(response, '更新到分店价格失败')
  return unwrapApiData(response)
}

export async function startUpdateToStorePricesJob(data: UpdateToStorePricesRequest): Promise<UpdateToStorePricesJobResult> {
  const response = await request.post<ApiResponse<UpdateToStorePricesJobResult>>(`${API_BASE}/update-to-store-prices/jobs`, data)
  assertApiSuccess(response, '创建更新到分店价格任务失败')
  return unwrapApiData(response)
}

export async function getUpdateToStorePricesJob(jobId: string): Promise<UpdateToStorePricesJobResult> {
  const response = await request.get<ApiResponse<UpdateToStorePricesJobResult>>(
    `${API_BASE}/update-to-store-prices/jobs/${encodeURIComponent(jobId)}`,
  )
  assertApiSuccess(response, '查询更新到分店价格任务失败')
  return unwrapApiData(response)
}

export async function ensureHqProducts(
  invoiceGuid: string,
  data: EnsureHqProductsRequest,
): Promise<EnsureHqProductsResult> {
  const response = await request.post<ApiResponse<EnsureHqProductsResult>>(
    `${API_BASE}/${invoiceGuid}/details/ensure-hq-products`,
    data,
  )
  assertApiSuccess(response, '同步商品到HQ失败')
  return unwrapApiData(response)
}

export async function updateHqProducts(
  invoiceGuid: string,
  data: UpdateHqProductsRequest,
): Promise<UpdateHqProductsResult> {
  const response = await request.post<ApiResponse<UpdateHqProductsResult>>(
    `${API_BASE}/${invoiceGuid}/details/update-hq-products`,
    data,
  )
  assertApiSuccess(response, '更新HQ商品失败')
  return unwrapApiData(response)
}

export async function startUpdateHqProductsJob(
  invoiceGuid: string,
  data: UpdateHqProductsRequest,
): Promise<UpdateHqProductsJobResult> {
  const response = await request.post<ApiResponse<UpdateHqProductsJobResult>>(
    `${API_BASE}/${invoiceGuid}/details/update-hq-products/jobs`,
    data,
  )
  assertApiSuccess(response, '创建更新HQ商品任务失败')
  return unwrapApiData(response)
}

export async function getUpdateHqProductsJob(
  invoiceGuid: string,
  jobId: string,
): Promise<UpdateHqProductsJobResult> {
  const response = await request.get<ApiResponse<UpdateHqProductsJobResult>>(
    `${API_BASE}/${invoiceGuid}/details/update-hq-products/jobs/${encodeURIComponent(jobId)}`,
  )
  assertApiSuccess(response, '查询更新HQ商品任务失败')
  return unwrapApiData(response)
}

export async function batchUpdateDetails(
  invoiceGuid: string,
  items: InvoiceDetailUpsertItemDto[],
  editFields: BatchEditFields,
): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/${invoiceGuid}/details/batch-update`, {
    items,
    editFields,
  })
  assertApiSuccess(response, '批量编辑明细失败')
  return unwrapApiData(response)
}

export async function updateLastPurchasePrices(
  invoiceGuid: string,
  data: UpdateLastPurchasePricesRequest,
): Promise<UpdateLastPurchasePricesResult> {
  const response = await request.post<ApiResponse<UpdateLastPurchasePricesResult>>(
    `${API_BASE}/${invoiceGuid}/details/update-last-purchase-prices`,
    data,
  )
  assertApiSuccess(response, '更新上次进货价失败')
  return unwrapApiData(response)
}

export async function getBarcodeAbnormalDetails(invoiceGuid: string) {
  const response = await request.get<ApiResponse<{ details: any[] }>>(`${API_BASE}/${invoiceGuid}/barcode-abnormal-details`)
  return unwrapApiData(response)
}

export async function getProductsByBarcode(invoiceGuid: string, barcode: string): Promise<ProductsByBarcodeResponse> {
  const response = await request.get<ApiResponse<ProductsByBarcodeResponse>>(
    `${API_BASE}/${invoiceGuid}/products-by-barcode`,
    { params: { barcode } },
  )
  return unwrapApiData(response)
}

export async function getProductsByProductCode(invoiceGuid: string, productCode: string) {
  const response = await request.get<ApiResponse<{ productCode: string; matchedProducts: any[] }>>(
    `${API_BASE}/${invoiceGuid}/products-by-product-code`,
    { params: { productCode } },
  )
  return unwrapApiData(response)
}

export async function checkInvoiceNoExists(data: CheckInvoiceNoRequest): Promise<CheckInvoiceNoResponse> {
  const response = await request.post<ApiResponse<CheckInvoiceNoResponse>>(`${API_BASE}/check-invoice-no`, data)
  return unwrapApiData(response)
}

export async function batchExecuteActions(data: BatchExecuteActionsRequest): Promise<BatchExecuteActionsResult> {
  if (!data.detailGuids.length) {
    throw new Error('请选择要执行的明细')
  }
  if (!data.expectedActions.length || data.confirmedCreateProductCount == null || !data.confirmedAt) {
    throw new Error('请先确认批量执行操作')
  }
  const response = await request.post<ApiResponse<BatchExecuteActionsResult>>(`${API_BASE}/${data.invoiceGuid}/details/batch-execute`, {
    detailGuids: data.detailGuids,
    expectedActions: data.expectedActions,
    confirmedCreateProductCount: data.confirmedCreateProductCount,
    confirmedAt: data.confirmedAt,
    newProductProductTypeSelections: data.newProductProductTypeSelections ?? [],
  })
  assertApiSuccess(response, '批量执行操作失败')
  return unwrapApiData(response)
}

export async function previewInvoiceImport(file: File): Promise<LocalSupplierInvoiceImportPreviewResponse> {
  const formData = new FormData()
  formData.append('file', file)
  const response = await request.post<ApiResponse<LocalSupplierInvoiceImportPreviewResponse>>(
    `${API_BASE}/import/preview`,
    formData,
  )
  // 上传走统一 request 认证链路后，仍要保留 200 + success=false 的业务失败判断。
  assertApiSuccess(response, '预览导入文件失败')
  return unwrapApiData(response)
}

export async function confirmInvoiceImport(
  payload: LocalSupplierInvoiceImportConfirmRequest,
): Promise<LocalSupplierInvoiceImportConfirmResponse> {
  const response = await request.post<ApiResponse<LocalSupplierInvoiceImportConfirmResponse>>(
    `${API_BASE}/import/confirm`,
    payload,
  )
  assertApiSuccess(response, '确认创建导入进货单失败')
  return unwrapApiData(response)
}

export async function pushInvoicesToHq(invoiceGuids: string[]): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/push-to-hq`, invoiceGuids)
  return unwrapApiData(response)
}

export async function syncInvoicesFromHq(data: LocalSupplierInvoiceHqSyncRequest): Promise<LocalSupplierInvoiceHqSyncResult> {
  let response: ApiResponse<LocalSupplierInvoiceHqSyncResult>
  try {
    response = await request.post<ApiResponse<LocalSupplierInvoiceHqSyncResult>>(`${API_BASE}/sync-from-hq`, data)
  } catch (error) {
    if (error instanceof RequestError) {
      const payload = error.payload as ApiResponse<LocalSupplierInvoiceHqSyncResult> | undefined
      const syncResult = payload?.data ?? (payload?.details as LocalSupplierInvoiceHqSyncResult | undefined)
      if (syncResult) {
        throw new RequestError(payload?.message || error.message, error.status, {
          ...payload,
          data: syncResult,
        })
      }
    }
    throw error
  }

  // 这里显式识别业务失败，避免后端返回 200 + success=false 时被误当成成功。
  assertApiSuccess(response, '从HQ同步失败')

  return unwrapApiData(response)
}

export async function saveCheckResults(invoiceGuid: string, data: { results: any[] }): Promise<void> {
  await request.post(`${API_BASE}/${invoiceGuid}/save-check-results`, data)
}
