import {
  batchUpdateWarehouseProducts,
  createWarehouseProductBatchUpdateJob,
  createWarehouseProductBatchUpdateJobPoller,
  getWarehouseProductBatchUpdateJob,
  patchWarehouseProduct,
  updateWarehouseProductFull,
} from './warehouseProductService'
import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedMethod: string | undefined
let capturedBody: Record<string, unknown> | undefined
const serviceSource = readFileSync(path.resolve(process.cwd(), 'src/services/warehouseProductService.ts'), 'utf8')

assert(
  serviceSource.includes('MinOrderQuantity?: number') &&
    serviceSource.includes('PackingQuantity?: number'),
  '仓库商品批量更新类型应声明 MinOrderQuantity 和 PackingQuantity',
)

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedMethod = init?.method
  capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>

  return new Response(JSON.stringify({ success: true, data: { success: true, successCount: 1 } }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await batchUpdateWarehouseProducts([
    {
      ProductCode: 'P001',
      MinOrderQuantity: 0,
      PackingQuantity: 0,
      IsActive: false,
      DomesticPrice: undefined,
    },
  ], { syncStorePurchasePrice: false })

  assert(capturedBody, '应捕获仓库商品批量更新请求体')
  assert(capturedUrl.endsWith('/api/react/v1/product-warehouse/batch-update'), '批量更新应调用仓库商品 batch-update 接口')
  assert(capturedMethod === 'POST', '批量更新应使用 POST 方法')
  assertDeepEqual(
    capturedBody,
    {
      Items: [
        {
          ProductCode: 'P001',
          MinOrderQuantity: 0,
          PackingQuantity: 0,
          IsActive: false,
        },
      ],
      SyncStorePurchasePrice: false,
    },
    '批量更新请求体应保留数量零值和 false，并忽略 undefined 字段',
  )

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = init?.method
    capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>

    return new Response(JSON.stringify({
      success: true,
      successCount: 1,
      failedCount: 0,
      imageUpdatedCount: 1,
      hqImageSync: {
        requested: true,
        success: false,
        updatedCount: 0,
        failedCount: 1,
        errorCode: 'HQ_IMAGE_SYNC_ITEM_ERRORS',
        errors: ['HQ 商品不存在: P001'],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const imageResult = await batchUpdateWarehouseProducts(
    [{ ProductCode: 'P001' }],
    {
      generateImageUrls: true,
      imageBaseUrl: 'https://images.example.com/catalog/',
      syncImageToHq: true,
    },
  )

  assertDeepEqual(
    capturedBody,
    {
      Items: [{ ProductCode: 'P001' }],
      GenerateImageUrls: true,
      ImageBaseUrl: 'https://images.example.com/catalog/',
      SyncImageToHq: true,
    },
    '批量图片更新应发送图片生成和 HQ 同步选项',
  )
  assert(imageResult.imageUpdatedCount === 1, '应归一化本地图片更新数量')
  assert(imageResult.hqImageSync?.success === false, 'HQ 逐项失败不应由服务层抛错')
  assert(imageResult.hqImageSync?.failedCount === 1, '应归一化 HQ 失败数量')
  assertDeepEqual(
    imageResult.hqImageSync?.errors,
    ['HQ 商品不存在: P001'],
    '应保留 HQ 同步错误明细',
  )

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedMethod = init?.method
    capturedBody = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>
    const isStatusRequest = capturedMethod === 'GET'
    const data = isStatusRequest
      ? {
          jobId: 'batch-job-1',
          operationId: 'warehouse-product-batch-update:test',
          status: 'PartiallySucceeded',
          result: {
            success: true,
            successCount: 1,
            failedCount: 1,
            imageUpdatedCount: 1,
            hqImageSync: {
              requested: true,
              success: false,
              failedCount: 1,
              errors: ['HQ 商品不存在: P001'],
            },
          },
        }
      : {
          jobId: 'batch-job-1',
          operationId: 'warehouse-product-batch-update:test',
          status: 'Queued',
          createdAt: '2026-08-13T00:00:00Z',
        }

    return new Response(JSON.stringify({ success: true, data }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const createdJob = await createWarehouseProductBatchUpdateJob(
    [{ ProductCode: 'P001' }],
    {
      generateImageUrls: true,
      imageBaseUrl: 'https://images.example.com/catalog/',
      syncImageToHq: true,
    },
  )
  assert(capturedUrl.endsWith('/api/react/v1/product-warehouse/batch-update/jobs'), '后台批量修改应调用 jobs 创建接口')
  assert(capturedMethod === 'POST', '后台批量修改任务应使用 POST 创建')
  assert(createdJob.status === 'Queued', '创建任务应保留 Queued 状态')
  assertDeepEqual(
    capturedBody,
    {
      Items: [{ ProductCode: 'P001' }],
      GenerateImageUrls: true,
      ImageBaseUrl: 'https://images.example.com/catalog/',
      SyncImageToHq: true,
    },
    '后台任务请求应完整携带批量修改与图片同步选项',
  )

  const completedJob = await getWarehouseProductBatchUpdateJob('batch-job-1')
  assert(capturedUrl.endsWith('/api/react/v1/product-warehouse/batch-update/jobs/batch-job-1'), '应按 jobId 查询后台批量修改状态')
  assert(String(capturedMethod) === 'GET', '查询后台批量修改状态应使用 GET')
  assert(completedJob.status === 'PartiallySucceeded', '应保留 PartiallySucceeded 终态')
  assert(completedJob.result?.hqImageSync?.failedCount === 1, '应归一化后台任务中的 HQ 失败明细')

  const scheduledCallbacks: Array<() => void> = []
  const poller = createWarehouseProductBatchUpdateJobPoller({
    jobId: 'batch-job-1',
    getJob: async () => completedJob,
    setTimeoutFn: (callback) => {
      scheduledCallbacks.push(callback)
      return scheduledCallbacks.length
    },
    clearTimeoutFn: () => undefined,
  })
  assert(scheduledCallbacks.length === 2, '后台任务轮询应同时安排超时与首次状态查询')
  scheduledCallbacks[1]?.()
  const polledJob = await poller.promise
  assert(polledJob.status === 'PartiallySucceeded', 'PartiallySucceeded 应作为批量修改轮询终态')

  capturedMethod = undefined
  capturedBody = undefined
  await patchWarehouseProduct('HB 001', { oemPrice: 0 })

  assert(capturedBody, '应捕获仓库商品单字段更新请求体')
  assert(capturedUrl.endsWith('/api/react/v1/product-warehouse/HB%20001'), '单字段更新应编码商品货号并调用仓库商品根 PATCH 接口')
  assert(capturedMethod === 'PATCH', '单字段更新应使用 PATCH 方法')
  assertDeepEqual(
    capturedBody,
    { OEMPrice: 0 },
    '单字段更新应只发送一个 PascalCase 字段并保留零值',
  )

  capturedMethod = undefined
  capturedBody = undefined
  await updateWarehouseProductFull('P001', {
    minOrderQuantity: 0,
    isActive: true,
  })

  const fullUpdateBody = capturedBody as Record<string, unknown> | undefined
  assert(fullUpdateBody, '应捕获仓库商品完整更新请求体')
  assert(capturedMethod === 'PUT', '完整更新应继续使用 PUT 方法')
  assert(fullUpdateBody.MinOrderQuantity === 0, '编辑弹窗完整更新应发送 MinOrderQuantity 并保留零值')
  assert(!('MiddlePackQuantity' in fullUpdateBody), '编辑弹窗不应再把中包数发送为 Product.MiddlePackageQuantity 对应字段')
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseProductService.batchUpdate.test: ok')
