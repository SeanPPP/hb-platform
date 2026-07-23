import { readFileSync } from 'node:fs'
import { buildAssignContainerItems, detectDuplicates, mergeDuplicateProducts } from './utils'
import type { ProductImportItem } from './types'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assert(condition: boolean, label: string) {
  if (!condition) throw new Error(label)
}

function createProduct({
  id,
  productCode,
  quantity,
  casePackQuantity,
  volume,
  productName = '保留首行商品',
}: {
  id: string
  productCode: string
  quantity: number
  casePackQuantity?: number
  volume?: number
  productName?: string
}): ProductImportItem {
  return {
    id,
    selected: true,
    imageUrl: `${id}.jpg`,
    customImage: true,
    imageLoadStatus: 'success',
    newProduct: {
      quantity,
      productCode,
      productName,
      barcode: `barcode-${id}`,
      domesticPrice: 1.25,
      casePackQuantity,
      volume,
    },
    matchedProduct: {
      productCode: `local-${id}`,
      hbProductNo: productCode.trim(),
      packingQuantity: casePackQuantity,
      unitVolume: volume,
    },
    status: 'duplicate',
    isDuplicate: true,
    duplicateGroup: productCode.trim(),
    diffFields: ['packingQuantity'],
    errors: { productCode: '旧错误' },
    calculated: { totalProducts: quantity, totalVolume: quantity * (volume ?? 0) },
  }
}

const first = createProduct({ id: 'first', productCode: ' HB-001 ', quantity: 2, casePackQuantity: 360, volume: 0.018 })
const second = createProduct({ id: 'second', productCode: 'HB-001', quantity: 3, casePackQuantity: 360, volume: 0.018, productName: '不应覆盖首行' })
const lowerCase = createProduct({ id: 'lower', productCode: 'hb-001', quantity: 1, casePackQuantity: 12, volume: 0.01 })
const sourceProducts = [first, second, lowerCase]
const sourceSnapshot = JSON.stringify(sourceProducts)

const groups = detectDuplicates(sourceProducts)
assertDeepEqual(
  groups.map((group) => ({
    productCode: group.productCode,
    count: group.count,
    merged: group.merged,
    invalidFields: group.invalidFields,
    isMergeable: group.isMergeable,
  })),
  [{
    productCode: 'HB-001',
    count: 2,
    merged: { quantity: 1, casePackQuantity: 1800, volume: 0.09 },
    invalidFields: [],
    isMergeable: true,
  }],
  '重复货号应按原件数加权汇总装箱数和体积，大小写不同的货号不合并',
)

const mergeResult = mergeDuplicateProducts(sourceProducts)
assertDeepEqual(
  {
    invalidGroupCount: mergeResult.invalidGroups.length,
    mergedGroupCount: mergeResult.mergedGroupCount,
    productCount: mergeResult.products.length,
    merged: mergeResult.products[0]?.newProduct,
    status: mergeResult.products[0]?.status,
    isDuplicate: mergeResult.products[0]?.isDuplicate,
    duplicateGroup: mergeResult.products[0]?.duplicateGroup,
    mergedFrom: mergeResult.products[0]?.mergedFrom,
    diffFields: mergeResult.products[0]?.diffFields,
    errors: mergeResult.products[0]?.errors,
    calculated: mergeResult.products[0]?.calculated,
    untouchedCode: mergeResult.products[1]?.newProduct.productCode,
  },
  {
    invalidGroupCount: 0,
    mergedGroupCount: 1,
    productCount: 2,
    merged: {
      quantity: 1,
      productCode: ' HB-001 ',
      productName: '保留首行商品',
      barcode: 'barcode-first',
      domesticPrice: 1.25,
      casePackQuantity: 1800,
      volume: 0.09,
    },
    status: 'unchanged',
    isDuplicate: false,
    mergedFrom: 2,
    diffFields: [],
    calculated: { totalProducts: 1, totalVolume: 0.09 },
    untouchedCode: 'hb-001',
  },
  '合并应保留首行业务字段、清理重复检测状态并重新计算统计字段',
)

