import type { StoreOption } from '../services/storeService'
import type { UserStoreDto } from '../types/user'

export type ManagedStoreCodes = string[] | null | undefined

export interface TextStoreCodeFilter {
  filterType: 'text'
  type: 'equals'
  filter: string
}

export interface SetStoreCodeFilter {
  filterType: 'set'
  values: string[]
}

export type StoreCodeFilter = TextStoreCodeFilter | SetStoreCodeFilter

export function normalizeManagedStoreCodes(managedStoreCodes: ManagedStoreCodes): string[] | null {
  if (managedStoreCodes === null || managedStoreCodes === undefined) {
    return null
  }

  return Array.from(new Set(managedStoreCodes.filter(Boolean)))
}

export function shouldSkipScopedStoreQuery(managedStoreCodes: ManagedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes)
  return Array.isArray(normalized) && normalized.length === 0
}

export function shouldSkipStoreQueryForScope(
  selectedStoreCode: string | undefined,
  storeCodes: ManagedStoreCodes,
) {
  const normalized = normalizeManagedStoreCodes(storeCodes)
  if (normalized === null) {
    return false
  }
  if (!selectedStoreCode || normalized.length === 0) {
    return true
  }
  return !normalized.includes(selectedStoreCode)
}

export function filterStoreOptionsByManagedCodes<T extends StoreOption>(
  stores: T[],
  managedStoreCodes: ManagedStoreCodes,
) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes)
  if (normalized === null) {
    return stores
  }

  const managedStoreCodeSet = new Set(normalized)
  return stores.filter((store) => managedStoreCodeSet.has(store.value))
}

export function buildStoreOptionsFromUserStores(
  stores: UserStoreDto[] | undefined,
  options: { manageableOnly?: boolean } = {},
): StoreOption[] {
  const seenStoreCodes = new Set<string>()
  return (stores ?? [])
    .filter((store) => !options.manageableOnly || store.isManageable)
    .filter((store) => {
      if (!store.storeCode || seenStoreCodes.has(store.storeCode)) {
        return false
      }
      seenStoreCodes.add(store.storeCode)
      return true
    })
    .map((store) => ({
      label: store.storeName || store.storeCode,
      value: store.storeCode,
    }))
}

export function buildScopedStoreCodeFilter(
  selectedStoreCode: string | undefined,
  managedStoreCodes: ManagedStoreCodes,
): StoreCodeFilter | undefined {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes)
  if (selectedStoreCode) {
    if (normalized === null || normalized.includes(selectedStoreCode)) {
      return { filterType: 'text', type: 'equals', filter: selectedStoreCode }
    }
    return { filterType: 'set', values: [] }
  }

  if (normalized === null) {
    return undefined
  }

  if (normalized.length === 1) {
    return { filterType: 'text', type: 'equals', filter: normalized[0] }
  }

  if (normalized.length > 1) {
    return { filterType: 'set', values: normalized }
  }

  return undefined
}

export function isStoreCodeInManagedScope(storeCode: string | undefined, managedStoreCodes: ManagedStoreCodes) {
  const normalized = normalizeManagedStoreCodes(managedStoreCodes)
  if (normalized === null) {
    return true
  }
  if (!storeCode) {
    return false
  }
  return normalized.includes(storeCode)
}
