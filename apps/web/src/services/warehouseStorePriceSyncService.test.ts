import {
  createWarehouseStorePriceSyncJob,
  createWarehouseStorePriceSyncJobPoller,
  getAllWarehouseProductCount,
  getWarehouseStorePriceSyncJob,
  getWarehouseStorePriceSyncTargetStores,
  HqProductSyncPollingCancelledError,
  WAREHOUSE_STORE_PRICE_SYNC_POLL_TIMEOUT_MS,
} from './warehouseStorePriceSyncService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const originalFetch = globalThis.fetch
const requests: Array<{ url: string; method: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  const method = String(init?.method ?? 'GET')
  const body = init?.body ? JSON.parse(String(init.body)) : undefined
  requests.push({ url, method, body })

  if (url.endsWith('/target-stores')) {
    return new Response(JSON.stringify({
      success: true,
      data: [
        { storeCode: 'S01', storeName: 'Sydney' },
        { storeCode: 'S02', storeName: 'Brisbane' },
      ],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  if (url.endsWith('/jobs') && method === 'POST') {
    return new Response(JSON.stringify({
      success: true,
      data: { jobId: 'job-1', status: 'Pending', isDuplicateRequest: false },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  if (url.endsWith('/jobs/job-1')) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        jobId: 'job-1',
        status: 'PartiallySucceeded',
        result: {
          errors: [
            'legacy error text',
            {
              stage: 'Local',
              productCode: 'P002',
              storeCode: 'S01',
              code: 'PRICE_UPDATE_FAILED',
              message: '本地价格更新失败',
            },
          ],
        },
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  if (url.endsWith('/jobs/job-unknown')) {
    return new Response(JSON.stringify({
      success: true,
      data: { jobId: 'job-unknown', status: 'Mystery' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  if (url.endsWith('/product-count')) {
    return new Response(JSON.stringify({ success: true, data: 42 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }
  throw new Error(`Unexpected request: ${method} ${url}`)
}) as typeof fetch

try {
  const stores = await getWarehouseStorePriceSyncTargetStores()
  assertDeepEqual(stores, [
    { storeCode: 'S01', storeName: 'Sydney' },
    { storeCode: 'S02', storeName: 'Brisbane' },
  ], '目标分店接口应解包并保留多选代码和名称')

  const created = await createWarehouseStorePriceSyncJob({
    productCodes: ['P001', 'P002'],
    applyToAllProducts: false,
    targetStoreCodes: ['S01', 'S02'],
    syncToHq: false,
  })
  assert(created.jobId === 'job-1' && created.status === 'Pending', '创建接口应返回后台 job')

  const completed = await getWarehouseStorePriceSyncJob('job-1')
  assert(completed.status === 'PartiallySucceeded', '查询接口应保留 PartiallySucceeded 终态')
  assertDeepEqual(completed.result?.errors, [
    { message: 'legacy error text' },
    {
      stage: 'Local',
      productCode: 'P002',
      storeCode: 'S01',
      code: 'PRICE_UPDATE_FAILED',
      message: '本地价格更新失败',
    },
  ], 'service 应兼容字符串和结构化 errors，并保留商品/分店上下文')

  const count = await getAllWarehouseProductCount()
  assert(count === 42, '全量模式应通过专用接口取得与执行范围一致的商品总数')

  let unknownStatusRejected = false
  try {
    await getWarehouseStorePriceSyncJob('job-unknown')
  } catch (error) {
    unknownStatusRejected = error instanceof Error && error.message.includes('Mystery')
  }
  assert(unknownStatusRejected, '未知服务端状态应中止本次轮询响应，不能伪装成 Failed 终态')

  assertDeepEqual(requests.slice(0, 3).map(({ url, method, body }) => ({ url, method, body })), [
    { url: '/api/react/v1/product-warehouse/store-price-sync/target-stores', method: 'GET', body: undefined },
    {
      url: '/api/react/v1/product-warehouse/store-price-sync/jobs',
      method: 'POST',
      body: {
        productCodes: ['P001', 'P002'],
        applyToAllProducts: false,
        targetStoreCodes: ['S01', 'S02'],
        syncToHq: false,
      },
    },
    { url: '/api/react/v1/product-warehouse/store-price-sync/jobs/job-1', method: 'GET', body: undefined },
  ], 'service 应调用固定 target-stores/jobs/job 端点并保持 payload 范围')
  assert(requests.some(({ url }) => url.endsWith('/api/react/v1/product-warehouse/store-price-sync/product-count')), '全量总数应调用专用 product-count 接口')

  const scheduledDelays: number[] = []
  const poller = createWarehouseStorePriceSyncJobPoller({
    jobId: 'job-1',
    getJob: async () => ({ jobId: 'job-1', status: 'Pending' as const }),
    setTimeoutFn: (_callback, delay) => {
      scheduledDelays.push(delay)
      return scheduledDelays.length
    },
    clearTimeoutFn: () => undefined,
  })
  assert(scheduledDelays.includes(WAREHOUSE_STORE_PRICE_SYNC_POLL_TIMEOUT_MS), '分店价格轮询应使用约 35 分钟的独立默认超时')
  poller.stop()
  await poller.promise.catch((error) => assert(error instanceof HqProductSyncPollingCancelledError, '停止轮询应只取消前端轮询，不改变服务端任务状态'))

  console.log('warehouseStorePriceSyncService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
