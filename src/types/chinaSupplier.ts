export interface ChinaSupplierItem {
  guid: string
  supplierCode: string
  supplierName: string
  shopNumber?: string
  contactPerson?: string
  phone?: string
  email?: string
  storefrontPhoto?: string
  status: number
  remarks?: string
  createdAt?: string
  updatedAt?: string
}

export interface ChinaSupplierListParams {
  page?: number
  pageSize?: number
  search?: string
  status?: number
  sortField?: string
  sortDirection?: 'asc' | 'desc'
}

export interface ChinaSupplierListResult {
  items: ChinaSupplierItem[]
  total: number
  page: number
  pageSize: number
}

export interface SaveChinaSupplierPayload {
  supplierCode: string
  supplierName: string
  shopNumber?: string
  contactPerson?: string
  phone?: string
  email?: string
  storefrontPhoto?: string
  status?: number
  remarks?: string
}

export interface SyncChinaSupplierResult {
  totalProcessed: number
  successCount: number
  failCount: number
  insertedCount: number
  updatedCount: number
  errors: string[]
}
