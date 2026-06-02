export interface LocalSupplierInvoiceListDto {
  invoiceGUID: string
  storeCode?: string
  storeName?: string
  supplierCode?: string
  supplierName?: string
  invoiceNo?: string
  remarks?: string
  orderDate?: string
  inboundDate?: string
  totalAmount?: number
  receivedTotalAmount?: number
  flowStatus?: number
  inboundStatus?: number
  createdAt: string
  createdBy?: string
  updatedAt?: string
  updatedBy?: string
}

export interface LocalSupplierInvoiceDetailDto {
  invoiceGUID: string
  appGUID?: string
  pcGUID?: string
  storeCode?: string
  storeName?: string
  supplierCode?: string
  supplierName?: string
  invoiceNo?: string
  voucherType?: number
  orderDate?: string
  inboundDate?: string
  totalAmount?: number
  receivedTotalAmount?: number
  voucherImage?: string
  remarks?: string
  importTemplate?: string
  flowStatus?: number
  inboundStatus?: number
  createdAt: string
  updatedAt?: string
}

export interface LocalSupplierInvoiceItemDto {
  detailGUID: string
  invoiceGUID?: string
  storeCode?: string
  supplierCode?: string
  productTagGUID?: string
  productCategoryGUID?: string
  storeProductCode?: string
  productCode?: string
  itemNumber?: string
  barcode?: string
  productName?: string
  specification?: string
  unit?: string
  quantity?: number
  lastPurchasePrice?: number
  purchasePrice?: number
  retailPrice?: number
  amount?: number
  existingProductCount?: number
  barcodeStatus?: number
  barcodeMatchCount?: number
  productImage?: string
  activityType?: number
  discountRate?: number
  autoPricing?: boolean
  pricingFloatRate?: number
  newAutoRetailPrice?: number
  isSpecialProduct?: boolean
  oldStoreProductCode?: string
}

export interface UpdateInvoiceRequest {
  storeCode?: string
  supplierCode?: string
  invoiceNo?: string
  orderDate?: string
  inboundDate?: string
  remarks?: string
  voucherImage?: string
  flowStatus?: number
  inboundStatus?: number
}

export interface InvoiceDetailUpsertItemDto {
  detailGUID?: string
  itemNumber?: string
  barcode?: string
  productName?: string
  productCategoryGUID?: string
  storeProductCode?: string
  productCode?: string
  quantity?: number
  lastPurchasePrice?: number
  purchasePrice?: number
  retailPrice?: number
  amount?: number
  activityType?: number
  discountRate?: number
  autoPricing?: boolean
  pricingFloatRate?: number
  newAutoRetailPrice?: number
  isSpecialProduct?: boolean
}

export interface UpdateToStorePricesFields {
  updatePurchasePrice: boolean
  purchasePrice?: number
  updateRetailPrice: boolean
  retailPrice?: number
  updateIsAutoPricing: boolean
  isAutoPricing?: boolean
  updateIsSpecialProduct: boolean
  isSpecialProduct?: boolean
  updateDiscountRate: boolean
  discountRate?: number
}

export interface BatchEditFields {
  updatePurchasePrice: boolean
  purchasePrice?: number
  updateRetailPrice: boolean
  retailPrice?: number
  updateIsAutoPricing: boolean
  isAutoPricing?: boolean
  updateIsSpecialProduct: boolean
  isSpecialProduct?: boolean
  updateDiscountRate: boolean
  discountRate?: number
  updateAction: boolean
  action?: DetailAction
}

export interface UpdateToStorePricesRequest {
  invoiceGuid: string
  detailGuids: string[]
  targetStoreCodes: string[]
  updateFields: UpdateToStorePricesFields
}

export interface BatchResultDto {
  inserted: number
  updated: number
  failed: number
}

export type UpdateToStorePricesResult = BatchResultDto

export interface EnsureHqProductError {
  detailGuid: string
  storeCode?: string
  message: string
}

export interface EnsureHqProductsRequest {
  detailGuids: string[]
  targetStoreCodes: string[]
  idempotencyKey?: string
}

export interface EnsureHqProductsResult {
  total: number
  hqExisting: number
  hbwebCreated: number
  hqCreated: number
  hqSynced: number
  hqPurchasePricesUpdated: number
  skipped: number
  failed: number
  errors: EnsureHqProductError[]
}

