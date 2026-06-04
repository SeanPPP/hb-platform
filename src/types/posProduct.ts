export interface PosProductDto {
  productCode: string
  barcode: string
  productName: string
  productNameCn?: string
  itemNumber?: string
  localSupplierCode?: string
  localSupplierName?: string
  categoryGuid?: string
  categoryName?: string
  purchasePrice: number
  retailPrice: number
  unitWeight?: number
  isActive: boolean
  isSet?: boolean
  setCount?: number
  storeCode?: string
  storeName?: string
  hqProductCode?: string
  productImage?: string
  productType?: number
  middlePackageQuantity?: number
  isAutoPricing?: boolean
  isSpecialProduct?: boolean
  storeRecordCount?: number
  createdAt?: string
  updatedAt?: string
}

export interface ProductStoreRecordDto {
  storeCode?: string
  storeName?: string
  storeProductCode?: string
  purchasePrice?: number
  storeRetailPriceValue?: number
  discountRate?: number
  isActive: boolean
  isAutoPricing: boolean
  isSpecialProduct: boolean
  updatedAt?: string
  updatedBy?: string
}

export interface PosProductFilterParams {
  pageIndex?: number
  pageSize?: number
  keyword?: string
  supplierCode?: string
  categoryGuid?: string
  isActive?: boolean
  isSet?: boolean
  storeCode?: string
  sortBy?: string
  sortOrder?: 'ascend' | 'descend'
}

export interface BatchUpdatePosProductDto {
  productCode: string
  retailPrice?: number
  purchasePrice?: number
  middlePackageQuantity?: number
  isAutoPricing?: boolean | null
  isSpecialProduct?: boolean | null
  isActive?: boolean | null
  categoryGuid?: string
  localSupplierCode?: string
  productName?: string
  unitWeight?: number
}

export interface SyncProductsToStoresRequest {
  productCodes: string[]
  storeCodes: string[]
  overwrite?: boolean
  fields: SyncProductsToStoresField[]
}

export type SyncProductsToStoresField =
  | 'purchasePrice'
  | 'retailPrice'
  | 'isAutoPricing'
  | 'isSpecialProduct'

export interface SyncProductsToStoresResult {
  successCount: number
  failedCount: number
  errors: string[]
}

export interface PushProductsToHqRequest {
  productCodes: string[]
}

export interface SyncSelectedProductsFromHqRequest {
  productCodes: string[]
}

export interface PushProductsToHqResult {
  successCount: number
  failedCount: number
  totalCount: number
  affectedRowCount?: number
  productsAdded?: number
  productsUpdated?: number
  storeRetailPricesCreated?: number
  storeRetailPricesUpdated?: number
  productSetCodesCreated?: number
  productSetCodesUpdated?: number
  storeMultiCodesCreated?: number
  storeMultiCodesUpdated?: number
  errors: string[]
  message?: string
}

export interface HqProductSyncResult {
  addedCount?: number
  updatedCount?: number
  deletedCount?: number
  totalCount?: number
  errorCount?: number
  message?: string
  totalHqProducts?: number
  totalLocalProducts?: number
  productsAdded?: number
  productsUpdated?: number
  productsDeleted?: number
  productsSoftDeleted?: number
  storeRetailPricesCreated?: number
  storeRetailPricesUpdated?: number
  storeRetailPricesDeleted?: number
  productSetCodesCreated?: number
  productSetCodesAdded?: number
  productSetCodesUpdated?: number
  productSetCodesDeleted?: number
  productSetCodesSoftDeleted?: number
  storeMultiCodesCreated?: number
  storeMultiCodesUpdated?: number
  storeMultiCodesDeleted?: number
  errors?: string[]
  durationMs?: number
}

export interface HqProductIncrementalSyncRequest {
  startDate?: string
}

export type HqProductSyncJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export type HqProductSyncMode = 'Full' | 'Incremental'

export interface HqProductFullSyncJobRequest {
  operationId: string
}

export interface HqProductIncrementalSyncJobRequest extends HqProductIncrementalSyncRequest {
  operationId: string
}

export interface HqProductSyncJobResult extends HqProductSyncResult {
  jobId: string
  status: HqProductSyncJobStatus
  mode?: HqProductSyncMode
  operationId?: string
  success?: boolean
  startDate?: string
  result?: HqProductSyncResult
}
