import { readFileSync } from 'node:fs'
import path from 'node:path'
import { formatPaginationTotalText, getPaginationTotalPages } from './pagination'
import {
  getStorePriceTransferJob,
  getStoreProductPriceGrid,
  startStorePriceTransferJob,
  syncFromHq,
} from '../../../services/storeProductPriceService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, message: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, message)
    return
  }

  throw new Error(`${message}。Expected promise to reject`)
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

const pageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/StoreProductPrice/index.tsx')
const typeFile = path.resolve(process.cwd(), 'src/types/storeProductPrice.ts')
const pageSource = readFileSync(pageFile, 'utf8')
const typeSource = readFileSync(typeFile, 'utf8')

async function main() {
  const failures: string[] = []

  const typeFailure = await runTest('SyncFromHqRequest 应声明 endDate', () => {
    assert(
      typeSource.includes('endDate?: string'),
      'SyncFromHqRequest 类型中应新增 endDate 可选字段',
    )
  })
  if (typeFailure) failures.push(typeFailure)

  const transferTypeFailure = await runTest('StorePriceTransferRequest 应声明方向、表和字段选择', () => {
    assert(
      typeSource.includes("export type StorePriceTransferDirection = 'HqToLocal' | 'LocalToHq'"),
      '双向价格同步类型应限制方向枚举',
    )
    assert(
      typeSource.includes('syncRetailPrices: boolean') && typeSource.includes('syncMultiCodePrices: boolean'),
      '双向价格同步请求应包含价格表和多码表选择',
    )
    assert(
      typeSource.includes('syncPurchasePrice: boolean') && typeSource.includes('syncRetailPrice: boolean'),
      '双向价格同步请求应包含价格字段选择',
    )
  })
  if (transferTypeFailure) failures.push(transferTypeFailure)

  const pagePayloadFailure = await runTest('页面应同时传递 startDate 和 endDate', () => {
    assert(
      pageSource.includes('selectedStoreCodes: values.selectedStoreCodes'),
      '页面应总是把必填分店列表写入请求体',
    )
    assert(
      pageSource.includes("startDate: values.dateRange[0].format('YYYY-MM-DD')"),
      '页面应继续从范围选择器读取 startDate',
    )
    assert(
      pageSource.includes("endDate: values.dateRange[1].format('YYYY-MM-DD')"),
      '页面应从范围选择器读取 endDate',
    )
  })
  if (pagePayloadFailure) failures.push(pagePayloadFailure)

  const pageRequiredFailure = await runTest('页面应要求选择分店和日期范围', () => {
    assert(
      pageSource.includes("rules={[{ required: true, message: t('posAdmin.productPrice.selectStoreRequired', '请选择分店') }]}"),
      'HQ 同步弹窗应要求选择分店',
    )
    assert(
      pageSource.includes("rules={[{ required: true, message: t('posAdmin.productPrice.selectDateRangeRequired', '请选择日期范围') }]}"),
      'HQ 同步弹窗应要求选择日期范围',
    )
  })
  if (pageRequiredFailure) failures.push(pageRequiredFailure)

  const selectAllFailure = await runTest('页面应提供从 storeOptions 生成分店全选的逻辑', () => {
    assert(
      pageSource.includes('const selectAllHqSyncStores = () => {'),
      '页面应声明 HQ 同步分店全选函数',
    )
    assert(
      pageSource.includes("hqSyncForm.setFieldValue('selectedStoreCodes', storeOptions.map((option) => option.value))"),
      '全选函数应把所有分店编码写入 selectedStoreCodes',
    )
    assert(
      pageSource.includes("t('posAdmin.productPrice.selectAllStores', '全选分店')"),
      'HQ 同步弹窗应显示全选分店按钮文案',
    )
  })
  if (selectAllFailure) failures.push(selectAllFailure)

  const selectAllBindingFailure = await runTest('HQ 同步分店多选框应直接绑定到 Form.Item 字段', () => {
    assert(
      !pageSource.includes('<Form.Item name="selectedStoreCodes" label={t('),
      'selectedStoreCodes 不应包住 Space.Compact，否则 Select 不会接收表单字段 value/onChange',
    )
    assert(
      pageSource.includes('<Form.Item name="selectedStoreCodes" noStyle rules={[{ required: true, message: t('),
      'selectedStoreCodes 应使用 noStyle Form.Item 直接包住 Select',
    )
    assert(
      pageSource.includes('<Button htmlType="button" icon={<CheckSquareOutlined />} onClick={selectAllHqSyncStores}>'),
      '全选按钮应声明 htmlType="button"，避免被表单上下文当成提交按钮',
    )
  })
  if (selectAllBindingFailure) failures.push(selectAllBindingFailure)

  const pageErrorFailure = await runTest('页面应优先展示后端返回的失败文案', () => {
    assert(
      pageSource.includes("error instanceof Error ? error.message : t('posAdmin.productPrice.hqSyncFailed', '从HQ同步失败')"),
      '页面 catch 分支应优先展示后端错误消息，而不是固定提示',
    )
  })
  if (pageErrorFailure) failures.push(pageErrorFailure)

  const priceTransferPageFailure = await runTest('页面应新增独立 HQ/本地价格同步 job 弹窗', () => {
    assert(
      pageSource.includes("t('posAdmin.productPrice.priceTransfer', 'HQ/本地价格同步')"),
      '页面应显示独立的 HQ/本地价格同步入口',
    )
    assert(
      pageSource.includes("direction: 'HqToLocal'"),
      '弹窗默认方向应为 HQ -> 本地',
    )
    assert(
      pageSource.includes('startStorePriceTransferJob(dto)'),
      '页面提交应走后台 job 创建接口',
    )
    assert(
      pageSource.includes('createHqSyncJobPoller<StorePriceTransferJobDto>'),
      '页面应复用 2 秒轮询 job 工具',
    )
    assert(
      pageSource.includes('syncRetailPrices: !!values.syncRetailPrices') &&
        pageSource.includes('syncMultiCodePrices: !!values.syncMultiCodePrices'),
      '页面 payload 应包含同步表选择',
    )
  })
  if (priceTransferPageFailure) failures.push(priceTransferPageFailure)

  const validationErrorFailure = await runTest('表单校验失败不应弹出同步失败全局提示', () => {
    assert(
      pageSource.includes('function isFormValidationError(error: unknown): error is { errorFields: unknown[] }'),
      '页面应声明 AntD 表单校验错误识别函数',
    )
    assert(
      pageSource.includes('if (isFormValidationError(error)) return'),
      'handleSyncFromHq 应让字段级校验错误留在表单内展示，不弹出同步失败提示',
    )
  })
  if (validationErrorFailure) failures.push(validationErrorFailure)

  const paginationTotalPagesFailure = await runTest('分页文案应同时显示总数和总页数', () => {
    assertEqual(getPaginationTotalPages(128, 50), 3, '128 条且每页 50 条时应显示 3 页')
    assertEqual(getPaginationTotalPages(0, 50), 0, '0 条数据时应显示 0 页')
    assertEqual(getPaginationTotalPages(10, 0), 10, 'pageSize 异常时应使用 1 作为兜底页大小')

    const text = formatPaginationTotalText(128, 50, (_key, _fallback, values) => (
      `共 ${values?.count} 条 / ${values?.pages} 页`
    ))

    assertEqual(text, '共 128 条 / 3 页', '分页文案应把总数和总页数一起展示')
  })
  if (paginationTotalPagesFailure) failures.push(paginationTotalPagesFailure)

  const originalFetch = globalThis.fetch

  const pagedFieldFailure = await runTest('商品价格分页接口应兼容 totalCount 和 pageIndex 字段', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: true,
      data: {
        items: [{ productCode: 'P001', isActive: true, isStoreAutoPricing: false, isStoreSpecialProduct: false }],
        totalCount: 128,
        pageIndex: 3,
        pageSize: 50,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    const result = await getStoreProductPriceGrid({
      storeCode: 'S01',
      pageNumber: 3,
      pageSize: 50,
    })

    assertEqual(result.total, 128, '分页结果应把 totalCount 归一为 total')
    assertEqual(result.page, 3, '分页结果应把 pageIndex 归一为 page')
    assertEqual(result.pageSize, 50, '分页结果应保留后端 pageSize')
    assertEqual(result.items.length, 1, '分页结果应保留商品列表')
  })
  if (pagedFieldFailure) failures.push(pagedFieldFailure)

  const businessFailure = await runTest('syncFromHq 遇到 success false 时应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步失败：测试业务错误',
      data: {
        addedCount: 99,
        updatedCount: 88,
        totalProcessed: 187,
        durationMs: 2000,
        errors: [],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => syncFromHq({ selectedStoreCodes: ['S01'], startDate: '2026-05-01', endDate: '2026-05-31' }),
      'HQ 同步失败：测试业务错误',
      'syncFromHq 不应把 success false 的响应当成成功结果',
    )
  })
  if (businessFailure) failures.push(businessFailure)

  const httpFailure = await runTest('syncFromHq 遇到非 2xx 时应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步失败：测试 HTTP 错误',
    }), {
      status: 500,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => syncFromHq({ selectedStoreCodes: ['S01'], startDate: '2026-05-01', endDate: '2026-05-31' }),
      'HQ 同步失败：测试 HTTP 错误',
      'syncFromHq 应把非 2xx 的后端消息透传出来',
    )
  })
  if (httpFailure) failures.push(httpFailure)

  const priceTransferStartFailure = await runTest('startStorePriceTransferJob 应 POST 到后台任务接口并归一化返回', async () => {
    globalThis.fetch = (async (input, init) => {
      assertEqual(String(input), '/api/react/v1/store-product-prices/store-price-transfer-jobs', '创建任务接口路径应正确')
      const body = JSON.parse(String(init?.body || '{}'))
      assertEqual(body.direction, 'HqToLocal', '创建任务 payload 应包含方向')
      assertEqual(body.syncMultiCodePrices, true, '创建任务 payload 应包含多码表选择')
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: 'job-1',
          status: 'Running',
          isDuplicateRequest: true,
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    const job = await startStorePriceTransferJob({
      direction: 'HqToLocal',
      sourceStoreCode: 'S01',
      targetStoreCode: 'T01',
      syncRetailPrices: true,
      syncMultiCodePrices: true,
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncDiscountRate: false,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
    })

    assertEqual(job.jobId, 'job-1', '创建任务应返回 jobId')
    assertEqual(job.status, 'Running', '创建任务应归一化运行中状态')
    assertEqual(job.isDuplicateRequest, true, '创建任务应保留重复提交标记')
  })
  if (priceTransferStartFailure) failures.push(priceTransferStartFailure)

  const priceTransferGetFailure = await runTest('getStorePriceTransferJob 应 GET 任务状态并归一化统计', async () => {
    globalThis.fetch = (async (input) => {
      assertEqual(String(input), '/api/react/v1/store-product-prices/store-price-transfer-jobs/job-1', '查询任务接口路径应正确')
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: 'job-1',
          status: 'Succeeded',
          result: {
            totalProcessed: 4,
            insertedCount: 2,
            updatedCount: 2,
            retailPriceInserted: 1,
            multiCodeInserted: 1,
          },
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    const job = await getStorePriceTransferJob('job-1')
    assertEqual(job.status, 'Succeeded', '查询任务应归一化成功状态')
    assertEqual(job.result?.totalProcessed, 4, '查询任务应归一化总处理数')
    assertEqual(job.result?.retailPriceInserted, 1, '查询任务应归一化价格表新增数')
    assertEqual(job.result?.multiCodeInserted, 1, '查询任务应归一化多码表新增数')
  })
  if (priceTransferGetFailure) failures.push(priceTransferGetFailure)

  const priceTransferMissingStatusFailure = await runTest('getStorePriceTransferJob 缺少状态时应暴露契约错误', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: true,
      data: {
        jobId: 'job-missing-status',
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => getStorePriceTransferJob('job-missing-status'),
      '分店价格同步任务缺少状态',
      '缺少 status 时不应被默认为 Running',
    )
  })
  if (priceTransferMissingStatusFailure) failures.push(priceTransferMissingStatusFailure)

  globalThis.fetch = originalFetch

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('StoreProductPrice.hqSync.logic.test: ok')
}

await main()
