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
  const canViewSalesIntelligence = canViewReports || canViewProductMovementReport;
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
  const canAccessOrderFront = isAdmin || hasPermission(P.OrderFront.View);
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

// src/pages/System/Users/userScope.ts
var SCOPED_MANAGER_FORBIDDEN_ROLE_NAMES = /* @__PURE__ */ new Set([
  "admin",
  "\u7BA1\u7406\u5458",
  "superadmin",
  "\u8D85\u7EA7\u7BA1\u7406\u5458",
  "storemanager",
  "\u5E97\u957F",
  "\u7ECF\u7406",
  "warehousemanager",
  "\u4ED3\u5E93\u7ECF\u7406",
  "\u4ED3\u5E93\u7BA1\u7406\u5458"
]);
function normalizeRoleName(roleName) {
  return roleName?.trim().toLowerCase() ?? "";
}
function isForbiddenRoleForScopedManager(roleName) {
  return SCOPED_MANAGER_FORBIDDEN_ROLE_NAMES.has(normalizeRoleName(roleName));
}
function hasForbiddenRoleForScopedManager(user) {
  return (user.roleNames ?? []).some((roleName) => isForbiddenRoleForScopedManager(roleName));
}
function filterUsersVisibleToScopedManager(users) {
  return users.filter((user) => !hasForbiddenRoleForScopedManager(user));
}
function filterRoleOptionsForScopedManager(roles) {
  return roles.filter((role) => !isForbiddenRoleForScopedManager(role.roleName));
}
function areRoleGuidsAllowedForScopedManager(selectedRoleGuids, availableRoles) {
  const allowedRoleGuidSet = new Set(filterRoleOptionsForScopedManager(availableRoles).map((role) => role.roleGUID));
  return selectedRoleGuids.every((roleGuid) => allowedRoleGuidSet.has(roleGuid));
}
function isScopedStoreManager(currentUser, access) {
  return Boolean(currentUser && access.isStoreLevelManager);
}
function getManagedStores(currentUser, access) {
  if (!isScopedStoreManager(currentUser, access)) {
    return [];
  }
  return (currentUser?.stores ?? []).filter((store) => store.isManageable && Boolean(store.storeGUID));
}
function isStoreVisibleToManager(storeGuid, managedStores) {
  if (!storeGuid) {
    return false;
  }
  return managedStores.some((store) => store.storeGUID === storeGuid);
}
function getScopedStoreGuidsForQuery(selectedStoreGuid, managedStores) {
  if (selectedStoreGuid) {
    if (isStoreVisibleToManager(selectedStoreGuid, managedStores)) {
      return [selectedStoreGuid];
    }
  }
  return managedStores.map((store) => store.storeGUID).filter(Boolean);
}
function mergeUsersByGuid(users) {
  const merged = /* @__PURE__ */ new Map();
  users.forEach((user) => {
    if (!merged.has(user.userGUID)) {
      merged.set(user.userGUID, user);
    }
  });
  return Array.from(merged.values());
}
function filterStoresForManager(stores, managedStores) {
  const managedStoreGuids = new Set(managedStores.map((store) => store.storeGUID));
  return stores.filter((store) => managedStoreGuids.has(store.storeGUID));
}
function buildScopedStoreAssignments(existingStores, selectedManagedStoreGuids, manageableStoreGuids, managedStores) {
  const managedStoreGuids = new Set(managedStores.map((store) => store.storeGUID));
  const manageableStoreGuidSet = new Set(manageableStoreGuids);
  const assignments = /* @__PURE__ */ new Map();
  existingStores.forEach((store) => {
    if (store.storeGUID && !managedStoreGuids.has(store.storeGUID)) {
      assignments.set(store.storeGUID, {
        storeGUID: store.storeGUID,
        accessLevel: "ReadWrite",
        isManageable: store.isManageable
      });
    }
  });
  selectedManagedStoreGuids.forEach((storeGUID) => {
    if (managedStoreGuids.has(storeGUID)) {
      assignments.set(storeGUID, {
        storeGUID,
        accessLevel: "ReadWrite",
        isManageable: manageableStoreGuidSet.has(storeGUID)
      });
    }
  });
  return Array.from(assignments.values());
}

