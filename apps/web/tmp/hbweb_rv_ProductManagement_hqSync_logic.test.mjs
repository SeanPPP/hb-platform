// src/pages/PosAdmin/ProductManagement/ProductManagement.hqSync.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/types/permissions.ts
var P = {
  Users: {
    View: "Users.View",
    Create: "Users.Create",
    Edit: "Users.Edit",
    Delete: "Users.Delete",
    ManageRoles: "Users.ManageRoles",
    ManageStores: "Users.ManageStores",
    ManagePosTerminalPermissions: "Users.ManagePosTerminalPermissions",
    ResetPassword: "Users.ResetPassword"
  },
  Roles: {
    View: "Roles.View",
    Create: "Roles.Create",
    Edit: "Roles.Edit",
    Delete: "Roles.Delete",
    ManagePermissions: "Roles.ManagePermissions",
    ManageUsers: "Roles.ManageUsers"
  },
  Stores: {
    View: "Stores.View",
    Create: "Stores.Create",
    Edit: "Stores.Edit",
    Delete: "Stores.Delete",
    Sync: "Stores.Sync"
  },
  Products: {
    View: "Products.View",
    Create: "Products.Create",
    Edit: "Products.Edit",
    Delete: "Products.Delete"
  },
  Orders: {
    View: "Orders.View",
    Create: "Orders.Create",
    Edit: "Orders.Edit",
    Delete: "Orders.Delete"
  },
  Warehouse: {
    View: "Warehouse.View",
    Manage: "Warehouse.Manage",
    ManageProducts: "Warehouse.ManageProducts",
    ManageCategories: "Warehouse.ManageCategories",
    ManageLocations: "Warehouse.ManageLocations",
    ManageOrders: "Warehouse.ManageOrders"
  },
  Container: {
    View: "Container.View",
    Create: "Container.Create",
    Edit: "Container.Edit",
    Delete: "Container.Delete"
  },
  InstallmentOrders: {
    View: "InstallmentOrders.View"
  },
  StoreVouchers: {
    View: "StoreVouchers.View"
  },
  DomesticPurchase: {
    View: "DomesticPurchase.View",
    ManageSuppliers: "DomesticPurchase.ManageSuppliers",
    ManageProducts: "DomesticPurchase.ManageProducts",
    ManagePrefixCodes: "DomesticPurchase.ManagePrefixCodes"
  },
  Prices: {
    View: "Prices.View",
    Modify: "Prices.Modify",
    Delete: "Prices.Delete"
  },
  Reports: {
    View: "Reports.View",
    Export: "Reports.Export",
    ProductMovementView: "Reports.ProductMovement.View"
  },
  System: {
    ViewLogs: "System.ViewLogs",
    ManageScheduledTasks: "System.ManageScheduledTasks",
    ManageSettings: "System.ManageSettings",
    ViewAppDownloads: "System.ViewAppDownloads",
    ManageAppDownloads: "System.ManageAppDownloads"
  },
  DeviceRegistration: {
    View: "DeviceRegistration.View",
    Manage: "DeviceRegistration.Manage"
  },
  EmployeeProfiles: {
    View: "EmployeeProfiles.View",
    Edit: "EmployeeProfiles.Edit"
  },
  StoreProducts: {
    View: "StoreProducts.View",
    Create: "StoreProducts.Create",
    Edit: "StoreProducts.Edit"
  },
  Promotions: {
    View: "Promotions.View",
    Edit: "Promotions.Edit"
  },
  Advertisements: {
    View: "Advertisements.View",
    Edit: "Advertisements.Edit"
  },
  PricingStrategy: {
    View: "PricingStrategy.View",
    Edit: "PricingStrategy.Edit"
  },
  LocalPurchase: {
    View: "LocalPurchase.View",
    Edit: "LocalPurchase.Edit",
    PushToHq: "LocalPurchase.PushToHq"
  },
  AustralianSuppliers: {
    View: "AustralianSuppliers.View",
    Edit: "AustralianSuppliers.Edit"
  },
  Store: {
    ManageOperations: "Store.ManageOperations",
    ManageInfo: "Store.ManageInfo"
  },
  PosProducts: {
    View: "PosProducts.View",
    Manage: "PosProducts.Manage"
  },
  PosTerminal: {
    AuditView: "Permissions.PosTerminal.Audit.View"
  },
  Dashboard: {
    View: "Dashboard"
  },
  OrderFront: {
    View: "OrderFront"
  },
  Attendance: {
    ScheduleViewSelf: "Attendance.Schedule.ViewSelf",
    ScheduleViewStore: "Attendance.Schedule.ViewStore",
    ScheduleEditManagedStore: "Attendance.Schedule.EditManagedStore",
    AvailabilitySubmitSelf: "Attendance.Availability.SubmitSelf",
    AvailabilityViewManagedStore: "Attendance.Availability.ViewManagedStore",
    PunchSelf: "Attendance.Punch.Self",
    PunchViewManagedStore: "Attendance.Punch.ViewManagedStore",
    ApprovalViewManagedStore: "Attendance.Approval.ViewManagedStore",
    ApprovalReviewManagedStore: "Attendance.Approval.ReviewManagedStore",
    HolidayViewStore: "Attendance.Holiday.ViewStore",
    HolidayEditManagedStore: "Attendance.Holiday.EditManagedStore",
    LeaveApplySelf: "Attendance.Leave.ApplySelf",
    LeaveViewManagedStore: "Attendance.Leave.ViewManagedStore",
    LeaveReviewManagedStore: "Attendance.Leave.ReviewManagedStore",
    SettingsEdit: "Attendance.Settings.Edit",
    AdminView: "Attendance.Admin.View"
  }
};
var ALL_PERMISSIONS = Object.values(P).flatMap(
  (group) => Object.values(group)
);

// src/utils/webPortalAccess.ts
var ADMIN_ENTRY_RULES = [
  {
    defaultPath: "/dashboard",
    targetPrefixes: ["/dashboard"],
    canAccess: (access) => access.canAccessDashboard
  },
  {
    defaultPath: "/warehouse/store-orders",
    targetPrefixes: [
      "/warehouse/store-orders",
      "/warehouse/preorders",
      "/warehouse/store-order"
    ],
    canAccess: (access) => access.canManageWarehouseOrders
  },
  {
    defaultPath: "/warehouse/store-order-import-price-variance",
    targetPrefixes: ["/warehouse/store-order-import-price-variance"],
    canAccess: (access) => access.canManageStoreOrderImportPriceVariance
  },
  {
    defaultPath: "/warehouse/store-orders",
    // 旧 Warehouse.Manage 只覆盖其实际派生的仓库业务页，不能绕过价差或货柜叶子权限。
    targetPrefixes: [
      "/warehouse/products",
      "/warehouse/categories",
      "/warehouse/locations",
      "/warehouse/product-grade-management"
    ],
    canAccess: (access) => access.canManageWarehouse
  },
  {
    defaultPath: "/warehouse/containers",
    targetPrefixes: [
      "/warehouse/containers",
      "/warehouse/container/detail",
      "/warehouse/container/allocation-sales"
    ],
    canAccess: (access) => access.canViewContainers
  },
  {
    defaultPath: "/executive-sales-intelligence/product-movement-report",
    targetPrefixes: ["/executive-sales-intelligence/product-movement-report"],
    // Reports.View 兼容叶子页面访问，但后台导航入口与后端一致，仅认专用权限。
    canAccess: (access) => access.hasPermission(P.Reports.ProductMovementView)
  },
  {
    defaultPath: "/executive-sales-intelligence/purchase-amount-dashboard",
    targetPrefixes: [
      "/executive-sales-intelligence/purchase-amount-dashboard",
      "/pos-admin/local-supplier-invoices",
      "/pos-admin/local-supplier-purchase-sales-analysis",
      "/pos-admin/invoice-detail"
    ],
    canAccess: (access) => access.canManageLocalPurchase
  },
  {
    defaultPath: "/system/invoice-email-settings",
    targetPrefixes: [
      "/system/invoice-email-settings",
      "/system/payment-terminal-settings",
      "/system/emergency-login-keys"
    ],
    canAccess: (access) => access.canManageSystemSettings
  },
  {
    defaultPath: "/system/app-downloads",
    targetPrefixes: ["/system/app-downloads", "/system/wpf-versions"],
    canAccess: (access) => access.canViewAppDownloads
  },
  {
    defaultPath: "/pos-admin/operation-logs",
    targetPrefixes: ["/pos-admin/operation-logs"],
    canAccess: (access) => access.canViewOperationAudits
  }
];
function hasBackendNavigationAccess(access) {
  return access.isAdmin || ADMIN_ENTRY_RULES.some((rule) => rule.canAccess(access));
}

// src/utils/access.ts
var PERMISSION_ALIAS_GROUPS = [
  {
    canonicalCode: P.LocalPurchase.View,
    aliasCodes: ["LocalInvocie.View"]
  },
  {
    canonicalCode: P.LocalPurchase.Edit,
    aliasCodes: ["LocalInvocie.Edit"]
  }
];
var permissionAliasMap = /* @__PURE__ */ new Map();
PERMISSION_ALIAS_GROUPS.forEach(({ canonicalCode, aliasCodes }) => {
  const codes = [canonicalCode, ...aliasCodes];
  const uniqueCodes = Array.from(new Set(codes.map((code) => code.toLowerCase())));
  codes.forEach((code) => {
    permissionAliasMap.set(code.toLowerCase(), uniqueCodes);
  });
});
function getEquivalentPermissionCodes(permission) {
  const normalizedPermission = permission.toLowerCase();
  return permissionAliasMap.get(normalizedPermission) ?? [normalizedPermission];
}
function createEmptyAccess() {
  const alwaysFalse = () => false;
  return {
    isAdmin: false,
    isManager: false,
    isUser: false,
    isWarehouseStaff: false,
    isWarehouseStaffOnly: false,
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
    canManageStoreOrderImportPriceVariance: false,
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
    canViewOperationAudits: false,
    canManageSystemSettings: false,
    canManageScheduledTasks: false,
    canViewAppDownloads: false,
    canManageAppDownloads: false,
    canViewDeviceRegistration: false,
    canManageDeviceRegistration: false,
    canViewPosProducts: false,
    canManagePosProducts: false,
    canAccessAdminShell: false,
    canAccessDashboard: false,
    canAccessOrderFront: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    managedStoreCodes: () => null,
    visibleStoreCodes: () => null
  };
}
function buildAccess(currentUser) {
  if (!currentUser) {
    return createEmptyAccess();
  }
  const hasRole = (role) => currentUser.roleNames?.some((item) => item.toLowerCase() === role.toLowerCase()) ?? false;
  const isAdmin = hasRole("Admin") || hasRole("\u7BA1\u7406\u5458") || hasRole("SuperAdmin") || hasRole("\u8D85\u7EA7\u7BA1\u7406\u5458");
  const isWarehouseManager = hasRole("WarehouseManager") || hasRole("\u4ED3\u5E93\u7ECF\u7406");
  const currentPermissionSet = new Set((currentUser.permissions ?? []).map((item) => item.toLowerCase()));
  const hasPermission = (permission) => {
    if (isAdmin) return true;
    return getEquivalentPermissionCodes(permission).some((code) => currentPermissionSet.has(code));
  };
  const onlyRole = (role) => {
    if (!currentUser.roleNames?.length) {
      return false;
    }
    return hasRole(role) && currentUser.roleNames.length === 1;
  };
  const hasAnyRole = (roles) => roles.some((role) => hasRole(role));
  const hasAllRoles = (roles) => roles.every((role) => hasRole(role));
  const isStoreManager = hasRole("StoreManager") || hasRole("\u5E97\u957F") || hasRole("\u7ECF\u7406");
  const isManager = isStoreManager || isWarehouseManager;
  const isUser = hasRole("User") || hasRole("\u7528\u6237");
  const isWarehouseStaff = isAdmin || hasRole("WarehouseStaff") || hasRole("\u4ED3\u5E93\u5458\u5DE5") || hasRole("WarehouseManager");
  const isWarehouseStaffOnly = isWarehouseStaff && !isAdmin && !isWarehouseManager && (hasRole("WarehouseStaff") || hasRole("\u4ED3\u5E93\u5458\u5DE5"));
  const isStoreStaff = hasRole("StoreStaff") || hasRole("\u5E97\u94FA\u5458\u5DE5");
  const isStoreLevelManager = isStoreManager && !isAdmin && !isWarehouseManager;
  const onlyOrder = onlyRole("Order") || hasRole("\u8BA2\u8D27\u5458");
  const managedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.filter((item) => item.isManageable).map((item) => item.storeCode).filter(Boolean);
    }
    return [];
  };
  const visibleStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.map((item) => item.storeCode).filter(Boolean);
    }
    return [];
  };
  const canReadUser = isAdmin || hasPermission(P.Users.View);
  const canWriteUser = isAdmin || hasPermission(P.Users.Create) || hasPermission(P.Users.Edit);
  const canDeleteUser = isAdmin || hasPermission(P.Users.Delete);
  const canReadRole = isAdmin || hasPermission(P.Roles.View);
  const canWriteRole = isAdmin || hasPermission(P.Roles.Create) || hasPermission(P.Roles.Edit);
  const canDeleteRole = isAdmin || hasPermission(P.Roles.Delete);
  const canReadStore = isAdmin || hasPermission(P.Stores.View);
  const canWriteStore = isAdmin || hasPermission(P.Stores.Create) || hasPermission(P.Stores.Edit);
  const canDeleteStore = isAdmin || hasPermission(P.Stores.Delete);
  const canManageWarehouse = isAdmin || hasPermission(P.Warehouse.Manage);
  const canManageStore = isAdmin || hasPermission(P.Stores.Edit) || hasPermission(P.Warehouse.Manage);
  const canReadOrder = isAdmin || hasPermission(P.Orders.View);
  const canWriteOrder = isAdmin || hasPermission(P.Orders.Create) || hasPermission(P.Orders.Edit);
  const canDeleteOrder = isAdmin || hasPermission(P.Orders.Delete);
  const canReadProduct = isAdmin || hasPermission(P.Products.View);
  const canWriteProduct = isAdmin || hasPermission(P.Products.Create) || hasPermission(P.Products.Edit);
  const canDeleteProduct = isAdmin || hasPermission(P.Products.Delete);
  const canViewReports = isAdmin || hasPermission(P.Reports.View);
  const canViewProductMovementReport = isAdmin || hasPermission(P.Reports.ProductMovementView) || hasPermission(P.Reports.View);
  const canViewSalesIntelligence = canViewReports || canViewProductMovementReport || hasPermission(P.LocalPurchase.View);
  const canExportData = isAdmin || hasPermission(P.Reports.Export);
  const canModifyPrice = isAdmin || hasPermission(P.Prices.Modify);
  const canDeletePrice = isAdmin || hasPermission(P.Prices.Delete);
  const canManageWarehouseProducts = isAdmin || hasPermission(P.Warehouse.ManageProducts) || hasPermission(P.Warehouse.Manage);
  const canManageWarehouseOrders = isAdmin || hasPermission(P.Warehouse.ManageOrders) || hasPermission(P.Warehouse.Manage);
  const canManageStoreOrderImportPriceVariance = canManageWarehouseOrders && !isWarehouseStaffOnly;
  const canManageWarehouseCategories = isAdmin || hasPermission(P.Warehouse.ManageCategories) || hasPermission(P.Warehouse.Manage);
  const canManageWarehouseLocations = isAdmin || hasPermission(P.Warehouse.ManageLocations) || hasPermission(P.Warehouse.Manage);
  const canViewContainers = isAdmin || hasPermission(P.Container.View);
  const canCreateContainer = isAdmin || hasPermission(P.Container.Create);
  const canEditContainer = isAdmin || hasPermission(P.Container.Edit);
  const canDeleteContainer = isAdmin || hasPermission(P.Container.Delete);
  const canManageStoreProducts = isAdmin || hasPermission(P.StoreProducts.View);
  const canEditStoreProducts = isAdmin || hasPermission(P.StoreProducts.Edit);
  const canCreateStoreProducts = isAdmin || hasPermission(P.StoreProducts.Create);
  const canManageStoreOps = isAdmin || hasPermission(P.Store.ManageOperations);
  const canManageLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.View);
  const canEditLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.Edit);
  const canPushLocalPurchaseToHq = isAdmin || hasPermission(P.LocalPurchase.PushToHq);
  const canManagePricing = isAdmin || hasPermission(P.PricingStrategy.View);
  const canEditPricing = isAdmin || hasPermission(P.PricingStrategy.Edit);
  const canManagePromotions = isAdmin || hasPermission(P.Promotions.View);
  const canEditPromotions = isAdmin || hasPermission(P.Promotions.Edit);
  const canManageAdvertisements = isAdmin || hasPermission(P.Advertisements.View);
  const canEditAdvertisements = isAdmin || hasPermission(P.Advertisements.Edit);
  const canViewAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.View);
  const canEditAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.Edit);
  const canManageDomesticSuppliers = isAdmin || hasPermission(P.DomesticPurchase.ManageSuppliers);
  const canManageDomesticProducts = isAdmin || hasPermission(P.DomesticPurchase.ManageProducts);
  const canManageDomesticPrefixCodes = isAdmin || hasPermission(P.DomesticPurchase.ManagePrefixCodes);
  const canViewAttendanceSchedule = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ScheduleViewStore);
  const canEditAttendanceSchedule = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ScheduleEditManagedStore);
  const canViewAttendanceAvailability = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.AvailabilityViewManagedStore);
  const canViewAttendancePunches = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.PunchViewManagedStore);
  const canReviewAttendance = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ApprovalReviewManagedStore) || hasPermission(P.Attendance.LeaveReviewManagedStore);
  const canEditAttendanceHoliday = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.HolidayEditManagedStore);
  const canEditAttendanceSettings = isAdmin || hasPermission(P.Attendance.SettingsEdit);
  const canViewEmployeeProfiles = isAdmin || hasPermission(P.EmployeeProfiles.View);
  const canViewSystemLogs = isAdmin || hasPermission(P.System.ViewLogs);
  const canViewOperationAudits = isAdmin || hasPermission(P.PosTerminal.AuditView);
  const canManageScheduledTasks = isAdmin || hasPermission(P.System.ManageScheduledTasks);
  const canManageSystemSettings = isAdmin || hasPermission(P.System.ManageSettings);
  const canManageAppDownloads = isAdmin || hasPermission(P.System.ManageAppDownloads);
  const canViewAppDownloads = isAdmin || canManageAppDownloads || hasPermission(P.System.ViewAppDownloads);
  const canManageDeviceRegistration = isAdmin || hasPermission(P.DeviceRegistration.Manage);
  const canViewDeviceRegistration = canManageDeviceRegistration || isAdmin || hasPermission(P.DeviceRegistration.View);
  const canViewPosProducts = isAdmin || hasPermission(P.PosProducts.View) || hasPermission(P.PosProducts.Manage);
  const canManagePosProducts = isAdmin || hasPermission(P.PosProducts.Manage);
  const canAccessDashboard = isAdmin || hasPermission(P.Dashboard.View);
  const canAccessAdminShell = hasBackendNavigationAccess({
    isAdmin,
    canAccessDashboard,
    canManageWarehouse,
    canManageWarehouseOrders,
    canManageStoreOrderImportPriceVariance,
    canViewContainers,
    canViewProductMovementReport,
    canManageLocalPurchase,
    canEditLocalPurchase,
    canManageSystemSettings,
    canViewAppDownloads,
    canViewOperationAudits,
    hasPermission
  });
  const canAccessOrderFront = isAdmin || hasPermission(P.OrderFront.View) || isWarehouseStaffOnly && hasPermission(P.Orders.Create);
  return {
    isAdmin,
    isManager,
    isUser,
    isWarehouseStaff,
    isWarehouseStaffOnly,
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
    canManageStoreOrderImportPriceVariance,
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
    canViewOperationAudits,
    canManageScheduledTasks,
    canManageSystemSettings,
    canViewAppDownloads,
    canManageAppDownloads,
    canViewDeviceRegistration,
    canManageDeviceRegistration,
    canViewPosProducts,
    canManagePosProducts,
    canAccessAdminShell,
    canAccessDashboard,
    canAccessOrderFront,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    managedStoreCodes,
    visibleStoreCodes
  };
}

