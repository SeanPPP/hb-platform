import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'
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

runTest('minOrderQuantity 为空 0 或无效时应返回空字符串', () => {
  assertEqual(formatInnerPackCount(24, undefined), '', 'minOrderQuantity 为空时应显示空字符串')
  assertEqual(formatInnerPackCount(24, 0), '', 'minOrderQuantity 为 0 时应显示空字符串')
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
  itemsTotal: 3,
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
  assertDeepEqual(
    excelData.detailRows.map((row) => row[6]),
    ['1', '1.5', ''],
    '内包装数量应复用统一格式化逻辑',
  )
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

console.log('pickingListLogic.test: ok')
