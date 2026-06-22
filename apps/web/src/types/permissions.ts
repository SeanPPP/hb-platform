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
  Container: {
    View: 'Container.View',
    Create: 'Container.Create',
    Edit: 'Container.Edit',
    Delete: 'Container.Delete',
  },
  InstallmentOrders: {
    View: 'InstallmentOrders.View',
  },
  StoreVouchers: {
    View: 'StoreVouchers.View',
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
    ProductMovementView: 'Reports.ProductMovement.View',
  },
  System: {
    ViewLogs: 'System.ViewLogs',
    ManageScheduledTasks: 'System.ManageScheduledTasks',
    ManageSettings: 'System.ManageSettings',
    ViewAppDownloads: 'System.ViewAppDownloads',
    ManageAppDownloads: 'System.ManageAppDownloads',
  },
  DeviceRegistration: {
    View: 'DeviceRegistration.View',
    Manage: 'DeviceRegistration.Manage',
  },
  EmployeeProfiles: {
    View: 'EmployeeProfiles.View',
    Edit: 'EmployeeProfiles.Edit',
  },
  StoreProducts: {
    View: 'StoreProducts.View',
    Create: 'StoreProducts.Create',
    Edit: 'StoreProducts.Edit',
  },
  Promotions: {
    View: 'Promotions.View',
    Edit: 'Promotions.Edit',
  },
  Advertisements: {
    View: 'Advertisements.View',
    Edit: 'Advertisements.Edit',
  },
  PricingStrategy: {
    View: 'PricingStrategy.View',
    Edit: 'PricingStrategy.Edit',
  },
  LocalPurchase: {
    View: 'LocalPurchase.View',
    Edit: 'LocalPurchase.Edit',
    PushToHq: 'LocalPurchase.PushToHq',
  },
  AustralianSuppliers: {
    View: 'AustralianSuppliers.View',
    Edit: 'AustralianSuppliers.Edit',
  },
  Store: {
    ManageOperations: 'Store.ManageOperations',
    ManageInfo: 'Store.ManageInfo',
  },
  PosProducts: {
    View: 'PosProducts.View',
    Manage: 'PosProducts.Manage',
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

/** All permission code values as a flat array */
export const ALL_PERMISSIONS: string[] = Object.values(P).flatMap((group) =>
  Object.values(group),
)
