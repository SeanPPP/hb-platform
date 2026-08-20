import {
  Alert,
  Button,
  Card,
  DatePicker,
  Empty,
  Modal,
  Space,
  Statistic,
  Table,
  Tooltip,
  Typography,
  message,
} from 'antd'
import { CalendarOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import DailySalesChart from '../../ExecutiveSalesIntelligence/ProductSalesAnalysis/DailySalesChart'
import {
  queryProductSalesBranchDaily,
  queryProductSalesBranches,
  queryProductSalesDaily,
  queryProductSalesSummary,
} from '../../../services/productSalesAnalysisService'
import type {
  ProductSalesAnalysisFilter,
  ProductSalesAnalysisPaged,
  ProductSalesBranch,
  ProductSalesDaily,
  ProductSalesSummaryRow,
} from '../../../types/productSalesAnalysis'
import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'
import {
  buildBrisbaneDateRange,
  buildSalesScope,
  buildSalesSelection,
  formatAveragePrice,
  formatAustralianCurrency,
  formatQuantity,
  getDateRangeError,
  isAbortError,
} from './logic'

const { RangePicker } = DatePicker

interface SalesPanelProps {
  productCode: string
  enabled: boolean
}

function makeFilter(startDate: string, endDate: string): ProductSalesAnalysisFilter {
  return {
    startDate,
    endDate,
    keyword: undefined,
    australianSupplierCodes: [],
    chinaSupplierCodes: [],
  }
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback
}

export default function SalesPanel({ productCode, enabled }: SalesPanelProps) {
  const { t, i18n } = useTranslation()
  const initialRange = useMemo(() => {
    const range = buildBrisbaneDateRange(30)
    return [dayjs(range.startDate), dayjs(range.endDate)] as [Dayjs, Dayjs]
  }, [])

  const [brisbaneToday, setBrisbaneToday] = useState(initialRange[1].format('YYYY-MM-DD'))
  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(initialRange)
  const [activeQuickDays, setActiveQuickDays] = useState<number | null>(30)
  const [committedRange, setCommittedRange] = useState({
    startDate: initialRange[0].format('YYYY-MM-DD'),
    endDate: initialRange[1].format('YYYY-MM-DD'),
  })

  const [summary, setSummary] = useState<ProductSalesAnalysisPaged<ProductSalesSummaryRow> | null>(null)
  const [productDaily, setProductDaily] = useState<ProductSalesDaily[]>([])
  const [branches, setBranches] = useState<ProductSalesBranch[]>([])
  const [branchDaily, setBranchDaily] = useState<ProductSalesDaily[]>([])
  const [selectedBranchCode, setSelectedBranchCode] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [branchLoading, setBranchLoading] = useState(false)
  const [error, setError] = useState<string>()
  const [branchError, setBranchError] = useState<string>()

  const mainAbortRef = useRef<AbortController>()
  const mainGuardRef = useRef(createLatestRequestGuard())
  const branchAbortRef = useRef<AbortController>()
  const branchGuardRef = useRef(createLatestRequestGuard())
  const selectedBranchProductCodeRef = useRef<string | null>(null)
  const previousProductCodeRef = useRef(productCode)

  const selection = useMemo(() => buildSalesSelection(productCode), [productCode])
  const scope = useMemo(() => buildSalesScope(productCode), [productCode])
  const viewBranchDailyLabel = useMemo(() => {
    const activeLanguage = i18n.resolvedLanguage || i18n.language
    const separator = activeLanguage.startsWith('zh') ? '' : ' '
    return `${t('common.view')}${separator}${t('warehouseProductRecords.branchDaily')}`
  }, [i18n.language, i18n.resolvedLanguage, t])

  const clearBranchDailyRequest = useCallback(() => {
    branchAbortRef.current?.abort()
    branchGuardRef.current.invalidate()
    branchAbortRef.current = undefined
    setBranchDaily([])
    setBranchLoading(false)
    setBranchError(undefined)
  }, [])

  const closeBranchDailyModal = useCallback(() => {
    clearBranchDailyRequest()
    selectedBranchProductCodeRef.current = null
    setSelectedBranchCode(null)
  }, [clearBranchDailyRequest])

  const openBranchDailyModal = useCallback((branchCode: string) => {
    clearBranchDailyRequest()
    selectedBranchProductCodeRef.current = productCode
    setSelectedBranchCode(branchCode)
  }, [clearBranchDailyRequest, productCode])

  const commitRange = (range: [Dayjs, Dayjs], quickDays: number | null) => {
    const startDate = range[0].format('YYYY-MM-DD')
    const endDate = range[1].format('YYYY-MM-DD')
    const rangeError = getDateRangeError(startDate, endDate)
    if (rangeError) {
      message.warning(t('warehouseProductRecords.dateRangeInvalid'))
      return
    }
    closeBranchDailyModal()
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
    const range = buildBrisbaneDateRange(30)
    setBrisbaneToday(range.endDate)
    commitRange([dayjs(range.startDate), dayjs(range.endDate)], 30)
  }

  useEffect(() => {
    if (!enabled) return
    const currentRange = buildBrisbaneDateRange(activeQuickDays ?? 1)
    if (currentRange.endDate === brisbaneToday) return

    setBrisbaneToday(currentRange.endDate)
    // 快捷范围跟随布里斯班自然日滚动；自定义范围继续保留。
    if (activeQuickDays != null) {
      const nextRange: [Dayjs, Dayjs] = [dayjs(currentRange.startDate), dayjs(currentRange.endDate)]
      setDraftRange(nextRange)
      setCommittedRange({
        startDate: currentRange.startDate,
        endDate: currentRange.endDate,
      })
      closeBranchDailyModal()
    }
  }, [activeQuickDays, brisbaneToday, closeBranchDailyModal, enabled])

  useEffect(() => {
    const productChanged = previousProductCodeRef.current !== productCode
    previousProductCodeRef.current = productCode
    if (!enabled || productChanged) {
      closeBranchDailyModal()
    }
  }, [closeBranchDailyModal, enabled, productCode])

  useEffect(() => {
    mainAbortRef.current?.abort()
    mainGuardRef.current.invalidate()
    if (!enabled) {
      setLoading(false)
      return
    }

    const controller = new AbortController()
    mainAbortRef.current = controller
    const requestId = mainGuardRef.current.begin()
    setSummary(null)
    setProductDaily([])
    setBranches([])
    setLoading(true)
    setError(undefined)

    const filter = makeFilter(committedRange.startDate, committedRange.endDate)

    Promise.all([
      queryProductSalesSummary(filter, selection, scope, { pageNumber: 1, pageSize: 1 }, controller.signal, { allowNonFreshData: true }),
      queryProductSalesDaily(filter, selection, scope, controller.signal, { allowNonFreshData: true }),
      queryProductSalesBranches(filter, selection, scope, controller.signal, { allowNonFreshData: true }),
    ])
      .then(([summaryEnvelope, dailyEnvelope, branchesEnvelope]) => {
        if (!mainGuardRef.current.isLatest(requestId)) return
        setSummary(summaryEnvelope.data)
        setProductDaily(dailyEnvelope.data)
        setBranches(branchesEnvelope.data)
      })
      .catch((nextError) => {
        if (!mainGuardRef.current.isLatest(requestId) || isAbortError(nextError)) return
        setSummary(null)
        setProductDaily([])
        setBranches([])
        setError(getErrorMessage(nextError, t('warehouseProductRecords.loadFailed')))
      })
      .finally(() => {
        if (mainGuardRef.current.isLatest(requestId)) setLoading(false)
      })

    return () => {
      controller.abort()
      mainGuardRef.current.invalidate()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, productCode, selection, scope, committedRange])

  useEffect(() => {
    clearBranchDailyRequest()
    if (
      !enabled
      || !selectedBranchCode
      || selectedBranchProductCodeRef.current !== productCode
    ) {
      setBranchDaily([])
      setBranchLoading(false)
      return
    }

    const controller = new AbortController()
    branchAbortRef.current = controller
    const requestId = branchGuardRef.current.begin()
    setBranchLoading(true)
    setBranchError(undefined)

    queryProductSalesBranchDaily(
      makeFilter(committedRange.startDate, committedRange.endDate),
      selection,
      scope,
      selectedBranchCode,
      controller.signal,
      { allowNonFreshData: true },
    )
      .then((envelope) => {
        if (!branchGuardRef.current.isLatest(requestId)) return
        setBranchDaily(envelope.data)
      })
      .catch((nextError) => {
        if (!branchGuardRef.current.isLatest(requestId) || isAbortError(nextError)) return
        setBranchError(getErrorMessage(nextError, t('warehouseProductRecords.loadFailed')))
      })
      .finally(() => {
        if (branchGuardRef.current.isLatest(requestId)) setBranchLoading(false)
      })

    return () => {
      controller.abort()
      branchGuardRef.current.invalidate()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, selectedBranchCode, productCode, selection, scope, committedRange, clearBranchDailyRequest])

  const kpi = summary?.items[0]?.metrics

  const dailyColumns: ColumnsType<ProductSalesDaily> = [
    {
      title: t('column.date'),
      dataIndex: 'date',
      key: 'date',
      width: 112,
      fixed: 'left',
      render: (value: string) => <Typography.Text>{value}</Typography.Text>,
    },
    {
      title: t('warehouseProductRecords.netQuantity'),
      dataIndex: 'quantity',
      key: 'quantity',
      width: 110,
      align: 'right',
      render: (value: number) => formatQuantity(value),
    },
    {
      title: t('warehouseProductRecords.netSalesAmount'),
      dataIndex: 'salesAmount',
      key: 'salesAmount',
      width: 130,
      align: 'right',
      render: (value: number) => formatAustralianCurrency(value),
    },
    {
      title: t('warehouseProductRecords.averagePrice'),
      dataIndex: 'averageUnitPrice',
      key: 'averageUnitPrice',
      width: 110,
      align: 'right',
      render: (value: number | null, record) => formatAveragePrice(record.quantity, value),
    },
  ]

  const branchColumns: ColumnsType<ProductSalesBranch> = [
    {
      title: t('warehouseProductRecords.storeName'),
      key: 'branch',
      width: 180,
      fixed: 'left',
      render: (_, branch) => (
        <Space direction="vertical" size={0}>
          <Typography.Text strong>{branch.branchName || branch.branchCode}</Typography.Text>
          <Typography.Text type="secondary">{branch.branchCode}</Typography.Text>
        </Space>
      ),
    },
    {
      title: t('warehouseProductRecords.netQuantity'),
      dataIndex: ['metrics', 'quantity'],
      key: 'quantity',
      width: 100,
      align: 'right',
      render: (value: number) => formatQuantity(value),
    },
    {
      title: t('warehouseProductRecords.netSalesAmount'),
      dataIndex: ['metrics', 'salesAmount'],
      key: 'salesAmount',
      width: 130,
      align: 'right',
      render: (value: number) => formatAustralianCurrency(value),
    },
    {
      title: t('warehouseProductRecords.averagePrice'),
      dataIndex: ['metrics', 'averageUnitPrice'],
      key: 'averageUnitPrice',
      width: 110,
      align: 'right',
      render: (value: number | null, record) => formatAveragePrice(record.metrics.quantity, value),
    },
    {
      title: '',
      key: 'drilldown',
      width: 56,
      fixed: 'right',
      render: (_, branch) => (
        <Tooltip title={viewBranchDailyLabel}>
          <Button
            type="text"
            size="small"
            icon={<CalendarOutlined />}
            aria-label={`${viewBranchDailyLabel}：${branch.branchName || branch.branchCode}（${branch.branchCode}）`}
            onClick={() => openBranchDailyModal(branch.branchCode)}
          />
        </Tooltip>
      ),
    },
  ]

  if (!enabled) {
    return null
  }

  const selectedBranch = branches.find((branch) => branch.branchCode === selectedBranchCode)
  const selectedBranchName = selectedBranch?.branchName || selectedBranchCode
  const branchDailyChartData = branchDaily.map((item) => ({
    date: item.date,
    quantity: item.quantity,
    averageUnitPrice: item.averageUnitPrice,
  }))
  const branchDailyChartAriaLabel = `${t('warehouseProductRecords.branchDaily')} · ${selectedBranchName || '-'}（${selectedBranchCode || '-'}） · ${t('warehouseProductRecords.netQuantity')} / ${t('warehouseProductRecords.averagePrice')}`

  return (
    <>
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
        </Card>

        {error ? <Alert type="error" showIcon message={t('warehouseProductRecords.loadFailed')} description={error} /> : null}

        <Card size="small" loading={loading} title={t('warehouseProductRecords.title')}>
          <Space size={32} wrap>
            <Statistic title={t('warehouseProductRecords.netQuantity')} value={kpi?.quantity ?? 0} />
            <Statistic title={t('warehouseProductRecords.netSalesAmount')} value={kpi?.salesAmount ?? 0} precision={2} prefix="$" />
            <Statistic
              title={t('warehouseProductRecords.averagePrice')}
              value={formatAveragePrice(kpi?.quantity ?? 0, kpi?.averageUnitPrice)}
            />
          </Space>
        </Card>

        <Card size="small" title={t('warehouseProductRecords.branches')}>
          {branches.length === 0 && !loading ? (
            <Empty description={t('warehouseProductRecords.noData')} />
          ) : (
            <Table
              rowKey="branchCode"
              size="small"
              loading={loading}
              columns={branchColumns}
              dataSource={branches}
              pagination={false}
              scroll={{ x: 640 }}
              locale={{ emptyText: t('warehouseProductRecords.noData') }}
            />
          )}
        </Card>

        <Card size="small" title={t('warehouseProductRecords.dailySummary')}>
          {productDaily.length === 0 && !loading ? (
            <Empty description={t('warehouseProductRecords.noData')} />
          ) : (
            <Table
              rowKey="date"
              size="small"
              loading={loading}
              columns={dailyColumns}
              dataSource={productDaily}
              pagination={false}
              scroll={{ x: 480 }}
              locale={{ emptyText: t('warehouseProductRecords.noData') }}
            />
          )}
        </Card>
      </Space>

      <Modal
        open={selectedBranchCode !== null}
        title={`${t('warehouseProductRecords.branchDaily')} · ${selectedBranchName || '-'}（${selectedBranchCode || '-'}）`}
        width={760}
        footer={null}
        keyboard
        maskClosable
        focusTriggerAfterClose
        destroyOnHidden
        onCancel={closeBranchDailyModal}
      >
        {branchError ? (
          <Alert
            type="error"
            showIcon
            message={t('warehouseProductRecords.loadFailed')}
            description={branchError}
          />
        ) : (
          <Space direction="vertical" size={8} style={{ width: '100%' }}>
            {!branchLoading && branchDaily.length > 0 ? (
              <>
                <Space size={16} wrap>
                  <Space size={6}>
                    <span
                      aria-hidden="true"
                      style={{
                        display: 'inline-block',
                        width: 10,
                        height: 10,
                        borderRadius: 2,
                        background: '#1677ff',
                      }}
                    />
                    <Typography.Text type="secondary">
                      {t('warehouseProductRecords.netQuantity')}
                    </Typography.Text>
                  </Space>
                  <Space size={6}>
                    <span
                      aria-hidden="true"
                      style={{ display: 'inline-block', width: 18, borderTop: '2px solid #003670' }}
                    />
                    <Typography.Text type="secondary">
                      {t('warehouseProductRecords.averagePrice')}
                    </Typography.Text>
                  </Space>
                </Space>
                <DailySalesChart
                  data={branchDailyChartData}
                  ariaLabel={branchDailyChartAriaLabel}
                  height={200}
                />
              </>
            ) : null}

            {branchDaily.length === 0 && !branchLoading ? (
              <Empty description={t('warehouseProductRecords.noData')} />
            ) : (
              <Table
                rowKey="date"
                size="small"
                loading={branchLoading}
                columns={dailyColumns}
                dataSource={branchDaily}
                pagination={false}
                scroll={{ x: 480, y: '32vh' }}
                locale={{ emptyText: t('warehouseProductRecords.noData') }}
              />
            )}
          </Space>
        )}
      </Modal>
    </>
  )
}
