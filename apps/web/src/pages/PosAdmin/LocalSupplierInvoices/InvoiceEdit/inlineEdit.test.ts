import {
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
