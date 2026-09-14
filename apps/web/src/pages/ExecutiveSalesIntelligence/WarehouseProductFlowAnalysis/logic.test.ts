import {
  buildWarehouseProductFlowCategoryOptions,
  buildWarehouseProductFlowDefaultPeriods,
  filterWarehouseProductFlowCategoryOptions,
  filterWarehouseProductFlowSupplierOptions,
  createWarehouseProductFlowFilter,
  isValidWarehouseProductFlowRange,
  filterWarehouseProductFlowShipments,
  sortWarehouseProductFlowBranches,
} from './logic'
import type { WarehouseProductFlowBranch } from '../../../types/warehouseProductFlowAnalysis'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

const defaults = buildWarehouseProductFlowDefaultPeriods(new Date('2026-08-19T02:00:00.000Z'))
assertDeepEqual(defaults, {
  containerPeriod: { startDate: '2025-08-19', endDate: '2026-08-18' },
  orderShipmentPeriod: { startDate: '2026-02-19', endDate: '2026-08-18' },
  salesPeriod: { startDate: '2026-02-19', endDate: '2026-08-18' },
}, '三套默认日期必须截至 Brisbane 昨天，并按自然月分别回溯 12/6/6 月')

assertEqual(isValidWarehouseProductFlowRange([new Date('2026-08-01'), new Date('2026-08-18')], new Date('2026-08-19T02:00:00.000Z')), true, '昨天为可选结束日')
assertEqual(isValidWarehouseProductFlowRange([new Date('2026-08-01'), new Date('2026-08-19')], new Date('2026-08-19T02:00:00.000Z')), false, '今天不能作为结束日')
assertEqual(isValidWarehouseProductFlowRange([new Date('2025-08-18'), new Date('2026-08-18')], new Date('2026-08-19T02:00:00.000Z')), true, '366 个自然日可查询')
assertEqual(isValidWarehouseProductFlowRange([new Date('2025-08-17'), new Date('2026-08-18')], new Date('2026-08-19T02:00:00.000Z')), false, '超过 366 个自然日不可查询')
assertDeepEqual(createWarehouseProductFlowFilter(' 玩具 ', ['cat-1'], ['CN-1'], ' OOLU '), {
  keyword: '玩具', warehouseCategoryGuids: ['cat-1'], supplierCodes: ['CN-1'], documentKeyword: 'OOLU',
}, '商品主档筛选不得包含日期字段')
const categoryOptions = buildWarehouseProductFlowCategoryOptions([
  {
    categoryGUID: 'parent-guid', categoryName: 'Toys', chineseName: '玩具', isActive: true,
    children: [{ categoryGUID: 'child-guid', categoryName: 'Building Blocks', chineseName: '积木', isActive: true, children: [] }],
  },
])
assertDeepEqual(categoryOptions.map((option) => option.label), ['玩具', '— 积木'], '分类选项必须保留原有层级缩进')
assertDeepEqual(filterWarehouseProductFlowCategoryOptions(categoryOptions, ' toys ').map((option) => option.value), ['parent-guid', 'child-guid'], '分类英文和完整父级路径必须忽略首尾空白及大小写匹配')
assertDeepEqual(filterWarehouseProductFlowCategoryOptions(categoryOptions, '玩具   积木').map((option) => option.value), ['child-guid'], '分类中文祖先路径必须忽略连续空白匹配后代')
assertDeepEqual(filterWarehouseProductFlowCategoryOptions(categoryOptions, 'CHILD-GUID').map((option) => option.value), ['child-guid'], '分类 GUID 必须忽略大小写匹配')

const supplierOptions = filterWarehouseProductFlowSupplierOptions([
  { code: 'TOY', name: '玩具批发' },
  { code: 'TOY-001', name: '优选玩具' },
  { code: 'SUP-101', name: 'Toy Planet' },
  { code: 'OTHER-1', name: 'Retro Toy Store' },
], ' toy ')
assertDeepEqual(supplierOptions.map((option) => option.value), ['TOY', 'SUP-101', 'TOY-001', 'OTHER-1'], '供应商搜索必须覆盖编码和名称，并按 exact、prefix、contains 排序')
assertDeepEqual(filterWarehouseProductFlowSupplierOptions([{ code: 'CN-1', name: '优品玩具' }], ' 优品  ').map((option) => option.value), ['CN-1'], '供应商名称搜索必须忽略首尾与连续空白')

const shipments = [
  { shipmentNumber: 'S1', posEnabled: true },
  { shipmentNumber: 'S2', posEnabled: false },
  { shipmentNumber: 'S3', posEnabled: null },
  { shipmentNumber: 'S4', posEnabled: true },
]
assertDeepEqual(filterWarehouseProductFlowShipments(shipments, 'all'), shipments, '全部分店必须保留未知 POS 状态和原发货顺序')
assertDeepEqual(filterWarehouseProductFlowShipments(shipments, 'enabled').map((row) => row.shipmentNumber), ['S1', 'S4'], '启用 POS 筛选必须保留同一分店的多张发货单')
assertDeepEqual(filterWarehouseProductFlowShipments(shipments, 'disabled').map((row) => row.shipmentNumber), ['S2'], '未启用 POS 筛选不能误包含状态未知的分店')
assertDeepEqual(filterWarehouseProductFlowShipments([], 'enabled'), [], '无发货明细时筛选必须返回空列表')

const branch = (branchCode: string, netSalesQuantity: number): WarehouseProductFlowBranch => ({ branchCode, netSalesQuantity, orderedQuantity: 0, shippedQuantity: 0, netSalesAmount: 0, sellThroughRate: null, averageUnitPrice: null })
const unsortedBranches = [branch('B', 3), branch('D', -2), branch('C', 10), branch('E', 0), branch('A', 3)]
assertDeepEqual(sortWarehouseProductFlowBranches(unsortedBranches).map((row) => row.branchCode), ['C', 'A', 'B', 'E', 'D'], '分店必须按净销量降序，同销量按分店编码稳定排序，退货负数排在零之后')
assertDeepEqual(unsortedBranches.map((row) => row.branchCode), ['B', 'D', 'C', 'E', 'A'], '排序不得修改接口返回的原始数组')

console.log('warehouseProductFlowAnalysis.logic.test: ok')
