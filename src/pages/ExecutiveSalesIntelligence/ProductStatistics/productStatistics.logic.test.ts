import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import {
  getProductStatisticActionErrorMessage,
  getProductStatisticRangeDays,
  isProductStatisticRangeWithinLimit,
  MAX_PRODUCT_STATISTIC_RANGE_DAYS,
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
assert(isProductStatisticRangeWithinLimit(validRange), '30 天范围应允许提交')
assert(!isProductStatisticRangeWithinLimit(tooLongRange), '超过 31 天范围应在前端拦截')

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
