import {
  batchUpdateStoreOrderLines,
  batchMapStoreOrderStoreCode,
  createStoreOrderPasteReplaceJob,
  getUnmatchedStoreOrderGroups,
  getStoreOrderDetail,
  getStoreOrderDetailFull,
  getStoreOrderDetailProductCodes,
  getStoreOrderProducts,
  getStoreOrderInvoiceEmailJob,
  getStoreOrderPasteReplaceJob,
  refreshStoreOrderImportPrices,
  sendStoreOrderInvoiceEmail,
  translateStoreOrderInvoiceEmailText,
  updateStoreOrderStatus,
  updateStoreOrderStoreContact,
  updateStoreOrderOutboundDate,
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
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)

    return new Response(
      JSON.stringify({
        success: true,
        data: [
          {
            sourceStoreCode: '11111111-1111-1111-1111-111111111111',
            sourceStoreName: 'Ada - Tas - Kingston',
            orderCount: 3,
            latestOrderDate: '2026-06-15T00:00:00',
          },
        ],
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await getUnmatchedStoreOrderGroups()

  assertEqual(capturedUrl, '/api/react/v1/store-order/unmatched-store-groups', '未匹配分店聚合接口路径应保持一致')
  assertEqual(capturedMethod, 'GET', '未匹配分店聚合接口应使用 GET')
  assertDeepEqual(
    result,
    [
      {
        sourceStoreCode: '11111111-1111-1111-1111-111111111111',
        sourceStoreName: 'Ada - Tas - Kingston',
        orderCount: 3,
        latestOrderDate: '2026-06-15T00:00:00',
      },
    ],
    '未匹配分店聚合接口应保留后端分组数据',
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
          updatedCount: 2,
          unchangedCount: 1,
          skippedCount: 1,
          missingWarehousePriceCount: 1,
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await refreshStoreOrderImportPrices({
    orderGUID: 'order-1',
    detailGUIDs: ['detail-1', 'detail-2'],
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/line/refresh-import-prices', '更新进货价接口路径应保持一致')
  assertEqual(capturedMethod, 'POST', '更新进货价接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      detailGUIDs: ['detail-1', 'detail-2'],
    },
    '有选中行时应把明细 GUID 列表传给后端',
  )
  assertDeepEqual(
    result,
    {
      updatedCount: 2,
      unchangedCount: 1,
      skippedCount: 1,
      missingWarehousePriceCount: 1,
    },
    '更新进货价结果应归一化统计字段',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  let capturedBody: unknown = null

  globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null

    return new Response(
      JSON.stringify({
        success: true,
        data: {},
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await refreshStoreOrderImportPrices({
    orderGUID: 'order-1',
  })

  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
    },
    '没有选中行时应只发送订单 GUID，让后端按整单刷新',
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
          updatedCount: 2,
          skippedCount: 1,
          items: [
            {
              sourceStoreCode: '11111111-1111-1111-1111-111111111111',
              targetStoreCode: '1042',
              updatedCount: 2,
              skippedCount: 1,
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

  const result = await batchMapStoreOrderStoreCode({
    mappings: [
      {
        sourceStoreCode: '11111111-1111-1111-1111-111111111111',
        targetStoreCode: '1042',
      },
    ],
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/batch-map-store-code', '批量修复分店 GUID 接口路径应保持一致')
  assertEqual(capturedMethod, 'POST', '批量修复分店 GUID 接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      mappings: [
        {
          sourceStoreCode: '11111111-1111-1111-1111-111111111111',
          targetStoreCode: '1042',
        },
      ],
    },
    '批量修复分店 GUID 应原样发送映射关系',
  )
  assertDeepEqual(
    result,
    {
      updatedCount: 2,
      skippedCount: 1,
      items: [
        {
          sourceStoreCode: '11111111-1111-1111-1111-111111111111',
          targetStoreCode: '1042',
          updatedCount: 2,
          skippedCount: 1,
        },
      ],
    },
    '批量修复分店 GUID 应归一化后端结果',
  )
} finally {
  globalThis.fetch = originalFetch
}

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
          outboundDate: '2026-06-07T00:00:00',
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
      outboundDate: '2026-06-07T00:00:00',
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
  const controller = new AbortController()
  let capturedUrl = ''
  let capturedMethod = ''
  let capturedSignal: AbortSignal | null = null
  let capturedBody: unknown = null

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedSignal = (init?.signal as AbortSignal | null) ?? null
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              productCode: 'product-1',
              itemNumber: 'HB137-001',
              productName: '测试商品',
            },
          ],
          total: 1,
          pageNumber: 1,
          pageSize: 100,
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await getStoreOrderProducts(
    {
      itemNumber: 'HB137-',
      supplierCode: 'CN001',
      excludeOrderGUID: 'order-1',
      pageNumber: 1,
      pageSize: 100,
      sortBy: 'Default',
    },
    controller.signal,
  )

  assertEqual(capturedUrl, '/api/react/v1/store-order/products', '商品选择查询接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', '商品选择查询接口应使用 POST')
  assertEqual(capturedSignal, controller.signal, '商品选择查询接口应透传取消信号')
  assertDeepEqual(
    capturedBody,
    {
      itemNumber: 'HB137-',
      supplierCode: 'CN001',
      excludeOrderGUID: 'order-1',
      pageNumber: 1,
      pageSize: 100,
      sortBy: 'Default',
    },
    '商品选择查询接口应发送原始查询条件',
  )
  assertDeepEqual(
    result,
    {
      items: [
        {
          productCode: 'product-1',
          itemNumber: 'HB137-001',
          productName: '测试商品',
        },
      ],
      total: 1,
      page: 1,
      pageSize: 100,
    },
    '商品选择查询接口应归一化分页响应',
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
          jobId: 'paste-job-1',
          status: 'Queued',
          message: 'Excel 粘贴导入任务已提交',
          orderGUID: 'order-1',
          targetField: 'quantity',
          totalCount: 3,
          importedCount: 2,
          skippedCount: 1,
          createdAt: '2026-06-11T00:00:00Z',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await createStoreOrderPasteReplaceJob({
    orderGUID: 'order-1',
    targetField: 'quantity',
    items: [
      {
        productCode: 'P001',
        quantity: 2,
        action: 'replace',
      },
    ],
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/line/paste-replace/jobs', 'Excel 粘贴导入 job 创建接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', 'Excel 粘贴导入 job 创建接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      targetField: 'quantity',
      items: [
        {
          productCode: 'P001',
          quantity: 2,
          action: 'replace',
        },
      ],
    },
    'Excel 粘贴导入 job 创建接口应发送原始 payload',
  )
  assertDeepEqual(
    result,
    {
      jobId: 'paste-job-1',
      status: 'Queued',
      message: 'Excel 粘贴导入任务已提交',
      orderGUID: 'order-1',
      targetField: 'quantity',
      totalCount: 3,
      importedCount: 2,
      skippedCount: 1,
      createdAt: '2026-06-11T00:00:00Z',
      completedAt: undefined,
    },
    'Excel 粘贴导入 job 创建接口应归一化响应',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  globalThis.fetch = (async () =>
    new Response(
      JSON.stringify({
        success: false,
        message: 'Excel 粘贴导入任务创建失败',
        data: null,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )) as typeof fetch

  const result = await createStoreOrderPasteReplaceJob({
    orderGUID: 'order-1',
    targetField: 'allocQuantity',
    items: [],
  })

  assertDeepEqual(
    result,
    {
      jobId: '',
      status: 'Failed',
      message: 'Excel 粘贴导入任务创建失败',
      orderGUID: undefined,
      targetField: undefined,
      totalCount: undefined,
      importedCount: undefined,
      skippedCount: undefined,
      createdAt: undefined,
      completedAt: undefined,
    },
    'Excel 粘贴导入 job 外层失败响应应归一化为 Failed 并保留 message',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: 'paste/job with spaces',
          status: 'Succeeded',
          message: 'Excel 粘贴导入完成',
          orderGUID: 'order-1',
          targetField: 'allocQuantity',
          totalCount: 4,
          importedCount: 3,
          skippedCount: 1,
          completedAt: '2026-06-11T00:01:00Z',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await getStoreOrderPasteReplaceJob('paste/job with spaces')

  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/line/paste-replace/jobs/paste%2Fjob%20with%20spaces',
    'Excel 粘贴导入 job 查询接口应编码 jobId',
  )
  assertEqual(capturedMethod, 'GET', 'Excel 粘贴导入 job 查询接口应使用 GET')
  assertDeepEqual(
    result,
    {
      jobId: 'paste/job with spaces',
      status: 'Succeeded',
      message: 'Excel 粘贴导入完成',
      orderGUID: 'order-1',
      targetField: 'allocQuantity',
      totalCount: 4,
      importedCount: 3,
      skippedCount: 1,
      createdAt: undefined,
      completedAt: '2026-06-11T00:01:00Z',
    },
    'Excel 粘贴导入 job 查询接口应归一化完成结果',
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
        data: true,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await updateStoreOrderOutboundDate({
    orderGUID: 'order-1',
    outboundDate: '2026-06-07',
    completeOrder: true,
  })

  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/outbound-date',
    '出库日期接口路径应保持契约一致',
  )
  assertEqual(capturedMethod, 'POST', '出库日期接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      outboundDate: '2026-06-07',
      completeOrder: true,
      orderGuid: 'order-1',
    },
    '出库日期接口应发送订单、日期和是否完成订单',
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
    capturedBody = JSON.parse(String(init?.body))

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          subject: 'Custom subject',
          body: 'Custom body',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const translatedEmailText = await translateStoreOrderInvoiceEmailText({
    orderGUID: 'order-1',
    targetLanguage: 'en',
    subject: '自定义主题',
    body: '自定义正文',
  })

  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/invoice/email/translate-text',
    '发票邮件文本翻译接口路径应保持契约一致',
  )
  assertEqual(capturedMethod, 'POST', '发票邮件文本翻译接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      targetLanguage: 'en',
      subject: '自定义主题',
      body: '自定义正文',
    },
    '发票邮件文本翻译接口应发送目标语言和当前编辑内容',
  )
  assertDeepEqual(
    translatedEmailText,
    { subject: 'Custom subject', body: 'Custom body' },
    '发票邮件文本翻译接口应返回归一化结果',
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
    syncImportPrice: false,
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/line/update', '单行保存接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', '单行保存接口应继续使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      productCode: 'product-1',
      importPrice: 1.25,
      syncImportPrice: false,
      quantity: 7,
    },
    '单行保存应映射发货数字段，并保留进口价同步开关',
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
        data: null,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await batchUpdateStoreOrderLines({
    orderGUID: 'order-1',
    items: [
      {
        productCode: 'product-1',
        importPrice: 2.5,
        syncImportPrice: true,
      },
    ],
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/line/batch-update', '批量保存接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', '批量保存接口应继续使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      items: [
        {
          productCode: 'product-1',
          importPrice: 2.5,
          syncImportPrice: true,
        },
      ],
    },
    '批量保存应保留只改进口价和同步开关的 payload',
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
        data: null,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await updateStoreOrderStatus({
    orderGUID: 'order-1',
    newStatus: 3,
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/status', '详情页状态更改接口路径应保持不变')
  assertEqual(capturedMethod, 'POST', '详情页状态更改接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      newStatus: 3,
    },
    '详情页状态更改应保持后端兼容 payload',
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
        data: null,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  await updateStoreOrderStoreContact({
    orderGUID: 'order-1',
    storeCode: 'S001',
    address: '1 Test Street',
    contactEmail: 'store@example.com',
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/store-contact/update', '分店地址邮箱更新接口路径应保持契约一致')
  assertEqual(capturedMethod, 'POST', '分店地址邮箱更新接口应使用 POST')
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      storeCode: 'S001',
      address: '1 Test Street',
      contactEmail: 'store@example.com',
    },
    '分店地址邮箱更新应原样发送前后端约定 payload',
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
          jobId: 'job-1',
          status: 'Queued',
          message: '发票邮件发送任务已提交',
          orderGUID: 'order-1',
          toEmail: 'invoice@example.com',
          createdAt: '2026-06-05T00:00:00Z',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await sendStoreOrderInvoiceEmail({
    orderGUID: 'order-1',
    toEmail: 'invoice@example.com',
    subject: 'Store Order Invoice',
    body: 'Please check the attached invoice.',
  })

  assertEqual(capturedUrl, '/api/react/v1/store-order/invoice/email', '发票邮件接口路径应保持契约一致')
  assertEqual(capturedMethod, 'POST', '发票邮件接口应使用 POST')
  assertDeepEqual(
    result,
    {
      jobId: 'job-1',
      status: 'Queued',
      message: '发票邮件发送任务已提交',
      orderGUID: 'order-1',
      toEmail: 'invoice@example.com',
      createdAt: '2026-06-05T00:00:00Z',
      completedAt: undefined,
    },
    '发票邮件接口应返回归一化 job 状态',
  )
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: 'order-1',
      toEmail: 'invoice@example.com',
      subject: 'Store Order Invoice',
      body: 'Please check the attached invoice.',
    },
    '发票邮件接口应只发送确认信息，不上传前端附件',
  )
} finally {
  globalThis.fetch = originalFetch
}

try {
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: 'job/with spaces',
          status: 'Succeeded',
          message: '发票邮件发送成功',
          completedAt: '2026-06-05T00:01:00Z',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  const result = await getStoreOrderInvoiceEmailJob('job/with spaces')

  assertEqual(
    capturedUrl,
    '/api/react/v1/store-order/invoice/email/jobs/job%2Fwith%20spaces',
    '发票邮件 job 查询接口应编码 jobId',
  )
  assertEqual(capturedMethod, 'GET', '发票邮件 job 查询接口应使用 GET')
  assertDeepEqual(
    result,
    {
      jobId: 'job/with spaces',
      status: 'Succeeded',
      message: '发票邮件发送成功',
      orderGUID: undefined,
      toEmail: undefined,
      createdAt: undefined,
      completedAt: '2026-06-05T00:01:00Z',
    },
    '发票邮件 job 查询接口应归一化成功状态',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('storeOrderService.detail.test: ok')
