export interface StoreDto {
  storeGUID: string
  storeName: string
  storeCode: string
  description?: string
  address?: string
  contactPhone?: string
  contactEmail?: string
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

export interface UpdateStoreDto {
  storeName: string
  storeCode: string
  description?: string
  address?: string
  contactPhone?: string
  contactEmail?: string
  abn?: string
  brandName?: string
  isActive?: boolean
}

export interface StoreUserDto {
  userGUID: string
  username: string
  fullName?: string
  realName?: string
  email: string
  roles: string[]
  isManageable: boolean
  isActive: boolean
  assignedAt: string
}

export interface StoreDetailDto extends StoreDto {
  users?: StoreUserDto[]
}

export interface AddUserToStoreDto {
  userGUID: string
  isManageable?: boolean
}

export interface StoreUserQueryDto {
  page?: number
  pageSize?: number
  search?: string
  roleGuid?: string
  isActive?: boolean
  sortBy?: string
  sortDirection?: string
}
