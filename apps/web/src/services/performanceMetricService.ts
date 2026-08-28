import type { ApiResponse } from '../types/api'
import request, { unwrapApiData } from '../utils/request'
import { postCenterLogAuthorizedJson } from '../utils/centerLogClient'

const importMetaEnv = (import.meta as ImportMeta & { env?: ImportMetaEnv }).env ?? {}
// 与 Center Log 使用相同的公开配置项；指标环境必须由部署显式声明，不能猜测为 Production。
const CENTER_LOG_ENVIRONMENT = (importMetaEnv.VITE_CENTER_LOG_ENVIRONMENT || '').trim()
const CLIENT_METRIC_BATCH_PATH = '/api/system/performance/client-batches'
const PERFORMANCE_OVERVIEW_PATH = '/api/system/performance/overview'
const PERFORMANCE_BASELINE_PATH = '/api/system/performance/baseline'
const PERFORMANCE_FREEZE_PATH = '/api/system/performance/baseline/freeze'
const PERFORMANCE_SLOW_SQL_PATH = '/api/system/performance/slow-sql'
const PERFORMANCE_RUNS_PATH = '/api/system/performance/runs'
const PERFORMANCE_SAMPLING_STORAGE_KEY = 'hbweb.performance.sampling-policy.v1'
const PERFORMANCE_SESSION_STORAGE_KEY = 'hbweb.performance.session-id.v1'
const MAX_BATCH_EVENTS = 200
const MAX_PENDING_EVENTS = 400
const FLUSH_DELAY_MS = 1_000

export const WEB_TABLE_REACT_COMMIT_METRIC = 'web.table.react_commit.duration'
export const WEB_TABLE_RENDER_TO_PAINT_METRIC = 'web.table.render_to_paint.duration'

export type WebTableMetricName =
  | typeof WEB_TABLE_REACT_COMMIT_METRIC
  | typeof WEB_TABLE_RENDER_TO_PAINT_METRIC

export interface PerformanceMetricEventV1 {
  eventId: string
  metric: WebTableMetricName
  observedAt: string
  value: number
  unit: 'ms'
  dimensions: Record<string, string>
}

export interface PerformanceMetricBatchV1 {
  schemaVersion: 1
  events: PerformanceMetricEventV1[]
}

export function normalizePerformanceMetricEnvironment(value: unknown) {
  if (typeof value !== 'string') {
    return undefined
  }

  switch (value.trim().toLowerCase()) {
    case 'production':
      return 'Production'
    case 'development':
      return 'Development'
    case 'preview':
      return 'Preview'
    case 'staging':
      return 'Staging'
    case 'test':
      return 'Test'
    case 'uat':
      return 'UAT'
    default:
      return undefined
  }
}

export interface PerformanceMetricIngestResult {
  acceptedCount: number
  duplicateCount: number
  rejectedCount: number
  baselineState?: string
  defaultSampleRate?: number
  policies?: PerformanceSamplingPolicy[]
}

export interface PerformanceSamplingPolicy {
  metric: string
  selector: string
  sampleRate: number
  slowThreshold?: number | null
}

export interface PerformanceSamplingStrategy {
  baselineState: string
  defaultSampleRate: number
  policies: PerformanceSamplingPolicy[]
}

export interface PerformancePercentile {
  metric: string
  selector: string
  sampleCount: number
  p50?: number | null
  p95?: number | null
  p99?: number | null
  average?: number | null
  maximum?: number | null
  baselineP95?: number | null
  warningThreshold?: number | null
  isWarning?: boolean
  consecutiveBreaches?: number
  lastObservedAtUtc?: string | null
  coverageState: string
}

export interface PerformanceBaselineStatus {
  state: string
  observationStartedAtUtc?: string | null
  observationEndsAtUtc?: string | null
  frozenAtUtc?: string | null
  qualifiedMetricCount: number
  insufficientMetricCount: number
}

