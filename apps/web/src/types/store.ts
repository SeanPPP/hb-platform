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
  timeZoneId?: string
  returnPolicy?: string
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
  brandName?: string
  timeZoneId?: string
  userGUID?: string
  sortField?: string
  sortOrder?: string
}

export interface CreateStoreDto {
  storeName: string
  storeCode: string
  description?: string
  address?: string
  contactPhone?: string
  contactEmail?: string
  abn?: string
  brandName?: string
  timeZoneId?: string
  returnPolicy?: string
  isActive?: boolean
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
  timeZoneId?: string
  returnPolicy?: string
  isActive?: boolean
}

export type StoreBatchUpdateField =
  | 'timeZoneId'
  | 'abn'
  | 'brandName'
  | 'isActive'
  | 'returnPolicy'

export interface BatchUpdateStoresRequest {
  storeGuids: string[]
  fields: StoreBatchUpdateField[]
  timeZoneId?: string
  abn?: string | null
  brandName?: string | null
  isActive?: boolean
  returnPolicy?: string | null
}

export interface BatchUpdateStoresResult {
  requestedCount: number
  updatedCount: number
  updatedStoreGuids: string[]
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
