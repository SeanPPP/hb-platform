import { OrderType } from '../../types/posmSalesOrder'

import {
  DEFAULT_SORT,
  DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS,
  MAX_RANGE_DAYS,
  buildFilterChips,
  buildListQuery,
  countDays,
  countMoreFilters,
  createDefaultFilters,
  detectDatePreset,
  formatMoney,
  isLatestRequest,
  mapTableSort,
  normalizeFilterNumber,
  resolveDatePreset,
  shortOrderNo,
  stepOrderIndex,
  summarizeStatuses,
  tableSortOrder,
  validateFilters,
} from './posmSalesOrdersLogic'
import { formatPosmSalesOrderTime } from './time'

// 固定业务测试时区，避免 CI 机器默认 UTC 导致跨日与 offset 断言不稳定。
process.env.TZ = 'Australia/Brisbane'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assert(condition: boolean, label: string) {
  if (!condition) throw new Error(label)
}

const today = '2026-09-19'

// 日期快捷项：今天、昨天、近 7 天（含今天）、本月；自定义区间不高亮任何快捷项。
assertDeepEqual(resolveDatePreset('today', today), ['2026-09-19', '2026-09-19'], '今天')
assertDeepEqual(resolveDatePreset('yesterday', today), ['2026-09-18', '2026-09-18'], '昨天')
assertDeepEqual(resolveDatePreset('last7', today), ['2026-09-13', '2026-09-19'], '近 7 天含今天共 7 天')
assertDeepEqual(resolveDatePreset('thisMonth', today), ['2026-09-01', '2026-09-19'], '本月从 1 号到今天')
assert(detectDatePreset('2026-09-13', '2026-09-19', today) === 'last7', '区间正好是近 7 天时高亮该快捷项')
assert(detectDatePreset('2026-09-10', '2026-09-19', today) === null, '自定义区间不高亮快捷项')
assert(countDays('2026-09-19', '2026-09-19') === 1 && countDays('2026-06-20', '2026-09-19') === 92, '区间天数含首尾')

// 校验与后端同口径：区间必填且最长 92 天；件数、种数在全部分店时最长 7 天；下限不能大于上限。
const base = createDefaultFilters(today)
assert(validateFilters(base).ok, '默认条件（今天、全部分店）可以直接查询')
assert(base.status === OrderType.All && base.keyword === '' && base.branchCode === '', '默认不限状态、关键词与分店')
const tooLong = validateFilters({ ...base, startDate: '2026-06-19', endDate: today })
assert(!tooLong.ok && tooLong.reason === 'rangeTooLong', `超过 ${MAX_RANGE_DAYS} 天应拒绝`)
const reversed = validateFilters({ ...base, startDate: '2026-09-20', endDate: today })
assert(!reversed.ok && reversed.reason === 'rangeRequired', '开始晚于结束应拒绝')
const eightDays = { ...base, startDate: '2026-09-12', endDate: today }
const detailAllStores = validateFilters({ ...eightDays, quantityMin: 5 })
assert(
  !detailAllStores.ok && detailAllStores.reason === 'detailRangeTooLong',
  `件数条件在全部分店超过 ${DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS} 天应拒绝`,
)
assert(validateFilters({ ...eightDays, quantityMin: 5, branchCode: '1005' }).ok, '指定分店后件数条件沿用 92 天上限')
assert(validateFilters({ ...eightDays, actualPayMin: 20 }).ok, '实收金额只查订单主表，不受 7 天约束')
const invalidRange = validateFilters({ ...base, skuCountMin: 5, skuCountMax: 2 })
assert(!invalidRange.ok && invalidRange.reason === 'invalidNumberRange', '种数下限大于上限应拒绝')

