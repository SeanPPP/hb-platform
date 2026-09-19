import type { StorePriceUpdateTask, StorePriceUpdateTaskProductStore } from '../../../services/storePriceUpdateTaskService'
import {
  buildByStoreCsvRows,
  buildCsvContent,
  buildPriceUpdateTasksQuery,
  buildStoreProgressSegments,
  buildTasksCsvRows,
  collectAllPages,
  createDefaultPriceUpdateTasksFilters,
  createPriceUpdateTasksRequestCoordinator,
  describeLabelStatus,
  describeProductStore,
  formatCsvCell,
  formatDiscount,
  formatPendingAge,
  formatRelativeTime,
  formatWaiting,
  getCompletionRateColor,
  getDefaultPriceUpdateTasksRange,
  getHqSyncStatusMeta,
  getPendingAgeDays,
  getProductChangeLines,
  getTaskPriceChange,
  parsePriceUpdateTasksSearch,
  resolveInitiatorName,
  resolveInitiatorSourceLabel,
  toPercent,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) throw new Error(`${message}: expected ${expectedJson}, received ${actualJson}`)
}

const PREFIX = 'warehouse.priceUpdateTasks.'
const t = (key: string, options?: Record<string, unknown>) => {
  const short = key.startsWith(PREFIX) ? key.slice(PREFIX.length) : key
  return options ? `${short}${JSON.stringify(options)}` : short
}

// ---------- 来源映射（契约第 1 节） ----------
for (const code of ['WarehouseProducts', 'MobileWarehouse', 'BatchUpdate', 'WarehouseAutoSync', 'LocalSupplierInvoice', 'DomesticImport', 'StoreOrderImportPriceVariance']) {
  assertEqual(resolveInitiatorSourceLabel(code, null, t), `sources.${code}`, `${code} 应映射到对应文案`)
}
assertEqual(resolveInitiatorSourceLabel('StoreSync', '1001', t), 'sources.StoreSyncWithStore{"store":"1001"}', 'StoreSync 应带来源分店代码')
assertEqual(resolveInitiatorSourceLabel('StoreSync', ' ', t), 'sources.StoreSync', 'StoreSync 无来源分店时用通用文案')
assertEqual(resolveInitiatorSourceLabel('DataSyncProducts', null, t), 'sources.DataSync', 'DataSync 前缀 = 总部数据同步')
assertEqual(resolveInitiatorSourceLabel('DataSync', null, t), 'sources.DataSync', 'DataSync 本身也按前缀处理')
assertEqual(resolveInitiatorSourceLabel('SomethingNew', null, t), 'sources.Other', '未知来源归为其它入口，不展示英文代码')
assertEqual(resolveInitiatorSourceLabel('ContainerDetail', null, t), 'sources.Container', '货柜各变体按前缀归类')
assertEqual(resolveInitiatorSourceLabel('DomesticProductBatch', null, t), 'sources.DomesticProduct', '国内采购各变体按前缀归类')
assertEqual(resolveInitiatorSourceLabel('LocalSupplierInvoiceHqProductSync', null, t), 'sources.LocalSupplierInvoice', '发票各变体按前缀归类')
assertEqual(resolveInitiatorSourceLabel('StoreOrderProductStatus', null, t), 'sources.StoreOrder', '分店订货各变体按前缀归类')
assertEqual(resolveInitiatorSourceLabel('', null, t), '--', '空来源显示占位')
assertEqual(resolveInitiatorName('System', t), 'initiator.system', 'System 显示为系统')
assertEqual(resolveInitiatorName('system', t), 'initiator.system', 'System 大小写不敏感')
assertEqual(resolveInitiatorName(' alice ', t), 'alice', '普通发起人原样显示')
assertEqual(resolveInitiatorName(null, t), '--', '空发起人显示占位')

// ---------- URL 初始化 ----------
assertDeepEqual(
  parsePriceUpdateTasksSearch('?tab=by-product&keyword=HB%20001'),
  { tab: 'by-product', keyword: 'HB 001', storeCode: '', hqSyncFailedOnly: false },
  '应从 URL 读取页签与关键字',
)
assertDeepEqual(
  parsePriceUpdateTasksSearch('?tab=tasks&storeCode=1001&hqSyncFailedOnly=true'),
  { tab: 'tasks', keyword: '', storeCode: '1001', hqSyncFailedOnly: true },
  '应从 URL 读取分店与总部同步失败筛选',
)
assertEqual(parsePriceUpdateTasksSearch('?tab=evil').tab, 'by-store', '非法页签回退到默认的按分店')
assertEqual(parsePriceUpdateTasksSearch('').tab, 'by-store', '无参数默认按分店')

