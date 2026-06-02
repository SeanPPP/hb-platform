import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'
import fs from 'node:fs'
import path from 'node:path'
import { buildPickingListExcelData, formatInnerPackCount } from './pickingListLogic'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${label}。Expected: ${expectedText}, received: ${actualText}`)
  }
}

function runTest(name: string, execute: () => void) {
  execute()
  console.log(`ok - ${name}`)
}

// 这里锁定配货单“内包装数量”的业务规则，避免组件里再次出现临时兜底逻辑。
runTest('minOrderQuantity 有效时应返回纯数字格式的内包装数量', () => {
  assertEqual(formatInnerPackCount(12, 12), '1', '整除时应显示整数且不带小数')
  assertEqual(formatInnerPackCount(18, 12), '1.5', '非整除时应保留 1 位小数')
  assertEqual(formatInnerPackCount(0, 12), '0', '订货数量为 0 时应显示 0')
})

runTest('minOrderQuantity 为空 0 1 或无效时应返回空字符串', () => {
  assertEqual(formatInnerPackCount(24, undefined), '', 'minOrderQuantity 为空时应显示空字符串')
  assertEqual(formatInnerPackCount(24, 0), '', 'minOrderQuantity 为 0 时应显示空字符串')
  assertEqual(formatInnerPackCount(24, 1), '', 'minOrderQuantity 为 1 时应显示空字符串')
  assertEqual(formatInnerPackCount(24, Number.NaN), '', 'minOrderQuantity 非法时应显示空字符串')
})

const excelTexts = {
  sheetName: 'Picking List',
  orderNoLabel: 'Order No.',
  storeLabel: 'Store',
  orderDateLabel: 'Order Date',
  printTimeLabel: 'Print Time',
  remarksLabel: 'Remarks',
  totalSKULabel: 'Total SKU',
  totalOrderQtyLabel: 'Total Order Qty',
  totalShipQtyLabel: 'Total Ship Qty',
  totalOrderVolumeLabel: 'Order Volume',
  detailHeaders: {
    index: '#',
    itemNumber: '货号',
    location: '货位',
    productName: '商品名称',
    importPrice: '进口价',
    rrp: 'RRP',
    innerPackCount: '内包装数量',
    orderQuantity: '订货数量',
    allocQuantity: '发货数',
  },
}

const excelItems: StoreOrderDetailLine[] = [
  {
    detailGUID: 'detail-1',
    productCode: 'P-001',
    itemNumber: 'A-001',
    barcode: '111',
    productName: '商品 A',
    quantity: 12,
    allocQuantity: 10,
    price: 0,
    amount: 0,
    importPrice: 3.5,
    importAmount: 0,
    minOrderQuantity: 12,
    isActive: true,
    locationCode: 'L-01',
    rrp: 5.5,
  },
  {
    detailGUID: 'detail-2',
    productCode: 'P-002',
    itemNumber: 'A-002',
    barcode: '222',
    productName: '商品 B',
    quantity: 18,
    allocQuantity: 9,
    price: 0,
    amount: 0,
    importPrice: 4,
    importAmount: 0,
    minOrderQuantity: 12,
    isActive: true,
    locationCode: 'L-02',
    rrp: 6,
  },
  {
    detailGUID: 'detail-3',
    productCode: 'P-003',
    itemNumber: 'A-003',
    barcode: '333',
    productName: '商品 C',
    quantity: 7,
    allocQuantity: 0,
    price: 0,
    amount: 0,
    importPrice: 8,
    importAmount: 0,
    minOrderQuantity: 0,
    isActive: true,
    locationCode: 'L-03',
  },
  {
    detailGUID: 'detail-4',
    productCode: 'P-004',
    itemNumber: 'A-004',
    barcode: '444',
    productName: '商品 D',
    quantity: 24,
    allocQuantity: 24,
    price: 0,
    amount: 0,
    importPrice: 2,
    importAmount: 0,
    minOrderQuantity: 1,
    isActive: true,
    locationCode: 'L-04',
    rrp: 3,
  },
]

const excelOrder: StoreOrderDetail = {
  orderGUID: 'order-1',
  orderNo: 'SO-001',
  storeCode: 'ST-01',
  totalAmount: 0,
  totalQuantity: 37,
  totalImportAmount: 0,
  totalVolume: 0,
  totalOrderVolume: 12.3456,
  remarks: '请优先处理',
  totalAllocQuantity: 19,
  totalSKU: 3,
  itemsTotal: 4,
  orderDate: '2026-06-01T00:00:00.000Z',
  items: excelItems,
}

runTest('配货单 Excel 数据应包含固定列顺序、备注和总计信息', () => {
  const excelData = buildPickingListExcelData(excelOrder, excelItems, excelTexts)
  assertEqual(excelData.sheetName, 'Picking List', 'sheet 名称应来自传入文案')
  assertDeepEqual(
    excelData.detailHeader,
    ['#', '货号', '货位', '商品名称', '进口价', 'RRP', '内包装数量', '订货数量', '发货数'],
    '明细列顺序应与需求一致',
  )
  assertDeepEqual(excelData.detailRows.map((row) => row[6]), ['1', '1.5', '', ''], '内包装数量应复用统一格式化逻辑')
  assertDeepEqual(excelData.remarksRow, ['Remarks', '请优先处理'], '备注行应保留原始备注')
  assertDeepEqual(
    excelData.totalRows,
    [
      ['Total SKU', 3],
      ['Total Order Qty', 37],
      ['Total Ship Qty', 19],
      ['Order Volume', '12.3456'],
    ],
    '总计行应包含 SKU、订货数、发货数和订货体积',
  )
})

runTest('配货单打印应取消固定 30 行分页并交给 A4 打印流填满页面', () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/PickingList.tsx'), 'utf8')
  assertEqual(pickingListSource.includes('PICKING_PRINT_ROWS_PER_PAGE'), false, '打印组件不应保留固定 30 行分页常量')
  assertEqual(pickingListSource.includes('buildPickingPrintPages'), false, '打印组件不应按固定行数预切页')
})

function readCssRule(source: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`))
  return match?.[1] ?? ''
}

