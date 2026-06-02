import {
  getStoreOrderDetail,
  getStoreOrderDetailFull,
  getStoreOrderDetailProductCodes,
  updateStoreOrderLine,
} from './storeOrderService'

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
  const controller = new AbortController()
  let capturedUrl = ''
  let capturedMethod = ''
  let capturedSignal: AbortSignal | null = null

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedSignal = (init?.signal as AbortSignal | null) ?? null

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          orderGUID: 'order-1',
          orderNo: 'SO-001',
          totalAmount: 100,
          totalQuantity: 8,
          totalImportAmount: 88,
          totalVolume: 12,
          itemsTotal: 35,
          items: [
            {
              detailGUID: 'detail-1',
              productCode: 'product-1',
              quantity: 3,
              price: 10,
              amount: 30,
              importPrice: 8,
              importAmount: 24,
              minOrderQuantity: 1,
              isActive: true,
            },
          ],
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

	  const result = await getStoreOrderDetail(
	    'order-1',
	    {
	      pageNumber: 2,
	      pageSize: 20,
	      keyword: 'ABC-123',
	      statFilter: 'orderedNotShipped',
	      sortBy: 'itemNumber',
	      sortDescending: true,
	    },
	    controller.signal,
	  )

	  assertEqual(
	    capturedUrl,
	    '/api/react/v1/store-order/detail/order-1?pageNumber=2&pageSize=20&keyword=ABC-123&statFilter=orderedNotShipped&sortBy=itemNumber&sortDescending=true',
	    '订货明细接口应通过 query 传递远程分页筛选排序参数',
	  )
  assertEqual(capturedMethod, 'GET', '订货明细接口应继续使用 GET 请求')
  assertEqual(capturedSignal, controller.signal, '订货明细接口应透传取消信号')
  assertDeepEqual(
    result,
    {
      orderGUID: 'order-1',
      orderNo: 'SO-001',
      totalAmount: 100,
      totalQuantity: 8,
      totalImportAmount: 88,
      totalVolume: 12,
      itemsTotal: 35,
      items: [
        {
          detailGUID: 'detail-1',
          productCode: 'product-1',
          quantity: 3,
          price: 10,
          amount: 30,
          importPrice: 8,
          importAmount: 24,
          minOrderQuantity: 1,
          isActive: true,
        },
      ],
    },
    '订货明细接口应保留服务端返回的当前页 items 与 itemsTotal',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  let capturedUrl = ''
  globalThis.fetch = (async (input: RequestInfo | URL) => {
    capturedUrl = String(input)
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          orderGUID: 'order-1',
          totalAmount: 0,
          totalQuantity: 0,
          totalImportAmount: 0,
          totalVolume: 0,
          items: [],
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await getStoreOrderDetail('order-1')
  assertEqual(capturedUrl, '/api/react/v1/store-order/detail/order-1/full', '旧调用默认应读取全量明细')

  await getStoreOrderDetailFull('order-2')
  assertEqual(capturedUrl, '/api/react/v1/store-order/detail/order-2/full', '全量明细接口应使用 /full 路径')
} finally {
  globalThis.fetch = originalFetch
}

try {
  let capturedUrl = ''
  globalThis.fetch = (async (input: RequestInfo | URL) => {
    capturedUrl = String(input)
    return new Response(
      JSON.stringify({
        success: true,
        data: ['P001', 'P002', 123, null],
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const productCodes = await getStoreOrderDetailProductCodes('order-1')
  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/detail/order-1/product-codes',
    '跨页去重应读取轻量商品编码接口',
  )
  assertDeepEqual(productCodes, ['P001', 'P002'], '商品编码接口应过滤非字符串值')
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
        data: null,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await updateStoreOrderLine({
    orderGUID: 'order-1',
    productCode: 'product-1',
    allocQuantity: 7,
    importPrice: 1.25,
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/line/update', '单行保存接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', '单行保存接口应继续使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      productCode: 'product-1',
      importPrice: 1.25,
      quantity: 7,
    },
    '单行保存应在 service 层把前端 allocQuantity 显式映射为后端 quantity 字段',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('storeOrderService.detail.test: ok')
