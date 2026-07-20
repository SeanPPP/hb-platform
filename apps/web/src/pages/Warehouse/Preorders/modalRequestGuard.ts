export interface ModalRequestGuard {
  version: number
  controller: AbortController | null
}

export interface ModalRequestToken {
  version: number
  signal: AbortSignal
}

export function createModalRequestGuard(): ModalRequestGuard {
  return { version: 0, controller: null }
}

export function invalidateModalRequest(guard: ModalRequestGuard) {
  guard.version += 1
  guard.controller?.abort()
  guard.controller = null
}

export function beginModalRequest(guard: ModalRequestGuard): ModalRequestToken {
  invalidateModalRequest(guard)
  const controller = new AbortController()
  guard.controller = controller
  return { version: guard.version, signal: controller.signal }
}

export function isCurrentModalRequest(guard: ModalRequestGuard, token: ModalRequestToken) {
  return guard.version === token.version && !token.signal.aborted
}
