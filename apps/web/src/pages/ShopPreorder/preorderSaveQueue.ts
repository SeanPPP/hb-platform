export type PreorderSaveRequestResult<TDetail> =
  | { status: 'saved' }
  | { status: 'conflict'; detail: TDetail }
  | { status: 'failed'; error: unknown }

interface SavePromiseState<TDetail> {
  currentRequestPromise: Promise<PreorderSaveRequestResult<TDetail>> | null
  drainPromise: Promise<boolean> | null
}

interface PendingSaveState<TPending, TDetail> {
  currentRequestPromise: Promise<PreorderSaveRequestResult<TDetail>> | null
  pending: TPending | null
  stopAfterCurrentRequest: boolean
}

export function exposeCurrentSaveRequest<TDetail>(
  state: Pick<SavePromiseState<TDetail>, 'currentRequestPromise'>,
  promise: Promise<PreorderSaveRequestResult<TDetail>>,
) {
  // 较旧 Promise 完成时不得清空后来启动的新任务。
  const trackedPromise = promise.finally(() => {
    if (state.currentRequestPromise === trackedPromise) {
      state.currentRequestPromise = null
    }
  })
  state.currentRequestPromise = trackedPromise
  return trackedPromise
}

export function exposeSaveDrain(state: Pick<SavePromiseState<unknown>, 'drainPromise'>, promise: Promise<boolean>) {
  const trackedPromise = promise.finally(() => {
    if (state.drainPromise === trackedPromise) {
      state.drainPromise = null
    }
  })
  state.drainPromise = trackedPromise
  return trackedPromise
}

export function awaitCurrentSaveRequest<TDetail>(state: Pick<SavePromiseState<TDetail>, 'currentRequestPromise'>) {
  return state.currentRequestPromise ?? Promise.resolve({ status: 'saved' } as const)
}

export function awaitSaveDrain(state: Pick<SavePromiseState<unknown>, 'drainPromise'>) {
  return state.drainPromise ?? Promise.resolve(true)
}

export function freezeSaveQueueForSubmission<TPending, TDetail>(state: PendingSaveState<TPending, TDetail>) {
  // 提交 snapshot 已冻结，后续只等待当前 PUT，不能继续消费之前排队的 snapshot。
  state.pending = null
  state.stopAfterCurrentRequest = Boolean(state.currentRequestPromise)
  return awaitCurrentSaveRequest(state)
}

export function takeNextPendingSave<TPending, TDetail>(state: PendingSaveState<TPending, TDetail>) {
  if (state.stopAfterCurrentRequest) {
    state.stopAfterCurrentRequest = false
    state.pending = null
    return null
  }
  const next = state.pending
  state.pending = null
  return next
}

export function createDebouncedTask(task: () => void, delayMs: number) {
  const timeoutId = setTimeout(task, delayMs)
  return {
    cancel: () => clearTimeout(timeoutId),
  }
}