// src/pages/PosAdmin/ProductManagement/storeRecordSorting.ts
function compareText(left, right) {
  return (left || "").localeCompare(right || "");
}
function compareNumber(left, right) {
  const leftMissing = left === void 0 || left === null;
  const rightMissing = right === void 0 || right === null;
  if (leftMissing && rightMissing) return 0;
  if (leftMissing) return 1;
  if (rightMissing) return -1;
  return left - right;
}
function compareBoolean(left, right) {
  return Number(left ?? false) - Number(right ?? false);
}
function compareDateText(left, right) {
  const leftTime = left ? new Date(left).getTime() : Number.POSITIVE_INFINITY;
  const rightTime = right ? new Date(right).getTime() : Number.POSITIVE_INFINITY;
  return leftTime - rightTime;
}
function compareProductStoreRecordsByStoreCode(a, b) {
  return compareText(a.storeCode, b.storeCode);
}
function compareProductStoreRecordsByName(a, b) {
  const leftName = a.storeName || a.storeCode || "";
  const rightName = b.storeName || b.storeCode || "";
  const nameResult = leftName.localeCompare(rightName);
  if (nameResult !== 0) return nameResult;
  return (a.storeCode || "").localeCompare(b.storeCode || "");
}
function compareProductStoreRecordsByStoreProductCode(a, b) {
  return compareText(a.storeProductCode, b.storeProductCode);
}
function compareProductStoreRecordsByPurchasePrice(a, b) {
  return compareNumber(a.purchasePrice, b.purchasePrice);
}
function compareProductStoreRecordsByRetailPrice(a, b) {
  return compareNumber(a.storeRetailPriceValue, b.storeRetailPriceValue);
}
function compareProductStoreRecordsByDiscountRate(a, b) {
  return compareNumber(a.discountRate, b.discountRate);
}
function compareProductStoreRecordsByAutoPricing(a, b) {
  return compareBoolean(a.isAutoPricing, b.isAutoPricing);
}
function compareProductStoreRecordsBySpecialProduct(a, b) {
  return compareBoolean(a.isSpecialProduct, b.isSpecialProduct);
}
function compareProductStoreRecordsByActive(a, b) {
  return compareBoolean(a.isActive, b.isActive);
}
function compareProductStoreRecordsByUpdatedAt(a, b) {
  return compareDateText(a.updatedAt, b.updatedAt);
}
function compareProductStoreRecordsByUpdatedBy(a, b) {
  return compareText(a.updatedBy, b.updatedBy);
}

// src/pages/PosAdmin/ProductManagement/productIntegrityReport.ts
function appendIssueRows(rows, scope, tableReport) {
  if (!tableReport) return;
  if (tableReport.orphanedCount > 0) {
    rows.push({
      key: `${scope}-${tableReport.tableName}-orphaned`,
      scope,
      tableName: tableReport.tableName,
      issueType: "\u5B64\u7ACB\u8BB0\u5F55",
      count: tableReport.orphanedCount,
      sampleProductCodes: tableReport.orphanedProductCodes ?? []
    });
  }
  if (tableReport.missingCount > 0) {
    rows.push({
      key: `${scope}-${tableReport.tableName}-missing`,
      scope,
      tableName: tableReport.tableName,
      issueType: "\u7F3A\u5931\u8BB0\u5F55",
      count: tableReport.missingCount,
      sampleProductCodes: tableReport.missingProductCodes ?? []
    });
  }
  if ((tableReport.invalidKeyCount ?? 0) > 0) {
    rows.push({
      key: `${scope}-${tableReport.tableName}-invalid-key`,
      scope,
      tableName: tableReport.tableName,
      issueType: "\u65E0\u6548\u5173\u952E\u7F16\u7801",
      count: tableReport.invalidKeyCount,
      sampleProductCodes: tableReport.errors ?? []
    });
  }
}
function getIssueCount(tableReport) {
  if (!tableReport) return 0;
  return (tableReport.orphanedCount ?? 0) + (tableReport.missingCount ?? 0) + (tableReport.invalidKeyCount ?? 0);
}
function getStoreScope(storeCode, storeName) {
  return storeName && storeName !== storeCode ? `${storeName} (${storeCode})` : storeCode;
}
function buildProductIntegritySummary(result) {
  if (!result) {
    return {
      storeCount: 0,
      totalChecked: 0,
      issueCount: 0,
      durationSeconds: 0,
      issueRows: []
    };
  }
  const issueRows = [];
  appendIssueRows(issueRows, "\u603B\u90E8", result.productSetCodeReport);
  const productSetCodeTotal = result.productSetCodeReport?.totalChecked ?? 0;
  const storeReports = result.storeReports ?? [];
  const storeTotal = storeReports.reduce(
    (sum, store) => sum + (store.tableReports ?? []).reduce(
      (tableSum, table) => tableSum + (table.totalChecked ?? 0),
      0
    ),
    0
  );
  const productSetCodeIssues = getIssueCount(result.productSetCodeReport);
  let storeIssueCount = 0;
  storeReports.forEach((store) => {
    const scope = getStoreScope(store.storeCode, store.storeName);
    const tableReports = store.tableReports ?? [];
    tableReports.forEach((tableReport) => {
      storeIssueCount += getIssueCount(tableReport);
      appendIssueRows(issueRows, scope, tableReport);
    });
  });
  return {
    storeCount: storeReports.length,
    totalChecked: productSetCodeTotal + storeTotal,
    issueCount: productSetCodeIssues + storeIssueCount,
    durationSeconds: result.durationSeconds ?? 0,
    issueRows
  };
}
function buildProductIntegrityFixSummary(result) {
  const reports = result.reports ?? [];
  return reports.reduce(
    (summary, report) => ({
      deletedCount: summary.deletedCount + (report.deletedCount ?? 0),
      addedCount: summary.addedCount + (report.addedCount ?? 0),
      errorCount: summary.errorCount + (report.errorCount ?? 0),
      errors: [...summary.errors, ...report.errors ?? []]
    }),
    {
      deletedCount: 0,
      addedCount: 0,
      errorCount: 0,
      errors: []
    }
  );
}

