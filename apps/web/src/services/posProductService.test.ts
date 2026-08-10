import {
  batchUpdateProductStoreRecords,
  createProduct,
  createProductWithPrices,
  createPushProductsToHqJob,
  createHqProductFullSyncJob,
  createHqProductIncrementalSyncJob,
  createSupplierImageBatchUpdateJob,
  getProductByBarcode,
  getProductById,
  getProducts,
  getPushToHqStoreOptions,
  getPushProductsToHqJob,
  getSyncProductsToStoresJob,
  getSupplierImageBatchUpdateJob,
  getHqProductSyncJob,
  pushProductsToHq,
  startSyncProductsToStoresJob,
  syncProductsFromHqFull,
  updateProduct,
} from './posProductService'
import { getAllChinaSuppliers } from './chinaSupplierService'
import { RequestError } from '../utils/request'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

// 深度比较对象时忽略键顺序（JSON.stringify 对键序敏感，会让归一化后的等价对象误判失败），数组仍按索引顺序比较。
function findDeepMismatch(actual: unknown, expected: unknown): string | undefined {
  if (Array.isArray(actual) || Array.isArray(expected)) {
    if (!Array.isArray(actual) || !Array.isArray(expected) || actual.length !== expected.length) {
      return '数组结构不一致'
    }
    for (let index = 0; index < actual.length; index++) {
      const mismatch = findDeepMismatch(actual[index], expected[index])
      if (mismatch) {
        return `[${index}]${mismatch}`
      }
    }
    return undefined
  }

  if (isPlainObject(actual) || isPlainObject(expected)) {
    if (!isPlainObject(actual) || !isPlainObject(expected)) {
      return '对象结构不一致'
    }
    const actualKeys = Object.keys(actual)
    if (actualKeys.length !== Object.keys(expected).length) {
      return `键集合不一致（实际 ${actualKeys.sort().join(',')}，期望 ${Object.keys(expected).sort().join(',')}）`
    }
    for (const key of Object.keys(expected)) {
      if (!Object.prototype.hasOwnProperty.call(actual, key)) {
        return `缺少键 ${key}`
      }
      const mismatch = findDeepMismatch(actual[key], expected[key])
      if (mismatch) {
        return `.${key}${mismatch}`
      }
    }
    return undefined
  }

  return actual === expected ? undefined : '值不一致'
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const mismatch = findDeepMismatch(actual, expected)
  if (mismatch) {
    throw new Error(
      `${message}。Expected: ${JSON.stringify(expected)}, received: ${JSON.stringify(actual)}（差异：${mismatch}）`,
    )
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
      productsAdded: 1,
      productsUpdated: 2,
      warehouseInventoriesCreated: 9,
      warehouseInventoriesUpdated: 10,
      storeRetailPricesCreated: 3,
      storeRetailPricesUpdated: 4,
      productSetCodesCreated: 5,
      productSetCodesUpdated: 6,
      storeMultiCodesCreated: 7,
      storeMultiCodesUpdated: 8,
      errors: [],
    },
  }

  const pushResult = await pushProductsToHq({
    productCodes: ['HB001', 'HB002'],
    targetStoreCodes: ['1001', '1002'],
    items: [
      {
        productCode: 'HB001',
        localSupplierCode: 'DATS',
        itemNumber: '72653',
        domesticPrice: 3.8,
        importPrice: 1.21,
        oemPrice: 1.45,
        isNewProduct: false,
        warehouseIsActive: true,
      },
      {
        localSupplierCode: 'DATS',
        itemNumber: '72654',
        domesticPrice: 4.2,
        importPrice: 1.33,
        oemPrice: 1.58,
        isNewProduct: false,
        warehouseIsActive: false,
      },
    ],
  })
  assertEqual(capturedUrl, '/api/react/v1/products/push-to-hq', '选中商品发送 HQ 应调用固定接口')
  assertEqual(capturedInit?.method, 'POST', '选中商品发送 HQ 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCodes: ['HB001', 'HB002'],
      targetStoreCodes: ['1001', '1002'],
      items: [
        {
          productCode: 'HB001',
          localSupplierCode: 'DATS',
          itemNumber: '72653',
          domesticPrice: 3.8,
          importPrice: 1.21,
          oemPrice: 1.45,
          isNewProduct: false,
          warehouseIsActive: true,
        },
        {
          localSupplierCode: 'DATS',
          itemNumber: '72654',
          domesticPrice: 4.2,
          importPrice: 1.33,
          oemPrice: 1.58,
          isNewProduct: false,
          warehouseIsActive: false,
        },
      ],
    },
    '选中商品发送 HQ 请求应携带 productCodes、目标分店、items 与价格字段',
  )
  assertEqual(pushResult.successCount, 2, '发送 HQ 应使用后端返回的商品成功数')
  assertEqual(pushResult.failedCount, 0, '发送 HQ 无错误明细时失败数应为 0')
  assertEqual(pushResult.totalCount, 2, '发送 HQ 应使用后端返回的商品合计数')
  assertEqual(pushResult.affectedRowCount, 55, '发送 HQ 缺少后端汇总时应把库存、分店价格和多码统计合并为影响记录数')
  assertEqual(pushResult.warehouseInventoriesCreated, 9, '发送 HQ 应保留仓库库存新增统计')
  assertEqual(pushResult.warehouseInventoriesUpdated, 10, '发送 HQ 应保留仓库库存更新统计')

  nextPayload = {
    success: true,
    data: [
      { storeCode: ' 1001 ', storeName: 'Sunnybank' },
      { storeCode: '1001', storeName: 'Duplicate Sunnybank' },
      { storeCode: '1002', storeName: 'Garden City' },
      { storeCode: '   ', storeName: 'BlankCode' },
    ],
  }

  const storeOptions = await getPushToHqStoreOptions()
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/push-to-hq/store-options',
    '发送 HQ 弹窗应通过固定接口读取最新 HQ 分店选项',
  )
  assertEqual(capturedInit?.method, 'GET', '读取 HQ 分店选项应使用 GET')
  assertDeepEqual(
    storeOptions,
    [
      { storeCode: '1001', storeName: 'Sunnybank' },
      { storeCode: '1002', storeName: 'Garden City' },
    ],
    '发送 HQ 分店选项应通过 unwrapApiData 并去空、去重归一 ApiResponse.data 数组',
  )

  nextPayload = {
    success: true,
    data: {
      jobId: 'push-hq-job-1',
      operationId: 'container-push-hq:container-1:HB001',
      status: 'queued',
      message: '任务已提交',
    },
  }

  const pushJob = await createPushProductsToHqJob({
    operationId: 'container-push-hq:container-1:HB001',
    productCodes: ['HB001'],
    items: [
      {
        productCode: 'HB001',
        localSupplierCode: 'DATS',
        itemNumber: '72653',
        domesticPrice: 3.8,
        importPrice: 1.21,
        oemPrice: 1.45,
        isNewProduct: false,
        warehouseIsActive: true,
      },
    ],
  })
  assertEqual(capturedUrl, '/api/react/v1/products/push-to-hq/jobs', '发送 HQ job 应调用后台任务创建接口')
  assertEqual(capturedInit?.method, 'POST', '发送 HQ job 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      operationId: 'container-push-hq:container-1:HB001',
      productCodes: ['HB001'],
      items: [
        {
          productCode: 'HB001',
          localSupplierCode: 'DATS',
          itemNumber: '72653',
          domesticPrice: 3.8,
          importPrice: 1.21,
          oemPrice: 1.45,
          isNewProduct: false,
          warehouseIsActive: true,
        },
      ],
    },
    '发送 HQ job 请求应保留 operationId、productCodes 和候选 items',
  )
  assertEqual(pushJob.status, 'Queued', '发送 HQ job queued 应归一为 Queued')

  nextPayload = {
    success: true,
    data: {
      jobId: 'push-hq-job-1',
      status: 'completed',
      result: {
        successCount: 1,
        failedCount: 1,
        totalCount: 2,
        productsAdded: 1,
        productsUpdated: 2,
        warehouseInventoriesCreated: 3,
        warehouseInventoriesUpdated: 4,
        storeRetailPricesCreated: 5,
        storeRetailPricesUpdated: 6,
        productSetCodesCreated: 7,
        productSetCodesUpdated: 8,
        storeMultiCodesCreated: 9,
        storeMultiCodesUpdated: 10,
        errors: ['HB002 写入失败'],
      },
      errors: ['后台任务存在错误'],
    },
  }

  const completedPushJob = await getPushProductsToHqJob('push-hq-job-1')
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/push-to-hq/jobs/push-hq-job-1',
    '查询发送 HQ job 应调用任务查询接口',
  )
  assertEqual(completedPushJob.status, 'Succeeded', 'completed 应归一为 Succeeded')
  assertEqual(completedPushJob.result?.productsAdded, 1, '发送 HQ job 应保留商品新增统计')
  assertEqual(completedPushJob.result?.warehouseInventoriesCreated, 3, '发送 HQ job 应保留库存新增统计')
  assertEqual(completedPushJob.result?.storeRetailPricesUpdated, 6, '发送 HQ job 应保留零售价更新统计')
  assertEqual(completedPushJob.result?.productSetCodesCreated, 7, '发送 HQ job 应保留套装编码新增统计')
  assertEqual(completedPushJob.result?.storeMultiCodesUpdated, 10, '发送 HQ job 应保留多码更新统计')
  assertDeepEqual(completedPushJob.result?.errors, ['HB002 写入失败'], '发送 HQ job 应保留 result 错误明细')
  assertDeepEqual(completedPushJob.errors, ['后台任务存在错误'], '发送 HQ job 应保留顶层错误摘要')

  nextPayload = {
    success: true,
    data: {
      jobId: 'supplier-image-job-1',
      operationId: 'supplier-image:DATS',
      status: 'queued',
      request: {
        localSupplierCode: 'DATS',
      },
    },
  }

  const imageJob = await createSupplierImageBatchUpdateJob({
    localSupplierCode: 'DATS',
    urlTemplate: 'https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg',
    updateHbweb: true,
    updateHq: false,
    saveSupplierImageBaseUrl: false,
    productCodes: ['P001', 'P002'],
    operationId: 'supplier-image:DATS',
  })
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/batch-update-supplier-images/job',
    '供应商图片批量修改 job 应调用后台任务创建接口',
  )
  assertEqual(capturedInit?.method, 'POST', '供应商图片批量修改 job 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      localSupplierCode: 'DATS',
      urlTemplate: 'https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg',
      updateHbweb: true,
      updateHq: false,
      saveSupplierImageBaseUrl: false,
      productCodes: ['P001', 'P002'],
      operationId: 'supplier-image:DATS',
    },
    '供应商图片批量修改 job 请求应保留模板、目标库、选择商品、保存标记和 operationId',
  )
  assertEqual(imageJob.status, 'Queued', '供应商图片批量修改 job queued 应归一为 Queued')

  nextPayload = {
    success: true,
    data: {
      jobId: 'supplier-image-job-1',
      status: 'succeeded',
      result: {
        totalCount: 12,
        hbwebUpdatedCount: 12,
        hqUpdatedCount: 0,
        hbwebSkippedExistingImageCount: 3,
        hqSkippedExistingImageCount: 4,
        skippedCount: 0,
        hqFailedCount: 0,
        errors: [],
      },
    },
  }

  const completedImageJob = await getSupplierImageBatchUpdateJob('supplier-image-job-1')
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/batch-update-supplier-images/job/supplier-image-job-1',
    '查询供应商图片批量修改 job 应调用任务查询接口',
  )
  assertEqual(completedImageJob.status, 'Succeeded', '供应商图片批量修改 job succeeded 应归一为 Succeeded')
  assertEqual(completedImageJob.result?.hbwebUpdatedCount, 12, '供应商图片批量修改 job 应保留结果统计')
  assertEqual(completedImageJob.result?.hbwebSkippedExistingImageCount, 3, '供应商图片批量修改 job 应保留 Hbweb 已有图片跳过数量')
  assertEqual(completedImageJob.result?.hqSkippedExistingImageCount, 4, '供应商图片批量修改 job 应保留 HQ 已有图片跳过数量')

  nextPayload = {
    success: true,
    data: {
      jobId: 'sync-store-job-1',
      operationId: 'sync-store:HB001:S001',
      status: 'pending',
      isDuplicateRequest: true,
      message: '任务已存在，继续复用后台执行',
    },
  }

  const syncToStoresJob = await startSyncProductsToStoresJob({
    productCodes: ['HB001'],
    storeCodes: ['S001'],
    overwrite: false,
    fields: ['purchasePrice', 'retailPrice'],
  })
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/sync-to-stores/jobs',
    '同步到分店 job 应调用后台任务创建接口',
  )
  assertEqual(capturedInit?.method, 'POST', '同步到分店 job 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCodes: ['HB001'],
      storeCodes: ['S001'],
      overwrite: false,
      fields: ['purchasePrice', 'retailPrice'],
    },
    '同步到分店 job 请求应保留商品、分店、覆盖开关和字段列表',
  )
  assertEqual(syncToStoresJob.status, 'Queued', 'pending 应归一为 Queued')
  assertEqual(syncToStoresJob.isDuplicateRequest, true, '同步到分店 job 应保留重复提交标记')

  nextPayload = {
    success: true,
    data: {
      jobId: 'sync-store-job-1',
      operationId: 'sync-store:HB001:S001',
      status: 'completed',
      message: '同步完成',
      result: {
        createdCount: 2,
        updatedCount: 3,
        failedCount: 1,
        errors: ['S003 同步失败'],
      },
    },
  }

  const completedSyncToStoresJob = await getSyncProductsToStoresJob('sync-store-job-1')
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/sync-to-stores/jobs/sync-store-job-1',
    '查询同步到分店 job 应调用任务查询接口',
  )
  assertEqual(completedSyncToStoresJob.status, 'Succeeded', 'completed 应归一为 Succeeded')
  assertEqual(completedSyncToStoresJob.result?.createdCount, 2, '同步到分店 job 应保留创建数量')
  assertEqual(completedSyncToStoresJob.result?.updatedCount, 3, '同步到分店 job 应保留更新数量')
  assertEqual(completedSyncToStoresJob.result?.failedCount, 1, '同步到分店 job 应保留失败数量')
  assertDeepEqual(completedSyncToStoresJob.result?.errors, ['S003 同步失败'], '同步到分店 job 应保留错误明细')

  nextPayload = {
    success: true,
    data: {
      jobId: 'sync-store-job-failed-1',
      operationId: 'sync-store:HB001:S001',
      status: 'failed',
      message: '同步到分店任务失败',
      result: {
        createdCount: 0,
        updatedCount: 0,
        failedCount: 2,
        errors: ['S001 写入失败', 'S002 写入失败'],
        message: '全部分店写入失败',
      },
      errors: ['后端任务执行失败'],
    },
  }

  const failedSyncToStoresJob = await getSyncProductsToStoresJob('sync-store-job-failed-1')
  assertEqual(failedSyncToStoresJob.status, 'Failed', '同步到分店 job failed payload 应归一为 Failed')
  assertEqual(failedSyncToStoresJob.message, '同步到分店任务失败', '同步到分店 job Failed payload 应保留顶层 message')
  assertEqual(failedSyncToStoresJob.result?.message, '全部分店写入失败', '同步到分店 job Failed payload 应保留 result.message')
  assertEqual(failedSyncToStoresJob.result?.failedCount, 2, '同步到分店 job Failed payload 应保留 result.failedCount')
  assertDeepEqual(
    failedSyncToStoresJob.result?.errors,
    ['S001 写入失败', 'S002 写入失败'],
    '同步到分店 job Failed payload 应保留 result.errors',
  )
  assertDeepEqual(
    failedSyncToStoresJob.errors,
    ['后端任务执行失败'],
    '同步到分店 job Failed payload 应保留顶层 errors',
  )

  nextPayload = {
    success: true,
    data: {
      successCount: 2,
      failedCount: 1,
      errors: ['S003 更新失败'],
    },
  }

  const batchStoreRecordResult = await batchUpdateProductStoreRecords('HB 001/测试', {
    storeCodes: ['S001', 'S002'],
    changes: {
      purchasePrice: 10.5,
      storeRetailPriceValue: 19.9,
      discountRate: 0.88,
      isAutoPricing: true,
      isSpecialProduct: false,
      isActive: true,
    },
  })
  assertEqual(
    capturedUrl,
    '/api/react/v1/products/HB%20001%2F%E6%B5%8B%E8%AF%95/store-records/batch-update',
    '分店记录批量修改应对 productCode 做 encode 后再拼接路径',
  )
  assertEqual(capturedInit?.method, 'POST', '分店记录批量修改应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      storeCodes: ['S001', 'S002'],
      changes: {
        purchasePrice: 10.5,
        storeRetailPriceValue: 19.9,
        discountRate: 0.88,
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true,
      },
    },
    '分店记录批量修改请求体只应包含 storeCodes 和 changes',
  )
  assertDeepEqual(
    batchStoreRecordResult,
    {
      successCount: 2,
      failedCount: 1,
      errors: ['S003 更新失败'],
    },
    '分店记录批量修改应返回 unwrap 后的统计结果',
  )

  nextPayload = {
    success: true,
    data: {
      productCode: 'HB10001',
      storeProductCodes: {
        S001: 'S001-HB10001',
        S002: 'S002-HB10001',
      },
      product: {
        productCode: 'HB10001',
        productName: '测试新商品',
      },
    },
  }

  const createWithPricesResult = await createProductWithPrices({
    barcode: '930000000001',
    productName: '测试新商品',
    productImage: 'https://img.example.com/HB10001.jpg',
    purchasePrice: 5.2,
    retailPrice: 9.9,
    localSupplierCode: 'SUP01',
    isAutoPricing: true,
    isSpecialProduct: false,
    isActive: true,
  })
  assertEqual(capturedUrl, '/api/react/v1/products/create-with-prices', '创建商品带分店价格应调用固定接口')
  assertEqual(capturedInit?.method, 'POST', '创建商品带分店价格应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      barcode: '930000000001',
      productName: '测试新商品',
      productImage: 'https://img.example.com/HB10001.jpg',
      purchasePrice: 5.2,
      retailPrice: 9.9,
      localSupplierCode: 'SUP01',
      isAutoPricing: true,
      isSpecialProduct: false,
      isActive: true,
    },
    '创建商品带分店价格请求体应原样提交 DTO',
  )
  assertDeepEqual(
    createWithPricesResult,
    {
      productCode: 'HB10001',
      storeProductCodes: {
        S001: 'S001-HB10001',
        S002: 'S002-HB10001',
      },
      product: {
        productCode: 'HB10001',
        productName: '测试新商品',
      },
    },
    '创建商品带分店价格应返回 unwrap 后的结果',
  )

  nextPayload = {
    success: true,
    data: [
      {
        productCode: 'P001',
        productCategoryGUID: 'cat-list-1',
        warehouseCategoryGUID: 'wh-list-1',
        domesticSupplierCode: 'CN-001',
        domesticSupplierName: '国内供应商一',
      },
    ],
    total: 1,
  }

  const listResult = await getProducts({
    pageIndex: 1,
    pageSize: 20,
    categoryGuid: 'top-cat',
    warehouseCategoryGuid: 'top-wh',
    columnFilters: {
      categoryGuid: ['col-cat'],
      warehouseCategoryGuid: ['col-wh'],
      domesticSupplierCode: ['CN-001', 'CN-002'],
    },
  })
  assertEqual(capturedUrl, '/api/react/v1/products/list', '商品列表应调用分页查询接口')
  assertEqual(capturedInit?.method, 'POST', '商品列表应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      pageNumber: 1,
      pageSize: 20,
      productCategoryGUIDs: ['top-cat'],
      warehouseCategoryGUID: 'top-wh',
      warehouseCategoryGUIDs: ['top-wh'],
      domesticSupplierCodes: ['CN-001', 'CN-002'],
    },
    '商品列表顶部 categoryGuid/warehouseCategoryGuid 应优先于同列头过滤并发送数组；国内供应商列头发送 domesticSupplierCodes',
  )
  assertEqual(listResult.total, 1, '商品列表应透传后端 total')
  assertDeepEqual(
    listResult.items[0],
    {
      productCode: 'P001',
      categoryGuid: 'cat-list-1',
      warehouseCategoryGuid: 'wh-list-1',
      domesticSupplierCode: 'CN-001',
      domesticSupplierName: '国内供应商一',
    },
    '商品列表响应应归一化 productCategoryGUID/warehouseCategoryGUID 及国内供应商字段',
  )

  nextPayload = {
    success: true,
    data: [
      {
        Guid: 'supplier-disabled',
        SupplierCode: 'CN-DISABLED',
        SupplierName: '停用但仍有关联的供应商',
        Status: 0,
      },
      {
        Guid: 'supplier-disabled-duplicate',
        SupplierCode: 'CN-DISABLED',
        SupplierName: '停用但仍有关联的供应商',
        Status: 0,
      },
      {
        Guid: 'supplier-empty-code',
        SupplierCode: '   ',
        SupplierName: '无有效编码供应商',
        Status: 1,
      },
    ],
  }
  const allDomesticSuppliers = await getAllChinaSuppliers()
  assertEqual(capturedUrl, '/api/v1/ChinaSuppliers/all', '商品列头选项应调用包含停用且排除软删记录的全部国内供应商接口')
  assertEqual(allDomesticSuppliers.length, 1, '全部国内供应商接口应按非空供应商代码去重')
  assertEqual(allDomesticSuppliers[0]?.guid, 'supplier-disabled', '全部国内供应商接口应归一化 GUID')
  assertEqual(allDomesticSuppliers[0]?.supplierCode, 'CN-DISABLED', '全部国内供应商接口应归一化供应商代码')
  assertEqual(allDomesticSuppliers[0]?.supplierName, '停用但仍有关联的供应商', '全部国内供应商接口应归一化供应商名称')
  assertEqual(allDomesticSuppliers[0]?.status, 0, '全部国内供应商接口应保留停用状态')

  nextPayload = {
    success: true,
    data: [],
    total: 0,
  }

  await getProducts({
    columnFilters: {
      categoryGuid: ['col-cat-1', 'col-cat-2'],
      warehouseCategoryGuid: ['col-wh-1'],
      domesticSupplierCode: ['CN-009'],
    },
  })
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCategoryGUIDs: ['col-cat-1', 'col-cat-2'],
      warehouseCategoryGUIDs: ['col-wh-1'],
      domesticSupplierCodes: ['CN-009'],
    },
    '无顶部筛选时商品列表应使用 categoryGuid/warehouseCategoryGuid/domesticSupplierCode 列头过滤值',
  )

  nextPayload = [
    {
      productCode: 'P002',
      productCategoryGUID: 'cat-array',
      warehouseCategoryGUID: 'wh-array',
      domesticSupplierCode: 'CN-002',
    },
  ]
  const arrayResult = await getProducts({})
  assertDeepEqual(
    arrayResult.items,
    [
      {
        productCode: 'P002',
        categoryGuid: 'cat-array',
        warehouseCategoryGuid: 'wh-array',
        domesticSupplierCode: 'CN-002',
      },
    ],
    '商品列表直接数组响应应归一化字段',
  )
  assertEqual(arrayResult.total, 1, '商品列表直接数组响应 total 应为数组长度')

  nextPayload = {
    success: true,
    data: [
      {
        productCode: 'P003',
        productCategoryGUID: 'cat-data',
        warehouseCategoryGUID: 'wh-data',
      },
    ],
    total: 9,
  }
  const dataResult = await getProducts({})
  assertDeepEqual(
    dataResult.items,
    [
      {
        productCode: 'P003',
        categoryGuid: 'cat-data',
        warehouseCategoryGuid: 'wh-data',
      },
    ],
    '商品列表 data+total 包裹响应应归一化字段',
  )
  assertEqual(dataResult.total, 9, '商品列表 data+total 包裹响应应保留 total')

  nextPayload = {
    success: true,
    data: {
      items: [
        {
          productCode: 'P004',
          productCategoryGUID: 'cat-paged',
          warehouseCategoryGUID: 'wh-paged',
          domesticSupplierName: '国内四',
        },
      ],
      total: 7,
      pageNumber: 1,
      pageSize: 10,
    },
  }
  const pagedResult = await getProducts({})
  assertDeepEqual(
    pagedResult.items,
    [
      {
        productCode: 'P004',
        categoryGuid: 'cat-paged',
        warehouseCategoryGuid: 'wh-paged',
        domesticSupplierName: '国内四',
      },
    ],
    '商品列表 PagedResult 包裹响应应归一化字段',
  )
  assertEqual(pagedResult.total, 7, '商品列表 PagedResult 包裹响应应保留 total')
  assertEqual((pagedResult as { page?: number }).page, 1, '商品列表 PagedResult 包裹响应应保留页码')

  const detailPayload = {
    success: true,
    data: {
      productCode: 'P005',
      productCategoryGUID: 'cat-detail',
      warehouseCategoryGUID: 'wh-detail',
      DomesticSupplierCode: 'CN-005',
      DomesticSupplierName: '国内五',
    },
  }

  nextPayload = detailPayload
  const productDetail = await getProductById('P005')
  assertDeepEqual(
    productDetail,
    {
      productCode: 'P005',
      categoryGuid: 'cat-detail',
      warehouseCategoryGuid: 'wh-detail',
      domesticSupplierCode: 'CN-005',
      domesticSupplierName: '国内五',
    },
    '商品详情应复用同一归一化 helper',
  )

  nextPayload = detailPayload
  const barcodeDetail = await getProductByBarcode('930000000005')
  assertEqual(barcodeDetail.categoryGuid, 'cat-detail', '条码查询商品应归一化商品分类')
  assertEqual(barcodeDetail.warehouseCategoryGuid, 'wh-detail', '条码查询商品应归一化仓库分类')
  assertEqual(barcodeDetail.domesticSupplierCode, 'CN-005', '条码查询商品应归一化国内供应商编码')

  nextPayload = detailPayload
  const createdProduct = await createProduct({
    productName: '新建商品',
    categoryGuid: 'cat-create',
    warehouseCategoryGuid: 'wh-create',
  })
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productName: '新建商品',
      productCategoryGUID: 'cat-create',
      warehouseCategoryGUID: 'wh-create',
    },
    '创建商品请求应映射两套分类 GUID 且不发送前端别名',
  )
  assertEqual(createdProduct.categoryGuid, 'cat-detail', '创建商品返回应归一化商品分类')
  assertEqual(createdProduct.warehouseCategoryGuid, 'wh-detail', '创建商品返回应归一化仓库分类')

  nextPayload = detailPayload
  const updatedProduct = await updateProduct('P005', {
    productName: '更新商品',
    categoryGuid: 'cat-update',
    warehouseCategoryGuid: 'wh-update',
  })
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productName: '更新商品',
      productCategoryGUID: 'cat-update',
      warehouseCategoryGUID: 'wh-update',
    },
    '更新商品请求应映射两套分类 GUID 且不发送前端别名',
  )
  assertEqual(updatedProduct.categoryGuid, 'cat-detail', '更新商品返回应归一化商品分类')
  assertEqual(updatedProduct.domesticSupplierCode, 'CN-005', '更新商品返回应归一化国内供应商编码')

  nextPayload = {
    success: true,
    data: {
      productCode: 'P006',
      storeProductCodes: {},
      product: {
        productCode: 'P006',
        productCategoryGUID: 'cat-create-prices',
        warehouseCategoryGUID: 'wh-create-prices',
        domesticSupplierCode: 'CN-006',
      },
    },
  }
  const createPricesResult = await createProductWithPrices({
    productName: '带价创建',
    isAutoPricing: true,
    isSpecialProduct: false,
  })
  assertEqual(createPricesResult.product?.categoryGuid, 'cat-create-prices', '创建带分店价格商品应归一化内嵌 product 商品分类')
  assertEqual(createPricesResult.product?.warehouseCategoryGuid, 'wh-create-prices', '创建带分店价格商品应归一化内嵌 product 仓库分类')
  assertEqual(createPricesResult.product?.domesticSupplierCode, 'CN-006', '创建带分店价格商品应归一化内嵌 product 国内供应商编码')

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

  const syncToStoresJobFailurePayload = {
    success: false,
    message: '创建同步到分店任务失败',
    data: {
      reason: 'duplicate operationId',
      request: {
        productCodes: ['HB001'],
        storeCodes: ['S001'],
      },
    },
  }
  nextPayload = syncToStoresJobFailurePayload

  await assertRequestError(
    () =>
      startSyncProductsToStoresJob({
        productCodes: ['HB001'],
        storeCodes: ['S001'],
        overwrite: false,
        fields: ['purchasePrice'],
      }),
    '创建同步到分店任务失败',
    syncToStoresJobFailurePayload,
    '同步到分店 job 接口 success:false',
  )

  const createWithPricesFailurePayload = {
    success: false,
    message: '创建商品失败',
    data: {
      errors: ['条码已存在'],
    },
  }
  nextPayload = createWithPricesFailurePayload

  await assertRequestError(
    () =>
      createProductWithPrices({
        barcode: '930000000001',
        productName: '测试新商品',
        purchasePrice: 5.2,
        retailPrice: 9.9,
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true,
      }),
    '创建商品失败',
    createWithPricesFailurePayload,
    '创建商品带分店价格接口 success:false',
  )
} finally {
  globalThis.fetch = originalFetch
}
