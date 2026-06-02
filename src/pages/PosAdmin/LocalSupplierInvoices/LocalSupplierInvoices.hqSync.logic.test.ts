import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  batchExecuteActions,
  batchUpdateDetailAction,
  ensureHqProducts,
  syncInvoicesFromHq,
  updateDetailAction,
  updateHqProducts,
  updateToStorePrices,
} from '../../../services/localSupplierInvoiceService'
import {
  filterInvoiceDetails,
  getBarcodeStatusFilter,
  getDetailStatusStats,
  getProductStatusFilter,
  toggleStatusFilter,
} from './InvoiceEdit/statusFilters'
import { parsePasteText } from './InvoiceEdit/pasteDetails'
import {
  buildBatchExecuteConfirmText,
  constrainSelectedRowKeysToVisibleDetails,
  countSelectedBatchExecuteActions,
} from './InvoiceEdit/batchExecuteConfirm'
import { DetailAction, type LocalSupplierInvoiceItemDto } from '../../../types/localSupplierInvoice'

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
    assert(!typeSource.includes('updateHqProduct?: boolean'), '更新到分店请求不应再携带同步 HQ 开关')
    assert(typeSource.includes('UpdateHqProductsRequest'), '应声明字段级更新 HQ 请求类型')
    assert(typeSource.includes('updateFields: UpdateToStorePricesFields'), '字段级更新 HQ 请求应复用字段选择契约')
    assert(typeSource.includes('UpdateHqProductsResult'), '应声明字段级更新 HQ 结果类型')
    assert(typeSource.includes('hqRetailPricesUpdated?: number'), '字段级更新 HQ 结果应包含零售价更新计数')
    assert(typeSource.includes('hqDiscountRatesUpdated?: number'), '字段级更新 HQ 结果应包含折扣率更新计数')
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

  const editPageButtonFailure = await runTest('编辑页应使用专用权限显示更新HQ商品按钮', () => {
    assert(editPageSource.includes('canWriteLocalPurchaseToHq'), '编辑页应使用可编辑本地进货 + PushToHq 的组合权限控制写 HQ 入口')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsBtn', '更新HQ商品')"), '编辑页应显示更新HQ商品按钮文案')
    assert(editPageSource.includes('setHqUpdateVisible(true)'), '更新HQ商品按钮应打开独立弹窗')
    assert(editPageSource.includes('disabled={hqUpdateLoading || !selectedRowKeys.length}'), '更新HQ商品按钮必须要求已选择明细')
    assert(editPageSource.includes('handleUpdateHqProducts'), '编辑页应实现字段级更新HQ处理函数')
    assert(editPageSource.includes('updateHqProducts(invoiceGuid'), '编辑页应调用字段级更新 HQ 专用接口')
    assert(editPageSource.includes('if (hqUpdateLoading) return'), '更新HQ商品应阻止重复请求')
    assert(editPageSource.includes('hqUpdateIdempotencyKeyRef.current'), '更新HQ商品应在请求周期内复用稳定 idempotencyKey')
  })
  if (editPageButtonFailure) failures.push(editPageButtonFailure)

  const editPageImportFailure = await runTest('编辑页不应动态导入已静态使用的本地供应商进货单服务', () => {
    assert(
      !editPageSource.includes("await import('../../../../services/localSupplierInvoiceService')"),
      '编辑页已静态导入该服务，不能再动态导入同一模块，否则 Vite 会提示动态导入无法拆分 chunk',
    )
    assert(editPageSource.includes('updateDetailAction,'), '编辑页应静态导入更新行操作接口')
  })
  if (editPageImportFailure) failures.push(editPageImportFailure)

  const updateToStoreHqFailure = await runTest('更新到分店应移除同步HQ耦合并保留独立HQ弹窗', () => {
    assert(!editPageSource.includes('name="updateHqProduct"'), '更新到分店弹窗不应再包含 updateHqProduct 复选框')
    assert(!editPageSource.includes('confirmUpdateToStorePrices'), '更新到分店不应再包含同时更新 HQ 的二次确认')
    assert(!editPageSource.includes('showUpdateToStoreHqResult'), '更新到分店不应再展示 HQ 混合结果')
    assert(!editPageSource.includes('updateHqProduct:'), '更新到分店请求不应再传递 updateHqProduct')
    assert(editPageSource.includes('hqUpdateForm.validateFields()'), '更新HQ商品应校验独立弹窗表单')
    assert(editPageSource.includes('name="targetStoreCodes"'), '更新HQ商品弹窗应选择目标分店')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsTitle'"), '更新HQ商品应有独立弹窗标题')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsResultTitle'"), '更新HQ商品应有独立结果弹窗')
  })
  if (updateToStoreHqFailure) failures.push(updateToStoreHqFailure)

  const batchExecuteConfirmFailure = await runTest('批量执行操作应先展示二次确认并突出新建商品数量', () => {
    assert(editPageSource.includes('Modal.confirm({'), '批量执行操作应使用确认框')
    assert(editPageSource.includes('batchExecuteConfirmTitle'), '确认框应有专用标题文案')
    assert(editPageSource.includes('batchExecuteCreateProductNotice'), '确认框应包含新建商品风险提示文案')
    assert(editPageSource.includes('canRunGlobalLocalPurchaseBatchActions'), '批量执行入口应使用与后端全店访问一致的权限条件')
    assert(editPageSource.includes('okButtonProps: { danger: previewSnapshot.confirmedCreateProductCount > 0 }'), '存在新建商品时确认按钮应使用确认预览快照的危险态')
    assert(editPageSource.includes('constrainSelectedRowKeysToVisibleDetails(selectedRowKeys, filteredDetails)'), '批量执行前应收敛到当前可见选中明细')
    assert(
      editPageSource.indexOf('await updateDetailAction(invoiceGuid, detailGuid, action)') < editPageSource.indexOf('setRowActions((prev) => ({ ...prev, [detailGuid]: action }))'),
      '行操作类型应在服务端更新成功后再更新本地状态',
    )

    const counts = countSelectedBatchExecuteActions(
      ['d1', 'd2', 'd3'],
      [
        { detailGUID: 'd1', activityType: DetailAction.CreateProduct },
        { detailGUID: 'd2', activityType: DetailAction.UpdatePurchasePrice },
        { detailGUID: 'd3', activityType: DetailAction.WaitForOperation },
      ],
      { d2: DetailAction.CreateProduct },
    )
    assertEqual(counts.selectedCount, 3, '确认统计应包含选中条数')
    assertEqual(counts.createProductCount, 2, '确认统计应以 rowActions 覆盖后的操作类型计算新建商品数量')

    const confirmText = buildBatchExecuteConfirmText({
      ...counts,
      labels: {
        title: '确认执行批量操作？',
        content: '将对 {{count}} 条明细执行已设置的操作。',
        createProductNotice: '其中 {{count}} 条会新建商品，请确认货号、条码和名称无误。',
        okText: '确认执行',
        cancelText: '取消',
      },
    })
    assert(confirmText.content.includes('3 条明细'), '确认文案应展示执行条数')
    assert(confirmText.content.includes('2 条会新建商品'), '确认文案应展示新建商品数量')

    const visibleKeys = constrainSelectedRowKeysToVisibleDetails(
      ['d1', 'd2', 'hidden'],
      [
        { detailGUID: 'd1' },
        { detailGUID: 'd2' },
      ],
    )
    assertDeepEqual(visibleKeys.map(String), ['d1', 'd2'], '筛选后隐藏明细不应继续参与批量执行')
  })
  if (batchExecuteConfirmFailure) failures.push(batchExecuteConfirmFailure)

  const editPageStatsFailure = await runTest('编辑页应提供状态统计栏并支持点击叠加过滤', () => {
    assert(editPageSource.includes("useState<StatusFilterValue<ProductStatusFilter>>('all')"), '编辑页应维护商品状态过滤状态')
    assert(editPageSource.includes("useState<StatusFilterValue<BarcodeStatusFilter>>('all')"), '编辑页应维护条码状态过滤状态')
    assert(editPageSource.includes('getDetailStatusStats(details)'), '编辑页应基于全部 details 计算状态统计')
    assert(editPageSource.includes('filterInvoiceDetails(details'), '编辑页过滤链应委托行为级纯函数')
    assert(editPageSource.includes('[details, searchText, priceFilter, productStatusFilter, barcodeStatusFilter]'), '过滤结果应依赖搜索、涨跌和状态过滤，按 AND 叠加')
    assert(editPageSource.includes("toggleStatusFilter(productStatusFilter, 'exists')"), '再次点击同一商品状态标签应取消过滤')
    assert(editPageSource.includes("toggleStatusFilter(barcodeStatusFilter, 'normal')"), '再次点击同一条码状态标签应取消过滤')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.statusStatsTitle', '状态统计')"), '页面应显示状态统计栏标题')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.statusStatsAll', '全部 {{count}}'"), '页面应提供全部状态标签以清除状态过滤')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.productStatusLabel', '商品状态')"), '页面应显示商品状态分组标题')
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.barcodeStatusLabel', '条码状态')"), '页面应显示条码状态分组标题')
    assert(editPageSource.includes('statusStatsTagColors'), '状态统计标签应使用显式语义色配置')
    assert(editPageSource.includes("product: { all: 'blue', notDetected: 'purple', exists: 'green', notExists: 'red' }"), '商品状态标签应使用不同颜色')
    assert(editPageSource.includes("barcode: { all: 'geekblue', notDetected: 'purple', normal: 'cyan', noMatch: 'volcano', multiMatch: 'orange' }"), '条码状态标签应使用不同颜色')
    assert(editPageSource.includes('getStatusStatsTagStyle('), '状态统计标签应使用独立选中态样式')
    assert(editPageSource.includes('/* 状态统计栏：数量按全部明细计算，点击后与搜索和涨跌筛选叠加。 */'), '状态统计栏应位于明细卡片内容区、工具栏按钮上方')
    assert(editPageSource.includes('productNameCellStyle'), '商品名称列应使用专用换行样式')
    assert(editPageSource.includes('WebkitLineClamp: 2'), '商品名称列应最多自动换行 2 行')
  })
  if (editPageStatsFailure) failures.push(editPageStatsFailure)

  const editPageStatsBehaviorFailure = await runTest('状态统计和过滤应按真实明细行为计算', () => {
    const details: LocalSupplierInvoiceItemDto[] = [
      {
        detailGUID: 'not-detected',
        itemNumber: 'CARD-001',
        barcode: '111',
        productName: 'Birthday Card',
        lastPurchasePrice: 1,
        purchasePrice: 2,
      },
      {
        detailGUID: 'exists-normal',
        itemNumber: 'BAG-002',
        barcode: '222',
        productName: 'Gift Bag',
        storeProductCode: 'STORE-002',
        existingProductCount: 2,
        barcodeStatus: 1,
        barcodeMatchCount: 1,
        lastPurchasePrice: 5,
        purchasePrice: 4,
      },
      {
        detailGUID: 'not-exists-no-match',
        itemNumber: 'WRAP-003',
        barcode: '333',
        productName: 'Wrap',
        existingProductCount: 0,
        barcodeStatus: 2,
        barcodeMatchCount: 0,
        lastPurchasePrice: 1,
        purchasePrice: 1.5,
      },
      {
        detailGUID: 'exists-multi-match',
        itemNumber: 'CARD-004',
        barcode: '444',
        productName: 'Birthday Card Multi',
        existingProductCount: 1,
        barcodeStatus: 2,
        barcodeMatchCount: 3,
        lastPurchasePrice: 2,
        purchasePrice: 3,
      },
    ]

    assertEqual(getProductStatusFilter(details[0]), 'notDetected', '未检测商品状态应来自空 existingProductCount')
    assertEqual(getProductStatusFilter(details[1]), 'exists', '已存在商品状态应来自 existingProductCount > 0')
    assertEqual(getProductStatusFilter(details[2]), 'notExists', '不存在商品状态应来自 existingProductCount = 0')
    assertEqual(getBarcodeStatusFilter(details[0]), 'notDetected', '未检测条码状态应来自空 barcodeStatus')
    assertEqual(getBarcodeStatusFilter(details[1]), 'normal', '正常条码状态应来自 barcodeStatus = 1')
    assertEqual(getBarcodeStatusFilter(details[2]), 'noMatch', '无匹配条码状态应来自异常且匹配数为 0')
    assertEqual(getBarcodeStatusFilter(details[3]), 'multiMatch', '多匹配条码状态应来自异常且匹配数大于 0')

    assertDeepEqual(
      getDetailStatusStats(details),
      {
        product: { notDetected: 1, exists: 2, notExists: 1 },
        barcode: { notDetected: 1, normal: 1, noMatch: 1, multiMatch: 1 },
      },
      '状态统计应基于全部明细计算',
    )

    const filtered = filterInvoiceDetails(details, {
      searchText: 'card',
      priceFilter: 'up',
      productStatusFilter: 'exists',
      barcodeStatusFilter: 'multiMatch',
    })
    assertDeepEqual(filtered.map((item) => item.detailGUID), ['exists-multi-match'], '搜索、涨价、商品状态、条码状态应按 AND 叠加')
    assertEqual(getDetailStatusStats(filtered).product.exists, 1, '过滤结果可单独统计，但页面全量统计不应依赖过滤结果')
    assertEqual(toggleStatusFilter('exists', 'exists'), 'all', '再次点击同一商品状态应取消过滤')
    assertEqual(toggleStatusFilter('all', 'normal'), 'normal', '从全部点击某个条码状态应启用过滤')
  })
  if (editPageStatsBehaviorFailure) failures.push(editPageStatsBehaviorFailure)

  const pastePriceParseFailure = await runTest('粘贴解析应识别带货币符号的本次进货价', () => {
    const [row] = parsePasteText('WEW1272\t9313559661518\tFolded Wrap\t15\tA$1.25\t$3.50\tAUD 2.99')

    assertEqual(row.itemNumber, 'WEW1272', '应保留货号')
    assertEqual(row.quantity, 15, '应解析数量')
    assertEqual(row.purchasePrice, 1.25, '应解析带 A$ 的本次进货价')
    assertEqual(row.newAutoRetailPrice, 3.5, '应解析带 $ 的新自动零售价')
    assertEqual(row.retailPrice, 2.99, '应解析带 AUD 的零售价')
  })
  if (pastePriceParseFailure) failures.push(pastePriceParseFailure)

  const serviceContractFailure = await runTest('服务层应显式识别业务失败并保留 payload', () => {
    assert(serviceSource.includes('ensureHqProducts('), '服务层应导出 ensureHqProducts')
    assert(serviceSource.includes('/details/ensure-hq-products'), '服务层应调用商品级同步到 HQ 接口')
    assert(serviceSource.includes('updateHqProducts('), '服务层应导出字段级更新 HQ 接口')
    assert(serviceSource.includes('/details/update-hq-products'), '服务层应调用字段级更新 HQ 专用接口')
    assert(serviceSource.includes('assertApiSuccess'), '服务层应复用业务失败检查 helper')
    assert(serviceSource.includes("response.success === false || response.isSuccess === false"), '服务层应识别 success false')
    assert(serviceSource.includes("assertApiSuccess(response, '批量执行操作失败')"), '批量执行服务层应识别业务失败')
    assert(serviceSource.includes("assertApiSuccess(response, '批量设置操作类型失败')"), '批量设置操作类型服务层应识别业务失败')
    assert(serviceSource.includes("assertApiSuccess(response, '更新操作类型失败')"), '行操作类型服务层应识别业务失败')
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

  const updateToStorePayloadFailure = await runTest('updateToStorePrices 不应传递 updateHqProduct', async () => {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input)
      capturedInit = init
      return new Response(JSON.stringify({
        success: true,
        data: {
          inserted: 0,
          updated: 1,
          failed: 0,
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
      },
      '更新到分店不应携带 updateHqProduct payload',
    )
    assertEqual(result.updated, 1, '更新到分店结果应保留分店更新统计')
  })
  if (updateToStorePayloadFailure) failures.push(updateToStorePayloadFailure)

  const updateHqProductsPayloadFailure = await runTest('updateHqProducts 应调用字段级更新 HQ 专用接口并保留 payload', async () => {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input)
      capturedInit = init
      return new Response(JSON.stringify({
        success: true,
        data: {
          total: 2,
          updated: 2,
          failed: 0,
          hqPurchasePricesUpdated: 2,
          hqRetailPricesUpdated: 2,
          hqAutoPricingUpdated: 0,
          hqSpecialProductsUpdated: 0,
          hqDiscountRatesUpdated: 0,
          errors: [],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }) as typeof fetch

    const result = await updateHqProducts('invoice-1', {
      detailGuids: ['detail-1', 'detail-2'],
      targetStoreCodes: ['1033', '1005'],
      updateFields: {
        updatePurchasePrice: true,
        updateRetailPrice: true,
        updateIsAutoPricing: false,
        updateIsSpecialProduct: false,
        updateDiscountRate: false,
      },
      idempotencyKey: 'hq-update-1',
    })

    assertEqual(capturedUrl, '/api/react/v1/local-supplier-invoices/invoice-1/details/update-hq-products', '应调用字段级更新 HQ 专用接口')
    assertEqual(capturedInit?.method, 'POST', '字段级更新 HQ 应使用 POST')
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      {
        detailGuids: ['detail-1', 'detail-2'],
        targetStoreCodes: ['1033', '1005'],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: true,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false,
        },
        idempotencyKey: 'hq-update-1',
      },
      '字段级更新 HQ 应保留请求 payload',
    )
    assertEqual(result.hqRetailPricesUpdated, 2, '字段级更新 HQ 应保留零售价更新统计')
  })
  if (updateHqProductsPayloadFailure) failures.push(updateHqProductsPayloadFailure)

  const batchExecuteBusinessFailure = await runTest('batchExecuteActions 遇到业务失败应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '批量执行业务失败',
      data: {
        createdProducts: 0,
        updatedPurchasePrices: 0,
        updatedItemNumbers: 0,
        addedMultiCodes: 0,
        skipped: 0,
        failed: 1,
        errors: ['测试执行失败'],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => batchExecuteActions({
        invoiceGuid: 'invoice-1',
        detailGuids: ['detail-1'],
        expectedActions: [
          { detailGuid: 'detail-1', action: 1, activityType: 1 },
        ],
        confirmedCreateProductCount: 1,
        confirmedAt: '2026-06-02T09:30:00.000Z',
      }),
      '批量执行业务失败',
      '批量执行遇到 success=false 时应透传后端消息',
    )
  })
  if (batchExecuteBusinessFailure) failures.push(batchExecuteBusinessFailure)

  const updateDetailActionBusinessFailure = await runTest('updateDetailAction 遇到业务失败应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '更新操作类型业务失败',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => updateDetailAction('invoice-1', 'detail-1', DetailAction.CreateProduct),
      '更新操作类型业务失败',
      '行操作类型更新遇到 success=false 时应透传后端消息',
    )
  })
  if (updateDetailActionBusinessFailure) failures.push(updateDetailActionBusinessFailure)

  const batchUpdateDetailActionBusinessFailure = await runTest('batchUpdateDetailAction 遇到业务失败应抛出后端消息', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '批量设置操作类型业务失败',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    await assertRejects(
      () => batchUpdateDetailAction('invoice-1', ['detail-1'], DetailAction.CreateProduct),
      '批量设置操作类型业务失败',
      '批量操作类型更新遇到 success=false 时应透传后端消息',
    )
  })
  if (batchUpdateDetailActionBusinessFailure) failures.push(batchUpdateDetailActionBusinessFailure)

  const updateHqProductsFailurePayload = await runTest('updateHqProducts 业务失败应保留 HQ 错误 payload', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({
      success: false,
      message: '更新HQ商品部分失败',
      data: {
        total: 2,
        updated: 1,
        failed: 1,
        hqPurchasePricesUpdated: 1,
        hqRetailPricesUpdated: 0,
        errors: [{ detailGuid: 'detail-2', storeCode: '1033', message: '条码多匹配' }],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })) as typeof fetch

    try {
      await updateHqProducts('invoice-1', {
        detailGuids: ['detail-1', 'detail-2'],
        targetStoreCodes: ['1033'],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: false,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false,
        },
      })
      throw new Error('Expected updateHqProducts to reject')
    } catch (error: any) {
      assertEqual(error.message, '更新HQ商品部分失败', '业务失败应透传后端消息')
      assertEqual(error.payload?.data?.failed, 1, '业务失败应保留 HQ 失败统计')
      assertEqual(error.payload?.data?.errors?.[0]?.message, '条码多匹配', '业务失败应保留 HQ 逐行错误')
    }
  })
  if (updateHqProductsFailurePayload) failures.push(updateHqProductsFailurePayload)

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
