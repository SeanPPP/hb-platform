import {
  buildContainerCreateProductsOperationId,
  createContainerProductCreationJob,
  getContainerProductCreationJob,
} from './containerProductCreationService'
import { RequestError } from '../utils/request'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
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

async function assertRejectsRequestError(execute: () => Promise<unknown>, expectedMessage: string) {
  try {
    await execute()
  } catch (error) {
    assert(error instanceof RequestError, '业务失败应抛出 RequestError')
    assert(error.message.includes(expectedMessage), `错误信息应包含 ${expectedMessage}`)
    return
  }

  throw new Error('预期请求失败，但实际成功')
}

async function main() {
  const failures: string[] = []

  const operationIdFailure = await runTest('货柜创建新商品 operationId 应由货柜和明细稳定生成', () => {
    assertEqual(
      buildContainerCreateProductsOperationId('container-1', ['detail-b', 'detail-a']),
      buildContainerCreateProductsOperationId('container-1', ['detail-a', 'detail-b']),
      '相同明细集合应生成同一个 operationId',
    )
    assertEqual(
      buildContainerCreateProductsOperationId('container-1', []),
      'container-create-products:container-1:empty',
      '空明细应使用 empty 哨兵，避免空字符串歧义',
    )
  })
  if (operationIdFailure) failures.push(operationIdFailure)

  const createJobFailure = await runTest('货柜创建新商品应创建后台 job 并携带 operationId 和明细 GUID', async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'container-product-job-1',
          status: 'Queued',
          operationId: 'op-1',
          result: {
            createdCount: 0,
            skippedCount: 0,
            failedCount: 0,
          },
        },
      },
      () =>
        createContainerProductCreationJob({
          operationId: 'op-1',
          containerGuid: 'container-1',
          detailHguids: ['detail-1', 'detail-2'],
        }),
    )

    assertDeepEqual(
      {
        url: captured.capturedUrl,
        method: captured.capturedMethod,
        body: captured.capturedBody,
        job: captured.result,
      },
      {
        url: '/api/react/v1/container-products/create-new-products/jobs',
        method: 'POST',
        body: {
          operationId: 'op-1',
          containerGuid: 'container-1',
          detailHguids: ['detail-1', 'detail-2'],
        },
        job: {
          jobId: 'container-product-job-1',
          status: 'Queued',
          operationId: 'op-1',
          result: {
            createdCount: 0,
            skippedCount: 0,
            failedCount: 0,
            created: [],
            skipped: [],
            errors: [],
          },
        },
      },
      '创建 job 请求和归一化结果不符合预期',
    )
  })
  if (createJobFailure) failures.push(createJobFailure)

  const queryJobFailure = await runTest('货柜创建新商品 job 查询应归一化顶层统计和错误数组', async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'container-product-job-2',
          status: 'Succeeded',
          createdCount: 2,
          skippedCount: 1,
          failedCount: 1,
          errors: [{ productCode: 'P-3', reasonCode: 'PRICE_INVALID', message: '价格异常' }],
        },
      },
      () => getContainerProductCreationJob('container-product-job-2'),
    )

    assertEqual(
      captured.capturedUrl,
      '/api/react/v1/container-products/create-new-products/jobs/container-product-job-2',
      '查询 job 应命中新接口',
    )
    assertDeepEqual(
      captured.result.result,
      {
        createdCount: 2,
        skippedCount: 1,
        failedCount: 1,
        created: [],
        skipped: [],
        errors: [{ productCode: 'P-3', reasonCode: 'PRICE_INVALID', message: '价格异常' }],
      },
      '查询 job 应归一化顶层统计',
    )
  })
  if (queryJobFailure) failures.push(queryJobFailure)

  const pascalCaseResultFailure = await runTest('货柜创建新商品 job 应兼容 PascalCase 失败结果', async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          JobId: 'container-product-job-pascal',
          Status: 'Succeeded',
          Result: {
            CreatedCount: 1,
            SkippedCount: 0,
            FailedCount: 1,
            Created: [{ productCode: 'P-1' }],
            Errors: [{ productCode: 'P-2', reasonCode: 'DUPLICATE_CODE', message: '商品已存在' }],
          },
        },
      },
      () => getContainerProductCreationJob('container-product-job-pascal'),
    )

    assertEqual(captured.result.jobId, 'container-product-job-pascal', 'PascalCase JobId 应被识别')
    assertEqual(captured.result.status, 'Succeeded', 'PascalCase Status 应被识别')
    assertDeepEqual(
      captured.result.result,
      {
        createdCount: 1,
        skippedCount: 0,
        failedCount: 1,
        created: [{ productCode: 'P-1' }],
        skipped: [],
        errors: [{ productCode: 'P-2', reasonCode: 'DUPLICATE_CODE', message: '商品已存在' }],
      },
      'PascalCase 失败结果应归一化到统一字段，避免页面误报纯成功',
    )
  })
  if (pascalCaseResultFailure) failures.push(pascalCaseResultFailure)

  const missingStatusFailure = await runTest('创建 job 响应缺少 status 时不得归一为成功', async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: 'container-product-job-missing-status',
          operationId: 'op-missing-status',
        },
      },
      () =>
        createContainerProductCreationJob({
          operationId: 'op-missing-status',
          containerGuid: 'container-1',
          detailHguids: ['detail-1'],
        }),
    )

    assertEqual(captured.result.status, 'Queued', '缺少 status 的创建响应应按待轮询 job 处理')
  })
  if (missingStatusFailure) failures.push(missingStatusFailure)

  const missingJobIdFailure = await runTest('创建 job 响应缺少 jobId 应抛出业务错误', async () => {
    await assertRejectsRequestError(
      () =>
        captureFetch(
          {
            success: true,
            data: {
              status: 'Queued',
            },
          },
          () =>
            createContainerProductCreationJob({
              operationId: 'op-missing-job',
              containerGuid: 'container-1',
              detailHguids: ['detail-1'],
            }),
        ).then(({ result }) => result),
      '创建新商品 job 缺少 jobId',
    )
  })
  if (missingJobIdFailure) failures.push(missingJobIdFailure)

  const businessFailure = await runTest('货柜创建新商品 job 接口 success false 应抛出业务错误', async () => {
    await assertRejectsRequestError(
      () =>
        captureFetch(
          {
            success: false,
            message: '创建新商品 job 失败',
          },
          () =>
            createContainerProductCreationJob({
              operationId: 'op-failed',
              containerGuid: 'container-1',
              detailHguids: ['detail-1'],
            }),
        ).then(({ result }) => result),
      '创建新商品 job 失败',
    )
  })
  if (businessFailure) failures.push(businessFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
