export interface ResolveStoreContactDraftParams {
  currentValue?: string
  previousStoreValue?: string
  nextStoreValue?: string
}

function normalizeStoreContactValue(value?: string) {
  return (value || '').trim()
}

// 仅在当前值为空，或当前值仍等于上一个分店默认值时，才跟随分店切换自动带入新默认值。
export function resolveStoreContactDraftValue({
  currentValue,
  previousStoreValue,
  nextStoreValue,
}: ResolveStoreContactDraftParams) {
  const normalizedCurrentValue = normalizeStoreContactValue(currentValue)
  const normalizedPreviousStoreValue = normalizeStoreContactValue(previousStoreValue)

  if (!normalizedCurrentValue || normalizedCurrentValue === normalizedPreviousStoreValue) {
    return nextStoreValue || ''
  }

  return currentValue || ''
}
