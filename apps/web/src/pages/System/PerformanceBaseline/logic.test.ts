import type { PerformanceOverview, PerformancePercentile } from '../../../services/performanceMetricService'
import {
  PERFORMANCE_GROUPS,
  buildPerformanceGroupRows,
  buildQualityBaselineBudget,
  canFreezePerformanceBaseline,
  createLoadingPerformanceQueryState,
  createPerformanceQueryKey,
  formatBrisbaneDateTime,
  formatPerformanceMetricValue,
  formatOptionalPerformanceValue,
  getLastReportedAtUtc,
  getOperationalRunRetryCount,
  getPendingBaselineMetricCount,
  getSlowSqlWindowRange,
  isInterruptedRunStatus,
  resolvePerformanceQueryData,
  resolveOperationalRunStatusColor,
  settlePerformanceQueryFailure,
  settlePerformanceQuerySuccess,
} from './logic'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function metric(metricName: string, lastObservedAtUtc?: string): PerformancePercentile {
  return {
    metric: metricName,
    selector: 'all',
    sampleCount: 12,
    p50: 10,
    p95: 20,
    p99: 30,
    average: 15,
    maximum: 40,
    lastObservedAtUtc,
    coverageState: 'qualified',
  }
}

const overview: PerformanceOverview = {
  environment: 'Production',
  startUtc: '2026-08-24T00:00:00.000Z',
  endUtc: '2026-08-25T00:00:00.000Z',
  generatedAtUtc: '2026-08-25T00:00:01.000Z',
  baseline: {
    state: 'observing',
    observationStartedAtUtc: '2026-08-18T00:00:00.000Z',
    observationEndsAtUtc: '2026-09-01T00:00:00.000Z',
    qualifiedMetricCount: 3,
    insufficientMetricCount: 1,
  },
  api: [metric('api.request.duration', '2026-08-24T22:00:00.000Z')],
  sql: [metric('sql.command.duration', '2026-08-24T23:00:00.000Z')],
  hqAndJobs: [
    {
      ...metric('hq.sync.success_rate', '2026-08-24T21:00:00.000Z'),
      p95: 0.975,
    },
    {
      ...metric('background.job.success_rate', '2026-08-24T21:30:00.000Z'),
      p95: 0.95,
    },
  ],
  webAndPos: [metric('web.table.react_commit.duration', '2026-08-24T20:00:00.000Z')],
  delivery: [metric('ci.run.duration', '2026-08-24T23:30:00.000Z')],
  acceptedDeployments: 2,
  acceptedRollbacks: 1,
  releaseEvents: [
    {
      id: '00000000-0000-0000-0000-000000000001',
      action: 'deploy',
      status: 'failed',
      environment: 'Production',
      component: 'web',
      commit: '7f5f3ee720201b5ef7b7472872c4a20421c6890c',
      version: '2026.08.25.1',
      startedAtUtc: '2026-08-24T23:00:00.000Z',
      completedAtUtc: '2026-08-24T23:01:00.000Z',
      source: 'github-actions',
    },
  ],
}

const warningMetric: PerformancePercentile = {
  ...metric('api.request.duration', '2026-08-24T22:00:00.000Z'),
  baselineP95: null,
  warningThreshold: 100,
  isWarning: true,
  consecutiveBreaches: 3,
}

assert(PERFORMANCE_GROUPS.length === 4, '性能基线看板必须固定为四组')
assert(
  PERFORMANCE_GROUPS.map((group) => group.key).join(',') ===
    'apiAndSql,hqAndJobs,webAndPos,delivery',
  '性能基线看板必须严格按 API/SQL、HQ/后台任务、Web/POS、CI/发布分组',
)

