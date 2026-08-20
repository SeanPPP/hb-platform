import type {
  LocalSupplierProductSalesAnalysisBranch,
  LocalSupplierProductSalesAnalysisCandidate,
  LocalSupplierProductSalesAnalysisDaily,
  LocalSupplierProductSalesAnalysisEnvelope,
  LocalSupplierProductSalesAnalysisInvoiceDetail,
  LocalSupplierProductSalesAnalysisOptions,
  LocalSupplierProductSalesAnalysisPaged,
  LocalSupplierProductSalesAnalysisRequest,
  LocalSupplierProductSalesAnalysisSummary,
  LocalSupplierProductSalesAnalysisSummaryRow,
  LocalSupplierProductSalesAnalysisTotals,
} from '../types/localSupplierProductSalesAnalysis'
import request from '../utils/request'

const API_BASE = '/api/react/v1/local-supplier-product-sales-analysis'
type UnknownRecord = Record<string, unknown>

function asRecord(value: unknown): UnknownRecord | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as UnknownRecord : null
}
function pick(record: UnknownRecord, camel: string, pascal: string): unknown { return record[camel] ?? record[pascal] }
function stringValue(value: unknown): string | undefined { return typeof value === 'string' && value.trim() ? value : undefined }
function numberValue(value: unknown, fallback = 0): number { return typeof value === 'number' && Number.isFinite(value) ? value : fallback }
function nullableNumber(value: unknown): number | null { return typeof value === 'number' && Number.isFinite(value) ? value : null }
function dateValue(value: unknown): string | undefined { const date = stringValue(value); return date && /^\d{4}-\d{2}-\d{2}/.test(date) ? date.slice(0, 10) : undefined }

function totals(raw: unknown): LocalSupplierProductSalesAnalysisTotals {
  const record = asRecord(raw) ?? {}
  return {
    purchaseQuantity: numberValue(pick(record, 'purchaseQuantity', 'PurchaseQuantity')),
    purchaseAmount: numberValue(pick(record, 'purchaseAmount', 'PurchaseAmount')),
    netSalesQuantity: numberValue(pick(record, 'netSalesQuantity', 'NetSalesQuantity')),
    netSalesAmount: numberValue(pick(record, 'netSalesAmount', 'NetSalesAmount')),
    sellThroughRate: nullableNumber(pick(record, 'sellThroughRate', 'SellThroughRate')),
  }
}

function candidate(raw: unknown): LocalSupplierProductSalesAnalysisCandidate | null {
  const record = asRecord(raw); const productCode = record && stringValue(pick(record, 'productCode', 'ProductCode'))
  if (!record || !productCode) return null
  return { productCode, itemNumber: stringValue(pick(record, 'itemNumber', 'ItemNumber')), barcode: stringValue(pick(record, 'barcode', 'Barcode')), productName: stringValue(pick(record, 'productName', 'ProductName')), imageUrl: stringValue(pick(record, 'imageUrl', 'ImageUrl')), warehouseCategoryGuid: stringValue(pick(record, 'warehouseCategoryGuid', 'WarehouseCategoryGuid')), warehouseCategoryName: stringValue(pick(record, 'warehouseCategoryName', 'WarehouseCategoryName')) }
}

function paged<T>(raw: unknown, parseItem: (value: unknown) => T | null): LocalSupplierProductSalesAnalysisPaged<T> {
  const record = asRecord(raw) ?? {}; const items = pick(record, 'items', 'Items')
  return { items: Array.isArray(items) ? items.map(parseItem).filter((item): item is T => !!item) : [], total: numberValue(pick(record, 'total', 'Total')), pageNumber: numberValue(pick(record, 'pageNumber', 'PageNumber'), 1), pageSize: numberValue(pick(record, 'pageSize', 'PageSize'), 20) }
}

function summaryRow(raw: unknown): LocalSupplierProductSalesAnalysisSummaryRow | null {
  const product = candidate(raw); const record = asRecord(raw)
  if (!product || !record) return null
  const rawSuppliers = pick(record, 'suppliers', 'Suppliers')
  const suppliers = Array.isArray(rawSuppliers) ? rawSuppliers.flatMap((value) => { const item = asRecord(value); const code = item && stringValue(pick(item, 'code', 'Code')); return code ? [{ code, name: stringValue(pick(item!, 'name', 'Name')) }] : [] }) : []
  return { ...product, ...totals(record), suppliers }
}

function daily(raw: unknown): LocalSupplierProductSalesAnalysisDaily | null {
  const record = asRecord(raw); const date = record && dateValue(pick(record, 'date', 'Date'))
  if (!record || !date) return null
  const netSalesQuantity = numberValue(pick(record, 'netSalesQuantity', 'NetSalesQuantity'))
  return { date, purchaseQuantity: numberValue(pick(record, 'purchaseQuantity', 'PurchaseQuantity')), purchaseAmount: numberValue(pick(record, 'purchaseAmount', 'PurchaseAmount')), netSalesQuantity, netSalesAmount: numberValue(pick(record, 'netSalesAmount', 'NetSalesAmount')), averageUnitPrice: netSalesQuantity === 0 ? null : nullableNumber(pick(record, 'averageUnitPrice', 'AverageUnitPrice')) }
}

