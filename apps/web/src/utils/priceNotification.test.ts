import {
  buildPriceNotificationPreviewText,
  buildPriceNotificationView,
  buildPriceUpdateTasksLink,
  computeSuggestedDiscountedPrice,
  createPriceNotificationCapture,
  normalizePriceNotificationSummary,
  parsePriceNotificationHeader,
  suggestedDiscountPercentToRate,
  suggestedDiscountRateToPercent,
} from './priceNotification'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) throw new Error(`${message}: expected ${expectedJson}, received ${actualJson}`)
}

// 测试用翻译：输出 key 与参数，便于断言选用了哪条文案。
const t = (key: string, options?: Record<string, unknown>) =>
  options ? `${key.replace('warehouse.priceNotification.', '')}${JSON.stringify(options)}` : key.replace('warehouse.priceNotification.', '')

const header = '{"productCount":1,"needsPriceUpdateStores":6,"labelOnlyStores":2,"cancelledStores":0,"skippedSpecialStores":1,"hasAny":true}'
assertDeepEqual(
  parsePriceNotificationHeader(header),
  { productCount: 1, needsPriceUpdateStores: 6, labelOnlyStores: 2, cancelledStores: 0, skippedSpecialStores: 1, hasAny: true },
  '应解析契约示例响应头',
)
assertEqual(parsePriceNotificationHeader(null), null, '头不存在应返回 null（不提示）')
assertEqual(parsePriceNotificationHeader(''), null, '空头应返回 null')
assertEqual(parsePriceNotificationHeader('{not json'), null, '解析失败应静默忽略')
assertEqual(parsePriceNotificationHeader('[1,2]'), null, '非对象 JSON 应忽略')
assertEqual(normalizePriceNotificationSummary(null), null, '任务快照 priceNotification 为 null 表示未涉及通知')
assertEqual(
  normalizePriceNotificationSummary({ needsPriceUpdateStores: 0, labelOnlyStores: 0, cancelledStores: 0, hasAny: true })?.hasAny,
  false,
  'hasAny 应按计数重新推导',
)
assertEqual(
  normalizePriceNotificationSummary({ NeedsPriceUpdateStores: '3' })?.needsPriceUpdateStores,
  3,
  '应兼容 PascalCase 与字符串数字',
)

const capture = createPriceNotificationCapture()
const fakeResponse = (value: string | null) => ({ headers: { get: (name: string) => (name === 'x-price-notification' ? value : null) } })
capture.onResponse(fakeResponse(header))
capture.onResponse(fakeResponse(null))
assertEqual(capture.getSummary()?.needsPriceUpdateStores, 6, '后续不带头的响应不应清掉之前的汇总')
capture.onResponse(fakeResponse('{"needsPriceUpdateStores":4,"labelOnlyStores":0,"cancelledStores":0,"skippedSpecialStores":0}'))
assertEqual(capture.getSummary()?.needsPriceUpdateStores, 4, '多个响应以最后一个带头的为准，不相加')
assertEqual(capture.getSummary()?.labelOnlyStores, 0, '最后一个响应整体覆盖，不与之前合并')
const jobCapture = createPriceNotificationCapture()
jobCapture.accept(null)
assertEqual(jobCapture.getSummary(), null, '任务快照无汇总时保持 null')
jobCapture.accept(parsePriceNotificationHeader(header))
assertEqual(jobCapture.getSummary()?.labelOnlyStores, 2, '应能接收任务快照里的汇总')

assertEqual(buildPriceNotificationView(null, t), null, '无汇总不提示')
const sent = buildPriceNotificationView(parsePriceNotificationHeader(header), t)
assertEqual(sent?.tone, 'success', '有通知应为 success')
assertEqual(sent?.title, 'sentTitle', '有通知标题')
assertDeepEqual(
  sent?.tags,
  [{ kind: 'priceUpdate', text: 'needsPriceUpdate{"count":6}' }, { kind: 'labelOnly', text: 'labelOnly{"count":2}' }],
  '应输出需改价/待换标签两个标签',
)
assertDeepEqual(sent?.lines, ['skippedSpecial{"count":1}'], '应提示跳过特殊商品分店数')
assertEqual(sent?.showTasksLink, true, '有通知时提供查看执行情况链接')

