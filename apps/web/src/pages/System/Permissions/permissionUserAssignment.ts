import type { PagedResult } from '../../../types/api'
import type { PermissionUserAssignmentDto, RoleUserDto } from '../../../types/role'
import type { UserDto } from '../../../types/user'

export interface PermissionUserOption {
  key: string
  title: string
  description: string
  isActive: boolean
}

type UserLike = Pick<UserDto, 'userGUID' | 'username' | 'email' | 'fullName' | 'isActive'>

function toOption(user: UserLike): PermissionUserOption {
  return {
    key: user.userGUID,
    title: user.fullName ? `${user.username} / ${user.fullName}` : user.username,
    description: user.email,
    isActive: user.isActive,
  }
}

/**
 * 合并「全部用户」与「已直接授权用户」作为穿梭框数据源。
 * 已授权用户若不在已加载列表中（例如分页上限截断）也要补进来，否则穿梭框会隐藏右侧已选项。
 */
export function buildPermissionUserOptions(allUsers: UserLike[], assignedUsers: UserLike[]): PermissionUserOption[] {
  const options = new Map<string, PermissionUserOption>()
  for (const user of [...allUsers, ...assignedUsers]) {
    if (!user.userGUID || options.has(user.userGUID)) continue
    options.set(user.userGUID, toOption(user))
  }
  return Array.from(options.values()).sort((a, b) => a.title.localeCompare(b.title))
}

export function matchesPermissionUserKeyword(option: PermissionUserOption, keyword: string) {
  const normalized = keyword.trim().toLowerCase()
  if (!normalized) return true
  return [option.title, option.description].some((value) => value.toLowerCase().includes(normalized))
}

/**
 * 计算保存时的增量；后端按增量写入，未加载或未改动的用户授权保持不变。
 */
export function buildPermissionUserDelta(baseline: Iterable<string>, target: Iterable<string>): PermissionUserAssignmentDto {
  const baselineSet = new Set(baseline)
  const targetSet = new Set(target)
  return {
    addUserGuids: [...targetSet].filter((guid) => !baselineSet.has(guid)),
    removeUserGuids: [...baselineSet].filter((guid) => !targetSet.has(guid)),
  }
}

export function isPermissionUserDeltaEmpty(delta: PermissionUserAssignmentDto) {
  return delta.addUserGuids.length === 0 && delta.removeUserGuids.length === 0
}

export function toAssignedUserGuids(users: Pick<RoleUserDto, 'userGUID'>[]) {
  return users.map((user) => user.userGUID)
}

/** 用户列表接口分页返回，这里逐页拉取；设置页数上限防止异常分页元数据导致死循环。 */
export async function loadAllUserPages<T>(
  fetchPage: (page: number) => Promise<PagedResult<T>>,
  maxPages = 50,
): Promise<T[]> {
  const items: T[] = []
  for (let page = 1; page <= maxPages; page += 1) {
    const result = await fetchPage(page)
    items.push(...result.items)
    const totalPages = result.totalPages ?? Math.ceil(result.total / Math.max(result.pageSize, 1))
    if (result.items.length === 0 || page >= totalPages) break
  }
  return items
}
