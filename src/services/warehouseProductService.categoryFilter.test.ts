import { getWarehouseProductsTable } from './warehouseProductService'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
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
    categoryFilter: 'all',
  })
  assert(capturedBody, '应捕获全部商品查询请求体')
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
