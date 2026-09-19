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
  /** 服务端排序：目前只支持近30天销量；不传时按建议紧急程度排序。 */
  sortBy?: 'salesQty30'
  sortDirection?: 'asc' | 'desc'
}

export interface ProductMovementReportRow {
  storeCode: string
  storeName?: string
  productCode: string
  /** 货号，取自商品档案；与内部商品编码 productCode 不是同一个值。 */
  itemNumber?: string
  productName?: string
  barcode?: string
  imageUrl?: string
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
  /** 数据来自后台预计算快照时为快照生成时间（UTC）；实时计算时为空。 */
  snapshotGeneratedAtUtc?: string | null
  calculationNote: string
  dataScopeNote: string
}
