import { getStoreOrderImportPriceVariance, getStoreOrderImportPriceVarianceDetails } from './storeOrderService'

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
              productCode: 'P-001',
              itemNumber: 'HB013-001',
              productName: 'Test Product',
              productImage: 'product.jpg',
              supplierCode: 'CN1',
              supplierName: '供应商一',
              domesticPrice: 8.8,
              unitVolume: 0.25,
              packingQuantity: 12,
              firstContainerImportPrice: 2.5,
              allocQuantityTotal: 10,
              originalImportAmountTotal: 35,
              baselineImportAmountTotal: 25,
              varianceAmountTotal: 10,
              detailCount: 2,
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
    supplierCode: 'CN1',
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
  assertDeepEqual(capturedBody, query, '首次货柜价差异查询应按后端契约原样提交国内供应商过滤')
  assertEqual(result.items.length, 1, '应保留后端返回的商品汇总列表')
  assertEqual(result.items[0].supplierCode, 'CN1', '商品汇总行应包含国内供应商编码')
  assertEqual(result.items[0].domesticPrice, 8.8, '商品汇总行应包含国内价格')
  assertEqual(result.items[0].unitVolume, 0.25, '商品汇总行应包含体积')
  assertEqual(result.items[0].packingQuantity, 12, '商品汇总行应包含装箱数')
  assertEqual(result.items[0].detailCount, 2, '商品汇总行应包含明细数量')
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
              firstContainerCode: 'container-1',
            },
          ],
          total: '1',
          pageNumber: '1',
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
    productCode: 'P-001',
    supplierCode: 'CN1',
    varianceDirection: 'increase' as const,
    pageNumber: 1,
    pageSize: 20,
    sortBy: 'orderDate',
    sortDescending: true,
  }
  const result = await getStoreOrderImportPriceVarianceDetails(query)

  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/import-price-variance/details',
    '首次货柜价差异明细接口路径应指向 details',
  )
  assertEqual(capturedMethod, 'POST', '首次货柜价差异明细接口应使用 POST')
  assertDeepEqual(capturedBody, query, '明细查询应携带 productCode 和当前供应商过滤')
  assertEqual(result.items.length, 1, '明细接口应保留后端返回的订单明细列表')
  assertEqual(result.items[0].orderGUID, 'order-1', '明细行应包含订单 GUID')
  assertEqual(result.total, 1, '明细 total 应归一化为数字')
  assertEqual(result.page, 1, '明细 pageNumber 应归一化为 page')
  assertEqual(result.pageSize, 20, '明细 pageSize 应归一化为数字')
  assertEqual(result.summary.varianceAmountTotal, 10, '明细 summary 应归一化')
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
  assertDeepEqual(
    result.summary,
    {
      totalRows: 0,
      originalImportAmountTotal: 0,
      baselineImportAmountTotal: 0,
      varianceAmountTotal: 0,
    },
    '缺失 summary 时应归一化为 0 汇总',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('storeOrderService.importPriceVariance.test: ok')