function detail(raw: unknown): LocalSupplierProductSalesAnalysisInvoiceDetail | null {
  const record = asRecord(raw); const detailGuid = record && stringValue(pick(record, 'detailGuid', 'DetailGUID'))
  if (!record || !detailGuid) return null
  return { detailGuid, invoiceGuid: stringValue(pick(record, 'invoiceGuid', 'InvoiceGUID')), invoiceNo: stringValue(pick(record, 'invoiceNo', 'InvoiceNo')), storeCode: stringValue(pick(record, 'storeCode', 'StoreCode')), storeName: stringValue(pick(record, 'storeName', 'StoreName')), supplierCode: stringValue(pick(record, 'supplierCode', 'SupplierCode')), supplierName: stringValue(pick(record, 'supplierName', 'SupplierName')), purchaseDate: dateValue(pick(record, 'purchaseDate', 'PurchaseDate')), productCode: stringValue(pick(record, 'productCode', 'ProductCode')), productName: stringValue(pick(record, 'productName', 'ProductName')), quantity: numberValue(pick(record, 'quantity', 'Quantity')), purchasePrice: nullableNumber(pick(record, 'purchasePrice', 'PurchasePrice')), amount: numberValue(pick(record, 'amount', 'Amount')) }
}

function branch(raw: unknown): LocalSupplierProductSalesAnalysisBranch | null {
  const record = asRecord(raw); const branchCode = record && stringValue(pick(record, 'branchCode', 'BranchCode'))
  if (!record || !branchCode) return null
  const netSalesQuantity = numberValue(pick(record, 'netSalesQuantity', 'NetSalesQuantity'))
  return { branchCode, branchName: stringValue(pick(record, 'branchName', 'BranchName')), netSalesQuantity, netSalesAmount: numberValue(pick(record, 'netSalesAmount', 'NetSalesAmount')), averageUnitPrice: netSalesQuantity === 0 ? null : nullableNumber(pick(record, 'averageUnitPrice', 'AverageUnitPrice')) }
}

function options(raw: unknown): LocalSupplierProductSalesAnalysisOptions {
  const record = asRecord(raw) ?? {}
  const rawCategories = pick(record, 'warehouseCategories', 'WarehouseCategories')
  const rawSuppliers = pick(record, 'suppliers', 'Suppliers')
  const warehouseCategories = Array.isArray(rawCategories) ? rawCategories.flatMap((entry) => {
    const item = asRecord(entry); const guid = item && stringValue(pick(item, 'guid', 'Guid'))
    return guid ? [{ guid, name: stringValue(pick(item!, 'name', 'Name')) }] : []
  }) : []
  const suppliers = Array.isArray(rawSuppliers) ? rawSuppliers.flatMap((entry) => {
    const item = asRecord(entry); const code = item && stringValue(pick(item, 'code', 'Code'))
    return code ? [{ code, name: stringValue(pick(item!, 'name', 'Name')) }] : []
  }) : []
  return { warehouseCategories, suppliers }
}

function unwrap<T>(raw: unknown, normalize: (value: unknown) => T): LocalSupplierProductSalesAnalysisEnvelope<T> {
  const response = asRecord(raw); if (!response) throw new Error('响应格式非法')
  if ((pick(response, 'success', 'Success') ?? pick(response, 'isSuccess', 'IsSuccess')) === false) throw new Error(stringValue(pick(response, 'message', 'Message')) ?? '请求失败')
  return { data: normalize(pick(response, 'data', 'Data') ?? response) }
}

async function post<T>(path: string, body: LocalSupplierProductSalesAnalysisRequest, normalize: (value: unknown) => T, signal?: AbortSignal) {
  return unwrap(await request.post<unknown>(`${API_BASE}${path}`, body, { signal }), normalize)
}

export function getLocalSupplierProductSalesAnalysisOptions(signal?: AbortSignal) {
  return request(`${API_BASE}/options`, { method: 'GET', signal }).then((raw) => unwrap(raw, options))
}
export function queryLocalSupplierProductSalesAnalysisCandidates(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/candidates', body, (raw) => paged(raw, candidate), signal) }
export function queryLocalSupplierProductSalesAnalysisSummary(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/summary', body, (raw) => { const page = paged(raw, summaryRow); const record = asRecord(raw) ?? {}; return { ...page, totals: totals(pick(record, 'totals', 'Totals')) } as LocalSupplierProductSalesAnalysisSummary }, signal) }
export function queryLocalSupplierProductSalesAnalysisProductDaily(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/product-daily', body, (raw) => Array.isArray(raw) ? raw.map(daily).filter((item): item is LocalSupplierProductSalesAnalysisDaily => !!item) : [], signal) }
export function queryLocalSupplierProductSalesAnalysisInvoiceDetails(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/invoice-details', body, (raw) => paged(raw, detail), signal) }
export function queryLocalSupplierProductSalesAnalysisBranches(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/branches', body, (raw) => Array.isArray(raw) ? raw.map(branch).filter((item): item is LocalSupplierProductSalesAnalysisBranch => !!item) : [], signal) }
export function queryLocalSupplierProductSalesAnalysisBranchDaily(body: LocalSupplierProductSalesAnalysisRequest, signal?: AbortSignal) { return post('/branch-daily', body, (raw) => Array.isArray(raw) ? raw.map(daily).filter((item): item is LocalSupplierProductSalesAnalysisDaily => !!item) : [], signal) }
