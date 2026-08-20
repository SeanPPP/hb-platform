export type ProductSalesAnalysisSelectionMode = 'allFiltered' | 'included'

export type ProductSalesAnalysisScopeMode = 'currentProduct' | 'selectedProducts'

export interface ProductSalesAnalysisSupplier {
  code: string
  name?: string
}

export interface ProductSalesAnalysisProduct {
  productCode: string
  itemNumber?: string
  barcode?: string
  productName?: string
  englishName?: string
  imageUrl?: string
  australianSuppliers: ProductSalesAnalysisSupplier[]
  chinaSuppliers: ProductSalesAnalysisSupplier[]
  chinaSupplierUnmapped?: boolean
}

export interface ProductSalesMetrics {
  quantity: number
  salesAmount: number
  averageUnitPrice: number | null
}

export interface ProductSalesSummaryRow extends ProductSalesAnalysisProduct {
  metrics: ProductSalesMetrics
}

export interface ProductSalesDaily {
  date: string
  quantity: number
  salesAmount: number
  averageUnitPrice: number | null
}

export interface ProductSalesBranch {
  branchCode: string
  branchName?: string
  metrics: ProductSalesMetrics
}

export interface ProductSalesAnalysisOptions {
  australianSuppliers: ProductSalesAnalysisSupplier[]
  chinaSuppliers: ProductSalesAnalysisSupplier[]
}

export interface ProductSalesAnalysisPaged<T> {
  items: T[]
  total: number
  pageNumber: number
  pageSize: number
}

export interface ProductSalesAnalysisEnvelope<T> {
  statisticStatus?: string
  statisticMessage?: string
  statisticUpdatedAt?: string
  cacheVersion?: string
  data: T
}

export interface ProductSalesAnalysisFilter {
  startDate: string
  endDate: string
  keyword?: string
  australianSupplierCodes: string[]
  chinaSupplierCodes: string[]
}

export interface ProductSalesAnalysisSelection {
  mode: ProductSalesAnalysisSelectionMode
  includedProductCodes: string[]
  excludedProductCodes: string[]
}

export interface ProductSalesAnalysisScope {
  mode: ProductSalesAnalysisScopeMode
  productCode?: string
}

export interface ProductSalesAnalysisPaging {
  pageNumber: number
  pageSize: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
}
