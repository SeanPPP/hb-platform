import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import {
  formatProductStatisticDateWithWeekday,
  getProductStatisticPaginationAfterLoad,
  getProductStatisticActionErrorMessage,
  getProductStatisticRangeDays,
  getProductStatisticRowNumber,
  getProductStatisticStatusTagColor,
  isProductStatisticRunning,
  isProductStatisticRangeWithinLimit,
  MAX_PRODUCT_STATISTIC_RANGE_DAYS,
  mergeUniqueDates,
} from './index'
import { RequestError } from '../../../utils/request'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const validRange = [dayjs('2026-06-01'), dayjs('2026-06-30')] as [Dayjs, Dayjs]
const tooLongRange = [dayjs('2026-06-01'), dayjs('2026-07-02')] as [Dayjs, Dayjs]

assertEqual(MAX_PRODUCT_STATISTIC_RANGE_DAYS, 31, '商品统计重算前端上限应和后端保持一致')
assertEqual(getProductStatisticRangeDays(validRange), 30, '日期范围天数应按包含首尾计算')
assertEqual(formatProductStatisticDateWithWeekday('2026-06-08'), '2026-06-08 周一', '统计日期应显示星期几')
assertEqual(formatProductStatisticDateWithWeekday('bad-date'), 'bad-date', '无效日期应保留原值')
assertEqual(formatProductStatisticDateWithWeekday('2026-02-30'), '2026-02-30', '不存在的日期不应被 dayjs 自动纠正')
assertEqual(formatProductStatisticDateWithWeekday('2026-13-01'), '2026-13-01', '不存在的月份不应被 dayjs 自动纠正')
assertEqual(getProductStatisticRowNumber(0, 1, 20), 1, '第一页首行序号应从 1 开始')
assertEqual(getProductStatisticRowNumber(2, 3, 20), 43, '后续分页序号应按页码和页容量累加')
assertEqual(
  getProductStatisticPaginationAfterLoad({ current: 3, pageSize: 20 }).current,
  3,
  '后台刷新不应重置当前页',
)
assertEqual(
  getProductStatisticPaginationAfterLoad({ current: 3, pageSize: 20 }, { resetPage: true }).current,
  1,
  '用户重新查询时应回到第一页',
)
assert(isProductStatisticRangeWithinLimit(validRange), '30 天范围应允许提交')
assert(!isProductStatisticRangeWithinLimit(tooLongRange), '超过 31 天范围应在前端拦截')
assert(isProductStatisticRunning('Queued'), '已排队状态应参与轮询')
assert(isProductStatisticRunning('Running'), '执行中状态应参与轮询')
assert(!isProductStatisticRunning('Fresh'), '已完成状态不应继续轮询')
assertEqual(getProductStatisticStatusTagColor('Queued'), 'purple', 'Queued 状态应使用紫色 Tag')
assertEqual(getProductStatisticStatusTagColor('Running'), 'processing', 'Running 状态应使用执行中蓝色 Tag')
assertEqual(getProductStatisticStatusTagColor('Pending'), 'cyan', 'Pending 状态应使用青色 Tag')
assertEqual(getProductStatisticStatusTagColor('Fresh'), 'green', 'Fresh 状态应使用绿色 Tag')
assertEqual(getProductStatisticStatusTagColor('Stale'), 'orange', 'Stale 状态应使用橙色 Tag')
assertEqual(getProductStatisticStatusTagColor('Failed'), 'red', 'Failed 状态应使用红色 Tag')
assertEqual(getProductStatisticStatusTagColor('Unknown'), 'default', '未知状态应回退默认 Tag 颜色')
assertEqual(
  mergeUniqueDates(['2026-06-02'], ['2026-06-01', '2026-06-02']).join(','),
  '2026-06-01,2026-06-02',
  '提交日期轮询列表应去重并排序',
)

const backendError = new RequestError(
  '请求失败',
  400,
  { message: '商品分店每日统计一次最多重算 31 天，请分段执行' },
)
assertEqual(
  getProductStatisticActionErrorMessage(backendError),
  '商品分店每日统计一次最多重算 31 天，请分段执行',
  '重算失败应优先展示后端返回的具体原因',
)

console.log('productStatistics.logic.test: ok')
