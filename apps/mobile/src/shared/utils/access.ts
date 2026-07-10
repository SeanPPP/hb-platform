import type { AccessControl, CurrentUser } from "@/modules/auth/types";

export const PERMISSIONS = {
  DeviceRegistration: {
    View: "DeviceRegistration.View",
    Manage: "DeviceRegistration.Manage",
  },
  EmployeeProfiles: {
    View: "EmployeeProfiles.View",
  },
  LocalPurchase: {
    View: "LocalPurchase.View",
    Edit: "LocalPurchase.Edit",
    PushToHq: "LocalPurchase.PushToHq",
  },
  InstallmentOrders: {
    View: "InstallmentOrders.View",
  },
  Advertisements: {
    View: "Advertisements.View",
    Edit: "Advertisements.Edit",
  },
  StoreVouchers: {
    View: "StoreVouchers.View",
  },
  StoreProducts: {
    View: "StoreProducts.View",
    Create: "StoreProducts.Create",
    Edit: "StoreProducts.Edit",
  },
  Container: {
    View: "Container.View",
    Create: "Container.Create",
    Edit: "Container.Edit",
    Delete: "Container.Delete",
  },
  SeasonalCards: {
    Remaining: {
      ViewManagedStore: "SeasonalCards.Remaining.ViewManagedStore",
      SubmitManagedStore: "SeasonalCards.Remaining.SubmitManagedStore",
    },
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
    AdminView: "Attendance.Admin.View",
  },
} as const;

const PERMISSION_ALIAS_GROUPS = [
  {
    canonicalCode: PERMISSIONS.LocalPurchase.View,
    aliasCodes: ["LocalInvocie.View"],
  },
  {
    canonicalCode: PERMISSIONS.LocalPurchase.Edit,
    aliasCodes: ["LocalInvocie.Edit"],
  },
  {
    canonicalCode: PERMISSIONS.StoreProducts.Create,
    aliasCodes: [
      "StoreProduct.Create",
      "StoreProductManagement.Create",
      "StoreProductMaintenance.Create",
      "StoreProductsManagement.Create",
      "StoreProductsMaintenance.Create",
      "分店商品管理.创建分店商品",
      "创建分店商品",
    ],
  },
] as const;

const ATTENDANCE_PERSONAL_PERMISSIONS = [
  PERMISSIONS.Attendance.ScheduleViewSelf,
  PERMISSIONS.Attendance.AvailabilitySubmitSelf,
  PERMISSIONS.Attendance.PunchSelf,
  PERMISSIONS.Attendance.LeaveApplySelf,
];

const ATTENDANCE_MANAGEMENT_PERMISSIONS = [
  PERMISSIONS.Attendance.ScheduleViewStore,
  PERMISSIONS.Attendance.ScheduleEditManagedStore,
  PERMISSIONS.Attendance.AvailabilityViewManagedStore,
  PERMISSIONS.Attendance.PunchViewManagedStore,
  PERMISSIONS.Attendance.ApprovalViewManagedStore,
  PERMISSIONS.Attendance.ApprovalReviewManagedStore,
  PERMISSIONS.Attendance.HolidayViewStore,
  PERMISSIONS.Attendance.HolidayEditManagedStore,
  PERMISSIONS.Attendance.LeaveViewManagedStore,
  PERMISSIONS.Attendance.LeaveReviewManagedStore,
  PERMISSIONS.Attendance.SettingsEdit,
  PERMISSIONS.Attendance.AdminView,
];

const ATTENDANCE_REVIEW_PERMISSIONS = [
  PERMISSIONS.Attendance.ApprovalReviewManagedStore,
  PERMISSIONS.Attendance.LeaveReviewManagedStore,
  PERMISSIONS.Attendance.AdminView,
];

function resolvePermissionAliases(permission: string) {
  const normalizedPermission = permission.toLowerCase();
  const group = PERMISSION_ALIAS_GROUPS.find(({ canonicalCode, aliasCodes }) =>
    [canonicalCode, ...aliasCodes].some(
      (item) => item.toLowerCase() === normalizedPermission
    )
  );

  return group ? [group.canonicalCode, ...group.aliasCodes] : [permission];
}

