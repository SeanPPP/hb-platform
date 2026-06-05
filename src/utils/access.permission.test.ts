import type { CurrentUser } from '../types/auth'
import { P } from '../types/permissions'
import { buildAccess } from './access'
import { buildExpoRoleMenuPreview, filterExpoRoutesByVisibility } from './expoRoleMenuPreview'
import { applyRolePermissionMutation, buildRolePreviewAccess } from './roleMenuPreview'
import { buildWebRoleMenuPreview, filterWebMenuNodesByVisibility, getAccessKeyPermissionCodes, type WebMenuPreviewNode } from './webMenuPreview'

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'test-user-guid',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const translate = (key: string, fallback?: string) => fallback ?? key

function findWebMenuNode(nodes: WebMenuPreviewNode[], path: string): WebMenuPreviewNode | undefined {
  for (const node of nodes) {
    if (node.path === path) {
      return node
    }
    const child = node.children ? findWebMenuNode(node.children, path) : undefined
    if (child) {
      return child
    }
  }
  return undefined
}

const warehouseManagerAccess = buildAccess(
  createCurrentUser({
    roleNames: ['WarehouseManager'],
    permissions: [],
  }),
)

assertEqual(
  warehouseManagerAccess.hasPermission(P.Warehouse.ManageOrders),
  false,
  'WarehouseManager without explicit permissions should not gain Warehouse.ManageOrders at runtime',
)

const warehouseOrderManagerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Warehouse.ManageOrders],
  }),
)

assertEqual(
  warehouseOrderManagerAccess.canManageWarehouseOrders,
  true,
  'Warehouse.ManageOrders should unlock warehouse store order management',
)

assertEqual(
  warehouseOrderManagerAccess.canManageWarehouse,
  false,
  'Warehouse.ManageOrders should not be confused with the legacy whole-warehouse permission',
)

const storeManagerAccess = buildAccess(
  createCurrentUser({
    roleNames: ['StoreManager'],
    permissions: [],
    stores: [
      {
        storeGUID: 'managed-store-guid',
        storeName: 'Managed Store',
        storeCode: 'M1',
        isManageable: true,
        assignedAt: '2026-01-01T00:00:00Z',
      },
      {
        storeGUID: 'linked-store-guid',
        storeName: 'Linked Store',
        storeCode: 'L1',
        isManageable: false,
        assignedAt: '2026-01-01T00:00:00Z',
      },
    ],
  }),
)

assertEqual(
  storeManagerAccess.hasPermission(P.StoreProducts.View),
  false,
  'StoreManager without explicit permissions should not gain StoreProducts.View at runtime',
)

assertEqual(
  storeManagerAccess.canManageStoreProducts,
  false,
  'StoreManager without explicit permissions should not unlock store product management',
)

assertEqual(
  storeManagerAccess.managedStoreCodes()?.join(','),
  'M1',
  'StoreManager managed store scope should include only manageable stores',
)

assertEqual(
  storeManagerAccess.visibleStoreCodes()?.join(','),
  'M1,L1',
  'StoreManager visible store scope should include all linked stores',
)

const adminAccess = buildAccess(
  createCurrentUser({
    roleNames: ['Admin'],
    permissions: [],
  }),
)

assertEqual(
  adminAccess.hasPermission(P.Container.Delete),
  true,
  'Admin should continue to satisfy all permission checks',
)

const systemLogAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ViewLogs],
  }),
)

assertEqual(
  systemLogAccess.canViewSystemLogs,
  true,
  'System.ViewLogs should unlock center log page visibility',
)

const legacyAliasAccess = buildAccess(
  createCurrentUser({
    permissions: ['LocalInvocie.View', 'LocalInvocie.Edit'],
  }),
)

assertEqual(
  legacyAliasAccess.hasPermission(P.LocalPurchase.View),
  true,
  'Legacy LocalInvocie.View should continue to satisfy LocalPurchase.View',
)

assertEqual(
  legacyAliasAccess.hasPermission(P.LocalPurchase.Edit),
  true,
  'Legacy LocalInvocie.Edit should continue to satisfy LocalPurchase.Edit',
)

const containerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Container.View],
  }),
)

assertEqual(
  containerAccess.canViewContainers,
  true,
  'Container.View should unlock container list/detail visibility',
)

const noPermissionPreviewAccess = buildRolePreviewAccess({
  roleGuid: 'role-without-permissions',
  roleName: 'NoPermissionRole',
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [],
  effectivePermissionCodes: [],
})
const noPermissionExpoPreview = buildExpoRoleMenuPreview(noPermissionPreviewAccess, translate)

assertEqual(
  noPermissionExpoPreview.visibleRoutes.map((route) => route.routeName).join(','),
  'settings',
  'Role preview without Expo app permissions should only show settings',
)

assertEqual(
  noPermissionExpoPreview.storeChildren.length,
  0,
  'Role preview without Expo app permissions should not show store menu children',
)

assertEqual(
  filterExpoRoutesByVisibility(noPermissionExpoPreview.allRoutes, 'visible').map((route) => route.routeName).join(','),
  'settings',
  'Visible HbwebExpo filter should only show settings for roles without app permissions',
)

assertEqual(
  filterExpoRoutesByVisibility(noPermissionExpoPreview.allRoutes, 'hidden').some((route) => route.routeName === 'home'),
  true,
  'Hidden HbwebExpo filter should include protected routes for roles without app permissions',
)

const orderCreatorExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'order-creator-role',
    roleName: 'OrderCreatorRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [
      P.Orders.Create,
      P.Orders.View,
      P.StoreProducts.View,
      P.LocalPurchase.View,
      P.InstallmentOrders.View,
      P.StoreVouchers.View,
    ],
    effectivePermissionCodes: [
      P.Orders.Create,
      P.Orders.View,
      P.StoreProducts.View,
      P.LocalPurchase.View,
      P.InstallmentOrders.View,
      P.StoreVouchers.View,
    ],
  }),
  translate,
)

assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === 'home'),
  true,
  'Orders.Create should show the HbwebExpo home tab',
)

assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === 'cart'),
  true,
  'Orders.Create should show the HbwebExpo cart tab',
)

assertEqual(
  orderCreatorExpoPreview.visibleRoutes.some((route) => route.routeName === 'product-query'),
  true,
  'StoreProducts.View should show the HbwebExpo product-query tab',
)

assertEqual(
  orderCreatorExpoPreview.displayTabs.some((item) => item.type === 'store'),
  true,
  'Overflowing HbwebExpo store routes should collapse into the store menu',
)

assertEqual(
  orderCreatorExpoPreview.storeChildren.some((route) => route.routeName === 'installment-orders'),
  true,
  'Collapsed HbwebExpo store menu should include installment orders when permitted',
)

assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, 'visible').some((route) => route.routeName === 'local-supplier-invoices'),
  true,
  'Visible HbwebExpo filter should include authorized local supplier invoice route',
)

assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, 'hidden').some((route) => route.routeName === 'local-supplier-invoices'),
  false,
  'Hidden HbwebExpo filter should exclude authorized local supplier invoice route',
)

assertEqual(
  filterExpoRoutesByVisibility(orderCreatorExpoPreview.allRoutes, 'all').length,
  orderCreatorExpoPreview.allRoutes.length,
  'All HbwebExpo filter should keep the complete route permission list',
)

const attendanceExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'attendance-role',
    roleName: 'AttendanceRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.ScheduleViewSelf],
    effectivePermissionCodes: [P.Attendance.ScheduleViewSelf],
  }),
  translate,
)

assertEqual(
  attendanceExpoPreview.visibleRoutes.some((route) => route.routeName === 'attendance-personal'),
  true,
  'HbwebExpo legacy attendance app menu should expand to attendance-personal',
)

assertEqual(
  attendanceExpoPreview.visibleRoutes.some((route) => route.routeName === 'attendance'),
  false,
  'HbwebExpo preview should not expose the legacy attendance route directly',
)

const attendanceSelfByAvailabilityPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'attendance-self-availability-role',
    roleName: 'AttendanceSelfAvailabilityRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.AvailabilitySubmitSelf],
    effectivePermissionCodes: [P.Attendance.AvailabilitySubmitSelf],
  }),
  translate,
)

assertEqual(
  attendanceSelfByAvailabilityPreview.visibleRoutes.some((route) => route.routeName === 'attendance-personal'),
  true,
  'HbwebExpo attendance personal menu should follow AnyPermissions rules, not only ScheduleViewSelf',
)

const attendanceManagementPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'attendance-management-role',
    roleName: 'AttendanceManagementRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Attendance.ScheduleViewStore],
    effectivePermissionCodes: [P.Attendance.ScheduleViewStore],
  }),
  translate,
)

assertEqual(
  attendanceManagementPreview.visibleRoutes.some((route) => route.routeName === 'attendance-management'),
  true,
  'HbwebExpo attendance management menu should follow AnyPermissions rules for store attendance permissions',
)

const warehousePreviewAccess = buildRolePreviewAccess({
  roleGuid: 'warehouse-role',
  roleName: 'WarehouseRole',
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [P.Warehouse.ManageProducts],
  effectivePermissionCodes: [P.Warehouse.ManageProducts],
})

assertEqual(
  buildExpoRoleMenuPreview(warehousePreviewAccess, translate).visibleRoutes.some((route) => route.routeName === 'warehouse'),
  true,
  'Warehouse.ManageProducts should show the HbwebExpo warehouse tab',
)

const superAdminPreviewAccess = buildRolePreviewAccess({
  roleGuid: 'super-admin-role',
  roleName: 'CustomSuperAdmin',
  isSuperAdmin: true,
  implicitAllPermissions: true,
  explicitPermissionCodes: [],
  effectivePermissionCodes: [],
})
const superAdminExpoPreview = buildExpoRoleMenuPreview(superAdminPreviewAccess, translate)

assertEqual(
  superAdminExpoPreview.visibleRoutes.some((route) => route.routeName === 'device-management'),
  true,
  'Super admin role preview should show protected HbwebExpo app tabs',
)

assertEqual(
  superAdminExpoPreview.visibleRoutes.length > orderCreatorExpoPreview.visibleRoutes.length,
  true,
  'Super admin role preview should show more HbwebExpo app tabs than a limited order role',
)

const roleReaderWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'role-reader-role',
    roleName: 'RoleReaderRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Roles.View],
    effectivePermissionCodes: [P.Roles.View],
  }),
  translate,
)
const systemMenu = roleReaderWebPreview.find((node) => node.path === '/system')
const rolesMenu = systemMenu?.children?.find((node) => node.path === '/system/roles')
const roleReaderCompleteWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'role-reader-role',
    roleName: 'RoleReaderRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Roles.View],
    effectivePermissionCodes: [P.Roles.View],
  }),
  translate,
  {
    includeHidden: true,
  },
)
const roleReaderVisibleWebPreview = filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, 'visible')
const roleReaderHiddenWebPreview = filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, 'hidden')

assertEqual(
  systemMenu?.permissionCodes.length,
  0,
  'Parent Web menus without direct accessKey should keep an empty permission list',
)

assertEqual(
  rolesMenu?.permissionCodes.join(','),
  P.Roles.View,
  'Roles.View role preview should show Roles.View on the system roles Web menu',
)

assertEqual(
  rolesMenu?.accessKey,
  'canReadRole',
  'Web menu preview should expose the accessKey that controls the system roles menu',
)

assertEqual(
  filterWebMenuNodesByVisibility(roleReaderCompleteWebPreview, 'all').length,
  roleReaderCompleteWebPreview.length,
  'All Web menu filter should keep the complete desktop menu tree',
)

assertEqual(
  Boolean(findWebMenuNode(roleReaderVisibleWebPreview, '/system')),
  true,
  'Visible Web menu filter should keep parent context for authorized child menus',
)

assertEqual(
  Boolean(findWebMenuNode(roleReaderVisibleWebPreview, '/system/roles')),
  true,
  'Visible Web menu filter should include authorized desktop menu entries',
)

assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, '/system')),
  true,
  'Hidden Web menu filter should keep parent context for hidden child menus',
)

assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, '/system/stores')),
  true,
  'Hidden Web menu filter should include unauthorized desktop menu entries',
)

assertEqual(
  Boolean(findWebMenuNode(roleReaderHiddenWebPreview, '/system/roles')),
  false,
  'Hidden Web menu filter should exclude authorized desktop menu entries',
)

const warehouseManageCodes = getAccessKeyPermissionCodes('canManageWarehouseProducts')

assertEqual(
  warehouseManageCodes.join(','),
  `${P.Warehouse.ManageProducts},${P.Warehouse.Manage}`,
  'Warehouse product Web menu should document both fine-grained and legacy manager permissions',
)

const warehouseLegacyWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'warehouse-legacy-role',
    roleName: 'WarehouseLegacyRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Warehouse.Manage],
    effectivePermissionCodes: [P.Warehouse.Manage],
  }),
  translate,
)
const warehouseProductsMenu = warehouseLegacyWebPreview
  .find((node) => node.path === '/warehouse')
  ?.children?.find((node) => node.path === '/warehouse/products')

assertEqual(
  warehouseProductsMenu?.permissionCodes.join(','),
  `${P.Warehouse.ManageProducts},${P.Warehouse.Manage}`,
  'Warehouse.Manage role preview should show the warehouse products Web menu with both accepted permissions',
)

const superAdminWebPreview = buildWebRoleMenuPreview(superAdminPreviewAccess, translate)
const superAdminDeviceMenu = superAdminWebPreview
  .find((node) => node.path === '/system')
  ?.children?.find((node) => node.path === '/system/device-registration')

assertEqual(
  superAdminDeviceMenu?.permissionCodes.join(','),
  `${P.DeviceRegistration.View},${P.DeviceRegistration.Manage}`,
  'Super admin Web preview should show protected menus with their normal permission codes',
)

const advertisementViewerAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Advertisements.View],
  }),
)
const advertisementEditorOnlyAccess = buildAccess(
  createCurrentUser({
    permissions: [P.Advertisements.Edit],
  }),
)

assertEqual(
  advertisementViewerAccess.canManageAdvertisements,
  true,
  'Advertisements.View should unlock advertisement management page visibility',
)

assertEqual(
  advertisementEditorOnlyAccess.canManageAdvertisements,
  false,
  'Advertisements.Edit alone should not unlock advertisement list visibility',
)

assertEqual(
  advertisementViewerAccess.canEditAdvertisements,
  false,
  'Advertisements.View alone should not unlock advertisement editing',
)

const advertisementManageCodes = getAccessKeyPermissionCodes('canManageAdvertisements')

assertEqual(
  advertisementManageCodes.join(','),
  P.Advertisements.View,
  'Advertisement Web menu should document the view permission required by backend read APIs',
)

const advertisementPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'advertisement-role',
    roleName: 'AdvertisementRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: [P.Advertisements.View],
    effectivePermissionCodes: [P.Advertisements.View],
  }),
  translate,
)

const advertisementMenu = advertisementPreview
  .find((node) => node.path === '/pos-admin')
  ?.children?.find((node) => node.path === '/pos-admin/advertisements')

assertEqual(
  advertisementMenu?.permissionCodes.join(','),
  P.Advertisements.View,
  'Advertisements.View role preview should show the advertisement menu with backend read permission',
)

const legacyLocalInvoiceWebPreview = buildWebRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'legacy-local-invoice-role',
    roleName: 'LegacyLocalInvoiceRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: ['LocalInvocie.View'],
    effectivePermissionCodes: ['LocalInvocie.View', P.LocalPurchase.View],
  }),
  translate,
  {
    includeHidden: true,
    explicitPermissionCodes: ['LocalInvocie.View'],
  },
)
const legacyLocalInvoiceMenu = legacyLocalInvoiceWebPreview
  .find((node) => node.path === '/pos-admin')
  ?.children?.find((node) => node.path === '/pos-admin/local-supplier-invoices')

