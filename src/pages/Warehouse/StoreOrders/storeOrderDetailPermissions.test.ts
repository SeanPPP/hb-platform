import { StoreOrderFlowStatus } from '../../../types/storeOrder'
import { deriveStoreOrderDetailPermissions } from './storeOrderDetailPermissions'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${label}。Expected: ${expectedText}, received: ${actualText}`)
  }
}

function runTest(name: string, execute: () => void) {
  execute()
  console.log(`ok - ${name}`)
}

runTest('已提交和配货中订单应允许编辑明细', () => {
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(StoreOrderFlowStatus.Submitted),
    {
      canEditOrder: true,
      canEditOutboundDate: true,
      canStartPicking: true,
      canCompleteOrder: true,
      isReadonlyOrder: false,
    },
    '已提交订单权限矩阵不正确',
  )
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(StoreOrderFlowStatus.Picking),
    {
      canEditOrder: true,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: true,
      isReadonlyOrder: false,
    },
    '配货中订单权限矩阵不正确',
  )
})

runTest('购物车 已完成 未知状态都应按只读处理', () => {
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(StoreOrderFlowStatus.ShoppingCart),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true,
    },
    '购物车订单权限矩阵不正确',
  )
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(StoreOrderFlowStatus.Completed),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true,
    },
    '已完成订单权限矩阵不正确',
  )
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(undefined),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true,
    },
    '缺失状态应按只读处理',
  )
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(999 as StoreOrderFlowStatus),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true,
    },
    '未知状态应按只读处理',
  )
})

console.log('storeOrderDetailPermissions.test: ok')
