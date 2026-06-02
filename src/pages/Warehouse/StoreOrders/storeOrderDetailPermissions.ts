import { StoreOrderFlowStatus } from '../../../types/storeOrder'

export interface StoreOrderDetailPermissions {
  canEditOrder: boolean
  canStartPicking: boolean
  canCompleteOrder: boolean
  isReadonlyOrder: boolean
}

export function deriveStoreOrderDetailPermissions(flowStatus?: StoreOrderFlowStatus | null): StoreOrderDetailPermissions {
  const canEditOrder =
    flowStatus === StoreOrderFlowStatus.ShoppingCart ||
    flowStatus === StoreOrderFlowStatus.Submitted
  const canStartPicking = flowStatus === StoreOrderFlowStatus.Submitted
  const canCompleteOrder =
    flowStatus === StoreOrderFlowStatus.Submitted ||
    flowStatus === StoreOrderFlowStatus.Picking

  return {
    canEditOrder,
    canStartPicking,
    canCompleteOrder,
    isReadonlyOrder: !canEditOrder,
  }
}