// 查询参数：字符串去空白，空值不发送；收银机映射到包含匹配的 deviceCodeKeyword。
assertDeepEqual(
  buildListQuery(
    { ...base, branchCode: ' 1005 ', keyword: '  HB022-249 ', deviceCode: ' 1231 ', status: OrderType.Refunded, actualPayMin: 20 },
    { field: 'actualPay', direction: 'asc' },
    3,
    50,
  ),
  {
    startDate: today,
    endDate: today,
    branchCode: '1005',
    orderType: OrderType.Refunded,
    keyword: 'HB022-249',
    deviceCodeKeyword: '1231',
    actualPayMin: 20,
    sortField: 'actualPay',
    sortDirection: 'asc',
    pageNumber: 3,
    pageSize: 50,
  },
  '查询参数应规整后发送',
)

// 表头排序：默认时间倒序；取消排序回到默认；只有白名单列可排序。
assertDeepEqual(DEFAULT_SORT, { field: 'orderTime', direction: 'desc' }, '默认按时间倒序，最新在前')
assertDeepEqual(mapTableSort('actualPay', 'ascend'), { field: 'actualPay', direction: 'asc' }, '实收升序')
assertDeepEqual(mapTableSort('branch', 'descend'), { field: 'branchCode', direction: 'desc' }, '分店列映射到分店编码')
assertDeepEqual(mapTableSort('actualPay', null), DEFAULT_SORT, '取消排序回到时间倒序')
assertDeepEqual(mapTableSort('goods', 'ascend'), DEFAULT_SORT, '商品列（件数/种数）不提供排序')
assert(tableSortOrder(DEFAULT_SORT, 'time') === 'descend' && tableSortOrder(DEFAULT_SORT, 'actualPay') === null, '表头排序高亮')

// 状态汇总：净实收 = 已支付实收 + 退款冲减；已取消等不计入；客单价按已支付单数。
const view = summarizeStatuses([
  { status: 1, orderCount: 7708, totalAmount: 96782.8, discountAmount: 849.05 },
  { status: 3, orderCount: 24, totalAmount: -425.09, discountAmount: 0 },
  { status: 2, orderCount: 72, totalAmount: 2013.84, discountAmount: 37.81 },
])
assert(view.allCount === 7804, '全部单数包含所有状态')
assert(view.netAmount === 95508.66, `净实收应为 95508.66，实际 ${view.netAmount}`)
assert(view.discountTotal === 849.05, '折扣合计只算计入实收的状态')
assert(view.averageTicket === 12.45, `客单价应为 12.45，实际 ${view.averageTicket}`)
assertDeepEqual(
  view.cards.map((card) => [card.status, card.orderCount, card.counted]),
  [
    [OrderType.Paid, 7708, true],
    [OrderType.Refunded, 24, true],
    [OrderType.Cancelled, 72, false],
  ],
  '汇总卡片固定为已支付、退款、已取消；没有数据的待处理、分期不出现',
)
const withPending = summarizeStatuses([{ status: 0, orderCount: 3, totalAmount: 9, discountAmount: 0 }])
assert(withPending.cards.some((card) => card.status === OrderType.Pending && !card.counted), '有待处理订单时出现且不计入实收')
assert(withPending.averageTicket === null, '没有已支付订单时客单价为空')

// 金额与订单号显示。
assert(formatMoney(95508.66) === '$95,508.66', '金额带千分位与两位小数')
assert(formatMoney(-425.09) === '−$425.09', '负数用减号放在货币符号前')
assert(formatMoney(undefined) === '$0.00', '空金额显示 0')
assert(shortOrderNo('01A0B860-80A2-7A2A-B0F2-C9B5AF1B4D21') === '1B4D21', '订单号显示后 6 位')
assert(shortOrderNo('e7c1b4d2-1111-4222-8333-aaaaaaaaaaaa') === 'AAAAAA', '小写订单号统一大写显示')

