import {
  HqProductSyncPollingTimeoutError,
  buildProductHqSyncOperationId,
  buildPushProductsToHqOperationId,
  createProductHqSyncJobPoller,
  createProductHqSyncFullJob,
  createProductHqSyncIncrementalJob,
  createPushProductsToHqJob,
  getProductHqSyncJob,
  syncSelectedProductsFromHq,
  syncProductsFromHqFull,
  syncProductsFromHqIncremental,
} from './posProductService'
import { RequestError } from '../utils/request'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, label)
    return error
  }

  throw new Error(`${label}. Expected promise to reject`)
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

type TimeoutTask = {
  id: number
  execute: () => void
  delay: number
  dueAt: number
}

function createFakeTimer() {
  let sequence = 0
  let now = 0
  const tasks = new Map<number, TimeoutTask>()

  return {
    setTimeout: (callback: () => void, delay: number) => {
      const id = sequence + 1
      sequence = id
      tasks.set(id, { id, execute: callback, delay, dueAt: now + delay })
      return id
    },
    clearTimeout: (id: unknown) => {
      if (typeof id === 'number') {
        tasks.delete(id)
      }
    },
    flushNext: () => {
      const next = Array.from(tasks.values()).sort((left, right) => {
        if (left.dueAt !== right.dueAt) {
          return left.dueAt - right.dueAt
        }
        return left.id - right.id
      })[0]
      if (!next) {
        throw new Error('没有可执行的定时任务')
      }
      tasks.delete(next.id)
      now = next.dueAt
      next.execute()
      return next.delay
    },
    pendingCount: () => tasks.size,
  }
}

function jsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

async function waitForPendingTimer(timer: ReturnType<typeof createFakeTimer>) {
  for (let index = 0; index < 20; index += 1) {
    if (timer.pendingCount() > 0) {
      return
    }
    await Promise.resolve()
    await new Promise((resolve) => setTimeout(resolve, 0))
  }
}

