/** 批量货号查询独立契约；所有数量均来自同一日期和门店范围。 */
export interface BatchSalesScope {
  startDate: string
  endDate: string
  storeCodes: string[]
}

export interface BatchSalesMetrics {
  /** 净销量；等于正价、折扣和未知净数量之和。 */
  quantity: number
  regularQuantity: number
  discountQuantity: number
  unknownQuantity: number
  /** 正数退货量，仅作辅助信息，已在上述净数量扣除。 */
  returnQuantity: number
  salesAmount: number
  discountStatus: 'complete' | 'partial' | 'unknown'
  originalPriceMin: number | null
  originalPriceMax: number | null
  discountPriceMin: number | null
  discountPriceMax: number | null
}

export interface BatchSalesProduct {
  productCode: string
  itemNumber: string
  productName: string
  englishName?: string
  barcode?: string
  imageUrl?: string
}

export interface BatchSalesProductSummary extends BatchSalesProduct {
  quantity: number
}

export interface BatchSalesMatch {
  itemNumber: string
  status: 'matched' | 'ambiguous' | 'notFound'
  productCodes: string[]
}

export interface BatchSalesQuery extends BatchSalesScope {
  itemNumbers: string[]
}

export interface BatchSalesQueryResult extends BatchSalesScope {
  matches: BatchSalesMatch[]
  products: BatchSalesProductSummary[]
  warnings: string[]
  statisticStatus?: string
  statisticUpdatedAt?: string
}

export interface BatchSalesDaily {
  date: string
  metrics: BatchSalesMetrics
}

export interface BatchSalesBranch {
  branchCode: string
  branchName: string
  metrics: BatchSalesMetrics
  daily: BatchSalesDaily[]
}

export interface BatchSalesDetailRequest extends BatchSalesScope {
  productCode: string
}

export interface BatchSalesDetail extends BatchSalesScope {
  product: BatchSalesProduct
  metrics: BatchSalesMetrics
  daily: BatchSalesDaily[]
  branches: BatchSalesBranch[]
  warnings: string[]
}

export interface BatchSalesOptions {
  stores: { code: string; name: string }[]
  maxItemNumbers: number
  maxDays: number
}

export interface BatchProductSalesApi {
  getOptions(signal?: AbortSignal): Promise<BatchSalesOptions>
  query(input: BatchSalesQuery, signal?: AbortSignal): Promise<BatchSalesQueryResult>
  getDetail(input: BatchSalesDetailRequest, signal?: AbortSignal): Promise<BatchSalesDetail>
}
