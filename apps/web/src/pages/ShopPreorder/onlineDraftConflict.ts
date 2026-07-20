import type { PreorderActivationDetail, PreorderActivationItem, PreorderOrderStatus } from '../../types/preorder'
import { mergePendingPreorderDraft } from './pendingDraft'
import { isEditablePreorderOrderStatus } from './preorderAvailability'

export type OnlinePreorderDraftChoice = 'local' | 'server'

export function resolvePreorderSubmitReconciliation(
  orderStatus: PreorderOrderStatus | null | undefined,
  wasDraftConflict: boolean,
) {
  if (!isEditablePreorderOrderStatus(orderStatus)) return 'terminal' as const
  return wasDraftConflict ? 'coordinate' as const : 'failed' as const
}

export function resolveOnlinePreorderDraftConflict(
  serverDraft: Pick<PreorderActivationDetail, 'draftRevision' | 'items' | 'orderStatus'>,
  localItems: PreorderActivationItem[],
  choice: OnlinePreorderDraftChoice,
) {
  const forcedServer = !isEditablePreorderOrderStatus(serverDraft.orderStatus)
  const effectiveChoice = forcedServer ? 'server' : choice
  return {
    // 两种选择都必须更新到刚刷新的服务器版本，后续重试/提交不能继续携带旧 revision。
    draftRevision: serverDraft.draftRevision,
    items: effectiveChoice === 'local'
      ? mergePendingPreorderDraft(serverDraft.items, localItems)
      : serverDraft.items,
    shouldSave: effectiveChoice === 'local',
    forcedServer,
  }
}
