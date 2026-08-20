export interface LocalSupplierProductSalesAnalysisFilter {
  startDate: string
  endDate: string
  keyword?: string
  categoryGuid?: string
  supplierCode?: string
  documentKeyword?: string
}

export type LocalSupplierProductSalesAnalysisSelectionMode = 'allFiltered' | 'included'

export interface LocalSupplierProductSalesAnalysisSelection {
  mode: LocalSupplierProductSalesAnalysisSelectionMode
  includedProductCodes: string[]
  excludedProductCodes: string[]
}

export interface LocalSupplierProductSalesAnalysisRequest {
  filter: LocalSupplierProductSalesAnalysisFilter
  selection: LocalSupplierProductSalesAnalysisSelection
  currentProductCode?: string
  branchCode?: string
  pageNumber?: number
  pageSize?: number
  forceRefresh?: boolean
}

export interface LocalSupplierProductSalesAnalysisCategoryOption { guid: string; name?: string }
export interface LocalSupplierProductSalesAnalysisSupplierOption { code: string; name?: string }
export interface LocalSupplierProductSalesAnalysisOptions {
  warehouseCategories: LocalSupplierProductSalesAnalysisCategoryOption[]
  suppliers: LocalSupplierProductSalesAnalysisSupplierOption[]
}

export interface LocalSupplierProductSalesAnalysisSupplierRef { code: string; name?: string }

export interface LocalSupplierProductSalesAnalysisCandidate {
  productCode: string
  itemNumber?: string
  barcode?: string
  productName?: string
  imageUrl?: string
  warehouseCategoryGuid?: string
  warehouseCategoryName?: string
}

export interface LocalSupplierProductSalesAnalysisTotals {
  purchaseQuantity: number
  purchaseAmount: number
  netSalesQuantity: number
  netSalesAmount: number
  sellThroughRate: number | null
}

export interface LocalSupplierProductSalesAnalysisSummaryRow extends LocalSupplierProductSalesAnalysisCandidate, LocalSupplierProductSalesAnalysisTotals {
  suppliers: LocalSupplierProductSalesAnalysisSupplierRef[]
}

export interface LocalSupplierProductSalesAnalysisPaged<T> {
  items: T[]
  total: number
  pageNumber: number
  pageSize: number
}

export interface LocalSupplierProductSalesAnalysisSummary extends LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisSummaryRow> {
  totals: LocalSupplierProductSalesAnalysisTotals
}

export interface LocalSupplierProductSalesAnalysisDaily {
  date: string
  purchaseQuantity: number
  purchaseAmount: number
  netSalesQuantity: number
  netSalesAmount: number
  averageUnitPrice: number | null
}

export interface LocalSupplierProductSalesAnalysisInvoiceDetail {
  detailGuid: string
  invoiceGuid?: string
  invoiceNo?: string
  storeCode?: string
  storeName?: string
  supplierCode?: string
  supplierName?: string
  purchaseDate?: string
  productCode?: string
  productName?: string
  quantity: number
  purchasePrice: number | null
  amount: number
}

export interface LocalSupplierProductSalesAnalysisBranch {
  branchCode: string
  branchName?: string
  netSalesQuantity: number
  netSalesAmount: number
  averageUnitPrice: number | null
}

export interface LocalSupplierProductSalesAnalysisEnvelope<T> { data: T }
