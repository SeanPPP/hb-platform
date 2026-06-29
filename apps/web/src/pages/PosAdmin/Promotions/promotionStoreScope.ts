import type { PromotionScopeType, PromotionStoreItemDto } from '../../../types/promotion'

type PromotionStoreScopeSource = {
  stores?: PromotionStoreItemDto[] | null
  scopeType?: PromotionScopeType | null
}

export function getPromotionStoreCodes(stores?: PromotionStoreItemDto[] | null) {
  return (stores ?? [])
    .map((store) => store.storeCode?.trim())
    .filter((storeCode): storeCode is string => !!storeCode)
}

export function isPromotionAllStoresScope(source: PromotionStoreScopeSource) {
  /* 后端约定：空 stores 表示总部/全部分店，保存时继续保持空数组。 */
  return source.scopeType === 'Headquarters' || getPromotionStoreCodes(source.stores).length === 0
}

export function getPromotionEditorStoreCodes(source: PromotionStoreScopeSource) {
  return isPromotionAllStoresScope(source) ? [] : getPromotionStoreCodes(source.stores)
}
