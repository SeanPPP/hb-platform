import type { UserStoreDto } from './user'

export interface LoginRequest {
  username: string
  password: string
}

export interface SessionResponse {
  accessTokenExpiry: string
  refreshTokenExpiry: string
}

export interface CurrentUser {
  userGUID: string
  username: string
  email: string
  fullName?: string
  phone?: string
  permissions: string[]
  roleNames: string[]
  storeNames: string[]
  stores?: UserStoreDto[]
}

export interface AccessControl {
  isAdmin: boolean
  isManager: boolean
  isUser: boolean
  isWarehouseStaff: boolean
  isWarehouseManager: boolean
  isStoreStaff: boolean
  isStoreManager: boolean
  isStoreLevelManager: boolean
  onlyOrder: boolean
  canReadOrder: boolean
  canWriteOrder: boolean
  canDeleteOrder: boolean
  canReadProduct: boolean
  canWriteProduct: boolean
  canDeleteProduct: boolean
  canReadUser: boolean
  canWriteUser: boolean
  canDeleteUser: boolean
  canReadRole: boolean
  canWriteRole: boolean
  canDeleteRole: boolean
  canReadStore: boolean
  canWriteStore: boolean
  canDeleteStore: boolean
  // 旧权限（保留兼容）
  canManageWarehouse: boolean
  canManageStore: boolean
  canViewReports: boolean
  canExportData: boolean
  canModifyPrice: boolean
  canDeletePrice: boolean
  // 新细粒度权限
  canManageWarehouseProducts: boolean
  canManageWarehouseOrders: boolean
  canManageWarehouseCategories: boolean
  canManageWarehouseLocations: boolean
  canViewContainers: boolean
  canCreateContainer: boolean
  canEditContainer: boolean
  canDeleteContainer: boolean
  canManageStoreProducts: boolean
  canEditStoreProducts: boolean
  canCreateStoreProducts: boolean
  canManageStoreOps: boolean
  canManageLocalPurchase: boolean
  canEditLocalPurchase: boolean
  canManagePricing: boolean
  canEditPricing: boolean
  canManagePromotions: boolean
  canEditPromotions: boolean
  canViewAustralianSuppliers: boolean
  canEditAustralianSuppliers: boolean
  canManageDomesticSuppliers: boolean
  canManageDomesticProducts: boolean
  canManageDomesticPrefixCodes: boolean
  canViewAttendanceSchedule: boolean
  canEditAttendanceSchedule: boolean
  canViewAttendanceAvailability: boolean
  canViewAttendancePunches: boolean
  canReviewAttendance: boolean
  canEditAttendanceHoliday: boolean
  canEditAttendanceSettings: boolean
  canAccessDashboard: boolean
  canAccessOrderFront: boolean
  hasPermission: (permission: string) => boolean
  hasRole: (role: string) => boolean
  onlyRole: (role: string) => boolean
  hasAnyRole: (roles: string[]) => boolean
  hasAllRoles: (roles: string[]) => boolean
  managedStoreCodes: () => string[] | null
}

export interface NavigationMenuDto {
  path: string
  titleKey: string
  icon: string
  permission?: string
  children?: NavigationMenuDto[]
}
