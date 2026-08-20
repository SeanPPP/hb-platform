export type WarehouseProductFlowSelectionMode = 'allFiltered' | 'included'

export interface WarehouseProductFlowFilter {
  keyword?: string
  warehouseCategoryGuids: string[]
  supplierCodes: string[]
  documentKeyword?: string
}

export interface WarehouseProductFlowPeriod {
  startDate: string
  endDate: string
}

export interface WarehouseProductFlowPeriods {
  containerPeriod: WarehouseProductFlowPeriod
  orderShipmentPeriod: WarehouseProductFlowPeriod
  salesPeriod: WarehouseProductFlowPeriod
}

export interface WarehouseProductFlowSelection {
  mode: WarehouseProductFlowSelectionMode
  includedProductCodes: string[]
  excludedProductCodes: string[]
}

export interface WarehouseProductFlowMetrics {
  inboundQuantity: number
  orderedQuantity?: number
  shippedQuantity: number
  netSalesQuantity: number
  netSalesAmount: number
  averageUnitPrice: number | null
}

export interface WarehouseProductFlowCandidate {
  productCode: string
  itemNumber?: string
  productName?: string
  englishName?: string
  barcode?: string
  imageUrl?: string
  supplierCode?: string
  supplierName?: string
  categoryName?: string
}

export interface WarehouseProductFlowProduct extends WarehouseProductFlowCandidate {
  metrics: WarehouseProductFlowMetrics
}

export interface WarehouseProductFlowSummaryData {
  totals: WarehouseProductFlowMetrics
  currentProduct: WarehouseProductFlowProduct | null
  items: WarehouseProductFlowProduct[]
  total: number
  pageNumber: number
  pageSize: number
}

export interface WarehouseProductFlowCandidatesData {
  items: WarehouseProductFlowCandidate[]
  total: number
  pageNumber: number
  pageSize: number
}

export interface WarehouseProductFlowSupplierOption {
  code: string
  name?: string
}

export interface WarehouseProductFlowOptions {
  domesticSuppliers: WarehouseProductFlowSupplierOption[]
}

export interface WarehouseProductFlowContainerRow {
  containerNumber: string
  arrivalDate?: string
  inboundQuantity: number
  inboundUnitPrice: number | null
  supplierName?: string
}

export interface WarehouseProductFlowOrderRow {
  orderNumber: string
  branchName?: string
  orderDate?: string
  orderedQuantity: number
}

export interface WarehouseProductFlowShipmentRow {
  shipmentNumber?: string
  orderNumber?: string
  branchName?: string
  shipmentDate?: string
  shippedQuantity: number
}

export interface WarehouseProductFlowDaily {
  date: string
  inboundQuantity: number
  /** 仓库流转接口始终返回；本地商品共享图表数据可省略。 */
  orderedQuantity?: number
  shippedQuantity: number
  netSalesQuantity: number
  netSalesAmount: number
  averageUnitPrice: number | null
}

export interface WarehouseProductFlowBranch {
  branchCode: string
  branchName?: string
  orderedQuantity: number
  shippedQuantity: number
  netSalesQuantity: number
  netSalesAmount: number
  sellThroughRate: number | null
  averageUnitPrice: number | null
}

export interface WarehouseProductFlowSummaryRequest {
  filter: WarehouseProductFlowFilter
  periods: WarehouseProductFlowPeriods
  selection: WarehouseProductFlowSelection
  currentProductCode?: string
  pageNumber: number
  pageSize: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
  forceRefresh?: boolean
}

export interface WarehouseProductFlowCandidateRequest {
  filter: WarehouseProductFlowFilter
  pageNumber: number
  pageSize: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
  forceRefresh?: boolean
}

export interface WarehouseProductFlowProductRequest {
  filter: WarehouseProductFlowFilter
  periods: WarehouseProductFlowPeriods
  currentProductCode: string
  forceRefresh?: boolean
}

export interface WarehouseProductFlowDetailRequest extends WarehouseProductFlowProductRequest {
  detailType?: never
}

export interface WarehouseProductFlowBranchRequest extends WarehouseProductFlowProductRequest {
  branchCode?: string
}

export interface WarehouseProductFlowEnvelope<T> {
  data: T
}
