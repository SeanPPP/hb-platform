import { readFileSync } from 'node:fs'
import path from 'node:path'
import { ensureHqProducts, syncInvoicesFromHq, updateToStorePrices } from '../../../services/localSupplierInvoiceService'

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

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
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

async function assertRequestErrorPayload(
  execute: () => Promise<unknown>,
  expectedMessage: string,
  message: string,
) {
  try {
    await execute()
  } catch (error: any) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, message)
    assertEqual(error.payload?.data?.invoiceAddedCount, 1, '400 失败时应保留后端 data payload')
    assertEqual(error.payload?.data?.errors?.[0], '测试页失败', '400 失败时应保留错误列表')
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

const pageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/LocalSupplierInvoices/index.tsx')
const editPageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx')
const serviceFile = path.resolve(process.cwd(), 'src/services/localSupplierInvoiceService.ts')
const typeFile = path.resolve(process.cwd(), 'src/types/localSupplierInvoice.ts')
const pageSource = readFileSync(pageFile, 'utf8')
const editPageSource = readFileSync(editPageFile, 'utf8')
const serviceSource = readFileSync(serviceFile, 'utf8')
const typeSource = readFileSync(typeFile, 'utf8')

async function main() {
  const failures: string[] = []

  const typeFailure = await runTest('同步请求和结果类型应声明页面契约字段', () => {
    assert(typeSource.includes('LocalSupplierInvoiceHqSyncRequest'), '应声明同步请求类型')
    assert(typeSource.includes('selectedStoreCodes?: string[]'), '请求应支持 selectedStoreCodes')
    assert(typeSource.includes('startDate?: string'), '请求应支持 startDate')
    assert(typeSource.includes('endDate?: string'), '请求应支持 endDate')
    assert(typeSource.includes('invoiceAddedCount: number'), '结果应支持主表新增计数')
    assert(typeSource.includes('detailUpdatedCount: number'), '结果应支持明细更新计数')
  })
  if (typeFailure) failures.push(typeFailure)

  const ensureHqTypeFailure = await runTest('编辑页商品同步到HQ应声明专用契约字段', () => {
    assert(typeSource.includes('EnsureHqProductsRequest'), '应声明 EnsureHqProductsRequest')
    assert(typeSource.includes('detailGuids: string[]'), '商品同步请求应强制传 detailGuids')
    assert(typeSource.includes('targetStoreCodes: string[]'), '商品同步请求应强制传 targetStoreCodes，避免后端扩大写入范围')
    assert(typeSource.includes('idempotencyKey?: string'), '商品同步请求应支持 idempotencyKey')
    assert(typeSource.includes('EnsureHqProductsResult'), '应声明 EnsureHqProductsResult')
    assert(typeSource.includes('hqPurchasePricesUpdated: number'), '商品同步结果应包含 HQ 分店进货价更新计数')
    assert(typeSource.includes('errors: EnsureHqProductError[]'), '商品同步结果应包含逐行错误列表')
    assert(typeSource.includes('updateHqProduct?: boolean'), '更新到分店请求应支持手动勾选同步 HQ')
  })
  if (ensureHqTypeFailure) failures.push(ensureHqTypeFailure)

  const pageButtonFailure = await runTest('页面应给管理员显示从HQ同步按钮并打开弹窗', () => {
    assert(pageSource.includes('CloudSyncOutlined'), '页面应使用同步图标')
    assert(pageSource.includes("t('posAdmin.invoices.syncFromHQ'"), '页面应存在从HQ同步按钮文案')
    assert(pageSource.includes('isAdmin &&') && pageSource.includes('setHqSyncModalOpen(true)'), '按钮应仅管理员可见并打开同步弹窗')
  })
  if (pageButtonFailure) failures.push(pageButtonFailure)

  const pagePayloadFailure = await runTest('页面应从弹窗提交分店和日期范围', () => {
    assert(pageSource.includes('hqSyncForm.validateFields()'), '同步前应校验弹窗表单')
    assert(pageSource.includes("dto.startDate = values.dateRange[0].format('YYYY-MM-DD')"), '页面应传 startDate')
    assert(pageSource.includes("dto.endDate = values.dateRange[1].format('YYYY-MM-DD')"), '页面应传 endDate')
    assert(pageSource.includes('dto.selectedStoreCodes = values.selectedStoreCodes'), '页面应传 selectedStoreCodes')
  })
  if (pagePayloadFailure) failures.push(pagePayloadFailure)

  const editPageButtonFailure = await runTest('编辑页应使用专用权限显示同步商品到HQ按钮', () => {
    assert(editPageSource.includes('canWriteLocalPurchaseToHq'), '编辑页应使用可编辑本地进货 + PushToHq 的组合权限控制写 HQ 入口')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.syncProductsToHqBtn', '同步商品到HQ')"), '编辑页应显示同步商品到HQ按钮文案')
    assert(editPageSource.includes('handleEnsureHqProducts'), '编辑页应实现同步商品到HQ处理函数')
    assert(editPageSource.includes('selectedRowKeys.length > 0 ? selectedRowKeys.map(String) : details.map((item) => item.detailGUID)'), '编辑页应实现选中优先、未选中则全部明细')
    assert(editPageSource.includes('ensureHqProducts(invoiceGuid'), '编辑页应调用商品级 ensureHqProducts 接口，不复用进货单推送接口')
    assert(editPageSource.includes('if (ensuringHqProducts) return'), '同步商品到HQ应阻止重复确认框和重复请求')
    assert(editPageSource.includes('!_invoice?.storeCode'), '同步商品到HQ应要求进货单分店存在')
    assert(editPageSource.includes('isStoreCodeInManagedScope(_invoice.storeCode, managedStoreCodes)'), '同步商品到HQ应复核进货单分店范围')
    assert(editPageSource.includes('ensureHqIdempotencyKeyRef.current'), '同步商品到HQ应在请求周期内复用稳定 idempotencyKey')
  })
  if (editPageButtonFailure) failures.push(editPageButtonFailure)

  const updateToStoreHqFailure = await runTest('更新到分店应默认不勾选并手动传递 updateHqProduct', () => {
    assert(editPageSource.includes('name="updateHqProduct"'), '更新到分店弹窗应包含 updateHqProduct 复选框')
    assert(editPageSource.includes("storePriceForm.setFieldsValue({ updateHqProduct: false })"), '打开弹窗时应显式默认不勾选同步 HQ')
    assert(editPageSource.includes('updateHqProduct: values.updateHqProduct ?? false'), '提交更新到分店时应传递手动勾选值')
    assert(editPageSource.includes('confirmUpdateToStorePrices'), '勾选同步 HQ 时应二次确认')
    assert(editPageSource.includes('getUpdateToStorePricesFailure'), '更新到分店失败时应解析后端统计 payload')
  })
  if (updateToStoreHqFailure) failures.push(updateToStoreHqFailure)

  const serviceContractFailure = await runTest('服务层应显式识别业务失败并保留 payload', () => {
    assert(serviceSource.includes('ensureHqProducts('), '服务层应导出 ensureHqProducts')
    assert(serviceSource.includes('/details/ensure-hq-products'), '服务层应调用商品级同步到 HQ 接口')
    assert(serviceSource.includes('assertApiSuccess'), '服务层应复用业务失败检查 helper')
    assert(serviceSource.includes("response.success === false || response.isSuccess === false"), '服务层应识别 success false')
  })
  if (serviceContractFailure) failures.push(serviceContractFailure)

  const originalFetch = globalThis.fetch
  let capturedUrl = ''
  let capturedInit: RequestInit | undefined

  const serviceSuccessFailure = await runTest('syncInvoicesFromHq 应调用页面专用接口并保留 payload', async () => {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input)
      capturedInit = init
      return new Response(JSON.stringify({
        success: true,
        data: {
          requestId: 'req-1',
          status: 'Succeeded',
          startedAt: '2026-05-01T00:00:00Z',
          completedAt: '2026-05-01T00:00:01Z',
          durationMs: 1000,
          invoiceAddedCount: 1,
          invoiceUpdatedCount: 2,
          detailAddedCount: 3,
          detailUpdatedCount: 4,
          totalProcessed: 10,
          errors: [],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    await syncInvoicesFromHq({
      selectedStoreCodes: ['S01'],
      startDate: '2026-05-01',
      endDate: '2026-05-31',
    })

    assertEqual(capturedUrl, '/api/react/v1/local-supplier-invoices/sync-from-hq', '应调用页面专用接口')
    assertEqual(capturedInit?.method, 'POST', '应使用 POST')
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { selectedStoreCodes: ['S01'], startDate: '2026-05-01', endDate: '2026-05-31' },
      '应保留同步请求 payload',
    )
  })
  if (serviceSuccessFailure) failures.push(serviceSuccessFailure)

  const serviceFailure = await runTest('syncInvoicesFromHq 遇到业务失败应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步失败：测试业务错误',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => syncInvoicesFromHq({ startDate: '2026-05-01' }),
      'HQ 同步失败：测试业务错误',
      '业务失败时应透传后端消息',
    )
  })
  if (serviceFailure) failures.push(serviceFailure)

  const serviceHttpFailure = await runTest('syncInvoicesFromHq 遇到 400 失败应保留统计 payload', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步部分失败',
      data: {
        requestId: 'req-failed',
        status: 'Failed',
        startedAt: '2026-05-01T00:00:00Z',
        completedAt: '2026-05-01T00:00:01Z',
        durationMs: 1000,
        invoiceAddedCount: 1,
        invoiceUpdatedCount: 0,
        detailAddedCount: 0,
        detailUpdatedCount: 0,
        totalProcessed: 1,
        errors: ['测试页失败'],
      },
    }), {
      status: 400,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRequestErrorPayload(
      () => syncInvoicesFromHq({ startDate: '2026-05-01' }),
      'HQ 同步部分失败',
      'HTTP 失败时应透传后端消息',
    )
  })
  if (serviceHttpFailure) failures.push(serviceHttpFailure)

  const ensureHqServiceSuccessFailure = await runTest('ensureHqProducts 应调用商品级接口并保留 payload', async () => {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input)
      capturedInit = init
      return new Response(JSON.stringify({
        success: true,
        data: {
          total: 2,
          hqExisting: 1,
          hbwebCreated: 1,
          hqCreated: 1,
          hqSynced: 2,
          hqPurchasePricesUpdated: 2,
          skipped: 0,
          failed: 0,
          errors: [],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    await ensureHqProducts('invoice-1', {
      detailGuids: ['detail-1', 'detail-2'],
      targetStoreCodes: ['1033'],
      idempotencyKey: 'idem-1',
    })

    assertEqual(capturedUrl, '/api/react/v1/local-supplier-invoices/invoice-1/details/ensure-hq-products', '应调用商品级同步到 HQ 接口')
    assertEqual(capturedInit?.method, 'POST', '商品同步到 HQ 应使用 POST')
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { detailGuids: ['detail-1', 'detail-2'], targetStoreCodes: ['1033'], idempotencyKey: 'idem-1' },
      '商品同步到 HQ 应保留请求 payload',
    )
  })
  if (ensureHqServiceSuccessFailure) failures.push(ensureHqServiceSuccessFailure)

  const updateToStorePayloadFailure = await runTest('updateToStorePrices 应传递 updateHqProduct 并保留结果', async () => {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input)
      capturedInit = init
      return new Response(JSON.stringify({
        success: true,
        data: {
          inserted: 0,
          updated: 1,
          failed: 0,
          hqPurchasePricesUpdated: 1,
          hqFailed: 0,
          hqErrors: [],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    const result = await updateToStorePrices({
      invoiceGuid: 'invoice-1',
      detailGuids: ['detail-1'],
      targetStoreCodes: ['1033'],
      updateFields: {
        updatePurchasePrice: true,
        updateRetailPrice: false,
        updateIsAutoPricing: false,
        updateIsSpecialProduct: false,
        updateDiscountRate: false,
      },
      updateHqProduct: true,
    })

    assertEqual(capturedUrl, '/api/react/v1/local-supplier-invoices/update-to-store-prices', '更新到分店接口地址应保持不变')
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      {
        invoiceGuid: 'invoice-1',
        detailGuids: ['detail-1'],
        targetStoreCodes: ['1033'],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: false,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false,
        },
        updateHqProduct: true,
      },
      '更新到分店应保留 updateHqProduct payload',
    )
    assertEqual(result.hqPurchasePricesUpdated, 1, '更新到分店结果应保留 HQ 更新统计')
  })
  if (updateToStorePayloadFailure) failures.push(updateToStorePayloadFailure)

  const updateToStoreFailurePayload = await runTest('updateToStorePrices 业务失败应保留 HQ 错误 payload', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '更新到分店部分失败',
      data: {
        inserted: 0,
        updated: 1,
        failed: 1,
        hqPurchasePricesUpdated: 1,
        hqFailed: 1,
        hqErrors: [{ detailGuid: 'detail-2', storeCode: '1033', message: '条码多匹配' }],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    try {
      await updateToStorePrices({
        invoiceGuid: 'invoice-1',
        detailGuids: ['detail-1', 'detail-2'],
        targetStoreCodes: ['1033'],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: false,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false,
        },
        updateHqProduct: true,
      })
      throw new Error('Expected updateToStorePrices to reject')
    } catch (error: any) {
      assertEqual(error.message, '更新到分店部分失败', '业务失败应透传后端消息')
      assertEqual(error.payload?.data?.hqFailed, 1, '业务失败应保留 HQ 失败统计')
      assertEqual(error.payload?.data?.hqErrors?.[0]?.message, '条码多匹配', '业务失败应保留 HQ 逐行错误')
    }
  })
  if (updateToStoreFailurePayload) failures.push(updateToStoreFailurePayload)

  const ensureHqServiceFailure = await runTest('ensureHqProducts 遇到业务失败应抛出后端消息并保留 payload', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '同步商品到HQ部分失败',
      data: {
        total: 1,
        hqExisting: 0,
        hbwebCreated: 0,
        hqCreated: 0,
        hqSynced: 0,
        hqPurchasePricesUpdated: 0,
        skipped: 0,
        failed: 1,
        errors: [{ detailGuid: 'detail-1', storeCode: '1033', message: '条码多匹配' }],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    try {
      await ensureHqProducts('invoice-1', { detailGuids: ['detail-1'], targetStoreCodes: ['1033'] })
      throw new Error('Expected ensureHqProducts to reject')
    } catch (error: any) {
      assertEqual(error.message, '同步商品到HQ部分失败', '业务失败应透传后端消息')
      assertEqual(error.payload?.data?.failed, 1, '业务失败应保留统计 payload')
      assertEqual(error.payload?.data?.errors?.[0]?.message, '条码多匹配', '业务失败应保留逐行错误')
    }
  })
  if (ensureHqServiceFailure) failures.push(ensureHqServiceFailure)

  globalThis.fetch = originalFetch

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('LocalSupplierInvoices.hqSync.logic.test: ok')
}

await main()
