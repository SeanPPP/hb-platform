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
  storeRecordCountMin?: number
  storeRecordCountMax?: number
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
  createdCount: number
  updatedCount: number
  failedCount: number
  successCount?: number
  errors: string[]
  message?: string
}

export type SyncProductsToStoresJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | (string & {})

export interface SyncProductsToStoresJobResult {
  jobId: string
  status: SyncProductsToStoresJobStatus
  operationId?: string
  result?: SyncProductsToStoresResult
  message?: string
  isDuplicateRequest?: boolean
  errors?: string[]
}

export interface BatchUpdateProductStoreRecordsChanges {
  purchasePrice?: number
  storeRetailPriceValue?: number
  discountRate?: number
  isAutoPricing?: boolean
  isSpecialProduct?: boolean
  isActive?: boolean
}

export interface BatchUpdateProductStoreRecordsRequest {
  storeCodes: string[]
  changes: BatchUpdateProductStoreRecordsChanges
}

export interface BatchUpdateProductStoreRecordsResult {
  successCount: number
  failedCount: number
  errors: string[]
}

export interface PushProductsToHqItem {
  productCode?: string
  localSupplierCode?: string
  itemNumber?: string
  productName?: string
  englishName?: string
  barcode?: string
  imageUrl?: string
  domesticPrice?: number
  importPrice?: number
  oemPrice?: number
  isNewProduct: boolean
  warehouseIsActive?: boolean
}

export interface PushProductsToHqRequest {
  productCodes: string[]
  items?: PushProductsToHqItem[]
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
  warehouseInventoriesCreated?: number
  warehouseInventoriesUpdated?: number
  storeRetailPricesCreated?: number
  storeRetailPricesUpdated?: number
  productSetCodesCreated?: number
  productSetCodesUpdated?: number
  storeMultiCodesCreated?: number
  storeMultiCodesUpdated?: number
  errors: string[]
  message?: string
}

export interface BatchUpdateSupplierImagesRequest {
  localSupplierCode: string
  urlTemplate: string
  updateHbweb: boolean
  updateHq: boolean
  saveSupplierImageBaseUrl?: boolean
  productCodes?: string[]
}

export interface BatchUpdateSupplierImagesJobRequest extends BatchUpdateSupplierImagesRequest {
  operationId: string
}

export interface BatchUpdateSupplierImagesResult {
  totalCount: number
  hbwebUpdatedCount: number
  hqUpdatedCount: number
  hbwebSkippedExistingImageCount?: number
  hqSkippedExistingImageCount?: number
  skippedCount: number
  hqFailedCount: number
  errors: string[]
  message?: string
}

export type BatchUpdateSupplierImagesJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface BatchUpdateSupplierImagesJobResult {
  jobId: string
  operationId?: string
  status: BatchUpdateSupplierImagesJobStatus
  request?: Partial<BatchUpdateSupplierImagesJobRequest>
  result?: BatchUpdateSupplierImagesResult
  message?: string
  errorMessage?: string
  errors?: string[]
  createdAt?: string
  startedAt?: string
  completedAt?: string
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