export interface UpdateHqProductsRequest {
  detailGuids: string[]
  targetStoreCodes: string[]
  updateFields: UpdateToStorePricesFields
  idempotencyKey?: string
}

export interface UpdateHqProductsResult {
  total: number
  updated: number
  failed: number
  hqExisting?: number
  hbwebCreated?: number
  hqCreated?: number
  hqSynced?: number
  hqPurchasePricesUpdated?: number
  hqRetailPricesUpdated?: number
  hqAutoPricingUpdated?: number
  hqSpecialProductsUpdated?: number
  hqDiscountRatesUpdated?: number
  errors: EnsureHqProductError[]
}

export interface LocalSupplierInvoiceHqSyncRequest {
  selectedStoreCodes?: string[]
  startDate?: string
  endDate?: string
}

export interface LocalSupplierInvoiceHqSyncResult {
  requestId: string
  status: string
  startedAt: string
  completedAt?: string
  durationMs: number
  invoiceAddedCount: number
  invoiceUpdatedCount: number
  detailAddedCount: number
  detailUpdatedCount: number
  totalProcessed: number
  errors: string[]
}

export interface GetInvoiceDetailResponse {
  invoice: LocalSupplierInvoiceDetailDto
  details: LocalSupplierInvoiceItemDto[]
}

export enum ProductStatus {
  Unknown = 0,
  Exists = 1,
  NotExists = 2,
}

export enum BarcodeStatus {
  Unknown = 0,
  Normal = 1,
  Abnormal = 2,
}

export enum DetailAction {
  None = 0,
  CreateProduct = 1,
  UpdatePurchasePrice = 2,
  WaitForOperation = 3,
  UpdateItemNumber = 4,
  AddMultiCode = 5,
}

export interface ProductCheckResult {
  detailGuid: string
  productStatus: ProductStatus
  barcodeStatus: BarcodeStatus
  existingProductCount: number
  autoPricing?: boolean
  isSpecialProduct?: boolean
  discountRate?: number
  storeProductCode?: string
  lastPurchasePrice?: number
  pricingFloatRate?: number
  newAutoRetailPrice?: number
  productInfo?: {
    productCode?: string
    productName?: string
    purchasePrice?: number
    retailPrice?: number
    productImage?: string
    storeProductCode?: string
  }
  barcodeMatchCount?: number
  defaultAction?: DetailAction
}

export interface CheckProductsRequest {
  invoiceGuid: string
  detailGuids?: string[]
}

export interface CheckProductsResponse {
  results: ProductCheckResult[]
  summary: {
    total: number
    productExists: number
    productNotExists: number
    barcodeNormal: number
    barcodeAbnormal: number
  }
}

export interface PasteDetailsRequest {
  invoiceGuid: string
  mode: 'append' | 'replace'
  items: {
    itemNumber?: string
    barcode?: string
    productName?: string
    quantity?: number
    purchasePrice?: number
    newAutoRetailPrice?: number
    retailPrice?: number
  }[]
}

export interface BarcodeAbnormalMatchedProductDto {
  productCode: string
  productName: string
  supplierCode: string
  supplierName?: string
  itemNumber?: string
  barcode: string
  productImage?: string
  isMultiCode: boolean
  isBundle: boolean
  productType?: number
}

export interface BarcodeAbnormalDetailDto {
  detailGuid: string
  itemNumber: string
  barcode: string
  productCode: string
  productName: string
  productStatus: number
  matchedProductCode?: string
  matchedProducts: BarcodeAbnormalMatchedProductDto[]
}

export interface BatchExecuteActionsRequest {
  invoiceGuid: string
  detailGuids: string[]
  expectedActions: BatchExecuteExpectedAction[]
  confirmedCreateProductCount: number
  confirmedAt: string
}

export interface BatchExecuteExpectedAction {
  detailGuid: string
  action: DetailAction
  activityType: DetailAction
}

export interface BatchExecuteActionsResult {
  createdProducts: number
  updatedPurchasePrices: number
  updatedItemNumbers: number
  addedMultiCodes: number
  skipped: number
  failed: number
  errors: string[]
}

export interface CheckInvoiceNoRequest {
  storeCode: string
  supplierCode: string
  invoiceNo: string
  excludeInvoiceGuid?: string
}

export interface CheckInvoiceNoResponse {
  exists: boolean
  existingInvoiceGuid?: string
}
