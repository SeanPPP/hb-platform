export interface ActivationDetailRequestGuard {
  version: number
  controller: AbortController | null
}

export interface ActivationDetailRequestToken {
  version: number
  activationGuid: string
  signal: AbortSignal
}

export function createActivationDetailRequestGuard(): ActivationDetailRequestGuard {
  return { version: 0, controller: null }
}

export function invalidateActivationDetailRequest(guard: ActivationDetailRequestGuard) {
  guard.version += 1
  guard.controller?.abort()
  guard.controller = null
}

export function beginActivationDetailRequest(
  guard: ActivationDetailRequestGuard,
  activationGuid: string,
): ActivationDetailRequestToken {
  invalidateActivationDetailRequest(guard)
  const controller = new AbortController()
  guard.controller = controller
  return { version: guard.version, activationGuid, signal: controller.signal }
}

export function isCurrentActivationDetailRequest(
  guard: ActivationDetailRequestGuard,
  token: ActivationDetailRequestToken,
  currentActivationGuid: string,
) {
  return guard.version === token.version
    && !token.signal.aborted
    && token.activationGuid === currentActivationGuid
}

export function isPreorderReturnContextCurrent(
  currentActivationGuid: string,
  targetActivationGuid: string,
  detailActivationGuid: string | undefined,
) {
  return Boolean(targetActivationGuid)
    && currentActivationGuid === targetActivationGuid
    && detailActivationGuid === targetActivationGuid
}

export function getActivationActionAvailability(
  detailActivationGuid: string | undefined,
  currentActivationGuid: string,
  status: string | undefined,
) {
  const hasCurrentDetail = detailActivationGuid === currentActivationGuid
  return {
    hasCurrentDetail,
    canAdjust: hasCurrentDetail && (status === 'Active' || status === 'Scheduled'),
    canClose: hasCurrentDetail && status === 'Active',
  }
}