// ---------- 查询 ----------
const now = new Date(2026, 8, 19, 15, 30, 0)
assertDeepEqual(getDefaultPriceUpdateTasksRange(now), { startDate: '2026-09-13', endDate: '2026-09-19' }, '默认近 7 天（含今天）')
assertDeepEqual(
  getDefaultPriceUpdateTasksRange(new Date(2026, 2, 3, 0, 5, 0)),
  { startDate: '2026-02-25', endDate: '2026-03-03' },
  '默认范围应正确跨月',
)
const query = buildPriceUpdateTasksQuery({ ...createDefaultPriceUpdateTasksFilters(now), keyword: '  HB001 ', initiatorName: ' bob ', storeCode: '1001', kind: 'LabelOnly' })
assertEqual(query.fromUtc, new Date(2026, 8, 13, 0, 0, 0, 0).toISOString(), 'fromUtc = 起始日本地零点')
assertEqual(query.toUtc, new Date(2026, 8, 19, 23, 59, 59, 999).toISOString(), 'toUtc = 结束日本地最后一毫秒')
assertEqual(query.keyword, 'HB001', '关键字应去空白')
assertEqual(query.initiatorName, 'bob', '发起人应去空白')
assertEqual(query.storeCode, '1001', '应带分店')
assertEqual(query.kind, 'LabelOnly', '应带类型')
const emptyQuery = buildPriceUpdateTasksQuery(createDefaultPriceUpdateTasksFilters(now))
assertDeepEqual(Object.keys(emptyQuery).sort(), ['fromUtc', 'toUtc'], '空筛选不应传多余参数')

// ---------- 展示 ----------
assertEqual(formatDiscount(null, t), '--', '折扣 null = 未设置')
assertEqual(formatDiscount(0, t), 'discount.none', '折扣 0 = 无折扣')
assertEqual(formatDiscount(0.2, t), 'discount.off{"percent":"20"}', '0.2 = 减 20%')
assertEqual(formatDiscount(0.125, t), 'discount.off{"percent":"12.5"}', '0.125 = 减 12.5%')
assertEqual(getCompletionRateColor(0.59), 'red', '<60% 红')
assertEqual(getCompletionRateColor(0.6), 'orange', '60% 起为橙')
assertEqual(getCompletionRateColor(0.849), 'orange', '<85% 橙')
assertEqual(getCompletionRateColor(0.85), 'green', '85% 起为绿')
assertEqual(toPercent(0.666), 67, '完成率四舍五入到整数百分比')
assertEqual(toPercent(null), 0, '空完成率按 0')

const utcNow = new Date('2026-09-19T06:00:00Z')
assertEqual(getPendingAgeDays(null, utcNow), null, '无未完成任务时无天数')
assertEqual(getPendingAgeDays('2026-09-19T01:00:00Z', utcNow), 0, '不足一天算今天')
assertEqual(getPendingAgeDays('2026-09-16T05:00:00Z', utcNow), 3, '满三天')
assertEqual(getPendingAgeDays('2026-09-16T05:00:00', utcNow), 3, '不带 Z 的时间应按 UTC 解析')
assertEqual(formatPendingAge(0, t), 'age.today', '0 天显示今天')
assertEqual(formatPendingAge(4, t), 'age.days{"count":4}', 'N 天')
assertEqual(formatPendingAge(null, t), '--', '无数据占位')
assertEqual(formatWaiting('2026-09-19T05:40:00Z', utcNow, t), 'waiting.hours{"count":1}', '不足 1 小时按 1 小时')
assertEqual(formatWaiting('2026-09-19T01:00:00Z', utcNow, t), 'waiting.hours{"count":5}', '不足一天按小时')
assertEqual(formatWaiting('2026-09-17T05:00:00Z', utcNow, t), 'waiting.days{"count":2}', '满一天按天')
assertEqual(formatRelativeTime('2026-09-19T05:59:40Z', utcNow, t), 'relative.justNow', '一分钟内 = 刚刚')
assertEqual(formatRelativeTime('2026-09-19T05:30:00Z', utcNow, t), 'relative.minutes{"count":30}', '分钟')
assertEqual(formatRelativeTime('2026-09-19T03:00:00Z', utcNow, t), 'relative.hours{"count":3}', '小时')
assertEqual(formatRelativeTime('2026-09-10T03:00:00Z', utcNow, t), 'relative.days{"count":9}', '天')
assertEqual(formatRelativeTime(undefined, utcNow, t), '--', '空时间占位')

