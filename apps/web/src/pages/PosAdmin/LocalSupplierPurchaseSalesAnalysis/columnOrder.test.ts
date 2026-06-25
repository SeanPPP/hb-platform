import { readFileSync } from 'node:fs'
import {
  LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER,
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized,
  mergeLocalSupplierPurchaseSalesAnalysisColumnOrder,
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder,
  type LocalSupplierPurchaseSalesAnalysisColumnKey,
} from './columnOrder'

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

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const defaultOrder: LocalSupplierPurchaseSalesAnalysisColumnKey[] = [
  ...LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER,
]
const pageSource = readFileSync('src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/index.tsx', 'utf8')

assertDeepEqual(
  mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(
    ['salesQty90', 'removed', 'supplierName', 'salesQty90'],
    defaultOrder,
  ),
  [
    'salesQty90',
    'supplierName',
    'previousPurchaseDate',
    'latestPurchaseDate',
    'purchaseIntervalDays',
    'salesBetweenPurchases',
    'salesQty30',
    'salesQty60',
    'salesStatisticLastUpdate',
  ],
  '分店进货销量分析列顺序应过滤未知列、去重并补齐新增列',
)

assertDeepEqual(
  mergeLocalSupplierPurchaseSalesAnalysisColumnOrder({ supplierName: true }, defaultOrder),
  defaultOrder,
  '分店进货销量分析列顺序遇到非数组持久化值时应回退默认顺序',
)

assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, 'salesQty90', 'supplierName'),
  [
    'salesQty90',
    'supplierName',
    'previousPurchaseDate',
    'latestPurchaseDate',
    'purchaseIntervalDays',
    'salesBetweenPurchases',
    'salesQty30',
    'salesQty60',
    'salesStatisticLastUpdate',
  ],
  '分店进货销量分析列拖拽应把 active 列移动到 over 列位置',
)

assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, 'missing', 'supplierName'),
  defaultOrder,
  '分店进货销量分析列拖拽遇到未知 active 列时应保持原顺序',
)

assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, 'salesQty90', 'missing'),
  defaultOrder,
  '分店进货销量分析列拖拽遇到未知 over 列时应保持原顺序',
)

assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, 'supplierName', 'supplierName'),
  defaultOrder,
  '分店进货销量分析列拖拽 active 与 over 相同时应保持原顺序',
)

assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(defaultOrder, defaultOrder),
  false,
  '分店进货销量分析默认列顺序不应判定为已自定义',
)

assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(
    moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, 'salesQty90', 'supplierName'),
    defaultOrder,
  ),
  true,
  '分店进货销量分析拖拽列顺序后应判定为已自定义',
)

assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized([], defaultOrder),
  false,
  '分店进货销量分析列顺序初始化为空时不应误判为已自定义',
)

assertEqual(
  defaultOrder.includes('image'),
  false,
  '分店进货销量分析默认拖拽列不应包含图片固定列',
)

assertEqual(
  defaultOrder.includes('itemNumber'),
  false,
  '分店进货销量分析默认拖拽列不应包含货号名称固定列',
)

assertEqual(
  defaultOrder.indexOf('previousPurchaseDate') < defaultOrder.indexOf('latestPurchaseDate'),
  true,
  '分店进货销量分析默认拖拽列中上次进货应在最近进货前',
)

assert(
  pageSource.includes('DndContext') &&
    pageSource.includes('SortableContext') &&
    pageSource.includes('useSortable') &&
    pageSource.includes('horizontalListSortingStrategy'),
  '分店进货销量分析表头拖拽应复用 @dnd-kit 横向排序能力',
)

assert(
  pageSource.includes("const LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_COLUMN_ORDER_STORAGE_KEY =") &&
    pageSource.includes("hbweb_rv.localSupplierPurchaseSalesAnalysis.columnOrder.v1") &&
    pageSource.includes('localStorage.setItem(') &&
    pageSource.includes('localStorage.removeItem(') &&
    pageSource.includes('mergeLocalSupplierPurchaseSalesAnalysisColumnOrder('),
  '分店进货销量分析列顺序应保存到独立 localStorage key，并兼容列增删',
)

assert(
  pageSource.includes("const STATIC_PURCHASE_SALES_ANALYSIS_COLUMN_KEYS = new Set(['image', 'itemNumber'])") &&
    pageSource.includes('const fixedColumns = baseColumns.filter((column) =>') &&
    pageSource.includes('STATIC_PURCHASE_SALES_ANALYSIS_COLUMN_KEYS.has(String(column.key))') &&
    pageSource.includes("'data-column-key': String(column.key)") &&
    pageSource.includes('return [...fixedColumns, ...draggableColumns]'),
  '分店进货销量分析固定左列不应进入拖拽列顺序',
)

assert(
  pageSource.includes('components={{ header: { cell: DraggableHeaderCell } }}') &&
    pageSource.includes('items={columnOrder.length ? columnOrder : draggableColumnKeys}') &&
    pageSource.includes('activationConstraint:') &&
    pageSource.includes('distance: 6'),
  '分店进货销量分析表格应接入可拖拽表头 cell，并设置拖拽距离避免误触排序',
)

assert(
  pageSource.includes('catch {') &&
    pageSource.includes('localStorage 不可用时不影响当前页面内拖拽排序。') &&
    pageSource.includes('localStorage 不可用时仍恢复当前页面内的默认列顺序。'),
  '分店进货销量分析 localStorage 失败时不应阻断拖拽或重置列',
)

assert(
  pageSource.includes('const handleTableChange = (') &&
    pageSource.includes('onChange={handleTableChange}') &&
    pageSource.includes('列头排序必须透传到后端，不能只在当前页本地排序。'),
  '分店进货销量分析列头拖拽不能移除既有服务端排序回调',
)

console.log('localSupplierPurchaseSalesAnalysis.columnOrder.test: ok')