const apiAndSqlGroup = PERFORMANCE_GROUPS.find((group) => group.key === 'apiAndSql')
assert(apiAndSqlGroup, '第一组必须合并 API 与 SQL')
assert(
  buildPerformanceGroupRows(overview, apiAndSqlGroup).map((item) => item.metric).join(',') ===
    'api.request.duration,sql.command.duration',
  'API/SQL 组必须稳定包含 api 与 sql 两个后端集合',
)
const hqAndJobsGroup = PERFORMANCE_GROUPS.find((group) => group.key === 'hqAndJobs')
assert(hqAndJobsGroup, '第二组必须是 HQ/后台任务组')
assert(
  buildPerformanceGroupRows(overview, hqAndJobsGroup)
    .map((item) => `${item.metric}:${formatPerformanceMetricValue(item.metric, item.p95)}`)
    .join(',') ===
    'hq.sync.success_rate:97.50%,background.job.success_rate:95.00%',
  'HQ/后台组必须呈现 HQ 同步与后台任务成功率，并统一按百分比显示',
)
const webAndPosGroup = PERFORMANCE_GROUPS.find((group) => group.key === 'webAndPos')
assert(webAndPosGroup, '第三组必须是独立 Web/POS 组')
assert(
  buildPerformanceGroupRows(overview, webAndPosGroup).map((item) => item.metric).join(',') ===
    'web.table.react_commit.duration',
  'Web/POS 组不得混入 delivery 指标',
)
const deliveryGroup = PERFORMANCE_GROUPS.find((group) => group.key === 'delivery')
assert(deliveryGroup, '第四组必须是独立 CI/发布组')
assert(
  buildPerformanceGroupRows(overview, deliveryGroup).map((item) => item.metric).join(',') ===
    'ci.run.duration',
  'CI/发布组必须只使用 delivery 后端集合',
)
assert(
  getLastReportedAtUtc(overview) === '2026-08-24T23:30:00.000Z',
  '最后上报时间应取全部四组指标中的最新值',
)
assert(
  formatPerformanceMetricValue('sentry.crash_free_session.ratio', 0.9876) === '98.76%',
  '*.ratio 指标必须按百分比显示',
)
assert(
  formatPerformanceMetricValue('api.request.failure_rate', 0.125) === '12.50%',
  '*.failure_rate 指标必须按百分比显示',
)
assert(
  formatPerformanceMetricValue('hq.sync.success_rate', 0.975) === '97.50%',
  '*.success_rate display_only 指标必须按百分比显示',
)
assert(
  formatPerformanceMetricValue(warningMetric.metric, warningMetric.baselineP95) === '-',
  '缺少 baselineP95 时必须显示短横线',
)
assert(
  formatOptionalPerformanceValue(undefined, 'ms') === '-',
  '运行耗时缺失时必须显示短横线，不能伪造成 0',
)
assert(
  formatOptionalPerformanceValue(0, 'ms') === '0 ms',
  '真实 0 值必须与缺失值区分显示',
)
assert(isInterruptedRunStatus('Cancelled'), 'Cancelled 状态必须在状态列明确可见')
assert(isInterruptedRunStatus('Interrupted'), 'Interrupted 状态必须在状态列明确可见')
assert(!isInterruptedRunStatus('Succeeded'), '普通成功状态不应标为取消或中断')
assert(resolveOperationalRunStatusColor('success') === 'green', 'success 必须显示绿色')
assert(resolveOperationalRunStatusColor('failure') === 'red', 'failure 必须显示红色')
assert(resolveOperationalRunStatusColor('cancelled') === 'orange', 'cancelled 必须显示橙色')
assert(resolveOperationalRunStatusColor('interrupted') === 'orange', 'interrupted 必须显示橙色')
assert(resolveOperationalRunStatusColor('queued') === 'blue', 'queued 必须显示蓝色')
assert(resolveOperationalRunStatusColor('running') === 'processing', 'running 必须显示处理中状态')
assert(resolveOperationalRunStatusColor('retry_wait') === 'gold', 'retry_wait 必须显示等待重试状态')
assert(getOperationalRunRetryCount(1) === 0, 'attempt=1 必须显示 0 次重试')
assert(getOperationalRunRetryCount(3) === 2, 'attempt=3 必须显示 2 次重试')
assert(getOperationalRunRetryCount(0) === 0, '异常 attempt=0 仍必须钳制为 0 次重试')
assert(getOperationalRunRetryCount(undefined) === undefined, '缺失 attempt 不得伪造成 0 次重试')
const freezeNow = new Date('2026-09-01T00:00:00.000Z')
assert(
  !canFreezePerformanceBaseline('not_started', '2026-08-18T00:00:00.000Z', 1, freezeNow),
  '未开始观察时不得冻结，即使存在待补指标',
)
assert(
  !canFreezePerformanceBaseline('observing', '2026-09-02T00:00:00.000Z', 0, freezeNow),
  '观察期未满 14 天时不得首次冻结',
)
assert(
  canFreezePerformanceBaseline('observing', '2026-09-01T00:00:00.000Z', 0, freezeNow),
  '观察期恰好到期时必须允许首次冻结',
)
assert(
  canFreezePerformanceBaseline('frozen', null, 1, freezeNow),
  '已冻结但仍有待补指标时必须允许补冻',
)
assert(
  !canFreezePerformanceBaseline('frozen', null, 0, freezeNow),
  '全部指标已冻结后不得重复提交',
)
assert(
  !canFreezePerformanceBaseline('observing', 'invalid', 0, freezeNow),
  '观察结束时间非法时不得开放冻结入口',
)
assert(
  getPendingBaselineMetricCount({
    ...overview,
    baseline: { ...overview.baseline, state: 'frozen', insufficientMetricCount: 0 },
    api: [{ ...overview.api[0], baselineP95: 20 }],
    sql: [{ ...overview.sql[0], baselineP95: null }],
    hqAndJobs: overview.hqAndJobs.map((item) => ({ ...item, baselineP95: item.p95 })),
    webAndPos: overview.webAndPos.map((item) => ({ ...item, baselineP95: item.p95 })),
    delivery: overview.delivery.map((item) => ({ ...item, baselineP95: item.p95 })),
  }) === 1,
  '冻结后新出现且尚无 baselineP95 的 selector 必须重新开放补冻入口',
)

