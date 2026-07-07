// src/types/permissions.ts
var P = {
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

// src/utils/expoRoleMenuPreview.ts
var MAX_VISIBLE_TABS = 4;
var STORE_ROUTE_NAMES = /* @__PURE__ */ new Set([
  "home",
  "orders",
  "cart",
  "product-query",
  "local-supplier-invoices",
  "installment-orders",
  "store-vouchers"
]);
var ATTENDANCE_PERSONAL_PERMISSION_CODES = [
  P.Attendance.ScheduleViewSelf,
  P.Attendance.AvailabilitySubmitSelf,
  P.Attendance.PunchSelf,
  P.Attendance.LeaveApplySelf
];
var ATTENDANCE_MANAGEMENT_PERMISSION_CODES = [
  P.Attendance.ScheduleViewStore,
  P.Attendance.ScheduleEditManagedStore,
  P.Attendance.AvailabilityViewManagedStore,
  P.Attendance.PunchViewManagedStore,
  P.Attendance.ApprovalViewManagedStore,
  P.Attendance.ApprovalReviewManagedStore,
  P.Attendance.HolidayViewStore,
  P.Attendance.HolidayEditManagedStore,
  P.Attendance.LeaveViewManagedStore,
  P.Attendance.LeaveReviewManagedStore,
  P.Attendance.SettingsEdit,
  P.Attendance.AdminView
];
var TAB_PATHS = {
  home: "/(tabs)/home",
  orders: "/(tabs)/orders",
  cart: "/(tabs)/cart",
  warehouse: "/(tabs)/warehouse",
  "domestic-purchase": "/(tabs)/domestic-purchase",
  "local-supplier-invoices": "/(tabs)/local-supplier-invoices",
  "installment-orders": "/(tabs)/installment-orders",
  "store-vouchers": "/(tabs)/store-vouchers",
  "attendance-personal": "/(tabs)/attendance-personal",
  "attendance-management": "/(tabs)/attendance-management",
  "product-query": "/(tabs)/product-query",
  users: "/(tabs)/users",
  "employee-profile": "/(tabs)/employee-profile",
  "device-management": "/(tabs)/device-management",
  settings: "/(tabs)/settings"
};
var ROUTE_LABELS = {
  home: { zhTitle: "\u5546\u54C1", enTitle: "Home" },
  orders: { zhTitle: "\u8BA2\u5355", enTitle: "Orders" },
  cart: { zhTitle: "\u8D2D\u7269\u8F66", enTitle: "Cart" },
  warehouse: { zhTitle: "\u4ED3\u5E93", enTitle: "Warehouse" },
  "domestic-purchase": { zhTitle: "\u4E2D\u56FD\u91C7\u8D2D", enTitle: "China Purchase" },
  "local-supplier-invoices": { zhTitle: "\u6FB3\u6D32\u8FDB\u8D27", enTitle: "AU Invoices" },
  "installment-orders": { zhTitle: "\u5206\u671F\u8BA2\u5355", enTitle: "Installments" },
  "store-vouchers": { zhTitle: "\u95E8\u5E97\u4EE3\u91D1\u5238", enTitle: "Vouchers" },
  "attendance-personal": { zhTitle: "\u8003\u52E4", enTitle: "Attendance" },
  "attendance-management": { zhTitle: "\u8003\u52E4\u7BA1\u7406", enTitle: "Attendance Management" },
  "product-query": { zhTitle: "\u5546\u54C1\u7EF4\u62A4", enTitle: "Products" },
  users: { zhTitle: "\u7528\u6237", enTitle: "Users" },
  "employee-profile": { zhTitle: "\u5458\u5DE5", enTitle: "Employee" },
  "device-management": { zhTitle: "\u8BBE\u5907\u7BA1\u7406", enTitle: "Devices" },
  settings: { zhTitle: "\u8BBE\u7F6E", enTitle: "Settings" }
};
var EXPO_APP_MENU_DEFINITIONS = [
  {
    routeName: "home",
    titleKey: "tabs.home",
    icon: "home",
    permissionCodes: [P.Orders.Create],
    order: 10,
    ...ROUTE_LABELS.home
  },
  {
    routeName: "orders",
    titleKey: "tabs.orders",
    icon: "clipboard-list",
    permissionCodes: [P.Orders.View],
    order: 20,
    ...ROUTE_LABELS.orders
  },
  {
    routeName: "cart",
    titleKey: "tabs.cart",
    icon: "cart-outline",
    permissionCodes: [P.Orders.Create],
    order: 30,
    ...ROUTE_LABELS.cart
  },
  {
    routeName: "warehouse",
    titleKey: "tabs.warehouse",
    icon: "warehouse",
    permissionCodes: [P.Warehouse.ManageProducts],
    order: 40,
    ...ROUTE_LABELS.warehouse
  },
  {
    routeName: "domestic-purchase",
    titleKey: "tabs.domesticPurchase",
    icon: "shopping-outline",
    permissionCodes: [P.DomesticPurchase.ManageProducts],
    order: 45,
    ...ROUTE_LABELS["domestic-purchase"]
  },
  {
    routeName: "local-supplier-invoices",
    titleKey: "tabs.localSupplierInvoices",
    icon: "receipt-text-outline",
    permissionCodes: [P.LocalPurchase.View, "LocalInvocie.View"],
    order: 46,
    ...ROUTE_LABELS["local-supplier-invoices"]
  },
  {
    routeName: "product-query",
    titleKey: "tabs.productQuery",
    icon: "barcode-scan",
    permissionCodes: [P.StoreProducts.View],
    order: 50,
    ...ROUTE_LABELS["product-query"]
  },
  {
    routeName: "installment-orders",
    titleKey: "tabs.installmentOrders",
    icon: "cash-clock",
    permissionCodes: [P.InstallmentOrders.View],
    order: 51,
    ...ROUTE_LABELS["installment-orders"]
  },
  {
    routeName: "store-vouchers",
    titleKey: "tabs.storeVouchers",
    icon: "ticket-percent-outline",
    permissionCodes: [P.StoreVouchers.View],
    order: 52,
    ...ROUTE_LABELS["store-vouchers"]
  },
  {
    routeName: "attendance-personal",
    titleKey: "tabs.attendancePersonal",
    icon: "account-clock-outline",
    permissionCodes: ATTENDANCE_PERSONAL_PERMISSION_CODES,
    order: 55,
    ...ROUTE_LABELS["attendance-personal"]
  },
  {
    routeName: "attendance-management",
    titleKey: "tabs.attendanceManagement",
    icon: "calendar-clock",
    permissionCodes: ATTENDANCE_MANAGEMENT_PERMISSION_CODES,
    order: 56,
    ...ROUTE_LABELS["attendance-management"]
  },
  {
    routeName: "users",
    titleKey: "tabs.users",
    icon: "account-group-outline",
    permissionCodes: [P.Users.View],
    order: 57,
    ...ROUTE_LABELS.users
  },
  {
    routeName: "employee-profile",
    titleKey: "tabs.employeeProfile",
    icon: "card-account-details-outline",
    permissionCodes: [P.EmployeeProfiles.View],
    order: 58,
    ...ROUTE_LABELS["employee-profile"]
  },
  {
    routeName: "device-management",
    titleKey: "tabs.deviceManagement",
    icon: "cellphone-cog",
    permissionCodes: [P.DeviceRegistration.View],
    order: 59,
    ...ROUTE_LABELS["device-management"]
  },
  {
    routeName: "settings",
    titleKey: "tabs.settings",
    icon: "account-circle-outline",
    permissionCodes: [],
    order: 60,
    fixed: true,
    ...ROUTE_LABELS.settings
  }
];
function buildDisplayTabs(visibleRoutes) {
  if (visibleRoutes.length <= MAX_VISIBLE_TABS) {
    return visibleRoutes.map((route) => ({
      type: "route",
      key: route.routeName,
      route
    }));
  }
  const storeChildren = visibleRoutes.filter((route) => STORE_ROUTE_NAMES.has(route.routeName));
  if (!storeChildren.length) {
    return visibleRoutes.map((route) => ({
      type: "route",
      key: route.routeName,
      route
    }));
  }
  let hasInsertedStore = false;
  const tabs = [];
  visibleRoutes.forEach((route) => {
    if (!STORE_ROUTE_NAMES.has(route.routeName)) {
      tabs.push({ type: "route", key: route.routeName, route });
      return;
    }
    if (hasInsertedStore) {
      return;
    }
    hasInsertedStore = true;
    tabs.push({
      type: "store",
      key: "store",
      zhTitle: "\u95E8\u5E97",
      enTitle: "Store",
      children: storeChildren
    });
  });
  return tabs;
}
function buildExpoRoute(definition, access, explicitPermissionCodeSet, readOnly) {
  const anyPermission = definition.permissionCodes.length > 1;
  const visible = definition.fixed || definition.permissionCodes.length === 0 || definition.permissionCodes.some((permissionCode) => access.hasPermission(permissionCode));
  return {
    ...definition,
    path: TAB_PATHS[definition.routeName],
    visible,
    anyPermission,
    readOnly,
    locked: Boolean(definition.fixed || readOnly),
    addPermissionCodes: !visible && definition.permissionCodes.length > 0 ? [definition.permissionCodes.find((permissionCode) => !explicitPermissionCodeSet.has(permissionCode)) ?? definition.permissionCodes[0]] : [],
    removePermissionCodes: definition.permissionCodes
  };
}
function buildExpoRoleMenuPreview(access, _t, options = {}) {
  const explicitPermissionCodeSet = new Set(options.explicitPermissionCodes ?? []);
  const readOnly = Boolean(options.readOnly);
  const allRoutes = EXPO_APP_MENU_DEFINITIONS.map((definition) => buildExpoRoute(definition, access, explicitPermissionCodeSet, readOnly)).sort((left, right) => left.order - right.order);
  const visibleRoutes = allRoutes.filter((route) => route.visible);
  const displayTabs = buildDisplayTabs(visibleRoutes);
  const storeTab = displayTabs.find((item) => item.type === "store");
  return {
    visibleRoutes,
    allRoutes,
    displayTabs,
    storeChildren: storeTab?.children ?? []
  };
}
function filterExpoRoutesByVisibility(routes, filter) {
  if (filter === "visible") {
    return routes.filter((route) => route.visible);
  }
  if (filter === "hidden") {
    return routes.filter((route) => !route.visible);
  }
  return routes;
}

// src/utils/roleMenuPreview.ts
var IMPLICIT_ALL_ROLE_NAMES = /* @__PURE__ */ new Set(["admin", "\u7BA1\u7406\u5458"]);
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

// src/utils/webMenuPreview.ts
var accessKeyPermissionMap = {
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
var webMenuPreviewRoutes = [
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
var warehouseStaffVisibleMenuPaths = /* @__PURE__ */ new Set([
  "/warehouse",
  "/warehouse/store-orders"
]);
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

// src/utils/access.permission.test.ts
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
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var translate = (key, fallback) => fallback ?? key;
function findWebMenuNode(nodes, path) {
  for (const node of nodes) {
    if (node.path === path) {
      return node;
    }
    const child = node.children ? findWebMenuNode(node.children, path) : void 0;
    if (child) {
      return child;
    }
  }
  return void 0;
}
var warehouseManagerAccess = buildAccess(
  createCurrentUser({
    roleNames: ["WarehouseManager"],
    permissions: []
  })
);
assertEqual(
  warehouseManagerAccess.hasPermission(P.Warehouse.ManageOrders),
  false,
  "WarehouseManager without explicit permissions should not gain Warehouse.ManageOrders at runtime"
);
var warehouseOrderManagerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Warehouse.ManageOrders]
  })
);
assertEqual(
  warehouseOrderManagerAccess.canManageWarehouseOrders,
  true,
  "Warehouse.ManageOrders should unlock warehouse store order management"
);
assertEqual(
  warehouseOrderManagerAccess.canManageWarehouse,
  false,
  "Warehouse.ManageOrders should not be confused with the legacy whole-warehouse permission"
);
var storeManagerAccess = buildAccess(
  createCurrentUser({
    roleNames: ["StoreManager"],
    permissions: [],
    stores: [
      {
        storeGUID: "managed-store-guid",
        storeName: "Managed Store",
        storeCode: "M1",
        isManageable: true,
        assignedAt: "2026-01-01T00:00:00Z"
      },
      {
        storeGUID: "linked-store-guid",
        storeName: "Linked Store",
        storeCode: "L1",
        isManageable: false,
        assignedAt: "2026-01-01T00:00:00Z"
      }
    ]
  })
);
assertEqual(
  storeManagerAccess.hasPermission(P.StoreProducts.View),
  false,
  "StoreManager without explicit permissions should not gain StoreProducts.View at runtime"
);
assertEqual(
  storeManagerAccess.canManageStoreProducts,
  false,
  "StoreManager without explicit permissions should not unlock store product management"
);
assertEqual(
  storeManagerAccess.managedStoreCodes()?.join(","),
  "M1",
  "StoreManager managed store scope should include only manageable stores"
);
assertEqual(
  storeManagerAccess.visibleStoreCodes()?.join(","),
  "M1,L1",
  "StoreManager visible store scope should include all linked stores"
);
var productMovementReportOnlyAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Reports.ProductMovementView]
  })
);
assertEqual(
  productMovementReportOnlyAccess.canViewReports,
  false,
  "Reports.ProductMovement.View should not unlock legacy sales reports"
);
assertEqual(
  productMovementReportOnlyAccess.canViewProductMovementReport,
  true,
  "Reports.ProductMovement.View should unlock product movement report"
);
assertEqual(
  productMovementReportOnlyAccess.canViewSalesIntelligence,
  true,
  "Product movement report permission should keep sales dashboard parent visible"
);
var adminAccess = buildAccess(
  createCurrentUser({
    roleNames: ["Admin"],
    permissions: []
  })
);
assertEqual(
  adminAccess.hasPermission(P.Container.Delete),
  true,
  "Admin should continue to satisfy all permission checks"
);
var systemLogAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ViewLogs]
  })
);
assertEqual(
  systemLogAccess.canViewSystemLogs,
  true,
  "System.ViewLogs should unlock center log page visibility"
);
var appDownloadAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ViewAppDownloads]
  })
);
assertEqual(
  appDownloadAccess.canViewAppDownloads,
  true,
  "System.ViewAppDownloads should unlock App download page visibility"
);
assertEqual(
  appDownloadAccess.canManageAppDownloads,
  false,
  "System.ViewAppDownloads should not unlock OTA management actions"
);
var appDownloadManagerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ManageAppDownloads]
  })
);
assertEqual(
  appDownloadManagerAccess.canManageAppDownloads,
  true,
  "System.ManageAppDownloads should unlock OTA management actions"
);
assertEqual(
  appDownloadManagerAccess.canViewAppDownloads,
  true,
  "System.ManageAppDownloads should implied System.ViewAppDownloads for page visibility"
);
var appDownloadDeniedAccess = buildAccess(createCurrentUser());
assertEqual(
  appDownloadDeniedAccess.canViewAppDownloads,
  false,
  "Missing System.ViewAppDownloads should not unlock App download page visibility"
);
assertEqual(
  adminAccess.canViewAppDownloads,
  true,
  "Admin should continue to satisfy App download page visibility"
);
assertEqual(
  adminAccess.canManageAppDownloads,
  true,
  "Admin should continue to satisfy App download management actions"
);
var scheduledTaskManagerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ManageScheduledTasks]
  })
);
assertEqual(
  scheduledTaskManagerAccess.canManageScheduledTasks,
  true,
  "System.ManageScheduledTasks should unlock scheduled task runtime control writes"
);
var legacyAliasAccess = buildAccess(
  createCurrentUser({
    permissions: ["LocalInvocie.View", "LocalInvocie.Edit"]
  })
);
assertEqual(
  legacyAliasAccess.hasPermission(P.LocalPurchase.View),
  true,
  "Legacy LocalInvocie.View should continue to satisfy LocalPurchase.View"
);
assertEqual(
  legacyAliasAccess.hasPermission(P.LocalPurchase.Edit),
  true,
  "Legacy LocalInvocie.Edit should continue to satisfy LocalPurchase.Edit"
);
var containerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Container.View]
  })
);
assertEqual(
  containerAccess.canViewContainers,
  true,
  "Container.View should unlock container list/detail visibility"
);
var noPermissionPreviewAccess = buildRolePreviewAccess({
  roleGuid: "role-without-permissions",
  roleName: "NoPermissionRole",
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [],
  effectivePermissionCodes: []
});
var noPermissionExpoPreview = buildExpoRoleMenuPreview(noPermissionPreviewAccess, translate);
assertEqual(
  noPermissionExpoPreview.visibleRoutes.map((route) => route.routeName).join(","),
  "settings",
  "Role preview without Expo app permissions should only show settings"
);
assertEqual(
  noPermissionExpoPreview.storeChildren.length,
  0,
  "Role preview without Expo app permissions should not show store menu children"
);
assertEqual(
  filterExpoRoutesByVisibility(noPermissionExpoPreview.allRoutes, "visible").map((route) => route.routeName).join(","),
  "settings",
  "Visible HbwebExpo filter should only show settings for roles without app permissions"
);
assertEqual(
  filterExpoRoutesByVisibility(noPermissionExpoPreview.allRoutes, "hidden").some((route) => route.routeName === "home"),
  true,
  "Hidden HbwebExpo filter should include protected routes for roles without app permissions"
);
var orderCreatorExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "order-creator-role",
    roleName: "OrderCreatorRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [
      P.Orders.Create,
      P.Orders.View,
      P.StoreProducts.View,
      P.LocalPurchase.View,
      P.InstallmentOrders.View,
      P.StoreVouchers.View
    ],
    effectivePermissionCodes: [
      P.Orders.Create,
      P.Orders.View,
      P.StoreProducts.View,
      P.LocalPurchase.View,
      P.InstallmentOrders.View,
      P.StoreVouchers.View
    ]
  }),
  translate
);
assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === "home"),
  true,
  "Orders.Create should show the HbwebExpo home tab"
);
assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === "cart"),
  true,
  "Orders.Create should show the HbwebExpo cart tab"
);
assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === "product-query"),
  true,
  "StoreProducts.View should show the HbwebExpo product-query tab"
);
assertEqual(
  orderCreatorExpoPreview.displayTabs.some((item) => item.type === "store"),
  true,
  "Overflowing HbwebExpo store routes should collapse into the store menu"
);
assertEqual(
  orderCreatorExpoPreview.storeChildren.some((route) => route.routeName === "installment-orders"),
  true,
  "Collapsed HbwebExpo store menu should include installment orders when permitted"
);
assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, "visible").some((route) => route.routeName === "local-supplier-invoices"),
  true,
  "Visible HbwebExpo filter should include authorized local supplier invoice route"
);
assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, "hidden").some((route) => route.routeName === "local-supplier-invoices"),
  false,
  "Hidden HbwebExpo filter should exclude authorized local supplier invoice route"
);
assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, "all").length,
  orderCreatorExpoPreview.allRoutes.length,
  "All HbwebExpo filter should keep the complete route permission list"
);
var attendanceExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "attendance-role",
    roleName: "AttendanceRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.ScheduleViewSelf],
    effectivePermissionCodes: [P.Attendance.ScheduleViewSelf]
  }),
  translate
);
assertEqual(
  attendanceExpoPreview.visibleRoutes.some((route) => route.routeName === "attendance-personal"),
  true,
  "HbwebExpo legacy attendance app menu should expand to attendance-personal"
);
assertEqual(
  attendanceExpoPreview.visibleRoutes.some((route) => route.routeName === "attendance"),
  false,
  "HbwebExpo preview should not expose the legacy attendance route directly"
);
var attendanceSelfByAvailabilityPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "attendance-self-availability-role",
    roleName: "AttendanceSelfAvailabilityRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.AvailabilitySubmitSelf],
    effectivePermissionCodes: [P.Attendance.AvailabilitySubmitSelf]
  }),
  translate
);
assertEqual(
  attendanceSelfByAvailabilityPreview.visibleRoutes.some((route) => route.routeName === "attendance-personal"),
  true,
  "HbwebExpo attendance personal menu should follow AnyPermissions rules, not only ScheduleViewSelf"
);
var attendanceManagementPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "attendance-management-role",
    roleName: "AttendanceManagementRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.ScheduleViewStore],
    effectivePermissionCodes: [P.Attendance.ScheduleViewStore]
  }),
  translate
);
assertEqual(
  attendanceManagementPreview.visibleRoutes.some((route) => route.routeName === "attendance-management"),
  true,
  "HbwebExpo attendance management menu should follow AnyPermissions rules for store attendance permissions"
);
var warehousePreviewAccess = buildRolePreviewAccess({
  roleGuid: "warehouse-role",
  roleName: "WarehouseRole",
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [P.Warehouse.ManageProducts],
  effectivePermissionCodes: [P.Warehouse.ManageProducts]
});
assertEqual(
  buildExpoRoleMenuPreview(warehousePreviewAccess, translate).visibleRoutes.some((route) => route.routeName === "warehouse"),
  true,
  "Warehouse.ManageProducts should show the HbwebExpo warehouse tab"
);
var superAdminPreviewAccess = buildRolePreviewAccess({
  roleGuid: "super-admin-role",
  roleName: "CustomSuperAdmin",
  isSuperAdmin: true,
  implicitAllPermissions: true,
  explicitPermissionCodes: [],
  effectivePermissionCodes: []
});
var superAdminExpoPreview = buildExpoRoleMenuPreview(superAdminPreviewAccess, translate);
assertEqual(
  superAdminExpoPreview.visibleRoutes.some((route) => route.routeName === "device-management"),
  true,
  "Super admin role preview should show protected HbwebExpo app tabs"
);
assertEqual(
  superAdminExpoPreview.visibleRoutes.length > orderCreatorExpoPreview.visibleRoutes.length,
  true,
  "Super admin role preview should show more HbwebExpo app tabs than a limited order role"
);
var roleReaderWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "role-reader-role",
    roleName: "RoleReaderRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Roles.View],
    effectivePermissionCodes: [P.Roles.View]
  }),
  translate
);
var systemMenu = roleReaderWebPreview.find((node) => node.path === "/system");
var rolesMenu = systemMenu?.children?.find((node) => node.path === "/system/roles");
var adminWebMenuPreview = buildWebRoleMenuPreview(adminAccess, translate, {
  includeHidden: true
});
var productStatisticMenu = findWebMenuNode(adminWebMenuPreview, "/executive-sales-intelligence/product-statistics");
var productMovementReportOnlyWebPreview = buildWebRoleMenuPreview(productMovementReportOnlyAccess, translate, {
  includeHidden: true
});
var productMovementParentMenu = findWebMenuNode(productMovementReportOnlyWebPreview, "/executive-sales-intelligence");
var productMovementReportMenu = findWebMenuNode(
  productMovementReportOnlyWebPreview,
  "/executive-sales-intelligence/product-movement-report"
);
var hiddenSalesDetailForProductMovementOnly = findWebMenuNode(
  productMovementReportOnlyWebPreview,
  "/executive-sales-intelligence/sales-detail-v2"
);
var roleReaderCompleteWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "role-reader-role",
    roleName: "RoleReaderRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Roles.View],
    effectivePermissionCodes: [P.Roles.View]
  }),
  translate,
  {
    includeHidden: true
  }
);
var completeSystemMenu = roleReaderCompleteWebPreview.find((node) => node.path === "/system");
var scheduledStatisticsMenu = completeSystemMenu?.children?.find((node) => node.path === "/system/scheduled-statistics");
var invoiceEmailSettingsMenu = completeSystemMenu?.children?.find((node) => node.path === "/system/invoice-email-settings");
var roleReaderVisibleWebPreview = filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, "visible");
var roleReaderHiddenWebPreview = filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, "hidden");
var systemSettingsAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ManageSettings]
  })
);
assertEqual(
  systemSettingsAccess.canManageSystemSettings,
  true,
  "System.ManageSettings should unlock invoice email settings page visibility"
);
assertEqual(
  systemMenu?.permissionCodes.length,
  0,
  "Parent Web menus without direct accessKey should keep an empty permission list"
);
assertEqual(
  rolesMenu?.permissionCodes.join(","),
  P.Roles.View,
  "Roles.View role preview should show Roles.View on the system roles Web menu"
);
assertEqual(
  rolesMenu?.accessKey,
  "canReadRole",
  "Web menu preview should expose the accessKey that controls the system roles menu"
);
assertEqual(
  getAccessKeyPermissionCodes("canManageScheduledTasks").join(","),
  P.System.ManageScheduledTasks,
  "Web menu preview should map scheduled task management access to System.ManageScheduledTasks"
);
assertEqual(
  scheduledStatisticsMenu?.accessKey,
  "canManageScheduledTasks",
  "\u5B9A\u65F6\u7EDF\u8BA1\u4EFB\u52A1 Web \u83DC\u5355\u5E94\u7531 System.ManageScheduledTasks \u63A7\u5236"
);
assertEqual(
  scheduledStatisticsMenu?.permissionCodes.join(","),
  P.System.ManageScheduledTasks,
  "\u5B9A\u65F6\u7EDF\u8BA1\u4EFB\u52A1 Web \u83DC\u5355\u5E94\u5C55\u793A System.ManageScheduledTasks \u6743\u9650"
);
assertEqual(
  getAccessKeyPermissionCodes("canManageSystemSettings").join(","),
  P.System.ManageSettings,
  "Web menu preview should map system settings access to System.ManageSettings"
);
assertEqual(
  getAccessKeyPermissionCodes("canViewAppDownloads").join(","),
  `${P.System.ViewAppDownloads},${P.System.ManageAppDownloads}`,
  "Web menu preview should map App download access to both view and manage permissions"
);
assertEqual(
  invoiceEmailSettingsMenu?.accessKey,
  "canManageSystemSettings",
  "\u53D1\u7968\u90AE\u7BB1\u914D\u7F6E Web \u83DC\u5355\u5E94\u7531 System.ManageSettings \u63A7\u5236"
);
assertEqual(
  invoiceEmailSettingsMenu?.permissionCodes.join(","),
  P.System.ManageSettings,
  "\u53D1\u7968\u90AE\u7BB1\u914D\u7F6E Web \u83DC\u5355\u5E94\u5C55\u793A System.ManageSettings \u6743\u9650"
);
assertEqual(
  Boolean(productStatisticMenu),
  false,
  "\u9500\u552E\u770B\u677F Web \u83DC\u5355\u4E0D\u5E94\u518D\u663E\u793A\u65E7\u5546\u54C1\u7EDF\u8BA1\u72B6\u6001\u5165\u53E3"
);
assertEqual(
  productMovementParentMenu?.visible,
  true,
  "\u53EA\u6709\u5546\u54C1\u7ECF\u8425\u5206\u6790\u6743\u9650\u65F6\u9500\u552E\u770B\u677F\u7236\u83DC\u5355\u5E94\u53EF\u89C1"
);
assertEqual(
  productMovementReportMenu?.visible,
  true,
  "\u53EA\u6709\u5546\u54C1\u7ECF\u8425\u5206\u6790\u6743\u9650\u65F6\u5546\u54C1\u7ECF\u8425\u5206\u6790\u83DC\u5355\u5E94\u53EF\u89C1"
);
assertEqual(
  hiddenSalesDetailForProductMovementOnly?.visible,
  false,
  "\u53EA\u6709\u5546\u54C1\u7ECF\u8425\u5206\u6790\u6743\u9650\u65F6\u4E0D\u5E94\u6253\u5F00\u9500\u552E\u660E\u7EC6\u83DC\u5355"
);
assertEqual(
  filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, "all").length,
  roleReaderCompleteWebPreview.length,
  "All Web menu filter should keep the complete desktop menu tree"
);
assertEqual(
  Boolean(findWebMenuNode(roleReaderVisibleWebPreview, "/system")),
  true,
  "Visible Web menu filter should keep parent context for authorized child menus"
);
assertEqual(
  Boolean(findWebMenuNode(roleReaderVisibleWebPreview, "/system/roles")),
  true,
  "Visible Web menu filter should include authorized desktop menu entries"
);
assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, "/system")),
  true,
  "Hidden Web menu filter should keep parent context for hidden child menus"
);
assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, "/system/stores")),
  true,
  "Hidden Web menu filter should include unauthorized desktop menu entries"
);
assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, "/system/roles")),
  false,
  "Hidden Web menu filter should exclude authorized desktop menu entries"
);
var warehouseManageCodes = getAccessKeyPermissionCodes("canManageWarehouseProducts");
assertEqual(
  warehouseManageCodes.join(","),
  `${P.Warehouse.ManageProducts},${P.Warehouse.Manage}`,
  "Warehouse product Web menu should document both fine-grained and legacy manager permissions"
);
var warehouseLegacyWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "warehouse-legacy-role",
    roleName: "WarehouseLegacyRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Warehouse.Manage],
    effectivePermissionCodes: [P.Warehouse.Manage]
  }),
  translate
);
var warehouseProductsMenu = warehouseLegacyWebPreview.find((node) => node.path === "/warehouse")?.children?.find((node) => node.path === "/warehouse/products");
var warehouseLegacyPosAdminMenu = warehouseLegacyWebPreview.find((node) => node.path === "/pos-admin");
var warehouseLegacySalesOrdersMenu = findWebMenuNode(warehouseLegacyWebPreview, "/pos-admin/sales-orders");
assertEqual(
  warehouseProductsMenu?.permissionCodes.join(","),
  `${P.Warehouse.ManageProducts},${P.Warehouse.Manage}`,
  "Warehouse.Manage role preview should show the warehouse products Web menu with both accepted permissions"
);
assertEqual(
  Boolean(warehouseLegacyPosAdminMenu),
  false,
  "Warehouse.Manage role preview should not show POS admin parent menu without visible POS children"
);
assertEqual(
  Boolean(warehouseLegacySalesOrdersMenu),
  false,
  "Warehouse.Manage role preview should not show cashier records without Orders.View"
);
var warehouseStaffWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "warehouse-staff-role",
    roleName: "WarehouseStaff",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Dashboard.View, P.Warehouse.Manage, P.Orders.View],
    effectivePermissionCodes: [P.Dashboard.View, P.Warehouse.Manage, P.Orders.View]
  }),
  translate
);
var warehouseStaffMenu = warehouseStaffWebPreview.find((node) => node.path === "/warehouse");
assertEqual(
  warehouseStaffWebPreview.map((node) => node.path).join(","),
  "/warehouse",
  "WarehouseStaff desktop preview should only show the warehouse parent menu"
);
assertEqual(
  warehouseStaffMenu?.children?.map((node) => node.path).join(","),
  "/warehouse/store-orders",
  "WarehouseStaff desktop preview should only show the store order list under warehouse"
);
assertEqual(
  Boolean(findWebMenuNode(warehouseStaffWebPreview, "/dashboard")),
  false,
  "WarehouseStaff desktop preview should not show dashboard navigation"
);
assertEqual(
  Boolean(findWebMenuNode(warehouseStaffWebPreview, "/warehouse/products")),
  false,
  "WarehouseStaff desktop preview should not show warehouse products navigation"
);
assertEqual(
  Boolean(findWebMenuNode(warehouseStaffWebPreview, "/pos-admin/sales-orders")),
  false,
  "WarehouseStaff desktop preview should not show cashier records navigation"
);
var superAdminWebPreview = buildWebRoleMenuPreview(superAdminPreviewAccess, translate);
var superAdminDeviceMenu = superAdminWebPreview.find((node) => node.path === "/system")?.children?.find((node) => node.path === "/system/device-registration");
assertEqual(
  superAdminDeviceMenu?.permissionCodes.join(","),
  `${P.DeviceRegistration.View},${P.DeviceRegistration.Manage}`,
  "Super admin Web preview should show protected menus with their normal permission codes"
);
var advertisementViewerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Advertisements.View]
  })
);
var advertisementEditorOnlyAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Advertisements.Edit]
  })
);
assertEqual(
  advertisementViewerAccess.canManageAdvertisements,
  true,
  "Advertisements.View should unlock advertisement management page visibility"
);
assertEqual(
  advertisementEditorOnlyAccess.canManageAdvertisements,
  false,
  "Advertisements.Edit alone should not unlock advertisement list visibility"
);
assertEqual(
  advertisementViewerAccess.canEditAdvertisements,
  false,
  "Advertisements.View alone should not unlock advertisement editing"
);
var advertisementManageCodes = getAccessKeyPermissionCodes("canManageAdvertisements");
assertEqual(
  advertisementManageCodes.join(","),
  P.Advertisements.View,
  "Advertisement Web menu should document the view permission required by backend read APIs"
);
var advertisementPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "advertisement-role",
    roleName: "AdvertisementRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Advertisements.View],
    effectivePermissionCodes: [P.Advertisements.View]
  }),
  translate
);
var advertisementMenu = advertisementPreview.find((node) => node.path === "/pos-admin")?.children?.find((node) => node.path === "/pos-admin/advertisements");
assertEqual(
  advertisementMenu?.permissionCodes.join(","),
  P.Advertisements.View,
  "Advertisements.View role preview should show the advertisement menu with backend read permission"
);
var legacyLocalInvoiceWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "legacy-local-invoice-role",
    roleName: "LegacyLocalInvoiceRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: ["LocalInvocie.View"],
    effectivePermissionCodes: ["LocalInvocie.View", P.LocalPurchase.View]
  }),
  translate,
  {
    includeHidden: true,
    explicitPermissionCodes: ["LocalInvocie.View"]
  }
);
var legacyLocalInvoiceMenu = legacyLocalInvoiceWebPreview.find((node) => node.path === "/pos-admin")?.children?.find((node) => node.path === "/pos-admin/local-supplier-invoices");
assertEqual(
  legacyLocalInvoiceMenu?.edit.removePermissionCodes.includes("LocalInvocie.View"),
  true,
  "Web preview remove action should include legacy LocalInvocie.View so old roles can hide the menu"
);
var legacyLocalInvoiceRemovedPermissions = applyRolePermissionMutation({
  currentPermissionCodes: ["LocalInvocie.View"],
  removePermissionCodes: legacyLocalInvoiceMenu?.edit.removePermissionCodes ?? []
});
assertEqual(
  legacyLocalInvoiceRemovedPermissions.length,
  0,
  "Removing the legacy local invoice menu should delete the effective legacy permission assignment"
);
var hiddenLocalInvoiceAfterLegacyRemoval = filterWebMenuNodesByVisibility(
  buildWebRoleMenuPreview(
    buildRolePreviewAccess({
      roleGuid: "legacy-local-invoice-removed-role",
      roleName: "LegacyLocalInvoiceRemovedRole",
      isSuperAdmin: false,
      implicitAllPermissions: false,
      explicitPermissionCodes: legacyLocalInvoiceRemovedPermissions,
      effectivePermissionCodes: legacyLocalInvoiceRemovedPermissions
    }),
    translate,
    {
      includeHidden: true,
      explicitPermissionCodes: legacyLocalInvoiceRemovedPermissions
    }
  ),
  "hidden"
);
assertEqual(
  Boolean(findWebMenuNode(hiddenLocalInvoiceAfterLegacyRemoval, "/pos-admin/local-supplier-invoices")),
  true,
  "Hidden Web menu filter should include local supplier invoices after removing the legacy LocalInvocie.View alias"
);
var legacyLocalInvoiceExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: "legacy-local-invoice-expo-role",
    roleName: "LegacyLocalInvoiceExpoRole",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: ["LocalInvocie.View"],
    effectivePermissionCodes: ["LocalInvocie.View", P.LocalPurchase.View]
  }),
  translate,
  {
    explicitPermissionCodes: ["LocalInvocie.View"]
  }
);
var legacyLocalInvoiceExpoRoute = legacyLocalInvoiceExpoPreview.allRoutes.find(
  (route) => route.routeName === "local-supplier-invoices"
);
assertEqual(
  legacyLocalInvoiceExpoRoute?.removePermissionCodes.includes("LocalInvocie.View"),
  true,
  "HbwebExpo preview remove action should include legacy LocalInvocie.View so old roles can hide the menu"
);
assertEqual(
  applyRolePermissionMutation({
    currentPermissionCodes: ["LocalInvocie.View"],
    removePermissionCodes: legacyLocalInvoiceExpoRoute?.removePermissionCodes ?? []
  }).length,
  0,
  "Removing the legacy local invoice HbwebExpo menu should delete the effective legacy permission assignment"
);
var localPurchasePushAccess = buildAccess(
  createCurrentUser({
    permissions: [P.LocalPurchase.PushToHq]
  })
);
assertEqual(
  localPurchasePushAccess.canPushLocalPurchaseToHq,
  true,
  "LocalPurchase.PushToHq should expose the HQ write permission flag"
);
assertEqual(
  localPurchasePushAccess.canEditLocalPurchase && localPurchasePushAccess.canPushLocalPurchaseToHq,
  false,
  "LocalPurchase.PushToHq alone should not be treated as full edit-page HQ write access"
);
var localPurchaseViewOnlyAccess = buildAccess(
  createCurrentUser({
    permissions: [P.LocalPurchase.View]
  })
);
assertEqual(
  localPurchaseViewOnlyAccess.canPushLocalPurchaseToHq,
  false,
  "LocalPurchase.View should not unlock HQ write actions"
);
console.log("access.permission.test: ok");
