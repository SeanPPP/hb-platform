import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import {
  buildProductStatisticYearBackfillMessage,
  buildProductStatisticYearBackfillRequest,
  createProductStatisticSingleFlightGate,
  getBestSellerDefaultStatisticRange,
  formatProductStatisticDateWithWeekday,
  getProductStatisticPaginationAfterLoad,
  getProductStatisticActionErrorMessage,
  getProductStatisticConcurrency,
  getProductStatisticRangeDays,
  getProductStatisticRowNumber,
  getProductStatisticStatusTagColor,
  isProductStatisticRunning,
  isProductStatisticRangeWithinLimit,
  MAX_PRODUCT_STATISTIC_RANGE_DAYS,
  PRODUCT_STATISTIC_YEAR_BACKFILL_DAYS,
  requestProductStatisticYearBackfillConfirmation,
  selectProductStatisticPollDates,
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
assertEqual(PRODUCT_STATISTIC_YEAR_BACKFILL_DAYS, 365, '年度回填应固定提交 365 天')
assertEqual(getProductStatisticConcurrency(), 3, '商品统计范围重算默认并发应为 3')
assertEqual(getProductStatisticConcurrency(0), 3, '商品统计范围重算异常并发应回退默认值')
assertEqual(getProductStatisticConcurrency(11), 10, '商品统计范围重算并发最大应限制为 10')
assertEqual(getProductStatisticConcurrency(4.8), 4, '商品统计范围重算并发应取整数')
assertEqual(getProductStatisticRangeDays(validRange), 30, '日期范围天数应按包含首尾计算')
const bestSellerDefaultRange = getBestSellerDefaultStatisticRange(dayjs('2026-06-09'))
assertEqual(
  bestSellerDefaultRange[0].format('YYYY-MM-DD'),
  '2026-05-10',
  '前台默认热销范围应从昨天往前 29 天',
)
assertEqual(
  bestSellerDefaultRange[1].format('YYYY-MM-DD'),
  '2026-06-08',
  '前台默认热销范围应以昨天为结束日',
)
assertEqual(getProductStatisticRangeDays(bestSellerDefaultRange), 30, '前台默认热销范围应含首尾正好 30 天')
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
assertEqual(
  selectProductStatisticPollDates(['2026-06-01', '2026-06-02'], true).join(','),
  '2026-06-01,2026-06-02',
  '普通重算应返回已提交日期供轮询',
)
assertEqual(
  selectProductStatisticPollDates(['2025-06-10', '2026-06-09'], false).length,
  0,
  '年度回填应返回空轮询日期，不跟踪大批 submittedDates',
)

const annualRequest = buildProductStatisticYearBackfillRequest(dayjs('2026-06-09'), 12.8)
assertEqual(annualRequest.endDate, '2026-06-09', '年度回填应以今天为含当天结束日')
assertEqual(annualRequest.days, 365, '年度回填应使用固定天数')
assertEqual(annualRequest.maxConcurrency, 10, '年度回填并发应复用 1..10 归一化')
assertEqual(
  buildProductStatisticYearBackfillMessage({
    taskId: 'annual-task-1',
    jobId: 'annual-task-1',
    submittedDates: Array.from({ length: 365 }, (_, index) => `submitted-${index}`),
    skippedDates: ['2025-12-25', '2026-01-01'],
  }),
  '年度回填已提交：提交 365 天，跳过 2 天，任务 ID：annual-task-1',
  '年度提示只应摘要数量和真实任务 ID，不展开跳过日期',
)
assertEqual(
  buildProductStatisticYearBackfillMessage({
    message: '所选 365 天分属 2 个活动商品统计任务，本次未重复提交',
    submittedDates: [],
    skippedDates: Array.from({ length: 365 }, (_, index) => `skipped-${index}`),
    activeTaskIds: ['active-task-1', 'active-task-2'],
  }),
  '所选 365 天分属 2 个活动商品统计任务，本次未重复提交',
  '全量跳过时应保留后端未重复提交语义，不显示占位任务 ID',
)

type CapturedYearBackfillConfirmation = {
  onOk: () => Promise<void>
  onCancel: () => void
}

const singleFlightGate = createProductStatisticSingleFlightGate()
let confirmationCount = 0
let annualRequestCount = 0
let capturedConfirmation: CapturedYearBackfillConfirmation | null = null
const gateStates: boolean[] = []
const captureConfirmation = (confirmation: CapturedYearBackfillConfirmation) => {
  confirmationCount += 1
  capturedConfirmation = confirmation
}
const getCapturedConfirmation = () => {
  assert(capturedConfirmation, '应捕获年度回填确认回调')
  return capturedConfirmation
}
const startConfirmation = () => requestProductStatisticYearBackfillConfirmation({
  gate: singleFlightGate,
  confirm: captureConfirmation,
  action: async () => {
    annualRequestCount += 1
  },
  onGateChange: (locked) => gateStates.push(locked),
})

assert(startConfirmation(), '首次年度回填应打开确认框')
assert(!startConfirmation(), '确认框已打开时快速重复点击应被同步门禁拦截')
assertEqual(confirmationCount, 1, '快速重复点击只能打开一个确认框')
getCapturedConfirmation().onCancel()
await getCapturedConfirmation().onOk()
assertEqual(annualRequestCount, 0, '取消年度回填不得发起请求')
assert(!singleFlightGate.locked, '取消应释放 single-flight 门禁')

assert(startConfirmation(), '取消后应允许再次打开年度回填确认框')
const confirmedSubmission = getCapturedConfirmation()
const firstConfirmPromise = confirmedSubmission.onOk()
const repeatedConfirmPromise = confirmedSubmission.onOk()
assertEqual(repeatedConfirmPromise, firstConfirmPromise, '重复确认应复用同一个提交 Promise')
await Promise.all([firstConfirmPromise, repeatedConfirmPromise])
assertEqual(annualRequestCount, 1, '重复确认也只能发起一次年度回填请求')
assert(!singleFlightGate.locked, '年度回填提交完成后应释放 single-flight 门禁')
assertEqual(gateStates.join(','), 'true,false,true,false', '按钮锁定状态应与门禁获取和释放同步')

const failedGate = createProductStatisticSingleFlightGate()
const expectedFailure = new Error('年度回填失败')
let failedConfirmation: CapturedYearBackfillConfirmation | null = null
requestProductStatisticYearBackfillConfirmation({
  gate: failedGate,
  confirm: (confirmation) => {
    failedConfirmation = confirmation
  },
  action: async () => {
    throw expectedFailure
  },
})
assert(failedConfirmation, '应捕获失败路径的确认回调')
const rejectedConfirmation = failedConfirmation as CapturedYearBackfillConfirmation
let actualFailure: unknown
try {
  await rejectedConfirmation.onOk()
} catch (error) {
  actualFailure = error
}
assertEqual(actualFailure, expectedFailure, '年度回填失败应保留原始错误')
assert(!failedGate.locked, '年度回填失败后也必须释放 single-flight 门禁')

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
