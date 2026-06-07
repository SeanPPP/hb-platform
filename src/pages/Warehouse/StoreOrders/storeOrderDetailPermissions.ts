import { StoreOrderFlowStatus } from '../../../types/storeOrder'

export interface StoreOrderDetailPermissions {
  canEditOrder: boolean
  canEditOutboundDate: boolean
  canStartPicking: boolean
  canCompleteOrder: boolean
  isReadonlyOrder: boolean
}

export function deriveStoreOrderDetailPermissions(flowStatus?: StoreOrderFlowStatus | null): StoreOrderDetailPermissions {
  const canEditOrder =
    flowStatus === StoreOrderFlowStatus.Submitted ||
    flowStatus === StoreOrderFlowStatus.Picking
  const canStartPicking = flowStatus === StoreOrderFlowStatus.Submitted
  const canCompleteOrder =
    flowStatus === StoreOrderFlowStatus.Submitted ||
    flowStatus === StoreOrderFlowStatus.Picking

  return {
    canEditOrder,
    canEditOutboundDate: true,
    canStartPicking,
    canCompleteOrder,
    isReadonlyOrder: !canEditOrder,
  }
}
