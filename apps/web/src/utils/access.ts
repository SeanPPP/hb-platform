import type { AccessControl, CurrentUser } from '../types/auth'
import { P } from '../types/permissions'

const PERMISSION_ALIAS_GROUPS = [
  {
    canonicalCode: P.LocalPurchase.View,
    aliasCodes: ['LocalInvocie.View'],
  },
  {
    canonicalCode: P.LocalPurchase.Edit,
    aliasCodes: ['LocalInvocie.Edit'],
  },
] as const

const permissionAliasMap = new Map<string, string[]>()

PERMISSION_ALIAS_GROUPS.forEach(({ canonicalCode, aliasCodes }) => {
  const codes = [canonicalCode, ...aliasCodes]
  const uniqueCodes = Array.from(new Set(codes.map((code) => code.toLowerCase())))

  codes.forEach((code) => {
    permissionAliasMap.set(code.toLowerCase(), uniqueCodes)
  })
})

function getEquivalentPermissionCodes(permission: string): string[] {
  const normalizedPermission = permission.toLowerCase()
  return permissionAliasMap.get(normalizedPermission) ?? [normalizedPermission]
}

function createEmptyAccess(): AccessControl {
  const alwaysFalse = () => false

  return {
    isAdmin: false,
    isManager: false,
    isUser: false,
    isWarehouseStaff: false,
    isWarehouseManager: false,
    isStoreStaff: false,
    isStoreManager: false,
    isStoreLevelManager: false,
    onlyOrder: false,
    canReadOrder: false,
    canWriteOrder: false,
    canDeleteOrder: false,
    canReadProduct: false,
    canWriteProduct: false,
    canDeleteProduct: false,
    canReadUser: false,
    canWriteUser: false,
    canDeleteUser: false,
    canReadRole: false,
    canWriteRole: false,
    canDeleteRole: false,
    canReadStore: false,
    canWriteStore: false,
    canDeleteStore: false,
    canManageWarehouse: false,
    canManageStore: false,
    canViewReports: false,
    canViewSalesIntelligence: false,
    canViewProductMovementReport: false,
    canExportData: false,
    canModifyPrice: false,
    canDeletePrice: false,
    // 新细粒度权限
    canManageWarehouseProducts: false,
    canManageWarehouseOrders: false,
    canManageWarehouseCategories: false,
    canManageWarehouseLocations: false,
    canViewContainers: false,
    canCreateContainer: false,
    canEditContainer: false,
    canDeleteContainer: false,
    canManageStoreProducts: false,
    canEditStoreProducts: false,
    canCreateStoreProducts: false,
    canManageStoreOps: false,
    canManageLocalPurchase: false,
    canEditLocalPurchase: false,
    canPushLocalPurchaseToHq: false,
    canManagePricing: false,
    canEditPricing: false,
    canManagePromotions: false,
    canEditPromotions: false,
    canManageAdvertisements: false,
    canEditAdvertisements: false,
    canViewAustralianSuppliers: false,
    canEditAustralianSuppliers: false,
    canManageDomesticSuppliers: false,
    canManageDomesticProducts: false,
    canManageDomesticPrefixCodes: false,
    canViewAttendanceSchedule: false,
    canEditAttendanceSchedule: false,
    canViewAttendanceAvailability: false,
    canViewAttendancePunches: false,
    canReviewAttendance: false,
    canEditAttendanceHoliday: false,
    canEditAttendanceSettings: false,
    canViewEmployeeProfiles: false,
    canViewSystemLogs: false,
    canManageSystemSettings: false,
    canManageScheduledTasks: false,
    canViewAppDownloads: false,
    canManageAppDownloads: false,
    canViewDeviceRegistration: false,
    canManageDeviceRegistration: false,
    canViewPosProducts: false,
    canManagePosProducts: false,
    canAccessDashboard: false,
    canAccessOrderFront: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    managedStoreCodes: () => null,
    visibleStoreCodes: () => null,
  }
}

