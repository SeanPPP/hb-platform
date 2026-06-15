interface MenuNodeLike {
  key?: unknown
  path?: unknown
  children?: unknown
}

function isMenuNodeLike(item: unknown): item is MenuNodeLike {
  return typeof item === 'object' && item !== null
}

function hasMenuChildren(item: unknown): item is MenuNodeLike & { children: unknown[] } {
  if (!isMenuNodeLike(item)) {
    return false
  }
  return Array.isArray(item.children) && item.children.length > 0
}

function countMenuLeaves(items: unknown[] | undefined): number {
  if (!items?.length) {
    return 0
  }

  return items.reduce<number>((count, item) => {
    if (!isMenuNodeLike(item)) {
      return count
    }
    if (hasMenuChildren(item)) {
      return count + countMenuLeaves(item.children)
    }
    return count + 1
  }, 0)
}

function getMenuLeafKey(item: MenuNodeLike): string | undefined {
  const key = item.key ?? item.path
  if (typeof key === 'string' || typeof key === 'number') {
    return String(key)
  }
  return undefined
}

function collectMenuLeafKeys(items: unknown[] | undefined): string[] {
  if (!items?.length) {
    return []
  }

  return items.flatMap((item) => {
    if (!isMenuNodeLike(item)) {
      return []
    }
    if (hasMenuChildren(item)) {
      return collectMenuLeafKeys(item.children)
    }
    const key = getMenuLeafKey(item)
    return key ? [key] : []
  })
}

export function chooseNavigationMenus<TMenuItem>(
  localMenus: TMenuItem[] | undefined,
  backendMenus: TMenuItem[] | undefined,
): TMenuItem[] | undefined {
  if (!backendMenus?.length) {
    return localMenus
  }

  const localLeafKeys = collectMenuLeafKeys(localMenus as unknown[])
  const backendLeafKeys = new Set(collectMenuLeafKeys(backendMenus as unknown[]))
  const backendMissesLocalLeaf = localLeafKeys.some((key) => !backendLeafKeys.has(key))

  // 后端菜单通常是权限裁剪后的权威结果；只有缺少本地已授权叶子菜单时才回退本地菜单。
  return backendMissesLocalLeaf || countMenuLeaves(backendMenus) === 0
    ? localMenus
    : backendMenus
}
