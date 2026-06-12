import {
  buildSupplierImageBatchScopeRequest,
  getDefaultSupplierImageBatchScope,
} from './productImageBatchScope'

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

assertEqual(
  getDefaultSupplierImageBatchScope(['P001']),
  'selected',
  '已有选中商品时应默认更新选中商品',
)

assertEqual(
  getDefaultSupplierImageBatchScope([]),
  'supplier',
  '没有选中商品时应默认更新供应商全部商品',
)

assertDeepEqual(
  buildSupplierImageBatchScopeRequest('selected', ['P001', 'P002']),
  { productCodes: ['P001', 'P002'] },
  '选择更新选中商品时应提交 productCodes',
)

assertDeepEqual(
  buildSupplierImageBatchScopeRequest('supplier', ['P001', 'P002']),
  {},
  '选择供应商全部商品时不应提交 productCodes',
)

let missingSelectionError = ''
try {
  buildSupplierImageBatchScopeRequest('selected', [])
} catch (error) {
  missingSelectionError = error instanceof Error ? error.message : ''
}

assertEqual(
  missingSelectionError,
  '请先选择商品',
  '选择更新选中商品但没有已选商品时应提示选择商品',
)

console.log('productImageBatchScope.test: ok')
