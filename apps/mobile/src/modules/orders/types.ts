export enum StoreOrderFlowStatus {
  ShoppingCart = 0,
  Submitted = 1,
  Completed = 2,
  Picking = 3,
}

export interface StoreOrderStatusOption {
  value: StoreOrderFlowStatus;
  label: string;
  color: string;
}

export const StoreOrderStatusOptions: StoreOrderStatusOption[] = [
  { value: StoreOrderFlowStatus.ShoppingCart, label: "Cart", color: "default" },
  { value: StoreOrderFlowStatus.Submitted, label: "Submitted", color: "processing" },
  { value: StoreOrderFlowStatus.Completed, label: "Completed", color: "success" },
  { value: StoreOrderFlowStatus.Picking, label: "Picking", color: "warning" },
];

export const StoreOrderStatusLabelMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.label])
) as Record<StoreOrderFlowStatus, string>;

export const StoreOrderStatusColorMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.color])
) as Record<StoreOrderFlowStatus, string>;

export interface StoreOrderListItem {
  orderGUID: string;
  orderNo: string;
  storeCode?: string;
  storeName?: string;
  orderDate?: string;
  outboundDate?: string;
  flowStatus: StoreOrderFlowStatus;
  totalAmount: number;
  oemTotalAmount: number;
  importTotalAmount: number;
  totalOrderAmount: number;
  totalQuantity: number;
  totalAllocQuantity: number;
  totalOrderVolume?: number;
  totalAllocVolume?: number;
  remarks?: string;
  createdAt?: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
}

export interface StoreOrderListResult {
  items: StoreOrderListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface StoreOrderDetailLine {
  detailGUID: string;
  productCode: string;
  itemNumber?: string;
  barcode?: string;
  productName?: string;
  productImage?: string;
  quantity: number;
  allocQuantity?: number;
  price: number;
  amount: number;
  importPrice: number;
  importAmount: number;
  volume?: number;
  totalVolume?: number;
  orderVolume?: number;
  allocVolume?: number;
  minOrderQuantity: number;
  isActive: boolean;
  locationCode?: string;
  rrp?: number;
}

export interface StoreOrderDetail {
  orderGUID: string;
  orderNo?: string;
  storeCode?: string;
  totalAmount: number;
  totalQuantity: number;
  totalImportAmount: number;
  totalVolume: number;
  totalOrderVolume?: number;
  totalAllocVolume?: number;
  remarks?: string;
  shippingFee?: number;
  orderDate?: string;
  storeAddress?: string;
  flowStatus?: StoreOrderFlowStatus;
  totalAllocQuantity?: number;
  totalSKU?: number;
  items: StoreOrderDetailLine[];
}

export interface StoreOrderProductItem {
  productCode: string;
  itemNumber?: string;
  barcode?: string;
  grade?: string;
  productName?: string;
  productImage?: string;
  categoryName?: string;
  warehouseCategoryGUID?: string;
  oemPrice?: number;
  minOrderQuantity: number;
  stockQuantity: number;
  isInStock: boolean;
  packQty?: number;
  importPrice?: number;
}

export interface StoreOrderProductListResult {
  items: StoreOrderProductItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface StoreOrderCartItem {
  detailGUID: string;
  productCode: string;
  itemNumber?: string;
  barcode?: string;
  grade?: string;
  productName?: string;
  productImage?: string;
  price: number;
  quantity: number;
  allocQuantity?: number;
  amount: number;
  importPrice: number;
  importAmount: number;
  volume?: number;
  totalVolume?: number;
  minOrderQuantity: number;
  isActive: boolean;
  locationCode?: string;
  rrp?: number;
  updatedAt?: string;
}

export interface StoreOrderCart {
  orderGUID: string;
  orderNo?: string;
  storeCode?: string;
  storeName?: string;
  totalAmount: number;
  totalQuantity: number;
  totalImportAmount: number;
  totalVolume: number;
  remarks?: string;
  shippingFee?: number;
  orderDate?: string;
  storeAddress?: string;
  flowStatus?: StoreOrderFlowStatus;
  items: StoreOrderCartItem[];
}

export interface StoreOrderScanLookupResult {
  barcode: string;
  items: StoreOrderProductItem[];
}

export type StoreOrderScanStatus =
  | "ready"
  | "scanning"
  | "found"
  | "added"
  | "multiple"
  | "not_found"
  | "blocked"
  | "error";

export type StoreOrderPasteTargetField = "quantity" | "allocQuantity";