function createEmptyAccess(): AccessControl {
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
    canCreateOrder: false,
    canEditOrder: false,
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
    canViewContainers: false,
    canCreateContainer: false,
    canEditContainer: false,
    canDeleteContainer: false,
    canManageStore: false,
    canViewReports: false,
    canExportData: false,
    canModifyPrice: false,
    canDeletePrice: false,
    canViewDeviceRegistration: false,
    canManageDeviceRegistration: false,
    canViewEmployeeProfiles: false,
    canViewAttendancePersonal: false,
    canViewAttendanceManagement: false,
    canReviewAttendance: false,
    canEditAttendanceHoliday: false,
    canEditAttendanceSettings: false,
    canViewLocalPurchase: false,
    canEditLocalPurchase: false,
    canPushLocalPurchaseToHq: false,
    canViewInstallmentOrders: false,
    canViewAdvertisements: false,
    canManageAdvertisements: false,
    canViewStoreVouchers: false,
    canViewSeasonalCardRemaining: false,
    canSubmitSeasonalCardRemaining: false,
    canCreateStoreProducts: false,
    canManageAttendance: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    assignedStoreCodes: () => null,
    managedStoreCodes: () => null,
  };
}

export function buildAccess(currentUser?: CurrentUser | null): AccessControl {
  if (!currentUser) {
    return createEmptyAccess();
  }

  const hasRole = (role: string) =>
    currentUser.roleNames?.some(
      (item) => item.toLowerCase() === role.toLowerCase()
    ) ?? false;

  const onlyRole = (role: string) => {
    if (!currentUser.roleNames?.length) {
      return false;
    }
    return hasRole(role) && currentUser.roleNames.length === 1;
  };

  const hasAnyRole = (roles: string[]) => roles.some((role) => hasRole(role));
  const hasAllRoles = (roles: string[]) => roles.every((role) => hasRole(role));

  const isAdmin = hasRole("Admin") || hasRole("管理员");
  const normalizedPermissions = new Set(
    currentUser.permissions?.map((item) => item.toLowerCase()) ?? []
  );
  const hasPermission = (permission: string) =>
    isAdmin ||
    resolvePermissionAliases(permission).some((item) =>
      normalizedPermissions.has(item.toLowerCase())
    );
  const isWarehouseManager =
    hasRole("WarehouseManager") || hasRole("仓库经理");
  const isStoreManager = hasRole("StoreManager") || hasRole("经理");
  const isManager = isStoreManager || isWarehouseManager;
  const isUser = hasRole("User") || hasRole("用户");
  const isWarehouseStaff =
    isAdmin || hasRole("WarehouseStaff") || hasRole("仓库员工") || hasRole("WarehouseManager");
  // 纯仓库员工用于专用购物车分流：管理员/仓库经理仍按管理角色能力处理。
  const isWarehouseStaffOnly =
    (hasRole("WarehouseStaff") || hasRole("仓库员工")) && !isAdmin && !isWarehouseManager;
  const isStoreStaff = hasRole("StoreStaff") || hasRole("店铺员工");
  const isStoreLevelManager = isStoreManager && !isAdmin && !isWarehouseManager;
  const onlyOrder = onlyRole("Order") || hasRole("订货员");

  const assignedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.map((item) => item.storeCode).filter(Boolean);
    }
    return null;
  };

  const managedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      // isPrimary=true 表示可管理分店；false 是已分配但只读范围。
      return currentUser.stores
        .filter((item) => item.isPrimary === true)
        .map((item) => item.storeCode)
        .filter(Boolean);
    }
    return null;
  };

  const canReadUser = hasPermission("Users.View");
  const canWriteUser = hasPermission("Users.Create") || hasPermission("Users.Edit");
  const canDeleteUser = hasPermission("Users.Delete");
  const canReadRole = hasPermission("Roles.View");
  const canWriteRole = hasPermission("Roles.Create") || hasPermission("Roles.Edit");
  const canDeleteRole = hasPermission("Roles.Delete");
  const canReadStore = hasPermission("Stores.View");
  const canWriteStore = hasPermission("Stores.Create") || hasPermission("Stores.Edit");
  const canDeleteStore = hasPermission("Stores.Delete");
  const canManageWarehouse = hasPermission("Warehouse.Manage");
  const canViewContainers = hasPermission(PERMISSIONS.Container.View);
  const canCreateContainer = hasPermission(PERMISSIONS.Container.Create);
  const canEditContainer = hasPermission(PERMISSIONS.Container.Edit);
  const canDeleteContainer = hasPermission(PERMISSIONS.Container.Delete);
  const canManageStore = hasPermission("Stores.Edit") || hasPermission("Warehouse.Manage");

  const canReadOrder = hasPermission("Orders.View");
  // 普通订单建单和明细维护需要显式订单权限，旧 Warehouse.Manage 不再隐式放大。
  const canCreateOrder = hasPermission("Orders.Create");
  const canEditOrder = hasPermission("Orders.Edit");
  const canWriteOrder = canCreateOrder || canEditOrder;
  const canDeleteOrder = hasPermission("Orders.Delete");
  const canReadProduct = hasPermission("Products.View");
  const canWriteProduct = hasPermission("Products.Create") || hasPermission("Products.Edit");
  const canDeleteProduct = hasPermission("Products.Delete");

  const canViewReports = hasPermission("Reports.View");
  const canExportData = hasPermission("Reports.Export");
  const canModifyPrice = hasPermission("Prices.Modify");
  const canDeletePrice = hasPermission("Prices.Delete");
  const canManageDeviceRegistration = hasPermission(PERMISSIONS.DeviceRegistration.Manage);
  const canViewDeviceRegistration =
    canManageDeviceRegistration || hasPermission(PERMISSIONS.DeviceRegistration.View);
  const canViewEmployeeProfiles = hasPermission(PERMISSIONS.EmployeeProfiles.View);
  const canViewAttendancePersonal =
    isAdmin ||
    ATTENDANCE_PERSONAL_PERMISSIONS.some((permission) =>
      hasPermission(permission)
    );
  const canViewAttendanceManagement =
    isAdmin ||
    ATTENDANCE_MANAGEMENT_PERMISSIONS.some((permission) =>
      hasPermission(permission)
    );
  const canReviewAttendance =
    isAdmin ||
    ATTENDANCE_REVIEW_PERMISSIONS.some((permission) =>
      hasPermission(permission)
    );
  const canEditAttendanceHoliday =
    isAdmin || hasPermission(PERMISSIONS.Attendance.HolidayEditManagedStore);
  const canEditAttendanceSettings =
    isAdmin || hasPermission(PERMISSIONS.Attendance.SettingsEdit);
  const canViewLocalPurchase = hasPermission(PERMISSIONS.LocalPurchase.View);
  const canEditLocalPurchase = hasPermission(PERMISSIONS.LocalPurchase.Edit);
  const canPushLocalPurchaseToHq = hasPermission(PERMISSIONS.LocalPurchase.PushToHq);
  const canViewInstallmentOrders = hasPermission(PERMISSIONS.InstallmentOrders.View);
  const canViewAdvertisements = hasPermission(PERMISSIONS.Advertisements.View);
  const canManageAdvertisements = hasPermission(PERMISSIONS.Advertisements.Edit);
  const canViewStoreVouchers = hasPermission(PERMISSIONS.StoreVouchers.View);
  const canViewSeasonalCardRemaining = hasPermission(
    PERMISSIONS.SeasonalCards.Remaining.ViewManagedStore
  );
  const canSubmitSeasonalCardRemaining = hasPermission(
    PERMISSIONS.SeasonalCards.Remaining.SubmitManagedStore
  );
  const canCreateStoreProducts =
    hasPermission(PERMISSIONS.StoreProducts.Create) ||
    hasPermission(PERMISSIONS.StoreProducts.Edit);
  const canManageAttendance = canViewAttendanceManagement;

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
    canCreateOrder,
    canEditOrder,
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
    canViewContainers,
    canCreateContainer,
    canEditContainer,
    canDeleteContainer,
    canManageStore,
    canViewReports,
    canExportData,
    canModifyPrice,
    canDeletePrice,
    canViewDeviceRegistration,
    canManageDeviceRegistration,
    canViewEmployeeProfiles,
    canViewAttendancePersonal,
    canViewAttendanceManagement,
    canReviewAttendance,
    canEditAttendanceHoliday,
    canEditAttendanceSettings,
    canViewLocalPurchase,
    canEditLocalPurchase,
    canPushLocalPurchaseToHq,
    canViewInstallmentOrders,
    canViewAdvertisements,
    canManageAdvertisements,
    canViewStoreVouchers,
    canViewSeasonalCardRemaining,
    canSubmitSeasonalCardRemaining,
    canCreateStoreProducts,
    canManageAttendance,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    assignedStoreCodes,
    managedStoreCodes,
  };
}
