import type { AdvertisementStoreItemDto } from '../../../types/advertisement'
import type { StoreOption } from '../../../services/storeService'

const MAX_VISIBLE_STORE_TAGS = 3

function normalizeStoreCode(storeCode: string) {
  return storeCode.trim().toLocaleLowerCase()
}

function getStoreName(store: AdvertisementStoreItemDto, storeOptions: readonly StoreOption[]) {
  const responseStoreName = store.storeName?.trim()
  if (responseStoreName) {
    return responseStoreName
  }

  const normalizedStoreCode = normalizeStoreCode(store.storeCode)
  return storeOptions
    .find((option) => normalizeStoreCode(option.value) === normalizedStoreCode)
    ?.label.trim()
}

export function getAdvertisementStoreTagLabels(
  stores: readonly AdvertisementStoreItemDto[] | undefined,
  storeOptions: readonly StoreOption[],
) {
  if (!stores?.length) {
    return ['--']
  }

  // 接口名称优先，本地分店目录仅作为旧接口或受限目录的兼容回退。
  const labels = stores.slice(0, MAX_VISIBLE_STORE_TAGS).map((store) => {
    const storeCode = store.storeCode.trim()
    const storeName = getStoreName(store, storeOptions)
    return storeName && normalizeStoreCode(storeName) !== normalizeStoreCode(storeCode)
      ? `${storeName}（${storeCode}）`
      : storeCode
  })

  if (stores.length > MAX_VISIBLE_STORE_TAGS) {
    labels.push(`+${stores.length - MAX_VISIBLE_STORE_TAGS}`)
  }

  return labels
}
