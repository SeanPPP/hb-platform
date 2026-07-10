import { readFileSync } from 'node:fs'
import { updateHbwebProductNames } from '../../../services/domesticProductImportService'
import type { ProductImportItem } from './types'
import { buildHbwebProductNameUpdates } from './utils'

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

function createProduct(overrides: Omit<Partial<ProductImportItem>, 'newProduct'> & { newProduct?: Partial<ProductImportItem['newProduct']> }): ProductImportItem {
  const base: ProductImportItem = {
    id: 'row-1',
    selected: true,
    imageUrl: '',
    customImage: false,
    imageLoadStatus: 'success',
    newProduct: {
      quantity: 1,
      productCode: 'HB001',
      productName: '测试商品',
      englishName: 'TEST PRODUCT',
    },
    status: 'unchanged',
    isDuplicate: false,
    calculated: { totalProducts: 1, totalVolume: 0 },
  }

  return {
    ...base,
    ...overrides,
    newProduct: {
      ...base.newProduct,
      ...overrides.newProduct,
    },
  }
}

assertDeepEqual(
  buildHbwebProductNameUpdates([
    createProduct({ id: 'row-1', newProduct: { productCode: ' HB001 ', englishName: ' TEST PRODUCT ' } }),
    createProduct({ id: 'row-2', newProduct: { productCode: 'HB002', englishName: 'SECOND PRODUCT' } }),
    createProduct({ id: 'row-3', newProduct: { productCode: 'HB003', englishName: 'UNSELECTED PRODUCT' } }),
  ], ['row-1', 'row-2']),
  {
    products: [
      { ItemNumber: 'HB001', ProductName: 'TEST PRODUCT' },
      { ItemNumber: 'HB002', ProductName: 'SECOND PRODUCT' },
    ],
    missingItemNumbers: [],
    missingProductNames: [],
    conflictItemNumbers: [],
  },
  '应只用选中行英文名称构建 HBweb Product.ProductName 更新 payload',
)

assertDeepEqual(
  buildHbwebProductNameUpdates([
    createProduct({ id: 'missing-name', newProduct: { productCode: 'HB004', englishName: '   ' } }),
    createProduct({ id: 'missing-item', newProduct: { productCode: '   ', englishName: 'HAS NAME' } }),
  ], ['missing-name', 'missing-item']),
  {
    products: [],
    missingItemNumbers: ['missing-item'],
    missingProductNames: ['HB004'],
    conflictItemNumbers: [],
  },
  '应拦截缺货号和缺英文名称的选中行',
)

assertDeepEqual(
  buildHbwebProductNameUpdates([
    createProduct({ id: 'same-1', newProduct: { productCode: 'HB005', englishName: 'SAME NAME' } }),
    createProduct({ id: 'same-2', newProduct: { productCode: 'HB005', englishName: 'SAME NAME' } }),
  ], ['same-1', 'same-2']),
  {
    products: [{ ItemNumber: 'HB005', ProductName: 'SAME NAME' }],
    missingItemNumbers: [],
    missingProductNames: [],
    conflictItemNumbers: [],
  },
  '同货号同英文名称应去重',
)

assertDeepEqual(
  buildHbwebProductNameUpdates([
    createProduct({ id: 'conflict-1', newProduct: { productCode: 'HB006', englishName: 'NAME A' } }),
    createProduct({ id: 'conflict-2', newProduct: { productCode: 'HB006', englishName: 'NAME B' } }),
  ], ['conflict-1', 'conflict-2']).conflictItemNumbers,
  ['HB006'],
  '同货号不同英文名称应报告冲突',
)

const pageSource = readFileSync('src/pages/DomesticPurchase/ProductImport/index.tsx', 'utf8')
const serviceSource = readFileSync('src/services/domesticProductImportService.ts', 'utf8')
const zhLocaleSource = readFileSync('src/i18n/locales/zh.json', 'utf8')
const enLocaleSource = readFileSync('src/i18n/locales/en.json', 'utf8')

assertDeepEqual(
  [
    pageSource.includes('handleUpdateHbwebProductNames'),
    pageSource.includes('buildHbwebProductNameUpdates(state.products, state.selectedIds)'),
    pageSource.includes('updateHbwebProductNames({ Products: updatePayload.products })'),
    pageSource.includes('state.selectedIds.length === 0 || state.saving || state.detecting || translating'),
    pageSource.includes('Product.ProductName'),
    pageSource.includes('<EditOutlined />'),
    serviceSource.includes('/product-master-names'),
    zhLocaleSource.includes('"updateHbwebProductNames": "更新主表名称"'),
    enLocaleSource.includes('"updateHbwebProductNames": "Update Master Name"'),
  ],
  [true, true, true, true, true, true, true, true, true],
  '页面应暴露更新主表名称按钮、确认文案和服务调用',
)

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined
let nextPayload: unknown = {}
let nextStatus = 200

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(JSON.stringify(nextPayload), {
    status: nextStatus,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  nextPayload = {
    success: true,
    data: {
      updatedCount: 1,
      unchangedCount: 0,
      missingItemNumbers: [],
      errors: [],
    },
    message: 'ok',
  }

  const response = await updateHbwebProductNames({
    Products: [{ ItemNumber: 'HB001', ProductName: 'TEST PRODUCT' }],
  })

  assertEqual(capturedUrl, '/api/react/v1/domestic-products/product-master-names', '服务应调用国内商品主表名称更新接口')
  assertEqual(capturedInit?.method, 'PUT', '服务应使用 PUT')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { Products: [{ ItemNumber: 'HB001', ProductName: 'TEST PRODUCT' }] },
    '服务应保留 PascalCase 请求体',
  )
  assertEqual(response.data?.updatedCount, 1, '服务应返回更新数量')

  nextStatus = 400
  nextPayload = {
    success: false,
    errorCode: 'INVALID_HBWEB_PRODUCT_NAMES',
    message: '存在无效货号或商品名称，请先修正后再更新',
    data: {
      updatedCount: 0,
      unchangedCount: 0,
      missingItemNumbers: [],
      errors: ['商品名称不能为空: HB002'],
    },
  }

  const failedResponse = await updateHbwebProductNames({
    Products: [{ ItemNumber: 'HB002', ProductName: '' }],
  })

  assertEqual(failedResponse.success, false, '服务应保留后端 400 业务失败标记')
  assertEqual(failedResponse.errorCode, 'INVALID_HBWEB_PRODUCT_NAMES', '服务应保留后端错误码')
  assertDeepEqual(failedResponse.data?.errors, ['商品名称不能为空: HB002'], '服务应保留后端错误明细')
} finally {
  globalThis.fetch = originalFetch
}
