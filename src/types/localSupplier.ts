export interface LocalSupplierDto {
  guid: string
  localSupplierCode: string
  name: string
  status: number
  contactPerson?: string
  phone?: string
  email?: string
  remark?: string
}

export interface LocalSupplierQueryDto {
  pageIndex?: number
  pageSize?: number
  keyword?: string
  status?: string
  sortBy?: string
  sortOrder?: 'ascend' | 'descend'
}

export interface CreateLocalSupplierDto {
  name: string
  status?: number
  contactPerson?: string
  phone?: string
  email?: string
  remark?: string
}

export interface SyncLocalSupplierResult {
  createdCount: number
  updatedCount: number
  deactivatedCount: number
  skippedCount: number
  errors: string[]
}
