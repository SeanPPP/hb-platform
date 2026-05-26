export type StoreVoucherStatus = string | number | null;

export interface StoreVoucherFilters {
  storeCode?: string;
  branchCode?: string;
  supplierCode?: string;
  status?: StoreVoucherStatus;
  startDate?: string;
  endDate?: string;
}

export interface StoreVoucher {
  id: string;
  voucherCode: string;
  voucherType: number | null;
  storeCode: string;
  storeName: string;
  supplierCode: string;
  supplierName: string;
  customerCode: string;
  customerName: string;
  amount: number | null;
  remainingAmount: number | null;
  discountRate: number | null;
  status: StoreVoucherStatus;
  createTime: string;
  updateTime: string;
  expiredDate: string;
  createUser: string;
  updateUser: string;
  remark: string;
}

export interface StoreVoucherLedgerItem {
  id: string;
  voucherCode: string;
  action: "issued" | "used";
  amount: number | null;
  remainingAmount: number | null;
  actionTime: string;
  paymentMethod: number | null;
  paymentMethodName: string;
  reference: string;
  orderGuid: string;
  orderNo: string;
  operatorId: string;
  operatorName: string;
  remark: string;
}

export interface StoreVoucherRelatedOrder {
  orderGuid: string;
  orderNo: string;
  storeCode: string;
  supplierCode: string;
  amount: number | null;
  orderTime: string;
}

export interface StoreVoucherDetail {
  voucher: StoreVoucher | null;
  ledger: StoreVoucherLedgerItem[];
  relatedOrders: StoreVoucherRelatedOrder[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
