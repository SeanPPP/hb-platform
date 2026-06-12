import type { StoreOrderSyncJobResult } from '../../../types/storeOrder'

export const STORE_ORDER_SYNC_POLL_INTERVAL_MS = 2000
export const STORE_ORDER_SYNC_TIMEOUT_MS = 10 * 60 * 1000

export class StoreOrderSyncPollingTimeoutError extends Error {
  constructor() {
    super('store-order-sync-polling-timeout')
    this.name = 'StoreOrderSyncPollingTimeoutError'
  }
}

export class StoreOrderSyncPollingCancelledError extends Error {
  constructor() {
    super('store-order-sync-polling-cancelled')
    this.name = 'StoreOrderSyncPollingCancelledError'
  }
}

type TimeoutHandle = ReturnType<typeof setTimeout> | number
type SetTimeoutFn = (callback: () => void, delay: number) => TimeoutHandle
type ClearTimeoutFn = (handle: TimeoutHandle) => void

interface CreateStoreOrderSyncJobPollerOptions {
  jobId: string
  getJob: (jobId: string) => Promise<StoreOrderSyncJobResult>
  pollIntervalMs?: number
  timeoutMs?: number
  setTimeoutFn?: SetTimeoutFn
  clearTimeoutFn?: ClearTimeoutFn
}

function isTerminalStatus(status: StoreOrderSyncJobResult['status']) {
  return status === 'Succeeded' || status === 'Failed'
}

export function createStoreOrderSyncJobPoller({
  jobId,
  getJob,
  pollIntervalMs = STORE_ORDER_SYNC_POLL_INTERVAL_MS,
  timeoutMs = STORE_ORDER_SYNC_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}: CreateStoreOrderSyncJobPollerOptions) {
  let pollingTimer: TimeoutHandle | null = null
  let timeoutTimer: TimeoutHandle | null = null
  let stopped = false
  let rejectPromise: ((reason?: unknown) => void) | null = null

  const clearTimers = () => {
    if (pollingTimer) {
      clearTimeoutFn(pollingTimer)
      pollingTimer = null
    }
    if (timeoutTimer) {
      clearTimeoutFn(timeoutTimer)
      timeoutTimer = null
    }
  }

  const promise = new Promise<StoreOrderSyncJobResult>((resolve, reject) => {
    rejectPromise = reject

    const scheduleNextPoll = () => {
      pollingTimer = setTimeoutFn(async () => {
        try {
          const result = await getJob(jobId)

          if (stopped) {
            return
          }

          if (isTerminalStatus(result.status)) {
            clearTimers()
            resolve(result)
            return
          }

          scheduleNextPoll()
        } catch (error) {
          if (stopped) {
            return
          }
          clearTimers()
          reject(error)
        }
      }, pollIntervalMs)
    }

    scheduleNextPoll()
    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return
      }

      stopped = true
      clearTimers()
      reject(new StoreOrderSyncPollingTimeoutError())
    }, timeoutMs)
  })

  const stop = () => {
    if (stopped) {
      return
    }

    stopped = true
    clearTimers()
    rejectPromise?.(new StoreOrderSyncPollingCancelledError())
  }

  return {
    promise,
    stop,
  }
}
