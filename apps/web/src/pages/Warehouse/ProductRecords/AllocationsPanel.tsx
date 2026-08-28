import { SearchOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  DatePicker,
  Empty,
  Input,
  Space,
  Statistic,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { queryWarehouseProductAllocations } from '../../../services/warehouseProductRecordsService'
import type {
  WarehouseProductAllocationBranch,
  WarehouseProductAllocationReport,
} from '../../../types/warehouseProductRecords'
import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'
import {
  buildBrisbaneDateRange,
  filterAllocationBranches,
  formatAustralianCurrency,
  formatQuantity,
  getDateRangeError,
  isAbortError,
  sumAllocationBranchAmounts,
} from './logic'
import { MeasuredTable } from '../../../components/MeasuredTable'

const { RangePicker } = DatePicker

interface AllocationsPanelProps {
  productCode: string
  enabled: boolean
}

export default function AllocationsPanel({ productCode, enabled }: AllocationsPanelProps) {
  const { t } = useTranslation()
  const initialRange = useMemo(() => {
    const range = buildBrisbaneDateRange(30)
    return [dayjs(range.startDate), dayjs(range.endDate)] as [Dayjs, Dayjs]
  }, [])

  const [brisbaneToday, setBrisbaneToday] = useState(initialRange[1].format('YYYY-MM-DD'))
  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(initialRange)
  const [activeQuickDays, setActiveQuickDays] = useState<number | null>(30)
  const [committedRange, setCommittedRange] = useState({ startDate: initialRange[0].format('YYYY-MM-DD'), endDate: initialRange[1].format('YYYY-MM-DD') })
  const [keyword, setKeyword] = useState('')
  const [report, setReport] = useState<WarehouseProductAllocationReport | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const abortRef = useRef<AbortController>()
  const guardRef = useRef(createLatestRequestGuard())

  const commitRange = (range: [Dayjs, Dayjs], quickDays: number | null) => {
    const startDate = range[0].format('YYYY-MM-DD')
    const endDate = range[1].format('YYYY-MM-DD')
    const rangeError = getDateRangeError(startDate, endDate)
    if (rangeError) {
      message.warning(t('warehouseProductRecords.dateRangeInvalid'))
      return
    }
    setDraftRange(range)
    setActiveQuickDays(quickDays)
    setCommittedRange({ startDate, endDate })
  }

  const applyQuickRange = (days: number) => {
    const range = buildBrisbaneDateRange(days)
    setBrisbaneToday(range.endDate)
    commitRange([dayjs(range.startDate), dayjs(range.endDate)], days)
  }

  const resetRange = () => {
    setKeyword('')
    const range = buildBrisbaneDateRange(30)
    setBrisbaneToday(range.endDate)
    commitRange([dayjs(range.startDate), dayjs(range.endDate)], 30)
  }

  useEffect(() => {
    if (!enabled) return
    const currentRange = buildBrisbaneDateRange(activeQuickDays ?? 1)
    if (currentRange.endDate === brisbaneToday) return

    setBrisbaneToday(currentRange.endDate)
    // 快捷范围跟随新的一天滚动；用户自定义范围保持不变。
    if (activeQuickDays != null) {
      const nextRange: [Dayjs, Dayjs] = [dayjs(currentRange.startDate), dayjs(currentRange.endDate)]
      setDraftRange(nextRange)
      setCommittedRange({
        startDate: currentRange.startDate,
        endDate: currentRange.endDate,
      })
    }
  }, [activeQuickDays, brisbaneToday, enabled])

  useEffect(() => {
    abortRef.current?.abort()
    guardRef.current.invalidate()
    if (!enabled) {
      setLoading(false)
      return
    }

    const controller = new AbortController()
    abortRef.current = controller
    const requestId = guardRef.current.begin()
    setReport(null)
    setLoading(true)
    setError(null)

    queryWarehouseProductAllocations(
      productCode,
      { startDate: committedRange.startDate, endDate: committedRange.endDate },
      controller.signal,
    )
      .then((result) => {
        if (!guardRef.current.isLatest(requestId)) return
        setReport(result)
      })
      .catch((nextError) => {
        if (!guardRef.current.isLatest(requestId) || isAbortError(nextError)) return
        setReport(null)
        setError(nextError)
      })
      .finally(() => {
        if (guardRef.current.isLatest(requestId)) setLoading(false)
      })

    return () => {
      controller.abort()
      guardRef.current.invalidate()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, productCode, committedRange])

  const filteredBranches = useMemo(
    () => filterAllocationBranches(report?.branches ?? [], keyword),
    [report?.branches, keyword],
  )
  const filteredTotals = useMemo(() => sumAllocationBranchAmounts(filteredBranches), [filteredBranches])

  const columns: ColumnsType<WarehouseProductAllocationBranch> = [
    {
      title: t('warehouseProductRecords.storeCode'),
      dataIndex: 'storeCode',
      key: 'storeCode',
      width: 130,
      fixed: 'left',
      render: (value: string) => <Typography.Text>{value || '-'}</Typography.Text>,
    },
    {
      title: t('warehouseProductRecords.storeName'),
      dataIndex: 'storeName',
      key: 'storeName',
      width: 180,
      render: (value: string | null) => value || '-',
    },
    {
      title: t('warehouseProductRecords.isActive'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 90,
      render: (value: boolean) => (
        <Tag color={value ? 'green' : 'default'}>
          {value ? t('warehouseProductRecords.active') : t('warehouseProductRecords.inactive')}
        </Tag>
      ),
    },
    {
      title: t('warehouseProductRecords.allocationQuantity'),
      dataIndex: 'allocationQuantity',
      key: 'allocationQuantity',
      width: 120,
      align: 'right',
      render: formatQuantity,
    },
    {
      title: t('warehouseProductRecords.allocationAmount'),
      dataIndex: 'allocationAmount',
      key: 'allocationAmount',
      width: 130,
      align: 'right',
      render: formatAustralianCurrency,
    },
    {
      title: t('warehouseProductRecords.orderCount'),
      dataIndex: 'orderCount',
      key: 'orderCount',
      width: 100,
      align: 'right',
      render: formatQuantity,
    },
    {
      title: t('warehouseProductRecords.firstAllocationDate'),
      dataIndex: 'firstAllocationDate',
      key: 'firstAllocationDate',
      width: 120,
      render: (value: string | null) => value || '-',
    },
    {
      title: t('warehouseProductRecords.lastAllocationDate'),
      dataIndex: 'lastAllocationDate',
      key: 'lastAllocationDate',
      width: 120,
      render: (value: string | null) => value || '-',
    },
  ]

  if (!enabled) {
    return null
  }

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      <Card size="small">
        <Space wrap>
          <Typography.Text>{t('warehouseProductRecords.recent30')} / </Typography.Text>
          {[7, 30, 90].map((days) => (
            <Button
              key={days}
              type={activeQuickDays === days ? 'primary' : 'default'}
              onClick={() => applyQuickRange(days)}
            >
              {t(`warehouseProductRecords.quick${days}`)}
            </Button>
          ))}
          <RangePicker
            allowClear={false}
            value={draftRange}
            disabledDate={(date) => date.isAfter(dayjs(brisbaneToday), 'day')}
            onChange={(value) => {
              if (!value?.[0] || !value[1]) return
              setDraftRange([value[0].startOf('day'), value[1].endOf('day')])
              setActiveQuickDays(null)
            }}
          />
          <Button type="primary" onClick={() => commitRange(draftRange, activeQuickDays)}>
            {t('common.query')}
          </Button>
          <Button onClick={resetRange}>{t('common.reset')}</Button>
        </Space>
        <Space wrap style={{ marginTop: 12 }}>
          <Typography.Text>{t('warehouseProductRecords.storeKeyword')}:</Typography.Text>
          <Input
            allowClear
            value={keyword}
            prefix={<SearchOutlined />}
            placeholder={t('warehouseProductRecords.storeKeywordPlaceholder')}
            style={{ width: 240 }}
            onChange={(event) => setKeyword(event.target.value)}
          />
        </Space>
      </Card>

      {error ? (
        <Alert
          type="error"
          showIcon
          message={t('warehouseProductRecords.loadFailed')}
          description={error instanceof Error ? error.message : undefined}
        />
      ) : null}

      <Card size="small" title={t('warehouseProductRecords.fullTotal')}>
        <Space size={32} wrap>
          <Statistic title={t('warehouseProductRecords.allocationQuantity')} value={report?.summary.allocationQuantity ?? 0} />
          <Statistic title={t('warehouseProductRecords.allocationAmount')} value={report?.summary.allocationAmount ?? 0} precision={2} prefix="$" />
          <Statistic title={t('warehouseProductRecords.orderCount')} value={report?.summary.orderCount ?? 0} />
        </Space>
      </Card>

      <Card size="small" title={t('warehouseProductRecords.filteredTotal')}>
        <Space size={32} wrap style={{ marginBottom: 12 }}>
          <Statistic title={t('warehouseProductRecords.allocationQuantity')} value={filteredTotals.allocationQuantity} />
          <Statistic title={t('warehouseProductRecords.allocationAmount')} value={filteredTotals.allocationAmount} precision={2} prefix="$" />
        </Space>

        {filteredBranches.length === 0 && !loading ? (
          <Empty description={t('warehouseProductRecords.noData')} />
        ) : (
          <MeasuredTable metricId="warehouse.product-records.allocations-panel.table-1"
            rowKey={(record) => record.storeCode || '__NO_STORE_CODE__'}
            size="small"
            loading={loading}
            columns={columns}
            dataSource={filteredBranches}
            scroll={{ x: 900 }}
            pagination={false}
            locale={{ emptyText: t('warehouseProductRecords.noData') }}
          />
        )}
      </Card>
    </Space>
  )
}
