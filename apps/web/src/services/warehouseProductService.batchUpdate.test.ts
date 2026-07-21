import { batchUpdateWarehouseProducts } from './warehouseProductService'
import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedMethod: string | undefined
let capturedBody: Record<string, unknown> | undefined
const serviceSource = readFileSync(path.resolve(process.cwd(), 'src/services/warehouseProductService.ts'), 'utf8')

assert(
  serviceSource.includes('MinOrderQuantity?: number') &&
    serviceSource.includes('PackingQuantity?: number'),
  '仓库商品批量更新类型应声明 MinOrderQuantity 和 PackingQuantity',
)

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedMethod = init?.method
  capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>

  return new Response(JSON.stringify({ success: true, data: { success: true, successCount: 1 } }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await batchUpdateWarehouseProducts([
    {
      ProductCode: 'P001',
      MinOrderQuantity: 0,
      PackingQuantity: 0,
      IsActive: false,
      DomesticPrice: undefined,
    },
  ], { syncStorePurchasePrice: false })

  assert(capturedBody, '应捕获仓库商品批量更新请求体')
  assert(capturedUrl.endsWith('/api/react/v1/product-warehouse/batch-update'), '批量更新应调用仓库商品 batch-update 接口')
  assert(capturedMethod === 'POST', '批量更新应使用 POST 方法')
  assertDeepEqual(
    capturedBody,
    {
      Items: [
        {
          ProductCode: 'P001',
          MinOrderQuantity: 0,
          PackingQuantity: 0,
          IsActive: false,
        },
      ],
      SyncStorePurchasePrice: false,
    },
    '批量更新请求体应保留数量零值和 false，并忽略 undefined 字段',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseProductService.batchUpdate.test: ok')
