import {
  WEB_TABLE_REACT_COMMIT_METRIC,
  createPerformanceMetricEvent,
  freezePerformanceBaseline,
  mergePerformanceSamplingStrategy,
  normalizePerformanceMetricEnvironment,
  postPerformanceMetricBatchV1,
  shouldSamplePerformanceMetricEvent,
  type PerformanceMetricBatchV1,
  type PerformanceSamplingStrategy,
} from './performanceMetricService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const event = createPerformanceMetricEvent(
  WEB_TABLE_REACT_COMMIT_METRIC,
  12.5,
  { metricId: 'system.performance-baseline.api', route: '/system/performance-baseline' },
  new Date('2026-08-25T00:00:00.000Z'),
  'Production',
)
assert(event, '合法表格指标必须生成 MetricBatchV1 event')
assert(/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(event.eventId), 'eventId 必须是后端可解析的 UUID')
assert(event.observedAt === '2026-08-25T00:00:00.000Z', 'observedAt 必须以 UTC ISO 格式发送')
const sanitizedEvent = createPerformanceMetricEvent(
  WEB_TABLE_REACT_COMMIT_METRIC,
  8,
  {
    metricId: 'safe-table',
    route: '/system/performance-baseline',
    outcome: 'success',
    rowId: 'row-42',
    record: '{"orderGuid":"12345678-1234-1234-1234-123456789abc"}',
    deviceId: 'forbidden-device',
    employeeId: 'forbidden-employee',
    orderNumber: 'forbidden-order',
    cardNumber: 'forbidden-card',
    barcode: 'forbidden-barcode',
  },
  undefined,
  'Production',
)
assert(sanitizedEvent, '含额外维度时仍应生成合法事件')
assert(
  Object.keys(sanitizedEvent.dimensions).sort().join(',') === 'environment,metricId,outcome',
  'route、行内容、设备、员工、订单、卡号、条码等高基数或敏感维度必须在客户端剔除',
)
assert(!JSON.stringify(sanitizedEvent).includes('12345678-1234-1234-1234-123456789abc'), '事件不得包含订单 GUID 或行内容')
assert(
  createPerformanceMetricEvent(WEB_TABLE_REACT_COMMIT_METRIC, -1, { metricId: 'table' }, undefined, 'Production') ===
    undefined,
  '负数指标不得上报',
)
assert(
  createPerformanceMetricEvent(WEB_TABLE_REACT_COMMIT_METRIC, 1, {}, undefined, 'Production') === undefined,
  '缺少 metricId 的表格指标不得上报',
)

for (const [raw, expected] of [
  ['production', 'Production'],
  [' Development ', 'Development'],
  ['PREVIEW', 'Preview'],
  ['staging', 'Staging'],
  ['test', 'Test'],
  ['uat', 'UAT'],
] as const) {
  assert(
    normalizePerformanceMetricEnvironment(raw) === expected,
    `${raw} 必须规范化为 Center Log 指标环境 ${expected}`,
  )
  const environmentEvent = createPerformanceMetricEvent(
    WEB_TABLE_REACT_COMMIT_METRIC,
    8,
    { metricId: 'safe-table', environment: 'forbidden-caller-value' },
    new Date('2026-08-25T00:00:00.000Z'),
    raw,
  )
  assert(environmentEvent, `${raw} 必须生成可上报事件`)
  assert(
    environmentEvent.dimensions.environment === expected,
    '客户端必须覆盖调用方维度并显式发送规范化的 Center Log 环境',
  )
}
for (const raw of [undefined, '', 'prod'] as const) {
  assert(
    normalizePerformanceMetricEnvironment(raw) === undefined,
    `缺失或非法环境 ${String(raw)} 不得被映射为 Production`,
  )
  assert(
    createPerformanceMetricEvent(
      WEB_TABLE_REACT_COMMIT_METRIC,
      8,
      { metricId: 'safe-table' },
      new Date('2026-08-25T00:00:00.000Z'),
      raw,
    ) === undefined,
    '缺失或非法 Center Log 环境必须停用 Web 指标上报',
  )
}

const batch: PerformanceMetricBatchV1 = { schemaVersion: 1, events: [event] }
let receivedPath = ''
let receivedKeepalive = false
await postPerformanceMetricBatchV1(batch, async (path, payload, options) => {
  receivedPath = path
  receivedKeepalive = options?.keepalive === true
  assert(payload === batch, 'client 必须原样发送 MetricBatchV1')
  return new Response('{}', { status: 200 })
})
assert(receivedPath === '/api/system/performance/client-batches', 'client-batches 路径必须固定')
assert(receivedKeepalive, '页面离开时的性能批次必须允许 keepalive 发送')

