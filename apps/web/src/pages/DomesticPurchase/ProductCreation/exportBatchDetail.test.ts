import assert from 'node:assert/strict'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { BatchProductItem } from '../../../types/domesticProductCreation'
import { getExportableBatchItems } from './exportBatchDetail'

const items: BatchProductItem[] = [
  {
    itemNumber: 'sub-2',
    hbProductNo: 'HB001-8001-02',
    barcode: '9527800100002',
    productName: '子项2',
    productType: ProductCreationType.SET_SUB_ITEM,
    privateLabelPrice: 8.5,
    parentItemNumber: 'HB001-8001',
  },
  {
    itemNumber: 'normal',
    hbProductNo: 'HB001-9001',
    barcode: '9527900100001',
    productName: '普通商品',
    productType: ProductCreationType.NORMAL,
    privateLabelPrice: 5,
  },
  {
    itemNumber: 'set',
    hbProductNo: 'HB001-8001',
    barcode: '9527800100001',
    productName: '套装商品',
    productType: ProductCreationType.SET,
    privateLabelPrice: 10,
    setQuantity: 2,
    setPrice: 39.99,
  },
  {
    itemNumber: 'sub-1',
    hbProductNo: 'HB001-8001-01',
    barcode: '9527800100003',
    productName: '子项1',
    productType: ProductCreationType.SET_SUB_ITEM,
    privateLabelPrice: 8.5,
    parentItemNumber: 'HB001-8001',
  },
  {
    itemNumber: 'sub-unmatched',
    hbProductNo: 'HB001-0000-01',
    barcode: '9527000000001',
    productName: '父货号异常子项',
    productType: ProductCreationType.SET_SUB_ITEM,
    privateLabelPrice: 9.5,
    parentItemNumber: 'HB001-MISSING',
  },
]

const exportableItems = getExportableBatchItems(items)

assert.deepEqual(
  exportableItems.map((item) => item.hbProductNo),
  ['HB001-8001', 'HB001-8001-01', 'HB001-8001-02', 'HB001-0000-01', 'HB001-9001'],
)
assert.equal(exportableItems[1].parentItemNumber, 'HB001-8001')
assert.equal(exportableItems[2].productType, ProductCreationType.SET_SUB_ITEM)
assert.equal(exportableItems[3].parentItemNumber, 'HB001-MISSING')

console.log('exportBatchDetail.test: ok')
