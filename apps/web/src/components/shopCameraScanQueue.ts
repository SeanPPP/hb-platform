export const SHOP_CAMERA_SCAN_MAX_QUEUE = 20
export const SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS = 1200

export type ShopCameraScanEnqueueResult = 'queued' | 'duplicate' | 'full' | 'paused' | 'invalid'

export interface ShopCameraScanLease {
  readonly generation: number
  readonly id: number
  readonly value: string
}

export interface ShopCameraScanQueueSnapshot {
  pendingCount: number
  processingValue?: string
}

export interface ShopCameraScanQueueController {
  enqueue: (rawValue: string, now: number) => ShopCameraScanEnqueueResult
  finish: (lease: ShopCameraScanLease) => void
  getSnapshot: () => ShopCameraScanQueueSnapshot
  noteSighting: (rawValue: string, now: number) => void
  reset: () => void
  setPaused: (paused: boolean) => void
  takeNext: () => ShopCameraScanLease | null
}

export function createShopCameraScanQueue(
  maxQueueSize = SHOP_CAMERA_SCAN_MAX_QUEUE,
  repeatReleaseMs = SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS,
): ShopCameraScanQueueController {
  let generation = 0
  let nextLeaseId = 1
  let paused = false
  let processingLease: ShopCameraScanLease | null = null
  let pendingValues: string[] = []
  const pendingValueSet = new Set<string>()
  const lastSeenAtByValue = new Map<string, number>()

  const pruneExpiredSightings = (now: number) => {
    for (const [value, lastSeenAt] of lastSeenAtByValue) {
      if (now - lastSeenAt >= repeatReleaseMs) {
        lastSeenAtByValue.delete(value)
      }
    }
  }

  const recordSighting = (value: string, now: number) => {
    pruneExpiredSightings(now)
    const previousSeenAt = lastSeenAtByValue.get(value)
    lastSeenAtByValue.set(value, now)
    return previousSeenAt
  }

  return {
    enqueue(rawValue, now) {
      const value = rawValue.trim()
      if (!value) {
        return 'invalid'
      }

      const previousSeenAt = recordSighting(value, now)
      // 每次识别都刷新“仍在画面中”的时间；只有真正移开足够久后，同码才可再次加购。
      if (paused) {
        return 'paused'
      }

      if (
        processingLease?.value === value ||
        pendingValueSet.has(value) ||
        (previousSeenAt !== undefined && now - previousSeenAt < repeatReleaseMs)
      ) {
        return 'duplicate'
      }

      const retainedCount = pendingValues.length + (processingLease ? 1 : 0)
      if (retainedCount >= maxQueueSize) {
        return 'full'
      }

      pendingValues.push(value)
      pendingValueSet.add(value)
      return 'queued'
    },
    finish(lease) {
      // reset 后到达的旧请求不得释放新会话正在处理的 lease。
      if (processingLease === lease) {
        processingLease = null
      }
    },
    getSnapshot() {
      return {
        pendingCount: pendingValues.length,
        processingValue: processingLease?.value,
      }
    },
    noteSighting(rawValue, now) {
      const value = rawValue.trim()
      if (value) {
        recordSighting(value, now)
      }
    },
    reset() {
      generation += 1
      paused = false
      processingLease = null
      pendingValues = []
      pendingValueSet.clear()
      lastSeenAtByValue.clear()
    },
    setPaused(nextPaused) {
      paused = nextPaused
    },
    takeNext() {
      if (paused || processingLease || pendingValues.length === 0) {
        return null
      }

      const value = pendingValues.shift()
      if (!value) {
        return null
      }

      pendingValueSet.delete(value)
      processingLease = {
        generation,
        id: nextLeaseId,
        value,
      }
      nextLeaseId += 1
      return processingLease
    },
  }
}
