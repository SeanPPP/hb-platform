export interface WarehouseProductChangeHistoryRequestToken {
  id: number
  key: string
}

export function createWarehouseProductChangeHistoryRequestGuard() {
  let sequence = 0
  let activeKey = ''

  return {
    start(productCode: string, pageNumber: number, pageSize: number): WarehouseProductChangeHistoryRequestToken {
      const token = {
        id: sequence + 1,
        key: `${productCode}:${pageNumber}:${pageSize}`,
      }
      sequence = token.id
      activeKey = token.key
      return token
    },
    isCurrent(token: WarehouseProductChangeHistoryRequestToken) {
      return token.id === sequence && token.key === activeKey
    },
    cancel() {
      sequence += 1
      activeKey = ''
    },
  }
}

export function formatWarehouseProductChangeHistoryValue(value: unknown) {
  if (value === null || value === undefined) {
    return '--'
  }

  if (value === '') {
    return '""'
  }

  if (typeof value === 'boolean') {
    return value ? 'true' : 'false'
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? String(value) : '--'
  }

  if (typeof value === 'string') {
    return value
  }

  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}

export function getWarehouseProductChangeHistoryActionKey(value: string) {
  const normalized = value.replace(/[\s_-]/g, '').toLowerCase()
  return {
    create: 'create',
    update: 'update',
    batchupdate: 'batchUpdate',
    patch: 'patch',
    toggleactive: 'toggleActive',
    import: 'import',
    sync: 'sync',
  }[normalized]
}

export function isWarehouseProductChangeHistoryAbortError(error: unknown) {
  return Boolean(
    error &&
      typeof error === 'object' &&
      'name' in error &&
      (error as { name?: unknown }).name === 'AbortError',
  )
}
