import type { ApiResponse } from '../types/api'
import type {
  ProductMovementReportQuery,
  ProductMovementReportResponse,
  ProductMovementReportRow,
  ProductMovementReportSummary,
} from '../types/productMovementReport'
import type { StoreOption } from './storeService'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/product-movement-report'

function readNumber(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readOptionalNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function readString(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function normalizeSummary(raw: unknown): ProductMovementReportSummary | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }
  const record = raw as Record<string, unknown>
  const key = readString(record.key ?? record.Key)
  if (!key) {
    return null
  }
  return {
    key,
    count: readNumber(record.count ?? record.Count),
  }
}

function normalizeRow(raw: unknown): ProductMovementReportRow | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const storeCode = readString(record.storeCode ?? record.StoreCode)
  const productCode = readString(record.productCode ?? record.ProductCode)
  if (!storeCode || !productCode) {
    return null
  }

  return {
    storeCode,
    storeName: readString(record.storeName ?? record.StoreName),
    productCode,
    productName: readString(record.productName ?? record.ProductName),
    barcode: readString(record.barcode ?? record.Barcode),
    salesQty30: readNumber(record.salesQty30 ?? record.SalesQty30),
    salesQty90: readNumber(record.salesQty90 ?? record.SalesQty90),
    dailySalesQty30: readNumber(record.dailySalesQty30 ?? record.DailySalesQty30),
    salesAmount90Aud: readNumber(record.salesAmount90Aud ?? record.SalesAmount90Aud),
    grossProfit90Aud: readOptionalNumber(record.grossProfit90Aud ?? record.GrossProfit90Aud),
    grossMarginRate90: readOptionalNumber(record.grossMarginRate90 ?? record.GrossMarginRate90),
    lastSaleDate: readString(record.lastSaleDate ?? record.LastSaleDate) ?? null,
    noSaleDays: readOptionalNumber(record.noSaleDays ?? record.NoSaleDays),
    purchaseQty180: readNumber(record.purchaseQty180 ?? record.PurchaseQty180),
    salesQty180: readNumber(record.salesQty180 ?? record.SalesQty180),
    estimatedRemainingQty: readNumber(record.estimatedRemainingQty ?? record.EstimatedRemainingQty),
    estimatedCoverDays: readOptionalNumber(record.estimatedCoverDays ?? record.EstimatedCoverDays),
    dataCredibility: readString(record.dataCredibility ?? record.DataCredibility) ?? '中',
    dataExceptionFlag: readString(record.dataExceptionFlag ?? record.DataExceptionFlag) ?? '正常',
    systemSuggestion: readString(record.systemSuggestion ?? record.SystemSuggestion) ?? '正常',
    storeManagerAction:
      readString(record.storeManagerAction ?? record.StoreManagerAction) ??
      '暂无特殊动作，按正常陈列和订货节奏处理。',
    salesStatisticLastUpdate:
      readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
  }
}

function normalizeResponse(raw: unknown): ProductMovementReportResponse {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {}
  const items = Array.isArray(record.items ?? record.Items)
    ? ((record.items ?? record.Items) as unknown[]).map(normalizeRow).filter((item): item is ProductMovementReportRow => item !== null)
    : []

  const suggestionSummary = Array.isArray(record.suggestionSummary ?? record.SuggestionSummary)
    ? ((record.suggestionSummary ?? record.SuggestionSummary) as unknown[])
        .map(normalizeSummary)
        .filter((item): item is ProductMovementReportSummary => item !== null)
    : []

  const credibilitySummary = Array.isArray(record.credibilitySummary ?? record.CredibilitySummary)
    ? ((record.credibilitySummary ?? record.CredibilitySummary) as unknown[])
        .map(normalizeSummary)
        .filter((item): item is ProductMovementReportSummary => item !== null)
    : []

  return {
    items,
    total: readNumber(record.total ?? record.Total),
    page: readNumber(record.page ?? record.Page, 1),
    pageSize: readNumber(record.pageSize ?? record.PageSize, 50),
    suggestionSummary,
    credibilitySummary,
    salesStatisticLastUpdate:
      readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
    calculationNote:
      readString(record.calculationNote ?? record.CalculationNote) ??
      '估算剩余量=近180天进货单数量-近180天销售数量；不是货架库存、后仓库存或财务库存。',
    dataScopeNote:
      readString(record.dataScopeNote ?? record.DataScopeNote) ??
      '系统没有货架库存和后仓库存，本页不能判断从后仓补到货架；店长需要现场核对。',
  }
}

export async function getProductMovementReport(
  query: ProductMovementReportQuery,
  signal?: AbortSignal,
): Promise<ProductMovementReportResponse> {
  const response = await request.get<ApiResponse<ProductMovementReportResponse> | ProductMovementReportResponse>(
    API_BASE,
    {
      params: query as Record<string, unknown>,
      signal,
    },
  )

  return normalizeResponse(unwrapApiData(response))
}

export async function getProductMovementStoreOptions(): Promise<StoreOption[]> {
  const response = await request.get<ApiResponse<StoreOption[]> | StoreOption[]>(`${API_BASE}/store-options`)
  const data = unwrapApiData(response)
  return Array.isArray(data) ? data : []
}
