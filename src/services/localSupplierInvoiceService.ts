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
  GetInvoiceDetailResponse,
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  LocalSupplierInvoiceListDto,
  PasteDetailsRequest,
  UpdateInvoiceRequest,
  UpdateToStorePricesRequest,
} from '../types/localSupplierInvoice'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/local-supplier-invoices'

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

export async function updateToStorePrices(data: UpdateToStorePricesRequest): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/update-to-store-prices`, data)
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
  const response = await request.post<ApiResponse<BatchExecuteActionsResult>>(`${API_BASE}/${data.invoiceGuid}/details/batch-execute`, {
    detailGuids: data.detailGuids,
  })
  return unwrapApiData(response)
}

export async function pushInvoicesToHq(invoiceGuids: string[]): Promise<BatchResultDto> {
  const response = await request.post<ApiResponse<BatchResultDto>>(`${API_BASE}/push-to-hq`, invoiceGuids)
  return unwrapApiData(response)
}

export async function saveCheckResults(invoiceGuid: string, data: { results: any[] }): Promise<void> {
  await request.post(`${API_BASE}/${invoiceGuid}/save-check-results`, data)
}
