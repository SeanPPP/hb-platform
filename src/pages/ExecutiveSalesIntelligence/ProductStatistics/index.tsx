import {
  Alert,
  Button,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Modal,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  getProductStoreDailyStatisticStates,
  getProductStoreDailyStatisticSummary,
  recalculateProductStoreDaily,
  recalculateProductStoreDailyRange,
  recalculateRecentProductStoreDaily,
} from '../../../services/salesStatisticsManagementService'
import type {
  JobTriggerResponse,
  ProductStoreDailyStatisticSummary,
  SalesStatisticRefreshState,
  SalesStatisticStatus,
} from '../../../services/salesStatisticsManagementService'
import { RequestError } from '../../../utils/request'

const { RangePicker } = DatePicker
export const MAX_PRODUCT_STATISTIC_RANGE_DAYS = 31

type RangeValue = [Dayjs, Dayjs] | null

const statusOptions: Array<{ label: string; value: SalesStatisticStatus | '' }> = [
  { label: '全部状态', value: '' },
  { label: '已排队', value: 'Queued' },
  { label: '执行中', value: 'Running' },
  { label: '待统计', value: 'Pending' },
  { label: '已更新', value: 'Fresh' },
  { label: '已过期', value: 'Stale' },
  { label: '失败', value: 'Failed' },
]

const statusColorMap: Record<string, string> = {
  Queued: 'purple',
  Running: 'processing',
  Pending: 'blue',
  Fresh: 'green',
  Stale: 'orange',
  Failed: 'red',
}

const reconciliationColorMap: Record<string, string> = {
  Passed: 'green',
  Pending: 'blue',
  Failed: 'red',
}

function formatDate(value?: string) {
  if (!value) {
    return '--'
  }

  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD') : value
}

function formatDateTime(value?: string) {
  if (!value) {
    return '--'
  }

  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}

function formatNumber(value?: number) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return value.toLocaleString('en-AU')
}

function formatCurrency(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return value.toLocaleString('en-AU', {
    style: 'currency',
    currency: 'AUD',
  })
}

function renderStatus(status?: string) {
  const normalizedStatus = status || 'Pending'
  return <Tag color={statusColorMap[normalizedStatus] ?? 'default'}>{normalizedStatus}</Tag>
}

function renderReconciliationStatus(status?: string) {
  const normalizedStatus = status || 'Pending'
  return <Tag color={reconciliationColorMap[normalizedStatus] ?? 'default'}>{normalizedStatus}</Tag>
}

export function getProductStatisticRangeDays(dateRange?: RangeValue) {
  if (!dateRange?.[0] || !dateRange?.[1]) {
    return 0
  }

  return dateRange[1].startOf('day').diff(dateRange[0].startOf('day'), 'day') + 1
}

export function isProductStatisticRangeWithinLimit(dateRange?: RangeValue) {
  const requestedDays = getProductStatisticRangeDays(dateRange)
  return requestedDays > 0 && requestedDays <= MAX_PRODUCT_STATISTIC_RANGE_DAYS
}

export function getProductStatisticActionErrorMessage(error: unknown) {
  if (
    error instanceof RequestError &&
    error.payload &&
    typeof error.payload === 'object' &&
    'message' in error.payload &&
    typeof error.payload.message === 'string' &&
    error.payload.message.trim()
  ) {
    return error.payload.message
  }

  if (error instanceof RequestError && error.message) {
    return error.message
  }

  if (
    error &&
    typeof error === 'object' &&
    'message' in error &&
    typeof error.message === 'string' &&
    error.message.trim()
  ) {
    return error.message
  }

  return '统计重算触发失败'
}

function buildProductStatisticSubmitMessage(result: JobTriggerResponse, fallback: string) {
  const parts = [result.message || fallback]
  if (result.skippedDates?.length) {
    parts.push(`已跳过执行中的日期：${result.skippedDates.join(', ')}`)
  }
  return parts.join('；')
}

export function isProductStatisticRunning(status?: string) {
  return status === 'Queued' || status === 'Running'
}

export function mergeUniqueDates(currentDates: string[], submittedDates?: string[]) {
  return Array.from(new Set([...currentDates, ...(submittedDates ?? [])])).sort()
}

