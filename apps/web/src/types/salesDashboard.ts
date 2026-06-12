export interface BestSellerBranchSale {
  branchCode: string
  branchName?: string
  quantity: number
  salesAmount: number
  totalCost?: number
  grossProfit?: number
  grossMarginRate?: number
  costSource?: string
}

export interface BestSellerProduct {
  productCode: string
  itemNumber?: string
  barcode?: string
  productImage?: string
  productName?: string
  quantity: number
  salesAmount: number
  totalCost?: number
  grossProfit?: number
  grossMarginRate?: number
  costSource?: string
  rank: number
  // 是否上架，前端用它控制状态展示和加购按钮禁用态。
  isActive?: boolean
  // 最小起订量，用于热销商品快捷加购默认数量。
  minOrderQuantity?: number
  // 销售过该商品的分店数，优先使用后端聚合值而不是前端现算长度。
  branchSalesCount?: number
  // 分店销量明细，用于 Stores Sold 弹层展示。
  branchSales?: BestSellerBranchSale[]
  // 商品统计状态，用于提示数据是否完整。
  statisticStatus?: string
}

export interface BestSellerResponse {
  products: BestSellerProduct[]
  total: number
  pageIndex: number
  pageSize: number
  totalPages: number
  statisticStatus?: string
  statisticMessage?: string
}

export type CompareMode = 'ByWeek' | 'ByDate'

export interface DateRange {
  startDate: string
  endDate: string
  compareStartDate?: string
  compareEndDate?: string
  compareMode?: CompareMode
}

export interface SupplierSalesRank {
  startDate: string
  endDate: string
  supplierCode: string
  supplierName: string
  totalAmount: number
  totalQuantity: number
  storeCount: number
  compareTotalAmount?: number
  totalAmountGrowth?: number
}

export interface ChinaSupplierSalesRank {
  startDate: string
  endDate: string
  supplierCode: string
  supplierName: string
  totalAmount: number
  totalQuantity: number
  storeCount: number
  compareTotalAmount?: number
  totalAmountGrowth?: number
}

export interface SalesProductDetailWithDiscount {
  productCode: string
  itemNumber?: string
  productImage?: string
  productName?: string
  quantity: number
  discountedQuantity: number
  salesAmount: number
  averageUnitPrice: number
  averageOriginalPrice?: number
  orderCount: number
  quantityLY: number
  discountedQuantityLY: number
  salesAmountLY: number
  averageUnitPriceLY: number
  averageOriginalPriceLY?: number
  orderCountLY: number
}

export interface PagedSalesProductDetailWithDiscount {
  data: SalesProductDetailWithDiscount[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface BranchSalesAggregate {
  branchCode: string
  branchName: string
  totalRevenue: number
  totalRevenueLY: number
  totalQuantity: number
  totalQuantityLY: number
  orderCount: number
  orderCountLY: number
  hbRevenue: number
  hbRevenueLY: number
}

export interface WeeklyHierarchyData {
  key: string
  level: 'week' | 'branch' | 'date'
  hierarchy: string
  revenue: number
  revenueLY: number
  orders: number
  ordersLY: number
  aov: number
  aovLY: number
  yoyChange?: number
  children?: WeeklyHierarchyData[]
}

export interface ExecutiveBranchPerformance {
  rank: number
  branchCode: string
  branchName: string
  revenue: number
  revenueLY: number
  orderCount: number
  orderCountLY: number
  aov: number
  aovLY: number
}

export interface ExecutiveHourlyTraffic {
  hour: string
  revenue: number
  revenueLY: number
  percentage: number
  isPeak: boolean
  branchCode?: string
  branchName?: string
}
