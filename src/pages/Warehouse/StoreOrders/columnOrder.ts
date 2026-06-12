export type StoreOrderListTableColumnKey = string

export function mergeStoreOrderListColumnOrder(
  savedOrder: unknown,
  availableOrder: readonly StoreOrderListTableColumnKey[],
): StoreOrderListTableColumnKey[] {
  const availableSet = new Set(availableOrder)
  const seen = new Set<StoreOrderListTableColumnKey>()
  const merged: StoreOrderListTableColumnKey[] = []
  // localStorage 可能被写入合法但非数组的 JSON，统一在合并入口兜底，避免页面初始化崩溃。
  const savedValues = Array.isArray(savedOrder) ? savedOrder : []

  for (const value of savedValues) {
    if (typeof value !== 'string' || !availableSet.has(value)) {
      continue
    }
    if (seen.has(value)) {
      continue
    }
    seen.add(value)
    merged.push(value)
  }

  for (const key of availableOrder) {
    if (!seen.has(key)) {
      merged.push(key)
    }
  }

  return merged
}

export function moveStoreOrderListColumnOrder(
  currentOrder: readonly StoreOrderListTableColumnKey[],
  activeKey: unknown,
  overKey: unknown,
): StoreOrderListTableColumnKey[] {
  if (typeof activeKey !== 'string' || typeof overKey !== 'string' || activeKey === overKey) {
    return [...currentOrder]
  }

  const fromIndex = currentOrder.indexOf(activeKey)
  const toIndex = currentOrder.indexOf(overKey)
  if (fromIndex < 0 || toIndex < 0) {
    return [...currentOrder]
  }

  const nextOrder = [...currentOrder]
  const [moved] = nextOrder.splice(fromIndex, 1)
  nextOrder.splice(toIndex, 0, moved)
  return nextOrder
}

export function isStoreOrderListColumnOrderCustomized(
  currentOrder: readonly StoreOrderListTableColumnKey[],
  defaultOrder: readonly StoreOrderListTableColumnKey[],
): boolean {
  if (!currentOrder.length) {
    return false
  }

  const normalizedOrder = mergeStoreOrderListColumnOrder(currentOrder, defaultOrder)
  return normalizedOrder.length !== defaultOrder.length ||
    normalizedOrder.some((key, index) => key !== defaultOrder[index])
}