export default function ProductStatisticsPage() {
  const [form] = Form.useForm()
  const [rows, setRows] = useState<SalesStatisticRefreshState[]>([])
  const [loading, setLoading] = useState(false)
  const [actionLoading, setActionLoading] = useState(false)
  const [detailOpen, setDetailOpen] = useState(false)
  const [summaryLoading, setSummaryLoading] = useState(false)
  const [summary, setSummary] = useState<ProductStoreDailyStatisticSummary | null>(null)
  const [activePollDates, setActivePollDates] = useState<string[]>([])

  const defaultRange = useMemo<RangeValue>(() => [dayjs().subtract(6, 'day'), dayjs()], [])

  const loadStates = useCallback(async () => {
    const values = form.getFieldsValue() as {
      statisticType?: string
      dateRange?: RangeValue
      status?: SalesStatisticStatus | ''
    }
    const dateRange = values.dateRange ?? defaultRange

    setLoading(true)
    try {
      // 查询统计状态只读取状态表，不触发后台重算。
      const data = await getProductStoreDailyStatisticStates({
        statisticType: values.statisticType || 'ProductStoreDaily',
        startDate: dateRange?.[0]?.format('YYYY-MM-DD'),
        endDate: dateRange?.[1]?.format('YYYY-MM-DD'),
        status: values.status,
      })
      setRows(data)
    } catch (error) {
      console.error(error)
      message.error('加载商品统计状态失败')
    } finally {
      setLoading(false)
    }
  }, [defaultRange, form])

  useEffect(() => {
    form.setFieldsValue({
      statisticType: 'ProductStoreDaily',
      dateRange: defaultRange,
      status: '',
    })
    void loadStates()
  }, [defaultRange, form, loadStates])

  const openDetail = useCallback(async (record: SalesStatisticRefreshState) => {
    setDetailOpen(true)
    setSummary(null)
    setSummaryLoading(true)
    try {
      const data = await getProductStoreDailyStatisticSummary(formatDate(record.date))
      setSummary(data)
    } catch (error) {
      console.error(error)
      message.error('加载统计详情失败')
    } finally {
      setSummaryLoading(false)
    }
  }, [])

  const refreshActiveSubmissions = useCallback(async () => {
    if (!activePollDates.length) {
      return
    }

    try {
      const data = await getProductStoreDailyStatisticStates({
        statisticType: 'ProductStoreDaily',
        startDate: activePollDates[0],
        endDate: activePollDates[activePollDates.length - 1],
      })
      const activeDateSet = new Set(activePollDates)
      const stateMap = new Map(
        data
          .filter((row) => activeDateSet.has(formatDate(row.date)))
          .map((row) => [formatDate(row.date), row]),
      )
      const runningDates = activePollDates.filter((date) => {
        const row = stateMap.get(date)
        return row ? isProductStatisticRunning(row.status) : true
      })

      await loadStates()
      if (runningDates.length) {
        setActivePollDates(runningDates)
        return
      }

      setActivePollDates([])
      message.success('商品统计重算已完成')
    } catch (error) {
      console.error(error)
      message.error('刷新商品统计任务状态失败')
    }
  }, [activePollDates, loadStates])

  useEffect(() => {
    if (!activePollDates.length) {
      return undefined
    }

    const timer = window.setInterval(() => {
      void refreshActiveSubmissions()
    }, 7000)

    return () => window.clearInterval(timer)
  }, [activePollDates, refreshActiveSubmissions])

  const runAction = useCallback(async (action: () => Promise<JobTriggerResponse>, successText: string) => {
    setActionLoading(true)
    try {
      const result = await action()
      message.success(buildProductStatisticSubmitMessage(result, successText))
      if (result.submittedDates?.length) {
        setActivePollDates((currentDates) => mergeUniqueDates(currentDates, result.submittedDates))
      }
      await loadStates()
    } catch (error) {
      console.error(error)
      message.error(getProductStatisticActionErrorMessage(error))
    } finally {
      setActionLoading(false)
    }
  }, [loadStates])

  const handleRecalculateToday = useCallback(() => {
    const today = dayjs().format('YYYY-MM-DD')
    void runAction(() => recalculateProductStoreDaily(today), '已触发当天商品统计重算')
  }, [runAction])

  const handleRecalculateRecent = useCallback(() => {
    void runAction(() => recalculateRecentProductStoreDaily(7), '已触发最近 7 天商品统计重算')
  }, [runAction])

  const handleRecalculateRange = useCallback(() => {
    const values = form.getFieldsValue() as { dateRange?: RangeValue }
    const dateRange = values.dateRange

    if (!dateRange?.[0] || !dateRange?.[1]) {
      message.warning('请先选择日期范围')
      return
    }

    if (!isProductStatisticRangeWithinLimit(dateRange)) {
      message.warning(`商品统计一次最多重算 ${MAX_PRODUCT_STATISTIC_RANGE_DAYS} 天，请缩小日期范围`)
      return
    }

    Modal.confirm({
      title: '确认重算日期范围？',
      content: `${dateRange[0].format('YYYY-MM-DD')} 至 ${dateRange[1].format('YYYY-MM-DD')}`,
      okText: '重算',
      cancelText: '取消',
      onOk: () => runAction(
        () => recalculateProductStoreDailyRange(
          dateRange[0].format('YYYY-MM-DD'),
          dateRange[1].format('YYYY-MM-DD'),
        ),
        '已触发日期范围商品统计重算',
      ),
    })
  }, [form, runAction])

  const columns = useMemo<ColumnsType<SalesStatisticRefreshState>>(() => [
    {
      title: '日期',
      dataIndex: 'date',
      width: 130,
      render: (value?: string) => formatDate(value),
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 110,
      render: (value?: string) => renderStatus(value),
    },
    {
      title: 'POSM水位',
      dataIndex: 'lastSourceUploadTime',
      width: 180,
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: '最后统计时间',
      dataIndex: 'lastAggregatedAtUtc',
      width: 180,
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: '最后检查时间',
      dataIndex: 'lastCheckedAtUtc',
      width: 180,
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: '失败原因',
      dataIndex: 'errorMessage',
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: '操作',
      key: 'actions',
      fixed: 'right',
      width: 180,
      render: (_, record) => (
        <Space size="small">
          <Button type="link" size="small" onClick={() => openDetail(record)}>
            详情
          </Button>
          <Button
            type="link"
            size="small"
            loading={actionLoading}
            onClick={() => runAction(
              () => recalculateProductStoreDaily(formatDate(record.date)),
              '已触发该日期商品统计重算',
            )}
          >
            重算当天
          </Button>
        </Space>
      ),
    },
  ], [actionLoading, openDetail, runAction])

  return (
    <div style={{ padding: 24 }}>
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <div>
          <Typography.Title level={4} style={{ marginBottom: 4 }}>
            商品统计状态
          </Typography.Title>
          <Typography.Text type="secondary">
            查看商品/分店/每日统计状态，并触发热销商品统计重算。
          </Typography.Text>
        </div>

        <Form form={form} layout="inline" onFinish={loadStates}>
          <Form.Item name="statisticType" label="统计类型">
            <Select
              style={{ width: 190 }}
              options={[{ label: '商品/分店/每日', value: 'ProductStoreDaily' }]}
            />
          </Form.Item>
          <Form.Item name="dateRange" label="日期范围">
            <RangePicker allowClear={false} />
          </Form.Item>
          <Form.Item name="status" label="状态">
            <Select style={{ width: 130 }} options={statusOptions} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={loading}>
                查询
              </Button>
              <Button onClick={handleRecalculateToday} loading={actionLoading}>
                重算当天
              </Button>
              <Button onClick={handleRecalculateRecent} loading={actionLoading}>
                重算最近7天
              </Button>
              <Button onClick={handleRecalculateRange} loading={actionLoading}>
                重算日期范围
              </Button>
            </Space>
          </Form.Item>
        </Form>

        <Alert
          type="info"
          showIcon
          message="POSM 水位只表示原始销售上传进度，今天的数据可能因门店延迟上传而暂未完整。"
        />

        <Table
          rowKey={(record) => `${record.statisticType}-${record.date}`}
          columns={columns}
          dataSource={rows}
          loading={loading}
          scroll={{ x: 1080 }}
          pagination={{ pageSize: 20, showSizeChanger: true }}
        />
      </Space>

      <Drawer
        title={summary ? `统计详情：${formatDate(summary.date)}` : '统计详情'}
        width={560}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
      >
        <Descriptions column={1} bordered size="small">
          <Descriptions.Item label="商品统计记录数">
            {summaryLoading ? '加载中...' : formatNumber(summary?.recordCount)}
          </Descriptions.Item>
          <Descriptions.Item label="销量合计">
            {summaryLoading ? '加载中...' : formatNumber(summary?.totalQuantity)}
          </Descriptions.Item>
          <Descriptions.Item label="销售额合计">
            {summaryLoading ? '加载中...' : formatCurrency(summary?.totalAmount)}
          </Descriptions.Item>
          <Descriptions.Item label="毛利合计">
            {summaryLoading ? '加载中...' : formatCurrency(summary?.grossProfit)}
          </Descriptions.Item>
          <Descriptions.Item label="对账状态">
            {summaryLoading ? '加载中...' : renderReconciliationStatus(summary?.reconciliationStatus)}
          </Descriptions.Item>
          <Descriptions.Item label="POSM水位">
            {summaryLoading ? '加载中...' : formatDateTime(summary?.lastSourceUploadTime)}
          </Descriptions.Item>
          <Descriptions.Item label="最后统计时间">
            {summaryLoading ? '加载中...' : formatDateTime(summary?.lastAggregatedAtUtc)}
          </Descriptions.Item>
          <Descriptions.Item label="最后检查时间">
            {summaryLoading ? '加载中...' : formatDateTime(summary?.lastCheckedAtUtc)}
          </Descriptions.Item>
          <Descriptions.Item label="失败原因">
            {summaryLoading ? '加载中...' : summary?.errorMessage || '--'}
          </Descriptions.Item>
        </Descriptions>
      </Drawer>
    </div>
  )
}