async function captureFetch<T>(responseBody: unknown, execute: () => Promise<T>) {
  const originalFetch = globalThis.fetch
  let capturedUrl = ''
  let capturedMethod = ''
  let capturedBody: unknown

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = String(init?.method)
    capturedBody = init?.body ? JSON.parse(String(init.body)) : undefined

    return new Response(JSON.stringify(responseBody), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  try {
    const result = await execute()
    return { capturedUrl, capturedMethod, capturedBody, result }
  } finally {
    globalThis.fetch = originalFetch
  }
}

async function expectRejectsWithRequestError(execute: () => Promise<unknown>, expectedMessage: string) {
  try {
    await execute()
  } catch (error) {
    assert(error instanceof RequestError, '应抛出 RequestError')
    assert(error.message.includes(expectedMessage), `错误信息应包含 ${expectedMessage}`)
    return
  }

  throw new Error('预期请求失败，但实际成功')
}

async function main() {
  const failures: string[] = []

  const fullJobFailure = await runTest('全量商品 HQ 同步应创建后台 job 并携带 operationId', async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'product-job-full',
          status: 'Queued',
          mode: 'Full',
          operationId: 'product-hq-sync:full',
        },
      },
      () => createProductHqSyncFullJob({ operationId: 'product-hq-sync:full' }),
    )

    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result,
      },
      {
        url: '/api/react/v1/sync/products/jobs',
        method: 'POST',
        body: { operationId: 'product-hq-sync:full' },
        job: {
          jobId: 'product-job-full',
          status: 'Queued',
          mode: 'Full',
          operationId: 'product-hq-sync:full',
          errors: [],
        },
      },
      '全量商品同步 job 请求不符合预期',
    )
  })
  if (fullJobFailure) failures.push(fullJobFailure)

  const incrementalJobFailure = await runTest('增量商品 HQ 同步应创建后台 job 并携带日期与 operationId', async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'product-job-inc',
          status: 'Running',
          mode: 'Incremental',
          operationId: 'product-hq-sync:incremental:2026-02-21',
          startDate: '2026-02-21',
        },
      },
      () =>
        createProductHqSyncIncrementalJob({
          operationId: 'product-hq-sync:incremental:2026-02-21',
          startDate: '2026-02-21',
        }),
    )

    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result,
      },
      {
        url: '/api/react/v1/sync/products-incremental/jobs',
        method: 'POST',
        body: {
          operationId: 'product-hq-sync:incremental:2026-02-21',
          startDate: '2026-02-21',
        },
        job: {
          jobId: 'product-job-inc',
          status: 'Running',
          mode: 'Incremental',
          operationId: 'product-hq-sync:incremental:2026-02-21',
          startDate: '2026-02-21',
          errors: [],
        },
      },
      '增量商品同步 job 请求不符合预期',
    )
  })
  if (incrementalJobFailure) failures.push(incrementalJobFailure)

  const pushToHqFieldsFailure = await runTest('货柜发送 HQ job 应携带字段选择并让 operationId 区分字段', async () => {
    const operationId = buildPushProductsToHqOperationId(
      'container-1',
      ['P002', 'P001'],
      2,
      ['storeRetailPrice', 'inventoryImportPrice'],
    )
    assertEqual(
      operationId,
      'container-push-hq:container-1:P001,P002:2:inventoryImportPrice,storeRetailPrice',
      '字段选择应进入发送 HQ operationId',
    )

    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'push-job-1',
          status: 'Queued',
          operationId,
          result: { successCount: 0, failedCount: 0, totalCount: 0 },
        },
      },
      () => createPushProductsToHqJob({
        operationId,
        productCodes: ['P001'],
        updateFields: ['storeRetailPrice', 'inventoryImportPrice'],
        items: [
          {
            productCode: 'P001',
            isNewProduct: false,
            importPrice: 1.23,
            oemPrice: 4.56,
          },
        ],
      }),
    )

    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result,
      },
      {
        url: '/api/react/v1/products/push-to-hq/jobs',
        method: 'POST',
        body: {
          operationId,
          productCodes: ['P001'],
          updateFields: ['storeRetailPrice', 'inventoryImportPrice'],
          items: [
            {
              productCode: 'P001',
              isNewProduct: false,
              importPrice: 1.23,
              oemPrice: 4.56,
            },
          ],
        },
        job: {
          jobId: 'push-job-1',
          status: 'Queued',
          operationId,
          result: {
            successCount: 0,
            failedCount: 0,
            totalCount: 0,
            affectedRowCount: 0,
            errors: [],
          },
          errors: [],
        },
      },
      '发送 HQ job 请求应保留 updateFields',
    )
  })
  if (pushToHqFieldsFailure) failures.push(pushToHqFieldsFailure)

  const selectedProductsSyncFailure = await runTest('选中商品从 HQ 同步应调用选中商品接口并携带商品编码', async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          productsUpdated: 2,
          storeRetailPricesCreated: 3,
          storeMultiCodesCreated: 1,
          errors: [],
        },
      },
      () => syncSelectedProductsFromHq({ productCodes: ['EP112', 'EP194'] }),
    )

    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        syncResult: result.result,
      },
      {
        url: '/api/react/v1/products/sync-selected-from-hq',
        method: 'POST',
        body: { productCodes: ['EP112', 'EP194'] },
        syncResult: {
          productsUpdated: 2,
          storeRetailPricesCreated: 3,
          storeMultiCodesCreated: 1,
          errors: [],
          productsAdded: 0,
          productsDeleted: 0,
          productSetCodesCreated: 0,
          productSetCodesDeleted: 0,
          durationMs: 0,
        },
      },
      '选中商品 HQ 同步请求不符合预期',
    )
  })
  if (selectedProductsSyncFailure) failures.push(selectedProductsSyncFailure)

  const operationIdBuilderFailure = await runTest('商品 HQ 同步 operationId 应由服务层唯一生成', () => {
    assertEqual(
      buildProductHqSyncOperationId('full'),
      'product-hq-sync:full:all',
      '全量同步 operationId 应使用统一格式',
    )
    assertEqual(
      buildProductHqSyncOperationId('incremental', '2026-02-21'),
      'product-hq-sync:incremental:2026-02-21',
      '增量同步 operationId 应包含起始日期',
    )
    assertEqual(
      buildProductHqSyncOperationId('incremental'),
      'product-hq-sync:incremental:all',
      '缺省起始日期应使用 all，避免 latest/all 两套语义',
    )
  })
  if (operationIdBuilderFailure) failures.push(operationIdBuilderFailure)

  const sharedPollerSuccessFailure = await runTest('商品 HQ 同步共享轮询器应持续查询直到成功', async () => {
    const timer = createFakeTimer()
    const statuses = [
      { jobId: 'product-poller-job', status: 'Queued' as const },
      { jobId: 'product-poller-job', status: 'Running' as const },
      {
        jobId: 'product-poller-job',
        status: 'Succeeded' as const,
        result: { productsAdded: 3, productsUpdated: 5 },
      },
    ]
    const requestedJobIds: string[] = []

    const poller = createProductHqSyncJobPoller({
      jobId: 'product-poller-job',
      pollIntervalMs: 200,
      timeoutMs: 30000,
      getJob: async (jobId) => {
        requestedJobIds.push(jobId)
        return statuses.shift()!
      },
      setTimeoutFn: timer.setTimeout as typeof setTimeout,
      clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
    })

    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()
    await Promise.resolve()

    const result = await poller.promise
    assertDeepEqual(requestedJobIds, ['product-poller-job', 'product-poller-job', 'product-poller-job'], '轮询器应持续查询同一个 job')
    assertEqual(result.status, 'Succeeded', '共享轮询器应返回最终成功 job')
    assertEqual(result.result?.productsAdded, 3, '共享轮询器应透传最终结果')
  })
  if (sharedPollerSuccessFailure) failures.push(sharedPollerSuccessFailure)

  const sharedPollerTimeoutFailure = await runTest('商品 HQ 同步共享轮询器超时应抛出统一 timeout 错误', async () => {
    const timer = createFakeTimer()
    const poller = createProductHqSyncJobPoller({
      jobId: 'product-poller-timeout',
      pollIntervalMs: 200,
      timeoutMs: 200,
      getJob: async () => ({ jobId: 'product-poller-timeout', status: 'Running' }),
      setTimeoutFn: timer.setTimeout as typeof setTimeout,
      clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
    })

    timer.flushNext()

    const error = await assertRejects(
      () => poller.promise,
      '商品同步任务轮询超时',
      '共享轮询器 timeout 应进入明确错误路径',
    )
    assert(error instanceof HqProductSyncPollingTimeoutError, '共享轮询器 timeout 应使用专门错误类型')
  })
  if (sharedPollerTimeoutFailure) failures.push(sharedPollerTimeoutFailure)

  const statusFallbackFailure = await runTest('job 查询遇到 success true 且无 status 时应归一为 Succeeded', async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'product-job-ok',
          success: true,
          result: {
            productsAdded: 1,
            productsUpdated: 2,
            productsDeleted: 3,
          },
        },
      },
      () => getProductHqSyncJob('product-job-ok'),
    )

    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        job: result.result,
      },
      {
        url: '/api/react/v1/sync/products/jobs/product-job-ok',
        method: 'GET',
        job: {
          jobId: 'product-job-ok',
          status: 'Succeeded',
          success: true,
          result: {
            productsAdded: 1,
            productsUpdated: 2,
            productsDeleted: 3,
            productSetCodesCreated: 0,
            productSetCodesDeleted: 0,
            errors: [],
            durationMs: 0,
          },
          errors: [],
        },
      },
      'job success true 状态归一化不符合预期',
    )
  })
  if (statusFallbackFailure) failures.push(statusFallbackFailure)

  const unknownStatusFailure = await runTest('job 查询遇到未知 status 不应静默当成 Running', async () => {
    await expectRejectsWithRequestError(
      () =>
        captureFetch(
          {
            success: true,
            data: {
              jobId: 'product-job-weird',
              status: 'AlmostDone',
            },
          },
          () => getProductHqSyncJob('product-job-weird'),
        ),
      '未知同步任务状态',
    )
  })
  if (unknownStatusFailure) failures.push(unknownStatusFailure)

  const businessFailure = await runTest('旧商品 HQ 同步接口遇到 success false 应抛出后端消息', async () => {
    await expectRejectsWithRequestError(
      () =>
        captureFetch(
          {
            success: false,
            message: 'HQ 商品同步失败',
            data: { productsAdded: 0, errors: ['失败明细'] },
          },
          () => syncProductsFromHqFull(),
        ),
      'HQ 商品同步失败',
    )
  })
  if (businessFailure) failures.push(businessFailure)

  const duplicateSubmissionFailure = await runTest('连续点击确认只创建一次商品同步 job', async () => {
    const originalFetch = globalThis.fetch
    const timer = createFakeTimer()
    let postCount = 0

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)
        if (init?.method === 'POST' && url === '/api/react/v1/sync/products/jobs') {
          postCount += 1
          return jsonResponse({
            success: true,
            data: {
              jobId: 'product-job-once',
              status: 'Running',
              operationId: 'product-hq-sync:full:all',
            },
          })
        }
        if (url === '/api/react/v1/sync/products/jobs/product-job-once') {
          return jsonResponse({
            success: true,
            data: {
              jobId: 'product-job-once',
              status: 'Succeeded',
              productsAdded: 2,
            },
          })
        }
        throw new Error(`未预期的请求：${url}`)
      }) as typeof fetch

      const first = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1000,
        setTimeoutFn: timer.setTimeout as typeof setTimeout,
        clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
      })
      const second = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1000,
        setTimeoutFn: timer.setTimeout as typeof setTimeout,
        clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
      })

      await waitForPendingTimer(timer)
      timer.flushNext()
      await Promise.resolve()

      const [firstResult, secondResult] = await Promise.all([first, second])
      assertEqual(postCount, 1, '连续确认时只应创建一次 job')
      assertDeepEqual(firstResult, secondResult, '连续确认应共享同一个 active job 的结果')
      assertEqual(firstResult.productsAdded, 2, '最终结果应来自轮询完成的 job')
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (duplicateSubmissionFailure) failures.push(duplicateSubmissionFailure)

  const takeoverExistingJobFailure = await runTest('后端返回相同 operationId 的已有 job 时应接管轮询', async () => {
    const originalFetch = globalThis.fetch
    const timer = createFakeTimer()
    const requestedUrls: string[] = []

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)
        requestedUrls.push(url)
        if (init?.method === 'POST' && url === '/api/react/v1/sync/products-incremental/jobs') {
          return jsonResponse({
            success: true,
            data: {
              jobId: 'product-job-existing',
              status: 'Running',
              operationId: 'product-hq-sync:incremental:2026-05-20',
              message: '已有同步任务正在执行',
            },
          })
        }
        if (url === '/api/react/v1/sync/products/jobs/product-job-existing') {
          return jsonResponse({
            success: true,
            data: {
              jobId: 'product-job-existing',
              status: 'Succeeded',
              productsUpdated: 4,
            },
          })
        }
        throw new Error(`未预期的请求：${url}`)
      }) as typeof fetch

      const resultPromise = syncProductsFromHqIncremental(
        { startDate: '2026-05-20' },
        {
          pollIntervalMs: 100,
          timeoutMs: 1000,
          setTimeoutFn: timer.setTimeout as typeof setTimeout,
          clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
        },
      )
      await waitForPendingTimer(timer)
      timer.flushNext()
      await Promise.resolve()

      const result = await resultPromise
      assertDeepEqual(
        requestedUrls,
        ['/api/react/v1/sync/products-incremental/jobs', '/api/react/v1/sync/products/jobs/product-job-existing'],
        '服务层应创建增量 job 后接管返回的已有 job 轮询',
      )
      assertEqual(result.productsUpdated, 4, '接管轮询应返回已有 job 的完成统计')
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (takeoverExistingJobFailure) failures.push(takeoverExistingJobFailure)

  const failedStatusFailure = await runTest('轮询到 Failed 应抛出后端失败消息', async () => {
    const originalFetch = globalThis.fetch
    const timer = createFakeTimer()

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)
        if (init?.method === 'POST') {
          return jsonResponse({ success: true, data: { jobId: 'product-job-failed', status: 'Running' } })
        }
        if (url === '/api/react/v1/sync/products/jobs/product-job-failed') {
          return jsonResponse({
            success: true,
            data: { jobId: 'product-job-failed', status: 'Failed', message: '后端商品同步失败' },
          })
        }
        throw new Error(`未预期的请求：${url}`)
      }) as typeof fetch

      const resultPromise = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1000,
        setTimeoutFn: timer.setTimeout as typeof setTimeout,
        clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
      })
      await waitForPendingTimer(timer)
      timer.flushNext()
      await Promise.resolve()

      await assertRejects(
        () => resultPromise,
        '后端商品同步失败',
        'Failed 状态应进入明确错误路径',
      )
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (failedStatusFailure) failures.push(failedStatusFailure)

  const timeoutFailure = await runTest('轮询超时应抛出明确 timeout 错误', async () => {
    const originalFetch = globalThis.fetch
    const timer = createFakeTimer()

    try {
      globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method === 'POST') {
          return jsonResponse({ success: true, data: { jobId: 'product-job-timeout', status: 'Running' } })
        }
        return jsonResponse({ success: true, data: { jobId: 'product-job-timeout', status: 'Running' } })
      }) as typeof fetch

      const resultPromise = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 100,
        setTimeoutFn: timer.setTimeout as typeof setTimeout,
        clearTimeoutFn: timer.clearTimeout as typeof clearTimeout,
      })
      await waitForPendingTimer(timer)
      timer.flushNext()
      await Promise.resolve()

      const error = await assertRejects(
        () => resultPromise,
        '商品同步任务轮询超时',
        'timeout 应进入明确错误路径',
      )
      assert(error instanceof HqProductSyncPollingTimeoutError, 'timeout 应使用专门错误类型')
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (timeoutFailure) failures.push(timeoutFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('posProductService.hqSyncJob.test: ok')
}

await main()
