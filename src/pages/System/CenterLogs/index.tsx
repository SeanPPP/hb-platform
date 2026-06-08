import { EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Col,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Input,
  Row,
  Select,
  Space,
  Statistic,
  Switch,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { RangePickerProps } from 'antd/es/date-picker'
import dayjs from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getCenterLogDetail, getCenterLogs, getCenterLogSummary } from '../../../services/centerLogService'
import {
  getScheduledTaskRuntimeControl,
  updateScheduledTaskRuntimeControl,
} from '../../../services/scheduledTaskRuntimeControlService'
import { useAuthStore } from '../../../store/auth'
import type { ApplicationLogItem, ApplicationLogQueryParams, ApplicationLogSummaryGroup } from '../../../types/centerLog'
import type { ScheduledTaskRuntimeControlStatus } from '../../../types/scheduledTaskRuntimeControl'
import { formatCenterLogTimestamp } from './time'
import {
  CENTER_LOG_LEVEL_OPTIONS,
  CENTER_LOG_PROJECT_OPTIONS,
  CENTER_LOG_SOURCE_TYPE_OPTIONS,
  DEFAULT_CENTER_LOG_PAGE_SIZE,
  DEFAULT_CENTER_LOG_PROJECT_CODE,
  type CenterLogQueryFormValues,
  buildCenterLogQueryParams,
  buildDefaultCenterLogQueryParams,
} from './query'

function getLevelColor(level: string) {
  switch (level.toLowerCase()) {
    case 'trace':
      return 'default'
    case 'debug':
      return 'processing'
    case 'information':
      return 'blue'
    case 'warning':
      return 'gold'
    case 'error':
      return 'red'
    case 'critical':
      return 'magenta'
    default:
      return 'default'
  }
}

