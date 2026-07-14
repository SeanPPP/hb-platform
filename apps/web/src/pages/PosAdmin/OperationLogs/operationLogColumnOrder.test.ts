import { readFileSync } from 'node:fs'
import {
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  MAX_OPERATION_LOG_COLUMN_ORDER_STORAGE_LENGTH,
  createOperationLogDndAccessibility,
  dispatchOperationLogDragHandleKeyDown,
  isOperationLogColumnOrderCustomized,
  mergeOperationLogColumnOrder,
  moveOperationLogColumnOrder,
  parseOperationLogColumnOrder,
} from './operationLogColumnOrder'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

assertDeepEqual(
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  ['occurredAtUtc', 'storeCode', 'employee', 'operationType', 'products', 'amountDelta', 'deviceCode', 'outcome'],
  '默认顺序应包含全部八个业务列且不包含操作列',
)
assertDeepEqual(
  mergeOperationLogColumnOrder(
    ['products', 'invalid', 'products', 1, 'storeCode'],
    DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  ),
  ['products', 'storeCode', 'occurredAtUtc', 'employee', 'operationType', 'amountDelta', 'deviceCode', 'outcome'],
  '已保存列序应过滤非法项和重复项并补齐新增列',
)
assertDeepEqual(
  moveOperationLogColumnOrder(DEFAULT_OPERATION_LOG_COLUMN_ORDER, 'outcome', 'storeCode'),
  ['occurredAtUtc', 'outcome', 'storeCode', 'employee', 'operationType', 'products', 'amountDelta', 'deviceCode'],
  '拖拽应将活动列移动到目标列位置',
)
assertDeepEqual(
  moveOperationLogColumnOrder(DEFAULT_OPERATION_LOG_COLUMN_ORDER, 'actions', 'storeCode'),
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  '非业务列不应改变列顺序',
)
assertDeepEqual(
  mergeOperationLogColumnOrder(null),
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  '清除持久化列序后应恢复完整默认顺序',
)
assertEqual(
  isOperationLogColumnOrderCustomized(DEFAULT_OPERATION_LOG_COLUMN_ORDER),
  false,
  '默认顺序不应显示重置列按钮',
)
assertEqual(
  isOperationLogColumnOrderCustomized(['storeCode', ...DEFAULT_OPERATION_LOG_COLUMN_ORDER.filter((key) => key !== 'storeCode')]),
  true,
  '自定义顺序应显示重置列按钮',
)

let dragKeyDownCalled = 0
let stopPropagationCalled = 0
const keyboardEvent = {
  key: ' ',
  stopPropagation: () => {
    stopPropagationCalled += 1
  },
}
dispatchOperationLogDragHandleKeyDown(keyboardEvent, (event) => {
  dragKeyDownCalled += 1
  assertEqual(event, keyboardEvent, '应将原键盘事件交给 dnd listener')
})
assertEqual(dragKeyDownCalled, 1, '键盘拖拽 listener 应被调用一次')
assertEqual(stopPropagationCalled, 1, '键盘拖拽事件应停止冒泡，避免触发表头排序')

assertDeepEqual(
  parseOperationLogColumnOrder('x'.repeat(MAX_OPERATION_LOG_COLUMN_ORDER_STORAGE_LENGTH + 1)),
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  '超大 localStorage 字符串应在 JSON 解析前恢复默认列序',
)
assertDeepEqual(
  mergeOperationLogColumnOrder([
    'outcome',
    ...Array(DEFAULT_OPERATION_LOG_COLUMN_ORDER.length * 4).fill('storeCode'),
  ]),
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  '超长保存数组应恢复默认列序而不是继续遍历',
)

const dndAccessibility = createOperationLogDndAccessibility(
  { occurredAtUtc: '时间', storeCode: '门店' },
  {
    instructions: '中文键盘拖拽说明',
    unknownColumn: '当前列',
    dragStart: (column) => `拾取：${column}`,
    dragOver: (column, overColumn) => `移动：${column} -> ${overColumn}`,
    dragOverNone: (column) => `移出：${column}`,
    dragEnd: (column, overColumn) => `放下：${column} -> ${overColumn}`,
    dragCancel: (column) => `取消：${column}`,
  },
)
assertEqual(
  dndAccessibility.screenReaderInstructions.draggable,
  '中文键盘拖拽说明',
  '读屏键盘说明应使用本地化文案',
)
assertEqual(
  dndAccessibility.announcements.onDragStart({ active: { id: 'occurredAtUtc' } }),
  '拾取：时间',
  '开始拖拽播报应将内部 key 映射为本地化列名',
)
assertEqual(
  dndAccessibility.announcements.onDragOver({
    active: { id: 'occurredAtUtc' },
    over: { id: 'storeCode' },
  }),
  '移动：时间 -> 门店',
  '移动播报应包含源列和目标列的本地化名称',
)
assertEqual(
  dndAccessibility.announcements.onDragEnd({
    active: { id: 'internal-unknown-key' },
    over: null,
  }),
  '取消：当前列',
  '未知 key 和无目标位置时不得向读屏播报内部标识',
)

function interpolateLocale(template: string, values: Record<string, string>) {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.split(`{{${key}}}`).join(value),
    template,
  )
}

function createLocaleAccessibility(locale: Record<string, string>) {
  return createOperationLogDndAccessibility({}, {
    instructions: locale.instructions,
    unknownColumn: locale.unknownColumn,
    dragStart: (column) => interpolateLocale(locale.dragStart, { column }),
    dragOver: (column, overColumn) => interpolateLocale(locale.dragOver, { column, overColumn }),
    dragOverNone: (column) => interpolateLocale(locale.dragOverNone, { column }),
    dragEnd: (column, overColumn) => interpolateLocale(locale.dragEnd, { column, overColumn }),
    dragCancel: (column) => interpolateLocale(locale.dragCancel, { column }),
  })
}

const zhDndLocale = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8')).operationLogs.dnd
const enDndLocale = JSON.parse(readFileSync('src/i18n/locales/en.json', 'utf8')).operationLogs.dnd
assertEqual(
  createLocaleAccessibility(zhDndLocale).announcements.onDragStart({
    active: { id: 'unknown-id' },
  }),
  '已拾取当前列。',
  '中文未知列播报不应重复“列”字',
)
assertEqual(
  createLocaleAccessibility(enDndLocale).announcements.onDragStart({
    active: { id: 'unknown-id' },
  }),
  'Picked up the current column.',
  '英文未知列播报不应重复 column',
)

console.log('operationLogColumnOrder.test: ok')
