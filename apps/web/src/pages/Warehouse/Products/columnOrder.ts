export type WarehouseProductTableColumnKey = string

export function mergeWarehouseProductColumnOrder(
  savedOrder: readonly unknown[] | null | undefined,
  availableOrder: readonly WarehouseProductTableColumnKey[],
): WarehouseProductTableColumnKey[] {
  const availableSet = new Set(availableOrder)
  const seen = new Set<WarehouseProductTableColumnKey>()
  const merged: WarehouseProductTableColumnKey[] = []

  for (const value of savedOrder ?? []) {
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

export function moveWarehouseProductColumnOrder(
  currentOrder: readonly WarehouseProductTableColumnKey[],
  activeKey: unknown,
  overKey: unknown,
): WarehouseProductTableColumnKey[] {
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

export function isWarehouseProductColumnOrderCustomized(
  currentOrder: readonly WarehouseProductTableColumnKey[],
  defaultOrder: readonly WarehouseProductTableColumnKey[],
): boolean {
  const normalizedOrder = mergeWarehouseProductColumnOrder(currentOrder, defaultOrder)

  return normalizedOrder.length !== defaultOrder.length ||
    normalizedOrder.some((key, index) => key !== defaultOrder[index])
}
