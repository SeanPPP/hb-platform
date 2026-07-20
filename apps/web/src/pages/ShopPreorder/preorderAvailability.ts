import type { PreorderActivationItem, PreorderActivationSummary, PreorderOrderStatus } from '../../types/preorder'

export type PreorderActivationReadOnlyReason = 'scheduled' | 'ended' | 'closed' | 'cancelled' | 'unavailable'

interface PreorderQuantityEditability {
  hasDetail: boolean
  orderResponded: boolean
  hasReadOnlyReason: boolean
  submitting: boolean
  resolvingConflict: boolean
}

export function canEditPreorderQuantities(state: PreorderQuantityEditability) {
  return state.hasDetail &&
    !state.orderResponded &&
    !state.hasReadOnlyReason &&
    !state.submitting &&
    !state.resolvingConflict
}

export function isEditablePreorderOrderStatus(status: PreorderOrderStatus | null | undefined) {
  return !status || status === 'Draft' || status === 'ReturnedForRevision'
}

export function isNoDemandConfirmationMatch(value: string, expectedPhrase: string) {
  // 确认短语跟随当前语言，空格和标点也必须逐字一致。
  return value === expectedPhrase
}

export function createPreorderNoDemandSnapshot(items: readonly PreorderActivationItem[]) {
  // 放弃本期时使用新 snapshot，避免在提交确认前篡改页面上的用户输入。
  return items.map((item) => ({ ...item, packCount: 0, orderedQuantity: 0 }))
}

export function getPreorderActivationReadOnlyReason(
  activation: Pick<PreorderActivationSummary, 'status' | 'startAtUtc' | 'endAtUtc'> | null | undefined,
  nowMs = Date.now(),
): PreorderActivationReadOnlyReason | null {
  if (!activation) return 'unavailable'
  if (activation.status === 'Closed') return 'closed'
  if (activation.status === 'Cancelled') return 'cancelled'

  const startAt = Date.parse(activation.startAtUtc)
  const endAt = Date.parse(activation.endAtUtc)
  if (!Number.isFinite(startAt) || !Number.isFinite(endAt)) return 'unavailable'
  if (activation.status === 'Scheduled' || nowMs < startAt) return 'scheduled'
  if (activation.status !== 'Active') return 'unavailable'
  return nowMs < endAt ? null : 'ended'
}
