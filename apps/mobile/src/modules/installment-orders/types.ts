export type InstallmentOrderStatus = 1 | 2 | 3 | 4 | null;
export type InstallmentPaymentStatus = 1 | 2 | null;
export type InstallmentCancellationKind = 1 | 2 | null;

export interface InstallmentOrderFilters {
  startDate?: string;
  endDate?: string;
  branchCode?: string;
  status?: InstallmentOrderStatus;
  customerPhone?: string;
  customerName?: string;
}

export interface InstallmentOrderListItem {
  installmentGuid: string;
  installmentNumber: string;
  storeCode: string;
  storeName: string;
  cashierName: string;
  customerName: string;
  customerPhone: string;
  createdAt: string;
  totalAmount: number | null;
  minimumDownPayment: number | null;
  downPaymentAmount: number | null;
  paidAmount: number | null;
  balanceAmount: number | null;
  status: InstallmentOrderStatus;
  updatedAt: string;
}

export interface InstallmentPaymentRecord {
  paymentGuid: string;
  method: number | null;
  amount: number | null;
  reference: string;
  status: InstallmentPaymentStatus;
  recordedAt: string;
  cashierId: string;
  deviceCode: string;
}

export interface InstallmentOrderDetailOrder extends InstallmentOrderListItem {
  deviceCode: string;
  cashierId: string;
  note: string;
}

export interface InstallmentPickupInfo {
  pickedUpAt: string;
  pickedUpBy: string;
  pickupNote: string;
}

export interface InstallmentCancellationInfo {
  cancellationKind: InstallmentCancellationKind;
  cancelledAt: string;
  cancelledBy: string;
  cancellationReason: string;
}

export interface InstallmentOrderDetailLine {
  installmentLineGuid: string;
  productCode: string;
  referenceCode: string;
  displayName: string;
  lookupCode: string;
  quantity: number | null;
  unitPrice: number | null;
  discountAmount: number | null;
  actualAmount: number | null;
  itemNumber: string;
}

export interface InstallmentOrderDetail {
  order: InstallmentOrderDetailOrder | null;
  lines: InstallmentOrderDetailLine[];
  payments: InstallmentPaymentRecord[];
  pickupInfo: InstallmentPickupInfo | null;
  cancellationInfo: InstallmentCancellationInfo | null;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
