import {
  applyInvoiceDetailBatchEdit,
  applyInvoiceDetailInlineEdit,
  buildInvoiceDetailSaveItems,
  normalizeInvoiceDetailInlineValue,
} from './inlineEdit'
import type { LocalSupplierInvoiceItemDto } from '../../../../types/localSupplierInvoice'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

const baseDetails: LocalSupplierInvoiceItemDto[] = [
  {
    detailGUID: 'detail-1',
    itemNumber: '72750',
    barcode: '9311192727509',
    productName: 'Clean Angel Mop',
    quantity: 12,
    lastPurchasePrice: 1.94,
    purchasePrice: 0.44,
    retailPrice: 2.5,
    amount: 5.28,
    autoPricing: true,
    pricingFloatRate: 2.5,
    newAutoRetailPrice: 1.5,
    isSpecialProduct: false,
    discountRate: 0,
    existingProductCount: 1,
    barcodeStatus: 1,
    productImage: '/72750.jpg',
    activityType: 2,
  },
]

async function main() {
  const failures: string[] = []

  const amountFailure = await runTest('数量或本次进货价行内编辑后应重算金额', () => {
    const afterQuantity = applyInvoiceDetailInlineEdit(baseDetails, 'detail-1', 'quantity', 20)
    assertEqual(afterQuantity[0].quantity, 20, '应更新数量')
    assertEqual(afterQuantity[0].amount, 8.8, '数量变化后应按当前进货价重算金额')

    const afterPrice = applyInvoiceDetailInlineEdit(afterQuantity, 'detail-1', 'purchasePrice', 0.5)
    assertEqual(afterPrice[0].purchasePrice, 0.5, '应更新本次进货价')
    assertEqual(afterPrice[0].amount, 10, '进货价变化后应按当前数量重算金额')
  })
  if (amountFailure) failures.push(amountFailure)

  const discountFailure = await runTest('折扣率界面百分比应转为后端小数值', () => {
    const value = normalizeInvoiceDetailInlineValue('discountRate', 12.5)
    assertEqual(value, 0.125, '折扣率应从 12.5% 转成 0.125')

    const updated = applyInvoiceDetailInlineEdit(baseDetails, 'detail-1', 'discountRate', value)
    assertEqual(updated[0].discountRate, 0.125, '明细应保存折扣率小数值')
  })
  if (discountFailure) failures.push(discountFailure)

  const batchOptimisticFailure = await runTest('批量编辑本地乐观更新应只改勾选字段并保留 0 和 false', () => {
    const details: LocalSupplierInvoiceItemDto[] = [
      ...baseDetails,
      {
        ...baseDetails[0],
        detailGUID: 'detail-2',
        quantity: 5,
        purchasePrice: 1,
        retailPrice: 3,
        amount: 5,
        autoPricing: true,
        isSpecialProduct: false,
        discountRate: 0.2,
      },
    ]

    const updated = applyInvoiceDetailBatchEdit(details, ['detail-1'], {
      updatePurchasePrice: true,
      purchasePrice: 0,
      updateRetailPrice: true,
      retailPrice: 0,
      updateIsAutoPricing: true,
      isAutoPricing: false,
      updateIsSpecialProduct: true,
      isSpecialProduct: true,
      updateDiscountRate: true,
      discountRate: 0,
      updateAction: false,
    })

    assertEqual(updated[0].purchasePrice, 0, '进货价 0 应作为有效值写入')
    assertEqual(updated[0].amount, 0, '进货价变为 0 后金额应重算为 0')
    assertEqual(updated[0].retailPrice, 0, '零售价 0 应作为有效值写入')
    assertEqual(updated[0].autoPricing, false, '自动定价 false 应作为有效值写入')
    assertEqual(updated[0].isSpecialProduct, true, '特殊商品应按勾选值写入')
    assertEqual(updated[0].discountRate, 0, '折扣率 0 应作为有效值写入')
    assertEqual(updated[0].pricingFloatRate, 2.5, '前端乐观更新不应复制后端自动定价策略')
    assertDeepEqual(updated[1], details[1], '未选中的明细不应变化')
  })
  if (batchOptimisticFailure) failures.push(batchOptimisticFailure)

  const batchUncheckedFieldFailure = await runTest('批量编辑未勾选字段应保持不变', () => {
    const updated = applyInvoiceDetailBatchEdit(baseDetails, ['detail-1'], {
      updatePurchasePrice: false,
      purchasePrice: 0,
      updateRetailPrice: true,
      retailPrice: 1.99,
      updateIsAutoPricing: false,
      isAutoPricing: false,
      updateIsSpecialProduct: false,
      isSpecialProduct: true,
      updateDiscountRate: false,
      discountRate: 0.5,
      updateAction: false,
    })

    assertEqual(updated[0].purchasePrice, 0.44, '未勾选进货价时不应修改')
    assertEqual(updated[0].amount, 5.28, '未勾选进货价时金额不应重算')
    assertEqual(updated[0].retailPrice, 1.99, '勾选零售价时应修改')
    assertEqual(updated[0].autoPricing, true, '未勾选自动定价时不应修改')
    assertEqual(updated[0].isSpecialProduct, false, '未勾选特殊商品时不应修改')
    assertEqual(updated[0].discountRate, 0, '未勾选折扣率时不应修改')
  })
  if (batchUncheckedFieldFailure) failures.push(batchUncheckedFieldFailure)

  const payloadFailure = await runTest('保存明细 payload 应只包含业务可改字段和主键', () => {
    const items = buildInvoiceDetailSaveItems(baseDetails)
    assertDeepEqual(
      items,
      [
        {
          detailGUID: 'detail-1',
          itemNumber: '72750',
          barcode: '9311192727509',
          productName: 'Clean Angel Mop',
          quantity: 12,
          purchasePrice: 0.44,
          retailPrice: 2.5,
          amount: 5.28,
          autoPricing: true,
          pricingFloatRate: 2.5,
          newAutoRetailPrice: 1.5,
          isSpecialProduct: false,
          discountRate: 0,
        },
      ],
      'payload 不应携带图片、状态、上次进货价、操作类型等只读或独立操作字段',
    )
  })
  if (payloadFailure) failures.push(payloadFailure)

  const invalidFailure = await runTest('数字字段不允许负数', () => {
    let threw = false
    try {
      normalizeInvoiceDetailInlineValue('purchasePrice', -1)
    } catch {
      threw = true
    }
    assert(threw, '负数进货价应被拒绝')
  })
  if (invalidFailure) failures.push(invalidFailure)

  if (failures.length) {
    console.error(`\n${failures.length} test(s) failed:`)
    for (const failure of failures) {
      console.error(`- ${failure}`)
    }
    process.exit(1)
  }
}

void main()
