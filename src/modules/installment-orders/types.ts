export type InstallmentOrderStatus = number | null;

export interface InstallmentOrderFilters {
  startDate?: string;
  endDate?: string;
  branchCode?: string;
  status?: InstallmentOrderStatus;
  orderType?: number | null;
  userPhone?: string;
  userName?: string;
}

export interface InstallmentOrderListItem {
  orderGuid: string;
  branchCode: string;
  branchName: string;
  abn: string;
  brandName: string;
  deviceCode: string;
  orderNo: string;
  orderTime: string;
  customerPhone: string;
  customerName: string;
  skuCount: number | null;
  itemCount: number | null;
  totalAmount: number | null;
  discountAmount: number | null;
  actualAmount: number | null;
  status: InstallmentOrderStatus;
}

export interface InstallmentPaymentRecord {
  paymentGuid: string;
  orderGuid: string;
  paymentTime: string;
  paymentMethod: number | null;
  paymentMethodName: string;
  amount: number | null;
  reference: string;
  cashierId: string;
  cashierName: string;
  createdBy: string;
  updatedBy: string;
}

export interface InstallmentOrderDetailLine {
  productImage: string;
  productCode: string;
  productName: string;
  quantity: number | null;
  unitPrice: number | null;
  discountAmount: number | null;
  actualAmount: number | null;
}

export interface InstallmentOrderDetail {
  order: InstallmentOrderListItem | null;
  orderDetails: InstallmentOrderDetailLine[];
  paymentDetails: InstallmentPaymentRecord[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
