export interface LatestRequestGuard {
  begin: () => number
  isLatest: (requestId: number) => boolean
  invalidate: () => void
}

export interface LatestGuardedRequestHandlers<T> {
  onStart?: () => void
  onSuccess: (value: T) => void
  onError?: (error: unknown) => void
  onSettled?: () => void
}

/**
 * 网络请求无法可靠取消时，只允许最后一次请求写入页面状态。
 */
export function createLatestRequestGuard(): LatestRequestGuard {
  let latestRequestId = 0

  return {
    begin() {
      latestRequestId += 1
      return latestRequestId
    },
    isLatest(requestId) {
      return latestRequestId === requestId
    },
    invalidate() {
      latestRequestId += 1
    },
  }
}

/**
 * 统一请求生命周期；只有最后一次请求可以写入成功、错误和 loading 状态。
 */
export async function runLatestGuardedRequest<T>(
  guard: LatestRequestGuard,
  operation: () => Promise<T>,
  handlers: LatestGuardedRequestHandlers<T>,
): Promise<void> {
  const requestId = guard.begin()
  handlers.onStart?.()

  try {
    const result = await operation()
    if (guard.isLatest(requestId)) handlers.onSuccess(result)
  } catch (error) {
    if (guard.isLatest(requestId)) handlers.onError?.(error)
  } finally {
    if (guard.isLatest(requestId)) handlers.onSettled?.()
  }
}
