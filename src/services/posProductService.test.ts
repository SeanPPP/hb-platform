import {
  createHqProductFullSyncJob,
  createHqProductIncrementalSyncJob,
  getHqProductSyncJob,
  pushProductsToHq,
  syncProductsFromHqFull,
} from './posProductService'
import { RequestError } from '../utils/request'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

async function assertRequestError(
  execute: () => Promise<unknown>,
  expectedMessage: string,
  expectedPayload: unknown,
  label: string,
) {
  try {
    await execute()
  } catch (error) {
    assert(error instanceof RequestError, `${label} 应抛出 RequestError`)
    assertEqual(error.message, expectedMessage, `${label} 应保留后端错误消息`)
    assertEqual(error.status, 200, `${label} 业务失败应保留 HTTP 200 状态`)
    assertDeepEqual(error.payload, expectedPayload, `${label} 应保留完整 payload`)
    return
  }

  throw new Error(`${label} 应拒绝 Promise`)
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined
let nextPayload: unknown = {}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(JSON.stringify(nextPayload), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  nextPayload = {
    success: true,
    data: {
      jobId: 'job-full-1',
      status: 'queued',
      mode: 'Full',
    },
  }

  const fullJob = await createHqProductFullSyncJob({ operationId: 'op-full-1' })
  assertEqual(capturedUrl, '/api/react/v1/sync/products/jobs', '全量商品 HQ job 应调用后台任务接口')
  assertEqual(capturedInit?.method, 'POST', '全量商品 HQ job 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { operationId: 'op-full-1' },
    '全量商品 HQ job 请求应携带 operationId',
  )
  assertEqual(fullJob.status, 'Queued', 'queued 应归一为 Queued')

  nextPayload = {
    success: true,
    data: {
      jobId: 'job-incremental-1',
      status: 'running',
      mode: 'Incremental',
    },
  }

  const incrementalJob = await createHqProductIncrementalSyncJob({
    operationId: 'op-incremental-1',
    startDate: '2026-05-01',
  })
  assertEqual(
    capturedUrl,
    '/api/react/v1/sync/products-incremental/jobs',
    '增量商品 HQ job 应调用后台任务接口',
  )
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { operationId: 'op-incremental-1', startDate: '2026-05-01' },
    '增量商品 HQ job 请求应携带 operationId 和 startDate',
  )
  assertEqual(incrementalJob.status, 'Running', 'running 应归一为 Running')

  nextPayload = {
    success: true,
    data: {
      jobId: 'job-success-1',
      success: true,
      result: {
        productsAdded: 1,
        productsUpdated: 2,
      },
    },
  }

  const succeededJob = await getHqProductSyncJob('job-success-1')
  assertEqual(
    capturedUrl,
    '/api/react/v1/sync/products/jobs/job-success-1',
    '查询商品 HQ job 应调用 job 查询接口',
  )
  assertEqual(succeededJob.status, 'Succeeded', 'success:true 应归一为 Succeeded')
  assertEqual(succeededJob.result?.productsAdded, 1, '查询商品 HQ job 应保留 result 中的同步计数')
  assertEqual(succeededJob.result?.productsUpdated, 2, '查询商品 HQ job 应保留 result 中的更新计数')

  nextPayload = {
    success: true,
    data: {
      jobId: 'job-top-level-counts',
      status: 'Succeeded',
      addedCount: 3,
      updatedCount: 4,
      deletedCount: 5,
    },
  }

  const topLevelCountsJob = await getHqProductSyncJob('job-top-level-counts')
  assertEqual(topLevelCountsJob.productsAdded, 3, '查询商品 HQ job 应把顶层 addedCount 归一为 productsAdded')
  assertEqual(topLevelCountsJob.productsUpdated, 4, '查询商品 HQ job 应把顶层 updatedCount 归一为 productsUpdated')
  assertEqual(topLevelCountsJob.productsDeleted, 5, '查询商品 HQ job 应把顶层 deletedCount 归一为 productsDeleted')

  nextPayload = {
    success: true,
    data: {
      jobId: 'job-failed-1',
      success: false,
      message: '同步失败',
    },
  }

  const failedJob = await getHqProductSyncJob('job-failed-1')
  assertEqual(failedJob.status, 'Failed', 'success:false 应归一为 Failed')

  const unknownStatusPayload = {
    success: true,
    data: {
      jobId: 'job-unknown-1',
      status: 'paused',
    },
  }
  nextPayload = unknownStatusPayload

  await assertRequestError(
    () => getHqProductSyncJob('job-unknown-1'),
    '未知同步任务状态: paused',
    unknownStatusPayload.data,
    '未知 job status',
  )

  const fullSyncFailurePayload = {
    success: false,
    message: 'HQ 商品同步失败',
    data: {
      productsAdded: 0,
      errors: ['后端业务失败'],
    },
  }
  nextPayload = fullSyncFailurePayload

  await assertRequestError(
    () => syncProductsFromHqFull(),
    'HQ 商品同步失败',
    fullSyncFailurePayload,
    '同步接口 success:false',
  )

  nextPayload = {
    success: true,
    data: {
      successCount: 2,
      failedCount: 0,
      totalCount: 2,
      affectedRowCount: 36,
      productsAdded: 1,
      productsUpdated: 2,
      storeRetailPricesCreated: 3,
      storeRetailPricesUpdated: 4,
      productSetCodesCreated: 5,
      productSetCodesUpdated: 6,
      storeMultiCodesCreated: 7,
      storeMultiCodesUpdated: 8,
      errors: [],
    },
  }

  const pushResult = await pushProductsToHq({ productCodes: ['HB001', 'HB002'] })
  assertEqual(capturedUrl, '/api/react/v1/products/push-to-hq', '选中商品发送 HQ 应调用固定接口')
  assertEqual(capturedInit?.method, 'POST', '选中商品发送 HQ 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { productCodes: ['HB001', 'HB002'] },
    '选中商品发送 HQ 请求应只携带商品编码',
  )
  assertEqual(pushResult.successCount, 2, '发送 HQ 应使用后端返回的商品成功数')
  assertEqual(pushResult.failedCount, 0, '发送 HQ 无错误明细时失败数应为 0')
  assertEqual(pushResult.totalCount, 2, '发送 HQ 应使用后端返回的商品合计数')
  assertEqual(pushResult.affectedRowCount, 36, '发送 HQ 应保留商品、分店价格和多码影响记录数')

  const jobFailurePayload = {
    isSuccess: false,
    message: '创建任务失败',
    data: {
      reason: 'duplicate operationId',
    },
  }
  nextPayload = jobFailurePayload

  await assertRequestError(
    () => createHqProductFullSyncJob({ operationId: 'op-full-1' }),
    '创建任务失败',
    jobFailurePayload,
    'job 接口 isSuccess:false',
  )
} finally {
  globalThis.fetch = originalFetch
}
