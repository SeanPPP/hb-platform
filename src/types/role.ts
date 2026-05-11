export interface RoleQueryDto {
  page?: number
  pageNumber?: number
  pageSize?: number
  searchKeyword?: string
  isActive?: boolean
  sortBy?: string
  sortDirection?: string
}

export interface RoleDto {
  roleGUID: string
  roleName: string
  description?: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  userCount: number
}

export interface RoleUserDto {
  userGUID: string
  username: string
  email: string
  fullName?: string
  isActive: boolean
  assignedAt: string
}

export interface RoleDetailDto extends RoleDto {
  users: RoleUserDto[]
  permissions: string[]
}

export interface UpdateRoleDto {
  roleName: string
  description?: string
  isActive?: boolean
}

export interface RoleOptionDto {
  roleGUID: string
  roleName: string
  description?: string
  isActive?: boolean
}

/** Single permission returned by GET /api/roles/permissions */
export interface PermissionDto {
  name: string
  displayName: string
  description?: string
  category: string
  isSystemPermission: boolean
  createdAt: string
  createdBy?: string
  updatedAt?: string
  updatedBy?: string
}

/** Permission category with its child permissions */
export interface PermissionCategoryDto {
  category: string
  displayName: string
  description?: string
  permissions: PermissionDto[]
}

/** Request body for POST /api/roles/guid/{guid}/permissions */
export interface RolePermissionAssignmentDto {
  permissions: string[]
}

export interface SysPermissionDto {
  id: string
  code: string
  name: string
  category: string
  description?: string
}

export interface CreateSysPermissionDto {
  code: string
  name: string
  category: string
  description?: string
  actions?: string[]
}