assertEqual(
  legacyLocalInvoiceMenu?.edit.removePermissionCodes.includes('LocalInvocie.View'),
  true,
  'Web preview remove action should include legacy LocalInvocie.View so old roles can hide the menu',
)

const legacyLocalInvoiceRemovedPermissions = applyRolePermissionMutation({
  currentPermissionCodes: ['LocalInvocie.View'],
  removePermissionCodes: legacyLocalInvoiceMenu?.edit.removePermissionCodes ?? [],
})

assertEqual(
  legacyLocalInvoiceRemovedPermissions.length,
  0,
  'Removing the legacy local invoice menu should delete the effective legacy permission assignment',
)

const hiddenLocalInvoiceAfterLegacyRemoval = filterWebMenuNodesByVisibility(
  buildWebRoleMenuPreview(
    buildRolePreviewAccess({
      roleGuid: 'legacy-local-invoice-removed-role',
      roleName: 'LegacyLocalInvoiceRemovedRole',
      isSuperAdmin: false,
      implicitAllPermissions: false,
      explicitPermissionCodes: legacyLocalInvoiceRemovedPermissions,
      effectivePermissionCodes: legacyLocalInvoiceRemovedPermissions,
    }),
    translate,
    {
      includeHidden: true,
      explicitPermissionCodes: legacyLocalInvoiceRemovedPermissions,
    },
  ),
  'hidden',
)

assertEqual(
  Boolean(findWebMenuNode(hiddenLocalInvoiceAfterLegacyRemoval, '/pos-admin/local-supplier-invoices')),
  true,
  'Hidden Web menu filter should include local supplier invoices after removing the legacy LocalInvocie.View alias',
)

const legacyLocalInvoiceExpoPreview = buildExpoRoleMenuPreview(
  buildRolePreviewAccess({
    roleGuid: 'legacy-local-invoice-expo-role',
    roleName: 'LegacyLocalInvoiceExpoRole',
    isSuperAdmin: false,
    implicitAllPermissions: false,
    explicitPermissionCodes: ['LocalInvocie.View'],
    effectivePermissionCodes: ['LocalInvocie.View', P.LocalPurchase.View],
  }),
  translate,
  {
    explicitPermissionCodes: ['LocalInvocie.View'],
  },
)
const legacyLocalInvoiceExpoRoute = legacyLocalInvoiceExpoPreview.allRoutes.find(
  (route) => route.routeName === 'local-supplier-invoices',
)

assertEqual(
  legacyLocalInvoiceExpoRoute?.removePermissionCodes.includes('LocalInvocie.View'),
  true,
  'HbwebExpo preview remove action should include legacy LocalInvocie.View so old roles can hide the menu',
)

assertEqual(
  applyRolePermissionMutation({
    currentPermissionCodes: ['LocalInvocie.View'],
    removePermissionCodes: legacyLocalInvoiceExpoRoute?.removePermissionCodes ?? [],
  }).length,
  0,
  'Removing the legacy local invoice HbwebExpo menu should delete the effective legacy permission assignment',
)

const localPurchasePushAccess = buildAccess(
  createCurrentUser({
    permissions: [P.LocalPurchase.PushToHq],
  }),
)

assertEqual(
  localPurchasePushAccess.canPushLocalPurchaseToHq,
  true,
  'LocalPurchase.PushToHq should expose the HQ write permission flag',
)

assertEqual(
  localPurchasePushAccess.canEditLocalPurchase && localPurchasePushAccess.canPushLocalPurchaseToHq,
  false,
  'LocalPurchase.PushToHq alone should not be treated as full edit-page HQ write access',
)

const localPurchaseViewOnlyAccess = buildAccess(
  createCurrentUser({
    permissions: [P.LocalPurchase.View],
  }),
)

assertEqual(
  localPurchaseViewOnlyAccess.canPushLocalPurchaseToHq,
  false,
  'LocalPurchase.View should not unlock HQ write actions',
)

console.log('access.permission.test: ok')
