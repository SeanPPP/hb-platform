import { getStoreOrderProductActivityHistory } from './storeOrderService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const originalFetch = globalThis.fetch

try {
  let capturedUrl = ''
  let capturedMethod = ''
  let capturedBody: unknown = null
  let capturedSignal: AbortSignal | null | undefined

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null
    capturedSignal = init?.signal ?? null

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              recordType: 'order',
              recordDate: '2026-08-01',
              orderGUID: 'G-1',
              orderNo: 'ORD-001',
              orderDate: '2026-08-01',
              outboundDate: '2026-08-03',
              flowStatus: 2,
              quantity: '12',
              allocQuantity: '10',
            },
            {
              recordType: 'sales',
              recordDate: '2026-08-02',
              salesQuantity: 5,
              averagePrice: 12.5,
            },
            {
              recordType: 'sales',
              recordDate: '2026-08-03',
              salesQuantity: 0,
              averagePrice: null,
            },
            {
              recordType: 'salesSubtotal',
              periodStartDate: '2026-08-01',
              periodEndDate: '2026-08-07',
              salesQuantity: 0,
              averagePrice: null,
            },
            {
              recordDate: '2026-08-04',
              orderGUID: 'G-4',
              quantity: 3,
              allocQuantity: 0,
            },
          ],
          total: '5',
          pageNumber: '2',
          pageSize: '30',
          lastArrivalDate: '2026-07-20',
          endDate: '2026-08-17',
          latestOrderQuantity: '12',
          latestAllocQuantity: '10',
          totalSalesQuantity: 42,
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const query = {
    storeCode: 'STORE-1',
    productCode: 'P-001',
    pageNumber: 2,
    pageSize: 30,
    recordType: 'all' as const,
  }

  const abortController = new AbortController()
  const result = await getStoreOrderProductActivityHistory(query, abortController.signal)

  assertEqual(capturedUrl, '/api/react/v1/store-order/product-activity-history', '活动历史 route')
  assertEqual(capturedMethod, 'POST', '活动历史 method')
  assertDeepEqual(capturedBody, query, '活动历史 payload')
  assertEqual(capturedSignal, abortController.signal, '活动历史必须透传 AbortSignal')
  assertEqual(result.items.length, 5, '活动历史行数')

  assertEqual(result.items[0].recordType, 'order', '第一行类型为订货')
  assertEqual(result.items[0].orderGUID, 'G-1', '第一行 orderGUID')
  assertEqual(result.items[0].orderNo, 'ORD-001', '第一行 orderNo')
  assertEqual(result.items[0].orderDate, '2026-08-01', '第一行订货日期')
  assertEqual(result.items[0].outboundDate, '2026-08-03', '第一行出库日期')
  assertEqual(result.items[0].flowStatus, 2, '第一行状态数字')
  assertEqual(result.items[0].quantity, 12, '订货量字符串转数字')
  assertEqual(result.items[0].allocQuantity, 10, '发货量字符串转数字')
  assertEqual(result.items[0].salesQuantity, undefined, '订货行销量缺失')

  assertEqual(result.items[1].recordType, 'sales', '第二行类型为销售')
  assertEqual(result.items[1].salesQuantity, 5, '第二行销量')
  assertEqual(result.items[1].averagePrice, 12.5, '第二行均价')
  assertEqual(result.items[1].quantity, undefined, '销售行订货量缺失')

  assertEqual(result.items[2].salesQuantity, 0, '销量 0 必须保留')
  assertEqual(result.items[2].averagePrice, null, '均价 null 必须保留')

  assertEqual(result.items[4].recordType, 'order', '缺失/未知类型归一为订货')
  assertEqual(result.items[4].quantity, 3, '未知类型行订货量')
  assertEqual(result.items[4].allocQuantity, 0, '未知类型行发货量 0')

  assertEqual(result.total, 5, 'total 字符串转数字')
  assertEqual(result.pageNumber, 2, 'pageNumber 字符串转数字')
  assertEqual(result.pageSize, 30, 'pageSize 字符串转数字')
  assertEqual(result.lastArrivalDate, '2026-07-20', '最近来货日')
  assertEqual(result.endDate, '2026-08-17', '截止日')
  assertEqual(result.latestOrderQuantity, 12, '最近订货量')
  assertEqual(result.latestAllocQuantity, 10, '最近发货量')
  assertEqual(result.totalSalesQuantity, 42, '来货后总销量')

  assertEqual(result.items[3].recordType, 'salesSubtotal', '小计行类型')
  assertEqual(result.items[3].periodStartDate, '2026-08-01', '小计区间开始日')
  assertEqual(result.items[3].periodEndDate, '2026-08-07', '小计区间结束日')
  assertEqual(result.items[3].salesQuantity, 0, '小计销量 0 必须保留')
  assertEqual(result.items[3].averagePrice, null, '小计均价 null 必须保留')
  assertEqual(result.items[3].quantity, undefined, '小计行订货量缺失')
  assertEqual(result.items[3].allocQuantity, undefined, '小计行发货量缺失')

  console.log('storeOrderService.activityHistory.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
