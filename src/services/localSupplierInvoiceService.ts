import type { ApiResponse } from '../types/api'
import type {
  BatchExecuteActionsRequest,
  BatchExecuteActionsResult,
  BatchEditFields,
  BatchResultDto,
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
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  LocalSupplierInvoiceListDto,
  PasteDetailsRequest,
  UpdateHqProductsRequest,
  UpdateHqProductsResult,
  UpdateInvoiceRequest,
  UpdateToStorePricesResult,
  UpdateToStorePricesRequest,
} from '../types/localSupplierInvoice'
import request, { RequestError, unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/local-supplier-invoices'

function assertApiSuccess<T>(response: ApiResponse<T>, fallbackMessage: string): void {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
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
  return unwrapApiData(response)
}

export async function deleteDetails(invoiceGuid: string, detailGuids: string[]): Promise<void> {
  await request.delete(`${API_BASE}/${invoiceGuid}/details`, { data: detailGuids })
}

export async function checkProducts(data: CheckProductsRequest): Promise<CheckProductsResponse> {
  const response = await request.post<ApiResponse<CheckProductsResponse>>(`${API_BASE}/check-products`, data)
  return unwrapApiData(response)
}

export async function pasteDetails(data: PasteDetailsRequest): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/${data.invoiceGuid}/details/paste`, {
    mode: data.mode,
    items: data.items,
  })
  return unwrapApiData(response)
}

export async function batchUpdateDetailAction(
  invoiceGuid: string,
  detailGuids: string[],
  action: number,
): Promise<void> {
  await request.put(`${API_BASE}/${invoiceGuid}/details/batch-action`, { detailGuids, action })
}

export async function updateDetailAction(
  invoiceGuid: string,
  detailGuid: string,
  action: number,
): Promise<void> {
  await request.put(`${API_BASE}/${invoiceGuid}/details/${detailGuid}/action`, { action })
}

export async function updateToStorePrices(data: UpdateToStorePricesRequest): Promise<UpdateToStorePricesResult> {
  const response = await request.post<ApiResponse<UpdateToStorePricesResult>>(`${API_BASE}/update-to-store-prices`, data)
  assertApiSuccess(response, '更新到分店价格失败')
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

export async function batchUpdateDetails(
  invoiceGuid: string,
  items: InvoiceDetailUpsertItemDto[],
  editFields: BatchEditFields,
): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/${invoiceGuid}/details/batch-update`, {
    items,
    editFields,
  })
  return unwrapApiData(response)
}

export async function getBarcodeAbnormalDetails(invoiceGuid: string) {
  const response = await request.get<ApiResponse<{ details: any[] }>>(`${API_BASE}/${invoiceGuid}/barcode-abnormal-details`)
  return unwrapApiData(response)
}

export async function getProductsByBarcode(invoiceGuid: string, barcode: string) {
  const response = await request.get<ApiResponse<{ barcode: string; matchedProducts: any[] }>>(
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
  const response = await request.post<ApiResponse<BatchExecuteActionsResult>>(`${API_BASE}/${data.invoiceGuid}/details/batch-execute`, {
    detailGuids: data.detailGuids,
  })
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
