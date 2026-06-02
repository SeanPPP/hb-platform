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
    (capturedBody.Filters as Record<string, unknown>).warehouseCategoryGUID,
    undefined,
    'ALL 查询不应附加分类过滤条件',
  )

  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: 'uncategorized',
  })
  assert(capturedBody, '应捕获空分类查询请求体')
  assertDeepEqual(
    (capturedBody.Filters as Record<string, unknown>).warehouseCategoryGUID,
    [''],
    '空分类查询应通过 warehouseCategoryGUID 空值过滤传给表格接口',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseProductService.categoryFilter.test: ok')
