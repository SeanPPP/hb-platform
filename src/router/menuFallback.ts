interface MenuNodeLike {
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

export function chooseNavigationMenus<TMenuItem>(
  localMenus: TMenuItem[] | undefined,
  backendMenus: TMenuItem[] | undefined,
): TMenuItem[] | undefined {
  if (!backendMenus?.length) {
    return localMenus
  }

  return countMenuLeaves(backendMenus) < countMenuLeaves(localMenus)
    ? localMenus
    : backendMenus
}
