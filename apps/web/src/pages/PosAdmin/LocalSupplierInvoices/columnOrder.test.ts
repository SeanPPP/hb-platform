import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  MAX_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER_STORAGE_LENGTH,
  createLocalSupplierInvoiceDndAccessibility,
  dispatchLocalSupplierInvoiceDragHandleKeyDown,
  isLocalSupplierInvoiceColumnOrderCustomized,
  mergeLocalSupplierInvoiceColumnOrder,
  moveLocalSupplierInvoiceColumnOrder,
  parseLocalSupplierInvoiceColumnOrder,
} from './columnOrder'

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
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  [
    'storeCode',
    'supplierCode',
    'invoiceNo',
    'orderDate',
    'inboundDate',
    'totalAmount',
    'receivedTotalAmount',
    'flowStatus',
    'inboundStatus',
    'remarks',
    'createdAt',
    'createdBy',
    'updatedAt',
    'updatedBy',
  ],
  '默认列序应包含全部业务列和四个审计字段',
)
assertEqual(
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER.includes('index' as never),
  false,
  '序号列不应进入可拖动列序',
)
assertEqual(
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER.includes('action' as never),
  false,
  '操作列不应进入可拖动列序',
)

assertDeepEqual(
  mergeLocalSupplierInvoiceColumnOrder(
    ['updatedBy', 'unknown', 'updatedBy', 'storeCode'],
    DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  ),
  [
    'updatedBy',
    'storeCode',
    'supplierCode',
    'invoiceNo',
    'orderDate',
    'inboundDate',
    'totalAmount',
    'receivedTotalAmount',
    'flowStatus',
    'inboundStatus',
    'remarks',
    'createdAt',
    'createdBy',
    'updatedAt',
  ],
  '持久化列序应过滤未知和重复列，并按默认顺序补齐新增列',
)
assertDeepEqual(
  mergeLocalSupplierInvoiceColumnOrder({ storeCode: true }),
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  '非数组持久化值应恢复默认列序',
)
assertDeepEqual(
  moveLocalSupplierInvoiceColumnOrder(
    DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
    'updatedAt',
    'supplierCode',
  ),
  [
    'storeCode',
    'updatedAt',
    'supplierCode',
    'invoiceNo',
    'orderDate',
    'inboundDate',
    'totalAmount',
    'receivedTotalAmount',
    'flowStatus',
    'inboundStatus',
    'remarks',
    'createdAt',
    'createdBy',
    'updatedBy',
  ],
  '拖拽应将审计字段移动到目标业务列位置',
)
assertDeepEqual(
  moveLocalSupplierInvoiceColumnOrder(
    DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
    'action',
    'storeCode',
  ),
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  '静态列不得改变业务列序',
)
assertEqual(
  isLocalSupplierInvoiceColumnOrderCustomized(DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER),
  false,
  '默认列序不应显示重置列按钮',
)
assertEqual(
  isLocalSupplierInvoiceColumnOrderCustomized(
    moveLocalSupplierInvoiceColumnOrder(
      DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
      'updatedBy',
      'storeCode',
    ),
  ),
  true,
  '调整列序后应显示重置列按钮',
)

assertDeepEqual(
  parseLocalSupplierInvoiceColumnOrder('{invalid-json'),
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  '损坏的 JSON 应恢复默认列序',
)
assertDeepEqual(
  parseLocalSupplierInvoiceColumnOrder(
    'x'.repeat(MAX_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER_STORAGE_LENGTH + 1),
  ),
  DEFAULT_LOCAL_SUPPLIER_INVOICE_COLUMN_ORDER,
  '超长存储内容应在解析前恢复默认列序',
)

let dragKeyDownCalled = 0
let stopPropagationCalled = 0
const keyboardEvent = {
  key: ' ',
  stopPropagation: () => {
    stopPropagationCalled += 1
  },
}
dispatchLocalSupplierInvoiceDragHandleKeyDown(keyboardEvent, (event) => {
  dragKeyDownCalled += 1
  assertEqual(event, keyboardEvent, '应把原键盘事件交给 dnd listener')
})
assertEqual(dragKeyDownCalled, 1, '键盘拖拽 listener 应调用一次')
assertEqual(stopPropagationCalled, 1, '键盘拖拽事件应停止冒泡，避免触发表头排序')

const dndAccessibility = createLocalSupplierInvoiceDndAccessibility(
  { storeCode: '分店', updatedAt: '更新时间' },
  {
    instructions: '键盘拖拽说明',
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
  '键盘拖拽说明',
  '读屏键盘说明应使用本地化文案',
)
assertEqual(
  dndAccessibility.announcements.onDragStart({ active: { id: 'updatedAt' } }),
  '拾取：更新时间',
  '拖拽播报应使用本地化审计字段名称',
)
assertEqual(
  dndAccessibility.announcements.onDragEnd({ active: { id: 'unknown' }, over: null }),
  '取消：当前列',
  '未知列不得向读屏暴露内部 key',
)

function interpolateLocale(template: string, values: Record<string, string>) {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.split(`{{${key}}}`).join(value),
    template,
  )
}

function createLocaleAccessibility(locale: Record<string, string>) {
  return createLocalSupplierInvoiceDndAccessibility({}, {
    instructions: locale.instructions,
    unknownColumn: locale.unknownColumn,
    dragStart: (column) => interpolateLocale(locale.dragStart, { column }),
    dragOver: (column, overColumn) =>
      interpolateLocale(locale.dragOver, { column, overColumn }),
    dragOverNone: (column) => interpolateLocale(locale.dragOverNone, { column }),
    dragEnd: (column, overColumn) =>
      interpolateLocale(locale.dragEnd, { column, overColumn }),
    dragCancel: (column) => interpolateLocale(locale.dragCancel, { column }),
  })
}

const zhDndLocale = JSON.parse(
  readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'),
).posAdmin.invoices.dnd
const enDndLocale = JSON.parse(
  readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'),
).posAdmin.invoices.dnd
assertEqual(
  createLocaleAccessibility(zhDndLocale).announcements.onDragStart({
    active: { id: 'unknown' },
  }),
  '已拾取当前列。',
  '中文未知列播报不应重复“列”字',
)
assertEqual(
  createLocaleAccessibility(enDndLocale).announcements.onDragStart({
    active: { id: 'unknown' },
  }),
  'Picked up the current column.',
  '英文未知列播报不应重复 column',
)

const pageSource = readFileSync(
  path.resolve(process.cwd(), 'src/pages/PosAdmin/LocalSupplierInvoices/index.tsx'),
  'utf8',
)
assertEqual(
  pageSource.includes('components={{ header: { cell: DraggableHeaderCell } }}'),
  true,
  '表格应接入可拖拽表头 cell',
)
assertEqual(
  pageSource.includes('horizontalListSortingStrategy'),
  true,
  '列拖拽应使用横向排序策略',
)
assertEqual(
  pageSource.includes("'data-drag-label'"),
  true,
  '拖拽手柄应提供本地化无障碍标签',
)
assertEqual(
  pageSource.includes('function SortableHeaderCell')
    && pageSource.includes('if (!columnKey) return <th'),
  true,
  '静态表头应跳过 sortable 注册，避免多个静态列共享重复拖拽 ID',
)

console.log('LocalSupplierInvoices.columnOrder.test: ok')
