export enum StoreOrderFlowStatus {
  ShoppingCart = 0,
  Submitted = 1,
  Completed = 2,
  Picking = 3,
}

export interface StoreOrderStatusOption {
  value: StoreOrderFlowStatus
  label: string
  color: string
}

export const StoreOrderStatusOptions: StoreOrderStatusOption[] = [
  { value: StoreOrderFlowStatus.ShoppingCart, label: '购物车', color: 'default' },
  { value: StoreOrderFlowStatus.Submitted, label: '已提交', color: 'processing' },
  { value: StoreOrderFlowStatus.Completed, label: '已完成', color: 'success' },
  { value: StoreOrderFlowStatus.Picking, label: '配货中', color: 'warning' },
]

export const StoreOrderStatusLabelMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.label]),
) as Record<StoreOrderFlowStatus, string>

export const StoreOrderStatusColorMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.color]),
) as Record<StoreOrderFlowStatus, string>

export interface StoreOrderBranchOption {
  guid: string
  code: string
  name: string
}

export interface StoreOrderListQuery {
  keyword?: string
  storeCodes?: string[]
  startDate?: string
  endDate?: string
  statusList?: StoreOrderFlowStatus[]
  pageNumber: number
  pageSize: number
  sortBy?: string
  sortDescending?: boolean
}

export interface StoreOrderListItem {
  orderGUID: string
  orderNo: string
  storeCode?: string
  storeName?: string
  orderDate?: string
  outboundDate?: string
  flowStatus: StoreOrderFlowStatus
  totalAmount: number
  oemTotalAmount: number
  importTotalAmount: number
  totalOrderAmount: number
  totalQuantity: number
  totalAllocQuantity: number
  totalOrderVolume?: number
  totalAllocVolume?: number
  remarks?: string
  createdAt?: string
  createdBy?: string
  updatedAt?: string
  updatedBy?: string
}

export interface StoreOrderListResult {
  items: StoreOrderListItem[]
  total: number
  page: number
  pageSize: number
}

export interface CreateStoreOrderPayload {
  storeCode: string
  remarks?: string
}

export interface CopyStoreOrderPayload {
  sourceOrderGUID: string
  targetStoreCode: string
  copyOrderQuantity: boolean
  copyAllocQuantity: boolean
}

export interface CopyStoreOrderResult {
  orderGUID: string
  orderNo?: string
}

export interface StoreOrderStatusUpdatePayload {
  orderGUID: string
  newStatus: StoreOrderFlowStatus
}

export interface StoreOrderBatchStatusUpdatePayload {
  orderGUIDs: string[]
  newStatus: StoreOrderFlowStatus
}

export interface SyncMissingStoreOrdersResult {
  success: boolean
  message: string
  mode?: StoreOrderSyncMode
  runId?: string
  ordersSynced: number
  detailsSynced: number
  ordersUpdated: number
  detailsUpdated: number
  ordersSoftDeleted?: number
  detailsSoftDeleted?: number
  hqOrderCount?: number
  hqDetailCount?: number
  shadowRowCount?: number
  durationMs?: number
  errors?: string[]
}

export interface SyncMissingStoreOrdersPayload {
  storeCodes?: string[]
  storeCode?: string
}

export type StoreOrderSyncMode = 'Full' | 'Incremental'
export type StoreOrderSyncConflictStrategy = 'LatestWins' | 'HqWins'

export interface StoreOrderHqSyncPayload extends SyncMissingStoreOrdersPayload {
  startDate?: string
  endDate?: string
  conflictStrategy?: StoreOrderSyncConflictStrategy
}

export type StoreOrderSyncJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface StoreOrderSyncJobResult {
  jobId: string
  status: StoreOrderSyncJobStatus
  mode?: StoreOrderSyncMode
  conflictStrategy?: StoreOrderSyncConflictStrategy
  message?: string
  success?: boolean
  storeCodes?: string[]
  startDate?: string
  endDate?: string
  ordersSynced?: number
  detailsSynced?: number
  ordersUpdated?: number
  detailsUpdated?: number
  ordersSoftDeleted?: number
  detailsSoftDeleted?: number
  skippedOrdersBecauseLocalNewer?: number
  skippedDetailsBecauseLocalNewer?: number
  hqOrderCount?: number
  hqDetailCount?: number
  shadowRowCount?: number
  durationMs?: number
  errors?: string[]
}

export type StoreOrderDetailStatFilter = 'all' | 'orderedNotShipped' | 'shippedWithoutOrder'

export type StoreOrderDetailSortField = 'itemNumber' | 'locationCode'

export interface StoreOrderDetailQuery {
  pageNumber: number
  pageSize: number
  keyword?: string
  statFilter?: StoreOrderDetailStatFilter
  sortBy?: StoreOrderDetailSortField
  sortDescending?: boolean
}

export interface StoreOrderDetailLine {
  detailGUID: string
  productCode: string
  itemNumber?: string
  barcode?: string
  productName?: string
  productImage?: string
  quantity: number
  allocQuantity?: number
  price: number
  amount: number
  importPrice: number
  importAmount: number
  volume?: number
  totalVolume?: number
  orderVolume?: number
  allocVolume?: number
  minOrderQuantity: number
  isActive: boolean
  locationCode?: string
  rrp?: number
}

