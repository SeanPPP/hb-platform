import { shouldShowDetailInitialLoading } from '../../../utils/detailLoadState'

export interface StoreOrderDetailInitialLoadingInput {
  requestedOrderId: string
  loadedOrderId: string | null
  visibleDetailId: string | null
}

export function shouldShowStoreOrderDetailInitialLoading({
  requestedOrderId,
  loadedOrderId,
  visibleDetailId,
}: StoreOrderDetailInitialLoadingInput) {
  return shouldShowDetailInitialLoading({
    requestedDetailId: requestedOrderId,
    loadedDetailId: loadedOrderId,
    visibleDetailId,
  })
}
