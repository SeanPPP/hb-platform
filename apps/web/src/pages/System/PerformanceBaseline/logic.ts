import type {
  PerformanceBaseline,
  PerformanceOverview,
  PerformancePercentile,
  SlowSqlWindow,
} from '../../../services/performanceMetricService'

export interface QualityBaselineBudget {
  schemaVersion: 'QualityBaselineBudgetV1'
  mode: 'frozen'
  metrics: Record<
    string,
    {
      max: number
      unit: 'bytes'
      required: true
    }
  >
}

const WEB_BUNDLE_BUDGET_SPECS = [
  {
    metric: 'web.first_screen.bytes',
    budgetKey: 'web.first_screen.bytes#lane=web',
    maximumHeadroomBytes: 100 * 1024,
  },
  {
    metric: 'web.largest_initial_chunk.bytes',
    budgetKey: 'web.largest_initial_chunk.bytes#lane=web',
    maximumHeadroomBytes: 50 * 1024,
  },
] as const

type PerformanceOverviewMetricKey =
  | 'api'
  | 'sql'
  | 'hqAndJobs'
  | 'webAndPos'
  | 'delivery'

export interface PerformanceGroupDefinition {
  key: 'apiAndSql' | 'hqAndJobs' | 'webAndPos' | 'delivery'
  titleKey: string
  sourceKeys: readonly PerformanceOverviewMetricKey[]
}

export type PerformanceQueryStatus = 'idle' | 'loading' | 'success' | 'error'

export interface PerformanceQueryState<T> {
  key: string
  status: PerformanceQueryStatus
  data?: T
  error?: string
}

export const PERFORMANCE_GROUPS = [
  {
    key: 'apiAndSql',
    titleKey: 'performanceBaseline.groups.apiAndSql',
    sourceKeys: ['api', 'sql'],
  },
  {
    key: 'hqAndJobs',
    titleKey: 'performanceBaseline.groups.hqAndJobs',
    sourceKeys: ['hqAndJobs'],
  },
  {
    key: 'webAndPos',
    titleKey: 'performanceBaseline.groups.webAndPos',
    sourceKeys: ['webAndPos'],
  },
  {
    key: 'delivery',
    titleKey: 'performanceBaseline.groups.delivery',
    sourceKeys: ['delivery'],
  },
] as const satisfies readonly PerformanceGroupDefinition[]

const BRISBANE_DATE_TIME_FORMATTER = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Australia/Brisbane',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
})

export function createPerformanceQueryKey(
  scope: string,
  environment: string,
  startUtc: string,
  endUtc: string,
  refreshVersion: number,
) {
  return JSON.stringify([scope, environment, startUtc, endUtc, refreshVersion])
}

export function createLoadingPerformanceQueryState<T>(key: string): PerformanceQueryState<T> {
  return { key, status: 'loading' }
}

export function settlePerformanceQuerySuccess<T>(
  current: PerformanceQueryState<T>,
  key: string,
  data: T,
): PerformanceQueryState<T> {
  // 只接受当前查询键的响应，避免被取消或较慢的旧请求覆盖新筛选结果。
  return current.key === key ? { key, status: 'success', data } : current
}

export function settlePerformanceQueryFailure<T>(
  current: PerformanceQueryState<T>,
  key: string,
  error: string,
): PerformanceQueryState<T> {
  // 失败状态不保留旧 data，调用方据此明确显示“不可用”而不是伪造 0。
  return current.key === key ? { key, status: 'error', error } : current
}

export function resolvePerformanceQueryData<T>(
  state: PerformanceQueryState<T>,
  key: string,
): T | null {
  return state.key === key && state.status === 'success' ? state.data ?? null : null
}

export function buildPerformanceGroupRows(
  overview: PerformanceOverview,
  group: PerformanceGroupDefinition,
): PerformancePercentile[] {
  return group.sourceKeys.flatMap((sourceKey) => overview[sourceKey])
}

export function getLastReportedAtUtc(overview: PerformanceOverview | null) {
  if (!overview) {
    return undefined
  }

  const timestamps = PERFORMANCE_GROUPS.flatMap((group) =>
    buildPerformanceGroupRows(overview, group)
      .map((item) => item.lastObservedAtUtc)
      .filter((value): value is string => Boolean(value)),
  )

  return timestamps.reduce<string | undefined>((latest, value) => {
    if (!latest || Date.parse(value) > Date.parse(latest)) {
      return value
    }
    return latest
  }, undefined)
}