export interface PerformanceBaselineDefinition {
  metric: string
  selector: string
  sampleCount: number
  p50?: number | null
  p95?: number | null
  p99?: number | null
  warningThreshold?: number | null
  coverageState: string
  gatePolicy: string
}

export interface PerformanceBaseline {
  status: PerformanceBaselineStatus
  definitions: PerformanceBaselineDefinition[]
}

export interface PerformanceReleaseEvent {
  id: string
  action: string
  status: string
  environment: string
  component: string
  commit: string
  version?: string | null
  startedAtUtc: string
  completedAtUtc: string
  source: string
}

export interface PerformanceOverview {
  environment: string
  startUtc: string
  endUtc: string
  generatedAtUtc: string
  baseline: PerformanceBaselineStatus
  api: PerformancePercentile[]
  sql: PerformancePercentile[]
  hqAndJobs: PerformancePercentile[]
  webAndPos: PerformancePercentile[]
  delivery: PerformancePercentile[]
  acceptedDeployments: number
  acceptedRollbacks: number
  releaseEvents: PerformanceReleaseEvent[]
}

export interface PerformanceOverviewQuery {
  environment: string
  startUtc: string
  endUtc: string
  signal?: AbortSignal
}

export type SlowSqlWindow = '1h' | '24h' | '7d'
export type SlowSqlSortBy = 'total' | 'p95' | 'max'

export interface PerformanceSlowSqlItem {
  databaseContext: string
  fingerprint: string
  template: string
  executionCount: number
  totalDurationMs: number
  averageDurationMs: number
  p95DurationMs?: number | null
  maximumDurationMs: number
  lastObservedAtUtc: string
}

export interface PerformanceSlowSqlQuery {
  environment: string
  window: SlowSqlWindow
  sortBy: SlowSqlSortBy
  startUtc: string
  endUtc: string
  signal?: AbortSignal
}

export interface PerformanceOperationalRun {
  id: string
  category: string
  operation: string
  status: string
  attempt?: number | null
  backlog?: number | null
  queuedAtUtc: string
  startedAtUtc?: string | null
  completedAtUtc?: string | null
  durationMs?: number | null
}

export interface PerformanceRunsQuery {
  environment: string
  startUtc: string
  endUtc: string
  signal?: AbortSignal
}

type MetricTransport = typeof postCenterLogAuthorizedJson
type FreezeTransport = (
  path: string,
  data: unknown,
) => Promise<ApiResponse<PerformanceBaselineStatus> | PerformanceBaselineStatus>

const ALLOWED_PERFORMANCE_DIMENSIONS = new Set([
  'metricId',
  'method',
  'statusClass',
  'environment',
  'instance',
  'app',
  'version',
  'channel',
  'store',
  'paymentType',
  'outcome',
  'databaseContext',
  'sqlFingerprint',
  'sqlTemplate',
  'taskType',
  'operation',
  'lane',
  'component',
  'source',
  'release',
  'dist',
  'project',
  'action',
])
const ALWAYS_SAMPLE_OUTCOMES = new Set([
  'failed',
  'failure',
  'error',
  'rejected',
  'reject',
  'timeout',
  'timedout',
  'timed_out',
])

const pendingEvents: PerformanceMetricEventV1[] = []
let flushTimer: ReturnType<typeof setTimeout> | undefined
let activeSamplingStrategy: PerformanceSamplingStrategy | null | undefined
let fallbackSessionId: string | undefined

