import type { CurrentUser } from '../../../types/auth'
import type { UserDto, UserStoreDto } from '../../../types/user'
import { buildAccess } from '../../../utils/access'
import {
  areRoleGuidsAllowedForScopedManager,
  buildScopedStoreAssignments,
  filterRoleOptionsForScopedManager,
  filterStoresForManager,
  filterUsersVisibleToScopedManager,
  getManagedStores,
  getScopedStoreGuidsForQuery,
  hasForbiddenRoleForScopedManager,
  isForbiddenRoleForScopedManager,
  isScopedStoreManager,
  isStoreVisibleToManager,
  mergeUsersByGuid,
} from './userScope'
import type { RoleOptionDto } from '../../../types/role'

function createStore(overrides: Partial<UserStoreDto>): UserStoreDto {
  return {
    storeGUID: 'store-a-guid',
    storeName: 'Store A',
    storeCode: 'A',
    isManageable: false,
    assignedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'current-user-guid',
    username: 'storemanager',
    email: 'storemanager@example.com',
    permissions: [],
    roleNames: ['StoreManager'],
    storeNames: [],
    stores: [],
    ...overrides,
  }
}

function createUser(overrides: Partial<UserDto>): UserDto {
  return {
    userGUID: 'user-a-guid',
    username: 'user-a',
    email: 'user-a@example.com',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

function createRoleOption(overrides: Partial<RoleOptionDto>): RoleOptionDto {
  return {
    roleGUID: 'role-user-guid',
    roleName: 'User',
    description: '',
    isActive: true,
    ...overrides,
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertArrayEqual<T>(actual: T[], expected: T[], message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

const manageableStore = createStore({
  storeGUID: 'managed-store-guid',
  storeName: 'Managed Store',
  storeCode: 'M1',
  isManageable: true,
})
const linkedOnlyStore = createStore({
  storeGUID: 'linked-store-guid',
  storeName: 'Linked Store',
  storeCode: 'L1',
  isManageable: false,
})

const storeManager = createCurrentUser({
  stores: [manageableStore, linkedOnlyStore],
})
const storeManagerAccess = buildAccess(storeManager)

assertEqual(
  isScopedStoreManager(storeManager, storeManagerAccess),
  true,
  'StoreManager should use scoped user visibility',
)

assertArrayEqual(
  getManagedStores(storeManager, storeManagerAccess).map((store) => store.storeGUID),
  ['managed-store-guid'],
  'StoreManager scope should include only manageable stores',
)

const chineseStoreManager = createCurrentUser({
  roleNames: ['店长'],
  stores: [manageableStore, linkedOnlyStore],
})
const chineseStoreManagerAccess = buildAccess(chineseStoreManager)

assertEqual(
  isScopedStoreManager(chineseStoreManager, chineseStoreManagerAccess),
  true,
  'Chinese 店长 role should use scoped user visibility',
)

assertArrayEqual(
  getManagedStores(chineseStoreManager, chineseStoreManagerAccess).map((store) => store.storeGUID),
  ['managed-store-guid'],
  'Chinese 店长 scope should include only manageable stores',
)

const managerAlias = createCurrentUser({
  roleNames: ['经理'],
  stores: [manageableStore, linkedOnlyStore],
})
const managerAliasAccess = buildAccess(managerAlias)

assertEqual(
  isScopedStoreManager(managerAlias, managerAliasAccess),
  true,
  'Chinese 经理 role should use scoped user visibility',
)

assertArrayEqual(
  getManagedStores(managerAlias, managerAliasAccess).map((store) => store.storeGUID),
  ['managed-store-guid'],
  'Chinese 经理 scope should include only manageable stores',
)

assertEqual(
  isStoreVisibleToManager('managed-store-guid', getManagedStores(storeManager, storeManagerAccess)),
  true,
  'Manageable store should be visible to the manager',
)

assertEqual(
  isStoreVisibleToManager('linked-store-guid', getManagedStores(storeManager, storeManagerAccess)),
  false,
  'Linked-only store should not be visible to the manager',
)

assertArrayEqual(
  getScopedStoreGuidsForQuery(undefined, [
    manageableStore,
    createStore({ storeGUID: 'second-managed-store-guid', isManageable: true }),
  ]),
  ['managed-store-guid', 'second-managed-store-guid'],
  'Scoped query without a selected store should include all manageable stores',
)

assertArrayEqual(
  getScopedStoreGuidsForQuery('second-managed-store-guid', [
    manageableStore,
    createStore({ storeGUID: 'second-managed-store-guid', isManageable: true }),
  ]),
  ['second-managed-store-guid'],
  'Scoped query with a selected manageable store should query only that store',
)

assertArrayEqual(
  getScopedStoreGuidsForQuery('linked-store-guid', [manageableStore]),
  ['managed-store-guid'],
  'Scoped query should fall back to the first manageable store when selected store is outside scope',
)

const admin = createCurrentUser({
  roleNames: ['Admin', 'StoreManager'],
  stores: [manageableStore],
})
const adminAccess = buildAccess(admin)

assertEqual(
  isScopedStoreManager(admin, adminAccess),
  false,
  'Admin should not be limited by store manager scope',
)

const chineseAdminWithManagerAlias = createCurrentUser({
  roleNames: ['管理员', '经理'],
  stores: [manageableStore],
})
const chineseAdminWithManagerAliasAccess = buildAccess(chineseAdminWithManagerAlias)

assertEqual(
  isScopedStoreManager(chineseAdminWithManagerAlias, chineseAdminWithManagerAliasAccess),
  false,
  'Chinese admin should not be limited by manager alias scope',
)

const warehouseManager = createCurrentUser({
  roleNames: ['WarehouseManager', 'StoreManager'],
  stores: [manageableStore],
})
const warehouseManagerAccess = buildAccess(warehouseManager)

assertEqual(
  isScopedStoreManager(warehouseManager, warehouseManagerAccess),
  false,
  'WarehouseManager should not be limited by store manager scope',
)

const chineseWarehouseManagerWithManagerAlias = createCurrentUser({
  roleNames: ['仓库经理', '经理'],
  stores: [manageableStore],
})
const chineseWarehouseManagerWithManagerAliasAccess = buildAccess(chineseWarehouseManagerWithManagerAlias)

assertEqual(
  isScopedStoreManager(chineseWarehouseManagerWithManagerAlias, chineseWarehouseManagerWithManagerAliasAccess),
  false,
  'Chinese warehouse manager should not be limited by manager alias scope',
)

assertArrayEqual(
  mergeUsersByGuid([
    createUser({ userGUID: 'user-a-guid', username: 'first-a' }),
    createUser({ userGUID: 'user-b-guid', username: 'user-b' }),
    createUser({ userGUID: 'user-a-guid', username: 'second-a' }),
  ]).map((user) => user.username),
  ['first-a', 'user-b'],
  'Merged users should be de-duplicated by userGUID while keeping first occurrence',
)

assertEqual(
  isForbiddenRoleForScopedManager('管理员'),
  true,
  'Chinese admin role should be forbidden for scoped store managers',
)

assertEqual(
  isForbiddenRoleForScopedManager('仓库管理员'),
  true,
  'Chinese warehouse admin alias should be forbidden for scoped store managers',
)

assertEqual(
  isForbiddenRoleForScopedManager('SuperAdmin'),
  true,
  'SuperAdmin role should be forbidden for scoped store managers',
)

assertEqual(
  isForbiddenRoleForScopedManager('超级管理员'),
  true,
  'Chinese super administrator role should be forbidden for scoped store managers',
)

assertEqual(
  hasForbiddenRoleForScopedManager(createUser({ roleNames: ['收银员', '店长'] })),
  true,
  'Users with manager-level roles should be hidden from scoped store managers',
)

assertEqual(
  hasForbiddenRoleForScopedManager(createUser({ roleNames: ['收银员'] })),
  false,
  'Ordinary users should remain visible to scoped store managers',
)

assertArrayEqual(
  filterUsersVisibleToScopedManager([
    createUser({ userGUID: 'visible-user-guid', username: 'visible-user', roleNames: ['收银员'] }),
    createUser({ userGUID: 'admin-user-guid', username: 'admin-user', roleNames: ['管理员'] }),
    createUser({ userGUID: 'warehouse-user-guid', username: 'warehouse-user', roleNames: ['仓库经理'] }),
  ]).map((user) => user.userGUID),
  ['visible-user-guid'],
  'Scoped store manager list should exclude users with forbidden roles',
)

const roleOptions = [
  createRoleOption({ roleGUID: 'role-user-guid', roleName: 'User' }),
  createRoleOption({ roleGUID: 'role-admin-guid', roleName: 'Admin' }),
  createRoleOption({ roleGUID: 'role-store-manager-guid', roleName: '店长' }),
  createRoleOption({ roleGUID: 'role-warehouse-guid', roleName: '仓库管理员' }),
]

assertArrayEqual(
  filterRoleOptionsForScopedManager(roleOptions).map((role) => role.roleGUID),
  ['role-user-guid'],
  'Scoped store manager role options should exclude forbidden roles',
)

assertEqual(
  areRoleGuidsAllowedForScopedManager(['role-user-guid'], roleOptions),
  true,
  'Scoped store manager should be able to assign allowed roles',
)

assertEqual(
  areRoleGuidsAllowedForScopedManager(['role-user-guid', 'role-admin-guid'], roleOptions),
  false,
  'Scoped store manager should not be able to assign forbidden roles',
)

assertArrayEqual(
  filterStoresForManager([manageableStore, linkedOnlyStore], [manageableStore]).map((store) => store.storeGUID),
  ['managed-store-guid'],
  'Detail store filtering should keep only manageable stores',
)

assertArrayEqual(
  getManagedStores(createCurrentUser({ stores: [linkedOnlyStore] }), storeManagerAccess),
  [],
  'StoreManager without manageable stores should have an empty visible scope',
)

const hiddenStore = createStore({
  storeGUID: 'hidden-store-guid',
  storeName: 'Hidden Store',
  storeCode: 'H1',
  isManageable: false,
})

assertArrayEqual(
  buildScopedStoreAssignments(
    [manageableStore, hiddenStore],
    ['linked-store-guid'],
    ['linked-store-guid'],
    [manageableStore, linkedOnlyStore],
  ),
  [
    { storeGUID: 'hidden-store-guid', accessLevel: 'ReadWrite', isManageable: false },
    { storeGUID: 'linked-store-guid', accessLevel: 'ReadWrite', isManageable: true },
  ],
  'Scoped store saves should preserve hidden stores while replacing only managed scope',
)
