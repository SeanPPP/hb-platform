/**
 * Frontend permission constants — mirrors backend BlazorApp.Shared.Constants.Permissions
 * Used by <HasPermission code={P.XXX}> and buildAccess() for permission-driven access control.
 *
 * KEEP IN SYNC with backend Permissions.cs
 */

export const P = {
  Users: {
    View: 'Users.View',
    Create: 'Users.Create',
    Edit: 'Users.Edit',
    Delete: 'Users.Delete',
    ManageRoles: 'Users.ManageRoles',
    ManageStores: 'Users.ManageStores',
    ResetPassword: 'Users.ResetPassword',
  },
  Roles: {
    View: 'Roles.View',
    Create: 'Roles.Create',
    Edit: 'Roles.Edit',
    Delete: 'Roles.Delete',
    ManagePermissions: 'Roles.ManagePermissions',
    ManageUsers: 'Roles.ManageUsers',
  },
  Stores: {
    View: 'Stores.View',
    Create: 'Stores.Create',
    Edit: 'Stores.Edit',
    Delete: 'Stores.Delete',
    Sync: 'Stores.Sync',
  },
  Products: {
    View: 'Products.View',
    Create: 'Products.Create',
    Edit: 'Products.Edit',
    Delete: 'Products.Delete',
  },
  Orders: {
    View: 'Orders.View',
    Create: 'Orders.Create',
    Edit: 'Orders.Edit',
    Delete: 'Orders.Delete',
  },
  Warehouse: {
    View: 'Warehouse.View',
    Manage: 'Warehouse.Manage',
    ManageProducts: 'Warehouse.ManageProducts',
    ManageCategories: 'Warehouse.ManageCategories',
    ManageLocations: 'Warehouse.ManageLocations',
    ManageOrders: 'Warehouse.ManageOrders',
  },
  DomesticPurchase: {
    View: 'DomesticPurchase.View',
    ManageSuppliers: 'DomesticPurchase.ManageSuppliers',
    ManageProducts: 'DomesticPurchase.ManageProducts',
    ManagePrefixCodes: 'DomesticPurchase.ManagePrefixCodes',
  },
  Prices: {
    View: 'Prices.View',
    Modify: 'Prices.Modify',
    Delete: 'Prices.Delete',
  },
  Reports: {
    View: 'Reports.View',
    Export: 'Reports.Export',
  },
  System: {
    ViewLogs: 'System.ViewLogs',
    ManageSettings: 'System.ManageSettings',
  },
  DeviceRegistration: {
    View: 'DeviceRegistration.View',
    Manage: 'DeviceRegistration.Manage',
  },
  Dashboard: {
    View: 'Dashboard',
  },
  OrderFront: {
    View: 'OrderFront',
  },
  Attendance: {
    ScheduleViewSelf: 'Attendance.Schedule.ViewSelf',
    ScheduleViewStore: 'Attendance.Schedule.ViewStore',
    ScheduleEditManagedStore: 'Attendance.Schedule.EditManagedStore',
    AvailabilitySubmitSelf: 'Attendance.Availability.SubmitSelf',
    AvailabilityViewManagedStore: 'Attendance.Availability.ViewManagedStore',
    PunchSelf: 'Attendance.Punch.Self',
    PunchViewManagedStore: 'Attendance.Punch.ViewManagedStore',
    ApprovalViewManagedStore: 'Attendance.Approval.ViewManagedStore',
    ApprovalReviewManagedStore: 'Attendance.Approval.ReviewManagedStore',
    HolidayViewStore: 'Attendance.Holiday.ViewStore',
    HolidayEditManagedStore: 'Attendance.Holiday.EditManagedStore',
    LeaveApplySelf: 'Attendance.Leave.ApplySelf',
    LeaveViewManagedStore: 'Attendance.Leave.ViewManagedStore',
    LeaveReviewManagedStore: 'Attendance.Leave.ReviewManagedStore',
    SettingsEdit: 'Attendance.Settings.Edit',
    AdminView: 'Attendance.Admin.View',
  },
} as const

/**
 * Role-level permission grants for WarehouseManager.
 * Keep this list in sync with backend BlazorApp.Shared.Constants.Permissions.
 */
export const WAREHOUSE_MANAGER_PERMISSION_CODES = [
  P.Stores.View,
  P.Stores.Create,
  P.Stores.Edit,
  P.Stores.Delete,
  P.Stores.Sync,
  P.Products.View,
  P.Products.Create,
  P.Products.Edit,
  P.Products.Delete,
  P.Orders.View,
  P.Orders.Create,
  P.Orders.Edit,
  P.Orders.Delete,
  'Container.View',
  'Container.Create',
  'Container.Edit',
  'Container.Delete',
  P.Warehouse.View,
  P.Warehouse.Manage,
  P.Warehouse.ManageProducts,
  P.Warehouse.ManageCategories,
  P.Warehouse.ManageLocations,
  P.Warehouse.ManageOrders,
  P.DomesticPurchase.View,
  P.DomesticPurchase.ManageSuppliers,
  P.DomesticPurchase.ManageProducts,
  P.DomesticPurchase.ManagePrefixCodes,
  P.Reports.View,
  P.Reports.Export,
  P.Dashboard.View,
] as const

/** All permission code values as a flat array */
export const ALL_PERMISSIONS: string[] = Object.values(P).flatMap((group) =>
  Object.values(group),
)
