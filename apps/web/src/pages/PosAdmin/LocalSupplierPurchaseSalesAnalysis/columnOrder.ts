export type LocalSupplierPurchaseSalesAnalysisColumnKey = string

export const LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER = [
  'supplierName',
  'previousPurchaseDate',
  'latestPurchaseDate',
  'purchaseIntervalDays',
  'salesBetweenPurchases',
  'salesQty30',
  'salesQty60',
  'salesQty90',
  'salesStatisticLastUpdate',
] as const

export function mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(
  savedOrder: unknown,
  availableOrder: readonly LocalSupplierPurchaseSalesAnalysisColumnKey[] =
    LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER,
): LocalSupplierPurchaseSalesAnalysisColumnKey[] {
  const availableSet = new Set(availableOrder)
  const seen = new Set<LocalSupplierPurchaseSalesAnalysisColumnKey>()
  const merged: LocalSupplierPurchaseSalesAnalysisColumnKey[] = []
  // localStorage 可能保存旧版本或非数组 JSON，统一在合并入口兜底。
  const savedValues = Array.isArray(savedOrder) ? savedOrder : []

  for (const value of savedValues) {
    if (typeof value !== 'string' || !availableSet.has(value) || seen.has(value)) {
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

export function moveLocalSupplierPurchaseSalesAnalysisColumnOrder(
  currentOrder: readonly LocalSupplierPurchaseSalesAnalysisColumnKey[],
  activeKey: unknown,
  overKey: unknown,
): LocalSupplierPurchaseSalesAnalysisColumnKey[] {
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

export function isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(
  currentOrder: readonly LocalSupplierPurchaseSalesAnalysisColumnKey[],
  defaultOrder: readonly LocalSupplierPurchaseSalesAnalysisColumnKey[] =
    LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER,
): boolean {
  if (!currentOrder.length) {
    return false
  }

  const normalizedOrder = mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(currentOrder, defaultOrder)
  return normalizedOrder.length !== defaultOrder.length ||
    normalizedOrder.some((key, index) => key !== defaultOrder[index])
}
