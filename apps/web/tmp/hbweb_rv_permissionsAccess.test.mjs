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

// src/pages/System/Permissions/permissionsAccess.ts
function canManageSystemPermissions(access) {
  return access.hasPermission(P.Roles.ManagePermissions);
}

// src/pages/System/Permissions/permissionsAccess.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var readOnlyAccess = buildAccess({
  userGUID: "read-only",
  username: "read-only",
  email: "read-only@example.test",
  permissions: [P.Roles.View],
  roleNames: [],
  storeNames: []
});
assertEqual(
  readOnlyAccess.canReadRole,
  true,
  "Roles.View \u7528\u6237\u5E94\u7EE7\u7EED\u53EF\u8FDB\u5165\u6743\u9650\u53EA\u8BFB\u9875\u9762"
);
assertEqual(
  canManageSystemPermissions(readOnlyAccess),
  false,
  "Roles.View \u7528\u6237\u4E0D\u5E94\u62E5\u6709\u6743\u9650\u9875\u5199\u64CD\u4F5C"
);
var permissionManagerAccess = buildAccess({
  userGUID: "permission-manager",
  username: "permission-manager",
  email: "permission-manager@example.test",
  permissions: [P.Roles.ManagePermissions],
  roleNames: [],
  storeNames: []
});
assertEqual(
  canManageSystemPermissions(permissionManagerAccess),
  true,
  "Roles.ManagePermissions \u7528\u6237\u5E94\u53EF\u6267\u884C\u6743\u9650\u9875\u5199\u64CD\u4F5C"
);
var adminAccess = buildAccess({
  userGUID: "admin",
  username: "admin",
  email: "admin@example.test",
  permissions: [],
  roleNames: ["Admin"],
  storeNames: []
});
assertEqual(
  canManageSystemPermissions(adminAccess),
  true,
  "Admin \u5E94\u7EE7\u7EED\u62E5\u6709\u6743\u9650\u9875\u5199\u64CD\u4F5C"
);
console.log("permissionsAccess.test: ok");
