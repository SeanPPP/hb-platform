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
  pageNumber?: number
  pageSize?: number
}
