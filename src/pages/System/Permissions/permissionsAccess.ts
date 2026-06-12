import type { AccessControl } from '../../../types/auth'
import { P } from '../../../types/permissions'

export function canManageSystemPermissions(access: Pick<AccessControl, 'hasPermission'>) {
  return access.hasPermission(P.Roles.ManagePermissions)
}
