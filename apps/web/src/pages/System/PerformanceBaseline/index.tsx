import { DownloadOutlined, LockOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  DatePicker,
  Popconfirm,
  Row,
  Select,
  Space,
  Statistic,
  Tag,
  Typography,
} from 'antd'
import type { RangePickerProps } from 'antd/es/date-picker'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MeasuredTable } from '../../../components/MeasuredTable'
import PageContainer from '../../../components/PageContainer'
import {
  freezePerformanceBaseline,
  getPerformanceBaseline,
  getPerformanceOverview,
  getPerformanceRuns,
  getPerformanceSlowSql,
  type PerformanceOverview,
  type PerformanceOperationalRun,
  type PerformancePercentile,
  type PerformanceReleaseEvent,
  type PerformanceSlowSqlItem,
  type SlowSqlSortBy,
  type SlowSqlWindow,
} from '../../../services/performanceMetricService'
import { useAuthStore } from '../../../store/auth'
import { P } from '../../../types/permissions'
import {
  PERFORMANCE_GROUPS,
  buildPerformanceGroupRows,
  buildQualityBaselineBudget,
  canFreezePerformanceBaseline,
  createLoadingPerformanceQueryState,
  createPerformanceQueryKey,
  formatBrisbaneDateTime,
  formatOptionalPerformanceValue,
  formatPerformanceMetricValue,
  getLastReportedAtUtc,
  getOperationalRunRetryCount,
  getPendingBaselineMetricCount,
  getSlowSqlWindowRange,
  type PerformanceQueryState,
  resolvePerformanceQueryData,
  resolveOperationalRunStatusColor,
  settlePerformanceQueryFailure,
  settlePerformanceQuerySuccess,
} from './logic'

const { RangePicker } = DatePicker
type DateRange = [Dayjs, Dayjs]

const ENVIRONMENT_OPTIONS = ['Production', 'Staging', 'Development'].map((value) => ({
  label: value,
  value,
}))
const SLOW_SQL_WINDOW_OPTIONS: Array<{ label: string; value: SlowSqlWindow }> = [
  { label: '1h', value: '1h' },
  { label: '24h', value: '24h' },
  { label: '7d', value: '7d' },
]
const SLOW_SQL_SORT_OPTIONS: Array<{ label: string; value: SlowSqlSortBy }> = [
  { label: 'Total', value: 'total' },
  { label: 'P95', value: 'p95' },
  { label: 'Max', value: 'max' },
]

function baselineTagColor(state: string) {
  switch (state.toLowerCase()) {
    case 'frozen':
      return 'green'
    case 'observing':
      return 'processing'
    default:
      return 'default'
  }
}

export default function SystemPerformanceBaselinePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const canManagePerformanceBaseline = useAuthStore((state) =>
    state.access.hasPermission(P.System.ManagePerformanceBaseline),
  )
  const [environment, setEnvironment] = useState('Production')
  const [range, setRange] = useState<DateRange>(() => [dayjs().subtract(7, 'day'), dayjs()])
  const [refreshVersion, setRefreshVersion] = useState(0)
  const [overviewState, setOverviewState] = useState<PerformanceQueryState<PerformanceOverview>>({
    key: '',
    status: 'idle',
  })
  const [freezeLoading, setFreezeLoading] = useState(false)
  const [budgetExportLoading, setBudgetExportLoading] = useState(false)
  const [acceptedRollbacks30dState, setAcceptedRollbacks30dState] = useState<
    PerformanceQueryState<number>
  >({ key: '', status: 'idle' })
  const [slowSqlWindow, setSlowSqlWindow] = useState<SlowSqlWindow>('24h')
  const [slowSqlSortBy, setSlowSqlSortBy] = useState<SlowSqlSortBy>('total')
  const [slowSqlState, setSlowSqlState] = useState<PerformanceQueryState<PerformanceSlowSqlItem[]>>({
    key: '',
    status: 'idle',
  })
  const [runsState, setRunsState] = useState<PerformanceQueryState<PerformanceOperationalRun[]>>({
    key: '',
    status: 'idle',
  })
  const overviewQueryKey = createPerformanceQueryKey(
    'overview',
    environment,
    range[0].toISOString(),
    range[1].toISOString(),
    refreshVersion,
  )
  const acceptedRollbacks30dQueryKey = createPerformanceQueryKey(
    'accepted-rollbacks-30d',
    environment,
    '',
    '',
    refreshVersion,
  )
  const slowSqlQueryKey = createPerformanceQueryKey(
    `slow-sql:${slowSqlWindow}:${slowSqlSortBy}`,
    environment,
    '',
    '',
    refreshVersion,
  )
  const runsQueryKey = createPerformanceQueryKey(
    'runs',
    environment,
    range[0].toISOString(),
    range[1].toISOString(),
    refreshVersion,
  )
  const overview = resolvePerformanceQueryData(overviewState, overviewQueryKey)
  const acceptedRollbacks30d = resolvePerformanceQueryData(
    acceptedRollbacks30dState,
    acceptedRollbacks30dQueryKey,
  )
  const slowSqlRows = resolvePerformanceQueryData(slowSqlState, slowSqlQueryKey) ?? []
  const runs = resolvePerformanceQueryData(runsState, runsQueryKey) ?? []
  const loading = overviewState.key === overviewQueryKey && overviewState.status === 'loading'
  const error =
    overviewState.key === overviewQueryKey && overviewState.status === 'error'
      ? overviewState.error
      : undefined
  const slowSqlLoading = slowSqlState.key === slowSqlQueryKey && slowSqlState.status === 'loading'
  const slowSqlError =
    slowSqlState.key === slowSqlQueryKey && slowSqlState.status === 'error'
      ? slowSqlState.error
      : undefined
  const runsLoading = runsState.key === runsQueryKey && runsState.status === 'loading'
  const runsError =
    runsState.key === runsQueryKey && runsState.status === 'error' ? runsState.error : undefined

  useEffect(() => {
    const controller = new AbortController()
    setOverviewState(createLoadingPerformanceQueryState(overviewQueryKey))

    void getPerformanceOverview({
      environment,
      startUtc: range[0].toISOString(),
      endUtc: range[1].toISOString(),
      signal: controller.signal,
    })
      .then((value) =>
        setOverviewState((current) =>
          settlePerformanceQuerySuccess(current, overviewQueryKey, value),
        ),
      )
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setOverviewState((current) =>
            settlePerformanceQueryFailure(
              current,
              overviewQueryKey,
            requestError instanceof Error
              ? requestError.message
              : t('performanceBaseline.loadFailed', '性能概览加载失败'),
            ),
          )
        }
      })

    return () => controller.abort()
  }, [environment, overviewQueryKey, range, t])

  useEffect(() => {
    const controller = new AbortController()
    const end = dayjs()
    setAcceptedRollbacks30dState(createLoadingPerformanceQueryState(acceptedRollbacks30dQueryKey))
    void getPerformanceOverview({
      environment,
      startUtc: dayjs().subtract(30, 'day').toISOString(),
      endUtc: end.toISOString(),
      signal: controller.signal,
    })
      .then((value) =>
        setAcceptedRollbacks30dState((current) =>
          settlePerformanceQuerySuccess(current, acceptedRollbacks30dQueryKey, value.acceptedRollbacks),
        ),
      )
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setAcceptedRollbacks30dState((current) =>
            settlePerformanceQueryFailure(
              current,
              acceptedRollbacks30dQueryKey,
              requestError instanceof Error
                ? requestError.message
                : t('performanceBaseline.loadFailed', '性能概览加载失败'),
            ),
          )
        }
      })
    return () => controller.abort()
  }, [acceptedRollbacks30dQueryKey, environment, t])

  useEffect(() => {
    const controller = new AbortController()
    const queryRange = getSlowSqlWindowRange(slowSqlWindow)
    setSlowSqlState(createLoadingPerformanceQueryState(slowSqlQueryKey))

    void getPerformanceSlowSql({
      environment,
      window: slowSqlWindow,
      sortBy: slowSqlSortBy,
      startUtc: queryRange.startUtc,
      endUtc: queryRange.endUtc,
      signal: controller.signal,
    })
      .then((value) =>
        setSlowSqlState((current) =>
          settlePerformanceQuerySuccess(current, slowSqlQueryKey, value.slice(0, 20)),
        ),
      )
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setSlowSqlState((current) =>
            settlePerformanceQueryFailure(
              current,
              slowSqlQueryKey,
            requestError instanceof Error
              ? requestError.message
              : t('performanceBaseline.slowSql.loadFailed', '慢 SQL 加载失败'),
            ),
          )
        }
      })

    return () => controller.abort()
  }, [environment, slowSqlQueryKey, slowSqlSortBy, slowSqlWindow, t])

  useEffect(() => {
    const controller = new AbortController()
    setRunsState(createLoadingPerformanceQueryState(runsQueryKey))

    void getPerformanceRuns({
      environment,
      startUtc: range[0].toISOString(),
      endUtc: range[1].toISOString(),
      signal: controller.signal,
    })
      .then((value) =>
        setRunsState((current) =>
          settlePerformanceQuerySuccess(current, runsQueryKey, value.slice(0, 20)),
        ),
      )
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setRunsState((current) =>
            settlePerformanceQueryFailure(
              current,
              runsQueryKey,
            requestError instanceof Error
              ? requestError.message
              : t('performanceBaseline.runs.loadFailed', '最近运行加载失败'),
            ),
          )
        }
      })

    return () => controller.abort()
  }, [environment, range, runsQueryKey, t])

  const columns = useMemo<ColumnsType<PerformancePercentile>>(
    () => [
      {
        title: t('performanceBaseline.columns.metric', '指标'),
        dataIndex: 'metric',
        width: 230,
        render: (value: string) => <Typography.Text code>{value}</Typography.Text>,
      },
      {
        title: t('performanceBaseline.columns.selector', '对象'),
        dataIndex: 'selector',
        width: 150,
        ellipsis: true,
      },
      {
        title: t('performanceBaseline.columns.samples', '样本'),
        dataIndex: 'sampleCount',
        align: 'right',
        width: 76,
        render: (value: number) => value.toLocaleString(),
      },
      ...(['p50', 'p95', 'p99', 'maximum'] as const).map((field) => ({
        title: t(`performanceBaseline.columns.${field}`, field.toUpperCase()),
        dataIndex: field,
        align: 'right' as const,
        width: 102,
        render: (value: number | null | undefined, record: PerformancePercentile) =>
          formatPerformanceMetricValue(record.metric, value),
      })),
      {
        title: t('performanceBaseline.columns.baselineP95', '基线 P95'),
        dataIndex: 'baselineP95',
        align: 'right',
        width: 112,
        render: (value: number | null | undefined, record) =>
          formatPerformanceMetricValue(record.metric, value),
      },
      {
        title: t('performanceBaseline.columns.warningThreshold', '预警线'),
        dataIndex: 'warningThreshold',
        align: 'right',
        width: 112,
        render: (value: number | null | undefined, record) =>
          formatPerformanceMetricValue(record.metric, value),
      },
      {
        title: t('performanceBaseline.columns.lastReported', '最后上报'),
        dataIndex: 'lastObservedAtUtc',
        width: 156,
        render: (value: string | null | undefined) => formatBrisbaneDateTime(value),
      },
      {
        title: t('performanceBaseline.columns.status', '覆盖状态'),
        dataIndex: 'coverageState',
        width: 170,
        render: (coverageState: string, record) => {
          const normalized = coverageState.toLowerCase()
          return (
            <Space size={4} wrap>
              <Tag color={normalized === 'qualified' ? 'green' : normalized === 'insufficient' ? 'gold' : 'default'}>
                {t(`performanceBaseline.coverage.${normalized}`, coverageState)}
              </Tag>
              {record.isWarning ? (
                <Tag color="red">
                  {t('performanceBaseline.warning', '预警')} ·{' '}
                  {record.consecutiveBreaches === undefined
                    ? '-'
                    : t('performanceBaseline.breachWindows', '{{count}} 个窗口', {
                        count: record.consecutiveBreaches,
                      })}
                </Tag>
              ) : null}
            </Space>
          )
        },
      },
    ],
    [t],
  )

  const slowSqlColumns = useMemo<ColumnsType<PerformanceSlowSqlItem>>(
    () => [
      {
        title: t('performanceBaseline.slowSql.databaseContext', '数据库'),
        dataIndex: 'databaseContext',
        width: 130,
        ellipsis: true,
      },
      {
        title: t('performanceBaseline.slowSql.template', 'SQL 模板'),
        dataIndex: 'template',
        width: 340,
        ellipsis: true,
        render: (value: string) => value || '-',
      },
      {
        title: t('performanceBaseline.slowSql.executionCount', '次数'),
        dataIndex: 'executionCount',
        align: 'right',
        width: 82,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value),
      },
      {
        title: t('performanceBaseline.slowSql.total', '总耗时'),
        dataIndex: 'totalDurationMs',
        align: 'right',
        width: 104,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value, 'ms'),
      },
      {
        title: t('performanceBaseline.slowSql.average', '平均'),
        dataIndex: 'averageDurationMs',
        align: 'right',
        width: 96,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value, 'ms'),
      },
      {
        title: 'P95',
        dataIndex: 'p95DurationMs',
        align: 'right',
        width: 96,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value, 'ms'),
      },
      {
        title: t('performanceBaseline.slowSql.maximum', '最大'),
        dataIndex: 'maximumDurationMs',
        align: 'right',
        width: 96,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value, 'ms'),
      },
      {
        title: t('performanceBaseline.slowSql.lastObserved', '最后执行'),
        dataIndex: 'lastObservedAtUtc',
        width: 156,
        render: (value: string | null | undefined) => formatBrisbaneDateTime(value),
      },
    ],
    [t],
  )

  const runColumns = useMemo<ColumnsType<PerformanceOperationalRun>>(
    () => [
      {
        title: t('performanceBaseline.runs.category', '类别'),
        dataIndex: 'category',
        width: 130,
        ellipsis: true,
      },
      {
        title: t('performanceBaseline.runs.operation', '运行项'),
        dataIndex: 'operation',
        width: 230,
        ellipsis: true,
      },
      {
        title: t('performanceBaseline.runs.status', '状态'),
        dataIndex: 'status',
        width: 116,
        render: (status: string) => {
          const normalized = status.trim().toLowerCase()
          return (
            <Tag color={resolveOperationalRunStatusColor(status)}>
              {status
                ? t(`performanceBaseline.runs.statuses.${normalized}`, status)
                : '-'}
            </Tag>
          )
        },
      },
      {
        title: t('performanceBaseline.runs.retryCount', '重试次数'),
        dataIndex: 'attempt',
        align: 'right',
        width: 88,
        render: (value: number | null | undefined) =>
          formatOptionalPerformanceValue(getOperationalRunRetryCount(value)),
      },
      {
        title: t('performanceBaseline.runs.backlog', '积压'),
        dataIndex: 'backlog',
        align: 'right',
        width: 78,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value),
      },
      {
        title: t('performanceBaseline.runs.duration', '耗时'),
        dataIndex: 'durationMs',
        align: 'right',
        width: 98,
        render: (value: number | null | undefined) => formatOptionalPerformanceValue(value, 'ms'),
      },
      {
        title: t('performanceBaseline.runs.lastRun', '最近运行'),
        dataIndex: 'queuedAtUtc',
        width: 156,
        render: (_value: string, record) =>
          formatBrisbaneDateTime(
            record.completedAtUtc ?? record.startedAtUtc ?? record.queuedAtUtc,
          ),
      },
    ],
    [t],
  )

  const releaseEventColumns = useMemo<ColumnsType<PerformanceReleaseEvent>>(
    () => [
      {
        title: t('performanceBaseline.releaseEvents.completedAt', '验收时间'),
        dataIndex: 'completedAtUtc',
        width: 156,
        render: (value: string) => formatBrisbaneDateTime(value),
      },
      {
        title: t('performanceBaseline.releaseEvents.action', '动作'),
        dataIndex: 'action',
        width: 88,
        render: (action: string) => {
          const normalized = action.trim().toLowerCase()
          return (
            <Tag color={normalized === 'rollback' ? 'orange' : 'blue'}>
              {action
                ? t(`performanceBaseline.releaseEvents.actions.${normalized}`, action)
                : '-'}
            </Tag>
          )
        },
      },
      {
        title: t('performanceBaseline.releaseEvents.status', '结果'),
        dataIndex: 'status',
        width: 88,
        render: (status: string) => {
          const normalized = status.trim().toLowerCase()
          const color = normalized === 'accepted' ? 'green' : normalized === 'failed' ? 'red' : 'default'
          return (
            <Tag color={color}>
              {status
                ? t(`performanceBaseline.releaseEvents.statuses.${normalized}`, status)
                : '-'}
            </Tag>
          )
        },
      },
      {
        title: t('performanceBaseline.releaseEvents.component', '组件'),
        dataIndex: 'component',
        width: 120,
        ellipsis: true,
      },
      {
        title: t('performanceBaseline.releaseEvents.version', '版本'),
        dataIndex: 'version',
        width: 120,
        ellipsis: true,
        render: (value: string | null | undefined) => value || '-',
      },
      {
        title: t('performanceBaseline.releaseEvents.commit', '提交'),
        dataIndex: 'commit',
        width: 124,
        render: (value: string) =>
          value ? <Typography.Text code>{value.slice(0, 12)}</Typography.Text> : '-',
      },
      {
        title: t('performanceBaseline.releaseEvents.source', '来源'),
        dataIndex: 'source',
        width: 150,
        ellipsis: true,
        render: (value: string) => value || '-',
      },
    ],
    [t],
  )

  const unavailableText = t('performanceBaseline.unavailable', '不可用')
  const lastObservedAtUtc = getLastReportedAtUtc(overview)
  const baseline = overview?.baseline
  const baselineState = baseline?.state
  const pendingBaselineMetricCount = getPendingBaselineMetricCount(overview)
  const isSupplementalFreeze = baselineState?.toLowerCase() === 'frozen'
  const freezeActionLabel = isSupplementalFreeze
    ? t('performanceBaseline.supplementFreeze', '补冻基线')
    : t('performanceBaseline.freeze', '冻结基线')
  const handleRangeChange: RangePickerProps['onChange'] = (value) => {
    if (value?.[0] && value[1]) {
      setRange([value[0], value[1]])
    }
  }

  const handleFreezeBaseline = async () => {
    setFreezeLoading(true)
    try {
      const frozenBaseline = await freezePerformanceBaseline(environment)
      setOverviewState((current) => {
        if (
          current.key !== overviewQueryKey ||
          current.status !== 'success' ||
          !current.data
        ) {
          return current
        }

        return {
          ...current,
          data: {
            ...current.data,
            baseline: frozenBaseline,
          },
        }
      })
      message.success(
        isSupplementalFreeze
          ? t('performanceBaseline.supplementFreezeSuccess', '数据不足指标候选已更新')
          : t('performanceBaseline.freezeSuccess', '基线已冻结'),
      )
      setRefreshVersion((value) => value + 1)
    } catch (requestError) {
      message.error(
        requestError instanceof Error
          ? requestError.message
          : t('performanceBaseline.freezeFailed', '冻结基线失败'),
      )
    } finally {
      setFreezeLoading(false)
    }
  }

  const handleExportQualityBudget = async () => {
    if (environment !== 'Production') {
      message.error(
        t('performanceBaseline.productionBudgetOnly', '硬门禁预算只能从 Production 冻结值导出'),
      )
      return
    }
    setBudgetExportLoading(true)
    try {
      const frozenBaseline = await getPerformanceBaseline(environment)
      const budget = buildQualityBaselineBudget(frozenBaseline)
      const blob = new Blob([`${JSON.stringify(budget, null, 2)}\n`], {
        type: 'application/json;charset=utf-8',
      })
      const url = URL.createObjectURL(blob)
      try {
        const anchor = document.createElement('a')
        anchor.href = url
        anchor.download = 'quality-baseline-budget.json'
        document.body.appendChild(anchor)
        anchor.click()
        anchor.remove()
      } finally {
        URL.revokeObjectURL(url)
      }
      message.success(
        t(
          'performanceBaseline.budgetExportSuccess',
          '候选预算已导出；评审后再以独立 PR 替换仓库预算文件。',
        ),
      )
    } catch (requestError) {
      message.error(
        requestError instanceof Error
          ? requestError.message
          : t('performanceBaseline.budgetExportFailed', '候选预算导出失败'),
      )
    } finally {
      setBudgetExportLoading(false)
    }
  }

  return (
    <PageContainer
      title={t('performanceBaseline.title', '性能质量基线')}
      subtitle={t(
        'performanceBaseline.subtitle',
        '集中观察 API、SQL、同步任务、Web/POS 与交付质量，样本达标后再冻结预算。',
      )}
      extra={
        <Space wrap size={[8, 8]}>
          {canManagePerformanceBaseline ? (
            <Popconfirm
              title={
                isSupplementalFreeze
                  ? t(
                      'performanceBaseline.supplementFreezeConfirm',
                      '确认补冻当前环境中仍然数据不足的指标？既有冻结值不会改变。',
                    )
                  : t('performanceBaseline.freezeConfirm', '确认冻结当前环境的性能基线？')
              }
              onConfirm={handleFreezeBaseline}
              okText={freezeActionLabel}
              cancelText={t('common.cancel', '取消')}
            >
              <Button
                type="primary"
                icon={<LockOutlined />}
                loading={freezeLoading}
                disabled={
                  !canFreezePerformanceBaseline(
                    baselineState,
                    baseline?.observationEndsAtUtc,
                    pendingBaselineMetricCount,
                  )
                }
              >
                {freezeActionLabel}
              </Button>
            </Popconfirm>
          ) : null}
          {canManagePerformanceBaseline && isSupplementalFreeze ? (
            <Button
              icon={<DownloadOutlined />}
              loading={budgetExportLoading}
              disabled={environment !== 'Production'}
              onClick={handleExportQualityBudget}
            >
              {t('performanceBaseline.exportBudget', '导出候选预算')}
            </Button>
          ) : null}
          <Button
            icon={<ReloadOutlined />}
            loading={loading}
            onClick={() => setRefreshVersion((value) => value + 1)}
          >
            {t('common.refresh', '刷新')}
          </Button>
        </Space>
      }
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Card size="small">
          <Space direction="vertical" size={10} style={{ width: '100%' }}>
            <Space wrap size={[8, 8]}>
              <Select
                aria-label={t('performanceBaseline.environment', '环境')}
                value={environment}
                options={ENVIRONMENT_OPTIONS}
                style={{ width: 148 }}
                onChange={setEnvironment}
              />
              <RangePicker
                value={range}
                showTime
                allowClear={false}
                onChange={handleRangeChange}
              />
              <Tag color="blue">{overview?.environment ?? environment}</Tag>
              <Typography.Text type="secondary">
                {t('performanceBaseline.timeRange', '时间范围')}：
                {formatBrisbaneDateTime(overview?.startUtc ?? range[0].toISOString())} –{' '}
                {formatBrisbaneDateTime(overview?.endUtc ?? range[1].toISOString())}
              </Typography.Text>
            </Space>

            <Row gutter={[12, 8]}>
              <Col xs={12} md={6}>
                <Typography.Text type="secondary">
                  {t('performanceBaseline.baselineStatus', '基线状态')}
                </Typography.Text>
                <div style={{ marginTop: 4 }}>
                  <Tag color={baselineState ? baselineTagColor(baselineState) : 'default'}>
                    {baselineState
                      ? t(`performanceBaseline.states.${baselineState}`, baselineState)
                      : unavailableText}
                  </Tag>
                </div>
              </Col>
              <Col xs={12} md={6}>
                <Typography.Text type="secondary">
                  {t('performanceBaseline.lastReported', '最后上报')}
                </Typography.Text>
                <div style={{ marginTop: 4 }}>
                  {overview ? formatBrisbaneDateTime(lastObservedAtUtc) : unavailableText}
                </div>
              </Col>
              <Col xs={12} md={6}>
                <Statistic
                  title={t('performanceBaseline.qualifiedMetrics', '达标指标')}
                  value={baseline?.qualifiedMetricCount ?? unavailableText}
                  valueStyle={{ fontSize: 20 }}
                />
              </Col>
              <Col xs={12} md={6}>
                <Statistic
                  title={t('performanceBaseline.insufficientMetrics', '样本不足')}
                  value={baseline?.insufficientMetricCount ?? unavailableText}
                  valueStyle={{
                    color: baseline?.insufficientMetricCount ? '#d48806' : undefined,
                    fontSize: 20,
                  }}
                />
              </Col>
            </Row>
          </Space>
        </Card>

        {error ? (
          <Alert
            type="error"
            showIcon
            message={t('performanceBaseline.loadFailed', '性能概览加载失败')}
            description={error}
          />
        ) : null}

        <Row gutter={[12, 12]}>
          {PERFORMANCE_GROUPS.map((group) => {
            const rows = overview ? buildPerformanceGroupRows(overview, group) : []
            const insufficientCount = rows.filter(
              (item) => item.coverageState.toLowerCase() === 'insufficient',
            ).length

            return (
              <Col xs={24} xxl={12} key={group.key}>
                <Card
                  size="small"
                  title={t(group.titleKey)}
                  extra={
                    <Space size={4}>
                      <Tag>{overview ? rows.length : unavailableText}</Tag>
                      {insufficientCount > 0 ? (
                        <Tag color="gold">
                          {t('performanceBaseline.coverage.insufficient', '样本不足')} {insufficientCount}
                        </Tag>
                      ) : null}
                    </Space>
                  }
                >
                  {group.key === 'delivery' ? (
                    <Row gutter={[8, 8]} style={{ marginBottom: 8 }}>
                      <Col xs={24} sm={8}>
                        <Statistic
                          title={t('performanceBaseline.acceptedDeployments', '已接受部署')}
                          value={overview?.acceptedDeployments ?? unavailableText}
                          valueStyle={{ fontSize: 18 }}
                        />
                      </Col>
                      <Col xs={24} sm={8}>
                        <Statistic
                          title={t('performanceBaseline.acceptedRollbacks', '已接受回滚')}
                          value={overview?.acceptedRollbacks ?? unavailableText}
                          valueStyle={{ fontSize: 18 }}
                        />
                      </Col>
                      <Col xs={24} sm={8}>
                        <Card
                          size="small"
                          style={{
                            borderColor: acceptedRollbacks30d !== null && acceptedRollbacks30d > 0 ? '#ff4d4f' : undefined,
                            background: acceptedRollbacks30d !== null && acceptedRollbacks30d > 0 ? '#fff2f0' : undefined,
                          }}
                          styles={{ body: { padding: 8 } }}
                        >
                          <Statistic
                            title={t(
                              'performanceBaseline.acceptedRollbacks30d',
                              '最近 30 天已接受回滚',
                            )}
                            value={acceptedRollbacks30d ?? unavailableText}
                            valueStyle={{
                              color:
                                acceptedRollbacks30d !== null && acceptedRollbacks30d > 0
                                  ? '#cf1322'
                                  : undefined,
                              fontSize: 18,
                            }}
                          />
                        </Card>
                      </Col>
                    </Row>
                  ) : null}
                  <MeasuredTable<PerformancePercentile>
                    metricId={`system.performance-baseline.${group.key}`}
                    rowKey={(record) => `${record.metric}:${record.selector}`}
                    columns={columns}
                    dataSource={rows}
                    loading={loading}
                    size="small"
                    pagination={false}
                    scroll={{ x: 1260 }}
                    locale={{
                      emptyText: overview ? t('performanceBaseline.noData', '暂无指标数据') : unavailableText,
                    }}
                  />
                  {group.key === 'delivery' ? (
                    <div style={{ marginTop: 12 }}>
                      <Typography.Text strong>
                        {t('performanceBaseline.releaseEvents.title', '部署与回滚事件')}
                      </Typography.Text>
                      <MeasuredTable<PerformanceReleaseEvent>
                        metricId="system.performance-baseline.release-events"
                        rowKey="id"
                        columns={releaseEventColumns}
                        dataSource={overview?.releaseEvents ?? []}
                        loading={loading}
                        size="small"
                        pagination={false}
                        scroll={{ x: 846 }}
                        style={{ marginTop: 8 }}
                        locale={{
                          emptyText: overview
                            ? t(
                                'performanceBaseline.releaseEvents.empty',
                                '当前范围暂无发布事件',
                              )
                            : unavailableText,
                        }}
                      />
                    </div>
                  ) : null}
                </Card>
              </Col>
            )
          })}
        </Row>

        <Card
          size="small"
          title={t('performanceBaseline.slowSql.title', '慢 SQL Top 20')}
          extra={
            <Space size={8}>
              <Select<SlowSqlWindow>
                aria-label={t('performanceBaseline.slowSql.window', '慢 SQL 时间窗口')}
                value={slowSqlWindow}
                options={SLOW_SQL_WINDOW_OPTIONS}
                style={{ width: 88 }}
                onChange={setSlowSqlWindow}
              />
              <Select<SlowSqlSortBy>
                aria-label={t('performanceBaseline.slowSql.sortBy', '慢 SQL 排序')}
                value={slowSqlSortBy}
                options={SLOW_SQL_SORT_OPTIONS}
                style={{ width: 96 }}
                onChange={setSlowSqlSortBy}
              />
            </Space>
          }
        >
          {slowSqlError ? (
            <Alert
              type="error"
              showIcon
              message={t('performanceBaseline.slowSql.loadFailed', '慢 SQL 加载失败')}
              description={slowSqlError}
              style={{ marginBottom: 8 }}
            />
          ) : null}
          <MeasuredTable<PerformanceSlowSqlItem>
            metricId="performance-baseline.slow-sql"
            rowKey={(record) => `${record.databaseContext}:${record.fingerprint}`}
            columns={slowSqlColumns}
            dataSource={slowSqlRows}
            loading={slowSqlLoading}
            size="small"
            pagination={false}
            scroll={{ x: 1100 }}
            locale={{
              emptyText:
                slowSqlState.key === slowSqlQueryKey && slowSqlState.status === 'success'
                  ? t('performanceBaseline.slowSql.empty', '当前窗口暂无慢 SQL')
                  : unavailableText,
            }}
          />
        </Card>

        <Card size="small" title={t('performanceBaseline.runs.title', 'HQ / 后台最近运行')}>
          {runsError ? (
            <Alert
              type="error"
              showIcon
              message={t('performanceBaseline.runs.loadFailed', '最近运行加载失败')}
              description={runsError}
              style={{ marginBottom: 8 }}
            />
          ) : null}
          <MeasuredTable<PerformanceOperationalRun>
            metricId="performance-baseline.runs"
            rowKey="id"
            columns={runColumns}
            dataSource={runs}
            loading={runsLoading}
            size="small"
            pagination={false}
            scroll={{ x: 930 }}
            locale={{
              emptyText:
                runsState.key === runsQueryKey && runsState.status === 'success'
                  ? t('performanceBaseline.runs.empty', '当前范围暂无运行记录')
                  : unavailableText,
            }}
          />
        </Card>
      </Space>
    </PageContainer>
  )
}
