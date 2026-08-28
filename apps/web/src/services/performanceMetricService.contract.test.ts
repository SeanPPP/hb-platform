import { readFileSync } from 'node:fs'
import { join } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const servicePath = join(process.cwd(), 'src/services/performanceMetricService.ts')
const centerLogPath = join(process.cwd(), 'src/utils/centerLogClient.ts')
const serviceSource = (() => {
  try {
    return readFileSync(servicePath, 'utf8')
  } catch {
    return ''
  }
})()
const centerLogSource = readFileSync(centerLogPath, 'utf8')

assert(
  serviceSource.includes("const CLIENT_METRIC_BATCH_PATH = '/api/system/performance/client-batches'"),
  'MetricBatchV1 client 必须 POST 到固定 client-batches 路径',
)
assert(
  serviceSource.includes('schemaVersion: 1') && serviceSource.includes('events:'),
  'MetricBatchV1 client 必须发送后端约定的 schemaVersion=1 与 events 数组',
)
assert(
  serviceSource.includes("'web.table.react_commit.duration'") &&
    serviceSource.includes("'web.table.render_to_paint.duration'"),
  'Web 表格只应使用后端白名单中的 React commit 与双 RAF 指标名',
)
assert(
  serviceSource.includes('postCenterLogAuthorizedJson'),
  'MetricBatchV1 client 必须复用中心日志授权 POST 传输',
)
assert(
  serviceSource.includes('baselineState') &&
    serviceSource.includes('defaultSampleRate') &&
    serviceSource.includes('slowThreshold'),
  'MetricBatchV1 client 必须读取后端采样策略字段',
)
assert(
  serviceSource.includes("const PERFORMANCE_SLOW_SQL_PATH = '/api/system/performance/slow-sql'") &&
    serviceSource.includes("const PERFORMANCE_RUNS_PATH = '/api/system/performance/runs'"),
  '看板 client 必须锁定 slow-sql 与 runs GET 路径',
)
assert(
  serviceSource.includes('window: query.window') &&
    serviceSource.includes('sortBy: query.sortBy') &&
    serviceSource.includes('startUtc: query.startUtc') &&
    serviceSource.includes('endUtc: query.endUtc'),
  '慢 SQL client 必须发送 environment/window/sortBy/startUtc/endUtc 完整参数',
)
assert(
  serviceSource.includes('baselineP95') &&
    serviceSource.includes('warningThreshold') &&
    serviceSource.includes('isWarning') &&
    serviceSource.includes('consecutiveBreaches'),
  'overview percentile 类型必须覆盖后端基线与连续窗口预警字段',
)
assert(
  serviceSource.includes('localStorage') && serviceSource.includes('sessionStorage'),
  '采样策略必须持久化，采样身份必须在 session 内稳定',
)
assert(
  centerLogSource.includes('export async function postCenterLogAuthorizedJson') &&
    centerLogSource.includes("'X-Log-Project': CENTER_LOG_PROJECT") &&
    centerLogSource.includes("'X-Log-Key': CENTER_LOG_KEY"),
  '中心日志传输必须统一复用 X-Log-Project/Key',
)

console.log('performanceMetricService contract tests: ok')
