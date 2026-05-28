import type { UserPermissionInheritedSourceDto, UserPermissionStateDto } from '../../../types/user'

export function uniquePermissionCodes(permissionCodes: string[]): string[] {
  return Array.from(new Set(permissionCodes))
}

export function getCheckedPermissionKeys(permissionState: UserPermissionStateDto | null): string[] {
  if (!permissionState) return []
  return uniquePermissionCodes([
    ...permissionState.inheritedPermissionCodes,
    ...permissionState.directPermissionCodes,
  ])
}

export function buildFallbackUserPermissionState({
  userGuid,
  permissions,
}: {
  userGuid: string
  permissions?: string[]
}): UserPermissionStateDto {
  const effectivePermissionCodes = uniquePermissionCodes(permissions ?? [])

  return {
    userGuid,
    inheritedPermissionCodes: effectivePermissionCodes,
    directPermissionCodes: [],
    effectivePermissionCodes,
    inheritedSources: [],
  }
}

export function buildDirectPermissionPayload(permissionCodes: string[]): string[] {
  return uniquePermissionCodes(permissionCodes)
}

export function toggleDirectPermission({
  currentDirectPermissions,
  inheritedPermissionCodes,
  permissionCode,
  checked,
}: {
  currentDirectPermissions: string[]
  inheritedPermissionCodes: string[]
  permissionCode: string
  checked: boolean
}): string[] {
  const next = new Set(currentDirectPermissions)

  if (checked) {
    next.add(permissionCode)
  } else {
    next.delete(permissionCode)
  }

  if (!checked && inheritedPermissionCodes.includes(permissionCode)) {
    next.delete(permissionCode)
  }

  return buildDirectPermissionPayload(Array.from(next))
}

export function deriveDirectPermissionKeysFromChecked({
  checkedPermissionKeys,
  allPermissionCodes,
  inheritedPermissionCodes,
  currentDirectPermissionCodes,
}: {
  checkedPermissionKeys: string[]
  allPermissionCodes: string[]
  inheritedPermissionCodes: string[]
  currentDirectPermissionCodes: string[]
}): string[] {
  const checkedSet = new Set(checkedPermissionKeys)
  const inheritedSet = new Set(inheritedPermissionCodes)
  const currentDirectSet = new Set(currentDirectPermissionCodes)

  return buildDirectPermissionPayload(
    allPermissionCodes.filter((permissionCode) =>
      checkedSet.has(permissionCode) &&
      (!inheritedSet.has(permissionCode) || currentDirectSet.has(permissionCode)),
    ),
  )
}

export function buildPermissionSourceMap(
  inheritedSources: UserPermissionInheritedSourceDto[],
): Record<string, string[]> {
  const sourceMap: Record<string, string[]> = {}

  inheritedSources.forEach((source) => {
    source.permissionCodes.forEach((permissionCode) => {
      sourceMap[permissionCode] = [...(sourceMap[permissionCode] ?? []), source.roleName]
    })
  })

  return sourceMap
}

export function arePermissionSetsEqual(left: string[], right: string[]): boolean {
  const leftSet = new Set(left)
  const rightSet = new Set(right)

  if (leftSet.size !== rightSet.size) return false

  for (const item of leftSet) {
    if (!rightSet.has(item)) return false
  }

  return true
}
