export enum OrderType {
  All = -1,
  Pending = 0,
  Paid = 1,
  Cancelled = 2,
  Refunded = 3,
  Installment = 4,
}

export enum OrderStatus {
  Pending = 0,
  Paid = 1,
  Cancelled = 2,
  Refunded = 3,
  Installment = 4,
}

export interface PosmSalesOrder {
  orderGuid?: string
  branchCode?: string
  branchName?: string
  deviceCode?: string
  orderTime?: string
  skuCount?: number
  itemCount?: number
  totalAmount?: number
  discountAmount?: number
  actualAmount?: number
  status?: number
}

export interface PosmSalesOrderDetail {
  productImage?: string
  productCode?: string
  productName?: string
  quantity?: number
  unitPrice?: number
  discountAmount?: number
  actualAmount?: number
}

export interface PosmPaymentDetail {
  paymentTime?: string
  paymentMethod?: number
  paymentMethodName?: string
  amount?: number
}

export interface PosmSalesOrderDetailResponse {
  order?: PosmSalesOrder
  orderDetails?: PosmSalesOrderDetail[]
  paymentDetails?: PosmPaymentDetail[]
}

export interface PosmSalesOrderQueryParams {
  startDate?: string
  endDate?: string
  branchCode?: string
  deviceCode?: string
  orderType?: OrderType
  keyword?: string
  orderGuidKeyword?: string
  deviceCodeKeyword?: string
  timeStart?: string
  timeEnd?: string
  skuCountMin?: number
  skuCountMax?: number
  itemCountMin?: number
  itemCountMax?: number
  totalAmountMin?: number
  totalAmountMax?: number
  discountAmountMin?: number
  discountAmountMax?: number
  actualPayMin?: number
  actualPayMax?: number
  sortField?: PosmSalesOrderSortField
  sortDirection?: PosmSalesOrderSortDirection
  pageNumber?: number
  pageSize?: number
}

export type PosmSalesOrderSortField =
  | 'orderGuid'
  | 'branchCode'
  | 'deviceCode'
  | 'orderTime'
  | 'skuCount'
  | 'itemCount'
  | 'totalAmount'
  | 'discountAmount'
  | 'actualPay'

export type PosmSalesOrderSortDirection = 'asc' | 'desc'

export interface PosmSalesOrderSortState {
  field: PosmSalesOrderSortField
  direction: PosmSalesOrderSortDirection
}

export interface PosmSalesOrderColumnFilters {
  orderGuidKeyword?: string
  branchCode?: string
  deviceCodeKeyword?: string
  startDate?: string
  endDate?: string
  timeStart?: string
  timeEnd?: string
  skuCountMin?: number
  skuCountMax?: number
  itemCountMin?: number
  itemCountMax?: number
  totalAmountMin?: number
  totalAmountMax?: number
  discountAmountMin?: number
  discountAmountMax?: number
  actualPayMin?: number
  actualPayMax?: number
}
