export type WarehouseProductRecordSortDirection = 'asc' | 'desc'

export interface WarehouseProductRecordSummary {
  productCode: string
  itemNumber: string | null
  barcode: string | null
  productName: string | null
  englishName: string | null
  imageUrl: string | null
  isActive: boolean
}

export interface WarehouseProductContainerItem {
  detailCode: string
  containerCode: string
  containerNumber: string | null
  loadingDate: string | null
  estimatedArrivalDate: string | null
  actualArrivalDate: string | null
  effectiveArrivalDate: string | null
  status: number | null
  loadingPieces: number | null
  loadingQuantity: number | null
  domesticPrice: number | null
  importPrice: number | null
  totalAmount: number | null
}

export interface WarehouseProductContainerSummary {
  containerCount: number
  loadingPieces: number
  loadingQuantity: number
  totalAmount: number
}

export interface WarehouseProductContainerQuery {
  containerKeyword?: string
  arrivalStartDate?: string
  arrivalEndDate?: string
  statuses?: number[]
  pageNumber: number
  pageSize: number
  sortBy?: string
  sortDirection?: WarehouseProductRecordSortDirection
}

export interface WarehouseProductContainerReport {
  totalCount: number
  pageNumber: number
  pageSize: number
  summary: WarehouseProductContainerSummary
  items: WarehouseProductContainerItem[]
}

export interface WarehouseProductAllocationBranch {
  storeCode: string
  storeName: string | null
  isActive: boolean
  allocationQuantity: number
  allocationAmount: number
  orderCount: number
  firstAllocationDate: string | null
  lastAllocationDate: string | null
}

export interface WarehouseProductAllocationSummary {
  allocationQuantity: number
  allocationAmount: number
  orderCount: number
}

export interface WarehouseProductAllocationQuery {
  startDate: string
  endDate: string
}

export interface WarehouseProductAllocationReport {
  summary: WarehouseProductAllocationSummary
  branches: WarehouseProductAllocationBranch[]
}