// 已生效筛选条：日期与状态不重复成标签，其余逐个列出且可单独清除。
const chips = buildFilterChips(
  {
    ...base,
    branchCode: '1005',
    keyword: ' squishy ',
    timeStart: '15:00:00',
    timeEnd: '17:00:59',
    actualPayMin: 20,
    quantityMax: 3,
  },
  (code) => (code === '1005' ? 'Charlestown Square' : code),
)
assertDeepEqual(
  chips.map((chip) => [chip.field, chip.value]),
  [
    ['branch', 'Charlestown Square'],
    ['keyword', 'squishy'],
    ['time', '15:00 – 17:00'],
    ['actualPay', '≥ $20.00'],
    ['quantity', '≤ 3'],
  ],
  '已生效筛选标签',
)
assertDeepEqual(chips[2].clear, { timeStart: undefined, timeEnd: undefined }, '移除时段标签同时清除起止时间')
assert(countMoreFilters({ deviceCode: ' ', timeStart: '09:00:00', quantityMin: 1, quantityMax: 2 }) === 2, '更多筛选角标按条件组计数')

// 数字输入与抽屉切换。
assert(normalizeFilterNumber('', true) === undefined && normalizeFilterNumber(2.6, true) === 3, '件数取整，空值视为未设置')
assert(normalizeFilterNumber(19.995, false) === 19.995, '金额保留小数')
assert(stepOrderIndex(0, -1, 50) === 0 && stepOrderIndex(49, 1, 50) === 49 && stepOrderIndex(3, 1, 50) === 4, '抽屉切换到头停住')
assert(isLatestRequest(3, 3) && !isLatestRequest(2, 3), '只有最后一次请求能更新列表')

function formatLocalTime(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

assert(
  formatPosmSalesOrderTime('2026-07-17T00:15:01', 'YYYY-MM-DD HH:mm:ss') ===
    '2026-07-17 00:15:01',
  '无时区后缀的订单时间是门店墙钟时间，必须按字面显示，不做时区换算',
)
assert(
  formatPosmSalesOrderTime('2026-07-17 00:15:01', 'HH:mm:ss') === '00:15:01',
  '空格分隔的无后缀订单时间同样按字面显示',
)
assert(
  formatPosmSalesOrderTime('2026-07-17 17:05:11.1234567', 'YYYY-MM-DD HH:mm:ss') ===
    '2026-07-17 17:05:11',
  '.NET 七位小数秒的墙钟时间应被接受且不换算',
)

const explicitUtc = '2026-07-17T00:15:01Z'
assert(
  formatPosmSalesOrderTime(explicitUtc, 'HH:mm:ss') ===
    formatLocalTime(new Date(explicitUtc)),
  '带 Z 的订单时间才按显式 UTC 转换为浏览器本地时间',
)

const explicitOffset = '2026-07-17T00:15:01+02:00'
assert(
  formatPosmSalesOrderTime(explicitOffset, 'HH:mm:ss') ===
    formatLocalTime(new Date(explicitOffset)),
  '带 offset 的订单时间应保留原时区语义并转换为浏览器本地时间',
)

assert(
  formatPosmSalesOrderTime('not-a-date', 'HH:mm:ss') === 'not-a-date',
  '非法订单时间应保留原文，避免隐藏后端异常数据',
)
assert(
  formatPosmSalesOrderTime('2026-02-30T00:00:00', 'YYYY-MM-DD') ===
    '2026-02-30T00:00:00',
  '不存在的日历日期不得被 dayjs 正常化',
)
for (const invalidTimestamp of [
  '0000-01-01T00:00:00Z',
  '2026-13-01T00:00:00Z',
  '2026-07-17T24:00:00Z',
  '2026-07-17T00:60:00Z',
  '2026-07-17T00:00:60Z',
  '2026-07-17T00:00:00.12345678Z',
  '2026-07-17T00:00:00+14:01',
  '2026-07-17T00:00:00+1060',
]) {
  assert(
    formatPosmSalesOrderTime(invalidTimestamp, 'YYYY-MM-DD') === invalidTimestamp,
    `越界或不符合 .NET/ISO 格式的订单时间应保留原文: ${invalidTimestamp}`,
  )
}
assert(
  formatPosmSalesOrderTime('', 'HH:mm:ss') === '-' &&
    formatPosmSalesOrderTime(null, 'HH:mm:ss') === '-' &&
    formatPosmSalesOrderTime(undefined, 'HH:mm:ss') === '-',
  '空订单时间应显示占位符',
)

console.log('posmSalesOrdersLogic.test: ok')
