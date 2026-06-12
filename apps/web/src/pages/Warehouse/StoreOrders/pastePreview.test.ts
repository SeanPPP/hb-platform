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
} from './pastePreview'
import type { StoreOrderBatchLookupItem } from '../../../types/storeOrder'

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const packageFile = path.resolve(process.cwd(), 'package.json')
const detailSource = readFileSync(detailFile, 'utf8')
const packageSource = readFileSync(packageFile, 'utf8')

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

  const parseFailure = await runTest('粘贴解析应保留空数量、0、负数和格式错误异常行但不允许导入', () => {
    const rows = parseStoreOrderPasteRows('HB001\t10\nHB002\t\nHB003\t0\nHB004\t-2\nHB005\tabc\nHB006\t12abc\nHB007\t1.5', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })

    assertEqual(rows.length, 7, '解析应保留全部非空货号行')
    assertEqual(rows[0].quantityValid, true, '正数数量应有效')
    assertEqual(rows[1].quantityValid, false, '空数量应无效')
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

  const quantityDisplayFailure = await runTest('异常数量预览应展示原始 Excel 单元格值', () => {
    const rows = parseStoreOrderPasteRows('HB001\tabc\nHB002\t0\nHB003\t', {
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    const preview = createPastePreviewItems(rows, lookupRows, existingLines)

    assertEqual(formatPastePreviewQuantity(preview[0]), 'abc', '非数字异常应展示原始值')
    assertEqual(formatPastePreviewQuantity(preview[1]), '0', '0 数量异常应展示原始值')
    assertEqual(formatPastePreviewQuantity(preview[2]), '--', '空数量异常应显示占位')
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
  })
  if (detailUiFailure) failures.push(detailUiFailure)

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
