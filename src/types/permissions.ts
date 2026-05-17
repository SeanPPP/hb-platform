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
  Dashboard: {
    View: 'Dashboard',
  },
} as const

/** All permission code values as a flat array */
export const ALL_PERMISSIONS: string[] = Object.values(P).flatMap((group) =>
  Object.values(group),
)