assert(JSON.stringify(sourceProducts) === sourceSnapshot, '合并不得修改输入数组中的原始业务数据')
assert(mergeResult.products[0]?.newProduct !== first.newProduct, '合并行 newProduct 必须与首行解除引用共享')
assert(mergeResult.products[0]?.matchedProduct !== first.matchedProduct, '合并行 matchedProduct 必须与首行解除引用共享')
assert(mergeResult.products[0]?.calculated !== first.calculated, '合并行 calculated 必须与首行解除引用共享')

assertDeepEqual(
  buildAssignContainerItems([mergeResult.products[0]!]),
  [{
    hbProductNo: ' HB-001 ',
    productCode: 'local-first',
    quantity: 1,
    packingQuantity: 1800,
    unitVolume: 0.09,
    domesticPrice: 1.25,
    oemPrice: undefined,
    notes: undefined,
  }],
  '合并后的加权装箱数和体积应继续进入发送货柜请求',
)

const validGroup = [
  createProduct({ id: 'valid-1', productCode: 'VALID', quantity: 1, casePackQuantity: 10, volume: 0.02 }),
  createProduct({ id: 'valid-2', productCode: 'VALID', quantity: 2, casePackQuantity: 10, volume: 0.02 }),
]
const invalidGroup = [
  createProduct({ id: 'invalid-1', productCode: 'INVALID', quantity: 1, casePackQuantity: 20, volume: undefined }),
  createProduct({ id: 'invalid-2', productCode: 'INVALID', quantity: 1, casePackQuantity: 20, volume: 0.03 }),
]
const atomicSource = [...validGroup, ...invalidGroup]
const atomicResult = mergeDuplicateProducts(atomicSource)
assert(atomicResult.products === atomicSource, '任一重复组无效时必须原样返回整个输入列表')
assertDeepEqual(
  atomicResult.invalidGroups.map((group) => ({ productCode: group.productCode, invalidFields: group.invalidFields })),
  [{ productCode: 'INVALID', invalidFields: ['volume'] }],
  '原子合并失败应返回问题货号和无效字段',
)
assertDeepEqual(atomicResult.mergedGroupCount, 0, '原子合并失败时不得报告任何成功组')

const multipleGroupResult = mergeDuplicateProducts([
  ...validGroup,
  createProduct({ id: 'other-1', productCode: 'OTHER', quantity: 4, casePackQuantity: 6, volume: 0.015 }),
  createProduct({ id: 'other-2', productCode: 'OTHER', quantity: 1, casePackQuantity: 6, volume: 0.015 }),
  createProduct({ id: 'single', productCode: 'SINGLE', quantity: 1, casePackQuantity: 8, volume: 0.01 }),
])
assertDeepEqual(
  multipleGroupResult.products.map((product) => ({
    code: product.newProduct.productCode,
    quantity: product.newProduct.quantity,
    packingQuantity: product.newProduct.casePackQuantity,
    volume: product.newProduct.volume,
  })),
  [
    { code: 'VALID', quantity: 1, packingQuantity: 30, volume: 0.06 },
    { code: 'OTHER', quantity: 1, packingQuantity: 30, volume: 0.075 },
    { code: 'SINGLE', quantity: 1, packingQuantity: 8, volume: 0.01 },
  ],
  '多个有效重复组应一次合并，并保持单例商品原位置和值不变',
)
assertDeepEqual(multipleGroupResult.mergedGroupCount, 2, '多个有效重复组应返回准确成功组数')

