import {
  isWarehouseProductColumnOrderCustomized,
  mergeWarehouseProductColumnOrder,
  moveWarehouseProductColumnOrder,
} from './columnOrder'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  mergeWarehouseProductColumnOrder(['name', 'itemNumber'], ['itemNumber', 'name', 'barcode']),
  ['name', 'itemNumber', 'barcode'],
  '商品管理列顺序应保留已保存顺序，并自动追加新增列',
)

assertDeepEqual(
  mergeWarehouseProductColumnOrder(['removed', 'name', 'name', 'barcode'], ['itemNumber', 'name', 'barcode']),
  ['name', 'barcode', 'itemNumber'],
  '商品管理列顺序应过滤废弃列和重复列',
)

assertDeepEqual(
  mergeWarehouseProductColumnOrder(['removed', 'barcode'], ['itemNumber', 'name', 'barcode']),
  ['barcode', 'itemNumber', 'name'],
  '商品管理列顺序清理废弃列后应按默认顺序补齐缺失列',
)

assertDeepEqual(
  moveWarehouseProductColumnOrder(['itemNumber', 'name', 'barcode'], 'barcode', 'itemNumber'),
  ['barcode', 'itemNumber', 'name'],
  '商品管理列拖拽应把 active 列移动到 over 列位置',
)

assertDeepEqual(
  moveWarehouseProductColumnOrder(['itemNumber', 'name', 'barcode'], 'missing', 'name'),
  ['itemNumber', 'name', 'barcode'],
  '商品管理列拖拽遇到无效列时应保持原顺序',
)

assertDeepEqual(
  isWarehouseProductColumnOrderCustomized(['itemNumber', 'name', 'barcode'], ['itemNumber', 'name', 'barcode']),
  false,
  '商品管理列顺序与默认顺序一致时不应视为自定义',
)

assertDeepEqual(
  isWarehouseProductColumnOrderCustomized(['name', 'itemNumber', 'barcode'], ['itemNumber', 'name', 'barcode']),
  true,
  '商品管理列顺序与默认顺序不一致时应视为自定义',
)

console.log('warehouseProducts.columnOrder.test: ok')
