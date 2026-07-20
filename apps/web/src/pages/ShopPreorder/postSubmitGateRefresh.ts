import type { PreorderActivationSummary, PreorderActiveResult } from '../../types/preorder'

export interface PostSubmitGateState extends PreorderActiveResult {
  loading: boolean
  error: boolean
}

interface PostSubmitGateRefreshOptions {
  activationGuid: string
  storeCode: string
  knownActivations: PreorderActivationSummary[]
  loadGate: (signal: AbortSignal) => Promise<PreorderActiveResult>
  getCurrentStoreCode: () => string | null
  claimRequestToken: () => number
  isRequestCurrent: (token: number) => boolean
  setGate: (gate: PostSubmitGateState) => void
  navigate: (path: string) => void
  notifyRefreshFailed: () => void
  timeoutMs?: number
}

export type PostSubmitGateRefreshResult = 'success' | 'failed' | 'stale'

export function beginPostSubmitGateRefresh({
  activationGuid,
  storeCode,
  knownActivations,
  loadGate,
  getCurrentStoreCode,
  claimRequestToken,
  isRequestCurrent,
  setGate,
  navigate,
  notifyRefreshFailed,
  timeoutMs = 8_000,
}: PostSubmitGateRefreshOptions): Promise<PostSubmitGateRefreshResult> {
  const requestToken = claimRequestToken()
  if (!isRequestCurrent(requestToken) || getCurrentStoreCode() !== storeCode) {
    return Promise.resolve('stale')
  }
  const remainingActivations = knownActivations.filter((item) => item.activationGuid !== activationGuid)

  // POST 已确认终态后立即保持门禁关闭并导航，慢速刷新不得继续阻塞提交反馈。
  setGate({
    activations: remainingActivations,
    normalOrderBlocked: true,
    loading: true,
    error: false,
  })
  const next = remainingActivations[0]
  navigate(next ? `/shop/preorders/${next.activationGuid}` : '/shop')

  const controller = new AbortController()
  let timeoutId: ReturnType<typeof setTimeout> | undefined
  const timeout = new Promise<never>((_, reject) => {
    timeoutId = setTimeout(() => {
      controller.abort()
      reject(new Error('Preorder gate refresh timed out'))
    }, timeoutMs)
  })

  return (async () => {
    try {
      const gate = await Promise.race([loadGate(controller.signal), timeout])
      // 切店后旧请求只能静默结束，绝不能覆盖新分店的门禁。
      if (!isRequestCurrent(requestToken) || getCurrentStoreCode() !== storeCode) return 'stale'
      setGate({
        activations: gate.activations,
        normalOrderBlocked: gate.normalOrderBlocked,
        loading: false,
        error: false,
      })
      return 'success'
    } catch {
      if (!isRequestCurrent(requestToken) || getCurrentStoreCode() !== storeCode) return 'stale'
      setGate({
        activations: remainingActivations,
        normalOrderBlocked: true,
        loading: false,
        error: true,
      })
      notifyRefreshFailed()
      return 'failed'
    } finally {
      if (timeoutId !== undefined) clearTimeout(timeoutId)
    }
  })()
}
