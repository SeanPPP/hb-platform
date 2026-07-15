import {
  queryContainerAllocationSales,
  queryContainerAllocationSalesBranches,
} from './containerAllocationSalesService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

async function assertRejects(execute: () => Promise<unknown>, message: string) {
  try {
    await execute()
  } catch (error) {
    assertEqual(error instanceof Error ? error.message : String(error), message, '失败响应应透传后端消息')
    return
  }
  throw new Error('失败响应应拒绝 Promise')
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init
    return new Response(JSON.stringify({ success: true, data: { items: [], total: 0 } }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await queryContainerAllocationSales('container/guid', {
    startDate: '2026-06-01',
    endDate: '2026-06-28',
    search: 'ABC',
    pageNumber: 2,
    pageSize: 50,
    sortBy: 'salesAmount',
    sortDirection: 'desc',
  })
  assertEqual(capturedUrl, '/api/react/v1/containers/container%2Fguid/allocation-sales/query', '主查询应编码货柜 GUID 并调用正确接口')
  assertEqual(capturedInit?.method, 'POST', '主查询应使用 POST')
  assertDeepEqual(JSON.parse(String(capturedInit?.body)), {
    startDate: '2026-06-01',
    endDate: '2026-06-28',
    search: 'ABC',
    pageNumber: 2,
    pageSize: 50,
    sortBy: 'salesAmount',
    sortDirection: 'desc',
  }, '主查询应原样映射筛选、分页和排序请求')

  await queryContainerAllocationSalesBranches('GUID-1', {
    productCode: 'P-1',
    startDate: '2026-06-01',
    endDate: '2026-06-28',
  })
  assertEqual(capturedUrl, '/api/react/v1/containers/GUID-1/allocation-sales/branches/query', '分店查询应调用正确接口')
  assertDeepEqual(JSON.parse(String(capturedInit?.body)), {
    productCode: 'P-1',
    startDate: '2026-06-01',
    endDate: '2026-06-28',
  }, '分店查询应发送商品和日期范围')

  globalThis.fetch = (async () => new Response(JSON.stringify({ success: false, message: '统计未就绪' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch
  await assertRejects(() => queryContainerAllocationSales('GUID-1', {}), '统计未就绪')
} finally {
  globalThis.fetch = originalFetch
}

console.log('containerAllocationSalesService.test: ok')
