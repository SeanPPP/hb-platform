import { buildBatchCopyOrderQuantityPayload, shouldSubmitBatchCopyOrderQuantity } from './batchCopyOrderQuantity'
import type { StoreOrderDetailLine } from '../../../types/storeOrder'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function createLine(overrides: Partial<StoreOrderDetailLine>): StoreOrderDetailLine {
  return {
    detailGUID: overrides.detailGUID ?? `detail-${overrides.productCode ?? 'P001'}`,
    productCode: overrides.productCode ?? 'P001',
    quantity: overrides.quantity ?? 1,
    allocQuantity: overrides.allocQuantity,
    price: overrides.price ?? 0,
    amount: overrides.amount ?? 0,
    importPrice: overrides.importPrice ?? 0,
    importAmount: overrides.importAmount ?? 0,
    minOrderQuantity: overrides.minOrderQuantity ?? 1,
    isActive: overrides.isActive ?? true,
  }
}

function runTest(name: string, execute: () => void) {
  try {
    execute()
    console.log(`ok - ${name}`)
  } catch (error) {
    console.error(`not ok - ${name}`)
    throw error
  }
}

runTest('批量复制应按每行订货数量生成发货数量 payload', () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: 'P001', quantity: 12 }),
    createLine({ productCode: 'P002', quantity: 7 }),
  ])

  assertDeepEqual(
    result.items,
    [
      { detailGUID: 'detail-P001', productCode: 'P001', quantity: 12 },
      { detailGUID: 'detail-P002', productCode: 'P002', quantity: 7 },
    ],
    'payload 应保留每行明细和不同订货数量',
  )
  assertEqual(result.overwriteCount, 0, '未发货行不应计入覆盖数量')
  assertEqual(result.zeroOrderQuantityCount, 0, '普通订货数量不应计入 0 订货提示')
  assertEqual(result.shouldConfirm, true, '普通批量复制也应先二次确认')
})

runTest('已有发货数量应计入覆盖提示', () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: 'P001', quantity: 12, allocQuantity: 6 }),
    createLine({ productCode: 'P002', quantity: 7, allocQuantity: 0 }),
  ])

  assertEqual(result.overwriteCount, 1, '只有 allocQuantity > 0 才算已有发货数量')
  assertEqual(result.shouldConfirm, true, '覆盖已有发货数量需要二次确认')
})

runTest('订货数量为 0 时仍生成 0 发货 payload 并计入提示', () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: 'P-ZERO', quantity: 0, allocQuantity: 0 }),
  ])

  assertDeepEqual(
    result.items,
    [{ detailGUID: 'detail-P-ZERO', productCode: 'P-ZERO', quantity: 0 }],
    '订货数量 0 也应允许复制为发货数量 0',
  )
  assertEqual(result.zeroOrderQuantityCount, 1, '订货数量 0 应计入提示数量')
  assertEqual(result.shouldConfirm, true, '订货数量 0 需要二次确认')
})

runTest('需要确认的复制操作取消后不应继续提交', () => {
  const riskyPayload = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: 'P001', quantity: 12, allocQuantity: 6 }),
  ])
  const safePayload = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: 'P002', quantity: 7, allocQuantity: 0 }),
  ])

  assertEqual(shouldSubmitBatchCopyOrderQuantity(riskyPayload, false), false, '取消风险确认后不应提交')
  assertEqual(shouldSubmitBatchCopyOrderQuantity(riskyPayload, true), true, '确认风险提示后应允许提交')
  assertEqual(shouldSubmitBatchCopyOrderQuantity(safePayload, false), false, '取消普通复制确认后也不应提交')
  assertEqual(shouldSubmitBatchCopyOrderQuantity(safePayload, true), true, '确认普通复制后应允许提交')
  assertEqual(
    shouldSubmitBatchCopyOrderQuantity(
      {
        items: [],
        overwriteCount: 0,
        zeroOrderQuantityCount: 0,
        shouldConfirm: false,
      },
      true,
    ),
    false,
    '空复制 payload 不应提交',
  )
})