const none = buildPriceNotificationView(parsePriceNotificationHeader('{"productCount":1,"hasAny":false}'), t)
assertEqual(none?.tone, 'info', 'hasAny=false 应为 info')
assertEqual(none?.title, 'noneTitle', '未产生通知标题')
assertDeepEqual(none?.lines, ['noneDescription'], '未产生通知说明')
assertEqual(none?.showTasksLink, false, '未产生通知不提供链接')

const cancelled = buildPriceNotificationView(parsePriceNotificationHeader('{"cancelledStores":3}'), t)
assertEqual(cancelled?.tone, 'warning', '仅撤销应为 warning')
assertEqual(cancelled?.title, 'cancelledTitle{"count":3}', '撤销标题带分店数')

const mixed = buildPriceNotificationView(parsePriceNotificationHeader('{"labelOnlyStores":2,"cancelledStores":1}'), t)
assertEqual(mixed?.tone, 'success', '同时有新通知与撤销时以新通知为主')
assertDeepEqual(mixed?.lines, ['cancelledAlso{"count":1}'], '撤销数作为补充说明')

assertEqual(buildPriceNotificationPreviewText(null, true, t), null, '无预告不显示')
assertEqual(buildPriceNotificationPreviewText({ affectedStores: 0, skippedSpecialStores: 2 }, true, t), null, 'N=0 不显示')
assertEqual(
  buildPriceNotificationPreviewText({ affectedStores: 5, skippedSpecialStores: 0 }, true, t),
  'previewRetail{"count":5}previewEnd',
  '改零售价使用下发 + 待换标签文案',
)
assertEqual(
  buildPriceNotificationPreviewText({ affectedStores: 5, skippedSpecialStores: 2 }, false, t),
  'previewDiscountOnly{"count":5}previewSkipped{"count":2}previewEnd',
  '只改建议折扣使用需改价文案，并带特殊商品跳过数',
)

assertEqual(suggestedDiscountPercentToRate(null), null, '留空 = 未设置')
assertEqual(suggestedDiscountPercentToRate(undefined), null, 'undefined = 未设置')
assertEqual(suggestedDiscountPercentToRate(0), 0, '0 = 明确无折扣，不能被当成未设置')
assertEqual(suggestedDiscountPercentToRate(20), 0.2, '20% → 0.2')
assertEqual(suggestedDiscountPercentToRate(12.5), 0.125, '12.5% → 0.125')
assertEqual(suggestedDiscountPercentToRate(150), 1, '超出范围应夹到 100%')
assertEqual(suggestedDiscountRateToPercent(null), null, 'null 回填为空')
assertEqual(suggestedDiscountRateToPercent(0), 0, '0 回填为 0')
assertEqual(suggestedDiscountRateToPercent(0.07), 7, '0.07 → 7（无浮点误差）')
assertEqual(suggestedDiscountPercentToRate(suggestedDiscountRateToPercent(0.335)), 0.335, '往返转换不应让未改动的字段变脏')
assertEqual(computeSuggestedDiscountedPrice(10, 20), 8, '建议折后价 = 零售价 × (1 - 折扣)')
assertEqual(computeSuggestedDiscountedPrice(9.99, 15), 8.49, '折后价保留两位小数')
assertEqual(computeSuggestedDiscountedPrice(10, null), null, '未设置折扣时不显示折后价')
assertEqual(computeSuggestedDiscountedPrice(null, 20), null, '无零售价时不显示折后价')

assertEqual(
  buildPriceUpdateTasksLink({ tab: 'by-product', keyword: ' HB 001 ' }),
  '/warehouse/products/price-update-tasks?tab=by-product&keyword=HB+001',
  '链接应带页签与货号关键字',
)
assertEqual(
  buildPriceUpdateTasksLink({ tab: 'tasks', hqSyncFailedOnly: true }),
  '/warehouse/products/price-update-tasks?tab=tasks&hqSyncFailedOnly=true',
  '链接应支持 hqSyncFailedOnly',
)
assertEqual(buildPriceUpdateTasksLink({}), '/warehouse/products/price-update-tasks', '无参数时不带问号')

console.log('priceNotification.test: ok')