function createEventId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  const bytes = new Uint8Array(16)
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes)
  } else {
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256)
    }
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const value = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`
}

function normalizeSampleRate(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1
    ? value
    : undefined
}

function normalizePerformanceSamplingStrategy(
  value: unknown,
): PerformanceSamplingStrategy | undefined {
  if (!value || typeof value !== 'object') {
    return undefined
  }

  const candidate = value as Partial<PerformanceSamplingStrategy>
  const defaultSampleRate = normalizeSampleRate(candidate.defaultSampleRate)
  if (typeof candidate.baselineState !== 'string' || defaultSampleRate === undefined) {
    return undefined
  }

  const policies = Array.isArray(candidate.policies)
    ? candidate.policies.flatMap((policy) => {
        if (!policy || typeof policy !== 'object') {
          return []
        }
        const item = policy as Partial<PerformanceSamplingPolicy>
        const sampleRate = normalizeSampleRate(item.sampleRate)
        const slowThreshold = item.slowThreshold
        if (
          typeof item.metric !== 'string' ||
          !item.metric.trim() ||
          typeof item.selector !== 'string' ||
          !item.selector.trim() ||
          sampleRate === undefined ||
          (slowThreshold !== undefined &&
            slowThreshold !== null &&
            (typeof slowThreshold !== 'number' || !Number.isFinite(slowThreshold) || slowThreshold < 0))
        ) {
          return []
        }

        return [
          {
            metric: item.metric.trim(),
            selector: item.selector.trim(),
            sampleRate,
            slowThreshold: slowThreshold ?? null,
          },
        ]
      })
    : []

  return {
    baselineState: candidate.baselineState.trim() || 'not_started',
    defaultSampleRate,
    policies,
  }
}

export function mergePerformanceSamplingStrategy(
  current: PerformanceSamplingStrategy | undefined,
  incoming: PerformanceSamplingStrategy,
): PerformanceSamplingStrategy {
  if (!current || current.baselineState !== 'frozen' || incoming.baselineState !== 'frozen') {
    return incoming
  }

  const policies = new Map(
    current.policies.map((policy) => [`${policy.metric}\u0000${policy.selector}`, policy]),
  )
  incoming.policies.forEach((policy) => {
    policies.set(`${policy.metric}\u0000${policy.selector}`, policy)
  })

  return {
    ...incoming,
    policies: Array.from(policies.values()),
  }
}

function readPersistedSamplingStrategy() {
  if (activeSamplingStrategy !== undefined) {
    return activeSamplingStrategy ?? undefined
  }

  activeSamplingStrategy = null
  if (typeof localStorage === 'undefined') {
    return undefined
  }

  try {
    const stored = localStorage.getItem(PERFORMANCE_SAMPLING_STORAGE_KEY)
    activeSamplingStrategy = stored
      ? normalizePerformanceSamplingStrategy(JSON.parse(stored)) ?? null
      : null
  } catch {
    activeSamplingStrategy = null
  }
  return activeSamplingStrategy ?? undefined
}

function persistSamplingStrategy(incoming: PerformanceSamplingStrategy) {
  const strategy = mergePerformanceSamplingStrategy(
    readPersistedSamplingStrategy(),
    incoming,
  )
  activeSamplingStrategy = strategy

  if (typeof localStorage !== 'undefined') {
    try {
      localStorage.setItem(PERFORMANCE_SAMPLING_STORAGE_KEY, JSON.stringify(strategy))
    } catch {
      // 存储受限时只保留当前页面内策略，不影响遥测与业务页面。
    }
  }
  return strategy
}

function getPerformanceSessionId() {
  if (fallbackSessionId) {
    return fallbackSessionId
  }

  if (typeof sessionStorage !== 'undefined') {
    try {
      const stored = sessionStorage.getItem(PERFORMANCE_SESSION_STORAGE_KEY)
      if (stored) {
        fallbackSessionId = stored
        return stored
      }
      fallbackSessionId = createEventId()
      sessionStorage.setItem(PERFORMANCE_SESSION_STORAGE_KEY, fallbackSessionId)
      return fallbackSessionId
    } catch {
      // Safari 隐私模式可能禁止 sessionStorage，退化为模块生命周期内稳定值。
    }
  }

  fallbackSessionId = createEventId()
  return fallbackSessionId
}

function stableSessionSample(sessionId: string) {
  let hash = 0x811c9dc5
  for (let index = 0; index < sessionId.length; index += 1) {
    hash ^= sessionId.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193)
  }

  // 顺序相近的 session id 也需要均匀落桶，避免冻结后的 20% 采样发生偏斜。
  hash ^= hash >>> 16
  hash = Math.imul(hash, 0x85ebca6b)
  hash ^= hash >>> 13
  hash = Math.imul(hash, 0xc2b2ae35)
  hash ^= hash >>> 16
  return (hash >>> 0) / 0x1_0000_0000
}

function getEventSelector(event: PerformanceMetricEventV1) {
  return event.dimensions.metricId || 'all'
}

export function shouldSamplePerformanceMetricEvent(
  event: PerformanceMetricEventV1,
  strategy: PerformanceSamplingStrategy | undefined,
  sessionId: string,
) {
  if (!strategy || strategy.baselineState.toLowerCase() !== 'frozen') {
    return true
  }

  const outcome = event.dimensions.outcome?.trim().toLowerCase()
  if (outcome && ALWAYS_SAMPLE_OUTCOMES.has(outcome)) {
    return true
  }

  const selector = getEventSelector(event)
  const policy =
    strategy.policies.find(
      (item) => item.metric === event.metric && item.selector === selector,
    ) ??
    strategy.policies.find(
      (item) => item.metric === event.metric && item.selector === 'all',
    )
  if (
    policy?.slowThreshold !== undefined &&
    policy.slowThreshold !== null &&
    event.value > policy.slowThreshold
  ) {
    return true
  }

  const sampleRate = policy?.sampleRate ?? strategy.defaultSampleRate
  if (sampleRate >= 1) {
    return true
  }
  if (sampleRate <= 0) {
    return false
  }
  return stableSessionSample(sessionId) < sampleRate
}

export function createPerformanceMetricEvent(
  metric: WebTableMetricName,
  value: number,
  dimensions: Record<string, string>,
  observedAt = new Date(),
  environment: unknown = CENTER_LOG_ENVIRONMENT,
): PerformanceMetricEventV1 | undefined {
  const normalizedEnvironment = normalizePerformanceMetricEnvironment(environment)
  if (!Number.isFinite(value) || value < 0 || !dimensions.metricId?.trim() || !normalizedEnvironment) {
    return undefined
  }

  const normalizedDimensions = Object.fromEntries(
    Object.entries(dimensions)
      .filter(
        ([key, dimensionValue]) =>
          ALLOWED_PERFORMANCE_DIMENSIONS.has(key) && dimensionValue.trim().length > 0,
      )
      .slice(0, 10)
      .map(([key, dimensionValue]) => [key, dimensionValue.trim().slice(0, 120)]),
  )

  return {
    eventId: createEventId(),
    metric,
    observedAt: observedAt.toISOString(),
    value,
    unit: 'ms',
    // 调用方不能覆盖部署环境，确保每个指标都与 Center Log 落到同一环境分桶。
    dimensions: { ...normalizedDimensions, environment: normalizedEnvironment },
  }
}

export async function postPerformanceMetricBatchV1(
  batch: PerformanceMetricBatchV1,
  transport: MetricTransport = postCenterLogAuthorizedJson,
) {
  if (batch.schemaVersion !== 1 || batch.events.length < 1 || batch.events.length > MAX_BATCH_EVENTS) {
    throw new Error(`MetricBatchV1 events 数量必须在 1 到 ${MAX_BATCH_EVENTS} 之间`)
  }

  const response = await transport(CLIENT_METRIC_BATCH_PATH, batch, { keepalive: true })
  if (!response) {
    return { sent: false as const }
  }
  if (!response.ok) {
    throw new Error(`MetricBatchV1 上报失败 (${response.status})`)
  }

  let result: PerformanceMetricIngestResult | undefined
  try {
    const payload = (await response.json()) as
      | ApiResponse<PerformanceMetricIngestResult>
      | PerformanceMetricIngestResult
    result = unwrapApiData(payload)
  } catch (error) {
    if (error instanceof SyntaxError) {
      return { sent: true as const }
    }
    throw error
  }

  const strategy = normalizePerformanceSamplingStrategy(result)
  if (strategy) {
    persistSamplingStrategy(strategy)
  }

  return { sent: true as const, result }
}

export async function flushPerformanceMetrics() {
  if (flushTimer) {
    clearTimeout(flushTimer)
    flushTimer = undefined
  }
  if (!pendingEvents.length) {
    return
  }

  const events = pendingEvents.splice(0, MAX_BATCH_EVENTS)
  try {
    await postPerformanceMetricBatchV1({ schemaVersion: 1, events })
  } catch {
    // 性能遥测不得影响表格交互；失败批次直接丢弃，避免无限重试放大压力。
  }

  if (pendingEvents.length) {
    schedulePerformanceMetricFlush()
  }
}

function schedulePerformanceMetricFlush() {
  if (flushTimer) {
    return
  }

  flushTimer = setTimeout(() => {
    flushTimer = undefined
    void flushPerformanceMetrics()
  }, FLUSH_DELAY_MS)
}

export function recordWebTableMetric(
  metric: WebTableMetricName,
  value: number,
  dimensions: Record<string, string>,
) {
  const event = createPerformanceMetricEvent(
    metric,
    value,
    dimensions,
    new Date(),
    CENTER_LOG_ENVIRONMENT,
  )
  if (!event) {
    return
  }
  if (
    !shouldSamplePerformanceMetricEvent(
      event,
      readPersistedSamplingStrategy(),
      getPerformanceSessionId(),
    )
  ) {
    return
  }

  pendingEvents.push(event)
  if (pendingEvents.length > MAX_PENDING_EVENTS) {
    pendingEvents.splice(0, pendingEvents.length - MAX_PENDING_EVENTS)
  }

  if (pendingEvents.length >= MAX_BATCH_EVENTS) {
    void flushPerformanceMetrics()
    return
  }
  schedulePerformanceMetricFlush()
}

export async function getPerformanceOverview({
  environment,
  startUtc,
  endUtc,
  signal,
}: PerformanceOverviewQuery) {
  const payload = await request.get<ApiResponse<PerformanceOverview> | PerformanceOverview>(
    PERFORMANCE_OVERVIEW_PATH,
    {
      params: { environment, startUtc, endUtc },
      signal,
    },
  )
  return unwrapApiData(payload)
}

export async function freezePerformanceBaseline(
  environment: string,
  transport: FreezeTransport = (path, data) =>
    request.post<ApiResponse<PerformanceBaselineStatus> | PerformanceBaselineStatus>(path, data),
) {
  const payload = await transport(PERFORMANCE_FREEZE_PATH, { environment })
  return unwrapApiData(payload)
}

export async function getPerformanceBaseline(environment: string, signal?: AbortSignal) {
  const payload = await request.get<ApiResponse<PerformanceBaseline> | PerformanceBaseline>(
    PERFORMANCE_BASELINE_PATH,
    { params: { environment }, signal },
  )
  return unwrapApiData(payload)
}

export async function getPerformanceSlowSql(query: PerformanceSlowSqlQuery) {
  const payload = await request.get<
    ApiResponse<PerformanceSlowSqlItem[]> | PerformanceSlowSqlItem[]
  >(PERFORMANCE_SLOW_SQL_PATH, {
    params: {
      environment: query.environment,
      window: query.window,
      sortBy: query.sortBy,
      startUtc: query.startUtc,
      endUtc: query.endUtc,
    },
    signal: query.signal,
  })
  return unwrapApiData(payload)
}

export async function getPerformanceRuns({
  environment,
  startUtc,
  endUtc,
  signal,
}: PerformanceRunsQuery) {
  const payload = await request.get<
    ApiResponse<PerformanceOperationalRun[]> | PerformanceOperationalRun[]
  >(PERFORMANCE_RUNS_PATH, {
    params: { environment, startUtc, endUtc },
    signal,
  })
  return unwrapApiData(payload)
}
