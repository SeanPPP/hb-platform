import {
  buildMatchedProductMasterUpdatePayload,
  getMatchedProductMasterUpdateTarget,
} from './matchedProductMasterUpdate'
import type { LocalSupplierInvoiceDetailDto, LocalSupplierInvoiceItemDto } from '../../../../types/localSupplierInvoice'
import type { PosProductDto } from '../../../../types/posProduct'

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

const matchedProduct: PosProductDto = {
  productCode: 'CRAFT301713',
  barcode: '9320760301713',
  productName: 'Paint Pot',
  itemNumber: 'OLD-ITEM',
  localSupplierCode: 'OLD-SUP',
  localSupplierName: 'Old Supplier',
  categoryGuid: 'category-1',
  purchasePrice: 1.11,
  retailPrice: 2.5,
  isActive: true,
  isAutoPricing: true,
  isSpecialProduct: false,
  productImage: '/old.jpg',
}

const invoice: LocalSupplierInvoiceDetailDto = {
  invoiceGUID: 'invoice-1',
  supplierCode: 'HEADER-SUP',
  supplierName: 'Header Supplier',
  createdAt: '2026-06-05T00:00:00Z',
}

async function main() {
  const failures: string[] = []

  const payloadFailure = await runTest('更换匹配商品主档时应保留完整商品字段并只覆盖货号和供应商', () => {
    const detail: LocalSupplierInvoiceItemDto = {
      detailGUID: 'detail-1',
      itemNumber: ' NEW-ITEM ',
      supplierCode: ' NEW-SUP ',
    }

    const payload = buildMatchedProductMasterUpdatePayload(matchedProduct, detail, invoice)

    assertEqual(payload.productCode, matchedProduct.productCode, '商品编码应保持不变')
    assertEqual(payload.barcode, matchedProduct.barcode, '条码应沿用完整商品详情')
    assertEqual(payload.productName, matchedProduct.productName, '商品名称应沿用完整商品详情')
    assertEqual(payload.categoryGuid, matchedProduct.categoryGuid, '前端分类字段应沿用完整商品详情')
    assertEqual(payload.productCategoryGUID, matchedProduct.categoryGuid, '后端分类字段应从完整商品详情显式映射')
    assertEqual(payload.purchasePrice, matchedProduct.purchasePrice, '进货价应沿用完整商品详情')
    assertEqual(payload.retailPrice, matchedProduct.retailPrice, '零售价应沿用完整商品详情')
    assertEqual(payload.itemNumber, 'NEW-ITEM', '货号应来自当前明细并去除首尾空格')
    assertEqual(payload.localSupplierCode, 'NEW-SUP', '供应商应优先来自当前明细并去除首尾空格')
  })
  if (payloadFailure) failures.push(payloadFailure)

  const fallbackFailure = await runTest('当前明细缺供应商时应回退到发票头供应商', () => {
    const detail: LocalSupplierInvoiceItemDto = {
      detailGUID: 'detail-2',
      itemNumber: 'HEADER-ITEM',
    }

    const target = getMatchedProductMasterUpdateTarget(detail, invoice)

    assertEqual(target.itemNumber, 'HEADER-ITEM', '目标货号应来自当前明细')
    assertEqual(target.supplierCode, 'HEADER-SUP', '目标供应商应回退到发票头')
  })
  if (fallbackFailure) failures.push(fallbackFailure)

  const backendCategoryFailure = await runTest('完整商品详情只返回后端分类字段时应保留 productCategoryGUID', () => {
    const productWithBackendCategory = {
      ...matchedProduct,
      categoryGuid: undefined,
      productCategoryGUID: 'backend-category-1',
    } as PosProductDto & { productCategoryGUID?: string }

    const payload = buildMatchedProductMasterUpdatePayload(
      productWithBackendCategory,
      { itemNumber: 'NEW-BACKEND', supplierCode: 'SUP-BACKEND' },
      invoice,
    )

    assertEqual(payload.productCategoryGUID, 'backend-category-1', '后端分类字段不应被 undefined 覆盖')
  })
  if (backendCategoryFailure) failures.push(backendCategoryFailure)

  const missingItemNumberFailure = await runTest('当前明细缺货号时应禁止更换匹配商品主档', () => {
    let threw = false
    try {
      buildMatchedProductMasterUpdatePayload(matchedProduct, { supplierCode: 'SUP' }, invoice)
    } catch (error) {
      threw = error instanceof Error && error.message.includes('当前明细缺少货号')
    }
    assert(threw, '缺货号时应抛出明确错误')
  })
  if (missingItemNumberFailure) failures.push(missingItemNumberFailure)

  const missingSupplierFailure = await runTest('当前明细和发票头都缺供应商时应禁止更换匹配商品主档', () => {
    let threw = false
    try {
      buildMatchedProductMasterUpdatePayload(
        matchedProduct,
        { itemNumber: 'ITEM-4' },
        {},
      )
    } catch (error) {
      threw = error instanceof Error && error.message.includes('当前明细缺少供应商')
    }
    assert(threw, '缺供应商时应抛出明确错误')
  })
  if (missingSupplierFailure) failures.push(missingSupplierFailure)

  if (failures.length) {
    console.error(`\n${failures.length} test(s) failed:`)
    for (const failure of failures) {
      console.error(`- ${failure}`)
    }
    process.exit(1)
  }
}

void main()