const invalidValues = [undefined, 0, -1, Number.NaN, Number.POSITIVE_INFINITY]
for (const invalidValue of invalidValues) {
  const quantityGroups = detectDuplicates([
    createProduct({ id: `q-1-${String(invalidValue)}`, productCode: 'Q', quantity: invalidValue as number, casePackQuantity: 12, volume: 0.02 }),
    createProduct({ id: `q-2-${String(invalidValue)}`, productCode: 'Q', quantity: 1, casePackQuantity: 12, volume: 0.02 }),
  ])
  assert(quantityGroups[0]?.invalidFields.includes('quantity') === true, `件数 ${String(invalidValue)} 应阻止合并`)

  const packingGroups = detectDuplicates([
    createProduct({ id: `p-1-${String(invalidValue)}`, productCode: 'P', quantity: 1, casePackQuantity: invalidValue, volume: 0.02 }),
    createProduct({ id: `p-2-${String(invalidValue)}`, productCode: 'P', quantity: 1, casePackQuantity: 12, volume: 0.02 }),
  ])
  assert(packingGroups[0]?.invalidFields.includes('casePackQuantity') === true, `装箱数 ${String(invalidValue)} 应阻止合并`)

  const volumeGroups = detectDuplicates([
    createProduct({ id: `v-1-${String(invalidValue)}`, productCode: 'V', quantity: 1, casePackQuantity: 12, volume: invalidValue }),
    createProduct({ id: `v-2-${String(invalidValue)}`, productCode: 'V', quantity: 1, casePackQuantity: 12, volume: 0.02 }),
  ])
  assert(volumeGroups[0]?.invalidFields.includes('volume') === true, `体积 ${String(invalidValue)} 应阻止合并`)
}

const overflowGroup = detectDuplicates([
  createProduct({ id: 'overflow-1', productCode: 'OVERFLOW', quantity: 2, casePackQuantity: Number.MAX_VALUE, volume: Number.MAX_VALUE }),
  createProduct({ id: 'overflow-2', productCode: 'OVERFLOW', quantity: 1, casePackQuantity: 1, volume: 0.01 }),
])[0]
assertDeepEqual(
  overflowGroup?.invalidFields,
  ['casePackQuantity', 'volume'],
  '加权结果溢出为非有限值时应阻止合并',
)

const roundedToZeroSource = [
  createProduct({ id: 'tiny-1', productCode: 'TINY', quantity: 2, casePackQuantity: 12, volume: 0.0001 }),
  createProduct({ id: 'tiny-2', productCode: 'TINY', quantity: 2, casePackQuantity: 12, volume: 0.0001 }),
]
const roundedToZeroResult = mergeDuplicateProducts(roundedToZeroSource)
assert(roundedToZeroResult.products === roundedToZeroSource, '体积汇总保留三位后为 0 时必须整批原样返回')
assertDeepEqual(
  roundedToZeroResult.invalidGroups.map((group) => ({ productCode: group.productCode, invalidFields: group.invalidFields })),
  [{ productCode: 'TINY', invalidFields: ['volume'] }],
  '体积汇总保留三位后为 0 时应报告体积字段错误',
)

const dialogSource = readFileSync('src/pages/DomesticPurchase/ProductImport/DuplicateDialog.tsx', 'utf8')
const pageSource = readFileSync('src/pages/DomesticPurchase/ProductImport/index.tsx', 'utf8')
const mergeHandlerStart = pageSource.indexOf('const handleMergeDuplicates')
const mergeHandlerEnd = pageSource.indexOf('const handleBatchTranslate', mergeHandlerStart)
const mergeHandlerSource = pageSource.slice(mergeHandlerStart, mergeHandlerEnd)
assert(mergeHandlerStart >= 0 && mergeHandlerEnd > mergeHandlerStart, '应能定位合并确认处理器以验证成功后的状态清理')
assertDeepEqual(
  [
    dialogSource.includes("productImport.mergedPackingQuantity"),
    dialogSource.includes("productImport.mergedUnitVolume"),
    dialogSource.includes("productImport.mergeValidation"),
    dialogSource.includes('disabled={invalidGroupCount > 0}'),
    dialogSource.includes('scroll={{ x: 820 }}'),
    mergeHandlerSource.includes('setDuplicateDialogOpen(false)'),
    mergeHandlerSource.includes('setDuplicateGroups([])'),
    mergeHandlerSource.includes('setShowStatistics(false)'),
    mergeHandlerSource.includes('needsDetection: true'),
    pageSource.includes('PackingQuantity: p.newProduct.casePackQuantity'),
    pageSource.includes('UnitVolume: p.newProduct.volume'),
    pageSource.includes('updateItem.PackingQuantity = product.newProduct.casePackQuantity'),
    pageSource.includes('updateItem.UnitVolume = product.newProduct.volume'),
  ],
  [true, true, true, true, true, true, true, true, true, true, true, true, true],
  '重复合并弹窗和成功状态应提供预览、原子阻断、窄屏滚动、完整状态清理及主表字段写回链路',
)
