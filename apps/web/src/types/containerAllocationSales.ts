export type ContainerArrivalDateBasis = 'Actual' | 'Expected'
export type ContainerStatisticStatus = 'Fresh' | 'Stale' | 'Missing' | 'Failed' | string
export type ContainerAllocationSalesSortDirection = 'asc' | 'desc'

export interface ContainerAllocationSalesQuery {
  startDate?: string
  endDate?: string
  search?: string
  pageNumber?: number
  pageSize?: number
  sortBy?: string
  sortDirection?: ContainerAllocationSalesSortDirection
}

export interface ContainerAllocationSalesMetric {
  allocationQuantity: number
  allocationImportAmount: number
  salesQuantity: number | null
  salesAmount: number | null
  averageSalesPrice: number | null
  grossProfit: number | null
  grossMarginRate: number | null
  isGrossMarginComplete: boolean | null
}

export interface ContainerAllocationSalesProduct extends ContainerAllocationSalesMetric {
  productCode: string
  itemNumber: string | null
  productName: string | null
  loadingQuantity: number
}

export interface ContainerAllocationSalesTotals extends ContainerAllocationSalesMetric {
  productCount: number
  loadingQuantity: number
}

export interface ContainerAllocationSalesReport {
  containerGuid: string
  containerNumber: string | null
  arrivalDate: string | null
  arrivalDateBasis: ContainerArrivalDateBasis | null
  isEstimatedArrivalDate: boolean
  canQuery: boolean
  queryMessage: string | null
  startDate: string | null
  endDate: string | null
  dayCount: number
  startWeek: number
  endWeek: number
  rangeLabel: string
  items: ContainerAllocationSalesProduct[]
  total: number
  pageNumber: number
  pageSize: number
  totals: ContainerAllocationSalesTotals
  statisticStatus: ContainerStatisticStatus
  statisticMessage: string | null
}

export interface ContainerAllocationSalesBranchesQuery {
  productCode: string
  startDate: string
  endDate: string
}

export interface ContainerAllocationSalesBranch extends ContainerAllocationSalesMetric {
  branchCode: string
  branchName: string
  isActive: boolean
}

export interface ContainerAllocationSalesBranchesReport {
  containerGuid: string
  productCode: string
  startDate: string
  endDate: string
  statisticStatus: ContainerStatisticStatus
  statisticMessage: string | null
  items: ContainerAllocationSalesBranch[]
}
