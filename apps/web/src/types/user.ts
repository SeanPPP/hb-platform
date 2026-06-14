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
  isActive?: boolean
  isManageable: boolean
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

export interface UserPermissionInheritedSourceDto {
  roleName: string
  permissionCodes: string[]
}

export interface UserPermissionStateDto {
  userGuid: string
  inheritedPermissionCodes: string[]
  directPermissionCodes: string[]
  effectivePermissionCodes: string[]
  inheritedSources: UserPermissionInheritedSourceDto[]
}

export interface UserPermissionAssignmentDto {
  permissions: string[]
}

export interface UserStoreAssignmentDto {
  storeGUID: string
  accessLevel?: string
  isManageable?: boolean
}

export interface CreateUserDto {
  username: string
  email: string
  password: string
  passwordFormat?: 'raw' | 'clientSha256'
  fullName?: string
  isActive?: boolean
  roleGuids?: string[]
  storeGuids?: string[]
}

export interface UpdateUserPasswordDto {
  newPassword: string
  passwordFormat?: 'raw' | 'clientSha256'
  forcePasswordChange?: boolean
}
