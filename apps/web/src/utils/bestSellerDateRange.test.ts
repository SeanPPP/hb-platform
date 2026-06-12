import {
  BEST_SELLERS_DEFAULT_DAYS,
  buildBestSellerDateRange,
  formatLocalBusinessDate,
} from './bestSellerDateRange'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const referenceDate = new Date(2026, 5, 9, 9, 30, 0)
const last30Days = buildBestSellerDateRange(BEST_SELLERS_DEFAULT_DAYS, referenceDate)

assertEqual(BEST_SELLERS_DEFAULT_DAYS, 30, '热销商品默认日期范围应为 30 天')
assertEqual(last30Days.startDate, '2026-05-10', '热销商品 Last 30 Days 应从昨天往前 29 天')
assertEqual(last30Days.endDate, '2026-06-08', '热销商品 Last 30 Days 应以昨天为结束日')
assertEqual(last30Days.days, 30, '热销商品 Last 30 Days 应按包含首尾计算为 30 天')

const last7Days = buildBestSellerDateRange(7, referenceDate)
assertEqual(last7Days.startDate, '2026-06-02', '热销商品 Last 7 Days 应从昨天往前 6 天')
assertEqual(last7Days.endDate, '2026-06-08', '热销商品 Last 7 Days 也应以昨天为结束日')
assertEqual(last7Days.days, 7, '热销商品 Last 7 Days 应按包含首尾计算为 7 天')

assertEqual(
  formatLocalBusinessDate(new Date(2026, 5, 9, 0, 15, 0)),
  '2026-06-09',
  '业务日期格式化必须使用本地日期，不能被 UTC 转换偏到前一天',
)

console.log('bestSellerDateRange.test: ok')
