import { shouldShowDetailInitialLoading } from '../../../utils/detailLoadState'

export interface StoreOrderDetailPageLoadGateInput {
  keepAliveActive: boolean
  isMobileLayout: boolean
}

export function shouldLoadStoreOrderDetailPage({
  keepAliveActive,
  isMobileLayout,
}: StoreOrderDetailPageLoadGateInput) {
  // 移动布局直接渲染当前页面，没有 KeepAlive Provider；此时 context 的 active=false 不能阻止首次加载。
  return isMobileLayout || keepAliveActive
}

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
