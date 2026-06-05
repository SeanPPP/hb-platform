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
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { RangePickerProps } from 'antd/es/date-picker'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getCenterLogDetail, getCenterLogs, getCenterLogSummary } from '../../../services/centerLogService'
import type { ApplicationLogItem, ApplicationLogQueryParams, ApplicationLogSummaryGroup } from '../../../types/centerLog'

interface QueryFormValues {
  projectCode?: string
  level?: string
  keyword?: string
  timeRange?: [Dayjs, Dayjs]
}

const DEFAULT_PROJECT_CODE = 'hbweb_rv'
const DEFAULT_PAGE_SIZE = 20

const LEVEL_OPTIONS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical']

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

function buildQueryParams(values: QueryFormValues, pageNumber = 1, pageSize = DEFAULT_PAGE_SIZE): ApplicationLogQueryParams {
  return {
    projectCode: values.projectCode?.trim() || undefined,
    level: values.level || undefined,
    keyword: values.keyword?.trim() || undefined,
    startUtc: values.timeRange?.[0]?.toISOString(),
    endUtc: values.timeRange?.[1]?.toISOString(),
    pageNumber,
    pageSize,
    sortBy: 'TimestampUtc',
    sortDirection: 'desc',
  }
}

function formatDateTime(value?: string) {
  if (!value) {
    return '-'
  }

  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
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
  const [form] = Form.useForm<QueryFormValues>()
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [data, setData] = useState<ApplicationLogItem[]>([])
  const [summaryTotal, setSummaryTotal] = useState(0)
  const [summaryByLevel, setSummaryByLevel] = useState<ApplicationLogSummaryGroup[]>([])
  const [summaryByProject, setSummaryByProject] = useState<ApplicationLogSummaryGroup[]>([])
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [total, setTotal] = useState(0)
  const [activeQuery, setActiveQuery] = useState<ApplicationLogQueryParams>(
    buildQueryParams({ projectCode: DEFAULT_PROJECT_CODE }, 1, DEFAULT_PAGE_SIZE),
  )
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailRecord, setDetailRecord] = useState<ApplicationLogItem | null>(null)

  useEffect(() => {
    form.setFieldsValue({
      projectCode: DEFAULT_PROJECT_CODE,
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

  useEffect(() => {
    void loadData(activeQuery)
  }, [activeQuery])

  const handleQuery = () => {
    const values = form.getFieldsValue()
    setActiveQuery(buildQueryParams(values, 1, pageSize))
  }

  const handleReset = () => {
    form.setFieldsValue({
      projectCode: DEFAULT_PROJECT_CODE,
      level: undefined,
      keyword: undefined,
      timeRange: undefined,
    })
    setActiveQuery(buildQueryParams({ projectCode: DEFAULT_PROJECT_CODE }, 1, pageSize))
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
    setActiveQuery(buildQueryParams({ ...form.getFieldsValue(), level }, 1, pageSize))
  }

  const handleProjectQuickFilter = (projectCode: string) => {
    form.setFieldValue('projectCode', projectCode)
    setActiveQuery(buildQueryParams({ ...form.getFieldsValue(), projectCode }, 1, pageSize))
  }

  const columns = useMemo<ColumnsType<ApplicationLogItem>>(
    () => [
      {
        title: t('system.centerLogs.columns.timestamp'),
        dataIndex: 'timestampUtc',
        width: 180,
        render: (value: string) => formatDateTime(value),
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
        width: 140,
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
        <Card>
          <Form form={form} layout="vertical" onFinish={() => void handleQuery()}>
            <Row gutter={16}>
              <Col xs={24} md={6}>
                <Form.Item label={t('system.centerLogs.filters.projectCode')} name="projectCode">
                  <Input placeholder={t('system.centerLogs.filters.projectPlaceholder')} allowClear />
                </Form.Item>
              </Col>
              <Col xs={24} md={4}>
                <Form.Item label={t('system.centerLogs.filters.level')} name="level">
                  <Select
                    allowClear
                    placeholder={t('system.centerLogs.filters.levelPlaceholder')}
                    options={LEVEL_OPTIONS.map((item) => ({ label: item, value: item }))}
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
            scroll={{ x: 1200 }}
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
                {formatDateTime(detailRecord.timestampUtc)}
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
              <Descriptions.Item label={t('system.centerLogs.columns.requestPath')} span={2}>
                {detailRecord.requestPath || '-'}
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
