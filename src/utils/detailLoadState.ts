export interface DetailInitialLoadingInput {
  requestedDetailId: string
  loadedDetailId: string | null
  visibleDetailId: string | null
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
