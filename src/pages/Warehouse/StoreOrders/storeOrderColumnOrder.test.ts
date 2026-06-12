import {
  isStoreOrderListColumnOrderCustomized,
  mergeStoreOrderListColumnOrder,
  moveStoreOrderListColumnOrder,
  type StoreOrderListTableColumnKey,
} from './columnOrder'

function assertDeepEqual<T>(actual: T, expected: T, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\nExpected: ${expectedJson}\nActual: ${actualJson}`)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}\nExpected: ${expected}\nActual: ${actual}`)
  }
}

const defaultColumnOrder: StoreOrderListTableColumnKey[] = [
  'index',
  'orderNo',
  'storeCode',
  'orderDate',
  'flowStatus',
]

assertDeepEqual(
  mergeStoreOrderListColumnOrder(['storeCode', 'unknown', 'storeCode', 'orderNo'], defaultColumnOrder),
  ['storeCode', 'orderNo', 'index', 'orderDate', 'flowStatus'],
  '分店订货列表列顺序应过滤未知列、去重并补齐新增列',
)

assertDeepEqual(
  mergeStoreOrderListColumnOrder({ storeCode: true } as unknown as readonly unknown[], defaultColumnOrder),
  defaultColumnOrder,
  '分店订货列表列顺序遇到非数组持久化值时应回退默认顺序',
)

assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, 'flowStatus', 'orderNo'),
  ['index', 'flowStatus', 'orderNo', 'storeCode', 'orderDate'],
  '分店订货列表列拖拽应把 active 列移动到 over 列位置',
)

assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, 'missing', 'orderNo'),
  defaultColumnOrder,
  '分店订货列表列拖拽遇到未知 active 列时应保持原顺序',
)

assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, 'orderNo', 'orderNo'),
  defaultColumnOrder,
  '分店订货列表列拖拽 active 与 over 相同时应保持原顺序',
)

assertEqual(
  isStoreOrderListColumnOrderCustomized(defaultColumnOrder, defaultColumnOrder),
  false,
  '分店订货列表默认列顺序不应判定为已自定义',
)

assertEqual(
  isStoreOrderListColumnOrderCustomized(
    moveStoreOrderListColumnOrder(defaultColumnOrder, 'flowStatus', 'orderNo'),
    defaultColumnOrder,
  ),
  true,
  '分店订货列表拖拽列顺序后应判定为已自定义',
)

assertEqual(
  isStoreOrderListColumnOrderCustomized([], defaultColumnOrder),
  false,
  '分店订货列表列顺序初始化为空时不应误判为已自定义',
)

console.log('storeOrderColumnOrder.test: ok')
