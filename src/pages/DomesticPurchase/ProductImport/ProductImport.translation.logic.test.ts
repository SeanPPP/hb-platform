import {
  applyProductImportNameTranslations,
  containsChineseText,
} from './utils'
import type { ProductImportItem } from './types'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function createProduct(id: string, productName: string, englishName = ''): ProductImportItem {
  return {
    id,
    selected: false,
    imageUrl: '',
    customImage: false,
    imageLoadStatus: 'success',
    newProduct: {
      quantity: 1,
      productCode: id.toUpperCase(),
      productName,
      englishName,
    },
    status: 'unchanged',
    isDuplicate: false,
    calculated: { totalProducts: 1, totalVolume: 0 },
  }
}

const sourceProducts = [
  createProduct('row-1', '250g塑形泥红棕色'),
  createProduct('row-2', '切割板夹白芯A3厚2mm蓝粉', 'Old English Name'),
  createProduct('row-3', 'Canvas Frame 20x30cm'),
]

assertEqual(containsChineseText('250g塑形泥红棕色'), true, '中文检测应识别含中文商品名')
assertEqual(containsChineseText('250g Modeling Clay Reddish Brown'), false, '中文检测不应误判英文商品名')

const selectedResult = applyProductImportNameTranslations(
  sourceProducts,
  {
    '250g塑形泥红棕色': '250g Modeling Clay Reddish Brown',
    '切割板夹白芯A3厚2mm蓝粉': 'Cutting Board Clip White Core A3 2mm Blue Pink',
  },
  ['row-1'],
)

assertEqual(selectedResult.appliedCount, 1, '有选中行时只应应用选中行翻译')
assertDeepEqual(
  selectedResult.products.map((product) => product.newProduct.englishName),
  ['250g Modeling Clay Reddish Brown', 'Old English Name', ''],
  '有选中行时未选中行英文名称应保持不变',
)

const allRowsResult = applyProductImportNameTranslations(
  sourceProducts,
  {
    '250g塑形泥红棕色': '250g Modeling Clay Reddish Brown',
    '切割板夹白芯A3厚2mm蓝粉': 'Cutting Board Clip White Core A3 2mm Blue Pink',
  },
  [],
)

assertEqual(allRowsResult.appliedCount, 2, '无选中行时应翻译全部有效中文商品名')
assertDeepEqual(
  allRowsResult.products.map((product) => product.newProduct.englishName),
  [
    '250g Modeling Clay Reddish Brown',
    'Cutting Board Clip White Core A3 2mm Blue Pink',
    '',
  ],
  '无选中行时应填入全部有效英文名称并覆盖旧英文名',
)

const invalidResult = applyProductImportNameTranslations(
  sourceProducts,
  {
    '250g塑形泥红棕色': '250g塑形泥红棕色',
    '切割板夹白芯A3厚2mm蓝粉': 'Cutting Board Clip 白芯 A3',
  },
  [],
)

assertEqual(invalidResult.appliedCount, 0, '原文或仍含中文的翻译结果不应写入')
assertEqual(invalidResult.skippedCount, 2, '无效翻译结果应计入跳过数量')
assertDeepEqual(
  invalidResult.products.map((product) => product.newProduct.englishName),
  ['', 'Old English Name', ''],
  '无效翻译结果应保留原英文名称',
)

const duplicateNameResult = applyProductImportNameTranslations(
  [
    createProduct('row-1', '塑形泥'),
    createProduct('row-2', '塑形泥'),
  ],
  { 塑形泥: 'Modeling Clay' },
  [],
)

assertEqual(duplicateNameResult.appliedCount, 2, '重复商品名称应使用同一个翻译结果写入多行')
assertDeepEqual(
  duplicateNameResult.products.map((product) => product.newProduct.englishName),
  ['Modeling Clay', 'Modeling Clay'],
  '重复商品名称行应全部填入英文名称',
)
