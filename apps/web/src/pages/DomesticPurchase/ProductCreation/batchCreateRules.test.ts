import assert from 'node:assert/strict'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import {
  applyBatchAddProducts,
  buildPreviewItems,
  buildCreateBatchItems,
  createDraftProduct,
  findInvalidSetProduct,
  normalizeCreateCount,
} from './batchCreateRules'
import type { DraftProductItem } from './batchCreateRules'

let keyIndex = 0
const nextKey = (prefix: string) => `${prefix}-${keyIndex++}`

const products: DraftProductItem[] = [
  {
    key: 'normal-1',
    productName: ' 普通商品 ',
    productType: ProductCreationType.NORMAL,
    privateLabelPrice: 12.5,
  },
  {
    key: 'set-1',
    productName: ' 套装商品 ',
    productType: ProductCreationType.SET,
    createCount: 2.9,
    setQuantity: 1,
    setPrice: 25,
    subItems: [
      { key: 'empty-sub', productName: ' ', privateLabelPrice: null },
      { key: 'sub-1', productName: ' 子项商品 ', privateLabelPrice: 8 },
    ],
  },
]

assert.equal(normalizeCreateCount(undefined), 1)
assert.equal(normalizeCreateCount(0), 1)
assert.equal(normalizeCreateCount(2.9), 2)

assert.equal(findInvalidSetProduct(products), undefined)
assert.deepEqual(findInvalidSetProduct([
  { key: 'set-empty', productName: '', productType: ProductCreationType.SET, subItems: [] },
]), { key: 'set-empty', index: 1 })

const requestItems = buildCreateBatchItems(products)
assert.equal(requestItems[0].productName, '普通商品')
assert.equal(requestItems[0].createCount, undefined)
assert.equal(requestItems[1].productName, '套装商品')
assert.equal(requestItems[1].createCount, 2)
assert.equal(requestItems[1].subItems?.length, 1)
assert.equal(requestItems[1].subItems?.[0].productName, '子项商品')

keyIndex = 0
const appended = applyBatchAddProducts({
  products: [createDraftProduct(ProductCreationType.NORMAL, 0, null, nextKey)],
  selectedRowKeys: [],
  expandedRowKeys: [],
  type: ProductCreationType.SET,
  count: 2,
  price: 9.5,
  mode: 'append',
  createProduct: (type, index, price) => createDraftProduct(type, index, price, nextKey),
})

assert.equal(appended.products.length, 3)
assert.equal(appended.products[0].productType, ProductCreationType.NORMAL)
assert.equal(appended.products[1].productType, ProductCreationType.SET)
assert.equal(appended.products[2].productType, ProductCreationType.SET)
assert.deepEqual(appended.expandedRowKeys, ['temp-1', 'temp-3'])
assert.equal(appended.products[1].subItems?.length, 1)

keyIndex = 0
const overwritten = applyBatchAddProducts({
  products,
  selectedRowKeys: ['normal-1'],
  expandedRowKeys: ['set-1'],
  type: ProductCreationType.SET,
  count: 2,
  price: 10,
  mode: 'overwrite',
  createProduct: (type, index, price) => createDraftProduct(type, index, price, nextKey),
})

assert.equal(overwritten.products.length, 2)
assert.ok(overwritten.products.every((product) => product.productType === ProductCreationType.SET))
assert.deepEqual(overwritten.selectedRowKeys, [])
assert.deepEqual(overwritten.expandedRowKeys, ['temp-0', 'temp-2'])

const previewItems = buildPreviewItems([
  {
    key: 'set-preview',
    productName: '套装',
    productType: ProductCreationType.SET,
    createCount: 2,
    subItems: [
      { key: 'sub-preview', productName: ' 子项 ', privateLabelPrice: 3 },
      { key: 'price-only', productName: '   ', privateLabelPrice: 4 },
    ],
  },
], 'HB')

assert.deepEqual(previewItems.map((item) => item.itemNumber), ['HB0001', 'HB0002', 'HB0003', 'HB0004', 'HB0005', 'HB0006'])
assert.equal(previewItems[1].productName, '子项')
assert.equal(previewItems[2].productName, '')