// src/pages/System/Users/userScope.test.ts
function createStore(overrides) {
  return {
    storeGUID: "store-a-guid",
    storeName: "Store A",
    storeCode: "A",
    isManageable: false,
    assignedAt: "2026-01-01T00:00:00Z",
    ...overrides
  };
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
function createUser(overrides) {
  return {
    userGUID: "user-a-guid",
    username: "user-a",
    email: "user-a@example.com",
    isActive: true,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    roleNames: [],
    storeNames: [],
    ...overrides
  };
}
function createRoleOption(overrides) {
  return {
    roleGUID: "role-user-guid",
    roleName: "User",
    description: "",
    isActive: true,
    ...overrides
  };
}
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
var manageableStore = createStore({
  storeGUID: "managed-store-guid",
  storeName: "Managed Store",
  storeCode: "M1",
  isManageable: true
});
var linkedOnlyStore = createStore({
  storeGUID: "linked-store-guid",
  storeName: "Linked Store",
  storeCode: "L1",
  isManageable: false
});
var storeManager = createCurrentUser({
  stores: [manageableStore, linkedOnlyStore]
});
var storeManagerAccess = buildAccess(storeManager);
assertEqual(
  isScopedStoreManager(storeManager, storeManagerAccess),
  true,
  "StoreManager should use scoped user visibility"
);
assertArrayEqual(
  getManagedStores(storeManager, storeManagerAccess).map((store) => store.storeGUID),
  ["managed-store-guid"],
  "StoreManager scope should include only manageable stores"
);
var chineseStoreManager = createCurrentUser({
  roleNames: ["\u5E97\u957F"],
  stores: [manageableStore, linkedOnlyStore]
});
var chineseStoreManagerAccess = buildAccess(chineseStoreManager);
assertEqual(
  isScopedStoreManager(chineseStoreManager, chineseStoreManagerAccess),
  true,
  "Chinese \u5E97\u957F role should use scoped user visibility"
);
assertArrayEqual(
  getManagedStores(chineseStoreManager, chineseStoreManagerAccess).map((store) => store.storeGUID),
  ["managed-store-guid"],
  "Chinese \u5E97\u957F scope should include only manageable stores"
);
var managerAlias = createCurrentUser({
  roleNames: ["\u7ECF\u7406"],
  stores: [manageableStore, linkedOnlyStore]
});
var managerAliasAccess = buildAccess(managerAlias);
assertEqual(
  isScopedStoreManager(managerAlias, managerAliasAccess),
  true,
  "Chinese \u7ECF\u7406 role should use scoped user visibility"
);
assertArrayEqual(
  getManagedStores(managerAlias, managerAliasAccess).map((store) => store.storeGUID),
  ["managed-store-guid"],
  "Chinese \u7ECF\u7406 scope should include only manageable stores"
);
assertEqual(
  isStoreVisibleToManager("managed-store-guid", getManagedStores(storeManager, storeManagerAccess)),
  true,
  "Manageable store should be visible to the manager"
);
assertEqual(
  isStoreVisibleToManager("linked-store-guid", getManagedStores(storeManager, storeManagerAccess)),
  false,
  "Linked-only store should not be visible to the manager"
);
assertArrayEqual(
  getScopedStoreGuidsForQuery(void 0, [
    manageableStore,
    createStore({ storeGUID: "second-managed-store-guid", isManageable: true })
  ]),
  ["managed-store-guid", "second-managed-store-guid"],
  "Scoped query without a selected store should include all manageable stores"
);
assertArrayEqual(
  getScopedStoreGuidsForQuery("second-managed-store-guid", [
    manageableStore,
    createStore({ storeGUID: "second-managed-store-guid", isManageable: true })
  ]),
  ["second-managed-store-guid"],
  "Scoped query with a selected manageable store should query only that store"
);
assertArrayEqual(
  getScopedStoreGuidsForQuery("linked-store-guid", [manageableStore]),
  ["managed-store-guid"],
  "Scoped query should fall back to the first manageable store when selected store is outside scope"
);
var admin = createCurrentUser({
  roleNames: ["Admin", "StoreManager"],
  stores: [manageableStore]
});
var adminAccess = buildAccess(admin);
assertEqual(
  isScopedStoreManager(admin, adminAccess),
  false,
  "Admin should not be limited by store manager scope"
);
var chineseAdminWithManagerAlias = createCurrentUser({
  roleNames: ["\u7BA1\u7406\u5458", "\u7ECF\u7406"],
  stores: [manageableStore]
});
var chineseAdminWithManagerAliasAccess = buildAccess(chineseAdminWithManagerAlias);
assertEqual(
  isScopedStoreManager(chineseAdminWithManagerAlias, chineseAdminWithManagerAliasAccess),
  false,
  "Chinese admin should not be limited by manager alias scope"
);
var warehouseManager = createCurrentUser({
  roleNames: ["WarehouseManager", "StoreManager"],
  stores: [manageableStore]
});
var warehouseManagerAccess = buildAccess(warehouseManager);
assertEqual(
  isScopedStoreManager(warehouseManager, warehouseManagerAccess),
  false,
  "WarehouseManager should not be limited by store manager scope"
);
var chineseWarehouseManagerWithManagerAlias = createCurrentUser({
  roleNames: ["\u4ED3\u5E93\u7ECF\u7406", "\u7ECF\u7406"],
  stores: [manageableStore]
});
var chineseWarehouseManagerWithManagerAliasAccess = buildAccess(chineseWarehouseManagerWithManagerAlias);
assertEqual(
  isScopedStoreManager(chineseWarehouseManagerWithManagerAlias, chineseWarehouseManagerWithManagerAliasAccess),
  false,
  "Chinese warehouse manager should not be limited by manager alias scope"
);
assertArrayEqual(
  mergeUsersByGuid([
    createUser({ userGUID: "user-a-guid", username: "first-a" }),
    createUser({ userGUID: "user-b-guid", username: "user-b" }),
    createUser({ userGUID: "user-a-guid", username: "second-a" })
  ]).map((user) => user.username),
  ["first-a", "user-b"],
  "Merged users should be de-duplicated by userGUID while keeping first occurrence"
);
assertEqual(
  isForbiddenRoleForScopedManager("\u7BA1\u7406\u5458"),
  true,
  "Chinese admin role should be forbidden for scoped store managers"
);
assertEqual(
  isForbiddenRoleForScopedManager("\u4ED3\u5E93\u7BA1\u7406\u5458"),
  true,
  "Chinese warehouse admin alias should be forbidden for scoped store managers"
);
assertEqual(
  isForbiddenRoleForScopedManager("SuperAdmin"),
  true,
  "SuperAdmin role should be forbidden for scoped store managers"
);
assertEqual(
  isForbiddenRoleForScopedManager("\u8D85\u7EA7\u7BA1\u7406\u5458"),
  true,
  "Chinese super administrator role should be forbidden for scoped store managers"
);
assertEqual(
  hasForbiddenRoleForScopedManager(createUser({ roleNames: ["\u6536\u94F6\u5458", "\u5E97\u957F"] })),
  true,
  "Users with manager-level roles should be hidden from scoped store managers"
);
assertEqual(
  hasForbiddenRoleForScopedManager(createUser({ roleNames: ["\u6536\u94F6\u5458"] })),
  false,
  "Ordinary users should remain visible to scoped store managers"
);
assertArrayEqual(
  filterUsersVisibleToScopedManager([
    createUser({ userGUID: "visible-user-guid", username: "visible-user", roleNames: ["\u6536\u94F6\u5458"] }),
    createUser({ userGUID: "admin-user-guid", username: "admin-user", roleNames: ["\u7BA1\u7406\u5458"] }),
    createUser({ userGUID: "warehouse-user-guid", username: "warehouse-user", roleNames: ["\u4ED3\u5E93\u7ECF\u7406"] })
  ]).map((user) => user.userGUID),
  ["visible-user-guid"],
  "Scoped store manager list should exclude users with forbidden roles"
);
var roleOptions = [
  createRoleOption({ roleGUID: "role-user-guid", roleName: "User" }),
  createRoleOption({ roleGUID: "role-admin-guid", roleName: "Admin" }),
  createRoleOption({ roleGUID: "role-store-manager-guid", roleName: "\u5E97\u957F" }),
  createRoleOption({ roleGUID: "role-warehouse-guid", roleName: "\u4ED3\u5E93\u7BA1\u7406\u5458" })
];
assertArrayEqual(
  filterRoleOptionsForScopedManager(roleOptions).map((role) => role.roleGUID),
  ["role-user-guid"],
  "Scoped store manager role options should exclude forbidden roles"
);
assertEqual(
  areRoleGuidsAllowedForScopedManager(["role-user-guid"], roleOptions),
  true,
  "Scoped store manager should be able to assign allowed roles"
);
assertEqual(
  areRoleGuidsAllowedForScopedManager(["role-user-guid", "role-admin-guid"], roleOptions),
  false,
  "Scoped store manager should not be able to assign forbidden roles"
);
assertArrayEqual(
  filterStoresForManager([manageableStore, linkedOnlyStore], [manageableStore]).map((store) => store.storeGUID),
  ["managed-store-guid"],
  "Detail store filtering should keep only manageable stores"
);
assertArrayEqual(
  getManagedStores(createCurrentUser({ stores: [linkedOnlyStore] }), storeManagerAccess),
  [],
  "StoreManager without manageable stores should have an empty visible scope"
);
var hiddenStore = createStore({
  storeGUID: "hidden-store-guid",
  storeName: "Hidden Store",
  storeCode: "H1",
  isManageable: false
});
assertArrayEqual(
  buildScopedStoreAssignments(
    [manageableStore, hiddenStore],
    ["linked-store-guid"],
    ["linked-store-guid"],
    [manageableStore, linkedOnlyStore]
  ),
  [
    { storeGUID: "hidden-store-guid", accessLevel: "ReadWrite", isManageable: false },
    { storeGUID: "linked-store-guid", accessLevel: "ReadWrite", isManageable: true }
  ],
  "Scoped store saves should preserve hidden stores while replacing only managed scope"
);
