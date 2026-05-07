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