const frozenStrategy: PerformanceSamplingStrategy = {
  baselineState: 'frozen',
  defaultSampleRate: 0.2,
  policies: [
    {
      metric: WEB_TABLE_REACT_COMMIT_METRIC,
      selector: 'system.performance-baseline.api',
      sampleRate: 0.2,
      slowThreshold: 100,
    },
  ],
}
const successEvent = createPerformanceMetricEvent(
  WEB_TABLE_REACT_COMMIT_METRIC,
  20,
  { metricId: 'system.performance-baseline.api', outcome: 'success' },
  undefined,
  'Production',
)
assert(successEvent, '采样测试事件必须创建成功')

const sampledSessions = Array.from({ length: 1_000 }, (_, index) =>
  shouldSamplePerformanceMetricEvent(successEvent, frozenStrategy, `session-${index}`),
).filter(Boolean).length
assert(
  sampledSessions >= 170 && sampledSessions <= 230,
  `冻结后的稳定 session 采样率应接近 20%，实际 ${sampledSessions / 10}%`,
)
assert(
  shouldSamplePerformanceMetricEvent(successEvent, frozenStrategy, 'stable-session') ===
    shouldSamplePerformanceMetricEvent(successEvent, frozenStrategy, 'stable-session'),
  '同一 session 的采样决定必须稳定',
)

for (const outcome of ['failed', 'rejected', 'timeout']) {
  const exceptionalEvent = createPerformanceMetricEvent(
    WEB_TABLE_REACT_COMMIT_METRIC,
    20,
    { metricId: 'system.performance-baseline.api', outcome },
    undefined,
    'Production',
  )
  assert(exceptionalEvent, `${outcome} 测试事件必须创建成功`)
  assert(
    shouldSamplePerformanceMetricEvent(exceptionalEvent, frozenStrategy, 'not-sampled-session'),
    `${outcome} 事件必须绕过采样全量上报`,
  )
}

const slowEvent = createPerformanceMetricEvent(
  WEB_TABLE_REACT_COMMIT_METRIC,
  101,
  { metricId: 'system.performance-baseline.api', outcome: 'success' },
  undefined,
  'Production',
)
assert(slowEvent, '慢事件必须创建成功')
assert(
  shouldSamplePerformanceMetricEvent(slowEvent, frozenStrategy, 'not-sampled-session'),
  '超过 slowThreshold 的事件必须绕过采样全量上报',
)
assert(
  shouldSamplePerformanceMetricEvent(successEvent, undefined, 'any-session'),
  '首次尚无策略时必须按 1.0 全量上报',
)

const mergedStrategy = mergePerformanceSamplingStrategy(frozenStrategy, {
  baselineState: 'frozen',
  defaultSampleRate: 0.2,
  policies: [
    {
      metric: WEB_TABLE_REACT_COMMIT_METRIC,
      selector: 'system.performance-baseline.sql',
      sampleRate: 0.2,
      slowThreshold: 80,
    },
  ],
})
assert(mergedStrategy.policies.length === 2, '冻结策略响应必须按 metric/selector 合并持久化')

const storage = new Map<string, string>()
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  },
})
await postPerformanceMetricBatchV1(batch, async () =>
  new Response(
    JSON.stringify({
      success: true,
      data: {
        acceptedCount: 1,
        duplicateCount: 0,
        rejectedCount: 0,
        baselineState: 'frozen',
        defaultSampleRate: 0.2,
        policies: frozenStrategy.policies,
      },
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  ),
)
assert(
  [...storage.values()].some((value) => value.includes('"baselineState":"frozen"')),
  'ingest 响应中的采样策略必须持久化到 localStorage',
)

const frozenBaseline = await freezePerformanceBaseline('Production', async (path, data) => {
  assert(path === '/api/system/performance/baseline/freeze', '冻结路径必须固定')
  assert(
    JSON.stringify(data) === JSON.stringify({ environment: 'Production' }),
    '冻结请求必须发送当前环境',
  )
  return {
    success: true,
    data: {
      state: 'frozen',
      qualifiedMetricCount: 4,
      insufficientMetricCount: 0,
    },
  }
})
assert(frozenBaseline.state === 'frozen', '冻结成功应返回最新基线状态')

let freezeBusinessError = ''
try {
  await freezePerformanceBaseline('Production', async () => ({
    success: false,
    code: 'PERFORMANCE_BASELINE_NOT_READY',
    message: '观察期未满或数据不足',
  }))
} catch (error) {
  freezeBusinessError = error instanceof Error ? error.message : String(error)
}
assert(
  freezeBusinessError.includes('观察期未满或数据不足'),
  '冻结失败必须把后端业务错误原样交给页面显示',
)

let invalidBatchFailed = false
try {
  await postPerformanceMetricBatchV1({ schemaVersion: 1, events: [] })
} catch {
  invalidBatchFailed = true
}
assert(invalidBatchFailed, '空批次必须在网络请求前拒绝')

console.log('performanceMetricService tests: ok')
