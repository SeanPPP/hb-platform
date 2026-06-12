export interface DetailInitialLoadingInput {
  requestedDetailId: string
  loadedDetailId: string | null
  visibleDetailId: string | null
  requestedDetailQueryKey?: string
  loadedDetailQueryKey?: string | null
}

export function shouldShowDetailInitialLoading({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId,
}: DetailInitialLoadingInput) {
  if (!requestedDetailId) {
    return false
  }

  return loadedDetailId !== requestedDetailId || visibleDetailId !== requestedDetailId
}

export function shouldSkipDetailAutoReload({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId,
  requestedDetailQueryKey,
  loadedDetailQueryKey,
}: DetailInitialLoadingInput) {
  if (!requestedDetailId) {
    return false
  }

  // KeepAlive Tab 恢复时，只有同一详情且查询条件一致，才跳过自动刷新。
  const isSameDetail = loadedDetailId === requestedDetailId && visibleDetailId === requestedDetailId
  if (!isSameDetail) {
    return false
  }

  if (requestedDetailQueryKey !== undefined || loadedDetailQueryKey !== undefined) {
    return requestedDetailQueryKey === loadedDetailQueryKey
  }

  return true
}