export interface StoreOrderDetail {
  orderGUID: string
  orderNo?: string
  storeCode?: string
  totalAmount: number
  totalQuantity: number
  totalImportAmount: number
  totalVolume: number
  totalOrderVolume?: number
  totalAllocVolume?: number
  remarks?: string
  shippingFee?: number
  orderDate?: string
  storeAddress?: string
  storeContactEmail?: string
  flowStatus?: StoreOrderFlowStatus
  totalAllocQuantity?: number
  totalSKU?: number
  itemsTotal: number
  orderedNotShippedCount?: number
  shippedWithoutOrderCount?: number
  items: StoreOrderDetailLine[]
}

export interface StoreOrderProductQuery {
  itemNumber?: string
  productName?: string
  categoryGUID?: string
  localSupplierCode?: string
  excludeExistingWarehouseProducts?: boolean
  excludeOrderGUID?: string
  pageNumber: number
  pageSize: number
  sortBy?: string
  grade?: string
}

export interface StoreOrderProductItem {
  productCode: string
  itemNumber?: string
  barcode?: string
  productName?: string
  productImage?: string
  localSupplierCode?: string
  localSupplierName?: string
  categoryName?: string
  warehouseCategoryGUID?: string
  oemPrice?: number
  minOrderQuantity: number
  stockQuantity: number
  isInStock: boolean
  packQty?: number
  importPrice?: number
  grade?: string
}

export interface StoreOrderDynamicData {
  productCode: string
  lastOrderDate?: string
  lastQuantity?: number
  lastAllocQuantity?: number
  cartQuantity: number
}

export interface StoreOrderCartItem {
  detailGUID: string
  productCode: string
  itemNumber?: string
  barcode?: string
  productName?: string
  productImage?: string
  price: number
  quantity: number
  allocQuantity?: number
  amount: number
  importPrice: number
  importAmount: number
  volume?: number
  totalVolume?: number
  minOrderQuantity: number
  isActive: boolean
  locationCode?: string
  rrp?: number
}

export interface StoreOrderCart {
  orderGUID: string
  orderNo?: string
  storeCode?: string
  storeName?: string
  totalAmount: number
  totalQuantity: number
  totalImportAmount: number
  totalVolume: number
  remarks?: string
  shippingFee?: number
  orderDate?: string
  storeAddress?: string
  storeContactEmail?: string
  flowStatus?: StoreOrderFlowStatus
  items: StoreOrderCartItem[]
}

export interface StoreOrderProductListResult {
  items: StoreOrderProductItem[]
  total: number
  page: number
  pageSize: number
}

export type StoreOrderPasteTargetField = 'quantity' | 'allocQuantity'

export interface StoreOrderBatchLookupItem {
  lookupCode: string
  product?: StoreOrderProductItem
}

export interface StoreOrderScanLookupResult {
  barcode: string
  items: StoreOrderProductItem[]
}

export type StoreOrderScanStatus =
  | 'ready'
  | 'scanning'
  | 'added'
  | 'multiple'
  | 'not_found'
  | 'blocked'
  | 'error'

export interface AddStoreOrderLinePayload {
  orderGUID: string
  productCode: string
  quantity: number
}

export interface BatchAddStoreOrderLinePayload {
  orderGUID: string
  items: Array<{
    productCode: string
    quantity: number
    importPrice?: number
  }>
}

export interface UpdateStoreOrderLinePayload {
  orderGUID: string
  productCode: string
  allocQuantity: number
  importPrice?: number
}

export interface RemoveStoreOrderLinePayload {
  orderGUID: string
  detailGUID: string
}

export interface BatchUpdateStoreOrderLinePayload {
  orderGUID: string
  items: Array<{
    productCode: string
    quantity?: number
    importPrice?: number
  }>
}

export interface UpdateStoreOrderProductStatusPayload {
  productCode: string
  isActive: boolean
}

export interface BatchUpdateStoreOrderProductStatusPayload {
  productCodes: string[]
  isActive: boolean
}

export interface UpdateStoreOrderHeaderPayload {
  orderGUID: string
  remarks?: string
  shippingFee?: number
  storeCode?: string
  orderDate?: string
}

export interface UpdateStoreOrderStoreContactPayload {
  orderGUID: string
  storeCode: string
  address?: string
  contactEmail?: string
}

export interface SendStoreOrderInvoiceEmailPayload {
  orderGUID: string
  toEmail: string
  subject?: string
  body?: string
  pdfFileName: string
  pdfBase64: string
}

export type StoreOrderInvoiceEmailJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface StoreOrderInvoiceEmailJobResult {
  jobId: string
  status: StoreOrderInvoiceEmailJobStatus
  message?: string
  orderGUID?: string
  toEmail?: string
  createdAt?: string
  completedAt?: string
}

export interface StoreOrderBatchLookupPayload {
  codes: string[]
}

export interface PasteReplaceStoreOrderLinesPayload {
  orderGUID: string
  targetField: StoreOrderPasteTargetField
  items: Array<{
    productCode: string
    quantity: number
    importPrice?: number
  }>
}
