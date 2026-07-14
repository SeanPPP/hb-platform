export const DEFAULT_OPERATION_LOG_COLUMN_ORDER = [
  'occurredAtUtc',
  'storeCode',
  'employee',
  'operationType',
  'products',
  'amountDelta',
  'deviceCode',
  'outcome',
] as const

export const MAX_OPERATION_LOG_COLUMN_ORDER_STORAGE_LENGTH = 2048

export type OperationLogColumnKey = (typeof DEFAULT_OPERATION_LOG_COLUMN_ORDER)[number]

interface OperationLogDndEvent {
  active: { id: unknown }
  over?: { id: unknown } | null
}

interface OperationLogDndAccessibilityMessages {
  instructions: string
  unknownColumn: string
  dragStart: (column: string) => string
  dragOver: (column: string, overColumn: string) => string
  dragOverNone: (column: string) => string
  dragEnd: (column: string, overColumn: string) => string
  dragCancel: (column: string) => string
}

export function createOperationLogDndAccessibility(
  columnLabels: Partial<Record<OperationLogColumnKey, string>>,
  messages: OperationLogDndAccessibilityMessages,
) {
  const getColumnLabel = (id: unknown) => {
    if (
      typeof id === 'string'
      && DEFAULT_OPERATION_LOG_COLUMN_ORDER.includes(id as OperationLogColumnKey)
    ) {
      return columnLabels[id as OperationLogColumnKey] || messages.unknownColumn
    }
    return messages.unknownColumn
  }

  return {
    screenReaderInstructions: { draggable: messages.instructions },
    announcements: {
      onDragStart: ({ active }: OperationLogDndEvent) =>
        messages.dragStart(getColumnLabel(active.id)),
      onDragOver: ({ active, over }: OperationLogDndEvent) =>
        over
          ? messages.dragOver(getColumnLabel(active.id), getColumnLabel(over.id))
          : messages.dragOverNone(getColumnLabel(active.id)),
      onDragEnd: ({ active, over }: OperationLogDndEvent) =>
        over
          ? messages.dragEnd(getColumnLabel(active.id), getColumnLabel(over.id))
          : messages.dragCancel(getColumnLabel(active.id)),
      onDragCancel: ({ active }: OperationLogDndEvent) =>
        messages.dragCancel(getColumnLabel(active.id)),
    },
  }
}

export function dispatchOperationLogDragHandleKeyDown<
  TEvent extends { stopPropagation: () => void },
>(event: TEvent, dndListener?: (event: TEvent) => void) {
  // 先截断冒泡，再把同一个事件交给 dnd，避免 Enter/Space 同时触发表头排序。
  event.stopPropagation()
  dndListener?.(event)
}

export function mergeOperationLogColumnOrder(
  savedOrder: unknown,
  availableOrder: readonly OperationLogColumnKey[] = DEFAULT_OPERATION_LOG_COLUMN_ORDER,
): OperationLogColumnKey[] {
  const availableSet = new Set<OperationLogColumnKey>(availableOrder)
  const seen = new Set<OperationLogColumnKey>()
  const merged: OperationLogColumnKey[] = []
  const savedValues = Array.isArray(savedOrder) ? savedOrder : []

  // 列只有个位数，异常超长数组直接丢弃，避免损坏存储造成无意义遍历。
  if (savedValues.length > availableOrder.length * 4) return [...availableOrder]

  // localStorage 可能包含旧版本、重复项或损坏数据，在唯一入口统一清洗并补齐新增列。
  for (const value of savedValues) {
    if (typeof value !== 'string' || !availableSet.has(value as OperationLogColumnKey)) continue
    const key = value as OperationLogColumnKey
    if (seen.has(key)) continue
    seen.add(key)
    merged.push(key)
  }

  for (const key of availableOrder) {
    if (!seen.has(key)) merged.push(key)
  }

  return merged
}

export function parseOperationLogColumnOrder(raw: string | null): OperationLogColumnKey[] {
  if (!raw || raw.length > MAX_OPERATION_LOG_COLUMN_ORDER_STORAGE_LENGTH) {
    return [...DEFAULT_OPERATION_LOG_COLUMN_ORDER]
  }
  try {
    return mergeOperationLogColumnOrder(JSON.parse(raw))
  } catch {
    return [...DEFAULT_OPERATION_LOG_COLUMN_ORDER]
  }
}

export function moveOperationLogColumnOrder(
  currentOrder: readonly OperationLogColumnKey[],
  activeKey: unknown,
  overKey: unknown,
): OperationLogColumnKey[] {
  if (typeof activeKey !== 'string' || typeof overKey !== 'string' || activeKey === overKey) {
    return [...currentOrder]
  }

  const fromIndex = currentOrder.indexOf(activeKey as OperationLogColumnKey)
  const toIndex = currentOrder.indexOf(overKey as OperationLogColumnKey)
  if (fromIndex < 0 || toIndex < 0) return [...currentOrder]

  const nextOrder = [...currentOrder]
  const [moved] = nextOrder.splice(fromIndex, 1)
  nextOrder.splice(toIndex, 0, moved)
  return nextOrder
}

export function isOperationLogColumnOrderCustomized(
  currentOrder: readonly OperationLogColumnKey[],
  defaultOrder: readonly OperationLogColumnKey[] = DEFAULT_OPERATION_LOG_COLUMN_ORDER,
): boolean {
  const normalized = mergeOperationLogColumnOrder(currentOrder, defaultOrder)
  return normalized.some((key, index) => key !== defaultOrder[index])
}
