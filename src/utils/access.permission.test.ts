import type { CurrentUser } from '../types/auth'
import { P } from '../types/permissions'
import { buildAccess } from './access'
import { buildExpoRoleMenuPreview } from './expoRoleMenuPreview'
import { buildRolePreviewAccess } from './roleMenuPreview'

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

console.log('access.permission.test: ok')
