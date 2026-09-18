import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { buildPurchaseSalesTrendMetrics, formatMonthDay, isWeekendDate, toWholeQuantity } from './purchaseSalesTrend'

const daily = (entries: Array<[string, number]>) => entries.map(([date, quantity]) => ({ date, quantity }))

// 进货数量按整数展示：四舍五入（远离零），空值保持为 null。
assert.equal(toWholeQuantity(24), 24)
assert.equal(toWholeQuantity(12.5), 13)
assert.equal(toWholeQuantity(-2.5), -3)
assert.equal(toWholeQuantity(null), null)
assert.equal(toWholeQuantity(Number.NaN), null)

// 窗口从上次进货开始：指标只统计最近进货当天起的销量。
const metrics = buildPurchaseSalesTrendMetrics({
  latestPurchaseDate: '2026-06-03T00:00:00',
  latestPurchaseQty: 10,
  purchases: [{ date: '2026-06-01', quantity: 8 }, { date: '2026-06-03', quantity: 10 }],
  dailySales: daily([['2026-06-01', 5], ['2026-06-02', 5], ['2026-06-03', 4], ['2026-06-04', -1], ['2026-06-05', 7], ['2026-06-06', 2]]),
})
assert.equal(metrics.latestIndex, 2, '最近进货日应定位到序列下标')
assert.equal(metrics.sinceLatest.length, 4)
assert.equal(metrics.totalSinceLatest, 12, '累计销量应含退货负数且不含最近进货之前的销量')
assert.equal(metrics.averagePerDay, 3)
assert.equal(metrics.purchasedQuantity, 10)
assert.equal(metrics.sellThrough, 1.2)
assert.equal(metrics.soldOutDate, '2026-06-05', '累计销量首次达到进货量的日期即卖完日')

// 未卖完、无进货量、序列不含最近进货日等边界。
const notSoldOut = buildPurchaseSalesTrendMetrics({
  latestPurchaseDate: '2026-06-01',
  latestPurchaseQty: 100,
  purchases: [],
  dailySales: daily([['2026-06-01', 1], ['2026-06-02', 2]]),
})
assert.equal(notSoldOut.soldOutDate, null)
assert.equal(notSoldOut.sellThrough, 0.03)

const noPurchaseQuantity = buildPurchaseSalesTrendMetrics({ latestPurchaseDate: null, latestPurchaseQty: null, purchases: [], dailySales: daily([['2026-06-01', 4]]) })
assert.equal(noPurchaseQuantity.sellThrough, null, '进货量为 0 时售出比不可计算')
assert.equal(noPurchaseQuantity.latestIndex, 0)

const empty = buildPurchaseSalesTrendMetrics({ latestPurchaseDate: '2026-06-01', latestPurchaseQty: 5, purchases: [], dailySales: [] })
assert.equal(empty.averagePerDay, 0)
assert.equal(empty.totalSinceLatest, 0)

// 周末判定与日期格式化只依赖日期文本，不受运行时区影响。
assert.equal(isWeekendDate('2026-09-19'), true)
assert.equal(isWeekendDate('2026-09-20'), true)
assert.equal(isWeekendDate('2026-09-18'), false)
assert.equal(formatMonthDay('2026-09-18'), '09-18')

// 文案契约：图表文案随页面懒注册，中英文键集合必须一致，且不得回流到首屏主文案包。
const flatten = (value: unknown, prefix = ''): string[] =>
  value && typeof value === 'object'
    ? Object.entries(value as Record<string, unknown>).flatMap(([key, child]) => flatten(child, prefix ? `${prefix}.${key}` : key))
    : [prefix]
const zh = JSON.parse(readFileSync('src/components/PurchaseSalesTrend/messages.zh.json', 'utf8'))
const en = JSON.parse(readFileSync('src/components/PurchaseSalesTrend/messages.en.json', 'utf8'))
assert.deepEqual(flatten(zh).sort(), flatten(en).sort(), '图表中英文文案键必须一致')
const mainZh = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8'))
assert.equal(mainZh.purchaseSalesTrend, undefined, '图表文案必须懒注册，不能进入首屏主文案包')
assert.equal(mainZh.posAdmin.localSupplierPurchaseSalesAnalysis.columns.salesQty30, undefined, '已下线的 30/60/90 天列文案应从主文案包移除')
assert.equal(mainZh.shop.purchaseSalesAnalysis, undefined, '前台导航增量文案随 ShopLayout 懒注册')

console.log('purchaseSalesTrend.test: ok')
