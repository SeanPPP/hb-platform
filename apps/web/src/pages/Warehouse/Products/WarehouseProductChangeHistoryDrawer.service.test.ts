import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const serviceSource = readFileSync(
  path.resolve(process.cwd(), 'src/services/warehouseProductService.ts'),
  'utf8',
)

assert(
  serviceSource.includes('getWarehouseProductChangeHistory'),
  '仓库商品服务必须导出修改记录 GET API',
)
assert(
  serviceSource.includes("/change-history") && serviceSource.includes('encodeURIComponent(productCode)'),
  '修改记录 API 必须编码 ProductCode 并调用 change-history 路径',
)
assert(
  serviceSource.includes('pageNumber') && serviceSource.includes('pageSize'),
  '修改记录 API 必须透传 pageNumber 和 pageSize',
)
assert(
  serviceSource.includes('signal'),
  '修改记录 API 必须允许 Drawer 传入 AbortSignal',
)

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedMethod: string | undefined
let capturedSignal: AbortSignal | null | undefined

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedMethod = init?.method
  capturedSignal = init?.signal
  return new Response(JSON.stringify({
    success: true,
    data: {
      productCode: 'HB 001',
      itemNumber: 'MQ001',
      productName: 'Demo',
      pageNumber: 2,
      pageSize: 10,
      total: 1,
      events: [],
    },
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}) as typeof fetch

try {
  const service = await import('../../../services/warehouseProductService') as unknown as {
    getWarehouseProductChangeHistory?: (
      productCode: string,
      query: { pageNumber: number; pageSize: number },
      options?: { signal?: AbortSignal },
    ) => Promise<{ productCode: string; pageNumber: number; pageSize: number }>
  }
  assert(typeof service.getWarehouseProductChangeHistory === 'function', '修改记录 API 必须是可调用函数')
  const controller = new AbortController()
  const result = await service.getWarehouseProductChangeHistory(
    'HB 001',
    { pageNumber: 2, pageSize: 10 },
    { signal: controller.signal },
  )
  assert(capturedMethod === 'GET', '修改记录 API 必须使用 GET')
  assert(capturedSignal === controller.signal, '修改记录 API 必须透传 AbortSignal')
  assert(
    capturedUrl.endsWith('/api/react/v1/product-warehouse/HB%20001/change-history?pageNumber=2&pageSize=10'),
    `修改记录 API URL 不正确: ${capturedUrl}`,
  )
  assert(result.productCode === 'HB 001', '修改记录 API 应返回解包后的 data')
} finally {
  globalThis.fetch = originalFetch
}

console.log('WarehouseProductChangeHistoryDrawer.service.test: ok')
