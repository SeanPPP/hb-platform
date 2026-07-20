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

// src/utils/managedStoreScope.ts
function sortStoreOptionsByName(stores) {
  return [...stores].sort(
    (left, right) => left.label.localeCompare(right.label, "zh-CN", {
      numeric: true,
      sensitivity: "base"
    })
  );
}
function normalizeManagedStoreCodes(managedStoreCodes) {
  if (managedStoreCodes === null || managedStoreCodes === void 0) {
    return null;
  }
  return Array.from(new Set(managedStoreCodes.filter(Boolean)));
}
function shouldSkipScopedStoreQuery(managedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes);
  return Array.isArray(normalized) && normalized.length === 0;
}
function shouldSkipStoreQueryForScope(selectedStoreCode, storeCodes) {
  const normalized = normalizeManagedStoreCodes(storeCodes);
  if (normalized === null) {
    return false;
  }
  if (!selectedStoreCode || normalized.length === 0) {
    return true;
  }
  return !normalized.includes(selectedStoreCode);
}
function filterStoreOptionsByManagedCodes(stores, managedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes);
  if (normalized === null) {
    return stores;
  }
  const managedStoreCodeSet = new Set(normalized);
  return stores.filter((store) => managedStoreCodeSet.has(store.value));
}
function buildStoreOptionsFromUserStores(stores, options = {}) {
  const seenStoreCodes = /* @__PURE__ */ new Set();
  const storeOptions = (stores ?? []).filter((store) => !options.manageableOnly || store.isManageable).filter((store) => {
    if (!store.storeCode || seenStoreCodes.has(store.storeCode)) {
      return false;
    }
    seenStoreCodes.add(store.storeCode);
    return true;
  }).map((store) => ({
    label: store.storeName || store.storeCode,
    value: store.storeCode
  }));
  return sortStoreOptionsByName(storeOptions);
}
function buildScopedStoreCodeFilter(selectedStoreCode, managedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes);
  if (selectedStoreCode) {
    if (normalized === null || normalized.includes(selectedStoreCode)) {
      return { filterType: "text", type: "equals", filter: selectedStoreCode };
    }
    return { filterType: "set", values: [] };
  }
  if (normalized === null) {
    return void 0;
  }
  if (normalized.length === 1) {
    return { filterType: "text", type: "equals", filter: normalized[0] };
  }
  if (normalized.length > 1) {
    return { filterType: "set", values: normalized };
  }
  return void 0;
}
function isStoreCodeInManagedScope(storeCode, managedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes);
  if (normalized === null) {
    return true;
  }
  if (!storeCode) {
    return false;
  }
  return normalized.includes(storeCode);
}

// src/utils/managedStoreScope.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertArrayEqual(actual, expected, message) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`);
  }
}
function createCurrentUser(overrides = {}) {
  return {
    userGUID: "current-user-guid",
    username: "storemanager",
    email: "storemanager@example.com",
    permissions: [],
    roleNames: ["StoreManager"],
    storeNames: [],
    stores: [],
    ...overrides
  };
}
var allStoreOptions = [
  { label: "Managed One", value: "M1" },
  { label: "Linked One", value: "L1" },
  { label: "Managed Two", value: "M2" }
];
var scopedAccess = buildAccess(createCurrentUser({
  stores: [
    {
      storeGUID: "managed-one-guid",
      storeName: "Managed One",
      storeCode: "M1",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "linked-one-guid",
      storeName: "Linked One",
      storeCode: "L1",
      isManageable: false,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "managed-two-guid",
      storeName: "Managed Two",
      storeCode: "M2",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    }
  ]
}));
assertArrayEqual(
  scopedAccess.managedStoreCodes() ?? [],
  ["M1", "M2"],
  "Managed store codes should include only manageable stores"
);
assertArrayEqual(
  scopedAccess.visibleStoreCodes() ?? [],
  ["M1", "L1", "M2"],
  "Visible store codes should include all linked stores"
);
assertArrayEqual(
  buildStoreOptionsFromUserStores(scopedAccess.visibleStoreCodes() === null ? void 0 : [
    {
      storeGUID: "managed-one-guid",
      storeName: "Managed One",
      storeCode: "M1",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "linked-one-guid",
      storeName: "Linked One",
      storeCode: "L1",
      isManageable: false,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "managed-two-guid",
      storeName: "Managed Two",
      storeCode: "M2",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    }
  ]).map((store) => store.value),
  ["L1", "M1", "M2"],
  "Visible store options should include linked and manageable stores sorted by name"
);
assertArrayEqual(
  buildStoreOptionsFromUserStores([
    {
      storeGUID: "managed-one-guid",
      storeName: "Managed One",
      storeCode: "M1",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "linked-one-guid",
      storeName: "Linked One",
      storeCode: "L1",
      isManageable: false,
      assignedAt: "2026-01-01T00:00:00Z"
    },
    {
      storeGUID: "managed-two-guid",
      storeName: "Managed Two",
      storeCode: "M2",
      isManageable: true,
      assignedAt: "2026-01-01T00:00:00Z"
    }
  ], { manageableOnly: true }).map((store) => store.value),
  ["M1", "M2"],
  "Editable store options should include only manageable stores sorted by name"
);
assertArrayEqual(
  filterStoreOptionsByManagedCodes(allStoreOptions, ["M1", "M2"]).map((store) => store.value),
  ["M1", "M2"],
  "Scoped store options should include only managed store codes"
);
assertArrayEqual(
  filterStoreOptionsByManagedCodes(allStoreOptions, null).map((store) => store.value),
  ["M1", "L1", "M2"],
  "Unscoped store options should include all stores"
);
assertDeepEqual(
  buildScopedStoreCodeFilter("M1", ["M1", "M2"]),
  { filterType: "text", type: "equals", filter: "M1" },
  "Selected managed store should generate equals filter"
);
assertDeepEqual(
  buildScopedStoreCodeFilter(void 0, ["M1", "M2"]),
  { filterType: "set", values: ["M1", "M2"] },
  "Multiple managed stores should generate set filter"
);
assertDeepEqual(
  buildScopedStoreCodeFilter("L1", ["M1", "M2"]),
  { filterType: "set", values: [] },
  "Out-of-scope selected store should generate no-match filter"
);
assertEqual(
  buildScopedStoreCodeFilter(void 0, null),
  void 0,
  "Unscoped users should not get a store filter"
);
assertEqual(
  shouldSkipScopedStoreQuery([]),
  true,
  "Empty managed store scope should skip scoped queries"
);
assertEqual(
  shouldSkipStoreQueryForScope(void 0, ["M1", "L1"]),
  true,
  "Scoped visible queries should be skipped until a visible store is selected"
);
assertEqual(
  shouldSkipStoreQueryForScope("M1", ["M1", "L1"]),
  false,
  "Scoped visible queries should run for selected visible stores"
);
assertEqual(
  shouldSkipStoreQueryForScope("X1", ["M1", "L1"]),
  true,
  "Scoped visible queries should skip out-of-scope selected stores"
);
assertEqual(
  shouldSkipStoreQueryForScope(void 0, null),
  false,
  "Unscoped users may run all-store queries without selecting a store"
);
assertEqual(
  isStoreCodeInManagedScope("M2", ["M1", "M2"]),
  true,
  "Managed store record should be inside scope"
);
assertEqual(
  isStoreCodeInManagedScope("L1", ["M1", "M2"]),
  false,
  "Linked-only store record should be outside managed scope"
);