const stores: StorePriceUpdateTaskProductStore[] = [
  { storeCode: '1001', storeName: 'A', state: 'Completed', completedBy: 'amy', completedAtUtc: '2026-09-19T01:00:00Z', completionMode: 'Printed' },
  { storeCode: '1002', storeName: 'B', state: 'LabelOnly' },
  { storeCode: '1003', storeName: '', state: 'PriceUpdate', storeRetailPrice: 5.5, initiatedAtUtc: '2026-09-17T05:00:00Z' },
  { storeCode: '1004', storeName: 'D', state: 'Skipped' },
]
assertDeepEqual(
  buildStoreProgressSegments(stores).map((segment) => `${segment.storeCode}:${segment.state}:${segment.storeName}`),
  ['1001:Completed:A', '1002:LabelOnly:B', '1003:PriceUpdate:1003'],
  '分段条不含 Skipped，无店名时回退分店代码',
)
assertEqual(describeProductStore(stores[0], utcNow, t).startsWith('amy · '), true, '已完成说明以处理人开头')
assertEqual(describeProductStore(stores[0], utcNow, t).endsWith(' · completionMode.Printed'), true, '已完成说明以完成方式结尾')
assertEqual(describeProductStore(stores[1], utcNow, t), 'storeCard.labelOnly', '待换标签说明')
assertEqual(describeProductStore(stores[2], utcNow, t), 'storeCard.priceUpdate{"price":"$5.50"} · waiting.days{"count":2}', '需改价说明含本店价与等待时长')
assertEqual(describeProductStore(stores[3], utcNow, t), 'storeCard.skipped', '特殊商品跳过说明')

assertDeepEqual(getProductChangeLines({ targetRetailPrice: 12, targetDiscountRate: null }, t), ['change.retailPrice{"value":"$12.00"}'], '只改零售价')
assertDeepEqual(
  getProductChangeLines({ targetRetailPrice: null, targetDiscountRate: 0.2 }, t),
  ['change.discount{"value":"discount.off{\\"percent\\":\\"20\\"}"}'],
  '只改建议折扣',
)
assertDeepEqual(getProductChangeLines({ targetRetailPrice: undefined, targetDiscountRate: undefined }, t), ['--'], '无目标值占位')

const baseTask: StorePriceUpdateTask = {
  id: 1, storeCode: '1001', storeName: 'A', productCode: 'P1', productName: '=cmd', itemNumber: 'HB001', status: 'Pending', kind: 'PriceUpdate',
  shelfRetailPrice: 8, shelfDiscountRate: 0, storeRetailPrice: 9, storeDiscountRate: 0.1, targetRetailPrice: 10, targetDiscountRate: null,
  initiatorName: 'System', initiatorSource: 'StoreSync', initiatorReference: '1002', initiatedAtUtc: '2026-09-18T00:00:00Z', changeCount: 1, labelPrintCount: 0,
  hqSyncStatus: 'blocked',
}
const priceUpdateChange = getTaskPriceChange(baseTask, t)
assertEqual(priceUpdateChange.from, '$9.00 · discount.off{"percent":"10"}', '需改价：变化前 = 本店现值')
assertEqual(priceUpdateChange.to, '$10.00', '需改价：变化后 = 仓库目标（折扣 null 不比较）')
assertEqual(priceUpdateChange.strikeFrom, false, '需改价不加删除线')
const labelChange = getTaskPriceChange({ ...baseTask, kind: 'LabelOnly' }, t)
assertEqual(labelChange.from, '$8.00 · discount.none', '待换标签：变化前 = 货架标签旧值')
assertEqual(labelChange.to, '$9.00 · discount.off{"percent":"10"}', '待换标签：变化后 = 分店现价')
assertEqual(labelChange.strikeFrom, true, '待换标签旧值加删除线')

