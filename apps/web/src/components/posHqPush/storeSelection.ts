import type {
  PushProductsToHqStoreOption,
  PushProductsToHqUpdateField,
} from '../../types/posProduct'

// 发送到 HQ 弹窗的分店选择逻辑在商品管理页和仓库商品页共用，集中维护避免两页行为漂移。

export const PUSH_TO_HQ_STORE_DIMENSION_FIELDS: readonly PushProductsToHqUpdateField[] = [
  'supplierCode',
  'storePurchasePrice',
  'storeRetailPrice',
  'storeMultiCodes',
]

export function normalizePushToHqStoreOptions(raw: unknown): PushProductsToHqStoreOption[] {
  if (!Array.isArray(raw)) return []
  const options: PushProductsToHqStoreOption[] = []
  const seenCodes = new Set<string>()
  for (const item of raw) {
    if (!item || typeof item !== 'object') continue
    const record = item as Record<string, unknown>
    const storeCode = typeof record.storeCode === 'string' ? record.storeCode.trim() : ''
    if (!storeCode) continue
    const normalizedCode = storeCode.toLowerCase()
    if (seenCodes.has(normalizedCode)) continue
    seenCodes.add(normalizedCode)
    const storeName = typeof record.storeName === 'string' ? record.storeName.trim() : ''
    options.push({ storeCode, storeName })
  }
  return options
}

export function buildPushToHqStoreOptionLabel(option: PushProductsToHqStoreOption): string {
  return option.storeName ? `${option.storeName}（${option.storeCode}）` : option.storeCode
}

export function buildPushToHqStoreSelectOptions(options: readonly PushProductsToHqStoreOption[]) {
  return options.map((option) => ({
    value: option.storeCode,
    label: buildPushToHqStoreOptionLabel(option),
  }))
}

export function getPushToHqStoreSelectAllState(
  selectedCodes: readonly string[],
  allCodes: readonly string[],
) {
  const selectedSet = new Set(selectedCodes)
  const selectedCount = allCodes.filter((code) => selectedSet.has(code)).length
  return {
    checked: allCodes.length > 0 && selectedCount === allCodes.length,
    indeterminate: selectedCount > 0 && selectedCount < allCodes.length,
  }
}

export function getNextPushToHqStoreSelection(
  checked: boolean,
  allCodes: readonly string[],
): string[] {
  return checked ? [...allCodes] : []
}

export function isPushToHqTargetStoreRequired(
  updateFields: readonly PushProductsToHqUpdateField[],
): boolean {
  return updateFields.some((field) => PUSH_TO_HQ_STORE_DIMENSION_FIELDS.includes(field))
}

export function hasPushToHqTargetStoreError(
  updateFields: readonly PushProductsToHqUpdateField[],
  targetStoreCodes: readonly string[],
): boolean {
  return isPushToHqTargetStoreRequired(updateFields) && targetStoreCodes.length === 0
}

export interface PushToHqStoreOptionsGuard {
  begin: () => number
  isLatest: (requestId: number) => boolean
  invalidate: () => void
  isBusy: () => boolean
  complete: (requestId: number) => void
}

// HQ 分店选项获取守卫：同一时刻只允许一个请求，且取消/重开后只有最新请求能写入状态。
export function createPushToHqStoreOptionsGuard(): PushToHqStoreOptionsGuard {
  let latestRequestId = 0
  let busy = false

  return {
    begin() {
      if (busy) return -1
      latestRequestId += 1
      busy = true
      return latestRequestId
    },
    isLatest(requestId) {
      return latestRequestId === requestId
    },
    invalidate() {
      latestRequestId += 1
      busy = false
    },
    isBusy() {
      return busy
    },
    complete(requestId) {
      // 只允许最新请求释放单飞锁，避免过期请求把新请求的 busy 状态清掉。
      if (busy && requestId === latestRequestId) {
        busy = false
      }
    },
  }
}
