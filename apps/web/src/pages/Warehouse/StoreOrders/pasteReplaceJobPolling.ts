import type { StoreOrderPasteReplaceJobResult } from '../../../types/storeOrder'

type TimeoutHandle = number | ReturnType<typeof setTimeout>
type SetTimeoutFn = (callback: () => void, delay: number) => TimeoutHandle
type ClearTimeoutFn = (handle: TimeoutHandle) => void

export class StoreOrderPasteReplacePollingTimeoutError extends Error {
  constructor() {
    super('Excel 粘贴导入任务轮询超时')
    this.name = 'StoreOrderPasteReplacePollingTimeoutError'
  }
}

export class StoreOrderPasteReplacePollingCancelledError extends Error {
  constructor() {
    super('Excel 粘贴导入任务轮询已取消')
    this.name = 'StoreOrderPasteReplacePollingCancelledError'
  }
}

interface StoreOrderPasteReplaceJobPollerOptions {
  jobId: string
  getJob: (jobId: string) => Promise<StoreOrderPasteReplaceJobResult>
  pollIntervalMs?: number
  timeoutMs?: number
  setTimeoutFn?: SetTimeoutFn
  clearTimeoutFn?: ClearTimeoutFn
}

function isTerminalStatus(status: StoreOrderPasteReplaceJobResult['status']) {
  return status === 'Succeeded' || status === 'Failed'
}

export function createStoreOrderPasteReplaceJobPoller({
  jobId,
  getJob,
  pollIntervalMs = 2000,
  timeoutMs = 10 * 60 * 1000,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}: StoreOrderPasteReplaceJobPollerOptions) {
  let pollingTimer: TimeoutHandle | null = null
  let timeoutTimer: TimeoutHandle | null = null
  let stopped = false
  let rejectPromise: ((error: Error) => void) | null = null

  const clearTimers = () => {
    if (pollingTimer !== null) {
      clearTimeoutFn(pollingTimer)
      pollingTimer = null
    }
    if (timeoutTimer !== null) {
      clearTimeoutFn(timeoutTimer)
      timeoutTimer = null
    }
  }

  const promise = new Promise<StoreOrderPasteReplaceJobResult>((resolve, reject) => {
    rejectPromise = reject

    const scheduleNextPoll = () => {
      pollingTimer = setTimeoutFn(() => {
        void poll()
      }, pollIntervalMs)
    }

    const poll = async () => {
      if (stopped) {
        return
      }

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
    }

    scheduleNextPoll()
    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return
      }

      // 超时必须独立于网络响应，避免 getJob 挂起时 promise 永远不结束。
      stopped = true
      clearTimers()
      reject(new StoreOrderPasteReplacePollingTimeoutError())
    }, timeoutMs)
  })

  const stop = () => {
    if (stopped) {
      return
    }

    stopped = true
    clearTimers()
    rejectPromise?.(new StoreOrderPasteReplacePollingCancelledError())
  }

  return {
    promise,
    stop,
  }
}
