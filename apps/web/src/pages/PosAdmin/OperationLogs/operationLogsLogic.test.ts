import { readFileSync } from 'node:fs'
import {
  buildOperationAuditQuery,
  buildSystemLogLink,
  formatMoney,
  formatSignedMoney,
  summarizeProducts,
} from './operationLogsLogic'
import * as operationLogsLogic from './operationLogsLogic'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  buildOperationAuditQuery({
    startUtc: '2026-07-01T00:00:00.000Z',
    endUtc: '2026-07-08T00:00:00.000Z',
    storeCode: ' S01 ',
    cashierKeyword: ' Amy ',
    deviceCode: ' POS-01 ',
    operationType: 'SALE_COMPLETE',
    outcome: 'Succeeded',
    productKeyword: ' 10001 ',
    orderGuid: ' order-1 ',
    keyword: ' trace ',
    page: 2,
    pageSize: 50,
  }),
  {
    fromUtc: '2026-07-01T00:00:00.000Z',
    toUtc: '2026-07-08T00:00:00.000Z',
    storeCode: 'S01',
    cashierKeyword: 'Amy',
    deviceCode: 'POS-01',
    operationType: 'SALE_COMPLETE',
    outcome: 'Succeeded',
    productKeyword: '10001',
    orderGuid: 'order-1',
    keyword: 'trace',
    pageNumber: 2,
    pageSize: 50,
  },
  '查询参数应裁剪文本并映射分页字段',
)

assertEqual(formatSignedMoney(12.3, 'AUD'), '+$12.30', '正金额应显示加号')
assertEqual(formatSignedMoney(-5, 'AUD'), '-$5.00', '负金额应保留负号')
assertEqual(formatSignedMoney(null, 'AUD'), '-', '缺少金额应显示占位符')
assertEqual(formatMoney(12.3, 'AUD'), '$12.30', '普通金额应显示 AUD 符号和两位小数')
assertEqual(formatMoney(-5, 'AUD'), '-$5.00', '普通金额应保留负号')

assertEqual(summarizeProducts(null), '-', '没有商品时应显示占位符')
assertEqual(
  summarizeProducts({ productCount: 3, primaryProductName: 'Milk' }),
  'Milk +2',
  '多商品应显示首个商品和剩余数量',
)
assertEqual(
  summarizeProducts({ productCount: 1 }, '商品'),
  '商品',
  '缺少商品摘要时应使用当前语言兜底',
)

assertEqual(
  buildSystemLogLink({
    deviceCode: 'POS 01',
    traceId: 'trace/1',
    occurredAtUtc: '2026-07-10T01:02:03.000Z',
  }),
  '/system/center-logs?projectCode=hbpos_win&deviceCode=POS+01&traceId=trace%2F1&fromUtc=2026-07-10T00%3A57%3A03.000Z&toUtc=2026-07-10T01%3A07%3A03.000Z',
  '系统日志跳转应携带项目、设备、Trace 和前后五分钟窗口',
)

assertEqual(
  typeof (operationLogsLogic as Record<string, unknown>).OPERATION_TYPE_KEYS,
  'object',
  '操作日志应提供固定事件代码映射',
)

const operationTypeKeys = (
  operationLogsLogic as unknown as { OPERATION_TYPE_KEYS: Record<string, string> }
).OPERATION_TYPE_KEYS
assertDeepEqual(
  Object.keys(operationTypeKeys),
  [
    'CASHIER_LOGIN',
    'CASHIER_LOGOUT',
    'CART_ITEM_ADD',
    'CART_ITEM_REMOVE',
    'CART_ITEM_QUANTITY_CHANGE',
    'CART_ITEM_PRICE_CHANGE',
    'CART_LINE_DISCOUNT_CHANGE',
    'CART_ORDER_DISCOUNT_CHANGE',
    'CART_CLEAR',
    'ORDER_HOLD',
    'ORDER_RECALL',
    'ORDER_CANCEL',
    'CASH_DRAWER_OPEN',
    'PAYMENT_TENDER_ADD',
    'PAYMENT_TENDER_REMOVE',
    'PAYMENT_CANCEL',
    'SALE_COMPLETE',
    'RETURN_REFUND_COMPLETE',
    'SALE_VOID',
    'RECEIPT_REPRINT',
    'INSTALLMENT_REPAYMENT_COMPLETE',
    'INSTALLMENT_REPAYMENT_CANCEL',
    'DAILY_CLOSE_SAVE',
    'DAILY_CLOSE_REPRINT',
  ],
  '固定事件代码应完整覆盖核心收银链路',
)

assertEqual(
  typeof (operationLogsLogic as Record<string, unknown>).normalizeOperationAuditPage,
  'function',
  '操作日志应兼容两种分页字段命名',
)
assertDeepEqual(
  (
    operationLogsLogic as unknown as {
      normalizeOperationAuditPage: (payload: unknown) => unknown
    }
  ).normalizeOperationAuditPage({ items: [{ eventId: 'event-1' }], totalCount: 1, pageIndex: 3, pageSize: 50 }),
  { items: [{ eventId: 'event-1' }], total: 1, pageNumber: 3, pageSize: 50 },
  '分页响应应统一为前端稳定结构',
)

const operationLogsPageSource = readFileSync('src/pages/PosAdmin/OperationLogs/index.tsx', 'utf8')
assertEqual(
  operationLogsPageSource.includes('formatMoney(item.beforeUnitPrice'),
  true,
  '商品详情单价应使用金额格式化',
)
assertEqual(
  operationLogsPageSource.includes('formatSignedMoney(item.actualAmountDelta'),
  true,
  '商品详情金额变化应显示正负号',
)
assertEqual(
  operationLogsPageSource.includes('item.itemNumber') && operationLogsPageSource.includes('item.lineKind'),
  true,
  '商品详情应显示货号和行类型',
)
assertEqual(
  operationLogsPageSource.includes('detailRecord.isOfflineCached') &&
    operationLogsPageSource.includes('detailRecord.isEmergencyOverride'),
  true,
  '员工详情应显示离线缓存和紧急授权快照',
)

console.log('operationLogsLogic.test: ok')
