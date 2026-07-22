export const DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER = [
  'storeCode',
  'supplierCode',
  'invoiceNo',
  'orderDate',
  'inboundDate',
  'totalAmount',
  'receivedTotalAmount',
  'flowStatus',
  'inboundStatus',
  'remarks',
  'createdAt',
  'createdBy',
  'updatedAt',
  'updatedBy',
] as const

export const MAX_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER_STORAGE_LENGTH = 4096

export type LocalSupplierInvoiceColumnKey =
  (typeof DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER)[number]

interface LocalSupplierInvoiceDndEvent {
  active: { id: unknown }
  over?: { id: unknown } | null
}

interface LocalSupplierInvoiceDndAccessibilityMessages {
  instructions: string
  unknownColumn: string
  dragStart: (column: string) => string
  dragOver: (column: string, overColumn: string) => string
  dragOverNone: (column: string) => string
  dragEnd: (column: string, overColumn: string) => string
  dragCancel: (column: string) => string
}

export function createLocalSupplierInvoiceDndAccessibility(
  columnLabels: Partial<Record<LocalSupplierInvoiceColumnKey, string>>,
  messages: LocalSupplierInvoiceDndAccessibilityMessages,
) {
  const getColumnLabel = (id: unknown) => {
    if (
      typeof id === 'string'
      && DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER.includes(
        id as LocalSupplierInvoiceColumnKey,
      )
    ) {
      return columnLabels[id as LocalSupplierInvoiceColumnKey] || messages.unknownColumn
    }
    return messages.unknownColumn
  }

  return {
    screenReaderInstructions: { draggable: messages.instructions },
    announcements: {
      onDragStart: ({ active }: LocalSupplierInvoiceDndEvent) =>
        messages.dragStart(getColumnLabel(active.id)),
      onDragOver: ({ active, over }: LocalSupplierInvoiceDndEvent) =>
        over
          ? messages.dragOver(getColumnLabel(active.id), getColumnLabel(over.id))
          : messages.dragOverNone(getColumnLabel(active.id)),
      onDragEnd: ({ active, over }: LocalSupplierInvoiceDndEvent) =>
        over
          ? messages.dragEnd(getColumnLabel(active.id), getColumnLabel(over.id))
          : messages.dragCancel(getColumnLabel(active.id)),
      onDragCancel: ({ active }: LocalSupplierInvoiceDndEvent) =>
        messages.dragCancel(getColumnLabel(active.id)),
    },
  }
}

export function dispatchLocalSupplierInvoiceDragHandleKeyDown<
  TEvent extends { stopPropagation: () => void },
>(event: TEvent, dndListener?: (event: TEvent) => void) {
  // 拖拽按键只交给手柄处理，避免空格或回车同时触发表头排序。
  event.stopPropagation()
  dndListener?.(event)
}

export function mergeLocalSupplierInvoiceColumnOrder(
  savedOrder: unknown,
  availableOrder: readonly LocalSupplierInvoiceColumnKey[] =
    DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
): LocalSupplierInvoiceColumnKey[] {
  const availableSet = new Set<LocalSupplierInvoiceColumnKey>(availableOrder)
  const seen = new Set<LocalSupplierInvoiceColumnKey>()
  const merged: LocalSupplierInvoiceColumnKey[] = []
  const savedValues = Array.isArray(savedOrder) ? savedOrder : []

  if (savedValues.length > availableOrder.length * 4) {
    return [...availableOrder]
  }

  // 本地列序可能来自旧版本或损坏数据，统一过滤、去重并补齐新增列。
  for (const value of savedValues) {
    if (typeof value !== 'string' || !availableSet.has(value as LocalSupplierInvoiceColumnKey)) {
      continue
    }
    const key = value as LocalSupplierInvoiceColumnKey
    if (seen.has(key)) continue
    seen.add(key)
    merged.push(key)
  }

  for (const key of availableOrder) {
    if (!seen.has(key)) merged.push(key)
  }

  return merged
}

export function parseLocalSupplierInvoiceColumnOrder(
  raw: string | null,
): LocalSupplierInvoiceColumnKey[] {
  if (!raw || raw.length > MAX_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER_STORAGE_LENGTH) {
    return [...DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER]
  }

  try {
    return mergeLocalSupplierInvoiceColumnOrder(JSON.parse(raw))
  } catch {
    return [...DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER]
  }
}

export function moveLocalSupplierInvoiceColumnOrder(
  currentOrder: readonly LocalSupplierInvoiceColumnKey[],
  activeKey: unknown,
  overKey: unknown,
): LocalSupplierInvoiceColumnKey[] {
  if (typeof activeKey !== 'string' || typeof overKey !== 'string' || activeKey === overKey) {
    return [...currentOrder]
  }

  const fromIndex = currentOrder.indexOf(activeKey as LocalSupplierInvoiceColumnKey)
  const toIndex = currentOrder.indexOf(overKey as LocalSupplierInvoiceColumnKey)
  if (fromIndex < 0 || toIndex < 0) return [...currentOrder]

  const nextOrder = [...currentOrder]
  const [moved] = nextOrder.splice(fromIndex, 1)
  nextOrder.splice(toIndex, 0, moved)
  return nextOrder
}

export function isLocalSupplierInvoiceColumnOrderCustomized(
  currentOrder: readonly LocalSupplierInvoiceColumnKey[],
  defaultOrder: readonly LocalSupplierInvoiceColumnKey[] =
    DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
): boolean {
  const normalized = mergeLocalSupplierInvoiceColumnOrder(currentOrder, defaultOrder)
  return normalized.some((key, index) => key !== defaultOrder[index])
}
