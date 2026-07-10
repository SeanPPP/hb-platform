var __defProp = Object.defineProperty;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __esm = (fn, res) => function __init() {
  return fn && (res = (0, fn[__getOwnPropNames(fn)[0]])(fn = 0)), res;
};
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};

// src/types/permissions.ts
var permissions_exports = {};
__export(permissions_exports, {
  ALL_PERMISSIONS: () => ALL_PERMISSIONS,
  P: () => P
});
var P, ALL_PERMISSIONS;
var init_permissions = __esm({
  "src/types/permissions.ts"() {
    P = {
      Users: {
        View: "Users.View",
        Create: "Users.Create",
        Edit: "Users.Edit",
        Delete: "Users.Delete",
        ManageRoles: "Users.ManageRoles",
        ManageStores: "Users.ManageStores",
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
    ALL_PERMISSIONS = Object.values(P).flatMap(
      (group) => Object.values(group)
    );
  }
});

// src/utils/access.ts
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
  const isAdmin = hasRole("Admin") || hasRole("\u7BA1\u7406\u5458");
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
var PERMISSION_ALIAS_GROUPS, permissionAliasMap;
var init_access = __esm({
  "src/utils/access.ts"() {
    init_permissions();
    PERMISSION_ALIAS_GROUPS = [
      {
        canonicalCode: P.LocalPurchase.View,
        aliasCodes: ["LocalInvocie.View"]
      },
      {
        canonicalCode: P.LocalPurchase.Edit,
        aliasCodes: ["LocalInvocie.Edit"]
      }
    ];
    permissionAliasMap = /* @__PURE__ */ new Map();
    PERMISSION_ALIAS_GROUPS.forEach(({ canonicalCode, aliasCodes }) => {
      const codes = [canonicalCode, ...aliasCodes];
      const uniqueCodes = Array.from(new Set(codes.map((code) => code.toLowerCase())));
      codes.forEach((code) => {
        permissionAliasMap.set(code.toLowerCase(), uniqueCodes);
      });
    });
  }
});

// src/utils/roleMenuPreview.ts
var roleMenuPreview_exports = {};
__export(roleMenuPreview_exports, {
  applyRolePermissionMutation: () => applyRolePermissionMutation,
  buildRolePreviewAccess: () => buildRolePreviewAccess,
  isImplicitAllRole: () => isImplicitAllRole
});
function isImplicitAllRole(permissionState) {
  return permissionState.isSuperAdmin || permissionState.implicitAllPermissions || IMPLICIT_ALL_ROLE_NAMES.has(permissionState.roleName.trim().toLowerCase());
}
function applyRolePermissionMutation({
  currentPermissionCodes,
  addPermissionCodes = [],
  removePermissionCodes = []
}) {
  const nextCodes = currentPermissionCodes.filter((code) => !removePermissionCodes.includes(code));
  const nextCodeSet = new Set(nextCodes);
  addPermissionCodes.forEach((code) => {
    if (!nextCodeSet.has(code)) {
      nextCodes.push(code);
      nextCodeSet.add(code);
    }
  });
  return nextCodes;
}
function buildRolePreviewAccess(permissionState) {
  const roleNames = isImplicitAllRole(permissionState) ? ["Admin", permissionState.roleName] : [permissionState.roleName];
  const previewUser = {
    userGUID: `role-preview-${permissionState.roleGuid}`,
    username: permissionState.roleName,
    email: "",
    permissions: permissionState.effectivePermissionCodes,
    roleNames,
    storeNames: []
  };
  return buildAccess(previewUser);
}
var IMPLICIT_ALL_ROLE_NAMES;
var init_roleMenuPreview = __esm({
  "src/utils/roleMenuPreview.ts"() {
    init_access();
    IMPLICIT_ALL_ROLE_NAMES = /* @__PURE__ */ new Set(["admin", "\u7BA1\u7406\u5458"]);
  }
});

// src/utils/webMenuPreview.ts
var webMenuPreview_exports = {};
__export(webMenuPreview_exports, {
  buildWebRoleMenuPreview: () => buildWebRoleMenuPreview,
  filterWebMenuNodesByVisibility: () => filterWebMenuNodesByVisibility,
  getAccessKeyPermissionCodes: () => getAccessKeyPermissionCodes
});
function isWarehouseStaffNavigationLimited(access) {
  return access.isWarehouseStaff && !access.isAdmin && !access.isWarehouseManager && (access.hasRole("WarehouseStaff") || access.hasRole("\u4ED3\u5E93\u5458\u5DE5"));
}
function canAccessRoute(route, access) {
  const accessKey = route.accessKey;
  if (!accessKey) {
    return true;
  }
  return access[accessKey] === true;
}
function buildAddPermissionCodes(route, permissionCodes, explicitPermissionCodeSet) {
  if (!route.accessKey || !permissionCodes.length) {
    return [];
  }
  const primaryPermissionCode = permissionCodes[0];
  const nextCodes = [primaryPermissionCode];
  if (route.path !== "/dashboard" && !explicitPermissionCodeSet.has(P.Dashboard.View)) {
    nextCodes.push(P.Dashboard.View);
  }
  return nextCodes;
}
function buildRemovePermissionCodes(route, permissionCodes) {
  if (route.path === "/system/wpf-versions" && permissionCodes.length) {
    return [permissionCodes[0]];
  }
  return permissionCodes;
}
function buildPreviewNodes(routes, access, t, options) {
  const explicitPermissionCodeSet = new Set(options.explicitPermissionCodes ?? []);
  const limitWarehouseStaffNavigation = isWarehouseStaffNavigationLimited(access);
  return routes.flatMap((route) => {
    if (limitWarehouseStaffNavigation && !warehouseStaffVisibleMenuPaths.has(route.path)) {
      return [];
    }
    const children = route.children ? buildPreviewNodes(route.children, access, t, options) : void 0;
    const hasChildren = Boolean(children?.length);
    const hasSelfAccess = canAccessRoute(route, access);
    const hasVisibleChildren = Boolean(children?.some((child) => child.visible));
    const visible = route.children ? hasVisibleChildren : hasSelfAccess;
    if (!options.includeHidden && !visible) {
      return [];
    }
    if (options.includeHidden && !hasSelfAccess && !hasChildren && !route.accessKey) {
      return [];
    }
    const permissionCodes = route.accessKey ? getAccessKeyPermissionCodes(route.accessKey) : [];
    const addPermissionCodes = buildAddPermissionCodes(route, permissionCodes, explicitPermissionCodeSet);
    const removePermissionCodes = buildRemovePermissionCodes(route, permissionCodes);
    const isEditableRoute = permissionCodes.length > 0;
    const isReadOnly = Boolean(options.readOnly);
    const node = {
      key: route.path,
      title: t(route.title, route.title),
      path: route.path,
      permissionCodes,
      visible,
      edit: {
        canAdd: !visible && isEditableRoute && !isReadOnly,
        canRemove: hasSelfAccess && isEditableRoute && !isReadOnly,
        isReadOnly,
        isFixed: false,
        addPermissionCodes,
        removePermissionCodes,
        reason: isReadOnly ? "read-only" : !isEditableRoute ? "group" : void 0
      }
    };
    if (route.accessKey) {
      node.accessKey = route.accessKey;
    }
    if (children?.length) {
      node.children = children;
    }
    return [node];
  });
}
function getAccessKeyPermissionCodes(accessKey) {
  return accessKeyPermissionMap[accessKey] ?? [];
}
function buildWebRoleMenuPreview(access, t, options = {}) {
  return buildPreviewNodes(webMenuPreviewRoutes, access, t, options);
}
function filterWebMenuNodesByVisibility(nodes, filter) {
  if (filter === "all") {
    return nodes;
  }
  const expectedVisible = filter === "visible";
  return nodes.flatMap((node) => {
    const children = node.children ? filterWebMenuNodesByVisibility(node.children, filter) : void 0;
    if (node.visible !== expectedVisible && !children?.length) {
      return [];
    }
    const { children: _children, ...nodeWithoutChildren } = node;
    const nextNode = { ...nodeWithoutChildren };
    if (children?.length) {
      nextNode.children = children;
    }
    return [nextNode];
  });
}
var accessKeyPermissionMap, webMenuPreviewRoutes, warehouseStaffVisibleMenuPaths;
var init_webMenuPreview = __esm({
  "src/utils/webMenuPreview.ts"() {
    init_permissions();
    accessKeyPermissionMap = {
      isAdmin: [],
      canAccessDashboard: [P.Dashboard.View],
      canAccessOrderFront: [P.OrderFront.View],
      canReadStore: [P.Stores.View],
      canViewEmployeeProfiles: [P.EmployeeProfiles.View],
      canViewSystemLogs: [P.System.ViewLogs],
      canManageScheduledTasks: [P.System.ManageScheduledTasks],
      canManageSystemSettings: [P.System.ManageSettings],
      canViewAppDownloads: [P.System.ViewAppDownloads, P.System.ManageAppDownloads],
      canReadUser: [P.Users.View],
      canReadRole: [P.Roles.View],
      canViewDeviceRegistration: [P.DeviceRegistration.View, P.DeviceRegistration.Manage],
      canManageDomesticSuppliers: [P.DomesticPurchase.ManageSuppliers],
      canReadProduct: [P.Products.View],
      canManageDomesticPrefixCodes: [P.DomesticPurchase.ManagePrefixCodes],
      canManageDomesticProducts: [P.DomesticPurchase.ManageProducts],
      canManageWarehouseOrders: [P.Warehouse.ManageOrders, P.Warehouse.Manage],
      canManageStoreOrderImportPriceVariance: [P.Warehouse.ManageOrders, P.Warehouse.Manage],
      canViewContainers: [P.Container.View],
      canManageWarehouseProducts: [P.Warehouse.ManageProducts, P.Warehouse.Manage],
      canManageWarehouseCategories: [P.Warehouse.ManageCategories, P.Warehouse.Manage],
      canManageWarehouseLocations: [P.Warehouse.ManageLocations, P.Warehouse.Manage],
      canViewReports: [P.Reports.View],
      canViewSalesIntelligence: [P.Reports.View, P.Reports.ProductMovementView],
      canViewProductMovementReport: [P.Reports.ProductMovementView, P.Reports.View],
      canViewAustralianSuppliers: [P.AustralianSuppliers.View],
      canViewPosProducts: [P.PosProducts.View, P.PosProducts.Manage],
      canManageStoreProducts: [P.StoreProducts.View],
      canManagePricing: [P.PricingStrategy.View],
      canManagePromotions: [P.Promotions.View],
      canManageAdvertisements: [P.Advertisements.View],
      canViewAttendanceSchedule: [P.Attendance.AdminView, P.Attendance.ScheduleViewStore],
      canManageStoreOps: [P.Store.ManageOperations],
      canReadOrder: [P.Orders.View],
      canManageLocalPurchase: [P.LocalPurchase.View, "LocalInvocie.View"],
      canEditLocalPurchase: [P.LocalPurchase.Edit, "LocalInvocie.Edit"]
    };
    webMenuPreviewRoutes = [
      { path: "/dashboard", title: "menu.dashboard", accessKey: "canAccessDashboard" },
      {
        path: "/system",
        title: "menu.system",
        children: [
          { path: "/system/stores", title: "menu.systemStores", accessKey: "canReadStore" },
          { path: "/system/employee-profiles", title: "menu.systemEmployeeProfiles", accessKey: "canViewEmployeeProfiles" },
          { path: "/system/center-logs", title: "menu.systemCenterLogs", accessKey: "canViewSystemLogs" },
          { path: "/system/scheduled-statistics", title: "menu.scheduledStatistics", accessKey: "canManageScheduledTasks" },
          { path: "/system/invoice-email-settings", title: "menu.invoiceEmailSettings", accessKey: "canManageSystemSettings" },
          { path: "/system/payment-terminal-settings", title: "menu.paymentTerminalSettings", accessKey: "canManageSystemSettings" },
          { path: "/system/users", title: "menu.systemUsers", accessKey: "canReadUser" },
          { path: "/system/roles", title: "menu.systemRoles", accessKey: "canReadRole" },
          { path: "/system/permissions", title: "menu.systemPermissions", accessKey: "canReadRole" },
          { path: "/system/device-registration", title: "menu.deviceRegistration", accessKey: "canViewDeviceRegistration" },
          { path: "/system/app-downloads", title: "menu.appDownloads", accessKey: "canViewAppDownloads" },
          { path: "/system/wpf-versions", title: "menu.wpfVersions", accessKey: "canViewAppDownloads" }
        ]
      },
      {
        path: "/domestic-purchase",
        title: "menu.domesticPurchase",
        children: [
          { path: "/domestic-purchase/china-suppliers", title: "menu.chinaSuppliers", accessKey: "canManageDomesticSuppliers" },
          { path: "/domestic-purchase/domestic-products", title: "menu.domesticProducts", accessKey: "canReadProduct" },
          { path: "/domestic-purchase/prefix-code-management", title: "menu.prefixCodeManagement", accessKey: "canManageDomesticPrefixCodes" },
          { path: "/domestic-purchase/product-creation", title: "menu.productCreation", accessKey: "canManageDomesticProducts" },
          { path: "/domestic-purchase/product-import", title: "menu.productImport", accessKey: "canManageDomesticProducts" }
        ]
      },
      {
        path: "/warehouse",
        title: "menu.warehouse",
        children: [
          { path: "/warehouse/store-orders", title: "menu.storeOrders", accessKey: "canManageWarehouseOrders" },
          { path: "/warehouse/store-order-import-price-variance", title: "menu.storeOrderImportPriceVariance", accessKey: "canManageStoreOrderImportPriceVariance" },
          { path: "/warehouse/containers", title: "menu.containers", accessKey: "canViewContainers" },
          { path: "/warehouse/products", title: "menu.warehouseProducts", accessKey: "canManageWarehouseProducts" },
          { path: "/warehouse/categories", title: "menu.categories", accessKey: "canManageWarehouseCategories" },
          { path: "/warehouse/locations", title: "menu.warehouseLocations", accessKey: "canManageWarehouseLocations" },
          { path: "/warehouse/product-grade-management", title: "menu.productGradeManagement", accessKey: "canManageWarehouseProducts" }
        ]
      },
      {
        path: "/executive-sales-intelligence",
        title: "menu.executiveSalesIntelligence",
        accessKey: "canViewSalesIntelligence",
        children: [
          { path: "/executive-sales-intelligence/overview", title: "menu.salesData", accessKey: "canViewReports" },
          { path: "/executive-sales-intelligence/sales-detail-v2", title: "menu.salesDetail", accessKey: "canViewReports" },
          { path: "/executive-sales-intelligence/product-movement-report", title: "menu.productMovementReport", accessKey: "canViewProductMovementReport" }
        ]
      },
      {
        path: "/pos-admin",
        title: "menu.posAdmin",
        children: [
          { path: "/pos-admin/suppliers", title: "menu.suppliers", accessKey: "canViewAustralianSuppliers" },
          { path: "/pos-admin/products", title: "menu.productManagement", accessKey: "canViewPosProducts" },
          { path: "/pos-admin/store-product-price", title: "menu.storeProductPrice", accessKey: "canManageStoreProducts" },
          { path: "/pos-admin/pricing-strategies", title: "menu.pricingStrategies", accessKey: "canManagePricing" },
          { path: "/pos-admin/promotions", title: "menu.promotions", accessKey: "canManagePromotions" },
          { path: "/pos-admin/advertisements", title: "menu.advertisements", accessKey: "canManageAdvertisements" },
          { path: "/pos-admin/schedule-attendance", title: "menu.scheduleAttendance", accessKey: "canViewAttendanceSchedule" },
          { path: "/pos-admin/cash-register-users", title: "menu.cashRegisterUsers", accessKey: "canManageStoreOps" },
          { path: "/pos-admin/sales-orders", title: "menu.salesOrders", accessKey: "canReadOrder" },
          { path: "/pos-admin/local-supplier-invoices", title: "menu.localSupplierInvoices", accessKey: "canManageLocalPurchase" }
        ]
      }
    ];
    warehouseStaffVisibleMenuPaths = /* @__PURE__ */ new Set([
      "/warehouse",
      "/warehouse/store-orders"
    ]);
  }
});

// src/router/wpfVersionsRoute.test.ts
import { readFileSync } from "node:fs";
import { join } from "node:path";
var storage = /* @__PURE__ */ new Map();
Object.defineProperty(globalThis, "localStorage", {
  value: {
    getItem: (key) => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value),
    removeItem: (key) => storage.delete(key)
  },
  configurable: true
});
var { buildRolePreviewAccess: buildRolePreviewAccess2 } = await Promise.resolve().then(() => (init_roleMenuPreview(), roleMenuPreview_exports));
var { buildWebRoleMenuPreview: buildWebRoleMenuPreview2, getAccessKeyPermissionCodes: getAccessKeyPermissionCodes2 } = await Promise.resolve().then(() => (init_webMenuPreview(), webMenuPreview_exports));
var { P: P2 } = await Promise.resolve().then(() => (init_permissions(), permissions_exports));
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function findNode(nodes, path) {
  for (const node of nodes) {
    if (node.path === path) {
      return node;
    }
    const child = node.children ? findNode(node.children, path) : void 0;
    if (child) {
      return child;
    }
  }
  return void 0;
}
var translate = (key, fallback) => fallback ?? key;
var viewAccess = buildRolePreviewAccess2({
  roleGuid: "wpf-view-role",
  roleName: "WpfViewRole",
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [P2.System.ViewAppDownloads],
  effectivePermissionCodes: [P2.System.ViewAppDownloads]
});
var noWpfAccess = buildRolePreviewAccess2({
  roleGuid: "wpf-hidden-role",
  roleName: "WpfHiddenRole",
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [],
  effectivePermissionCodes: []
});
var routeSource = readFileSync(join(process.cwd(), "src/router/routes.tsx"), "utf8");
assertEqual(
  routeSource.includes("import SystemWpfVersionsPage from '../pages/System/WpfVersions'"),
  true,
  "Routes should import the WPF versions page"
);
assertEqual(
  routeSource.includes("path: '/system/wpf-versions'") && routeSource.includes("title: 'menu.wpfVersions'") && routeSource.includes("accessKey: 'canViewAppDownloads'") && routeSource.includes("element: <SystemWpfVersionsPage />"),
  true,
  "WPF versions route should be registered with reused App Downloads view permission"
);
assertEqual(
  getAccessKeyPermissionCodes2("canViewAppDownloads").join(","),
  `${P2.System.ViewAppDownloads},${P2.System.ManageAppDownloads}`,
  "WPF versions menu should document view and manage permissions accepted by the route"
);
var preview = buildWebRoleMenuPreview2(viewAccess, translate, { includeHidden: true });
var wpfVersionsMenu = findNode(preview, "/system/wpf-versions");
assertEqual(Boolean(wpfVersionsMenu), true, "Web role preview should include the WPF versions menu");
assertEqual(
  wpfVersionsMenu?.permissionCodes.join(","),
  `${P2.System.ViewAppDownloads},${P2.System.ManageAppDownloads}`,
  "WPF versions menu preview should display both accepted WPF version permissions"
);
var hiddenPreview = buildWebRoleMenuPreview2(noWpfAccess, translate, {
  includeHidden: true,
  explicitPermissionCodes: []
});
var hiddenWpfVersionsMenu = findNode(hiddenPreview, "/system/wpf-versions");
assertEqual(
  hiddenWpfVersionsMenu?.edit.addPermissionCodes.includes(P2.System.ViewAppDownloads),
  true,
  "Adding the hidden WPF versions menu should grant only the view permission"
);
assertEqual(
  hiddenWpfVersionsMenu?.edit.addPermissionCodes.includes(P2.System.ManageAppDownloads),
  false,
  "Adding the hidden WPF versions menu should not promote the role to manage permission"
);
assertEqual(
  wpfVersionsMenu?.edit.removePermissionCodes.join(","),
  P2.System.ViewAppDownloads,
  "Removing the WPF versions menu should not delete manage permission together with view permission"
);
console.log("wpfVersionsRoute.test: ok");
