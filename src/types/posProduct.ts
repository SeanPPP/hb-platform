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
  createdAt?: string
  updatedAt?: string
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
}

export interface SyncProductsToStoresResult {
  successCount: number
  failedCount: number
  errors: string[]
}

export interface HqProductSyncResult {
  createdCount: number
  updatedCount: number
  skippedCount: number
  errorCount: number
  errors: string[]
}
