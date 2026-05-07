export interface UserQueryDto {
  page?: number
  pageNumber?: number
  pageSize?: number
  search?: string
  searchKeyword?: string
  roleGuid?: string
  storeGuid?: string
  isActive?: boolean
  sortBy?: string
  sortDirection?: string
}

export interface UserStoreDto {
  storeGUID: string
  storeName: string
  storeCode: string
  isPrimary: boolean
  assignedAt: string
}

export interface UserDto {
  userGUID: string
  username: string
  email: string
  fullName?: string
  phone?: string
  lastLoginAt?: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  currentStore?: string
  roleNames: string[]
  storeNames: string[]
  stores?: UserStoreDto[]
  permissions?: string[]
}

export interface UserDetailDto extends UserDto {}
