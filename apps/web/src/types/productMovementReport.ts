export type ProductMovementSuggestion =
  | '需要订货'
  | '需要备货'
  | '值得囤货'
  | '需要清仓'
  | '好卖'
  | '观察'
  | '正常'

export type ProductMovementCredibility = '高' | '中' | '低'

export interface ProductMovementReportQuery {
  storeCode?: string
  asOfDate?: string
  suggestion?: string
  dataCredibility?: string
  keyword?: string
  page?: number
  pageSize?: number
}

export interface ProductMovementReportRow {
  storeCode: string
  storeName?: string
  productCode: string
  productName?: string
  barcode?: string
  salesQty30: number
  salesQty90: number
  dailySalesQty30: number
  salesAmount90Aud: number
  grossProfit90Aud?: number | null
  grossMarginRate90?: number | null
  lastSaleDate?: string | null
  noSaleDays?: number | null
  purchaseQty180: number
  salesQty180: number
  estimatedRemainingQty: number
  estimatedCoverDays?: number | null
  dataCredibility: ProductMovementCredibility | string
  dataExceptionFlag: string
  systemSuggestion: ProductMovementSuggestion | string
  storeManagerAction: string
  salesStatisticLastUpdate?: string | null
}

export interface ProductMovementReportSummary {
  key: string
  count: number
}

export interface ProductMovementReportResponse {
  items: ProductMovementReportRow[]
  total: number
  page: number
  pageSize: number
  suggestionSummary: ProductMovementReportSummary[]
  credibilitySummary: ProductMovementReportSummary[]
  salesStatisticLastUpdate?: string | null
  calculationNote: string
  dataScopeNote: string
}
