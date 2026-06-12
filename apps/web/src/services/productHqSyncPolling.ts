import type { HqProductSyncJobResult } from '../types/posProduct'

export const PRODUCT_HQ_SYNC_POLL_INTERVAL_MS = 2000
export const PRODUCT_HQ_SYNC_TIMEOUT_MS = 10 * 60 * 1000

export class HqProductSyncPollingTimeoutError extends Error {
  constructor(message = '商品同步任务轮询超时') {
    super(message)
    this.name = 'HqProductSyncPollingTimeoutError'
  }
}

export class HqProductSyncPollingCancelledError extends Error {
  constructor(message = '商品同步任务轮询已取消') {
    super(message)
    this.name = 'HqProductSyncPollingCancelledError'
  }
}

export type HqProductSyncTimerId = ReturnType<typeof setTimeout> | number
export type HqProductSyncSetTimeout = (callback: () => void, delay: number) => HqProductSyncTimerId
export type HqProductSyncClearTimeout = (timerId: HqProductSyncTimerId) => void

export interface HqProductSyncPollingOptions {
  pollIntervalMs?: number
  timeoutMs?: number
  setTimeoutFn?: HqProductSyncSetTimeout
  clearTimeoutFn?: HqProductSyncClearTimeout
}

interface CreateProductHqSyncJobPollerOptions extends HqProductSyncPollingOptions {
  jobId: string
  getJob: (jobId: string) => Promise<HqProductSyncJobResult>
}

interface HqSyncJobLike {
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | string
}

interface CreateHqSyncJobPollerOptions<TJob extends HqSyncJobLike> extends HqProductSyncPollingOptions {
  jobId: string
  getJob: (jobId: string) => Promise<TJob>
}

function isTerminalStatus(status: HqSyncJobLike['status']) {
  return status === 'Succeeded' || status === 'Failed'
}

export function createHqSyncJobPoller<TJob extends HqSyncJobLike>({
  jobId,
  getJob,
  pollIntervalMs = PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
  timeoutMs = PRODUCT_HQ_SYNC_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}: CreateHqSyncJobPollerOptions<TJob>) {
  let pollingTimer: HqProductSyncTimerId | null = null
  let timeoutTimer: HqProductSyncTimerId | null = null
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

  const promise = new Promise<TJob>((resolve, reject) => {
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

    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return
      }

      stopped = true
      clearTimers()
      reject(new HqProductSyncPollingTimeoutError())
    }, timeoutMs)
    scheduleNextPoll()
  })

  const stop = () => {
    if (stopped) {
      return
    }

    stopped = true
    clearTimers()
    rejectPromise?.(new HqProductSyncPollingCancelledError())
  }

  return {
    promise,
    stop,
  }
}

export function createProductHqSyncJobPoller(options: CreateProductHqSyncJobPollerOptions) {
  return createHqSyncJobPoller<HqProductSyncJobResult>(options)
}
