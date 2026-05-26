import type { AccessControl, CurrentUser } from '../types/auth'
import type { RolePermissionStateDto } from '../types/role'
import { buildAccess } from './access'

export function buildRolePreviewAccess(permissionState: RolePermissionStateDto): AccessControl {
  const roleNames = permissionState.isSuperAdmin
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