// src/pages/PosAdmin/ProductManagement/ProductManagement.hqSync.logic.test.ts
function createCurrentUser(overrides = {}) {
  return {
    userGUID: "test-user-guid",
    username: "tester",
    email: "tester@example.com",
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides
  };
}
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
var pageFile = path.resolve(process.cwd(), "src/pages/PosAdmin/ProductManagement/index.tsx");
var typeFile = path.resolve(process.cwd(), "src/types/posProduct.ts");
var productIntegrityTypeFile = path.resolve(process.cwd(), "src/types/productIntegrity.ts");
var serviceFile = path.resolve(process.cwd(), "src/services/posProductService.ts");
var productIntegrityHelperFile = path.resolve(process.cwd(), "src/pages/PosAdmin/ProductManagement/productIntegrityReport.ts");
var globalStyleFile = path.resolve(process.cwd(), "src/styles/global.css");
var pageSource = readFileSync(pageFile, "utf8");
var typeSource = readFileSync(typeFile, "utf8");
var productIntegrityTypeSource = readFileSync(productIntegrityTypeFile, "utf8");
var serviceSource = readFileSync(serviceFile, "utf8");
var productIntegrityHelperSource = readFileSync(productIntegrityHelperFile, "utf8");
var globalStyleSource = readFileSync(globalStyleFile, "utf8");
function assertSourceOrder(source, first, second, message) {
  const firstIndex = source.indexOf(first);
  const secondIndex = source.indexOf(second);
  assert(firstIndex >= 0, `${message}\uFF1A\u7F3A\u5C11 ${first}`);
  assert(secondIndex >= 0, `${message}\uFF1A\u7F3A\u5C11 ${second}`);
  assert(firstIndex < secondIndex, message);
}
async function main() {
  const failures = [];
  const adminAccessFailure = await runTest("Admin \u6743\u9650\u5224\u65AD\u6210\u7ACB", () => {
    const access = buildAccess(createCurrentUser({ roleNames: ["Admin"] }));
    assertEqual(access.isAdmin, true, "Admin \u5E94\u88AB\u8BC6\u522B\u4E3A\u7BA1\u7406\u5458");
  });
  if (adminAccessFailure) failures.push(adminAccessFailure);
  const warehouseAccessFailure = await runTest("WarehouseStaff \u6743\u9650\u5224\u65AD\u6210\u7ACB", () => {
    const access = buildAccess(createCurrentUser({ roleNames: ["WarehouseStaff"] }));
    assertEqual(access.isAdmin, false, "WarehouseStaff \u4E0D\u5E94\u88AB\u8BC6\u522B\u4E3A\u7BA1\u7406\u5458");
    assertEqual(access.isWarehouseStaff, true, "WarehouseStaff \u5E94\u88AB\u8BC6\u522B\u4E3A\u4ED3\u5E93\u5458\u5DE5");
  });
  if (warehouseAccessFailure) failures.push(warehouseAccessFailure);
  const productIntegritySummaryFailure = await runTest("\u5546\u54C1\u4E00\u81F4\u6027\u68C0\u67E5\u5E94\u628A\u540E\u7AEF\u5206\u7EC4\u62A5\u544A\u8F6C\u6362\u6210\u9875\u9762\u95EE\u9898\u884C", () => {
    const summary = buildProductIntegritySummary({
      checkTime: "2026-06-15T00:00:00Z",
      durationSeconds: 1.25,
      productSetCodeReport: {
        tableName: "ProductSetCode",
        totalChecked: 10,
        orphanedCount: 2,
        missingCount: 0,
        invalidKeyCount: 0,
        orphanedProductCodes: ["P001", "P002"],
        missingProductCodes: [],
        errors: []
      },
      storeReports: [
        {
          storeCode: "S1",
          storeName: "Sunnybank",
          tableReports: [
            {
              tableName: "StoreRetailPrice",
              totalChecked: 100,
              orphanedCount: 0,
              missingCount: 3,
              invalidKeyCount: 0,
              orphanedProductCodes: [],
              missingProductCodes: ["P100", "P101"],
              errors: []
            },
            {
              tableName: "StoreMultiCodeProduct",
              totalChecked: 8,
              orphanedCount: 1,
              missingCount: 4,
              invalidKeyCount: 0,
              orphanedProductCodes: ["P200"],
              missingProductCodes: ["P201", "P202"],
              errors: []
            }
          ]
        }
      ]
    });
    assertEqual(summary.storeCount, 1, "\u5E94\u7EDF\u8BA1\u68C0\u67E5\u5206\u5E97\u6570\u91CF");
    assertEqual(summary.totalChecked, 118, "\u5E94\u6C47\u603B\u603B\u90E8\u548C\u5206\u5E97\u8868\u7684\u68C0\u67E5\u8BB0\u5F55\u6570");
    assertEqual(summary.issueCount, 10, "\u95EE\u9898\u6570\u5E94\u4E3A\u5B64\u7ACB\u8BB0\u5F55\u548C\u7F3A\u5931\u8BB0\u5F55\u5408\u8BA1");
    assertEqual(summary.issueRows.length, 4, "\u5E94\u6309\u8303\u56F4\u3001\u8868\u540D\u548C\u95EE\u9898\u7C7B\u578B\u751F\u6210\u95EE\u9898\u884C");
    assert(summary.issueRows.some(
      (row) => row.scope === "\u603B\u90E8" && row.tableName === "ProductSetCode" && row.issueType === "\u5B64\u7ACB\u8BB0\u5F55" && row.count === 2 && row.sampleProductCodes.includes("P001")
    ), "ProductSetCode \u5B64\u7ACB\u8BB0\u5F55\u5E94\u751F\u6210\u603B\u90E8\u95EE\u9898\u884C");
    assert(summary.issueRows.some(
      (row) => row.scope === "Sunnybank (S1)" && row.tableName === "StoreRetailPrice" && row.issueType === "\u7F3A\u5931\u8BB0\u5F55" && row.count === 3 && row.sampleProductCodes.includes("P100")
    ), "StoreRetailPrice \u7F3A\u5931\u8BB0\u5F55\u5E94\u751F\u6210\u5206\u5E97\u95EE\u9898\u884C");
    assert(summary.issueRows.some(
      (row) => row.tableName === "StoreMultiCodeProduct" && row.issueType === "\u5B64\u7ACB\u8BB0\u5F55" && row.sampleProductCodes.includes("P200")
    ), "StoreMultiCodeProduct \u5B64\u7ACB\u8BB0\u5F55\u5E94\u4FDD\u7559\u6837\u672C\u7F16\u7801");
    assert(summary.issueRows.some(
      (row) => row.tableName === "StoreMultiCodeProduct" && row.issueType === "\u7F3A\u5931\u8BB0\u5F55" && row.sampleProductCodes.includes("P202")
    ), "StoreMultiCodeProduct \u7F3A\u5931\u8BB0\u5F55\u5E94\u4FDD\u7559\u6837\u672C\u7F16\u7801");
  });
  if (productIntegritySummaryFailure) failures.push(productIntegritySummaryFailure);
  const productIntegrityAllPassFailure = await runTest("\u5546\u54C1\u4E00\u81F4\u6027\u68C0\u67E5\u65E0\u95EE\u9898\u65F6 issueCount \u5E94\u4E3A 0", () => {
    const summary = buildProductIntegritySummary({
      checkTime: "2026-06-15T00:00:00Z",
      durationSeconds: 0.5,
      productSetCodeReport: {
        tableName: "ProductSetCode",
        totalChecked: 1,
        orphanedCount: 0,
        missingCount: 0,
        invalidKeyCount: 0,
        orphanedProductCodes: [],
        missingProductCodes: [],
        errors: []
      },
      storeReports: []
    });
    assertEqual(summary.issueCount, 0, "\u65E0\u5B64\u7ACB\u548C\u7F3A\u5931\u8BB0\u5F55\u65F6\u5E94\u89C6\u4E3A\u68C0\u67E5\u901A\u8FC7");
    assertEqual(summary.issueRows.length, 0, "\u65E0\u95EE\u9898\u65F6\u4E0D\u5E94\u751F\u6210\u8868\u683C\u884C");
  });
  if (productIntegrityAllPassFailure) failures.push(productIntegrityAllPassFailure);
  const productIntegrityInvalidKeyFailure = await runTest("\u5546\u54C1\u4E00\u81F4\u6027\u68C0\u67E5\u53EA\u6709\u65E0\u6548\u5173\u952E\u7F16\u7801\u65F6\u4E5F\u5E94\u663E\u793A\u95EE\u9898", () => {
    const summary = buildProductIntegritySummary({
      checkTime: "2026-06-15T00:00:00Z",
      durationSeconds: 0.5,
      productSetCodeReport: {
        tableName: "ProductSetCode",
        totalChecked: 1,
        orphanedCount: 0,
        missingCount: 0,
        invalidKeyCount: 3,
        orphanedProductCodes: [],
        missingProductCodes: [],
        errors: ["ProductSetCode \u5B58\u5728 3 \u6761\u7F3A\u5C11 ProductCode \u6216 SetProductCode \u7684\u8BB0\u5F55\uFF0C\u672A\u53C2\u4E0E\u81EA\u52A8\u4FEE\u590D\u3002"]
      },
      storeReports: []
    });
    assertEqual(summary.issueCount, 3, "\u65E0\u6548\u5173\u952E\u7F16\u7801\u5E94\u8BA1\u5165\u95EE\u9898\u6570");
    assertEqual(summary.issueRows.length, 1, "\u65E0\u6548\u5173\u952E\u7F16\u7801\u5E94\u751F\u6210\u95EE\u9898\u884C");
    assertEqual(summary.issueRows[0].issueType, "\u65E0\u6548\u5173\u952E\u7F16\u7801", "\u95EE\u9898\u884C\u7C7B\u578B\u5E94\u6807\u8BB0\u4E3A\u65E0\u6548\u5173\u952E\u7F16\u7801");
    assert(
      summary.issueRows[0].sampleProductCodes.some((message) => message.includes("\u7F3A\u5C11 ProductCode")),
      "\u95EE\u9898\u884C\u5E94\u4FDD\u7559\u540E\u7AEF\u9519\u8BEF\u8BF4\u660E"
    );
  });
  if (productIntegrityInvalidKeyFailure) failures.push(productIntegrityInvalidKeyFailure);
  const productIntegrityFixSummaryFailure = await runTest("\u5546\u54C1\u4E00\u81F4\u6027\u4FEE\u590D\u7ED3\u679C\u5E94\u4ECE reports \u6C47\u603B", () => {
    const summary = buildProductIntegrityFixSummary({
      fixTime: "2026-06-15T00:00:00Z",
      durationSeconds: 2,
      isDryRun: false,
      reports: [
        {
          tableName: "StoreRetailPrice",
          deletedCount: 2,
          addedCount: 3,
          errorCount: 0,
          errors: []
        },
        {
          tableName: "StoreMultiCodeProduct",
          deletedCount: 1,
          addedCount: 4,
          errorCount: 1,
          errors: ["S1 \u4FEE\u590D\u5931\u8D25"]
        }
      ]
    });
    assertEqual(summary.deletedCount, 3, "\u4FEE\u590D\u7ED3\u679C\u5E94\u6C47\u603B\u5220\u9664\u6570\u91CF");
    assertEqual(summary.addedCount, 7, "\u4FEE\u590D\u7ED3\u679C\u5E94\u6C47\u603B\u65B0\u589E\u6570\u91CF");
    assertEqual(summary.errorCount, 1, "\u4FEE\u590D\u7ED3\u679C\u5E94\u6C47\u603B\u9519\u8BEF\u6570\u91CF");
    assert(summary.errors.includes("S1 \u4FEE\u590D\u5931\u8D25"), "\u4FEE\u590D\u7ED3\u679C\u5E94\u4FDD\u7559\u9519\u8BEF\u660E\u7EC6");
  });
  if (productIntegrityFixSummaryFailure) failures.push(productIntegrityFixSummaryFailure);
  const productIntegritySourceFailure = await runTest("\u5546\u54C1\u4E00\u81F4\u6027\u68C0\u67E5\u9875\u9762\u548C\u7C7B\u578B\u5E94\u4F7F\u7528\u540E\u7AEF\u5206\u7EC4\u62A5\u544A\u7ED3\u6784", () => {
    assert(
      productIntegrityTypeSource.includes("storeReports: StoreIntegrityReport[]") && productIntegrityTypeSource.includes("productSetCodeReport?: TableIntegrityReport | null") && productIntegrityTypeSource.includes("reports: TableFixReport[]"),
      "\u5546\u54C1\u4E00\u81F4\u6027\u7C7B\u578B\u5E94\u58F0\u660E\u540E\u7AEF\u771F\u5B9E\u8FD4\u56DE\u7684\u5206\u7EC4\u62A5\u544A\u548C\u4FEE\u590D reports"
    );
    assert(
      productIntegrityHelperSource.includes("\u540E\u7AEF\u8FD4\u56DE\u7684\u662F\u6309\u5206\u5E97\u3001\u6309\u8868\u805A\u5408\u7684\u62A5\u544A") && productIntegrityHelperSource.includes("issueCount") && productIntegrityHelperSource.includes("sampleProductCodes"),
      "\u5546\u54C1\u4E00\u81F4\u6027 helper \u5E94\u628A\u540E\u7AEF\u5206\u7EC4\u62A5\u544A\u8F6C\u6362\u6210\u9875\u9762 summary \u548C\u95EE\u9898\u884C"
    );
    assert(
      pageSource.includes("buildProductIntegritySummary") && pageSource.includes("integritySummary.issueCount") && pageSource.includes("sampleProductCodes"),
      "\u5546\u54C1\u7BA1\u7406\u9875\u5E94\u901A\u8FC7 summary \u5C55\u793A\u68C0\u67E5\u7ED3\u679C"
    );
    assert(
      pageSource.includes("fixStoreRetailPrice: true") && pageSource.includes("fixStoreMultiCodeProduct: true") && pageSource.includes("fixProductSetCode: true") && pageSource.includes("buildProductIntegrityFixSummary"),
      "\u5546\u54C1\u7BA1\u7406\u9875\u81EA\u52A8\u4FEE\u590D\u5E94\u63D0\u4EA4\u540E\u7AEF\u771F\u5B9E\u5B57\u6BB5\u5E76\u6C47\u603B reports"
    );
    assert(
      !pageSource.includes("integrityResult.issues") && !pageSource.includes("integrityResult.totalProducts") && !pageSource.includes("integrityResult.passedCount") && !pageSource.includes("result.fixedCount"),
      "\u5546\u54C1\u7BA1\u7406\u9875\u4E0D\u5E94\u518D\u8BFB\u53D6\u65E7\u7248\u6241\u5E73\u5B57\u6BB5"
    );
  });
  if (productIntegritySourceFailure) failures.push(productIntegritySourceFailure);
  const adminButtonGuardFailure = await runTest("\u9875\u9762\u5E94\u4F7F\u7528 Admin \u6743\u9650\u63A7\u5236 HQ \u540C\u6B65\u6309\u94AE", () => {
    assert(
      pageSource.includes("t('posAdmin.products.fullSyncFromHQ', '\u5168\u91CF\u540C\u6B65')") && pageSource.includes("t('posAdmin.products.incrementalSyncFromHQ', '\u589E\u91CF\u540C\u6B65')"),
      "\u9875\u9762\u6E90\u7801\u4E2D\u5E94\u5B58\u5728\u201C\u5168\u91CF\u540C\u6B65\u201D\u548C\u201C\u589E\u91CF\u540C\u6B65\u201D\u4E24\u4E2A\u6309\u94AE"
    );
    assert(
      pageSource.includes("useAuthStore") && pageSource.includes("isAdmin"),
      "\u9875\u9762\u5E94\u663E\u5F0F\u8BFB\u53D6 auth store\uFF0C\u5E76\u57FA\u4E8E Admin \u6743\u9650\u51B3\u5B9A\u662F\u5426\u6E32\u67D3 HQ \u540C\u6B65\u6309\u94AE"
    );
    assert(
      pageSource.includes("ensureCanSyncProductsFromHq") && pageSource.includes("if (!ensureCanSyncProductsFromHq()) return"),
      "HQ \u540C\u6B65\u6253\u5F00\u5F39\u7A97\u548C\u786E\u8BA4\u63D0\u4EA4\u65F6\u90FD\u5E94\u6709 Admin \u6743\u9650\u5B88\u536B"
    );
  });
  if (adminButtonGuardFailure) failures.push(adminButtonGuardFailure);
  const writePermissionGuardFailure = await runTest("\u9875\u9762\u5199\u64CD\u4F5C\u5E94\u4F7F\u7528 POS \u5546\u54C1\u7BA1\u7406\u6743\u9650\u63A7\u5236", () => {
    assert(
      pageSource.includes("canManagePosProducts"),
      "\u9875\u9762\u5E94\u8BFB\u53D6 canManagePosProducts \u63A7\u5236\u5546\u54C1\u7F16\u8F91\u3001\u5206\u7C7B\u7BA1\u7406\u3001\u6279\u91CF\u548C\u540C\u6B65\u5230\u5206\u5E97\u7B49\u5199\u64CD\u4F5C"
    );
    assert(
      pageSource.includes("ensureCanManagePosProducts") && pageSource.includes("t('posAdmin.products.noManagePermission'"),
      "\u5199\u64CD\u4F5C\u5904\u7406\u51FD\u6570\u5E94\u6709\u7EDF\u4E00\u6743\u9650\u4FDD\u62A4\uFF0C\u907F\u514D\u53EA\u8BFB\u7528\u6237\u7ED5\u8FC7\u6309\u94AE\u76F4\u63A5\u89E6\u53D1"
    );
  });
  if (writePermissionGuardFailure) failures.push(writePermissionGuardFailure);
  const createProductModalFailure = await runTest("\u9875\u9762\u5E94\u63D0\u4F9B\u521B\u5EFA\u5546\u54C1\u5F39\u7A97\u5E76\u8C03\u7528 create-with-prices \u63A5\u53E3", () => {
    assert(
      typeSource.includes("CreateProductWithPricesDto") && typeSource.includes("CreateProductWithPricesResultDto") && typeSource.includes("storeProductCodes: Record<string, string>"),
      "\u5546\u54C1\u7C7B\u578B\u5B9A\u4E49\u5E94\u58F0\u660E\u521B\u5EFA\u5546\u54C1\u8BF7\u6C42\u548C\u7ED3\u679C DTO"
    );
    assert(
      serviceSource.includes("createProductWithPrices") && serviceSource.includes("`${API_BASE}/create-with-prices`"),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B createProductWithPrices \u5E76\u8C03\u7528 create-with-prices \u63A5\u53E3"
    );
    assert(
      pageSource.includes("canCreateStoreProducts") && pageSource.includes("t('posAdmin.products.createProduct', '\u521B\u5EFA\u5546\u54C1')") && pageSource.includes("openCreateModal") && pageSource.includes("handleCreateSave"),
      "\u9875\u9762\u5E94\u8BFB\u53D6 canCreateStoreProducts\uFF0C\u5E76\u63D0\u4F9B\u521B\u5EFA\u5546\u54C1\u6309\u94AE\u3001\u6253\u5F00\u5F39\u7A97\u548C\u4FDD\u5B58\u5904\u7406\u51FD\u6570"
    );
    assert(
      pageSource.includes("ensureCanCreateStoreProducts") && pageSource.includes("t('posAdmin.products.noCreatePermission'"),
      "\u521B\u5EFA\u5546\u54C1\u5165\u53E3\u548C\u63D0\u4EA4\u90FD\u5E94\u6709\u5355\u72EC\u7684\u521B\u5EFA\u6743\u9650\u5B88\u536B"
    );
    assert(
      pageSource.includes("const [createVisible, setCreateVisible] = useState(false)") && pageSource.includes("const [createSubmitting, setCreateSubmitting] = useState(false)") && pageSource.includes("const [createForm] = Form.useForm()"),
      "\u9875\u9762\u5E94\u7EF4\u62A4\u521B\u5EFA\u5546\u54C1\u5F39\u7A97\u3001\u63D0\u4EA4\u72B6\u6001\u548C\u72EC\u7ACB\u8868\u5355\u5B9E\u4F8B"
    );
    assert(
      pageSource.includes("await createProductWithPrices({") && pageSource.includes("productType: 0") && pageSource.includes("isActive: true"),
      "\u521B\u5EFA\u5546\u54C1\u63D0\u4EA4\u5E94\u56FA\u5B9A\u666E\u901A\u5546\u54C1\uFF0C\u5E76\u9ED8\u8BA4\u542F\u7528"
    );
    assert(
      pageSource.includes("Object.keys(result.storeProductCodes ?? {}).length") && pageSource.includes("result.productCode") && pageSource.includes("t('posAdmin.products.createProductSuccess'"),
      "\u521B\u5EFA\u6210\u529F\u63D0\u793A\u5E94\u5C55\u793A productCode \u548C\u5DF2\u521B\u5EFA\u5206\u5E97\u5546\u54C1\u6570\u91CF"
    );
    assert(
      pageSource.includes("setCreateVisible(false)") && pageSource.includes("createForm.resetFields()") && pageSource.includes("void loadData()"),
      "\u521B\u5EFA\u6210\u529F\u540E\u5E94\u5173\u95ED\u5F39\u7A97\u3001\u6E05\u7A7A\u8868\u5355\u5E76\u5237\u65B0\u5217\u8868"
    );
    assert(
      pageSource.includes("title={t('posAdmin.products.createProduct', '\u521B\u5EFA\u5546\u54C1')}") && pageSource.includes("open={createVisible}") && pageSource.includes("confirmLoading={createSubmitting}") && pageSource.includes('name="productName"') && pageSource.includes('name="productImage"') && pageSource.includes('name="barcode"') && pageSource.includes('name="localSupplierCode"') && pageSource.includes('name="purchasePrice"') && pageSource.includes('name="retailPrice"'),
      "\u521B\u5EFA\u5546\u54C1\u5F39\u7A97\u5E94\u5305\u542B\u57FA\u7840\u5B57\u6BB5\u5E76\u7ED1\u5B9A\u63D0\u4EA4\u72B6\u6001"
    );
    assert(
      !pageSource.includes("t('posAdmin.products.setProduct', '\u5957\u88C5\u5546\u54C1')") || pageSource.includes('name="productType"'),
      "\u521B\u5EFA\u5546\u54C1\u5F39\u7A97\u8303\u56F4\u5185\u4E0D\u5E94\u652F\u6301\u5957\u88C5/\u591A\u7801\u5207\u6362"
    );
  });
  if (createProductModalFailure) failures.push(createProductModalFailure);
  const editSetCodesIsolationFailure = await runTest("\u5546\u54C1\u7F16\u8F91\u5F39\u7A97\u5E94\u9694\u79BB\u5957\u88C5\u548C\u591A\u7801\u660E\u7EC6\u72B6\u6001", () => {
    assert(
      pageSource.includes("resetEditSetCodeState") && pageSource.includes("setEditSetCodes([])") && pageSource.includes("setEditSetPriceEdits({})") && pageSource.includes("setEditPendingDeletes({})"),
      "\u9875\u9762\u5E94\u63D0\u4F9B\u7EDF\u4E00\u91CD\u7F6E\u51FD\u6570\uFF0C\u6E05\u7A7A\u7F16\u8F91\u5F39\u7A97\u6761\u7801\u660E\u7EC6\u3001\u7F16\u8F91\u7F13\u5B58\u548C\u5F85\u5220\u7F13\u5B58"
    );
    const openEditStart = pageSource.indexOf("const openEdit = (record: PosProductDto) => {");
    const openEditEnd = pageSource.indexOf("const openStoreRecords", openEditStart);
    assert(openEditStart >= 0 && openEditEnd > openEditStart, "\u9875\u9762\u5E94\u4FDD\u7559 openEdit \u7F16\u8F91\u5165\u53E3");
    const openEditSource = pageSource.slice(openEditStart, openEditEnd);
    assertSourceOrder(
      openEditSource,
      "resetEditSetCodeState()",
      "setEditingProduct(record)",
      "\u6253\u5F00\u65B0\u5546\u54C1\u7F16\u8F91\u5F39\u7A97\u524D\u5E94\u5148\u6E05\u7A7A\u4E0A\u4E00\u4E2A\u5546\u54C1\u7684\u6761\u7801\u660E\u7EC6\u72B6\u6001"
    );
    const effectStart = pageSource.indexOf("useEffect(() => {\n    const requestSeq = editSetCodesRequestSeqRef.current + 1");
    const effectEnd = pageSource.indexOf("const handleProductTypeChange", effectStart);
    assert(effectStart >= 0 && effectEnd > effectStart, "\u52A0\u8F7D\u7F16\u8F91\u5F39\u7A97\u6761\u7801\u660E\u7EC6\u7684 effect \u5E94\u4F7F\u7528\u8BF7\u6C42\u5E8F\u53F7\u4FDD\u62A4");
    const effectSource = pageSource.slice(effectStart, effectEnd);
    assert(
      pageSource.includes("const editSetCodesRequestSeqRef = useRef(0)"),
      "\u9875\u9762\u5E94\u4F7F\u7528 ref \u4FDD\u5B58\u6761\u7801\u660E\u7EC6\u8BF7\u6C42\u5E8F\u53F7\uFF0C\u907F\u514D\u65E7\u8BF7\u6C42\u8986\u76D6\u65B0\u5546\u54C1"
    );
    assert(
      pageSource.includes("const editingProductCode = editingProduct?.productCode"),
      "\u591A\u7801/\u5957\u88C5\u660E\u7EC6\u52A0\u8F7D\u4F9D\u8D56\u5E94\u7ED1\u5B9A\u5F53\u524D\u7F16\u8F91\u5546\u54C1 productCode"
    );
    assert(
      effectSource.includes("getGridData({ productCode: editingProductCode, pageIndex: 1, pageSize: 200 })"),
      "\u6761\u7801\u660E\u7EC6\u52A0\u8F7D\u5E94\u4F7F\u7528\u5F53\u524D\u7F16\u8F91\u5546\u54C1 productCode"
    );
    assert(
      effectSource.includes("if (requestSeq === editSetCodesRequestSeqRef.current)") && effectSource.includes("setEditSetCodes(items)") && effectSource.includes("setEditSetCodesLoading(false)"),
      "\u6761\u7801\u660E\u7EC6\u8BF7\u6C42\u8FD4\u56DE\u548C loading \u6536\u5C3E\u90FD\u5E94\u5148\u6821\u9A8C\u8BF7\u6C42\u5E8F\u53F7"
    );
    assert(
      pageSource.includes("}, [editVisible, productTypeWatch, editingProductCode, resetEditSetCodeState, t])"),
      "\u6761\u7801\u660E\u7EC6\u52A0\u8F7D effect \u4F9D\u8D56\u5E94\u5305\u542B\u5F53\u524D\u5546\u54C1 productCode\u3001\u7C7B\u578B\u548C\u91CD\u7F6E\u51FD\u6570"
    );
    assert(
      effectSource.includes("resetEditSetCodeState()") && effectSource.includes("setEditSetCodesLoading(false)"),
      "\u975E\u5957\u88C5/\u975E\u591A\u7801\u72B6\u6001\u5E94\u6E05\u7A7A\u6761\u7801\u660E\u7EC6\u5E76\u9000\u51FA loading"
    );
  });
  if (editSetCodesIsolationFailure) failures.push(editSetCodesIsolationFailure);
  const editProductImageFailure = await runTest("\u5546\u54C1\u7F16\u8F91\u5F39\u7A97\u5E94\u663E\u793A\u5E76\u4FDD\u7559\u5546\u54C1\u56FE\u7247\u5B57\u6BB5", () => {
    const openEditStart = pageSource.indexOf("const openEdit = (record: PosProductDto) => {");
    const openEditEnd = pageSource.indexOf("const openStoreRecords", openEditStart);
    assert(openEditStart >= 0 && openEditEnd > openEditStart, "\u9875\u9762\u5E94\u4FDD\u7559 openEdit \u7F16\u8F91\u5165\u53E3");
    const openEditSource = pageSource.slice(openEditStart, openEditEnd);
    assert(
      openEditSource.includes("productImage: record.productImage || buildDefaultProductImageUrl(record.itemNumber || record.productCode)"),
      "\u6253\u5F00\u7F16\u8F91\u5F39\u7A97\u65F6\u5E94\u4F18\u5148\u56DE\u586B\u5546\u54C1\u56FE\u7247 URL\uFF0C\u65E0\u56FE\u65F6\u6309\u8D27\u53F7\u751F\u6210\u9ED8\u8BA4\u56FE\u7247 URL"
    );
    assert(
      pageSource.includes("const DEFAULT_PRODUCT_IMAGE_BASE_URL = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200'") && pageSource.includes("function buildDefaultProductImageUrl") && pageSource.includes("`${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(normalizedItemNumber)}.jpg`"),
      "\u5546\u54C1\u56FE\u7247\u9ED8\u8BA4 URL \u5E94\u4F7F\u7528 YW200 COS \u5730\u5740\u5E76\u6309\u8D27\u53F7\u751F\u6210 jpg"
    );
    const handleEditSaveStart = pageSource.indexOf("const handleEditSave = async () => {");
    const handleEditSaveEnd = pageSource.indexOf("const handleBatchEnable", handleEditSaveStart);
    assert(handleEditSaveStart >= 0 && handleEditSaveEnd > handleEditSaveStart, "\u9875\u9762\u5E94\u4FDD\u7559 handleEditSave \u4FDD\u5B58\u5165\u53E3");
    const handleEditSaveSource = pageSource.slice(handleEditSaveStart, handleEditSaveEnd);
    assert(
      handleEditSaveSource.includes("productImage: values.productImage ?? editingProduct.productImage ?? ''"),
      "\u5546\u54C1\u4FDD\u5B58 payload \u5E94\u663E\u5F0F\u5E26\u56DE productImage\uFF0C\u907F\u514D\u6DFB\u52A0\u591A\u7801\u540E\u8986\u76D6\u6E05\u7A7A\u56FE\u7247\u5B57\u6BB5"
    );
    const editModalStart = pageSource.indexOf("<Modal\n        open={editVisible}");
    const editModalEnd = pageSource.indexOf("{productTypeWatch === 1 &&", editModalStart);
    assert(editModalStart >= 0 && editModalEnd > editModalStart, "\u9875\u9762\u5E94\u4FDD\u7559\u7F16\u8F91\u5546\u54C1\u5F39\u7A97\u8868\u5355");
    const editModalSource = pageSource.slice(editModalStart, editModalEnd);
    assert(
      editModalSource.includes('name="productImage"') && editModalSource.includes("pos-products-edit-image-preview") && editModalSource.includes("prev.productImage !== cur.productImage"),
      "\u7F16\u8F91\u5F39\u7A97\u5E94\u63D0\u4F9B\u5546\u54C1\u56FE\u7247 URL \u8F93\u5165\u548C\u968F\u8868\u5355\u503C\u53D8\u5316\u7684\u56FE\u7247\u9884\u89C8"
    );
  });
  if (editProductImageFailure) failures.push(editProductImageFailure);
  const editSupplierFailure = await runTest("\u5546\u54C1\u7F16\u8F91\u5F39\u7A97\u4F9B\u5E94\u5546\u5E94\u5141\u8BB8\u4FEE\u6539\u5E76\u968F\u4FDD\u5B58\u63D0\u4EA4", () => {
    const handleEditSaveStart = pageSource.indexOf("const handleEditSave = async () => {");
    const handleEditSaveEnd = pageSource.indexOf("const handleBatchEnable", handleEditSaveStart);
    assert(handleEditSaveStart >= 0 && handleEditSaveEnd > handleEditSaveStart, "\u9875\u9762\u5E94\u4FDD\u7559 handleEditSave \u4FDD\u5B58\u5165\u53E3");
    const handleEditSaveSource = pageSource.slice(handleEditSaveStart, handleEditSaveEnd);
    assert(
      handleEditSaveSource.includes("localSupplierCode: values.localSupplierCode"),
      "\u5546\u54C1\u4FDD\u5B58 payload \u5E94\u63D0\u4EA4\u7F16\u8F91\u8868\u5355\u4E2D\u7684\u4F9B\u5E94\u5546\u7F16\u7801"
    );
    const editModalStart = pageSource.indexOf("<Modal\n        open={editVisible}");
    const editModalEnd = pageSource.indexOf("{productTypeWatch === 1 &&", editModalStart);
    assert(editModalStart >= 0 && editModalEnd > editModalStart, "\u9875\u9762\u5E94\u4FDD\u7559\u7F16\u8F91\u5546\u54C1\u5F39\u7A97\u8868\u5355");
    const editModalSource = pageSource.slice(editModalStart, editModalEnd);
    const supplierFieldStart = editModalSource.indexOf('name="localSupplierCode"');
    const supplierFieldEnd = editModalSource.indexOf('name="productType"', supplierFieldStart);
    assert(supplierFieldStart >= 0 && supplierFieldEnd > supplierFieldStart, "\u7F16\u8F91\u5F39\u7A97\u5E94\u4FDD\u7559\u4F9B\u5E94\u5546\u4E0B\u62C9\u8868\u5355\u9879");
    const supplierFieldSource = editModalSource.slice(supplierFieldStart, supplierFieldEnd);
    assert(
      supplierFieldSource.includes("options={supplierOptions}") && supplierFieldSource.includes("placeholder={t('posAdmin.products.selectSupplier', '\u8BF7\u9009\u62E9\u4F9B\u5E94\u5546')}") && !supplierFieldSource.includes("disabled"),
      "\u7F16\u8F91\u5F39\u7A97\u4F9B\u5E94\u5546\u4E0B\u62C9\u5E94\u6253\u5F00\u7F16\u8F91\uFF0C\u4E0D\u80FD\u7981\u7528"
    );
  });
  if (editSupplierFailure) failures.push(editSupplierFailure);
  const productTypeColumnFailure = await runTest("\u5546\u54C1\u5217\u8868\u5E94\u663E\u793A\u5546\u54C1\u7C7B\u578B\u5E76\u6309\u7C7B\u578B\u663E\u793A\u6761\u7801\u8BB0\u5F55\u6570\u91CF", () => {
    assert(
      pageSource.includes("function normalizeProductType(productType: unknown): 0 | 1 | 2") && pageSource.includes("function getProductTypeColor(productType: unknown): string") && pageSource.includes("const getProductTypeLabel = useCallback((productType: unknown) => {"),
      "\u9875\u9762\u5E94\u96C6\u4E2D\u5B9A\u4E49\u5546\u54C1\u7C7B\u578B\u5F52\u4E00\u3001\u989C\u8272\u548C\u6587\u6848\u51FD\u6570"
    );
    assert(
      pageSource.includes("if (normalizedType === 1) return t('posAdmin.products.setProduct', '\u5957\u88C5')") && pageSource.includes("if (normalizedType === 2) return t('posAdmin.products.multiCodeProductShort', '\u591A\u7801')") && pageSource.includes("return t('posAdmin.products.normalProduct', '\u666E\u901A')"),
      "\u5546\u54C1\u7C7B\u578B\u6587\u6848\u5E94\u8986\u76D6\u666E\u901A\u3001\u5957\u88C5\u548C\u591A\u7801"
    );
    const productTypeColumnStart = pageSource.indexOf("title: t('posAdmin.products.productTypeLabel', '\u5546\u54C1\u7C7B\u578B')");
    const barcodeRecordColumnStart = pageSource.indexOf("title: t('posAdmin.products.barcodeRecordCount', '\u6761\u7801\u8BB0\u5F55')");
    const storeRecordColumnStart = pageSource.indexOf("title: t('posAdmin.products.storeRecords', '\u5206\u5E97\u8BB0\u5F55')");
    assert(productTypeColumnStart >= 0, "\u8868\u683C\u5E94\u5B58\u5728\u5546\u54C1\u7C7B\u578B\u5217");
    assert(barcodeRecordColumnStart > productTypeColumnStart, "\u6761\u7801\u8BB0\u5F55\u5217\u5E94\u4F4D\u4E8E\u5546\u54C1\u7C7B\u578B\u5217\u4E4B\u540E");
    assert(storeRecordColumnStart > barcodeRecordColumnStart, "\u5206\u5E97\u8BB0\u5F55\u5217\u5E94\u4F4D\u4E8E\u6761\u7801\u8BB0\u5F55\u5217\u4E4B\u540E");
    const productTypeColumnSource = pageSource.slice(productTypeColumnStart, barcodeRecordColumnStart);
    assert(
      productTypeColumnSource.includes("dataIndex: 'productType'") && productTypeColumnSource.includes("getProductTypeColor(v)") && productTypeColumnSource.includes("getProductTypeLabel(v)"),
      "\u5546\u54C1\u7C7B\u578B\u5217\u5E94\u8BFB\u53D6 productType \u5E76\u663E\u793A\u7C7B\u578B Tag"
    );
    const barcodeRecordColumnSource = pageSource.slice(barcodeRecordColumnStart, storeRecordColumnStart);
    assert(
      barcodeRecordColumnSource.includes("dataIndex: 'setCount'") && barcodeRecordColumnSource.includes("if (!isBarcodeManagedProduct(record.productType))") && barcodeRecordColumnSource.includes("return <span>0</span>") && barcodeRecordColumnSource.includes("openSetCodeManager(record)"),
      "\u6761\u7801\u8BB0\u5F55\u5217\u5E94\u8BFB\u53D6 setCount\uFF0C\u591A\u7801/\u5957\u88C5\u6309 productType \u5224\u65AD\uFF0C\u666E\u901A\u5546\u54C1\u663E\u793A 0"
    );
    assert(
      !barcodeRecordColumnSource.includes("dataIndex: 'isSet'") && !barcodeRecordColumnSource.includes("record.isSet"),
      "\u6761\u7801\u8BB0\u5F55\u5217\u4E0D\u80FD\u518D\u4F9D\u8D56 isSet\uFF0C\u5426\u5219\u591A\u7801\u5546\u54C1\u6570\u91CF\u4F1A\u6F0F\u663E\u793A"
    );
  });
  if (productTypeColumnFailure) failures.push(productTypeColumnFailure);
  const productTypeActionFailure = await runTest("\u5546\u54C1\u5217\u8868\u64CD\u4F5C\u5217\u5E94\u5141\u8BB8\u5957\u88C5\u548C\u591A\u7801\u8FDB\u5165\u6761\u7801\u7BA1\u7406", () => {
    const actionColumnStart = pageSource.indexOf("key: 'actions'");
    const columnsEnd = pageSource.indexOf("  ]", actionColumnStart);
    assert(actionColumnStart >= 0 && columnsEnd > actionColumnStart, "\u9875\u9762\u5E94\u4FDD\u7559\u64CD\u4F5C\u5217");
    const actionColumnSource = pageSource.slice(actionColumnStart, columnsEnd);
    assert(
      actionColumnSource.includes("isBarcodeManagedProduct(record.productType)") && actionColumnSource.includes("normalizeProductType(record.productType) === 2") && actionColumnSource.includes("t('posAdmin.products.multiBarcodeManagement', '\u591A\u7801\u7BA1\u7406')") && actionColumnSource.includes("t('posAdmin.products.setManagement', '\u5957\u88C5\u7BA1\u7406')"),
      "\u64CD\u4F5C\u5217\u5E94\u6309 productType \u5141\u8BB8\u5957\u88C5\u548C\u591A\u7801\u8FDB\u5165\u6761\u7801\u7BA1\u7406\uFF0C\u5E76\u6309\u7C7B\u578B\u663E\u793A\u6309\u94AE\u6587\u6848"
    );
    assert(
      !actionColumnSource.includes("record.isSet &&"),
      "\u64CD\u4F5C\u5217\u4E0D\u80FD\u7EE7\u7EED\u53EA\u6309 isSet \u5C55\u793A\u6761\u7801\u7BA1\u7406\u5165\u53E3"
    );
  });
  if (productTypeActionFailure) failures.push(productTypeActionFailure);
  const categoryParentValueFailure = await runTest("\u5546\u54C1\u5206\u7C7B\u7236\u7EA7 Cascader \u5E94\u53EA\u63D0\u4EA4\u53F6\u5B50 GUID", () => {
    assert(
      pageSource.includes("resolveCascaderLeafValue") && pageSource.includes("const parentGuid = resolveCascaderLeafValue(values.parentGuid)"),
      "\u5206\u7C7B\u7236\u7EA7\u4FDD\u5B58\u524D\u5E94\u628A Cascader \u8DEF\u5F84\u8F6C\u6362\u4E3A\u6700\u540E\u4E00\u7EA7 GUID"
    );
    assert(
      pageSource.includes("parentGuid: getCategoryValueFromGuid(node.parentGuid, categoryTree)"),
      "\u7F16\u8F91\u5206\u7C7B\u65F6\u5E94\u4F7F\u7528\u5B8C\u6574\u8DEF\u5F84\u56DE\u586B\u7236\u5206\u7C7B Cascader"
    );
    assert(
      pageSource.includes("categoryParentDisabledGuids.has(parentGuid)") && pageSource.includes("invalidParentCategory"),
      "\u4FDD\u5B58\u5206\u7C7B\u65F6\u5E94\u518D\u6B21\u6821\u9A8C\u7236\u7EA7\u4E0D\u80FD\u662F\u81EA\u8EAB\u6216\u5B50\u5206\u7C7B\uFF0C\u907F\u514D\u7ED5\u8FC7 UI \u7981\u7528"
    );
  });
  if (categoryParentValueFailure) failures.push(categoryParentValueFailure);
  const syncFieldsRequestFailure = await runTest("\u540C\u6B65\u5230\u5206\u5E97\u5E94\u6539\u4E3A\u540E\u53F0 job \u63D0\u4EA4\u5E76\u8F6E\u8BE2\u7ED3\u679C", () => {
    assert(
      typeSource.includes("SyncProductsToStoresField") && typeSource.includes("fields: SyncProductsToStoresField[]"),
      "SyncProductsToStoresRequest \u5E94\u58F0\u660E\u540C\u6B65\u5B57\u6BB5\u5217\u8868"
    );
    assert(
      typeSource.includes("SyncProductsToStoresJobResult") && typeSource.includes("jobId: string") && typeSource.includes("status: SyncProductsToStoresJobStatus") && typeSource.includes("operationId?: string") && typeSource.includes("isDuplicateRequest?: boolean"),
      "\u7C7B\u578B\u5C42\u5E94\u58F0\u660E\u540C\u6B65\u5230\u5206\u5E97\u540E\u53F0 job \u7ED3\u679C\u3001\u72B6\u6001\u548C\u91CD\u590D\u63D0\u4EA4\u6807\u8BB0"
    );
    assert(
      serviceSource.includes("startSyncProductsToStoresJob") && serviceSource.includes("`${API_BASE}/sync-to-stores/jobs`") && serviceSource.includes("getSyncProductsToStoresJob") && serviceSource.includes("`${API_BASE}/sync-to-stores/jobs/${encodeURIComponent(jobId)}`"),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B\u540C\u6B65\u5230\u5206\u5E97 job \u7684\u521B\u5EFA\u4E0E\u67E5\u8BE2\u63A5\u53E3"
    );
    assert(
      pageSource.includes("buildSyncProductsToStoresFields(values)") && pageSource.includes("fields: syncFields") && pageSource.includes("selectSyncFields"),
      "\u540C\u6B65\u5230\u5206\u5E97\u5E94\u6839\u636E\u590D\u9009\u6846\u6784\u9020 fields\uFF0C\u5E76\u6821\u9A8C\u81F3\u5C11\u9009\u62E9\u4E00\u4E2A\u5B57\u6BB5"
    );
    assert(
      pageSource.includes("startSyncProductsToStoresJob(req)") && pageSource.includes("createHqSyncJobPoller<SyncProductsToStoresJobResult>") && pageSource.includes("getSyncProductsToStoresJob(jobId)"),
      "\u9875\u9762\u63D0\u4EA4\u540C\u6B65\u5230\u5206\u5E97\u540E\u5E94\u521B\u5EFA\u540E\u53F0 job\uFF0C\u5E76\u4F7F\u7528\u5171\u4EAB\u8F6E\u8BE2\u5668\u67E5\u8BE2\u4EFB\u52A1\u72B6\u6001"
    );
    assert(
      pageSource.includes("setSyncToStoreVisible(false)") && pageSource.includes("t('posAdmin.products.syncToStoreJobSubmitted', '\u540C\u6B65\u4EFB\u52A1\u5DF2\u63D0\u4EA4\uFF0C\u6B63\u5728\u540E\u53F0\u6267\u884C\u3002\u5B8C\u6210\u540E\u4F1A\u81EA\u52A8\u63D0\u793A\u7ED3\u679C\u3002')"),
      "\u540C\u6B65\u5230\u5206\u5E97 job \u521B\u5EFA\u6210\u529F\u540E\u5E94\u7ACB\u5373\u5173\u95ED\u5F39\u7A97\u5E76\u63D0\u793A\u540E\u53F0\u6267\u884C"
    );
    assert(
      pageSource.includes("createdCount") && pageSource.includes("updatedCount") && pageSource.includes("failedCount") && pageSource.includes("result.errors") && pageSource.includes("job.message"),
      "\u540C\u6B65\u5230\u5206\u5E97\u6700\u7EC8\u63D0\u793A\u5E94\u8BFB\u53D6 job.result \u7684\u521B\u5EFA\u3001\u66F4\u65B0\u3001\u5931\u8D25\u548C\u9519\u8BEF\u660E\u7EC6\uFF0C\u4EE5\u53CA\u540E\u7AEF message"
    );
    assert(
      pageSource.includes("error instanceof HqProductSyncPollingTimeoutError") && pageSource.includes("t('posAdmin.products.syncToStoreJobTimeout', '\u540E\u53F0\u4ECD\u5728\u6267\u884C\uFF0C\u8BF7\u7A0D\u540E\u5237\u65B0\u67E5\u770B')"),
      "\u540C\u6B65\u5230\u5206\u5E97\u8F6E\u8BE2\u8D85\u65F6\u65F6\u5E94\u63D0\u793A\u540E\u53F0\u4ECD\u5728\u6267\u884C\uFF0C\u800C\u4E0D\u662F\u8BEF\u62A5\u540C\u6B65\u5B8C\u6210"
    );
    assert(
      pageSource.includes("consecutivePollingFailures") && pageSource.includes("error instanceof RequestError") && pageSource.includes("error.status === 404") && pageSource.includes("error.status === 401") && pageSource.includes("error.status === 403") && pageSource.includes("syncToStoreJobMissingTitle") && pageSource.includes("syncToStoreJobAuthFailedTitle") && pageSource.includes("syncToStoreJobPollingStoppedTitle"),
      "\u540C\u6B65\u5230\u5206\u5E97 job \u67E5\u8BE2\u5931\u8D25\u4E0D\u5E94\u5168\u90E8\u4F2A\u88C5\u6210 Running\uFF0C404/\u6743\u9650/\u8FDE\u7EED\u5931\u8D25\u90FD\u8981\u505C\u6B62\u8F6E\u8BE2\u5E76\u63D0\u793A\u7528\u6237"
    );
    assert(
      !pageSource.includes("await syncProductsToStores(req)") && !pageSource.includes("t('posAdmin.products.syncToStoreComplete', '\u540C\u6B65\u5B8C\u6210\uFF1A\u6210\u529F {{success}}\uFF0C\u5931\u8D25 {{failed}}'"),
      "\u9875\u9762\u4E0D\u5E94\u7EE7\u7EED\u76F4\u8C03\u540C\u6B65\u63A5\u53E3\u6216\u5C55\u793A\u6210\u529F 0/\u5931\u8D25 0 \u7684\u65E7\u63D0\u793A"
    );
  });
  if (syncFieldsRequestFailure) failures.push(syncFieldsRequestFailure);
  const syncToStoreResultGuardFailure = await runTest("\u540C\u6B65\u5230\u5206\u5E97 job \u7ED3\u679C\u5C55\u793A\u5E94\u533A\u5206\u5931\u8D25\u3001\u90E8\u5206\u6210\u529F\u548C\u6210\u529F", () => {
    const showResultStart = pageSource.indexOf("function showSyncToStoreJobResult(job: SyncProductsToStoresJobResult)");
    const showResultEnd = pageSource.indexOf("const ensureCanSyncProductsFromHq", showResultStart);
    assert(showResultStart >= 0 && showResultEnd > showResultStart, "\u9875\u9762\u5E94\u4FDD\u7559 showSyncToStoreJobResult \u7ED3\u679C\u5C55\u793A\u51FD\u6570");
    const showResultSource = pageSource.slice(showResultStart, showResultEnd);
    assertSourceOrder(
      showResultSource,
      "if (job.status === 'Failed')",
      "if (errors.length || (result.failedCount ?? 0) > 0)",
      "job.status \u4E3A Failed \u65F6\u5E94\u5148\u8FDB\u5165\u5931\u8D25\u5206\u652F\uFF0C\u4E0D\u80FD\u88AB failedCount/errors \u8BEF\u5224\u4E3A\u90E8\u5206\u6210\u529F"
    );
    assert(
      showResultSource.includes("Modal.error({\n        title: t('posAdmin.products.syncToStoreFailed', '\u540C\u6B65\u5230\u5206\u5E97\u5931\u8D25')") && showResultSource.includes("Modal.warning({\n        title: t('posAdmin.products.syncToStorePartialSucceeded', '\u540C\u6B65\u5230\u5206\u5E97\u90E8\u5206\u6210\u529F')") && showResultSource.includes("Modal.success({\n      title: t('posAdmin.products.syncToStoreSucceeded', '\u540C\u6B65\u5230\u5206\u5E97\u5B8C\u6210')"),
      "\u540C\u6B65\u5230\u5206\u5E97 job \u7ED3\u679C\u5E94\u5206\u522B\u4F7F\u7528\u5931\u8D25\u3001\u90E8\u5206\u6210\u529F warning \u548C\u6210\u529F\u5F39\u7A97"
    );
    assert(
      showResultSource.includes("const errors = result.errors ?? job.errors ?? []") && showResultSource.includes("errors.length || (result.failedCount ?? 0) > 0"),
      "status Succeeded \u4F46\u5B58\u5728 failedCount/errors \u65F6\u5E94\u663E\u793A\u90E8\u5206\u6210\u529F warning"
    );
    const failedBranchStart = showResultSource.indexOf("if (job.status === 'Failed')");
    const failedBranchReturn = showResultSource.indexOf("      return", failedBranchStart);
    const partialBranchStart = showResultSource.indexOf("if (errors.length || (result.failedCount ?? 0) > 0)", failedBranchStart);
    const failedBranch = showResultSource.slice(failedBranchStart, failedBranchReturn);
    assert(
      failedBranch.includes("t('posAdmin.products.syncToStoreFailed', '\u540C\u6B65\u5230\u5206\u5E97\u5931\u8D25')") && !failedBranch.includes("setSelectedRowKeys([])") && !failedBranch.includes("void loadData()") && failedBranchReturn < partialBranchStart,
      "Failed \u5206\u652F\u53EA\u5C55\u793A\u5931\u8D25\u7ED3\u679C\u5E76\u7ACB\u5373\u8FD4\u56DE\uFF0C\u4E0D\u5E94\u6E05\u7A7A\u9009\u62E9\u6216\u5237\u65B0\u6210\u90E8\u5206\u6210\u529F\u8DEF\u5F84"
    );
  });
  if (syncToStoreResultGuardFailure) failures.push(syncToStoreResultGuardFailure);
  const syncToStorePollingGuardFailure = await runTest("\u540C\u6B65\u5230\u5206\u5E97 job \u8F6E\u8BE2\u5F02\u5E38\u5E94\u505C\u6B62\u5E76\u7ED9\u51FA\u660E\u786E\u63D0\u793A", () => {
    assertSourceOrder(
      pageSource,
      "if (error instanceof RequestError && (error.status === 404 || error.status === 401 || error.status === 403))",
      "consecutivePollingFailures += 1",
      "404/401/403 \u5E94\u7ACB\u5373\u629B\u51FA\u505C\u6B62\u8F6E\u8BE2\uFF0C\u4E0D\u80FD\u8BA1\u5165\u666E\u901A\u8FDE\u7EED\u5931\u8D25\u540E\u7EE7\u7EED\u4F2A\u88C5 Running"
    );
    assert(
      pageSource.includes("consecutivePollingFailures >= 3") && pageSource.includes("throw error") && pageSource.includes("title: t('posAdmin.products.syncToStoreJobPollingStoppedTitle', '\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u72B6\u6001\u83B7\u53D6\u5931\u8D25')"),
      "\u8FDE\u7EED\u67E5\u8BE2\u5931\u8D25\u8FBE\u5230\u9608\u503C\u540E\u5E94\u505C\u6B62\u8F6E\u8BE2\uFF0C\u5E76\u7528 warning \u544A\u77E5\u7528\u6237\u5237\u65B0\u786E\u8BA4"
    );
    assert(
      pageSource.includes("title: t('posAdmin.products.syncToStoreJobMissingTitle', '\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u4E0D\u5B58\u5728')") && pageSource.includes("title: t('posAdmin.products.syncToStoreJobAuthFailedTitle', '\u65E0\u6CD5\u67E5\u8BE2\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1')"),
      "\u540C\u6B65\u5230\u5206\u5E97 job \u8F6E\u8BE2 404 \u548C 401/403 \u5E94\u5206\u522B\u7ED9\u51FA\u4EFB\u52A1\u7F3A\u5931\u4E0E\u6743\u9650\u5931\u8D25\u63D0\u793A"
    );
  });
  if (syncToStorePollingGuardFailure) failures.push(syncToStorePollingGuardFailure);
  const storeRecordsFailure = await runTest("\u5546\u54C1\u7BA1\u7406\u5E94\u663E\u793A\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u5E76\u70B9\u51FB\u67E5\u770B\u5206\u5E97\u8BB0\u5F55\u660E\u7EC6", () => {
    assert(
      typeSource.includes("storeRecordCount?: number") && typeSource.includes("ProductStoreRecordDto"),
      "\u524D\u7AEF\u5546\u54C1\u7C7B\u578B\u5E94\u5305\u542B\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u548C\u5206\u5E97\u8BB0\u5F55\u660E\u7EC6 DTO"
    );
    assert(
      serviceSource.includes("getProductStoreRecords") && serviceSource.includes("/store-records"),
      "\u5546\u54C1\u670D\u52A1\u5E94\u63D0\u4F9B\u6309\u5546\u54C1\u7F16\u7801\u8BFB\u53D6\u5206\u5E97\u8BB0\u5F55\u660E\u7EC6\u7684\u63A5\u53E3"
    );
    assert(
      pageSource.includes("storeRecordCount") && pageSource.includes("openStoreRecords") && pageSource.includes("storeRecordsRequestSeqRef") && pageSource.includes("requestSeq === storeRecordsRequestSeqRef.current") && pageSource.includes("storeRecordsVisible") && pageSource.includes("storeRecordsLoading") && pageSource.includes("canManageStoreProducts") && pageSource.includes("count > 0 && canManageStoreProducts") && pageSource.includes("getProductStoreRecords(record.productCode)") && pageSource.includes("dataIndex: 'storeName'") && pageSource.includes("sorter: compareProductStoreRecordsByName"),
      "\u5546\u54C1\u7BA1\u7406\u9875\u9762\u5E94\u65B0\u589E\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u5217\u3001\u70B9\u51FB\u5904\u7406\u3001\u52A0\u8F7D\u72B6\u6001\u3001\u8BF7\u6C42\u7ADE\u6001\u4FDD\u62A4\u3001\u5206\u5E97\u540D\u79F0\u6392\u5E8F\u5668\uFF0C\u5E76\u4EC5\u5141\u8BB8\u6709\u5206\u5E97\u5546\u54C1\u6743\u9650\u65F6\u70B9\u51FB"
    );
    assert(
      pageSource.includes("t('posAdmin.products.storeRecords', '\u5206\u5E97\u8BB0\u5F55')") && pageSource.includes("t('posAdmin.products.noStoreRecords', '\u6682\u65E0\u5206\u5E97\u8BB0\u5F55')") && pageSource.includes("t('posAdmin.products.loadStoreRecordsFailed', '\u52A0\u8F7D\u5206\u5E97\u8BB0\u5F55\u5931\u8D25')"),
      "\u5206\u5E97\u8BB0\u5F55\u5217\u548C\u5F39\u7A97\u5E94\u6709\u4E2D\u6587\u515C\u5E95\u6587\u6848"
    );
  });
  if (storeRecordsFailure) failures.push(storeRecordsFailure);
  const storeRecordFiltersFailure = await runTest("\u5546\u54C1\u7BA1\u7406\u5E94\u652F\u6301\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u7B5B\u9009\u5E76\u628A\u8303\u56F4\u53C2\u6570\u53D1\u9001\u5230\u5546\u54C1\u5217\u8868\u63A5\u53E3", () => {
    assert(
      typeSource.includes("storeRecordCountMin?: number") && typeSource.includes("storeRecordCountMax?: number"),
      "PosProductFilterParams \u5E94\u58F0\u660E\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u6700\u5C0F\u503C/\u6700\u5927\u503C"
    );
    assert(
      serviceSource.includes("storeRecordCountMin: params.storeRecordCountMin") && serviceSource.includes("storeRecordCountMax: params.storeRecordCountMax"),
      "getProducts \u8BF7\u6C42\u4F53\u5E94\u628A\u5206\u5E97\u8BB0\u5F55\u6570\u91CF\u8303\u56F4\u539F\u6837\u53D1\u9001\u7ED9\u540E\u7AEF\u5217\u8868\u63A5\u53E3"
    );
    assert(
      pageSource.includes("const [storeRecordCountMode, setStoreRecordCountMode] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')") && pageSource.includes("const [storeRecordCountModeInput, setStoreRecordCountModeInput] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')") && pageSource.includes("const [storeRecordCountMin, setStoreRecordCountMin] = useState<number | undefined>(undefined)") && pageSource.includes("const [storeRecordCountMax, setStoreRecordCountMax] = useState<number | undefined>(undefined)") && pageSource.includes("const [storeRecordCountMinInput, setStoreRecordCountMinInput] = useState<number | undefined>(undefined)") && pageSource.includes("const [storeRecordCountMaxInput, setStoreRecordCountMaxInput] = useState<number | undefined>(undefined)"),
      "\u9875\u9762\u5E94\u5206\u522B\u7EF4\u62A4\u5DF2\u751F\u6548\u548C\u8F93\u5165\u4E2D\u7684\u5206\u5E97\u8BB0\u5F55\u7B5B\u9009\u6A21\u5F0F\u4E0E\u8303\u56F4"
    );
    assert(
      pageSource.includes("storeRecordCountMin: storeRecordCountMin") && pageSource.includes("storeRecordCountMax: storeRecordCountMax"),
      "\u67E5\u8BE2\u751F\u6548\u540E loadData \u5E94\u628A\u5F53\u524D\u5206\u5E97\u8BB0\u5F55\u7B5B\u9009\u6761\u4EF6\u5E26\u5165\u8BF7\u6C42\u53C2\u6570"
    );
    assert(
      pageSource.includes("let nextStoreRecordCountMin = storeRecordCountMinInput") && pageSource.includes("let nextStoreRecordCountMax = storeRecordCountMaxInput") && pageSource.includes("storeRecordCountModeInput === 'hasRecords'") && pageSource.includes("nextStoreRecordCountMin = 1") && pageSource.includes("storeRecordCountModeInput === 'noRecords'") && pageSource.includes("nextStoreRecordCountMin = 0") && pageSource.includes("nextStoreRecordCountMax = 0") && pageSource.includes("let nextStoreRecordCountMode = storeRecordCountModeInput") && pageSource.includes("nextStoreRecordCountMode = 'all'") && pageSource.includes("setStoreRecordCountMode(nextStoreRecordCountMode)") && pageSource.includes("setStoreRecordCountMin(nextStoreRecordCountMin)") && pageSource.includes("setStoreRecordCountMax(nextStoreRecordCountMax)"),
      "\u70B9\u51FB\u67E5\u8BE2\u540E\u5E94\u6309\u7B5B\u9009\u6A21\u5F0F\u628A\u8F93\u5165\u6761\u4EF6\u6298\u7B97\u6210\u771F\u5B9E\u67E5\u8BE2\u8303\u56F4\uFF0C\u518D\u5E94\u7528\u5230\u8BF7\u6C42\u72B6\u6001"
    );
    assert(
      pageSource.includes("storeRecordCountModeInput === 'custom'") && pageSource.includes("storeRecordCountMinInput === undefined") && pageSource.includes("storeRecordCountMaxInput === undefined") && pageSource.includes("message.warning(t('posAdmin.products.storeRecordFilterInvalidRange', '\u6700\u5C0F\u6570\u91CF\u4E0D\u80FD\u5927\u4E8E\u6700\u5927\u6570\u91CF'))") && pageSource.includes("return"),
      "\u81EA\u5B9A\u4E49\u8303\u56F4\u4E24\u7AEF\u4E3A\u7A7A\u5E94\u56DE\u5230\u5168\u90E8\uFF0C\u6700\u5C0F\u503C\u5927\u4E8E\u6700\u5927\u503C\u65F6\u5E94\u63D0\u793A\u5E76\u505C\u6B62\u67E5\u8BE2"
    );
    assert(
      !pageSource.includes("setStoreRecordCountMin(storeRecordCountMinInput)") && !pageSource.includes("setStoreRecordCountMax(storeRecordCountMaxInput)"),
      "\u67E5\u8BE2\u65F6\u4E0D\u80FD\u628A\u8F93\u5165\u6001\u8303\u56F4\u76F4\u63A5\u5199\u5165\u751F\u6548\u6001\uFF0C\u5E94\u53EA\u5199\u5165\u6309\u6A21\u5F0F\u6298\u7B97\u540E\u7684\u8303\u56F4"
    );
    assert(
      pageSource.includes("setStoreRecordCountModeInput('all')") && pageSource.includes("setStoreRecordCountMode('all')") && pageSource.includes("setStoreRecordCountMinInput(undefined)") && pageSource.includes("setStoreRecordCountMaxInput(undefined)") && pageSource.includes("setStoreRecordCountMin(undefined)") && pageSource.includes("setStoreRecordCountMax(undefined)"),
      "\u70B9\u51FB\u91CD\u7F6E\u65F6\u5E94\u6E05\u7A7A\u5206\u5E97\u8BB0\u5F55\u7B5B\u9009\u6A21\u5F0F\u4E0E\u8303\u56F4"
    );
    assert(
      pageSource.includes("t('posAdmin.products.storeRecordFilterPlaceholder', '\u5206\u5E97\u8BB0\u5F55')") && pageSource.includes("t('posAdmin.products.storeRecordFilterAll', '\u5168\u90E8')") && pageSource.includes("t('posAdmin.products.storeRecordFilterHasRecords', '\u6709\u8BB0\u5F55')") && pageSource.includes("t('posAdmin.products.storeRecordFilterNoRecords', '\u65E0\u8BB0\u5F55')") && pageSource.includes("t('posAdmin.products.storeRecordFilterCustom', '\u81EA\u5B9A\u4E49\u8303\u56F4')") && pageSource.includes("t('posAdmin.products.storeRecordFilterMin', '\u6700\u5C0F\u6570\u91CF')") && pageSource.includes("t('posAdmin.products.storeRecordFilterMax', '\u6700\u5927\u6570\u91CF')"),
      "\u9875\u9762\u5E94\u63D0\u4F9B\u5206\u5E97\u8BB0\u5F55\u7B5B\u9009\u6A21\u5F0F\u4E0E\u8303\u56F4\u8F93\u5165\u63A7\u4EF6"
    );
  });
  if (storeRecordFiltersFailure) failures.push(storeRecordFiltersFailure);
  const storeRecordListSortFailure = await runTest("\u5546\u54C1\u7BA1\u7406\u4E3B\u5217\u8868\u5206\u5E97\u8BB0\u5F55\u5217\u5E94\u542F\u7528\u670D\u52A1\u7AEF\u6392\u5E8F\u6620\u5C04", () => {
    assert(
      pageSource.includes("storeRecordCount: 'storerecordcount'"),
      "SORT_FIELD_MAP \u5E94\u628A storeRecordCount \u6620\u5C04\u5230\u540E\u7AEF storerecordcount \u6392\u5E8F\u5B57\u6BB5"
    );
    assert(
      pageSource.includes("dataIndex: 'storeRecordCount'") && pageSource.includes("sorter: true") && pageSource.includes("sortOrder: sortBy === 'storeRecordCount' ? sortOrder : undefined"),
      "\u5206\u5E97\u8BB0\u5F55\u4E3B\u5217\u8868\u5217\u5E94\u5F00\u542F\u670D\u52A1\u7AEF\u6392\u5E8F\uFF0C\u5E76\u628A\u5F53\u524D\u6392\u5E8F\u72B6\u6001\u7ED1\u5B9A\u5230 storeRecordCount"
    );
    assert(
      pageSource.includes("count > 0 && canManageStoreProducts") && pageSource.includes("compareProductStoreRecordsByName"),
      "\u5206\u5E97\u8BB0\u5F55\u4E3B\u5217\u8868\u5217\u4ECD\u5E94\u4FDD\u6301\u53EA\u6709\u6709\u6743\u9650\u4E14\u6570\u91CF\u5927\u4E8E 0 \u65F6\u624D\u53EF\u70B9\u51FB\u67E5\u770B\u660E\u7EC6"
    );
  });
  if (storeRecordListSortFailure) failures.push(storeRecordListSortFailure);
  const productColumnFiltersFailure = await runTest("\u5546\u54C1\u7BA1\u7406\u4E3B\u5217\u8868\u5E94\u652F\u6301\u5217\u5934\u7B5B\u9009\u5E76\u8D70\u540E\u7AEF\u67E5\u8BE2\u53C2\u6570", () => {
    assert(
      typeSource.includes("columnFilters?: PosProductColumnFilters") && typeSource.includes("export type PosProductTextFilterOperator = 'contains' | 'equals' | 'startsWith' | 'endsWith'") && typeSource.includes("export type PosProductNumberFilterOperator = 'equals' | 'between' | 'gte' | 'lte'") && typeSource.includes("export type PosProductDateFilterOperator = 'equals' | 'between' | 'gte' | 'lte'"),
      "\u5546\u54C1\u67E5\u8BE2\u7C7B\u578B\u5E94\u58F0\u660E\u5217\u5934\u7B5B\u9009\u548C\u6587\u672C/\u6570\u5B57/\u65E5\u671F\u64CD\u4F5C\u7B26"
    );
    assert(
      serviceSource.includes("TEXT_FILTER_TYPE_MAP") && serviceSource.includes("NUMBER_FILTER_TYPE_MAP") && serviceSource.includes("applyTextColumnFilter") && serviceSource.includes("applyNumberColumnFilter") && serviceSource.includes("applyDateColumnFilter") && serviceSource.includes("localSupplierCodes") && serviceSource.includes("isAutoPricingValues") && serviceSource.includes("productTypeValues") && serviceSource.includes("if (operator === 'lte')") && serviceSource.includes("payload[maxField] = value"),
      "\u5546\u54C1\u670D\u52A1\u5E94\u628A\u5217\u5934\u7B5B\u9009\u6620\u5C04\u4E3A\u540E\u7AEF ProductReactFilterDto \u5B57\u6BB5"
    );
    assert(
      pageSource.includes("const [columnFilters, setColumnFilters] = useState<PosProductColumnFilters>({})") && pageSource.includes("columnFilters,") && pageSource.includes("normalizeProductTableFilters(filters as Record<string, FilterValue | null>)") && pageSource.includes("setColumnFilters(nextColumnFilters)") && pageSource.includes("setPage(1)") && pageSource.includes("setSelectedRowKeys([])"),
      "\u9875\u9762\u5E94\u4FDD\u5B58\u5217\u5934\u7B5B\u9009\u72B6\u6001\uFF0C\u5E76\u5728\u8868\u683C\u7B5B\u9009/\u6392\u5E8F\u53D8\u5316\u540E\u56DE\u5230\u7B2C\u4E00\u9875\u4E14\u6E05\u7A7A\u9009\u62E9"
    );
    assert(
      pageSource.includes("function hasProductColumnFilterValue") && pageSource.includes("\u63D0\u4EA4\u67E5\u8BE2\u65F6\u518D\u8FC7\u6EE4\u7A7A\u7B5B\u9009") && pageSource.includes("return JSON.stringify({ operator, value: value.trim() })"),
      "\u5217\u5934\u7B5B\u9009\u4E0B\u62C9\u5E94\u4FDD\u7559\u7A7A\u503C draft token\uFF0C\u907F\u514D\u5148\u9009\u64CD\u4F5C\u7B26\u65F6\u88AB\u91CD\u7F6E"
    );
    assert(
      pageSource.includes("buildTextFilterDropdown") && pageSource.includes("buildNumberFilterDropdown") && pageSource.includes("buildDateFilterDropdown") && pageSource.includes("renderColumnFilterPanel") && pageSource.includes("pos-products-column-filter-panel") && pageSource.includes("textFilterProps('productCode'") && pageSource.includes("textFilterProps('itemNumber'") && pageSource.includes("numberFilterProps('purchasePrice')") && pageSource.includes("dateFilterProps('createdAt')") && pageSource.includes("enumFilterProps('isActive'"),
      "\u9875\u9762\u5E94\u7ED9\u6587\u672C\u3001\u6570\u5B57\u3001\u65E5\u671F\u548C\u679A\u4E3E\u5217\u7ED1\u5B9A\u5217\u5934\u7B5B\u9009\u63A7\u4EF6"
    );
    assert(
      globalStyleSource.includes(".pos-products-column-filter-panel") && globalStyleSource.includes(".pos-products-column-filter-actions") && globalStyleSource.includes(".pos-products-compact-table .ant-table-filter-column") && globalStyleSource.includes(".pos-products-compact-table .ant-table-filter-column-title"),
      "\u5546\u54C1\u7BA1\u7406\u5217\u5934\u7B5B\u9009\u5F39\u5C42\u548C\u8868\u5934\u5E94\u6709\u7EDF\u4E00\u6837\u5F0F\uFF0C\u907F\u514D\u63A7\u4EF6\u62E5\u6324\u548C\u6807\u9898\u7AD6\u6392"
    );
    assert(
      pageSource.includes("title: t('posAdmin.products.productCode', '\u5546\u54C1\u7F16\u7801')") && pageSource.includes("width: 86") && pageSource.includes("title: t('posAdmin.products.autoPricing', '\u81EA\u52A8\u5B9A\u4EF7')") && pageSource.includes("width: 92") && pageSource.includes("title: t('posAdmin.products.productTypeLabel', '\u5546\u54C1\u7C7B\u578B')") && pageSource.includes("width: 88") && pageSource.includes("scroll={{ x: 1640, y: tableScrollY }}"),
      "\u63A5\u5165\u7B5B\u9009/\u6392\u5E8F\u540E\u7684\u7A84\u5217\u8868\u5934\u5E94\u653E\u5BBD\u5217\u5BBD\uFF0C\u5E76\u540C\u6B65\u8868\u683C\u6A2A\u5411\u6EDA\u52A8\u5BBD\u5EA6"
    );
    assert(
      pageSource.includes("setColumnFilters({})") && !pageSource.includes("title: t('posAdmin.products.unitWeight', '\u91CD\u91CF')"),
      "\u91CD\u7F6E\u5E94\u6E05\u7A7A\u5217\u5934\u7B5B\u9009\uFF0C\u4E14\u4E3B\u8868\u91CD\u91CF\u5217\u5E94\u79FB\u9664"
    );
  });
  if (productColumnFiltersFailure) failures.push(productColumnFiltersFailure);
  const productAutoPricingColumnFailure = await runTest("\u5546\u54C1\u7BA1\u7406\u4E3B\u5217\u8868\u5E94\u663E\u793A\u81EA\u52A8\u5B9A\u4EF7\u5217", () => {
    const columnsStart = pageSource.indexOf("const columns: ColumnsType<ProductRow> = [");
    const columnsEnd = pageSource.indexOf("\n  ]\n\n  return (", columnsStart);
    const mainColumnsSource = pageSource.slice(columnsStart, columnsEnd);
    assert(
      mainColumnsSource.includes("title: t('posAdmin.products.autoPricing', '\u81EA\u52A8\u5B9A\u4EF7')") && mainColumnsSource.includes("dataIndex: 'isAutoPricing'") && mainColumnsSource.includes("<Tag color={value ? 'green' : 'default'}>") && mainColumnsSource.includes("t('common.yes', '\u662F')") && mainColumnsSource.includes("t('common.no', '\u5426')"),
      "\u5546\u54C1\u7BA1\u7406\u4E3B\u8868\u5E94\u65B0\u589E\u81EA\u52A8\u5B9A\u4EF7\u5217\uFF0C\u5E76\u4EE5\u662F/\u5426\u5C55\u793A ProductRow.isAutoPricing"
    );
  });
  if (productAutoPricingColumnFailure) failures.push(productAutoPricingColumnFailure);
  const storeRecordsBatchUpdateFailure = await runTest("\u5206\u5E97\u8BB0\u5F55\u5F39\u7A97\u5E94\u652F\u6301\u6279\u91CF\u4FEE\u6539\u5206\u5E97\u4E1A\u52A1\u5B57\u6BB5", () => {
    assert(
      typeSource.includes("BatchUpdateProductStoreRecordsRequest") && typeSource.includes("BatchUpdateProductStoreRecordsResult") && typeSource.includes("purchasePrice?: number") && typeSource.includes("storeRetailPriceValue?: number") && typeSource.includes("discountRate?: number") && typeSource.includes("isAutoPricing?: boolean") && typeSource.includes("isSpecialProduct?: boolean") && typeSource.includes("isActive?: boolean"),
      "\u7C7B\u578B\u5C42\u5E94\u58F0\u660E\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u8BF7\u6C42/\u7ED3\u679C\uFF0C\u4EE5\u53CA\u516D\u4E2A\u53EF\u6539\u5B57\u6BB5"
    );
    assert(
      serviceSource.includes("batchUpdateProductStoreRecords") && serviceSource.includes("/store-records/batch-update"),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u63A5\u53E3"
    );
    assert(
      pageSource.includes("const canEditStoreProducts = useAuthStore((state) => state.access.canEditStoreProducts)"),
      "\u9875\u9762\u5E94\u4ECE auth store \u8BFB\u53D6 canEditStoreProducts"
    );
    assert(
      pageSource.includes("const [storeRecordSelectedRowKeys, setStoreRecordSelectedRowKeys] = useState<React.Key[]>([])") && pageSource.includes("const [storeRecordBatchEditVisible, setStoreRecordBatchEditVisible] = useState(false)") && pageSource.includes("const [storeRecordBatchUpdating, setStoreRecordBatchUpdating] = useState(false)") && pageSource.includes("const [storeRecordBatchEditForm] = Form.useForm()"),
      "\u9875\u9762\u5E94\u7EF4\u62A4\u5206\u5E97\u8BB0\u5F55\u9009\u62E9\u3001\u6279\u91CF\u5B50\u5F39\u7A97\u53EF\u89C1\u6027\u3001\u63D0\u4EA4\u6001\u548C\u8868\u5355\u72B6\u6001"
    );
    assert(
      pageSource.includes("rowSelection={{") && pageSource.includes("selectedRowKeys: storeRecordSelectedRowKeys") && pageSource.includes("setStoreRecordSelectedRowKeys(keys)"),
      "\u5206\u5E97\u8BB0\u5F55\u8868\u683C\u5E94\u652F\u6301\u884C\u9009\u62E9\uFF0C\u5E76\u5355\u72EC\u7EF4\u62A4\u9009\u4E2D key"
    );
    assert(
      pageSource.includes("t('posAdmin.products.batchUpdateStoreRecords', '\u6279\u91CF\u4FEE\u6539')") && pageSource.includes("disabled={!canEditStoreProducts || !storeRecordSelectedRowKeys.length}") && pageSource.includes("t('common.close', '\u5173\u95ED')"),
      "\u5206\u5E97\u8BB0\u5F55\u5F39\u7A97 footer \u5E94\u63D0\u4F9B\u53D7\u7F16\u8F91\u6743\u9650\u548C\u9009\u4E2D\u8BB0\u5F55\u63A7\u5236\u7684\u201C\u6279\u91CF\u4FEE\u6539/\u5173\u95ED\u201D\u6309\u94AE"
    );
    assert(
      pageSource.includes("batchUpdateProductStoreRecords(storeRecordsProduct.productCode, {") && pageSource.includes("storeCodes: selectedStoreCodes") && pageSource.includes("changes,"),
      "\u63D0\u4EA4\u65F6\u5E94\u4F7F\u7528\u5F53\u524D\u5546\u54C1\u7F16\u7801\u3001\u9009\u4E2D\u5206\u5E97\u4EE3\u7801\u548C changes \u8C03\u7528\u6279\u91CF\u4FEE\u6539\u63A5\u53E3"
    );
    assert(
      pageSource.includes("const selectedStoreCodes = storeRecordSelectedRows") && pageSource.includes(".map((record) => record.storeCode)") && pageSource.includes(".filter((storeCode): storeCode is string => !!storeCode)"),
      "\u63D0\u4EA4\u76EE\u6807\u5E94\u4ECE\u5DF2\u9009\u8BB0\u5F55\u63D0\u53D6\u975E\u7A7A storeCode"
    );
    assert(
      pageSource.includes("t('posAdmin.invoiceDetail.purchasePrice', '\u8FDB\u8D27\u4EF7')") && pageSource.includes("t('posAdmin.invoiceDetail.retailPrice', '\u96F6\u552E\u4EF7')") && pageSource.includes("t('posAdmin.productPrice.discountRate', '\u6298\u6263\u7387')") && pageSource.includes("t('posAdmin.products.autoPricing', '\u81EA\u52A8\u5B9A\u4EF7')") && pageSource.includes("t('posAdmin.products.specialProduct', '\u7279\u6B8A\u5546\u54C1')") && pageSource.includes("t('posAdmin.cashierUsers.status', '\u72B6\u6001')"),
      "\u6279\u91CF\u4FEE\u6539\u5B50\u5F39\u7A97\u5E94\u5305\u542B\u516D\u4E2A\u4E1A\u52A1\u5B57\u6BB5"
    );
    assert(
      pageSource.includes("t('posAdmin.products.toggleFieldUpdate', '\u4FEE\u6539\u8BE5\u5B57\u6BB5')") && pageSource.includes("precision={2}") && pageSource.includes("precision={4}") && pageSource.includes("value: true, label: t('common.yes', '\u662F')") && pageSource.includes("value: false, label: t('common.no', '\u5426')"),
      "\u6BCF\u4E2A\u5B57\u6BB5\u5E94\u7531\u201C\u4FEE\u6539\u8BE5\u5B57\u6BB5\u201D\u63A7\u5236\u7EB3\u5165 changes\uFF0C\u6570\u5B57\u7CBE\u5EA6\u4E0E\u5E03\u5C14\u9009\u9879\u8981\u660E\u786E"
    );
    assert(
      pageSource.includes("if (!storeRecordSelectedRowKeys.length) {") && pageSource.includes("message.warning(t('posAdmin.products.selectStoreRecordsFirst', '\u8BF7\u5148\u9009\u62E9\u5206\u5E97\u8BB0\u5F55'))") && pageSource.includes("message.warning(t('posAdmin.products.selectAtLeastOneStoreRecordField', '\u8BF7\u81F3\u5C11\u9009\u62E9\u4E00\u4E2A\u8981\u4FEE\u6539\u7684\u5B57\u6BB5'))") && pageSource.includes("message.warning(t('posAdmin.products.completeStoreRecordFields', '\u8BF7\u586B\u5199\u5DF2\u52FE\u9009\u7684\u5B57\u6BB5\u503C'))"),
      "\u63D0\u4EA4\u524D\u5E94\u6821\u9A8C\u5DF2\u9009\u5206\u5E97\u3001\u81F3\u5C11\u4E00\u4E2A\u5B57\u6BB5\u3001\u4EE5\u53CA\u5DF2\u52FE\u9009\u5B57\u6BB5\u5FC5\u987B\u6709\u503C"
    );
    assert(
      pageSource.includes("message.success(t('posAdmin.products.batchUpdateStoreRecordsResult'") && pageSource.includes("success: result.successCount") && pageSource.includes("failed: result.failedCount") && pageSource.includes("Modal.error({") && pageSource.includes("result.errors.join"),
      "\u63D0\u4EA4\u6210\u529F\u540E\u5E94\u63D0\u793A\u6210\u529F/\u5931\u8D25\u7EDF\u8BA1\uFF0C\u6709\u9519\u8BEF\u660E\u7EC6\u65F6\u8981\u5F39\u7A97\u5C55\u793A"
    );
    assert(
      pageSource.includes("await openStoreRecords(storeRecordsProduct)") && pageSource.includes("await loadData()"),
      "\u6279\u91CF\u4FEE\u6539\u6210\u529F\u540E\u5E94\u5237\u65B0\u5F53\u524D\u5206\u5E97\u8BB0\u5F55\u548C\u4E3B\u5217\u8868"
    );
    assert(
      pageSource.includes("setStoreRecordSelectedRowKeys([])") && pageSource.includes("storeRecordBatchEditForm.resetFields()") && pageSource.includes("setStoreRecordBatchEditVisible(false)"),
      "\u5173\u95ED\u5206\u5E97\u8BB0\u5F55\u5F39\u7A97\u65F6\u5E94\u6E05\u7406\u5206\u5E97\u8BB0\u5F55\u9009\u62E9\u548C\u6279\u91CF\u5B50\u5F39\u7A97\u72B6\u6001"
    );
  });
  if (storeRecordsBatchUpdateFailure) failures.push(storeRecordsBatchUpdateFailure);
  const storeRecordSorterFailure = await runTest("\u5206\u5E97\u8BB0\u5F55\u540D\u79F0\u6392\u5E8F\u5E94\u540C\u540D\u6309\u5206\u5E97\u4EE3\u7801\u515C\u5E95", () => {
    const records = [
      { storeCode: "S01", storeName: "Beta", isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: "S99", storeName: "", isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: "S04", storeName: "Alpha", isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: "S03", storeName: "Alpha", isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: "S02", storeName: "Gamma", isActive: true, isAutoPricing: false, isSpecialProduct: false }
    ];
    const sortedCodes = records.slice().sort(compareProductStoreRecordsByName).map((item) => item.storeCode);
    assertEqual(sortedCodes.join(","), "S03,S04,S01,S02,S99", "\u5206\u5E97\u8BB0\u5F55\u6392\u5E8F\u5E94\u5148\u6309\u5206\u5E97\u540D\u79F0\uFF0C\u518D\u6309\u5206\u5E97\u4EE3\u7801\u7A33\u5B9A\u6392\u5E8F");
  });
  if (storeRecordSorterFailure) failures.push(storeRecordSorterFailure);
  const storeRecordColumnSorterFailure = await runTest("\u5206\u5E97\u8BB0\u5F55\u5F39\u7A97\u5404\u5217\u5E94\u652F\u6301\u524D\u7AEF\u6392\u5E8F", () => {
    assert(
      pageSource.includes("sorter: compareProductStoreRecordsByStoreCode") && pageSource.includes("sorter: compareProductStoreRecordsByName") && pageSource.includes("sorter: compareProductStoreRecordsByStoreProductCode") && pageSource.includes("sorter: compareProductStoreRecordsByPurchasePrice") && pageSource.includes("sorter: compareProductStoreRecordsByRetailPrice") && pageSource.includes("sorter: compareProductStoreRecordsByDiscountRate") && pageSource.includes("sorter: compareProductStoreRecordsByAutoPricing") && pageSource.includes("sorter: compareProductStoreRecordsBySpecialProduct") && pageSource.includes("sorter: compareProductStoreRecordsByActive") && pageSource.includes("sorter: compareProductStoreRecordsByUpdatedAt") && pageSource.includes("sorter: compareProductStoreRecordsByUpdatedBy"),
      "\u5206\u5E97\u8BB0\u5F55\u5F39\u7A97\u5E94\u7ED9\u5206\u5E97\u4EE3\u7801\u3001\u540D\u79F0\u3001\u7F16\u7801\u3001\u4EF7\u683C\u3001\u6298\u6263\u7387\u3001\u5E03\u5C14\u72B6\u6001\u3001\u66F4\u65B0\u65F6\u95F4\u548C\u66F4\u65B0\u4EBA\u5217\u7ED1\u5B9A\u524D\u7AEF sorter"
    );
    const records = [
      {
        storeCode: "1002",
        storeName: "Beta",
        storeProductCode: "B",
        purchasePrice: 4.87,
        storeRetailPriceValue: 12.5,
        discountRate: 0,
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true,
        updatedAt: "2025-01-01T00:00:00Z",
        updatedBy: "ReactSync"
      },
      {
        storeCode: "1001",
        storeName: "Alpha",
        storeProductCode: "A",
        purchasePrice: 1.25,
        storeRetailPriceValue: 9.99,
        discountRate: 0.1,
        isAutoPricing: false,
        isSpecialProduct: true,
        isActive: false,
        updatedAt: "2024-01-01T00:00:00Z",
        updatedBy: "admin"
      },
      {
        storeCode: "1003",
        storeName: "Gamma",
        storeProductCode: "C",
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true
      }
    ];
    assertEqual(records.slice().sort(compareProductStoreRecordsByStoreCode)[0]?.storeCode, "1001", "\u5206\u5E97\u4EE3\u7801\u5E94\u6309\u6587\u672C\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByStoreProductCode)[0]?.storeProductCode, "A", "\u5206\u5E97\u5546\u54C1\u7F16\u7801\u5E94\u6309\u6587\u672C\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByPurchasePrice)[0]?.purchasePrice, 1.25, "\u8FDB\u8D27\u4EF7\u5E94\u6309\u6570\u5B57\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByRetailPrice)[0]?.storeRetailPriceValue, 9.99, "\u96F6\u552E\u4EF7\u5E94\u6309\u6570\u5B57\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByDiscountRate)[0]?.discountRate, 0, "\u6298\u6263\u7387\u5E94\u6309\u6570\u5B57\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByAutoPricing)[0]?.isAutoPricing, false, "\u81EA\u52A8\u5B9A\u4EF7\u5E94\u6309\u5426\u5230\u662F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsBySpecialProduct)[0]?.isSpecialProduct, false, "\u7279\u6B8A\u5546\u54C1\u5E94\u6309\u5426\u5230\u662F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByActive)[0]?.isActive, false, "\u72B6\u6001\u5E94\u6309\u7981\u7528\u5230\u542F\u7528\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByUpdatedAt)[0]?.updatedAt, "2024-01-01T00:00:00Z", "\u66F4\u65B0\u65F6\u95F4\u5E94\u6309\u65F6\u95F4\u5347\u5E8F\u6392\u5E8F");
    assertEqual(records.slice().sort(compareProductStoreRecordsByUpdatedBy)[0]?.updatedBy, void 0, "\u66F4\u65B0\u4EBA\u7A7A\u503C\u5E94\u6309\u6587\u672C\u7A7A\u503C\u6392\u5E8F");
  });
  if (storeRecordColumnSorterFailure) failures.push(storeRecordColumnSorterFailure);
  const pushToHqFailure = await runTest("\u9009\u4E2D\u5546\u54C1\u53D1\u9001\u5230 HQ \u5E94\u590D\u7528\u9009\u62E9\u3001\u6743\u9650\u548C\u9632\u91CD\u590D\u63D0\u4EA4\u4FDD\u62A4", () => {
    const pushToHqHandlerStart = pageSource.indexOf("const handlePushToHq = async () => {");
    const pushToHqHandlerEnd = pageSource.indexOf("const handleSyncSelectedFromHq", pushToHqHandlerStart);
    const pushToHqHandlerSource = pageSource.slice(pushToHqHandlerStart, pushToHqHandlerEnd);
    assert(
      typeSource.includes("PushProductsToHqRequest") && typeSource.includes("productCodes: string[]") && typeSource.includes("updateFields?: PushProductsToHqUpdateField[]") && typeSource.includes("PushProductsToHqResult"),
      "\u7C7B\u578B\u5C42\u5E94\u58F0\u660E\u53D1\u9001\u5230 HQ \u7684\u8BF7\u6C42\u548C\u7ED3\u679C\u5951\u7EA6"
    );
    assert(
      serviceSource.includes("pushProductsToHq") && serviceSource.includes("`${API_BASE}/push-to-hq`") && serviceSource.includes("normalizePushProductsToHqResult"),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B\u56FA\u5B9A\u63A5\u53E3\u8C03\u7528\uFF0C\u5E76\u5F52\u4E00\u540E\u7AEF\u7EDF\u8BA1\u5B57\u6BB5"
    );
    assert(
      pageSource.includes("handlePushToHq") && pageSource.includes("const productCodes = selectedRowKeys.map(String)") && pageSource.includes("const updateFields = await confirmPushToHqUpdateFields(productCodes.length)") && pageSource.includes("updateFields,") && pageSource.includes("pushProductsToHq({") && pageSource.includes("showPushToHqResult(result)"),
      "\u9875\u9762\u5E94\u628A\u5F53\u524D\u9009\u4E2D\u5546\u54C1\u7F16\u7801\u548C\u52FE\u9009\u5B57\u6BB5\u53D1\u9001\u5230 HQ\uFF0C\u5E76\u5C55\u793A\u6210\u529F\u548C\u9519\u8BEF\u660E\u7EC6"
    );
    assert(
      typeSource.includes("export const pushProductsToHqUpdateFieldOptions = [") && typeSource.includes("type MissingPushProductsToHqUpdateFieldOption = Exclude<PushProductsToHqUpdateField, PushProductsToHqUpdateFieldOptionValue>") && typeSource.includes("const assertAllPushProductsToHqUpdateFieldsCovered: Record<MissingPushProductsToHqUpdateFieldOption, never> = {}") && typeSource.includes("export const defaultPushProductsToHqUpdateFields") && pageSource.includes("pushProductsToHqUpdateFieldOptions.map") && pageSource.includes("defaultPushProductsToHqUpdateFields") && pageSource.includes("<Checkbox.Group") && pageSource.includes("message.warning(t('containers.updateFields.selectAtLeastOne', '\u8BF7\u81F3\u5C11\u9009\u62E9\u4E00\u4E2A\u66F4\u65B0\u5B57\u6BB5'))") && pageSource.includes("'containers.updateFields.hqCreateHint'"),
      "\u53D1\u9001\u5230 HQ \u524D\u7AEF\u5E94\u63D0\u4F9B\u548C\u5B57\u6BB5\u7C7B\u578B\u5339\u914D\u7684\u52FE\u9009\u66F4\u65B0\u5B57\u6BB5\u5F39\u7A97"
    );
    assert(
      pageSource.includes("extractPushToHqErrorResult(error)") && pageSource.includes("payload.details") && pageSource.includes("Modal.error({") && pageSource.includes("errorResult.errors.join"),
      "\u53D1\u9001\u5230 HQ \u5931\u8D25\u65F6\u5E94\u4ECE\u540E\u7AEF data/details \u4E2D\u63D0\u53D6\u9519\u8BEF\u660E\u7EC6\u5E76\u5F39\u7A97\u5C55\u793A"
    );
    assert(
      pageSource.includes("pushToHqAffectedRows") && pageSource.includes("affectedRowCount") && pageSource.includes("\u5546\u54C1\u6210\u529F {{success}}"),
      "\u53D1\u9001\u5230 HQ \u7ED3\u679C\u5E94\u533A\u5206\u5546\u54C1\u6210\u529F\u6570\u548C HQ \u5F71\u54CD\u8BB0\u5F55\u6570\uFF0C\u907F\u514D\u7EDF\u8BA1\u8BED\u4E49\u6DF7\u6DC6"
    );
    assert(
      pageSource.includes("t('posAdmin.products.productSetCodesCreated', '\u5957\u88C5\u7F16\u7801\u65B0\u589E')") && pageSource.includes("t('posAdmin.products.productSetCodesUpdated', '\u5957\u88C5\u7F16\u7801\u66F4\u65B0')") && pageSource.includes("t('posAdmin.products.storeMultiCodesCreated', '\u95E8\u5E97\u591A\u7801\u65B0\u589E')") && pageSource.includes("t('posAdmin.products.storeMultiCodesUpdated', '\u95E8\u5E97\u591A\u7801\u66F4\u65B0')") && serviceSource.includes("Number(payload.productSetCodesCreated ?? payload.productSetCodesAdded ?? 0)") && serviceSource.includes("Number(payload.storeMultiCodesCreated ?? 0)") && serviceSource.includes("Number(payload.storeMultiCodesUpdated ?? 0)"),
      "\u53D1\u9001\u5230 HQ \u6210\u529F\u5F39\u7A97\u548C\u670D\u52A1\u5F52\u4E00\u5316\u5E94\u8986\u76D6\u5957\u88C5\u7F16\u7801\u4E0E\u95E8\u5E97\u591A\u7801\u7EDF\u8BA1"
    );
    assert(
      pageSource.includes("if (!ensureCanManagePosProducts()) return") && pageSource.includes("canManagePosProducts") && pageSource.includes("t('posAdmin.products.pushToHq', '\u53D1\u9001\u5230HQ')"),
      "\u53D1\u9001\u5230 HQ \u6309\u94AE\u548C\u5904\u7406\u51FD\u6570\u5E94\u590D\u7528 POS \u5546\u54C1\u7BA1\u7406\u6743\u9650"
    );
    assert(
      pageSource.includes("const pushToHqLoadingRef = useRef(false)") && pageSource.includes("if (pushToHqLoadingRef.current) return") && pageSource.includes("pushToHqLoadingRef.current = true") && pageSource.includes("pushToHqLoadingRef.current = false") && pageSource.includes("disabled={!selectedRowKeys.length || pushToHqLoading}"),
      "\u53D1\u9001\u5230 HQ \u5E94\u4F7F\u7528 ref \u9501\u548C loading \u72B6\u6001\u9632\u6B62\u8FDE\u7EED\u70B9\u51FB\u91CD\u590D\u63D0\u4EA4"
    );
    assertSourceOrder(
      pushToHqHandlerSource,
      "pushToHqLoadingRef.current = true",
      "const updateFields = await confirmPushToHqUpdateFields(productCodes.length)",
      "\u53D1\u9001\u5230 HQ \u5E94\u5728\u5B57\u6BB5\u786E\u8BA4\u5F39\u7A97\u524D\u5360\u7528\u9501\uFF0C\u907F\u514D\u8FDE\u7EED\u70B9\u51FB\u6253\u5F00\u591A\u4E2A\u786E\u8BA4\u6846"
    );
  });
  if (pushToHqFailure) failures.push(pushToHqFailure);
  const selectedFromHqFailure = await runTest("\u9009\u4E2D\u5546\u54C1\u4ECE HQ \u540C\u6B65\u5E94\u590D\u7528\u9009\u62E9\u3001Admin \u6743\u9650\u548C\u9632\u91CD\u590D\u63D0\u4EA4\u4FDD\u62A4", () => {
    assert(
      typeSource.includes("SyncSelectedProductsFromHqRequest") && typeSource.includes("productCodes: string[]"),
      "\u7C7B\u578B\u5C42\u5E94\u58F0\u660E\u9009\u4E2D\u5546\u54C1\u4ECE HQ \u540C\u6B65\u7684\u8BF7\u6C42\u5951\u7EA6"
    );
    assert(
      serviceSource.includes("syncSelectedProductsFromHq") && serviceSource.includes("`${API_BASE}/sync-selected-from-hq`") && serviceSource.includes("normalizeHqProductSyncResult"),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B\u9009\u4E2D\u5546\u54C1\u4ECE HQ \u540C\u6B65\u63A5\u53E3\uFF0C\u5E76\u590D\u7528 HQ \u540C\u6B65\u7ED3\u679C\u5F52\u4E00\u5316"
    );
    assert(
      pageSource.includes("handleSyncSelectedFromHq") && pageSource.includes("selectedRowKeys.map(String)") && pageSource.includes("syncSelectedProductsFromHq({") && pageSource.includes("showSelectedFromHqResult(result)"),
      "\u9875\u9762\u5E94\u628A\u5F53\u524D\u9009\u4E2D\u5546\u54C1\u7F16\u7801\u53D1\u9001\u7ED9\u4ECE HQ \u9009\u4E2D\u540C\u6B65\u63A5\u53E3\uFF0C\u5E76\u5C55\u793A\u7ED3\u679C\u660E\u7EC6"
    );
    assert(
      pageSource.includes("const selectedFromHqLoadingRef = useRef(false)") && pageSource.includes("if (selectedFromHqLoadingRef.current) return") && pageSource.includes("selectedFromHqLoadingRef.current = true") && pageSource.includes("selectedFromHqLoadingRef.current = false"),
      "\u9009\u4E2D\u5546\u54C1\u4ECE HQ \u540C\u6B65\u5E94\u4F7F\u7528 ref \u9501\u9632\u6B62\u8FDE\u7EED\u70B9\u51FB\u91CD\u590D\u63D0\u4EA4"
    );
    assert(
      pageSource.includes("ensureCanSyncProductsFromHq") && pageSource.includes("isAdmin") && pageSource.includes("t('posAdmin.products.syncSelectedFromHq', '\u4ECEHQ\u540C\u6B65\u9009\u4E2D')") && pageSource.includes("disabled={!selectedRowKeys.length || selectedFromHqLoading}"),
      "\u4ECE HQ \u540C\u6B65\u9009\u4E2D\u6309\u94AE\u5E94\u53EA\u5BF9 Admin \u663E\u793A\uFF0C\u5E76\u5728\u672A\u9009\u62E9\u6216 loading \u65F6\u7981\u7528"
    );
  });
  if (selectedFromHqFailure) failures.push(selectedFromHqFailure);
  const batchTranslateFailure = await runTest("\u9009\u4E2D\u5546\u54C1\u6279\u91CF\u7FFB\u8BD1\u5E94\u9ED8\u8BA4\u4E2D\u6587\u5230\u82F1\u6587\u5E76\u8986\u76D6\u5546\u54C1\u540D\u79F0", () => {
    assert(
      pageSource.includes("../../../services/translationService") && pageSource.includes("batchTranslate"),
      "\u9875\u9762\u5E94\u5F15\u5165\u6279\u91CF\u7FFB\u8BD1\u670D\u52A1"
    );
    assert(
      pageSource.includes("const [translating, setTranslating] = useState(false)") && pageSource.includes("setTranslating(true)") && pageSource.includes("setTranslating(false)"),
      "\u9875\u9762\u5E94\u7EF4\u62A4\u6279\u91CF\u7FFB\u8BD1 loading \u72B6\u6001"
    );
    assert(
      pageSource.includes("containsChineseText") && pageSource.includes("buildProductNameTranslationUpdates") && pageSource.includes("!containsChineseText(translatedName)"),
      "\u6279\u91CF\u7FFB\u8BD1\u5E94\u8FC7\u6EE4\u7A7A\u7ED3\u679C\u3001\u672A\u53D8\u5316\u7ED3\u679C\u548C\u4ECD\u5305\u542B\u4E2D\u6587\u7684\u7ED3\u679C"
    );
    assert(
      pageSource.includes("const selectedRows = data.filter((row) => selectedRowKeys.includes(row.key))") && pageSource.includes("Array.from(new Set(selectedRows.map((row) => row.productName.trim())") && pageSource.includes("const translations = await batchTranslate(names)"),
      "\u6279\u91CF\u7FFB\u8BD1\u5E94\u53EA\u4F7F\u7528\u5F53\u524D\u9875\u9009\u4E2D\u5546\u54C1\u540D\u79F0\u53BB\u91CD\u540E\u8C03\u7528\u7FFB\u8BD1\u63A5\u53E3"
    );
    assert(
      pageSource.includes("const result = await batchUpdateProducts(updates)") && pageSource.includes("productCode: row.productCode") && pageSource.includes("productName: translatedName") && pageSource.includes("englishName: translatedName"),
      "\u6279\u91CF\u7FFB\u8BD1\u5E94\u901A\u8FC7\u6279\u91CF\u66F4\u65B0\u63A5\u53E3\u63D0\u4EA4 productCode\u3001\u7FFB\u8BD1\u540E\u7684 productName \u548C englishName"
    );
    assert(
      typeSource.includes("englishName?: string"),
      "\u6279\u91CF\u66F4\u65B0\u5546\u54C1 DTO \u5E94\u58F0\u660E englishName \u5B57\u6BB5\u4EE5\u540C\u6B65\u5199\u5165\u540E\u7AEF EnglishName"
    );
    assert(
      pageSource.includes("t('posAdmin.products.batchTranslate', '\u6279\u91CF\u7FFB\u8BD1')") && pageSource.includes("disabled={!selectedRowKeys.length || translating}") && pageSource.includes("loading={translating}"),
      "\u5DE5\u5177\u680F\u5E94\u63D0\u4F9B\u53D7\u9009\u4E2D\u72B6\u6001\u548C loading \u63A7\u5236\u7684\u6279\u91CF\u7FFB\u8BD1\u6309\u94AE"
    );
  });
  if (batchTranslateFailure) failures.push(batchTranslateFailure);
  const jobEndpointFailure = await runTest("\u5168\u91CF\u548C\u589E\u91CF\u5E94\u521B\u5EFA\u540E\u53F0 job \u800C\u4E0D\u662F\u76F4\u63A5\u7B49\u5F85\u957F\u540C\u6B65\u8BF7\u6C42", () => {
    assert(
      serviceSource.includes("`${SYNC_API_BASE}/products/jobs`") && serviceSource.includes("`${SYNC_API_BASE}/products-incremental/jobs`") && (serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${encodeURIComponent(jobId)}`") || serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${jobId}`")),
      "\u670D\u52A1\u5C42\u5E94\u63D0\u4F9B\u5546\u54C1 HQ \u540C\u6B65 job \u521B\u5EFA\u548C\u67E5\u8BE2\u63A5\u53E3"
    );
    assert(
      pageSource.includes("createProductFullHqSyncJob({ operationId })") && pageSource.includes("createProductIncrementalHqSyncJob({") && pageSource.includes("const startDate = values.startDate ? values.startDate.format('YYYY-MM-DD')"),
      "\u9875\u9762\u5E94\u6309\u540C\u6B65\u6A21\u5F0F\u5206\u522B\u521B\u5EFA\u5168\u91CF/\u589E\u91CF job\uFF0C\u589E\u91CF\u9700\u8981\u4F20 YYYY-MM-DD \u8D77\u59CB\u65E5\u671F"
    );
    assert(
      !pageSource.includes("await syncProductsFromHqFull()") && !pageSource.includes("await syncProductsFromHqIncremental({"),
      "\u9875\u9762\u4E0D\u5E94\u7EE7\u7EED\u76F4\u63A5\u7B49\u5F85\u957F\u540C\u6B65\u63A5\u53E3\u5B8C\u6210"
    );
  });
  if (jobEndpointFailure) failures.push(jobEndpointFailure);
  const syncResultMappingFailure = await runTest("HqProductSyncResult \u4E0E\u9875\u9762\u6587\u6848\u5E94\u5207\u5230\u65B0\u5B57\u6BB5", () => {
    assert(
      typeSource.includes("productsAdded?: number") && typeSource.includes("productsUpdated?: number") && typeSource.includes("productsDeleted?: number"),
      "HqProductSyncResult \u7C7B\u578B\u5E94\u58F0\u660E productsAdded/productsUpdated/productsDeleted"
    );
    assert(
      pageSource.includes("productsAdded") && pageSource.includes("productsUpdated") && pageSource.includes("productsDeleted"),
      "\u9875\u9762\u540C\u6B65\u6210\u529F\u63D0\u793A\u5E94\u8BFB\u53D6 productsAdded/productsUpdated/productsDeleted"
    );
  });
  if (syncResultMappingFailure) failures.push(syncResultMappingFailure);
  const duplicateClickGuardFailure = await runTest("HQ \u540C\u6B65\u786E\u8BA4\u5E94\u9632\u6B62\u8FDE\u7EED\u70B9\u51FB\u91CD\u590D\u63D0\u4EA4", () => {
    assert(
      pageSource.includes("const [hqSyncSubmitting, setHqSyncSubmitting] = useState(false)") && pageSource.includes("const hqSyncSubmittingRef = useRef(false)"),
      "\u9875\u9762\u5E94\u7EF4\u62A4 hqSyncSubmitting \u72B6\u6001\u548C ref \u9501"
    );
    assert(
      pageSource.includes("if (hqSyncSubmittingRef.current) return") && pageSource.includes("hqSyncSubmittingRef.current = true") && pageSource.includes("hqSyncSubmittingRef.current = false"),
      "\u540C\u6B65\u5904\u7406\u51FD\u6570\u5E94\u5728\u8FDE\u7EED\u70B9\u51FB\u65F6\u76F4\u63A5\u8FD4\u56DE\uFF0C\u5E76\u5728\u7ED3\u675F\u540E\u91CA\u653E\u9501"
    );
    assert(
      pageSource.includes("confirmLoading={hqSyncSubmitting}") && pageSource.includes("disabled={hqSyncSubmitting}"),
      "\u540C\u6B65\u6309\u94AE\u548C\u5F39\u7A97\u786E\u8BA4\u5E94\u7ED1\u5B9A submitting \u72B6\u6001"
    );
  });
  if (duplicateClickGuardFailure) failures.push(duplicateClickGuardFailure);
  const backgroundJobFailure = await runTest("HQ \u540C\u6B65\u5E94\u63D0\u4EA4\u540E\u53F0 job \u540E\u7ACB\u5373\u5173\u95ED\u5F39\u7A97\u5E76\u63D0\u793A\u540E\u53F0\u6267\u884C", () => {
    assert(
      pageSource.includes("setHqSyncVisible(false)") && pageSource.includes("hqSyncJobSubmitted") && pageSource.includes("startHqSyncJobPolling(activeJob)"),
      "\u521B\u5EFA job \u6210\u529F\u540E\u5E94\u5173\u95ED\u5F39\u7A97\u3001\u63D0\u793A\u540E\u53F0\u6267\u884C\uFF0C\u5E76\u542F\u52A8\u8F6E\u8BE2"
    );
  });
  if (backgroundJobFailure) failures.push(backgroundJobFailure);
  const activeJobFailure = await runTest("HQ \u540C\u6B65 active job \u5E94\u5199\u5165 localStorage \u5E76\u5728\u5237\u65B0\u540E\u6062\u590D\u8F6E\u8BE2", () => {
    assert(
      pageSource.includes("PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY") && pageSource.includes("localStorage.setItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY") && pageSource.includes("localStorage.removeItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY"),
      "\u9875\u9762\u5E94\u4F7F\u7528\u56FA\u5B9A key \u4FDD\u5B58\u548C\u6E05\u7406 active job"
    );
    assert(
      pageSource.includes("restoreActiveHqSyncJob()") && pageSource.includes("readActiveProductHqSyncJob()") && pageSource.includes("startHqSyncJobPolling(restoredJob)"),
      "\u9875\u9762\u5237\u65B0\u540E\u5E94\u8BFB\u53D6 active job \u5E76\u6062\u590D\u8F6E\u8BE2"
    );
    assert(
      pageSource.includes("}, [stopHqSyncJobPolling])") && pageSource.includes("}, [restoreActiveHqSyncJob])"),
      "\u5378\u8F7D\u6E05\u7406\u548C\u6062\u590D\u8F6E\u8BE2\u5E94\u62C6\u6210\u72EC\u7ACB effect\uFF0C\u907F\u514D\u5206\u9875/\u7B5B\u9009\u53D8\u5316\u8BEF\u505C\u8F6E\u8BE2"
    );
  });
  if (activeJobFailure) failures.push(activeJobFailure);
  const hqSyncArchitectureFailure = await runTest("\u5546\u54C1 HQ \u540C\u6B65\u9875\u9762\u5E94\u4F7F\u7528\u5171\u4EAB\u8F6E\u8BE2\u5668\u548C\u7EDF\u4E00 operationId", () => {
    assert(
      serviceSource.includes("export function buildProductHqSyncOperationId") && pageSource.includes("buildProductHqSyncOperationId"),
      "operationId \u5E94\u7531\u670D\u52A1\u5C42\u7EDF\u4E00\u5BFC\u51FA\uFF0C\u9875\u9762\u4E0D\u5E94\u7EF4\u62A4\u53E6\u4E00\u5957\u751F\u6210\u89C4\u5219"
    );
    assert(
      serviceSource.includes("createProductHqSyncJobPoller") && pageSource.includes("createProductHqSyncJobPoller"),
      "\u9875\u9762\u548C\u670D\u52A1\u517C\u5BB9 wrapper \u5E94\u5171\u7528\u5546\u54C1 HQ \u540C\u6B65\u8F6E\u8BE2\u5668"
    );
    assert(
      !pageSource.includes("type HqSyncJobStatus = 'Queued'") && !pageSource.includes("type ProductHqSyncJobResult = HqProductSyncResult &"),
      "\u9875\u9762\u4E0D\u5E94\u955C\u50CF\u670D\u52A1\u5C42\u5DF2\u6709 HQ \u540C\u6B65 job \u7C7B\u578B"
    );
    assert(
      !pageSource.includes("setTimeout(poll, PRODUCT_HQ_SYNC_POLL_INTERVAL_MS)") && !pageSource.includes("async function poll()"),
      "\u9875\u9762\u4E0D\u5E94\u4FDD\u7559\u539F\u59CB setTimeout \u8F6E\u8BE2\u72B6\u6001\u673A"
    );
  });
  if (hqSyncArchitectureFailure) failures.push(hqSyncArchitectureFailure);
  const supplierImagePollingFailure = await runTest("\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u8F6E\u8BE2\u4E0D\u5E94\u628A 404 \u548C\u6743\u9650\u9519\u8BEF\u4F2A\u88C5\u6210 Running", () => {
    assert(
      pageSource.includes("error instanceof RequestError") && pageSource.includes("error.status === 404") && pageSource.includes("error.status === 401") && pageSource.includes("error.status === 403"),
      "\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u8F6E\u8BE2\u5E94\u6309 RequestError.status \u5206\u7C7B\u5904\u7406"
    );
    assert(
      pageSource.includes("clearActiveImageBatchJob(job.localSupplierCode)") && pageSource.includes("batchImageJobMissingTitle") && pageSource.includes("batchImageJobAuthFailedTitle"),
      "404/\u6743\u9650\u9519\u8BEF\u5E94\u6E05\u7406 active job \u5E76\u505C\u6B62\u8F6E\u8BE2\u63D0\u793A\u7528\u6237"
    );
  });
  if (supplierImagePollingFailure) failures.push(supplierImagePollingFailure);
  const supplierImageConcurrentJobFailure = await runTest("\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539\u5E94\u6309\u4F9B\u5E94\u5546\u8DDF\u8E2A active job", () => {
    assert(
      pageSource.includes("type ActiveSupplierImageBatchJobMap") && pageSource.includes("readActiveSupplierImageBatchJobs") && pageSource.includes("saveActiveSupplierImageBatchJobs") && pageSource.includes("activeImageBatchJobs"),
      "\u56FE\u7247\u6279\u91CF\u4FEE\u6539 active job \u5E94\u4FDD\u5B58\u4E3A\u6309\u4F9B\u5E94\u5546\u4EE3\u7801\u7D22\u5F15\u7684 map"
    );
    assert(
      pageSource.includes("stopSupplierImageBatchPolling(job.localSupplierCode)") && pageSource.includes("stopSupplierImageBatchPollingRef.current[jobKey] = poller.stop") && pageSource.includes("clearActiveImageBatchJob(job.localSupplierCode)"),
      "\u8F6E\u8BE2\u542F\u52A8\u3001\u505C\u6B62\u548C\u6E05\u7406\u5E94\u53EA\u4F5C\u7528\u4E8E\u5BF9\u5E94\u4F9B\u5E94\u5546"
    );
    assert(
      pageSource.includes("getActiveImageBatchJobBySupplier(values.localSupplierCode)") && pageSource.includes("showActiveSupplierImageBatchStatus(existingActiveJob)") && !pageSource.includes("const storedActiveJob = activeImageBatchJob ?? readActiveSupplierImageBatchJob()"),
      "\u63D0\u4EA4\u65F6\u53EA\u963B\u6B62\u540C\u4E00\u4F9B\u5E94\u5546\u5DF2\u6709\u4EFB\u52A1\uFF0C\u4E0D\u5E94\u56E0\u4E3A\u5176\u4ED6\u4F9B\u5E94\u5546\u4EFB\u52A1\u800C\u963B\u6B62\u6253\u5F00\u5F39\u7A97"
    );
    assert(
      pageSource.indexOf("imageBatchForm.resetFields()") < pageSource.indexOf("getActiveImageBatchJobBySupplier(values.localSupplierCode)"),
      "\u6253\u5F00\u56FE\u7247\u6279\u91CF\u4FEE\u6539\u5F39\u7A97\u65F6\u5E94\u5141\u8BB8\u5207\u6362\u5230\u5176\u4ED6\u4F9B\u5E94\u5546\uFF0C\u4E0D\u80FD\u5148\u7528\u9ED8\u8BA4\u4F9B\u5E94\u5546 active job \u76F4\u63A5\u62E6\u622A"
    );
    assert(
      pageSource.includes("hbwebSkippedExistingImageCount") && pageSource.includes("hqSkippedExistingImageCount"),
      "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A Hbweb/HQ \u5DF2\u6709\u56FE\u7247\u8DF3\u8FC7\u6570\u91CF"
    );
  });
  if (supplierImageConcurrentJobFailure) failures.push(supplierImageConcurrentJobFailure);
  const existingJobFailure = await runTest("\u5DF2\u6709 active job \u65F6 HQ \u540C\u6B65\u6309\u94AE\u53EA\u5C55\u793A\u72B6\u6001\u4E0D\u65B0\u5EFA\u4EFB\u52A1", () => {
    assert(
      pageSource.includes("const storedActiveJob = activeHqSyncJob ?? readActiveProductHqSyncJob()") && pageSource.includes("showActiveHqSyncJobStatus(storedActiveJob)"),
      "\u6253\u5F00 HQ \u540C\u6B65\u5F39\u7A97\u524D\u5E94\u5148\u5224\u65AD active job \u5E76\u5C55\u793A\u72B6\u6001"
    );
    assert(
      pageSource.includes("hqSyncInProgress"),
      "\u5DF2\u6709\u4EFB\u52A1\u65F6\u6309\u94AE\u5E94\u5C55\u793A\u540C\u6B65\u4E2D\u72B6\u6001"
    );
  });
  if (existingJobFailure) failures.push(existingJobFailure);
  const terminalResultFailure = await runTest("HQ \u540C\u6B65 job \u5B8C\u6210\u3001\u5931\u8D25\u3001Succeeded \u52A0\u9519\u8BEF\u660E\u7EC6\u5E94\u5C55\u793A\u53CB\u597D\u7ED3\u679C", () => {
    assert(
      pageSource.includes("result.status === 'Failed'") && pageSource.includes("hqSyncJobPartialSucceeded") && pageSource.includes("hqSyncJobSucceeded") && pageSource.includes("hqSyncJobTimeout"),
      "\u8F6E\u8BE2\u7EC8\u6001\u5E94\u533A\u5206\u5931\u8D25\u3001\u5B8C\u6210\u3001\u9519\u8BEF\u660E\u7EC6\u90E8\u5206\u6210\u529F\u548C\u8D85\u65F6"
    );
    assert(
      pageSource.includes("HqProductSyncPollingTimeoutError") && pageSource.includes("showPollingTimeout()"),
      "\u8F6E\u8BE2\u8D85\u65F6\u5E94\u7531\u5171\u4EAB poller \u629B\u51FA\u4E13\u95E8\u9519\u8BEF\uFF0C\u5E76\u8D70\u7EDF\u4E00\u8D85\u65F6\u63D0\u793A"
    );
    assert(
      pageSource.includes("incrementalStartDateRequired") && pageSource.includes("allowClear={false}"),
      "\u589E\u91CF\u540C\u6B65\u8D77\u59CB\u65E5\u671F\u5E94\u5FC5\u586B\uFF0C\u907F\u514D\u63D0\u4EA4\u65E0\u8303\u56F4\u7684\u589E\u91CF job"
    );
  });
  if (terminalResultFailure) failures.push(terminalResultFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("ProductManagement.hqSync.logic.test: ok");
}
await main();
