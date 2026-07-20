import type { PreorderTemplateStore } from '../../../types/preorder'

export interface ActivationStoreOption extends PreorderTemplateStore {
  isActive: boolean
}

function getStoreIdentity(storeGuid: string) {
  return storeGuid.trim().toLowerCase()
}

export function mergeActivationStoreOptions(
  activeStores: PreorderTemplateStore[],
  currentStores: PreorderTemplateStore[],
): ActivationStoreOption[] {
  const options = new Map<string, ActivationStoreOption>()
  currentStores.forEach((store) => {
    const identity = getStoreIdentity(store.storeGuid)
    if (!options.has(identity)) options.set(identity, { ...store, isActive: false })
  })
  activeStores.forEach((store) => {
    const identity = getStoreIdentity(store.storeGuid)
    const currentStore = options.get(identity)
    // 同一 GUID 的大小写不构成不同分店；当前目标 GUID 保持原样用于 Select value 和提交。
    options.set(identity, {
      ...store,
      storeGuid: currentStore?.storeGuid ?? store.storeGuid,
      isActive: true,
    })
  })
  return [...options.values()].sort((left, right) => (
    left.storeName.localeCompare(right.storeName, undefined, { numeric: true, sensitivity: 'base' })
      || left.storeCode.localeCompare(right.storeCode, undefined, { numeric: true, sensitivity: 'base' })
      || left.storeGuid.localeCompare(right.storeGuid)
  ))
}

export function getActivationStoreChanges(currentStoreGuids: string[], nextStoreGuids: string[]) {
  const current = new Set(currentStoreGuids.map(getStoreIdentity))
  const next = new Set(nextStoreGuids.map(getStoreIdentity))
  return {
    addedCount: [...next].filter((storeGuid) => !current.has(storeGuid)).length,
    removedCount: [...current].filter((storeGuid) => !next.has(storeGuid)).length,
  }
}
