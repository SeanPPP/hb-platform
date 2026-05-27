import type { AccessControl, CurrentUser } from '../types/auth'
import type { RolePermissionStateDto } from '../types/role'
import { buildAccess } from './access'

const IMPLICIT_ALL_ROLE_NAMES = new Set(['admin', '管理员'])

export function isImplicitAllRole(permissionState: RolePermissionStateDto): boolean {
  return (
    permissionState.isSuperAdmin ||
    permissionState.implicitAllPermissions ||
    IMPLICIT_ALL_ROLE_NAMES.has(permissionState.roleName.trim().toLowerCase())
  )
}

export function applyRolePermissionMutation({
  currentPermissionCodes,
  addPermissionCodes = [],
  removePermissionCodes = [],
}: {
  currentPermissionCodes: string[]
  addPermissionCodes?: string[]
  removePermissionCodes?: string[]
}): string[] {
  const nextCodes = currentPermissionCodes.filter((code) => !removePermissionCodes.includes(code))
  const nextCodeSet = new Set(nextCodes)

  addPermissionCodes.forEach((code) => {
    if (!nextCodeSet.has(code)) {
      nextCodes.push(code)
      nextCodeSet.add(code)
    }
  })

  return nextCodes
}

export function buildRolePreviewAccess(permissionState: RolePermissionStateDto): AccessControl {
  const roleNames = isImplicitAllRole(permissionState)
    ? ['Admin', permissionState.roleName]
    : [permissionState.roleName]

  const previewUser: CurrentUser = {
    userGUID: `role-preview-${permissionState.roleGuid}`,
    username: permissionState.roleName,
    email: '',
    permissions: permissionState.effectivePermissionCodes,
    roleNames,
    storeNames: [],
  }

  return buildAccess(previewUser)
}