export function formatBrisbaneDateTime(value?: string | null) {
  if (!value) {
    return '-'
  }

  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) {
    return '-'
  }

  const parts = Object.fromEntries(
    BRISBANE_DATE_TIME_FORMATTER.formatToParts(date).map((part) => [part.type, part.value]),
  )
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}:${parts.second}`
}

export function formatPerformanceMetricValue(metric: string, value?: number | null) {
  if (value === undefined || value === null || !Number.isFinite(value)) {
    return '-'
  }

  if (metric.endsWith('.bytes')) {
    if (value >= 1024 * 1024) {
      return `${(value / (1024 * 1024)).toFixed(2)} MiB`
    }
    if (value >= 1024) {
      return `${(value / 1024).toFixed(1)} KiB`
    }
    return `${Math.round(value)} B`
  }

  if (
    metric.endsWith('.ratio') ||
    metric.endsWith('.failure_rate') ||
    metric.endsWith('.success_rate')
  ) {
    return `${(value * 100).toFixed(2)}%`
  }

  if (metric.endsWith('.backlog')) {
    return Math.round(value).toLocaleString()
  }

  return `${value.toLocaleString(undefined, { maximumFractionDigits: 1 })} ms`
}

export function formatOptionalPerformanceValue(
  value: number | null | undefined,
  unit?: string,
) {
  if (value === undefined || value === null || !Number.isFinite(value)) {
    return '-'
  }
  const formatted = value.toLocaleString(undefined, { maximumFractionDigits: 1 })
  return unit ? `${formatted} ${unit}` : formatted
}

export function isInterruptedRunStatus(status: string) {
  const normalized = status.trim().toLowerCase()
  return normalized === 'cancelled' || normalized === 'canceled' || normalized === 'interrupted'
}

export function resolveOperationalRunStatusColor(status: string) {
  switch (status.trim().toLowerCase()) {
    case 'success':
    case 'succeeded':
      return 'green'
    case 'failure':
    case 'failed':
      return 'red'
    case 'cancelled':
    case 'canceled':
    case 'interrupted':
      return 'orange'
    case 'queued':
      return 'blue'
    case 'running':
      return 'processing'
    case 'retry_wait':
      return 'gold'
    default:
      return 'default'
  }
}

export function getOperationalRunRetryCount(attempt?: number | null) {
  if (attempt === undefined || attempt === null || !Number.isFinite(attempt)) {
    return undefined
  }
  return Math.max(Math.trunc(attempt) - 1, 0)
}

export function canFreezePerformanceBaseline(
  state: string | null | undefined,
  observationEndsAtUtc: string | null | undefined,
  insufficientMetricCount: number | null | undefined,
  now = new Date(),
) {
  const normalizedState = state?.trim().toLowerCase()
  if (normalizedState === 'frozen') {
    return (insufficientMetricCount ?? 0) > 0
  }

  if (normalizedState !== 'observing' || !observationEndsAtUtc) {
    return false
  }

  // 只有服务端给出的观察窗口已结束，才开放首次冻结，非法时间绝不放行。
  const observationEndsAt = new Date(observationEndsAtUtc)
  return (
    Number.isFinite(now.getTime()) &&
    Number.isFinite(observationEndsAt.getTime()) &&
    observationEndsAt.getTime() <= now.getTime()
  )
}

export function getPendingBaselineMetricCount(overview: PerformanceOverview | null) {
  if (!overview || overview.baseline.state.trim().toLowerCase() !== 'frozen') {
    return overview?.baseline.insufficientMetricCount ?? 0
  }

  const selectors = new Set<string>()
  PERFORMANCE_GROUPS.forEach((group) => {
    buildPerformanceGroupRows(overview, group).forEach((metric) => {
      if (metric.baselineP95 === undefined || metric.baselineP95 === null) {
        selectors.add(`${metric.metric}\u0000${metric.selector}`)
      }
    })
  })
  return Math.max(overview.baseline.insufficientMetricCount, selectors.size)
}

export function buildQualityBaselineBudget(
  baseline: PerformanceBaseline,
): QualityBaselineBudget {
  if (baseline.status.state.trim().toLowerCase() !== 'frozen') {
    throw new Error('性能基线尚未冻结，不能导出硬门禁预算')
  }

  const metrics: QualityBaselineBudget['metrics'] = {}
  WEB_BUNDLE_BUDGET_SPECS.forEach((spec) => {
    const definition = baseline.definitions.find(
      (item) => item.metric === spec.metric && item.selector === 'web',
    )
    if (
      !definition ||
      definition.coverageState.trim().toLowerCase() !== 'qualified' ||
      definition.gatePolicy !== 'web_bundle_hard' ||
      definition.sampleCount < 30 ||
      definition.p95 === undefined ||
      definition.p95 === null ||
      !Number.isFinite(definition.p95) ||
      definition.p95 < 0
    ) {
      throw new Error(`${spec.metric} 数据不足，不能生成虚假预算`)
    }

    metrics[spec.budgetKey] = {
      max: Math.ceil(
        definition.p95 +
          Math.min(definition.p95 * 0.05, spec.maximumHeadroomBytes),
      ),
      unit: 'bytes',
      required: true,
    }
  })

  return {
    schemaVersion: 'QualityBaselineBudgetV1',
    mode: 'frozen',
    metrics,
  }
}

export function getSlowSqlWindowRange(window: SlowSqlWindow, now = new Date()) {
  const durationMs =
    window === '1h'
      ? 60 * 60 * 1_000
      : window === '24h'
        ? 24 * 60 * 60 * 1_000
        : 7 * 24 * 60 * 60 * 1_000
  return {
    startUtc: new Date(now.getTime() - durationMs).toISOString(),
    endUtc: now.toISOString(),
  }
}
