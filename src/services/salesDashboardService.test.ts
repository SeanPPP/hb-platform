import { getBestSellers } from './salesDashboardService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

let capturedUrl = ''
let capturedInit: RequestInit | undefined
const originalFetch = globalThis.fetch

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(
    JSON.stringify({
      success: true,
      data: {
        products: [
          {
            ProductCode: 'P001',
            ItemNumber: 'HB001',
            Barcode: '9340000000012',
            ProductName: 'Best Seller',
            Quantity: 12,
            SalesAmount: 34.5,
            Rank: 1,
            IsActive: true,
            MinOrderQuantity: 2,
            // 用不同于明细长度的聚合值，锁定前端优先消费后端返回的销售分店数。
            BranchSalesCount: 5,
            BranchSales: [
              { BranchCode: 'S2', BranchName: 'Store 2', Quantity: 8 },
              { BranchCode: 'S1', BranchName: 'Store 1', Quantity: 4 },
            ],
          },
        ],
        total: 1,
        pageIndex: 2,
        pageSize: 100,
        totalPages: 1,
      },
    }),
    {
      status: 200,
      headers: { 'content-type': 'application/json' },
    },
  )
}) as typeof fetch

try {
  const controller = new AbortController()
  const result = await getBestSellers('2026-06-01', '2026-06-08', ['S1', 'S2'], 2, 100, controller.signal)
  const requestUrl = new URL(capturedUrl, 'http://localhost')

  assertEqual(requestUrl.pathname, '/api/react/v1/dashboard/best-sellers', '热销商品接口路径应保持不变')
  assertEqual(requestUrl.searchParams.get('startDate'), '2026-06-01', '应传递开始日期')
  assertEqual(requestUrl.searchParams.get('endDate'), '2026-06-08', '应传递结束日期')
  assertEqual(requestUrl.searchParams.get('pageIndex'), '2', '应传递页码')
  assertEqual(requestUrl.searchParams.get('pageSize'), '100', '应传递分页大小')
  assertEqual(requestUrl.searchParams.getAll('branchCodes').join(','), 'S1,S2', '应按重复参数传递分店')
  assertEqual(capturedInit?.method, 'GET', '热销商品接口应保持 GET 请求')
  assertEqual(capturedInit?.signal, controller.signal, '热销商品接口应透传 AbortSignal')
  assert(Array.isArray(result.products), '热销商品响应应继续解包 products')
  assertEqual(result.products[0]?.barcode, '9340000000012', '热销商品应接收条码字段')
  assertEqual(result.products[0]?.isActive, true, '热销商品应接收上下架字段')
  assertEqual(result.products[0]?.minOrderQuantity, 2, '热销商品应接收最小起订量')
  assertEqual(result.products[0]?.branchSalesCount, 5, '热销商品应接收销售分店数量')
  assertEqual(result.products[0]?.branchSales?.length, 2, '热销商品应继续保留分店销量明细列表')
  assertEqual(result.products[0]?.branchSales?.[0]?.branchCode, 'S2', '热销商品应接收分店销量明细')
  assertEqual(result.pageIndex, 2, '热销商品响应应继续解包 pageIndex')

  console.log('salesDashboardService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
