import { deepStrictEqual } from 'node:assert/strict'
import { getWarehouseProductsTable } from './warehouseProductService'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  try {
    deepStrictEqual(actual, expected)
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error)
    throw new Error(`${message}。${detail}`)
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const originalFetch = globalThis.fetch
let capturedBody: Record<string, unknown> | undefined

globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
  capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>

  return new Response(JSON.stringify({ success: true, data: [], total: 0 }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    supplierCode: 'SUP-001',
    productType: 2,
    isActive: false,
    filters: {
      productName: ['收纳箱'],
      localSupplierCode: ['LS-001'],
      volume: ['gte:1.5', 'lte:9.9'],
      createdAt: ['gte:2026-06-01', 'lte:2026-06-15'],
      domesticSupplierCode: ['SHOULD-BE-OVERRIDDEN'],
      productType: ['1'],
      isActive: ['true'],
    },
  })
  assert(capturedBody, '应捕获列头过滤查询请求体')
  assertDeepEqual(
    capturedBody.Filters,
    {
      productName: ['收纳箱'],
      localSupplierCode: ['LS-001'],
      volume: ['gte:1.5', 'lte:9.9'],
      createdAt: ['gte:2026-06-01', 'lte:2026-06-15'],
      domesticSupplierCode: ['SUP-001'],
      productType: ['2'],
      isActive: ['false'],
    },
    '列头过滤应合并进 Filters，顶部筛选字段应覆盖同名列头过滤',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    filters: {
      productType: ['1', '2'],
      isActive: ['true'],
    },
  })
  assert(capturedBody, '应捕获纯枚举列头过滤查询请求体')
  assertDeepEqual(
    capturedBody.Filters,
    {
      productType: ['1', '2'],
      isActive: ['true'],
    },
    '无顶部筛选时，枚举列头过滤应按原值进入 Filters',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    filters: {
      productName: [' ', ''],
      barcode: [],
      volume: ['gte:1'],
    },
  })
  assert(capturedBody, '应捕获空列头过滤清理请求体')
  assertDeepEqual(
    capturedBody.Filters,
    {
      volume: ['gte:1'],
    },
    '空数组和空白字符串应在发送前被清理，只保留有效列头过滤',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: 'all',
  })
  assert(capturedBody, '应捕获全部商品查询请求体')
  assertDeepEqual(
    capturedBody.Filters,
    undefined,
    '无有效筛选条件时不应发送空 Filters',
  )
  assertDeepEqual(
    capturedBody.CategoryGuids,
    undefined,
    'ALL 查询不应附加具体分类过滤条件',
  )
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    false,
    'ALL 查询不应启用未分类过滤',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: 'uncategorized',
  })
  assert(capturedBody, '应捕获空分类查询请求体')
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    true,
    '空分类查询应通过 UncategorizedOnly 传给表格接口',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryGuid: 'cat-guid-1',
    filters: {
      productName: ['分类商品'],
    },
  })
  assert(capturedBody, '应捕获具体分类查询请求体')
  assertDeepEqual(
    capturedBody.CategoryGuids,
    ['cat-guid-1'],
    '具体分类查询应通过 CategoryGuids 传给表格接口',
  )
  assertDeepEqual(
    capturedBody.IncludeSubCategories,
    true,
    '具体分类查询应默认包含子分类',
  )
  assertDeepEqual(
    capturedBody.Filters,
    { productName: ['分类商品'] },
    '分类查询仍应把普通列头过滤留在 Filters',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryGuid: 'cat-guid-1',
    uncategorizedOnly: true,
  })
  assert(capturedBody, '应捕获分类优先级查询请求体')
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    false,
    '具体分类和未分类同时存在时应以具体分类优先',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: 'uncategorized',
    filters: {
      productName: ['未分类商品'],
      updatedAt: ['gte:2026-06-10', 'lte:2026-06-16'],
    },
  })
  assert(capturedBody, '应捕获未分类列头过滤请求体')
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    true,
    '未分类筛选仍应通过顶层 UncategorizedOnly 传递',
  )
  assertDeepEqual(
    capturedBody.CategoryGuids,
    undefined,
    '未分类筛选不应混入 CategoryGuids',
  )
  assertDeepEqual(
    capturedBody.Filters,
    {
      productName: ['未分类商品'],
      updatedAt: ['gte:2026-06-10', 'lte:2026-06-16'],
    },
    '未分类场景下普通列头过滤仍应保留在 Filters',
  )

  globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
    capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>

    return new Response(JSON.stringify({
      success: true,
      data: [
        {
          ProductCode: 'P001',
          ProductName: '分类商品',
          ItemNumber: 'HB-001',
          CategoryName: '桐草工艺2',
          WarehouseCategoryGUID: 'category-guid-1',
          CategoryFullPath: '家居 / 工艺品 / 桐草工艺2',
          LocalSupplierCode: '200',
          LocalSupplierName: 'DATS',
          SupplierCode: 'CN-001',
          SupplierName: '国内供应商一',
        },
        {
          ProductCode: 'P002',
          ProductName: '兼容商品',
          ItemNumber: 'HB-002',
          categoryName: '收纳',
          productCategoryGUID: 'category-guid-2',
          categoryPath: '家居 / 收纳',
          localSupplier: {
            localSupplierCode: 'COS',
            name: 'Costco AU',
          },
          supplierCode: 'CN-002',
          supplierName: '国内供应商二',
        },
      ],
      total: 2,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const result = await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
  })

  assertDeepEqual(
    result.items.map((item) => ({
      categoryName: item.categoryName,
      warehouseCategoryGUID: item.warehouseCategoryGUID,
      categoryPath: item.categoryPath,
      domesticSupplierCode: item.domesticSupplierCode,
      domesticSupplierName: item.domesticSupplierName,
      localSupplierCode: item.localSupplierCode,
      localSupplierName: item.localSupplierName,
    })),
    [
      {
        categoryName: '桐草工艺2',
        warehouseCategoryGUID: 'category-guid-1',
        categoryPath: '家居 / 工艺品 / 桐草工艺2',
        domesticSupplierCode: 'CN-001',
        domesticSupplierName: '国内供应商一',
        localSupplierCode: '200',
        localSupplierName: 'DATS',
      },
      {
        categoryName: '收纳',
        warehouseCategoryGUID: 'category-guid-2',
        categoryPath: '家居 / 收纳',
        domesticSupplierCode: 'CN-002',
        domesticSupplierName: '国内供应商二',
        localSupplierCode: 'COS',
        localSupplierName: 'Costco AU',
      },
    ],
    '仓库商品列表应保留分类与供应商字段，并兼容澳洲供应商大小写和嵌套字段',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseProductService.categoryFilter.test: ok')
