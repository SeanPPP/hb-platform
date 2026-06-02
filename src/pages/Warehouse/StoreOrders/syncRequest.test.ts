import {
  createStoreOrderFullHqSyncJob,
  createStoreOrderIncrementalHqSyncJob,
  createStoreOrderSyncJob,
  getStoreOrderHqSyncJob,
  getStoreOrderSyncJob,
  syncMissingStoreOrders,
} from '../../../services/storeOrderService'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function captureSyncBody(payload?: Parameters<typeof syncMissingStoreOrders>[0]) {
  const originalFetch = globalThis.fetch
  let capturedBody: unknown
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(_input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : undefined

    return new Response(
      JSON.stringify({
        success: true,
        ordersSynced: 0,
        detailsSynced: 0,
        ordersUpdated: 0,
        detailsUpdated: 0,
        message: 'ok',
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  try {
    await syncMissingStoreOrders(payload)
    return { body: capturedBody, url: capturedUrl, method: capturedMethod }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureSyncBody({ storeCodes: ['S001', 'S002'] }),
  {
    body: { storeCodes: ['S001', 'S002'] },
    url: '/api/react/v1/store-order/sync-missing-orders',
    method: 'POST',
  },
  '同步订单应该发送全部已选分店',
)

assertDeepEqual(
  await captureSyncBody(),
  {
    body: {},
    url: '/api/react/v1/store-order/sync-missing-orders',
    method: 'POST',
  },
  '未选择分店时不应偷偷发送第一个分店',
)

assertDeepEqual(
  await captureSyncBody({ storeCode: 'S001' }),
  {
    body: { storeCodes: ['S001'] },
    url: '/api/react/v1/store-order/sync-missing-orders',
    method: 'POST',
  },
  '旧 storeCode 参数应该归一为 storeCodes 数组',
)

async function captureCreateJobRequest(payload?: Parameters<typeof createStoreOrderSyncJob>[0]) {
  const originalFetch = globalThis.fetch
  let capturedBody: unknown
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : undefined

    return new Response(
      JSON.stringify({
        success: true,
        message: '同步任务已提交',
        data: {
          jobId: 'job-001',
          status: 'Queued',
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  try {
    const result = await createStoreOrderSyncJob(payload)
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureCreateJobRequest({ storeCodes: ['S001', 'S002', 'S001'] }),
  {
    body: { storeCodes: ['S001', 'S002'] },
    url: '/api/react/v1/store-order/sync-missing-orders/jobs',
    method: 'POST',
    result: {
      jobId: 'job-001',
      status: 'Queued',
      message: '同步任务已提交',
      success: true,
    },
  },
  '创建同步任务应命中新 job 接口并保留提交反馈',
)

async function captureFullHqJobRequest() {
  const originalFetch = globalThis.fetch
  let capturedBody: unknown
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : undefined

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: 'job-full',
          status: 'Running',
          mode: 'Full',
        },
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )
  }) as typeof fetch

  try {
    const result = await createStoreOrderFullHqSyncJob()
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureFullHqJobRequest(),
  {
    body: {},
    url: '/api/react/v1/store-order/hq-sync/full/jobs',
    method: 'POST',
    result: {
      jobId: 'job-full',
      status: 'Running',
      mode: 'Full',
      success: true,
    },
  },
  '全量同步任务不应携带当前分店筛选',
)

async function captureIncrementalHqJobRequest(
  payload?: Parameters<typeof createStoreOrderIncrementalHqSyncJob>[0],
) {
  const originalFetch = globalThis.fetch
  let capturedBody: unknown
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : undefined

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: 'job-inc',
          status: 'Running',
          mode: 'Incremental',
        },
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )
  }) as typeof fetch

  try {
    const result = await createStoreOrderIncrementalHqSyncJob(payload)
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureIncrementalHqJobRequest({
    storeCodes: ['S001', 'S002', 'S001'],
    startDate: '2026-05-01T00:00:00.000Z',
    endDate: '2026-06-01T00:00:00.000Z',
  }),
  {
    body: {
      storeCodes: ['S001', 'S002'],
      startDate: '2026-05-01T00:00:00.000Z',
      endDate: '2026-06-01T00:00:00.000Z',
    },
    url: '/api/react/v1/store-order/hq-sync/incremental/jobs',
    method: 'POST',
    result: {
      jobId: 'job-inc',
      status: 'Running',
      mode: 'Incremental',
      success: true,
    },
  },
  '增量同步任务应发送日期范围和去重后的分店集合',
)

async function captureGetJobRequest(jobId: string) {
  const originalFetch = globalThis.fetch
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)

    return new Response(
      JSON.stringify({
        success: true,
        message: '同步完成',
        data: {
          jobId,
          status: 'Succeeded',
          result: {
            success: true,
            message: '同步完成：新增订单 3 条、详情 9 条；更新订单 1 条、详情 2 条',
            ordersSynced: 3,
            detailsSynced: 9,
            ordersUpdated: 1,
            detailsUpdated: 2,
          },
        },
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  try {
    const result = await getStoreOrderSyncJob(jobId)
    return { url: capturedUrl, method: capturedMethod, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureGetJobRequest('job-002'),
  {
    url: '/api/react/v1/store-order/sync-missing-orders/jobs/job-002',
    method: 'GET',
    result: {
      jobId: 'job-002',
      status: 'Succeeded',
      message: '同步完成：新增订单 3 条、详情 9 条；更新订单 1 条、详情 2 条',
      success: true,
      ordersSynced: 3,
      detailsSynced: 9,
      ordersUpdated: 1,
      detailsUpdated: 2,
    },
  },
  '轮询同步任务应命中 job 状态接口并展开结果',
)

async function captureGetHqJobRequest(jobId: string) {
  const originalFetch = globalThis.fetch
  let capturedUrl = ''
  let capturedMethod = ''

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)

    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId,
          status: 'Succeeded',
          mode: 'Full',
          result: {
            success: true,
            message: '全量同步完成',
            ordersSynced: 1,
            detailsSynced: 2,
            ordersSoftDeleted: 3,
            detailsSoftDeleted: 4,
          },
        },
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )
  }) as typeof fetch

  try {
    const result = await getStoreOrderHqSyncJob(jobId)
    return { url: capturedUrl, method: capturedMethod, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

assertDeepEqual(
  await captureGetHqJobRequest('job-hq'),
  {
    url: '/api/react/v1/store-order/hq-sync/jobs/job-hq',
    method: 'GET',
    result: {
      jobId: 'job-hq',
      status: 'Succeeded',
      mode: 'Full',
      message: '全量同步完成',
      success: true,
      ordersSynced: 1,
      detailsSynced: 2,
      ordersSoftDeleted: 3,
      detailsSoftDeleted: 4,
    },
  },
  'HQ 同步任务应命中新状态接口并展开软删统计',
)
