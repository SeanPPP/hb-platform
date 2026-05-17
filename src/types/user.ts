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

export interface UpdateUserDto {
  username: string
  email: string
  fullName?: string
  isActive?: boolean
}

export interface UserRoleAssignmentDto {
  roleGuids: string[]
}

export interface UserStoreAssignmentDto {
  storeGUID: string
  accessLevel?: string
  isPrimary?: boolean
}

export interface CreateUserDto {
  username: string
  email: string
  password: string
  fullName?: string
  isActive?: boolean
  roleGuids?: string[]
  storeGuids?: string[]
}

export interface UpdateUserPasswordDto {
  newPassword: string
  forcePasswordChange?: boolean
}