function formatPropertiesJson(value?: string) {
  if (!value) {
    return '-'
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function SummaryTagGroup({
  items,
  onClick,
}: {
  items: ApplicationLogSummaryGroup[]
  onClick?: (name: string) => void
}) {
  if (!items.length) {
    return <Typography.Text type="secondary">-</Typography.Text>
  }

  return (
    <Space wrap size={[8, 8]}>
      {items.map((item) => (
        <Tag
          key={`${item.name}-${item.count}`}
          color="blue"
          style={{ cursor: onClick ? 'pointer' : 'default' }}
          onClick={() => onClick?.(item.name)}
        >
          {item.name || '-'} ({item.count})
        </Tag>
      ))}
    </Space>
  )
}

export default function SystemCenterLogsPage() {
  const { t } = useTranslation()
  const canManageScheduledTasks = useAuthStore((state) => state.access.canManageScheduledTasks)
  const [form] = Form.useForm<CenterLogQueryFormValues>()
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [schedulerLoading, setSchedulerLoading] = useState(false)
  const [schedulerSaving, setSchedulerSaving] = useState(false)
  const [data, setData] = useState<ApplicationLogItem[]>([])
  const [schedulerStatus, setSchedulerStatus] = useState<ScheduledTaskRuntimeControlStatus | null>(null)
  const [summaryTotal, setSummaryTotal] = useState(0)
  const [summaryByLevel, setSummaryByLevel] = useState<ApplicationLogSummaryGroup[]>([])
  const [summaryByProject, setSummaryByProject] = useState<ApplicationLogSummaryGroup[]>([])
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_CENTER_LOG_PAGE_SIZE)
  const [total, setTotal] = useState(0)
  const [activeQuery, setActiveQuery] = useState<ApplicationLogQueryParams>(
    buildDefaultCenterLogQueryParams(),
  )
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailRecord, setDetailRecord] = useState<ApplicationLogItem | null>(null)

  useEffect(() => {
    form.setFieldsValue({
      projectCodes: [DEFAULT_CENTER_LOG_PROJECT_CODE],
    })
  }, [form])

  const loadData = async (query: ApplicationLogQueryParams) => {
    setLoading(true)
    try {
      const [listResult, summaryResult] = await Promise.all([
        getCenterLogs(query),
        getCenterLogSummary(query),
      ])
      setData(listResult.items)
      setTotal(listResult.total)
      setPageNumber(listResult.pageNumber)
      setPageSize(listResult.pageSize)
      setSummaryTotal(summaryResult.total)
      setSummaryByLevel(summaryResult.byLevel ?? [])
      setSummaryByProject(summaryResult.byProject ?? [])
    } catch (error) {
      console.error(error)
      message.error(t('system.centerLogs.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  const loadSchedulerStatus = async () => {
    setSchedulerLoading(true)
    try {
      setSchedulerStatus(await getScheduledTaskRuntimeControl())
    } catch (error) {
      console.error(error)
      message.error(t('system.centerLogs.scheduler.loadFailed'))
    } finally {
      setSchedulerLoading(false)
    }
  }

  useEffect(() => {
    void loadData(activeQuery)
  }, [activeQuery])

  useEffect(() => {
    void loadSchedulerStatus()
  }, [])

  const handleQuery = () => {
    const values = form.getFieldsValue()
    setActiveQuery(buildCenterLogQueryParams(values, 1, pageSize))
  }

  const handleReset = () => {
    form.setFieldsValue({
      projectCodes: [DEFAULT_CENTER_LOG_PROJECT_CODE],
      level: undefined,
      sourceType: undefined,
      category: undefined,
      requestPath: undefined,
      traceId: undefined,
      keyword: undefined,
      timeRange: undefined,
    })
    setActiveQuery(buildDefaultCenterLogQueryParams(pageSize))
  }

  const handleOpenDetail = async (record: ApplicationLogItem) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailRecord(null)
    try {
      const detail = await getCenterLogDetail(record.id)
      setDetailRecord(detail)
    } catch (error) {
      console.error(error)
      message.error(t('system.centerLogs.loadDetailFailed'))
    } finally {
      setDetailLoading(false)
    }
  }

  const handleLevelQuickFilter = (level: string) => {
    form.setFieldValue('level', level)
    setActiveQuery(buildCenterLogQueryParams({ ...form.getFieldsValue(), level }, 1, pageSize))
  }

  const handleProjectQuickFilter = (projectCode: string) => {
    const projectCodes = [projectCode]
    form.setFieldValue('projectCodes', projectCodes)
    setActiveQuery(buildCenterLogQueryParams({ ...form.getFieldsValue(), projectCodes }, 1, pageSize))
  }

  const handleUpdateScheduler = async (next: {
    schedulerEnabled?: boolean
    activeInstanceId?: string | null
  }) => {
    if (!schedulerStatus) {
      return
    }
    if (!canManageScheduledTasks) {
      message.warning(t('system.centerLogs.scheduler.readOnly'))
      return
    }

    setSchedulerSaving(true)
    try {
      const updated = await updateScheduledTaskRuntimeControl({
        schedulerEnabled: next.schedulerEnabled ?? schedulerStatus.schedulerEnabled,
        activeInstanceId: next.activeInstanceId ?? schedulerStatus.activeInstanceId,
      })
      setSchedulerStatus(updated)
      message.success(t('system.centerLogs.scheduler.updateSuccess'))
    } catch (error) {
      console.error(error)
      message.error(t('system.centerLogs.scheduler.updateFailed'))
    } finally {
      setSchedulerSaving(false)
    }
  }

  const schedulerInstanceOptions =
    schedulerStatus?.knownInstances.map((instance) => ({
      value: instance.instanceId,
      label: `${instance.instanceId}${instance.isCurrent ? ` (${t('system.centerLogs.scheduler.current')})` : ''}`,
    })) ?? []

  const columns = useMemo<ColumnsType<ApplicationLogItem>>(
    () => [
      {
        title: t('system.centerLogs.columns.timestamp'),
        dataIndex: 'timestampUtc',
        width: 180,
        render: (value: string) => formatCenterLogTimestamp(value),
      },
      {
        title: t('system.centerLogs.columns.project'),
        dataIndex: 'projectCode',
        width: 120,
      },
      {
        title: t('system.centerLogs.columns.level'),
        dataIndex: 'level',
        width: 110,
        render: (value: string) => <Tag color={getLevelColor(value)}>{value}</Tag>,
      },
      {
        title: t('system.centerLogs.columns.sourceType'),
        dataIndex: 'sourceType',
        width: 110,
      },
      {
        title: t('system.centerLogs.columns.category'),
        dataIndex: 'category',
        width: 160,
        ellipsis: true,
      },
      {
        title: t('system.centerLogs.columns.message'),
        dataIndex: 'message',
        ellipsis: true,
      },
      {
        title: t('system.centerLogs.columns.requestPath'),
        dataIndex: 'requestPath',
        width: 220,
        ellipsis: true,
      },
      {
        title: t('system.centerLogs.columns.statusCode'),
        dataIndex: 'statusCode',
        width: 100,
        render: (value?: number) => value ?? '-',
      },
      {
        title: t('system.centerLogs.columns.traceId'),
        dataIndex: 'traceId',
        width: 180,
        ellipsis: true,
      },
      {
        title: t('common.action'),
        key: 'actions',
        width: 90,
        fixed: 'right',
        render: (_, record) => (
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleOpenDetail(record)}>
            {t('common.view')}
          </Button>
        ),
      },
    ],
    [t],
  )

  const timeRangePresets: RangePickerProps['presets'] = [
    { label: t('system.centerLogs.presets.lastHour'), value: [dayjs().subtract(1, 'hour'), dayjs()] },
    { label: t('system.centerLogs.presets.last24Hours'), value: [dayjs().subtract(24, 'hour'), dayjs()] },
    { label: t('system.centerLogs.presets.last7Days'), value: [dayjs().subtract(7, 'day'), dayjs()] },
  ]

  return (
    <PageContainer
      title={t('system.centerLogs.pageTitle')}
      subtitle={t('system.centerLogs.pageSubtitle')}
      extra={
        <Button icon={<ReloadOutlined />} onClick={() => void loadData(activeQuery)}>
          {t('common.refresh')}
        </Button>
      }
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Card
          title={t('system.centerLogs.scheduler.title')}
          extra={
            <Button
              icon={<ReloadOutlined />}
              loading={schedulerLoading}
              onClick={() => void loadSchedulerStatus()}
            >
              {t('common.refresh')}
            </Button>
          }
        >
          <Row gutter={[16, 16]} align="middle">
            <Col xs={24} md={6}>
              <Statistic
                title={t('system.centerLogs.scheduler.effectiveStatus')}
                value={
                  schedulerStatus?.effectiveSchedulerEnabled
                    ? t('system.centerLogs.scheduler.running')
                    : t('system.centerLogs.scheduler.stopped')
                }
                valueStyle={{
                  color: schedulerStatus?.effectiveSchedulerEnabled ? '#1677ff' : '#cf1322',
                  fontSize: 20,
                }}
              />
            </Col>
            <Col xs={24} md={6}>
              <Typography.Text type="secondary">
                {t('system.centerLogs.scheduler.currentInstance')}
              </Typography.Text>
              <Typography.Paragraph copyable style={{ marginBottom: 0 }}>
                {schedulerStatus?.currentInstanceId ?? '-'}
              </Typography.Paragraph>
            </Col>
            <Col xs={24} md={7}>
              <Typography.Text type="secondary">
                {t('system.centerLogs.scheduler.activeInstance')}
              </Typography.Text>
              <Select
                loading={schedulerLoading}
                disabled={!schedulerStatus || schedulerSaving || !canManageScheduledTasks}
                value={schedulerStatus?.activeInstanceId}
                options={schedulerInstanceOptions}
                style={{ width: '100%', marginTop: 4 }}
                onChange={(value) => void handleUpdateScheduler({ activeInstanceId: value })}
              />
              {!canManageScheduledTasks ? (
                <Typography.Text type="secondary">
                  {t('system.centerLogs.scheduler.readOnly')}
                </Typography.Text>
              ) : null}
            </Col>
            <Col xs={24} md={5}>
              <Typography.Text type="secondary">
                {t('system.centerLogs.scheduler.enabled')}
              </Typography.Text>
              <div style={{ marginTop: 8 }}>
                <Switch
                  checked={!!schedulerStatus?.schedulerEnabled}
                  loading={schedulerSaving}
                  disabled={
                    !schedulerStatus
                    || !schedulerStatus.schedulerEnabledByConfig
                    || !canManageScheduledTasks
                  }
                  checkedChildren={t('common.enabled')}
                  unCheckedChildren={t('common.disabled')}
                  onChange={(checked) => void handleUpdateScheduler({ schedulerEnabled: checked })}
                />
              </div>
              {!schedulerStatus?.schedulerEnabledByConfig ? (
                <Typography.Text type="danger">
                  {t('system.centerLogs.scheduler.disabledByConfig')}
                </Typography.Text>
              ) : null}
            </Col>
          </Row>
        </Card>

        <Card>
          <Form form={form} layout="vertical" onFinish={() => void handleQuery()}>
            <Row gutter={16}>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.projectCode')} name="projectCodes">
                  <Select
                    allowClear
                    mode="tags"
                    maxTagCount="responsive"
                    placeholder={t('system.centerLogs.filters.projectPlaceholder')}
                    options={CENTER_LOG_PROJECT_OPTIONS.map((item) => ({ label: item, value: item }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={4}>
                <Form.Item label={t('system.centerLogs.filters.level')} name="level">
                  <Select
                    allowClear
                    placeholder={t('system.centerLogs.filters.levelPlaceholder')}
                    options={CENTER_LOG_LEVEL_OPTIONS.map((item) => ({ label: item, value: item }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={4}>
                <Form.Item label={t('system.centerLogs.filters.sourceType')} name="sourceType">
                  <Select
                    allowClear
                    placeholder={t('system.centerLogs.filters.sourceTypePlaceholder')}
                    options={CENTER_LOG_SOURCE_TYPE_OPTIONS.map((item) => ({ label: item, value: item }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item label={t('system.centerLogs.filters.timeRange')} name="timeRange">
                  <DatePicker.RangePicker
                    showTime
                    style={{ width: '100%' }}
                    presets={timeRangePresets}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.keyword')} name="keyword">
                  <Input
                    placeholder={t('system.centerLogs.filters.keywordPlaceholder')}
                    allowClear
                    onPressEnter={() => void handleQuery()}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.category')} name="category">
                  <Input
                    placeholder={t('system.centerLogs.filters.categoryPlaceholder')}
                    allowClear
                    onPressEnter={() => void handleQuery()}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.requestPath')} name="requestPath">
                  <Input
                    placeholder={t('system.centerLogs.filters.requestPathPlaceholder')}
                    allowClear
                    onPressEnter={() => void handleQuery()}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.traceId')} name="traceId">
                  <Input
                    placeholder={t('system.centerLogs.filters.traceIdPlaceholder')}
                    allowClear
                    onPressEnter={() => void handleQuery()}
                  />
                </Form.Item>
              </Col>
            </Row>
            <Space>
              <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
                {t('common.query')}
              </Button>
              <Button onClick={handleReset}>{t('common.reset')}</Button>
            </Space>
          </Form>
        </Card>

        <Row gutter={16}>
          <Col xs={24} md={6}>
            <Card>
              <Statistic title={t('system.centerLogs.summary.total')} value={summaryTotal} />
            </Card>
          </Col>
          <Col xs={24} md={18}>
            <Card title={t('system.centerLogs.summary.byLevel')}>
              <SummaryTagGroup items={summaryByLevel} onClick={handleLevelQuickFilter} />
            </Card>
          </Col>
        </Row>

        <Card title={t('system.centerLogs.summary.byProject')}>
          <SummaryTagGroup items={summaryByProject} onClick={handleProjectQuickFilter} />
        </Card>

        <Card>
          <Table<ApplicationLogItem>
            rowKey="id"
            loading={loading}
            columns={columns}
            dataSource={data}
            scroll={{ x: 1380 }}
            pagination={{
              current: pageNumber,
              pageSize,
              total,
              showSizeChanger: true,
              showTotal: (value) => t('system.centerLogs.pagination.total', { total: value }),
              onChange: (nextPage, nextPageSize) => {
                setActiveQuery({
                  ...activeQuery,
                  pageNumber: nextPage,
                  pageSize: nextPageSize,
                })
              },
            }}
          />
        </Card>
      </Space>

      <Drawer
        title={t('system.centerLogs.detailTitle')}
        width={820}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailRecord(null)
        }}
        destroyOnClose
      >
        {detailRecord ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2} size="small">
              <Descriptions.Item label={t('system.centerLogs.columns.timestamp')}>
                {formatCenterLogTimestamp(detailRecord.timestampUtc)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.level')}>
                <Tag color={getLevelColor(detailRecord.level)}>{detailRecord.level}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.project')}>
                {detailRecord.projectCode}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.sourceType')}>
                {detailRecord.sourceType}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.category')}>
                {detailRecord.category || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.serviceName')}>
                {detailRecord.serviceName || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.requestPath')} span={2}>
                {detailRecord.requestPath || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.requestMethod')}>
                {detailRecord.requestMethod || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.clientIp')}>
                {detailRecord.clientIp || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.traceId')} span={2}>
                {detailRecord.traceId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.user')}>
                {detailRecord.userName || detailRecord.userId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.columns.statusCode')}>
                {detailRecord.statusCode ?? '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.fields.message')} span={2}>
                <Typography.Paragraph style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {detailRecord.message}
                </Typography.Paragraph>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.fields.exception')} span={2}>
                <Typography.Paragraph style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {detailRecord.exceptionType || detailRecord.exceptionMessage
                    ? `${detailRecord.exceptionType || ''}\n${detailRecord.exceptionMessage || ''}`.trim()
                    : '-'}
                </Typography.Paragraph>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.fields.stackTrace')} span={2}>
                <Typography.Paragraph
                  code
                  style={{ marginBottom: 0, whiteSpace: 'pre-wrap', maxHeight: 240, overflow: 'auto' }}
                >
                  {detailRecord.stackTrace || '-'}
                </Typography.Paragraph>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.centerLogs.fields.properties')} span={2}>
                <Typography.Paragraph
                  code
                  style={{ marginBottom: 0, whiteSpace: 'pre-wrap', maxHeight: 240, overflow: 'auto' }}
                >
                  {formatPropertiesJson(detailRecord.propertiesJson)}
                </Typography.Paragraph>
              </Descriptions.Item>
            </Descriptions>
          </Space>
        ) : null}
        {detailLoading ? <Typography.Text type="secondary">{t('system.centerLogs.loadingDetail')}</Typography.Text> : null}
      </Drawer>
    </PageContainer>
  )
}
