import { getStoreOrderImportPriceVariance } from './storeOrderService'

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

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              orderGUID: 'order-1',
              detailGUID: 'detail-1',
              orderNo: 'SO-001',
              orderImportPrice: 3.5,
              firstContainerImportPrice: 2.5,
              allocQuantity: 10,
              originalImportAmount: 35,
              baselineImportAmount: 25,
              varianceAmount: 10,
            },
          ],
          total: '1',
          pageNumber: '2',
          pageSize: '20',
          summary: {
            totalRows: '1',
            originalImportAmountTotal: '35',
            baselineImportAmountTotal: '25',
            varianceAmountTotal: '10',
          },
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const query = {
    keyword: 'HB013',
    storeCode: '1042',
    orderNo: 'SO-001',
    startDate: '2026-06-01',
    endDate: '2026-06-23',
    varianceDirection: 'increase' as const,
    pageNumber: 2,
    pageSize: 20,
    sortBy: 'absoluteVarianceAmount',
    sortDescending: true,
  }
  const result = await getStoreOrderImportPriceVariance(query)

  assertEqual(capturedUrl, '/api/react/v1/store-order/import-price-variance', '首次货柜价差异接口路径应保持一致')
  assertEqual(capturedMethod, 'POST', '首次货柜价差异接口应使用 POST')
  assertDeepEqual(capturedBody, query, '首次货柜价差异查询应按后端契约原样提交')
  assertEqual(result.items.length, 1, '应保留后端返回的明细列表')
  assertEqual(result.total, 1, 'total 应归一化为数字')
  assertEqual(result.page, 2, 'pageNumber 应归一化为 page')
  assertEqual(result.pageSize, 20, 'pageSize 应归一化为数字')
  assertDeepEqual(
    result.summary,
    {
      totalRows: 1,
      originalImportAmountTotal: 35,
      baselineImportAmountTotal: 25,
      varianceAmountTotal: 10,
    },
    'summary 数字字段应归一化',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  globalThis.fetch = (async () =>
    new Response(
      JSON.stringify({
        success: true,
        data: {
          items: null,
          total: undefined,
          summary: null,
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )) as typeof fetch

  const result = await getStoreOrderImportPriceVariance({
    pageNumber: 3,
    pageSize: 50,
  })

  assertDeepEqual(result.items, [], 'items 非数组时应归一化为空列表')
  assertEqual(result.total, 0, '缺失 total 时应归一化为 0')
  assertEqual(result.page, 3, '缺失页码时应回退查询 pageNumber')
  assertEqual(result.pageSize, 50, '缺失 pageSize 时应回退查询 pageSize')
  assertDeepEqual(result.summary, {
    totalRows: 0,
    originalImportAmountTotal: 0,
    baselineImportAmountTotal: 0,
    varianceAmountTotal: 0,
  }, '缺失 summary 时应归一化为 0 汇总')
} finally {
  globalThis.fetch = originalFetch
}

console.log('storeOrderService.importPriceVariance.test: ok')