function readCssWidth(rule: string) {
  return Number(rule.match(/width:\s*(\d+(?:\.\d+)?)%/)?.[1] ?? Number.NaN)
}

function readCssNumber(rule: string, property: string) {
  return Number(rule.match(new RegExp(`${property}:\\s*(\\d+(?:\\.\\d+)?)`))?.[1] ?? Number.NaN)
}

runTest('配货单打印行高和字体应控制在每页约 30 行', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  const tableRule = readCssRule(printCssSource, '.store-order-picking-table')
  const bodyCellRule = readCssRule(printCssSource, '.store-order-picking-table td')
  const headerCellRule = readCssRule(printCssSource, '.store-order-picking-table th')
  const indexRule = readCssRule(printCssSource, '.store-order-picking-table .col-index')
  const itemRule = readCssRule(printCssSource, '.store-order-picking-table .col-item')
  const locationRule = readCssRule(printCssSource, '.store-order-picking-table .col-location')
  const productRule = readCssRule(printCssSource, '.store-order-picking-table .col-product')
  const priceRule = readCssRule(printCssSource, '.store-order-picking-table .col-price')
  const innerPackRule = readCssRule(printCssSource, '.store-order-picking-table .col-inner-pack')
  const quantityRule = readCssRule(printCssSource, '.store-order-picking-table .col-qty')
  const sendQuantityRule = readCssRule(printCssSource, '.store-order-picking-table .col-send-qty')
  const zebraRule = readCssRule(printCssSource, '.store-order-picking-table tbody tr:nth-child(even) td')

  assertEqual(readCssNumber(tableRule, 'font-size'), 15, '表格基础字体应为 15px')
  assertEqual(readCssNumber(tableRule, 'line-height'), 1.35, '表格基础行高应约为 1.35')
  assertEqual(/print-color-adjust:\s*exact/.test(tableRule), true, '表格打印应保留背景色')
  assertEqual(/-webkit-print-color-adjust:\s*exact/.test(tableRule), true, '表格打印应兼容 Chromium 背景色保留')
  assertEqual(/padding:\s*6px 2px/.test(bodyCellRule), true, '明细单元格 padding 应为 6px 2px')
  assertEqual(/padding:\s*6px 2px/.test(headerCellRule), true, '表头单元格 padding 应为 6px 2px')
  assertEqual(readCssNumber(indexRule, 'font-size'), 14.5, '行号字体应为 14.5px')
  assertEqual(readCssNumber(itemRule, 'font-size'), 14.5, '货号字体应为 14.5px')
  assertEqual(readCssNumber(locationRule, 'font-size'), 14.5, '货位字体应为 14.5px')
  assertEqual(readCssNumber(productRule, 'font-size'), 12.5, '商品名称字体应为 12.5px')
  assertEqual(/padding-right:\s*8px/.test(indexRule), true, '行号列右侧间距应为 8px')
  assertEqual(/font-weight:\s*700/.test(indexRule), true, '行号列应加粗')
  assertEqual(/font-weight:\s*700/.test(quantityRule), true, '订货数列应加粗')
  assertEqual(/font-weight:\s*700/.test(sendQuantityRule), false, '发货数列不应跟随订货数加粗')
  assertEqual(/background:\s*#eef2f7/.test(zebraRule), true, '明细偶数行应使用更清晰的浅色斑马背景')
  assertEqual(/background:\s*#fafafa/.test(sendQuantityRule), false, '发货数列不应固定背景色，应跟随斑马纹')
  for (const rule of [priceRule, innerPackRule, quantityRule, sendQuantityRule]) {
    assertEqual(readCssNumber(rule, 'font-size'), 13, '数字列字体应为 13px')
  }
})

runTest('配货单打印列宽应按草图重新分配并保留 colgroup', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/PickingList.tsx'), 'utf8')

  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-index')), 3, '行号列应为 3%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-item')), 16, '货号列应为 16%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-location')), 15, '货位列应为 15%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-product')), 34, '商品名称列应为 34%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-price')), 6, '价格列应为 6%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-inner-pack')), 5, '包数列应为 5%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-qty')), 5.5, '订货数量列应为 5.5%')
  assertEqual(readCssWidth(readCssRule(printCssSource, '.store-order-picking-table .col-send-qty')), 5.5, '发货数列应为 5.5%')
  assertEqual(pickingListSource.includes('<colgroup>'), true, '固定表格布局应使用 colgroup 明确列宽')
  assertDeepEqual(
    Array.from(pickingListSource.matchAll(/<col className="([^"]+)" \/>/g), (match) => match[1]),
    ['col-index', 'col-item', 'col-location', 'col-product', 'col-price', 'col-price', 'col-inner-pack', 'col-qty', 'col-send-qty'],
    'colgroup 应按表头顺序定义 9 列，并包含两列价格列',
  )
})

