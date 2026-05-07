export interface StoreDto {
  storeGUID: string
  storeName: string
  storeCode: string
  address?: string
  contactPhone?: string
  abn?: string
  brandName?: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  totalUsers?: number
  activeUsers?: number
}

export interface StoreQueryDto {
  page?: number
  pageSize?: number
  search?: string
  isActive?: boolean
  userGUID?: string
  sortField?: string
  sortOrder?: string
}
