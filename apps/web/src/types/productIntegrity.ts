export interface ProductIntegrityCheckResultDto {
  storeReports: StoreIntegrityReport[]
  productSetCodeReport?: TableIntegrityReport | null
  checkTime: string
  durationSeconds: number
}

export interface StoreIntegrityReport {
  storeCode: string
  storeName: string
  tableReports: TableIntegrityReport[]
}

export interface TableIntegrityReport {
  tableName: string
  totalChecked: number
  orphanedCount: number
  missingCount: number
  invalidKeyCount: number
  orphanedProductCodes: string[]
  missingProductCodes: string[]
  errors: string[]
}

export interface ProductIntegrityFixRequestDto {
  fixStoreRetailPrice?: boolean
  fixStoreMultiCodeProduct?: boolean
  fixProductSetCode?: boolean
  selectedStoreCodes?: string[]
  dryRun?: boolean
}

export interface ProductIntegrityFixResultDto {
  reports: TableFixReport[]
  fixTime: string
  durationSeconds: number
  isDryRun: boolean
}

export interface TableFixReport {
  tableName: string
  deletedCount: number
  addedCount: number
  errorCount: number
  errors: string[]
}