export function buildAccess(currentUser?: CurrentUser | null): AccessControl {
  if (!currentUser) {
    return createEmptyAccess()
  }

  const hasRole = (role: string) =>
    currentUser.roleNames?.some((item) => item.toLowerCase() === role.toLowerCase()) ?? false

  const isAdmin = hasRole('Admin') || hasRole('管理员')
  const isWarehouseManager = hasRole('WarehouseManager') || hasRole('仓库经理')
  const currentPermissionSet = new Set((currentUser.permissions ?? []).map((item) => item.toLowerCase()))

  const hasPermission = (permission: string) => {
    if (isAdmin) return true
    return getEquivalentPermissionCodes(permission).some((code) => currentPermissionSet.has(code))
  }

  const onlyRole = (role: string) => {
    if (!currentUser.roleNames?.length) {
      return false
    }
    return hasRole(role) && currentUser.roleNames.length === 1
  }

  const hasAnyRole = (roles: string[]) => roles.some((role) => hasRole(role))
  const hasAllRoles = (roles: string[]) => roles.every((role) => hasRole(role))

  // --- Role identity flags (backward compat) ---
  const isStoreManager = hasRole('StoreManager') || hasRole('店长') || hasRole('经理')
  const isManager = isStoreManager || isWarehouseManager
  const isUser = hasRole('User') || hasRole('用户')
  const isWarehouseStaff =
    isAdmin ||
    hasRole('WarehouseStaff') ||
    hasRole('仓库员工') ||
    hasRole('WarehouseManager')
  const isStoreStaff = hasRole('StoreStaff') || hasRole('店铺员工')
  const isStoreLevelManager = isStoreManager && !isAdmin && !isWarehouseManager
  const onlyOrder = onlyRole('Order') || hasRole('订货员')

  const managedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null
    }
    if (currentUser.stores?.length) {
      return currentUser.stores
        .filter((item) => item.isManageable)
        .map((item) => item.storeCode)
        .filter(Boolean)
    }
    return []
  }

  const visibleStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null
    }
    if (currentUser.stores?.length) {
      return currentUser.stores
        .map((item) => item.storeCode)
        .filter(Boolean)
    }
    return []
  }

  // --- 旧权限（保留兼容）---
  const canReadUser = isAdmin || hasPermission(P.Users.View)
  const canWriteUser = isAdmin || hasPermission(P.Users.Create) || hasPermission(P.Users.Edit)
  const canDeleteUser = isAdmin || hasPermission(P.Users.Delete)
  const canReadRole = isAdmin || hasPermission(P.Roles.View)
  const canWriteRole = isAdmin || hasPermission(P.Roles.Create) || hasPermission(P.Roles.Edit)
  const canDeleteRole = isAdmin || hasPermission(P.Roles.Delete)
  const canReadStore = isAdmin || hasPermission(P.Stores.View)
  const canWriteStore = isAdmin || hasPermission(P.Stores.Create) || hasPermission(P.Stores.Edit)
  const canDeleteStore = isAdmin || hasPermission(P.Stores.Delete)
  const canManageWarehouse = isAdmin || hasPermission(P.Warehouse.Manage)
  const canManageStore = isAdmin || hasPermission(P.Stores.Edit) || hasPermission(P.Warehouse.Manage)

  const canReadOrder = isAdmin || hasPermission(P.Orders.View)
  const canWriteOrder = isAdmin || hasPermission(P.Orders.Create) || hasPermission(P.Orders.Edit)
  const canDeleteOrder = isAdmin || hasPermission(P.Orders.Delete)
  const canReadProduct = isAdmin || hasPermission(P.Products.View)
  const canWriteProduct = isAdmin || hasPermission(P.Products.Create) || hasPermission(P.Products.Edit)
  const canDeleteProduct = isAdmin || hasPermission(P.Products.Delete)

  const canViewReports = isAdmin || hasPermission(P.Reports.View)
  const canViewProductMovementReport =
    isAdmin || hasPermission(P.Reports.ProductMovementView) || hasPermission(P.Reports.View)
  const canViewSalesIntelligence = canViewReports || canViewProductMovementReport
  const canExportData = isAdmin || hasPermission(P.Reports.Export)
  const canModifyPrice = isAdmin || hasPermission(P.Prices.Modify)
  const canDeletePrice = isAdmin || hasPermission(P.Prices.Delete)

  // --- 新细粒度权限 ---
  // 仓库
  const canManageWarehouseProducts =
    isAdmin || hasPermission(P.Warehouse.ManageProducts) || hasPermission(P.Warehouse.Manage)
  const canManageWarehouseOrders =
    isAdmin || hasPermission(P.Warehouse.ManageOrders) || hasPermission(P.Warehouse.Manage)
  const canManageWarehouseCategories =
    isAdmin || hasPermission(P.Warehouse.ManageCategories) || hasPermission(P.Warehouse.Manage)
  const canManageWarehouseLocations =
    isAdmin || hasPermission(P.Warehouse.ManageLocations) || hasPermission(P.Warehouse.Manage)
  const canViewContainers = isAdmin || hasPermission(P.Container.View)
  const canCreateContainer = isAdmin || hasPermission(P.Container.Create)
  const canEditContainer = isAdmin || hasPermission(P.Container.Edit)
  const canDeleteContainer = isAdmin || hasPermission(P.Container.Delete)

  // 分店商品
  const canManageStoreProducts = isAdmin || hasPermission(P.StoreProducts.View)
  const canEditStoreProducts = isAdmin || hasPermission(P.StoreProducts.Edit)
  const canCreateStoreProducts = isAdmin || hasPermission(P.StoreProducts.Create)

  // 分店运营
  const canManageStoreOps = isAdmin || hasPermission(P.Store.ManageOperations)

  // 本地进货
  const canManageLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.View)
  const canEditLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.Edit)
  const canPushLocalPurchaseToHq = isAdmin || hasPermission(P.LocalPurchase.PushToHq)

  // 定价策略
  const canManagePricing = isAdmin || hasPermission(P.PricingStrategy.View)
  const canEditPricing = isAdmin || hasPermission(P.PricingStrategy.Edit)

  // 促销
  const canManagePromotions = isAdmin || hasPermission(P.Promotions.View)
  const canEditPromotions = isAdmin || hasPermission(P.Promotions.Edit)

  // 广告
  const canManageAdvertisements = isAdmin || hasPermission(P.Advertisements.View)
  const canEditAdvertisements = isAdmin || hasPermission(P.Advertisements.Edit)

  // 澳洲供应商
  const canViewAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.View)
  const canEditAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.Edit)

  // 国内采购
  const canManageDomesticSuppliers = isAdmin || hasPermission(P.DomesticPurchase.ManageSuppliers)
  const canManageDomesticProducts = isAdmin || hasPermission(P.DomesticPurchase.ManageProducts)
  const canManageDomesticPrefixCodes =
    isAdmin || hasPermission(P.DomesticPurchase.ManagePrefixCodes)

  // 排班考勤
  const canViewAttendanceSchedule =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.ScheduleViewStore)
  const canEditAttendanceSchedule =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.ScheduleEditManagedStore)
  const canViewAttendanceAvailability =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.AvailabilityViewManagedStore)
  const canViewAttendancePunches =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.PunchViewManagedStore)
  const canReviewAttendance =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.ApprovalReviewManagedStore) ||
    hasPermission(P.Attendance.LeaveReviewManagedStore)
  const canEditAttendanceHoliday =
    isAdmin ||
    hasPermission(P.Attendance.AdminView) ||
    hasPermission(P.Attendance.HolidayEditManagedStore)
  const canEditAttendanceSettings = isAdmin || hasPermission(P.Attendance.SettingsEdit)
  const canViewEmployeeProfiles = isAdmin || hasPermission(P.EmployeeProfiles.View)
  const canViewSystemLogs = isAdmin || hasPermission(P.System.ViewLogs)
  const canManageScheduledTasks = isAdmin || hasPermission(P.System.ManageScheduledTasks)
  const canManageSystemSettings = isAdmin || hasPermission(P.System.ManageSettings)
  // App 下载页只认独立系统权限，非 Admin 需要后台显式分配。
  const canViewAppDownloads = isAdmin || hasPermission(P.System.ViewAppDownloads)
  // OTA 登记和回撤命令属于发布管理动作，和只读下载页权限分开控制。
  const canManageAppDownloads = isAdmin || hasPermission(P.System.ManageAppDownloads)
  const canManageDeviceRegistration = isAdmin || hasPermission(P.DeviceRegistration.Manage)
  const canViewDeviceRegistration =
    canManageDeviceRegistration || isAdmin || hasPermission(P.DeviceRegistration.View)
  const canViewPosProducts =
    isAdmin || hasPermission(P.PosProducts.View) || hasPermission(P.PosProducts.Manage)
  const canManagePosProducts = isAdmin || hasPermission(P.PosProducts.Manage)

  const canAccessDashboard = isAdmin || hasPermission(P.Dashboard.View)
  const canAccessOrderFront = isAdmin || hasPermission(P.OrderFront.View)

  return {
    isAdmin,
    isManager,
    isUser,
    isWarehouseStaff,
    isWarehouseManager,
    isStoreStaff,
    isStoreManager,
    isStoreLevelManager,
    onlyOrder,
    canReadOrder,
    canWriteOrder,
    canDeleteOrder,
    canReadProduct,
    canWriteProduct,
    canDeleteProduct,
    canReadUser,
    canWriteUser,
    canDeleteUser,
    canReadRole,
    canWriteRole,
    canDeleteRole,
    canReadStore,
    canWriteStore,
    canDeleteStore,
    canManageWarehouse,
    canManageStore,
    canViewReports,
    canViewSalesIntelligence,
    canViewProductMovementReport,
    canExportData,
    canModifyPrice,
    canDeletePrice,
    // 新细粒度
    canManageWarehouseProducts,
    canManageWarehouseOrders,
    canManageWarehouseCategories,
    canManageWarehouseLocations,
    canViewContainers,
    canCreateContainer,
    canEditContainer,
    canDeleteContainer,
    canManageStoreProducts,
    canEditStoreProducts,
    canCreateStoreProducts,
    canManageStoreOps,
    canManageLocalPurchase,
    canEditLocalPurchase,
    canPushLocalPurchaseToHq,
    canManagePricing,
    canEditPricing,
    canManagePromotions,
    canEditPromotions,
    canManageAdvertisements,
    canEditAdvertisements,
    canViewAustralianSuppliers,
    canEditAustralianSuppliers,
    canManageDomesticSuppliers,
    canManageDomesticProducts,
    canManageDomesticPrefixCodes,
    canViewAttendanceSchedule,
    canEditAttendanceSchedule,
    canViewAttendanceAvailability,
    canViewAttendancePunches,
    canReviewAttendance,
    canEditAttendanceHoliday,
    canEditAttendanceSettings,
    canViewEmployeeProfiles,
    canViewSystemLogs,
    canManageScheduledTasks,
    canManageSystemSettings,
    canViewAppDownloads,
    canManageAppDownloads,
    canViewDeviceRegistration,
    canManageDeviceRegistration,
    canViewPosProducts,
    canManagePosProducts,
    canAccessDashboard,
    canAccessOrderFront,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    managedStoreCodes,
    visibleStoreCodes,
  }
}
