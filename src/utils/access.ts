import type { AccessControl, CurrentUser } from '../types/auth'
import { P, WAREHOUSE_MANAGER_PERMISSION_CODES } from '../types/permissions'

const warehouseManagerPermissionSet = new Set<string>(WAREHOUSE_MANAGER_PERMISSION_CODES)

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
    canManagePricing: false,
    canEditPricing: false,
    canManagePromotions: false,
    canEditPromotions: false,
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
    canAccessDashboard: false,
    canAccessOrderFront: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    managedStoreCodes: () => null,
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

  const hasPermission = (permission: string) => {
    if (isAdmin) return true
    if (isWarehouseManager && warehouseManagerPermissionSet.has(permission)) return true
    return currentUser.permissions?.some((item) => item.toLowerCase() === permission.toLowerCase()) ?? false
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
  const isStoreManager = hasRole('StoreManager') || hasRole('经理')
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
      return currentUser.stores.map((item) => item.storeCode).filter(Boolean)
    }
    return null
  }

  // --- 旧权限（保留兼容）---
  const canReadUser = isAdmin || hasPermission('Users.View')
  const canWriteUser = isAdmin || hasPermission('Users.Create') || hasPermission('Users.Edit')
  const canDeleteUser = isAdmin || hasPermission('Users.Delete')
  const canReadRole = isAdmin || hasPermission('Roles.View')
  const canWriteRole = isAdmin || hasPermission('Roles.Create') || hasPermission('Roles.Edit')
  const canDeleteRole = isAdmin || hasPermission('Roles.Delete')
  const canReadStore = isAdmin || hasPermission('Stores.View')
  const canWriteStore = isAdmin || hasPermission('Stores.Create') || hasPermission('Stores.Edit')
  const canDeleteStore = isAdmin || hasPermission('Stores.Delete')
  const canManageWarehouse = isAdmin || hasPermission('Warehouse.Manage')
  const canManageStore = isAdmin || hasPermission('Stores.Edit') || hasPermission('Warehouse.Manage')

  const canReadOrder = isAdmin || hasPermission('Orders.View')
  const canWriteOrder = isAdmin || hasPermission('Orders.Create') || hasPermission('Orders.Edit')
  const canDeleteOrder = isAdmin || hasPermission('Orders.Delete')
  const canReadProduct = isAdmin || hasPermission('Products.View')
  const canWriteProduct = isAdmin || hasPermission('Products.Create') || hasPermission('Products.Edit')
  const canDeleteProduct = isAdmin || hasPermission('Products.Delete')

  const canViewReports = isAdmin || hasPermission('Reports.View')
  const canExportData = isAdmin || hasPermission('Reports.Export')
  const canModifyPrice = isAdmin || hasPermission('Prices.Modify')
  const canDeletePrice = isAdmin || hasPermission('Prices.Delete')

  // --- 新细粒度权限 ---
  // 仓库
  const canManageWarehouseProducts = isAdmin || hasPermission('Warehouse.ManageProducts') || hasPermission('Warehouse.Manage')
  const canManageWarehouseOrders = isAdmin || hasPermission('Warehouse.ManageOrders') || hasPermission('Warehouse.Manage')
  const canManageWarehouseCategories = isAdmin || hasPermission('Warehouse.ManageCategories') || hasPermission('Warehouse.Manage')
  const canManageWarehouseLocations = isAdmin || hasPermission('Warehouse.ManageLocations') || hasPermission('Warehouse.Manage')
  const canViewContainers = isAdmin || hasPermission('Container.View')
  const canCreateContainer = isAdmin || hasPermission('Container.Create')
  const canEditContainer = isAdmin || hasPermission('Container.Edit')
  const canDeleteContainer = isAdmin || hasPermission('Container.Delete')

  // 分店商品
  const canManageStoreProducts = isAdmin || hasPermission('StoreProducts.View')
  const canEditStoreProducts = isAdmin || hasPermission('StoreProducts.Edit')
  const canCreateStoreProducts = isAdmin || hasPermission('StoreProducts.Create')

  // 分店运营
  const canManageStoreOps = isAdmin || hasPermission('Store.ManageOperations')

  // 本地进货
  const canManageLocalPurchase = isAdmin || hasPermission('LocalPurchase.View') || hasPermission('LocalInvocie.View')
  const canEditLocalPurchase = isAdmin || hasPermission('LocalPurchase.Edit') || hasPermission('LocalInvocie.Edit')

  // 定价策略
  const canManagePricing = isAdmin || hasPermission('PricingStrategy.View')
  const canEditPricing = isAdmin || hasPermission('PricingStrategy.Edit')

  // 促销
  const canManagePromotions = isAdmin || hasPermission('Promotions.View')
  const canEditPromotions = isAdmin || hasPermission('Promotions.Edit')

  // 澳洲供应商
  const canViewAustralianSuppliers = isAdmin || hasPermission('AustralianSuppliers.View')
  const canEditAustralianSuppliers = isAdmin || hasPermission('AustralianSuppliers.Edit')

  // 国内采购
  const canManageDomesticSuppliers = isAdmin || hasPermission('DomesticPurchase.ManageSuppliers')
  const canManageDomesticProducts = isAdmin || hasPermission('DomesticPurchase.ManageProducts')
  const canManageDomesticPrefixCodes = isAdmin || hasPermission('DomesticPurchase.ManagePrefixCodes')

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
    canManagePricing,
    canEditPricing,
    canManagePromotions,
    canEditPromotions,
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
    canAccessDashboard,
    canAccessOrderFront,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    managedStoreCodes,
  }
}
