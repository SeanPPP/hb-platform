import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  buildPasteSubmitItems,
  createPastePreviewItems,
  filterPastePreviewItems,
  formatPastePreviewQuantity,
  parseStoreOrderPasteRows,
  setExistingPastePreviewAction,
  type ExistingStoreOrderPasteLine,
  type StoreOrderPastePreviewItem,
} from './pastePreview'
import {
  applyPasteOptimisticRowsToDetail,
  buildPasteOptimisticRows,
  resolvePasteOptimisticPendingAfterJob,
} from './pasteOptimisticRows'
import type { StoreOrderBatchLookupItem, StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const packageFile = path.resolve(process.cwd(), 'package.json')
const zhLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const enLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')
const detailSource = readFileSync(detailFile, 'utf8')
const packageSource = readFileSync(packageFile, 'utf8')
const zhLocaleSource = readFileSync(zhLocaleFile, 'utf8')
const enLocaleSource = readFileSync(enLocaleFile, 'utf8')

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`)
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

const lookupRows: StoreOrderBatchLookupItem[] = [
  {
    lookupCode: 'HB001',
    product: {
      productCode: 'P001',
      itemNumber: 'HB001',
      productName: 'Existing Product',
      minOrderQuantity: 1,
      stockQuantity: 0,
      isInStock: false,
    },
  },
  {
    lookupCode: 'HB002',
    product: {
      productCode: 'P002',
      itemNumber: 'HB002',
      productName: 'New Product',
      minOrderQuantity: 1,
      stockQuantity: 0,
      isInStock: false,
    },
  },
]

const existingLines: ExistingStoreOrderPasteLine[] = [
  {
    productCode: 'P001',
    quantity: 3,
    allocQuantity: 5,
  },
]

async function main() {
  const failures: string[] = []

  const parseFailure = await runTest('粘贴解析应把空数量按 1 处理并保留其他格式错误异常行', () => {
    const rows = parseStoreOrderPasteRows('HB001\t10\nHB002\t\nHB003\t0\nHB004\t-2\nHB005\tabc\nHB006\t12abc\nHB007\t1.5', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })

    assertEqual(rows.length, 7, '解析应保留全部非空货号行')
    assertEqual(rows[0].quantityValid, true, '正数数量应有效')
    assertEqual(rows[1].quantity, 1, '空数量应默认 1')
    assertEqual(rows[1].quantityValid, true, '空数量应按默认 1 视为有效')
    assertEqual(rows[2].quantityValid, false, '0 数量应无效')
    assertEqual(rows[3].quantityValid, false, '负数数量应无效')
    assertEqual(rows[4].quantityValid, false, '非数字数量应无效')
    assertEqual(rows[5].quantityValid, false, '带数字前缀的格式错误数量应无效')
    assertEqual(rows[6].quantityValid, false, '小数数量应无效')
  })
  if (parseFailure) failures.push(parseFailure)

  const leadingEmptyColumnFailure = await runTest('粘贴解析应保留 Excel 前置空列以匹配列映射', () => {
    const rows = parseStoreOrderPasteRows('\tHB001\t10', {
      itemNumber: 1,
      quantity: 2,
      price: -1,
    })

    assertEqual(rows.length, 1, '前置空列不应导致整行被跳过')
    assertEqual(rows[0].itemNumber, 'HB001', '货号应按映射读取第二列')
    assertEqual(rows[0].quantity, 10, '数量应按映射读取第三列')
    assertEqual(rows[0].quantityValid, true, '前置空列不应影响数量校验')
  })
  if (leadingEmptyColumnFailure) failures.push(leadingEmptyColumnFailure)

  const missingQuantityDefaultFailure = await runTest('只有货号没有数量列内容时应默认数量 1 并可导入', () => {
    const rows = parseStoreOrderPasteRows('HB001', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(rows, lookupRows, existingLines)

    assertEqual(rows.length, 1, '只有货号的行也应被解析')
    assertEqual(rows[0].quantity, 1, '缺失数量列内容应默认 1')
    assertEqual(rows[0].quantityValid, true, '缺失数量应视为有效')
    assertEqual(preview[0].valid, true, '匹配到商品后应可导入')
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: 'P001', quantity: 1, action: 'replace' }],
      '提交 payload 应使用默认数量 1',
    )
  })
  if (missingQuantityDefaultFailure) failures.push(missingQuantityDefaultFailure)

  const quantityDisplayFailure = await runTest('异常数量预览应展示原始 Excel 单元格值，空数量显示默认 1', () => {
    const rows = parseStoreOrderPasteRows('HB001\tabc\nHB002\t0\nHB003\t', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(rows, lookupRows, existingLines)

    assertEqual(formatPastePreviewQuantity(preview[0]), 'abc', '非数字异常应展示原始值')
    assertEqual(formatPastePreviewQuantity(preview[1]), '0', '0 数量异常应展示原始值')
    assertEqual(formatPastePreviewQuantity(preview[2]), 1, '空数量应显示默认 1')
  })
  if (quantityDisplayFailure) failures.push(quantityDisplayFailure)

  const previewFailure = await runTest('预览应标记新增、已存在、数量异常和未匹配状态', () => {
    const parsedRows = parseStoreOrderPasteRows('HB001\t10\nHB002\t4\nHB003\t0\nHB404\t7', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(parsedRows, lookupRows, existingLines)

    assertEqual(preview[0].status, 'existing', '已存在商品应标记 existing')
    assertEqual(preview[0].action, 'replace', '已存在商品默认覆盖')
    assertEqual(preview[0].existingQuantity, 3, '已存在商品应带订货数量')
    assertEqual(preview[0].existingAllocQuantity, 5, '已存在商品应带发货数量')
    assertEqual(preview[1].status, 'new', '未在订单中的匹配商品应标记新增')
    assertEqual(preview[2].status, 'invalidQuantity', '数量异常优先展示异常状态')
    assertEqual(preview[3].status, 'unmatched', '未匹配商品应标记 unmatched')
    assertEqual(preview.filter((item) => item.valid).length, 2, '只有新增和已存在有效行可导入')
  })
  if (previewFailure) failures.push(previewFailure)

  const filterFailure = await runTest('预览筛选应支持全部、可导入、异常、未匹配、已存在', () => {
    const parsedRows = parseStoreOrderPasteRows('HB001\t10\nHB002\t4\nHB003\t0\nHB404\t7', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(parsedRows, lookupRows, existingLines)

    assertEqual(filterPastePreviewItems(preview, 'all').length, 4, '全部筛选应返回所有行')
    assertEqual(filterPastePreviewItems(preview, 'importable').length, 2, '可导入筛选应返回有效行')
    assertEqual(filterPastePreviewItems(preview, 'invalid').length, 1, '异常筛选应返回数量异常行')
    assertEqual(filterPastePreviewItems(preview, 'unmatched').length, 1, '未匹配筛选应返回未匹配行')
    assertEqual(filterPastePreviewItems(preview, 'existing').length, 1, '已存在筛选应返回已存在行')
  })
  if (filterFailure) failures.push(filterFailure)

  const submitFailure = await runTest('提交项应携带逐行动作并过滤异常、未匹配和跳过行', () => {
    const parsedRows = parseStoreOrderPasteRows('HB001\t10\nHB002\t4\nHB003\t0\nHB404\t7', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = setExistingPastePreviewAction(
      createPastePreviewItems(parsedRows, lookupRows, existingLines),
      'append',
    ).map((item) => (item.product?.productCode === 'P002' ? { ...item, action: 'skip' as const } : item))

    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: 'P001', quantity: 10, action: 'append' }],
      '提交 payload 应只包含有效且未跳过的行，并保留 action',
    )
  })
  if (submitFailure) failures.push(submitFailure)

  const innerQuantitySubmitFailure = await runTest('inner 模式提交数量应乘商品中包数/最小订货量', () => {
    const parsedRows = parseStoreOrderPasteRows('HB-INNER\t2', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: 'HB-INNER',
          product: {
            productCode: 'P-INNER',
            itemNumber: 'HB-INNER',
            productName: 'Inner Product',
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false,
          },
        },
      ],
      [],
    )

    assertDeepEqual(
      buildPasteSubmitItems(preview, { quantityMode: 'inner' }),
      [{ productCode: 'P-INNER', quantity: 24, action: 'replace' }],
      'inner 模式应把 Excel 数量 2 换算成 24 后提交',
    )
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: 'P-INNER', quantity: 2, action: 'replace' }],
      '默认模式仍应保持原始 Excel 数量',
    )
    assertEqual(formatPastePreviewQuantity(preview[0], 'inner'), 24, 'inner 模式预览应显示最终写入数量')
  })
  if (innerQuantitySubmitFailure) failures.push(innerQuantitySubmitFailure)

  const innerMissingQuantitySubmitFailure = await runTest('inner 模式遇到空数量应先默认 1 再乘商品中包数', () => {
    const parsedRows = parseStoreOrderPasteRows('HB-INNER\t', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: 'HB-INNER',
          product: {
            productCode: 'P-INNER',
            itemNumber: 'HB-INNER',
            productName: 'Inner Product',
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false,
          },
        },
      ],
      [],
    )

    assertEqual(parsedRows[0].quantity, 1, '空数量应先按 1 解析')
    assertDeepEqual(
      buildPasteSubmitItems(preview, { quantityMode: 'inner' }),
      [{ productCode: 'P-INNER', quantity: 12, action: 'replace' }],
      'inner 模式应把默认数量 1 换算成 12 后提交',
    )
    assertEqual(formatPastePreviewQuantity(preview[0], 'inner'), 12, 'inner 模式预览应显示默认 1 换算后的最终数量')
  })
  if (innerMissingQuantitySubmitFailure) failures.push(innerMissingQuantitySubmitFailure)

  const innerQuantityFallbackFailure = await runTest('inner 模式遇到无效中包数应按 1 回退', () => {
    const baseItem: Omit<StoreOrderPastePreviewItem, 'product'> = {
      rowIndex: 0,
      itemNumber: 'HB-FALLBACK',
      quantity: 3,
      quantityValid: true,
      valid: true,
      status: 'new',
      action: 'replace',
    }
    const buildItem = (minOrderQuantity: number | undefined): StoreOrderPastePreviewItem => ({
      ...baseItem,
      product: {
        productCode: `P-FALLBACK-${String(minOrderQuantity)}`,
        itemNumber: 'HB-FALLBACK',
        productName: 'Fallback Product',
        minOrderQuantity: minOrderQuantity as unknown as number,
        stockQuantity: 0,
        isInStock: false,
      },
    })

    const invalidItems = [buildItem(0), buildItem(1), buildItem(undefined), buildItem(Number.NaN)]

    assertDeepEqual(
      buildPasteSubmitItems(invalidItems, { quantityMode: 'inner' }).map((item) => item.quantity),
      [3, 3, 3, 3],
      '0、1、空值和 NaN 都应按 1 回退，避免提交非法数量',
    )
  })
  if (innerQuantityFallbackFailure) failures.push(innerQuantityFallbackFailure)

  const optimisticRowsFailure = await runTest('乐观预览行应按覆盖/追加生成临时订单明细', () => {
    const parsedRows = parseStoreOrderPasteRows('HB001\t10\t1.5\nHB002\t4\t2.5', {
      itemNumber: 0,
      quantity: 1,
      price: 2,
    })
    const preview = setExistingPastePreviewAction(
      createPastePreviewItems(parsedRows, lookupRows, existingLines),
      'append',
    )
    const currentItems: StoreOrderDetailLine[] = [
      {
        detailGUID: 'detail-1',
        productCode: 'P001',
        itemNumber: 'HB001',
        productName: 'Existing Product',
        quantity: 3,
        allocQuantity: 5,
        price: 9,
        amount: 27,
        importPrice: 1,
        importAmount: 5,
        minOrderQuantity: 1,
        isActive: true,
      },
    ]

    const rows = buildPasteOptimisticRows({
      currentItems,
      previewItems: preview,
      targetField: 'allocQuantity',
    })

    assertEqual(rows.length, 2, '当前页行和本次新增有效行都应显示')
    assertEqual(rows[0].detailGUID, 'detail-1', '已有行应保留真实 detailGUID')
    assertEqual(rows[0].quantity, 3, '写入发货数量时不应改已有订货数量')
    assertEqual(rows[0].allocQuantity, 15, '追加动作应基于当前发货数量累加')
    assertEqual(rows[0].importPrice, 1.5, '粘贴价格应临时反映到进货价')
    assertEqual(rows[1].productCode, 'P002', '新增有效行应合成临时明细')
    assertEqual(rows[1].allocQuantity, 4, '新增有效行应写入目标发货数量')
    assert(rows[1].detailGUID.startsWith('optimistic-paste-'), '新增行应使用临时 detailGUID')
  })
  if (optimisticRowsFailure) failures.push(optimisticRowsFailure)

  const optimisticInnerQuantityFailure = await runTest('inner 模式乐观预览数量应和提交数量一致', () => {
    const parsedRows = parseStoreOrderPasteRows('HB-INNER\t2', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: 'HB-INNER',
          product: {
            productCode: 'P-INNER',
            itemNumber: 'HB-INNER',
            productName: 'Inner Product',
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false,
          },
        },
      ],
      [],
    )

    const rows = buildPasteOptimisticRows({
      currentItems: [],
      previewItems: preview,
      targetField: 'allocQuantity',
      quantityMode: 'inner',
    })
    const [submitItem] = buildPasteSubmitItems(preview, { quantityMode: 'inner' })

    assertEqual(rows[0].allocQuantity, submitItem.quantity, '乐观发货数量必须和提交 payload 数量一致')
    assertEqual(formatPastePreviewQuantity(preview[0], 'inner'), submitItem.quantity, '预览展示数量也应和提交 payload 一致')
  })
  if (optimisticInnerQuantityFailure) failures.push(optimisticInnerQuantityFailure)

  const optimisticDetailTotalsFailure = await runTest('乐观预览只替换表格行不覆盖服务器整单合计', () => {
    const originalDetail: StoreOrderDetail = {
      orderGUID: 'order-1',
      totalAmount: 999,
      totalQuantity: 100,
      totalImportAmount: 888,
      totalVolume: 77,
      totalAllocQuantity: 66,
      totalSKU: 55,
      itemsTotal: 44,
      items: [],
    }
    const optimisticRows: StoreOrderDetailLine[] = [
      {
        detailGUID: 'optimistic-paste-1',
        productCode: 'P001',
        itemNumber: 'HB001',
        productName: 'Preview Product',
        quantity: 1,
        allocQuantity: 2,
        price: 3,
        amount: 6,
        importPrice: 4,
        importAmount: 8,
        minOrderQuantity: 1,
        isActive: true,
      },
    ]

    const nextDetail = applyPasteOptimisticRowsToDetail(originalDetail, optimisticRows)

    assertEqual(nextDetail.items, optimisticRows, '临时预览应替换当前表格行')
    assertEqual(nextDetail.itemsTotal, 44, '远程分页总数应保持服务器真实值')
    assertEqual(nextDetail.totalQuantity, 100, '整单订货数量应保持服务器真实值')
    assertEqual(nextDetail.totalAllocQuantity, 66, '整单发货数量应保持服务器真实值')
    assertEqual(nextDetail.totalImportAmount, 888, '整单金额应保持服务器真实值')
    assertEqual(nextDetail.totalSKU, 55, '整单 SKU 数应保持服务器真实值')
  })
  if (optimisticDetailTotalsFailure) failures.push(optimisticDetailTotalsFailure)

  const optimisticPendingFailure = await runTest('乐观预览 pending 应在成功或失败终态清理', () => {
    const pending = { jobId: 'job-1', orderGUID: 'order-1' }

    assertDeepEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: 'job-1', status: 'Running' }),
      pending,
      '运行中状态不应清理 pending',
    )
    assertEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: 'job-1', status: 'Succeeded' }),
      null,
      '成功终态应清理 pending',
    )
    assertEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: 'job-1', status: 'Failed' }),
      null,
      '失败终态应清理 pending',
    )
    assertDeepEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: 'job-2', status: 'Succeeded' }),
      pending,
      '其它 job 终态不应清理当前 pending',
    )
  })
  if (optimisticPendingFailure) failures.push(optimisticPendingFailure)

  const detailUiFailure = await runTest('详情页粘贴预览应不分页并提供筛选和批量逐条动作', () => {
    assert(detailSource.includes('buildPasteSubmitItems') && detailSource.includes("from './pastePreview'"), '详情页应复用 pastePreview helper 生成提交项')
    assert(detailSource.includes('pagination={false}'), '粘贴预览表格应关闭分页')
    assert(
      !detailSource.includes('pagination={{ pageSize: 8, hideOnSinglePage: true }}'),
      '粘贴预览表格不应保留每页 8 行分页',
    )
    assert(detailSource.includes("key: 'rowIndex'"), '粘贴预览表格应提供行号列')
    assert(detailSource.includes('record.rowIndex + 1'), '行号列应显示 Excel 原始行号')
    assert(detailSource.includes('pastePreviewFilter'), '详情页应维护粘贴预览筛选状态')
    assert(detailSource.includes('setExistingPastePreviewAction'), '详情页应提供已存在行批量设置动作')
    assert(detailSource.includes('handleChangePastePreviewAction'), '详情页应支持逐行修改动作')
    assert(detailSource.includes("dataIndex: 'action'"), '粘贴预览表格应展示行级操作列')
    assert(detailSource.includes('getStoreOrderDetailFull(detail.orderGUID)'), '解析时应加载整单明细判断已存在商品')
    assert(detailSource.includes('createStoreOrderPasteReplaceJob'), '导入确认应创建后端后台 job')
    assert(detailSource.includes('getStoreOrderPasteReplaceJob'), '导入确认应轮询后端 job 状态')
    assert(detailSource.includes('createStoreOrderPasteReplaceJobPoller'), '详情页应使用独立粘贴导入 poller')
    assert(detailSource.includes('stopPasteReplacePollingRef.current?.()'), '详情页卸载或切换订单时应清理导入轮询')
    assert(detailSource.includes('notification.success'), '导入完成应使用右上角 notification 提示')
    assert(detailSource.includes('buildPasteOptimisticRows'), '导入任务提交成功后应先生成临时预览明细')
    assert(detailSource.includes('pasteOptimisticPending'), '详情页应维护 Excel 粘贴临时预览 pending 状态')
    assert(detailSource.includes('applyPasteOptimisticRowsToDetail'), '临时预览应只替换表格行并保留服务器合计')
    assert(detailSource.includes('isPasteOptimisticPreviewActive'), '临时预览期间应禁用依赖真实明细的编辑入口')
    assert(detailSource.includes('已先显示本次 Excel 预览'), '详情页应展示临时预览友好说明')
    assert(detailSource.includes('resolvePasteOptimisticPendingAfterJob'), '详情页应在任务终态清理临时预览 pending 状态')
  })
  if (detailUiFailure) failures.push(detailUiFailure)

  const innerTargetUiFailure = await runTest('详情页应提供发货数量按 inner 写入目标并映射回后端发货字段', () => {
    assert(detailSource.includes("type StoreOrderPasteWriteTarget = StoreOrderPasteTargetField | 'allocQuantityByInner'"), '详情页应使用本地 UI 写入目标扩展 inner 选项')
    assert(detailSource.includes('resolvePasteTargetField(writeTarget: StoreOrderPasteWriteTarget): StoreOrderPasteTargetField'), '详情页应把 UI 写入目标转换成后端目标字段')
    assert(detailSource.includes("writeTarget === 'allocQuantityByInner' ? 'allocQuantity' : writeTarget"), 'inner 写入目标应映射为后端 allocQuantity')
    assert(detailSource.includes("writeTarget === 'allocQuantityByInner' ? 'inner' : 'direct'"), 'inner 写入目标应启用提交数量换算')
    assert(detailSource.includes('<Radio value="allocQuantityByInner">'), '弹窗写入目标应展示按 inner 的发货数量选项')
    assert(detailSource.includes("t('storeOrders.detail.allocQuantityByInnerHelp')"), '按 inner 选项应有友好说明')
  })
  if (innerTargetUiFailure) failures.push(innerTargetUiFailure)

  const defaultQuantityCopyFailure = await runTest('中英文粘贴文案应说明空数量默认 1', () => {
    assert(zhLocaleSource.includes('数量为空时默认 1'), '中文文案应说明空数量默认 1')
    assert(enLocaleSource.includes('blank quantity defaults to 1'), '英文文案应说明 blank quantity defaults to 1')
  })
  if (defaultQuantityCopyFailure) failures.push(defaultQuantityCopyFailure)

  const packageFailure = await runTest('store-order-detail 测试脚本应接入粘贴预览测试', () => {
    assert(packageSource.includes('src/pages/Warehouse/StoreOrders/pastePreview.test.ts'), 'test:store-order-detail 应运行 pastePreview.test.ts')
    assert(packageSource.includes('src/pages/Warehouse/StoreOrders/pasteReplaceJobPolling.test.ts'), 'test:store-order-detail 应运行 pasteReplaceJobPolling.test.ts')
  })
  if (packageFailure) failures.push(packageFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('pastePreview.test: ok')
}

await main()
