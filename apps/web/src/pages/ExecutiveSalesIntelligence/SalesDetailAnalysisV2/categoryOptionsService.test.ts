import assert from 'node:assert/strict'
import { fetchSalesDetailCategoryOptions, normalizeSalesDetailCategoryOptions } from './categoryOptionsService'

const warehouse = normalizeSalesDetailCategoryOptions({ success: true, data: { warehouseCategories: [
  { categoryGUID: 'ROOT', categoryName: '家居', children: [
    { categoryGUID: 'CHILD', categoryName: '收纳', children: [] },
  ] },
] } })
assert.deepEqual(warehouse[0]?.options.map(option => [option.guid, option.name]), [
  ['ROOT', '家居'], ['CHILD', '家居 / 收纳'],
], '仓库分类的子节点必须可选')

const inactiveParent = normalizeSalesDetailCategoryOptions({ data: { warehouseCategories: [
  { categoryGuid: 'OLD', name: '旧分类', isActive: false, children: [
    { categoryGuid: 'ACTIVE', name: '新分类', isActive: true, children: [] },
  ] },
] } })
assert.deepEqual(inactiveParent[0]?.options.map(option => [option.guid, option.name]), [
  ['ACTIVE', '新分类'],
], '停用的父分类不应隐藏启用的子分类')

const supplier = normalizeSalesDetailCategoryOptions({ success: true, data: { supplierCategories: [
  { supplierCode: '200', categories: [{ categoryGuid: 'HB', name: '文具', children: [] }] },
  { supplierCode: '240', categories: [{ categoryGuid: 'AU', name: 'Cards', children: [
    { categoryGuid: 'AU-CHILD', name: 'Birthday', children: [] },
  ] }] },
] } })
assert.deepEqual(supplier.map(group => [group.supplierCode, ...group.options.map(option => option.guid)]), [
  ['200', 'HB'], ['240', 'AU', 'AU-CHILD'],
], '多个供应商的分类树必须保留各自子分类')

const originalFetch = globalThis.fetch
globalThis.fetch = (async (input: RequestInfo | URL) => {
  const url = new URL(String(input), 'http://localhost')
  assert.equal(url.pathname, '/api/react/v1/dashboard/sales-detail-view/category-options')
  assert.deepEqual(url.searchParams.getAll('supplierCodes'), ['200', '240'])
  return new Response(JSON.stringify({ success: true, data: { supplierCategories: [] } }),
    { headers: { 'content-type': 'application/json' } })
}) as typeof fetch
try {
  await fetchSalesDetailCategoryOptions('australia', ['200', '240'], new AbortController().signal)
} finally {
  globalThis.fetch = originalFetch
}
console.log('销售明细分类选项树及多供应商请求：通过')
