import type { AccessControl, CurrentUser } from '../../../types/auth'
import type { RoleOptionDto } from '../../../types/role'
import type { UserDto, UserStoreAssignmentDto, UserStoreDto } from '../../../types/user'

type StoreIdentity = Pick<UserStoreDto, 'storeGUID'>
type UserRoleIdentity = Pick<UserDto, 'roleNames'>
type RoleIdentity = Pick<RoleOptionDto, 'roleGUID' | 'roleName'>

const SCOPED_MANAGER_FORBIDDEN_ROLE_NAMES = new Set([
  'admin',
  '管理员',
  'superadmin',
  '超级管理员',
  'storemanager',
  '店长',
  '经理',
  'warehousemanager',
  '仓库经理',
  '仓库管理员',
])

function normalizeRoleName(roleName: string | undefined) {
  return roleName?.trim().toLowerCase() ?? ''
}

export function isForbiddenRoleForScopedManager(roleName: string | undefined) {
  return SCOPED_MANAGER_FORBIDDEN_ROLE_NAMES.has(normalizeRoleName(roleName))
}

export function hasForbiddenRoleForScopedManager(user: UserRoleIdentity) {
  return (user.roleNames ?? []).some((roleName) => isForbiddenRoleForScopedManager(roleName))
}

export function filterUsersVisibleToScopedManager<T extends UserRoleIdentity>(users: T[]) {
  return users.filter((user) => !hasForbiddenRoleForScopedManager(user))
}

export function filterRoleOptionsForScopedManager<T extends RoleIdentity>(roles: T[]) {
  return roles.filter((role) => !isForbiddenRoleForScopedManager(role.roleName))
}

export function areRoleGuidsAllowedForScopedManager(
  selectedRoleGuids: string[],
  availableRoles: RoleIdentity[],
) {
  const allowedRoleGuidSet = new Set(filterRoleOptionsForScopedManager(availableRoles).map((role) => role.roleGUID))
  return selectedRoleGuids.every((roleGuid) => allowedRoleGuidSet.has(roleGuid))
}

export function isScopedStoreManager(
  currentUser: CurrentUser | null | undefined,
  access: AccessControl,
) {
  return Boolean(currentUser && access.isStoreLevelManager)
}

export function getManagedStores(
  currentUser: CurrentUser | null | undefined,
  access: AccessControl,
) {
  if (!isScopedStoreManager(currentUser, access)) {
    return []
  }

  return (currentUser?.stores ?? []).filter((store) => store.isManageable && Boolean(store.storeGUID))
}

export function isStoreVisibleToManager(storeGuid: string | undefined, managedStores: StoreIdentity[]) {
  if (!storeGuid) {
    return false
  }

  return managedStores.some((store) => store.storeGUID === storeGuid)
}

export function getScopedStoreGuidsForQuery(
  selectedStoreGuid: string | undefined,
  managedStores: StoreIdentity[],
) {
  if (selectedStoreGuid) {
    if (isStoreVisibleToManager(selectedStoreGuid, managedStores)) {
      return [selectedStoreGuid]
    }
  }

  return managedStores.map((store) => store.storeGUID).filter(Boolean)
}

export function mergeUsersByGuid<T extends Pick<UserDto, 'userGUID'>>(users: T[]) {
  const merged = new Map<string, T>()

  users.forEach((user) => {
    if (!merged.has(user.userGUID)) {
      merged.set(user.userGUID, user)
    }
  })

  return Array.from(merged.values())
}

export function filterStoresForManager<T extends StoreIdentity>(stores: T[], managedStores: StoreIdentity[]) {
  const managedStoreGuids = new Set(managedStores.map((store) => store.storeGUID))
  return stores.filter((store) => managedStoreGuids.has(store.storeGUID))
}

export function buildScopedStoreAssignments(
  existingStores: UserStoreDto[],
  selectedManagedStoreGuids: string[],
  manageableStoreGuids: string[],
  managedStores: StoreIdentity[],
): UserStoreAssignmentDto[] {
  const managedStoreGuids = new Set(managedStores.map((store) => store.storeGUID))
  const manageableStoreGuidSet = new Set(manageableStoreGuids)
  const assignments = new Map<string, UserStoreAssignmentDto>()

  existingStores.forEach((store) => {
    if (store.storeGUID && !managedStoreGuids.has(store.storeGUID)) {
      assignments.set(store.storeGUID, {
        storeGUID: store.storeGUID,
        accessLevel: 'ReadWrite',
        isManageable: store.isManageable,
      })
    }
  })

  selectedManagedStoreGuids.forEach((storeGUID) => {
    if (managedStoreGuids.has(storeGUID)) {
      assignments.set(storeGUID, {
        storeGUID,
        accessLevel: 'ReadWrite',
        isManageable: manageableStoreGuidSet.has(storeGUID),
      })
    }
  })

  return Array.from(assignments.values())
}
