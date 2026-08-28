import { getBestSellers, getCompactSalesBoard } from './salesDashboardService'

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
  const requestUrl = new URL(capturedUrl, 'http://localhost')

  if (requestUrl.pathname === '/api/react/v1/dashboard/compact-sales-board') {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          Stores: [{ BranchCode: 'S1', BranchName: 'Store 1', TotalAmount: 100, TotalQuantity: 10, DomesticSupplierAmount: 70 }],
          ChinaSuppliers: [{ SupplierCode: 'SUP-CN', SupplierName: '国内供应商', TotalAmount: 70, TotalQuantity: 7 }],
          ProductDetails: {
            Data: [{ ProductCode: 'P001', ItemNumber: 'HB001', ProductName: '国内商品', ChinaSupplierCode: 'SUP-CN', TotalQuantity: 7, UnitPrice: 10, TotalAmount: 70 }],
            Total: 1,
            PageIndex: 1,
            PageSize: 80,
          },
          StatisticStatus: 'Fresh',
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

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
            TotalCost: 12.5,
            GrossProfit: 22,
            GrossMarginRate: 0.637681,
            CostSource: 'StoreRetailPrice',
            Rank: 1,
            IsActive: true,
            MinOrderQuantity: 2,
            // 用不同于明细长度的聚合值，锁定前端优先消费后端返回的销售分店数。
            BranchSalesCount: 5,
            StatisticStatus: 'Fresh',
            BranchSales: [
              { BranchCode: 'S2', BranchName: 'Store 2', Quantity: 8, SalesAmount: 24, GrossProfit: 16, GrossMarginRate: 0.666667 },
              { BranchCode: 'S1', BranchName: 'Store 1', Quantity: 4, SalesAmount: 10.5, GrossProfit: 6, GrossMarginRate: 0.571429 },
            ],
          },
        ],
        total: 1,
        pageIndex: 2,
        pageSize: 100,
        totalPages: 1,
        StatisticStatus: 'Fresh',
        StatisticMessage: 'Ready',
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
  assertEqual(result.products[0]?.totalCost, 12.5, '热销商品应接收成本金额')
  assertEqual(result.products[0]?.grossProfit, 22, '热销商品应接收毛利额')
  assertEqual(result.products[0]?.grossMarginRate, 0.637681, '热销商品应接收毛利率')
  assertEqual(result.products[0]?.costSource, 'StoreRetailPrice', '热销商品应接收成本来源')
  assertEqual(result.products[0]?.statisticStatus, 'Fresh', '热销商品应接收商品统计状态')
  assertEqual(result.products[0]?.branchSalesCount, 5, '热销商品应接收销售分店数量')
  assertEqual(result.products[0]?.branchSales?.length, 2, '热销商品应继续保留分店销量明细列表')
  assertEqual(result.products[0]?.branchSales?.[0]?.branchCode, 'S2', '热销商品应接收分店销量明细')
  assertEqual(result.products[0]?.branchSales?.[0]?.salesAmount, 24, '热销商品应接收分店销售额')
  assertEqual(result.products[0]?.branchSales?.[0]?.grossProfit, 16, '热销商品应接收分店毛利额')
  assertEqual(result.products[0]?.branchSales?.[0]?.grossMarginRate, 0.666667, '热销商品应接收分店毛利率')
  assertEqual(result.statisticStatus, 'Fresh', '热销商品响应应接收统计状态')
  assertEqual(result.statisticMessage, 'Ready', '热销商品响应应接收统计提示')
  assertEqual(result.pageIndex, 2, '热销商品响应应继续解包 pageIndex')

  const board = await getCompactSalesBoard(
    { startDate: '2026-06-17', endDate: '2026-06-17' },
    ['S1'],
    ['SUP-CN'],
    'P001',
    1,
    80,
    controller.signal,
    true,
  )
  const boardRequestUrl = new URL(capturedUrl, 'http://localhost')
  assertEqual(boardRequestUrl.pathname, '/api/react/v1/dashboard/compact-sales-board', '销售看板应请求独立接口')
  assertEqual(boardRequestUrl.searchParams.get('startDate'), '2026-06-17', '销售看板应传递开始日期')
  assertEqual(boardRequestUrl.searchParams.get('endDate'), '2026-06-17', '销售看板应传递结束日期')
  assertEqual(boardRequestUrl.searchParams.getAll('branchCodes').join(','), 'S1', '销售看板应传递分店筛选')
  assertEqual(boardRequestUrl.searchParams.getAll('chinaSupplierCodes').join(','), 'SUP-CN', '销售看板应传递国内供应商筛选')
  assertEqual(boardRequestUrl.searchParams.get('productCode'), 'P001', '销售看板应传递商品联动筛选')
  assertEqual(boardRequestUrl.searchParams.get('forceRefresh'), 'true', '销售看板手动刷新应传递强制刷新标记')
  assertEqual(capturedInit?.method, 'GET', '销售看板接口应使用 GET 请求')
  assertEqual(capturedInit?.signal, controller.signal, '销售看板接口应继续透传 AbortSignal')
  assertEqual(board.stores[0]?.domesticSupplierAmount, 70, '销售看板应归一化分店国内供应商金额')
  assertEqual(board.chinaSuppliers[0]?.supplierCode, 'SUP-CN', '销售看板应归一化国内供应商')
  assertEqual(board.productDetails.data[0]?.itemNumber, 'HB001', '销售看板应归一化商品货号')
  assertEqual(board.productDetails.data[0]?.unitPrice, 10, '销售看板应归一化商品单价')

  console.log('salesDashboardService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
