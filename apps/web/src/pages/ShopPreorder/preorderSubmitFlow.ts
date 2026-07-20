import type { PreorderActivationDetail } from '../../types/preorder'
import { resolvePreorderSubmitReconciliation } from './onlineDraftConflict'

export type PreorderSubmitOutcome = 'submitted' | 'terminal' | 'coordinated' | 'failed'

interface RunPreorderSubmitOptions {
  initialConflictDetail?: PreorderActivationDetail
  submit: () => Promise<unknown>
  loadDetail: () => Promise<PreorderActivationDetail>
  isConflict: (error: unknown) => boolean
  onTerminal?: (detail: PreorderActivationDetail) => void | Promise<void>
  coordinateConflict: (detail: PreorderActivationDetail) => void | Promise<void>
}

export async function runPreorderSubmit({
  initialConflictDetail,
  submit,
  loadDetail,
  isConflict,
  onTerminal,
  coordinateConflict,
}: RunPreorderSubmitOptions): Promise<PreorderSubmitOutcome> {
  if (initialConflictDetail) {
    // freeze 已完成唯一一次 conflict detail GET；提交协调必须直接复用，禁止 POST 或第二次读取。
    const reconciliation = resolvePreorderSubmitReconciliation(initialConflictDetail.orderStatus, true)
    if (reconciliation === 'terminal') {
      await onTerminal?.(initialConflictDetail)
      return 'terminal'
    }
    if (reconciliation === 'coordinate') {
      await coordinateConflict(initialConflictDetail)
      return 'coordinated'
    }
    return 'failed'
  }
  try {
    await submit()
    return 'submitted'
  } catch (error) {
    // POST 结果未知或冲突时只读取一次 detail，后续终态判断和冲突协调都复用它。
    const detail = await loadDetail()
    const reconciliation = resolvePreorderSubmitReconciliation(detail.orderStatus, isConflict(error))
    if (reconciliation === 'terminal') {
      await onTerminal?.(detail)
      return 'terminal'
    }
    if (reconciliation === 'coordinate') {
      await coordinateConflict(detail)
      return 'coordinated'
    }
    return 'failed'
  }
}
