import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const pagePath = join(process.cwd(), 'src/pages/System/PerformanceBaseline/index.tsx')
const logicPath = join(process.cwd(), 'src/pages/System/PerformanceBaseline/logic.ts')
const servicePath = join(process.cwd(), 'src/services/performanceMetricService.ts')

assert(existsSync(pagePath), '性能基线页面必须存在')
assert(existsSync(logicPath), '性能基线页面必须使用可测试的四组派生逻辑')
assert(existsSync(servicePath), '性能基线页面必须复用 performanceMetricService')

const pageSource = readFileSync(pagePath, 'utf8')
const logicSource = readFileSync(logicPath, 'utf8')
const serviceSource = readFileSync(servicePath, 'utf8')
const zh = JSON.parse(readFileSync(join(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))
const en = JSON.parse(readFileSync(join(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))

assert(
  serviceSource.includes("'/api/system/performance/overview'"),
  '看板必须通过现有 API client 读取 performance overview',
)
assert(
  logicSource.includes('PERFORMANCE_GROUPS') && logicSource.includes('satisfies readonly'),
  '看板必须由固定四组配置驱动',
)
assert(
  pageSource.includes('PERFORMANCE_GROUPS.map') && pageSource.includes('<MeasuredTable'),
  '页面必须紧凑渲染全部四组且表格统一使用 MeasuredTable',
)
assert(
  pageSource.includes("group.key === 'delivery'") &&
    pageSource.includes('acceptedDeployments') &&
    pageSource.includes('acceptedRollbacks'),
  'CI/发布组必须清楚展示部署与回滚统计',
)
assert(
  pageSource.includes('overview?.releaseEvents') &&
    pageSource.includes('system.performance-baseline.release-events') &&
    pageSource.includes('releaseEventColumns'),
  'CI/发布组必须以独立表格追溯成功与失败的部署及回滚事件',
)
assert(
  pageSource.includes('RangePicker') && pageSource.includes('environment'),
  '看板必须提供时间范围和环境筛选',
)
assert(
  pageSource.includes("dayjs().subtract(7, 'day')"),
  '看板默认时间范围必须是最近 7 天',
)
assert(
  pageSource.includes('coverageState') && pageSource.includes('lastObservedAtUtc'),
  '看板必须展示样本不足与最后上报信息',
)
assert(
  pageSource.includes('baselineP95') &&
    pageSource.includes('warningThreshold') &&
    pageSource.includes('record.isWarning') &&
    pageSource.includes('consecutiveBreaches'),
  '指标表必须直接消费后端基线与连续窗口预警字段',
)
assert(
  !pageSource.includes('record.p95 > record.warningThreshold'),
  '前端不得绕过后端连续三个完整窗口规则自行触发预警',
)
assert(
  pageSource.includes('baseline?.state') && pageSource.includes('insufficientMetricCount'),
  '看板必须展示基线状态与不足指标数量',
)
const pageExtraSource = pageSource.slice(
  pageSource.indexOf('extra={'),
  pageSource.indexOf('extra={') + 1_500,
)
assert(
  pageExtraSource.includes('<Space wrap size={[8, 8]}>'),
  '窄屏时页面操作按钮组必须换行，避免筛选区失去可达性',
)
assert(
  pageSource.includes('baseline?.observationEndsAtUtc'),
  '冻结按钮必须依据服务端观察结束时间判断是否可首次冻结',
)
assert(zh.menu?.performanceBaseline && en.menu?.performanceBaseline, '中英文菜单必须包含性能基线名称')
assert(zh.performanceBaseline && en.performanceBaseline, '中英文正文必须包含性能基线词条')
assert(
  serviceSource.includes("'/api/system/performance/baseline/freeze'") &&
    pageSource.includes('freezePerformanceBaseline'),
  '页面必须通过现有 API client 调用冻结基线路径',
)
assert(
  serviceSource.includes("'/api/system/performance/baseline'") &&
    pageSource.includes('buildQualityBaselineBudget') &&
    pageSource.includes("anchor.download = 'quality-baseline-budget.json'"),
  '冻结后必须可从服务端冻结定义导出待评审的 Web 硬门禁预算文件',
)
assert(
  serviceSource.includes("'/api/system/performance/slow-sql'") &&
    serviceSource.includes("'/api/system/performance/runs'"),
  '性能看板服务必须通过既有 API client 查询慢 SQL 与最近运行',
)
assert(
  pageSource.includes('getPerformanceSlowSql') &&
    pageSource.includes('getPerformanceRuns') &&
    pageSource.includes("useState<SlowSqlWindow>('24h')") &&
    pageSource.includes("useState<SlowSqlSortBy>('total')"),
  '页面必须默认查询 24h/total，并接入慢 SQL 与运行记录',
)
assert(
  pageSource.includes("value: '1h'") &&
    pageSource.includes("value: '24h'") &&
    pageSource.includes("value: '7d'") &&
    pageSource.includes("value: 'p95'") &&
    pageSource.includes("value: 'max'"),
  '慢 SQL 必须提供 1h/24h/7d 窗口与 total/p95/max 排序',
)
assert(
  pageSource.includes('performance-baseline.slow-sql') &&
    pageSource.includes('performance-baseline.runs') &&
    pageSource.includes("t('performanceBaseline.slowSql.empty'") &&
    pageSource.includes("t('performanceBaseline.runs.empty'"),
  '慢 SQL 与最近运行必须使用独立 MeasuredTable 和明确空态',
)
assert(
  pageSource.includes("dayjs().subtract(30, 'day')") &&
    pageSource.includes('acceptedRollbacks30d !== null && acceptedRollbacks30d > 0') &&
    pageSource.includes("'#fff2f0'"),
  '最近 30 天出现 accepted rollback 时必须只把提示卡标红',
)
assert(
  logicSource.includes("timeZone: 'Australia/Brisbane'") &&
    pageSource.includes('formatBrisbaneDateTime'),
  '所有服务端 UTC 时间必须通过固定 Brisbane 时区 helper 展示',
)
assert(
  logicSource.includes("case 'success'") &&
    logicSource.includes("case 'failure'") &&
    logicSource.includes("case 'retry_wait'") &&
    pageSource.includes('getOperationalRunRetryCount'),
  '运行状态颜色与重试次数必须按后端实际契约派生',
)
assert(
  logicSource.includes('createPerformanceQueryKey') &&
    logicSource.includes('settlePerformanceQuerySuccess') &&
    logicSource.includes('settlePerformanceQueryFailure') &&
    logicSource.includes('resolvePerformanceQueryData'),
  '看板必须以可测试的查询键状态机隔离异步响应',
)
assert(
  pageSource.includes('createLoadingPerformanceQueryState') &&
    pageSource.includes('resolvePerformanceQueryData') &&
    pageSource.includes('unavailableText'),
  '新查询必须隐藏旧结果，失败或未加载时必须显示不可用',
)
assert(
  !pageSource.includes('acceptedDeployments ?? 0') &&
    !pageSource.includes('acceptedRollbacks ?? 0') &&
    !pageSource.includes('qualifiedMetricCount ?? 0') &&
    !pageSource.includes('insufficientMetricCount ?? 0'),
  '未成功加载的数据不得以 0 作为默认值',
)

console.log('performanceBaseline source contract tests: ok')
