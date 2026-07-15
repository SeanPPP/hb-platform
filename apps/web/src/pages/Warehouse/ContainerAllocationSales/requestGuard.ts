export interface LatestRequestGuard {
  begin: () => number
  isLatest: (requestId: number) => boolean
  invalidate: () => void
}

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
