import { P } from '../../../types/permissions'
import { buildAccess } from '../../../utils/access'
import { canManageSystemPermissions } from './permissionsAccess'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const readOnlyAccess = buildAccess({
  userGUID: 'read-only',
  username: 'read-only',
  email: 'read-only@example.test',
  permissions: [P.Roles.View],
  roleNames: [],
  storeNames: [],
})

assertEqual(
  readOnlyAccess.canReadRole,
  true,
  'Roles.View 用户应继续可进入权限只读页面',
)
assertEqual(
  canManageSystemPermissions(readOnlyAccess),
  false,
  'Roles.View 用户不应拥有权限页写操作',
)

const permissionManagerAccess = buildAccess({
  userGUID: 'permission-manager',
  username: 'permission-manager',
  email: 'permission-manager@example.test',
  permissions: [P.Roles.ManagePermissions],
  roleNames: [],
  storeNames: [],
})

assertEqual(
  canManageSystemPermissions(permissionManagerAccess),
  true,
  'Roles.ManagePermissions 用户应可执行权限页写操作',
)

const adminAccess = buildAccess({
  userGUID: 'admin',
  username: 'admin',
  email: 'admin@example.test',
  permissions: [],
  roleNames: ['Admin'],
  storeNames: [],
})

assertEqual(
  canManageSystemPermissions(adminAccess),
  true,
  'Admin 应继续拥有权限页写操作',
)

console.log('permissionsAccess.test: ok')
