import type { StoreOrderInvoiceEmailJobResult } from '../../../types/storeOrder'

export const STORE_ORDER_INVOICE_EMAIL_POLL_INTERVAL_MS = 2000
export const STORE_ORDER_INVOICE_EMAIL_TIMEOUT_MS = 5 * 60 * 1000

export class StoreOrderInvoiceEmailPollingTimeoutError extends Error {
  constructor() {
    super('store-order-invoice-email-polling-timeout')
    this.name = 'StoreOrderInvoiceEmailPollingTimeoutError'
  }
}

export class StoreOrderInvoiceEmailPollingCancelledError extends Error {
  constructor() {
    super('store-order-invoice-email-polling-cancelled')
    this.name = 'StoreOrderInvoiceEmailPollingCancelledError'
  }
}

type TimeoutHandle = ReturnType<typeof setTimeout> | number
type SetTimeoutFn = (callback: () => void, delay: number) => TimeoutHandle
type ClearTimeoutFn = (handle: TimeoutHandle) => void

interface CreateStoreOrderInvoiceEmailJobPollerOptions {
  jobId: string
  getJob: (jobId: string) => Promise<StoreOrderInvoiceEmailJobResult>
  pollIntervalMs?: number
  timeoutMs?: number
  setTimeoutFn?: SetTimeoutFn
  clearTimeoutFn?: ClearTimeoutFn
}

function isTerminalStatus(status: StoreOrderInvoiceEmailJobResult['status']) {
  return status === 'Succeeded' || status === 'Failed'
}

export function createStoreOrderInvoiceEmailJobPoller({
  jobId,
  getJob,
  pollIntervalMs = STORE_ORDER_INVOICE_EMAIL_POLL_INTERVAL_MS,
  timeoutMs = STORE_ORDER_INVOICE_EMAIL_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}: CreateStoreOrderInvoiceEmailJobPollerOptions) {
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

  const promise = new Promise<StoreOrderInvoiceEmailJobResult>((resolve, reject) => {
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
      reject(new StoreOrderInvoiceEmailPollingTimeoutError())
    }, timeoutMs)
  })

  const stop = () => {
    if (stopped) {
      return
    }

    stopped = true
    clearTimers()
    rejectPromise?.(new StoreOrderInvoiceEmailPollingCancelledError())
  }

  return {
    promise,
    stop,
  }
}