assertEqual(describeLabelStatus(baseTask, t), 'labelStatus.waitingPrice', '需改价未完成：待改价')
assertEqual(describeLabelStatus({ ...baseTask, kind: 'LabelOnly' }, t), 'labelStatus.pending', '待换标签未完成：标签未处理')
assertEqual(describeLabelStatus({ ...baseTask, status: 'Completed', completionMode: 'Printed', labelPrintCount: 3 }, t), 'completionMode.Printed ×3', '多次打印显示次数')
assertEqual(describeLabelStatus({ ...baseTask, status: 'Completed', completionMode: 'KeptStorePrice', labelPrintCount: 0 }, t), 'completionMode.KeptStorePrice', '保持本店价')
assertEqual(describeLabelStatus({ ...baseTask, status: 'Cancelled' }, t), '--', '已取消无标签状态')
assertDeepEqual(getHqSyncStatusMeta('blocked', t), { color: 'error', label: 'hqSync.blocked' }, 'blocked = 同步失败')
assertEqual(getHqSyncStatusMeta(undefined, t), null, '无同步状态不显示')
assertDeepEqual(getHqSyncStatusMeta('weird', t), { color: 'default', label: 'weird' }, '未知状态原样显示')

// ---------- CSV ----------
assertEqual(formatCsvCell('a,b'), '"a,b"', '含逗号加引号')
assertEqual(formatCsvCell('say "hi"'), '"say ""hi"""', '引号转义')
assertEqual(formatCsvCell('=SUM(A1)'), "'=SUM(A1)", '公式开头应加前缀防注入')
assertEqual(formatCsvCell('-5.5'), '-5.5', '负数不算公式')
assertEqual(formatCsvCell(null), '', 'null 为空')
assertEqual(buildCsvContent([['a', 1], ['b', 2]]), '﻿a,1\r\nb,2\r\n', 'CSV 带 BOM 与 CRLF')
const tasksCsv = buildTasksCsvRows([baseTask], true, t)
assertEqual(tasksCsv[0].length, 16, '启用总部同步时导出含总部同步列')
assertEqual(buildTasksCsvRows([baseTask], false, t)[0].length, 15, '未启用总部同步时导出不含该列')
assertEqual(tasksCsv[1][8], 'initiator.system', '导出发起人同样映射 System')
assertEqual(tasksCsv[1][9], 'sources.StoreSyncWithStore{"store":"1002"}', '导出来源同样映射')
assertEqual(tasksCsv[1][15], 'hqSync.blocked', '导出总部同步状态')
const storeCsv = buildByStoreCsvRows([{ storeCode: '1001', storeName: 'A', pendingCount: 3, pendingPriceUpdateCount: 1, pendingLabelOnlyCount: 2, completedCount: 7, completionRate: 0.7, oldestPendingAtUtc: '2026-09-16T05:00:00Z' }], utcNow, t)
assertDeepEqual(storeCsv[1].slice(0, 8), ['1001', 'A', 3, 1, 2, 7, '70%', 'age.days{"count":3}'], '按分店导出行')

const pages: number[] = []
const collected = await collectAllPages(async (page, pageSize) => {
  pages.push(page)
  const all = Array.from({ length: 5 }, (_, index) => index)
  return { items: all.slice((page - 1) * pageSize, page * pageSize), total: all.length }
}, 2)
assertDeepEqual(collected, [0, 1, 2, 3, 4], '导出应翻完所有分页')
assertDeepEqual(pages, [1, 2, 3], '拿满 total 后停止翻页')
let runawayCalls = 0
await collectAllPages(async () => { runawayCalls += 1; return { items: [1], total: 999999 } }, 1, 4)
assertEqual(runawayCalls, 4, '异常 total 时受最大页数保护')

const coordinator = createPriceUpdateTasksRequestCoordinator()
const first = coordinator.start()
const second = coordinator.start()
assertEqual(first.signal.aborted, true, '新请求应终止旧请求')
assertEqual(coordinator.isLatest(first.requestId), false, '旧请求不再是最新')
assertEqual(coordinator.isLatest(second.requestId), true, '新请求为最新')
coordinator.dispose()
assertEqual(second.signal.aborted, true, '卸载时终止进行中的请求')

console.log('priceUpdateTasks.logic.test: ok')