runTest('配货单打印货号货位应保留间隔且名称价格区域更紧凑', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  const locationRule = readCssRule(printCssSource, '.store-order-picking-table .col-location')
  const productRule = readCssRule(printCssSource, '.store-order-picking-table .col-product')
  const priceRule = readCssRule(printCssSource, '.store-order-picking-table .col-price')
  const innerPackRule = readCssRule(printCssSource, '.store-order-picking-table .col-inner-pack')
  const quantityRule = readCssRule(printCssSource, '.store-order-picking-table .col-qty')
  const sendQuantityRule = readCssRule(printCssSource, '.store-order-picking-table .col-send-qty')

  assertEqual(/padding-left:\s*5px/.test(locationRule), true, '货位列左侧间隔应为 5px')
  assertEqual(/padding-right:\s*5px/.test(locationRule), true, '货位列右侧间隔应为 5px')
  assertEqual(readCssWidth(productRule), 34, '商品名称列应保持 34%，避免过早截断')
  assertEqual(readCssWidth(priceRule), 6, '价格列应为 6%')
  assertEqual(readCssWidth(quantityRule), 5.5, '订货数量列应为 5.5%')
  assertEqual(readCssWidth(sendQuantityRule), 5.5, '发货数列应为 5.5%')

  for (const rule of [priceRule, innerPackRule, quantityRule, sendQuantityRule]) {
    assertEqual(/padding-left:\s*1px/.test(rule), true, '数字列左侧 padding 应压缩到 1px')
    assertEqual(/padding-right:\s*1px/.test(rule), true, '数字列右侧 padding 应压缩到 1px')
  }
})