const frozenBudget = buildQualityBaselineBudget({
  status: { ...overview.baseline, state: 'frozen' },
  definitions: [
    {
      metric: 'web.first_screen.bytes',
      selector: 'web',
      sampleCount: 30,
      p95: 3_000_000,
      coverageState: 'qualified',
      gatePolicy: 'web_bundle_hard',
    },
    {
      metric: 'web.largest_initial_chunk.bytes',
      selector: 'web',
      sampleCount: 30,
      p95: 2_000_000,
      coverageState: 'qualified',
      gatePolicy: 'web_bundle_hard',
    },
  ],
})
assert(frozenBudget.mode === 'frozen', '候选预算必须显式进入 frozen 模式')
assert(
  frozenBudget.metrics['web.first_screen.bytes#lane=web'].max === 3_102_400,
  '首屏 gzip 上限必须是 P95 加 min(5%, 100 KiB)',
)
assert(
  frozenBudget.metrics['web.largest_initial_chunk.bytes#lane=web'].max === 2_051_200,
  '最大初始 chunk 上限必须是 P95 加 min(5%, 50 KiB)',
)
let rejectedInsufficientBudget = false
try {
  buildQualityBaselineBudget({
    ...frozenBudget,
    status: { ...overview.baseline, state: 'frozen' },
    definitions: [],
  })
} catch {
  rejectedInsufficientBudget = true
}
assert(rejectedInsufficientBudget, '任一 Web 体积指标数据不足时不得生成虚假 frozen 预算')
assert(
  formatBrisbaneDateTime('2026-08-25T00:00:00.000Z') === '2026-08-25 10:00:00',
  '服务端 UTC 时间必须固定转换到 Australia/Brisbane，而不是浏览器本地时区',
)
assert(formatBrisbaneDateTime(null) === '-', '缺失服务端时间必须显示短横线')
assert(formatBrisbaneDateTime('invalid') === '-', '无效服务端时间必须安全显示短横线')

const productionQueryKey = createPerformanceQueryKey(
  'overview',
  'Production',
  '2026-08-24T00:00:00.000Z',
  '2026-08-25T00:00:00.000Z',
  0,
)
const stagingQueryKey = createPerformanceQueryKey(
  'overview',
  'Staging',
  '2026-08-24T00:00:00.000Z',
  '2026-08-25T00:00:00.000Z',
  0,
)
const loadingOverview = createLoadingPerformanceQueryState<PerformanceOverview>(productionQueryKey)

assert(
  resolvePerformanceQueryData(loadingOverview, productionQueryKey) === null,
  '加载中的查询不得把缺失数据伪造成 0 或沿用旧结果',
)

const loadedOverview = settlePerformanceQuerySuccess(loadingOverview, productionQueryKey, overview)
assert(
  resolvePerformanceQueryData(loadedOverview, productionQueryKey) === overview,
  '只有当前查询成功后才可读取概览结果',
)
assert(
  resolvePerformanceQueryData(loadedOverview, stagingQueryKey) === null,
  '切换环境后旧查询结果必须立即失效',
)

const failedOverview = settlePerformanceQueryFailure(
  createLoadingPerformanceQueryState<PerformanceOverview>(stagingQueryKey),
  stagingQueryKey,
  '请求失败',
)
assert(
  resolvePerformanceQueryData(failedOverview, stagingQueryKey) === null,
  '失败查询必须显示不可用，不能显示旧值或伪 0',
)
assert(
  settlePerformanceQuerySuccess(loadedOverview, stagingQueryKey, overview) === loadedOverview,
  '过期请求不得覆盖当前已绑定的查询状态',
)

const slowSqlRange = getSlowSqlWindowRange('24h', new Date('2026-08-25T00:00:00.000Z'))
assert(
  slowSqlRange.startUtc === '2026-08-24T00:00:00.000Z' &&
    slowSqlRange.endUtc === '2026-08-25T00:00:00.000Z',
  '慢 SQL 默认 24h 窗口必须生成准确 UTC 范围',
)

console.log('performanceBaseline logic tests: ok')