runTest('配货单打印表头应将内包装数量显示为包数', () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/PickingList.tsx'), 'utf8')
  assertEqual(
    pickingListSource.includes('<th className="col-inner-pack">{t(\'warehouse.pickingList.innerPackShort\')}</th>'),
    true,
    '打印表头应使用配货单专用包数翻译',
  )
  assertEqual(pickingListSource.includes('<th className="col-inner-pack">包数</th>'), false, '打印表头不应硬编码“包数”')
  assertEqual(pickingListSource.includes("<th className=\"col-inner-pack\">{t('column.innerPackCount')}</th>"), false, '打印表头不应继续显示“内包装数量”翻译')
})

runTest('配货单打印表头应将订货数量显示为订货数', () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/PickingList.tsx'), 'utf8')
  assertEqual(
    pickingListSource.includes('<th className="col-qty">{t(\'warehouse.pickingList.orderQtyShort\')}</th>'),
    true,
    '打印表头应使用配货单专用订货数翻译',
  )
  assertEqual(pickingListSource.includes('<th className="col-qty">订货数</th>'), false, '打印表头不应硬编码“订货数”')
  assertEqual(pickingListSource.includes("<th className=\"col-qty\">{t('column.orderQuantity')}</th>"), false, '打印表头不应继续显示“订货数量”翻译')
})

runTest('配货单打印短表头应包含中英文翻译', () => {
  const zhLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))
  const enLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))

  assertEqual(zhLocale.warehouse.pickingList.innerPackShort, '包数', '中文包数短表头应存在')
  assertEqual(zhLocale.warehouse.pickingList.orderQtyShort, '订货数', '中文订货数短表头应存在')
  assertEqual(enLocale.warehouse.pickingList.innerPackShort, 'Packs', '英文包数短表头应存在')
  assertEqual(enLocale.warehouse.pickingList.orderQtyShort, 'Order Qty', '英文订货数短表头应存在')
})

runTest('配货单打印货号货位应单行完整显示且不能被省略隐藏', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  const itemRule = readCssRule(printCssSource, '.store-order-picking-table .col-item')
  assertEqual(/padding-left:\s*10px/.test(itemRule), true, '货号列左侧间距应为 10px')
  assertEqual(/text-align:\s*center/.test(itemRule), true, '货号列应居中显示')

  for (const selector of ['.store-order-picking-table .col-item', '.store-order-picking-table .col-location']) {
    const rule = readCssRule(printCssSource, selector)
    assertEqual(rule.includes('white-space: nowrap'), true, `${selector} 应保持单行显示`)
    assertEqual(/overflow:\s*hidden/.test(rule), false, `${selector} 不应隐藏溢出内容`)
    assertEqual(/text-overflow:\s*ellipsis/.test(rule), false, `${selector} 不应使用省略号截断`)
  }
})

runTest('配货单打印商品名称应单行并在超出列宽时隐藏截断', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  for (const selector of ['.store-order-picking-table .col-product', '.store-order-picking-name']) {
    const rule = readCssRule(printCssSource, selector)
    assertEqual(rule.includes('white-space: nowrap'), true, `${selector} 应保持单行显示`)
    assertEqual(/overflow:\s*hidden/.test(rule), true, `${selector} 超出时应隐藏截断`)
    assertEqual(/text-overflow:\s*ellipsis/.test(rule), true, `${selector} 超出时应显示省略号`)
  }
})

runTest('配货单打印应绑定专用 A4 页面边距', () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css'), 'utf8')
  assertEqual(printCssSource.includes('@page store-order-picking'), true, '应保留配货单专用命名页面')
  assertEqual(
    /\.store-order-picking-paper\s*\{[\s\S]*?page:\s*store-order-picking/.test(printCssSource),
    true,
    '配货单纸张元素应绑定命名页面',
  )
})

console.log('pickingListLogic.test: ok')
