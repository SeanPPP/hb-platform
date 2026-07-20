import {
  EyeOutlined,
  ReloadOutlined,
  ShopOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Drawer,
  Empty,
  Row,
  Select,
  Skeleton,
  Space,
  Statistic,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  getLocalPurchaseDashboard,
  getLocalPurchaseSupplierDetails,
} from '../../../services/localPurchaseDashboardService'
import type {
  LocalPurchaseDashboardResponse,
  LocalPurchaseStoreSummary,
  LocalPurchaseSupplierDetailResponse,
  LocalPurchaseSupplierSummary,
} from '../../../types/localPurchaseDashboard'
import {
  buildRollingMonths,
  buildPurchaseMonthRows,
  createLatestRequestGuard,
  filterPurchaseStores,
  formatPurchaseAmount,
  getPurchaseMatrixScroll,
  getPurchaseMonthColumnLayout,
  getPurchaseStoreMonthAmount,
  getSupplierDetailScroll,
  getSupplierDisplayName,
  resolvePurchaseReportViewState,
  sortPurchaseMonthsDescending,
  sortPurchaseSuppliers,
  type PurchaseMonthRow,
} from './logic'
import styles from './index.module.css'

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback
}

function isAbortError(error: unknown) {
  return error instanceof Error && error.name === 'AbortError'
}

export default function PurchaseAmountDashboardPage() {
  const { t } = useTranslation()
  const { active } = useKeepAliveContext()
  const [endMonth, setEndMonth] = useState<Dayjs>(() => dayjs().startOf('month'))
  const [selectedStoreCodes, setSelectedStoreCodes] = useState<string[]>([])
  const [refreshVersion, setRefreshVersion] = useState(0)
  const [report, setReport] = useState<LocalPurchaseDashboardResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [selectedStore, setSelectedStore] = useState<LocalPurchaseStoreSummary | null>(null)
  const [supplierReport, setSupplierReport] = useState<LocalPurchaseSupplierDetailResponse | null>(null)
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [supplierError, setSupplierError] = useState<string>()
  const dashboardGuardRef = useRef(createLatestRequestGuard())
  const supplierGuardRef = useRef(createLatestRequestGuard())
  const dashboardAbortRef = useRef<AbortController>()
  const supplierAbortRef = useRef<AbortController>()
  const endMonthKey = endMonth.format('YYYY-MM')

  useEffect(() => {
    if (!active) {
      dashboardAbortRef.current?.abort()
      supplierAbortRef.current?.abort()
      dashboardGuardRef.current.invalidate()
      supplierGuardRef.current.invalidate()
      setLoading(false)
      setSupplierLoading(false)
      return
    }

    dashboardAbortRef.current?.abort()
    supplierAbortRef.current?.abort()
    supplierGuardRef.current.invalidate()
    setDrawerOpen(false)
    setSupplierReport(null)
    setSupplierLoading(false)
    const abortController = new AbortController()
    dashboardAbortRef.current = abortController
    const requestId = dashboardGuardRef.current.begin()
    // 月份口径变化后先清空旧结果，失败时也不能在新月份控件下展示旧金额。
    setReport(null)
    setLoading(true)
    setError(undefined)

    getLocalPurchaseDashboard(endMonthKey, abortController.signal)
      .then((data) => {
        if (dashboardGuardRef.current.isLatest(requestId)) setReport(data)
      })
      .catch((nextError) => {
        if (!dashboardGuardRef.current.isLatest(requestId) || isAbortError(nextError)) return
        setError(getErrorMessage(nextError, t('purchaseAmountDashboard.loadFailed')))
      })
      .finally(() => {
        // 旧月份请求结束时不能关闭新月份请求的 loading。
        if (dashboardGuardRef.current.isLatest(requestId)) setLoading(false)
      })

    return () => {
      abortController.abort()
      dashboardGuardRef.current.invalidate()
    }
  }, [active, endMonthKey, refreshVersion, t])

  const months = useMemo(
    () => report?.months?.length ? report.months : buildRollingMonths(endMonthKey),
    [endMonthKey, report?.months],
  )
  const monthRows = useMemo(() => buildPurchaseMonthRows(months), [months])
  const visibleStores = useMemo(
    () => filterPurchaseStores(report?.stores ?? [], selectedStoreCodes),
    [report?.stores, selectedStoreCodes],
  )
  const storeOptions = useMemo(
    () => (report?.stores ?? []).map((store) => ({
      label: `${store.storeName} (${store.storeCode})`,
      value: store.storeCode,
    })),
    [report?.stores],
  )

  const closeDrawer = () => {
    supplierAbortRef.current?.abort()
    supplierGuardRef.current.invalidate()
    setSupplierLoading(false)
    setDrawerOpen(false)
  }

  const openStore = useCallback(async (store: LocalPurchaseStoreSummary) => {
    supplierAbortRef.current?.abort()
    const abortController = new AbortController()
    supplierAbortRef.current = abortController
    const requestId = supplierGuardRef.current.begin()
    setSelectedStore(store)
    setDrawerOpen(true)
    setSupplierReport(null)
    setSupplierError(undefined)
    setSupplierLoading(true)

    try {
      const data = await getLocalPurchaseSupplierDetails(store.storeCode, endMonthKey, abortController.signal)
      if (supplierGuardRef.current.isLatest(requestId)) setSupplierReport(data)
    } catch (nextError) {
      if (!supplierGuardRef.current.isLatest(requestId) || isAbortError(nextError)) return
      setSupplierError(getErrorMessage(nextError, t('purchaseAmountDashboard.detailLoadFailed')))
    } finally {
      // 用户连续切换分店时，旧抽屉请求不得改写新分店状态。
      if (supplierGuardRef.current.isLatest(requestId)) setSupplierLoading(false)
    }
  }, [endMonthKey, t])

  const columns = useMemo<ColumnsType<PurchaseMonthRow>>(() => [
    {
      title: t('purchaseAmountDashboard.month'),
      dataIndex: 'month',
      ...getPurchaseMonthColumnLayout(),
      render: (month: string) => <Typography.Text strong>{month}</Typography.Text>,
    },
    ...visibleStores.map((store) => ({
      title: (
        <div className={styles.storeHeader}>
          <div className={styles.storeCell}>
            <Typography.Text strong className={styles.storeName}>{store.storeName}</Typography.Text>
            <Typography.Text type="secondary" className={styles.storeCode}>{store.storeCode}</Typography.Text>
          </div>
          <Tooltip title={t('purchaseAmountDashboard.viewDetail')}>
            <Button
              type="text"
              size="small"
              icon={<EyeOutlined />}
              aria-label={t('purchaseAmountDashboard.openStoreDetail', { store: store.storeName })}
              onClick={() => void openStore(store)}
            />
          </Tooltip>
        </div>
      ),
      key: store.storeCode,
      width: 184,
      align: 'right' as const,
      render: (_: unknown, row: PurchaseMonthRow) => {
        const amount = getPurchaseStoreMonthAmount(store, row.month)
        return (
          <div className={styles.amountCell}>
            <div className={styles.amountLine}>
              <Typography.Text type="secondary">{t('purchaseAmountDashboard.totalShort')}</Typography.Text>
              <Typography.Text strong>{formatPurchaseAmount(amount.totalAmount)}</Typography.Text>
            </div>
            <div className={styles.amountLine}>
              <Typography.Text type="secondary">{t('purchaseAmountDashboard.warehouseShort')}</Typography.Text>
              <Typography.Text>{formatPurchaseAmount(amount.warehouseAmount)}</Typography.Text>
            </div>
            <div className={styles.amountLine}>
              <Typography.Text type="secondary">{t('purchaseAmountDashboard.localSupplierShort')}</Typography.Text>
              <Typography.Text>{formatPurchaseAmount(amount.localSupplierAmount)}</Typography.Text>
            </div>
            <div className={`${styles.amountLine} ${styles.salesLine}`}>
              <Typography.Text className={styles.salesLabel}>{t('purchaseAmountDashboard.salesShort')}</Typography.Text>
              <Typography.Text className={styles.salesAmount}>{formatPurchaseAmount(amount.salesAmount)}</Typography.Text>
            </div>
          </div>
        )
      },
    })),
  ], [openStore, t, visibleStores])

  const drawerMonths = useMemo(
    () => sortPurchaseMonthsDescending(
      supplierReport?.months?.length ? supplierReport.months : months,
    ),
    [months, supplierReport?.months],
  )
  const supplierColumns = useMemo<ColumnsType<LocalPurchaseSupplierSummary>>(() => [
    {
      title: t('purchaseAmountDashboard.sourceSupplier'),
      key: 'supplier',
      fixed: 'left',
      width: 220,
      render: (_, supplier) => (
        <Space direction="vertical" size={2}>
          <Space size={6}>
            <Typography.Text strong>
              {getSupplierDisplayName(supplier, {
                warehouse: t('purchaseAmountDashboard.warehouse'),
                unassigned: t('purchaseAmountDashboard.unassignedSupplier'),
              })}
            </Typography.Text>
            {supplier.isWarehouse ? <Tag color="blue">{t('purchaseAmountDashboard.warehouse')}</Tag> : null}
          </Space>
          {!supplier.isWarehouse ? (
            <Typography.Text type="secondary">{supplier.supplierCode ?? supplier.sourceCode}</Typography.Text>
          ) : null}
        </Space>
      ),
    },
    ...drawerMonths.map((month) => ({
      title: month,
      key: month,
      width: 132,
      align: 'right' as const,
      render: (_: unknown, supplier: LocalPurchaseSupplierSummary) => (
        <Typography.Text className={styles.monthCell}>
          {formatPurchaseAmount(supplier.monthlyAmounts.find((item) => item.month === month)?.amount ?? 0)}
        </Typography.Text>
      ),
    })),
    {
      title: t('purchaseAmountDashboard.periodTotal'),
      dataIndex: 'totalAmount',
      key: 'totalAmount',
      fixed: 'right',
      width: 148,
      align: 'right',
      render: (value: number) => <Typography.Text strong>{formatPurchaseAmount(value)}</Typography.Text>,
    },
  ], [drawerMonths, t])

  const supplierRows = useMemo(
    () => sortPurchaseSuppliers(supplierReport?.suppliers ?? []),
    [supplierReport?.suppliers],
  )
  const dashboardViewState = resolvePurchaseReportViewState({
    loading,
    hasError: Boolean(error),
    hasReport: Boolean(report),
    hasRows: visibleStores.length > 0,
  })
  const supplierViewState = resolvePurchaseReportViewState({
    loading: supplierLoading,
    hasError: Boolean(supplierError),
    hasReport: Boolean(supplierReport),
    hasRows: supplierRows.length > 0,
  })

  return (
    <PageContainer
      title={t('purchaseAmountDashboard.title')}
      subtitle={t('purchaseAmountDashboard.subtitle')}
    >
      <Space direction="vertical" size={12} className={styles.content}>
        <Alert
          type="info"
          showIcon
          message={t('purchaseAmountDashboard.basisTitle')}
          description={t('purchaseAmountDashboard.basisDescription')}
        />

        {report && (dashboardViewState === 'ready' || dashboardViewState === 'empty') ? (
          <Row gutter={[12, 12]}>
          <Col xs={24} md={8}>
            <Card size="small" loading={loading && !report} className={styles.summaryCard}>
              <Statistic title={t('purchaseAmountDashboard.warehouseTotal')} value={report?.warehouseAmount ?? 0} precision={2} prefix="$" />
            </Card>
          </Col>
          <Col xs={24} md={8}>
            <Card size="small" loading={loading && !report} className={styles.summaryCard}>
              <Statistic title={t('purchaseAmountDashboard.localSupplierTotal')} value={report?.localSupplierAmount ?? 0} precision={2} prefix="$" />
            </Card>
          </Col>
          <Col xs={24} md={8}>
            <Card size="small" loading={loading && !report} className={styles.summaryCard}>
              <Statistic title={t('purchaseAmountDashboard.grandTotal')} value={report?.totalAmount ?? 0} precision={2} prefix="$" valueStyle={{ fontWeight: 600 }} />
            </Card>
          </Col>
          </Row>
        ) : null}

        <Card size="small">
          <Space direction="vertical" size={12} className={styles.content}>
            <div className={styles.toolbar}>
              <Space wrap>
                <Typography.Text>{t('purchaseAmountDashboard.endMonth')}</Typography.Text>
                <DatePicker
                  picker="month"
                  allowClear={false}
                  value={endMonth}
                  aria-label={t('purchaseAmountDashboard.endMonth')}
                  disabledDate={(current) => current.isAfter(dayjs(), 'month')}
                  onChange={(value) => value && setEndMonth(value.startOf('month'))}
                />
                <Button
                  icon={<ReloadOutlined />}
                  loading={loading}
                  onClick={() => setRefreshVersion((value) => value + 1)}
                >
                  {t('common.refresh')}
                </Button>
              </Space>
              <Select
                mode="multiple"
                allowClear
                showSearch
                optionFilterProp="label"
                maxTagCount="responsive"
                value={selectedStoreCodes}
                options={storeOptions}
                onChange={setSelectedStoreCodes}
                placeholder={t('purchaseAmountDashboard.allStores')}
                aria-label={t('purchaseAmountDashboard.storeFilter')}
                className={styles.storeFilter}
              />
            </div>

            {error ? (
              <Alert
                type="error"
                showIcon
                message={t('purchaseAmountDashboard.loadFailed')}
                description={error}
                action={<Button size="small" onClick={() => setRefreshVersion((value) => value + 1)}>{t('purchaseAmountDashboard.retry')}</Button>}
              />
            ) : null}

            {dashboardViewState === 'error' ? null : dashboardViewState === 'loading' ? (
              <Skeleton active paragraph={{ rows: 10 }} />
            ) : dashboardViewState === 'empty' ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={t('purchaseAmountDashboard.noStores')}
              />
            ) : dashboardViewState === 'ready' ? (
              <Table<PurchaseMonthRow>
                rowKey="month"
                size="small"
                loading={loading}
                columns={columns}
                dataSource={monthRows}
                pagination={false}
                className={styles.matrixTable}
                scroll={getPurchaseMatrixScroll(visibleStores.length)}
                locale={{
                  emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('purchaseAmountDashboard.noStores')} />,
                }}
              />
            ) : null}
          </Space>
        </Card>
      </Space>

      <Drawer
        title={t('purchaseAmountDashboard.drawerTitle', {
          store: selectedStore?.storeName ?? selectedStore?.storeCode ?? '',
        })}
        width="min(1100px, 92vw)"
        open={drawerOpen}
        onClose={closeDrawer}
      >
        {supplierViewState === 'error' ? (
          <Alert
            type="error"
            showIcon
            message={t('purchaseAmountDashboard.detailLoadFailed')}
            description={supplierError}
            action={selectedStore ? (
              <Button size="small" onClick={() => void openStore(selectedStore)}>{t('purchaseAmountDashboard.retry')}</Button>
            ) : null}
            style={{ marginBottom: 12 }}
          />
        ) : null}

        {supplierReport && (supplierViewState === 'ready' || supplierViewState === 'empty') ? (
          <Space wrap className={styles.drawerSummary}>
            <Tag icon={<ShopOutlined />} color="blue">
              {t('purchaseAmountDashboard.warehouseShort')}: {formatPurchaseAmount(supplierReport.warehouseAmount)}
            </Tag>
            <Tag color="cyan">
              {t('purchaseAmountDashboard.localSupplierShort')}: {formatPurchaseAmount(supplierReport.localSupplierAmount)}
            </Tag>
            <Typography.Text strong>
              {t('purchaseAmountDashboard.periodTotal')}: {formatPurchaseAmount(supplierReport.totalAmount)}
            </Typography.Text>
          </Space>
        ) : null}

        {supplierViewState === 'loading' ? (
          <Skeleton active paragraph={{ rows: 8 }} />
        ) : supplierViewState === 'empty' ? (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={t('purchaseAmountDashboard.noSupplierData')}
          />
        ) : supplierViewState === 'ready' ? (
          <Table<LocalPurchaseSupplierSummary>
            rowKey="rowKey"
            size="small"
            columns={supplierColumns}
            dataSource={supplierRows}
            pagination={false}
            scroll={getSupplierDetailScroll(drawerMonths.length)}
          />
        ) : null}
      </Drawer>
    </PageContainer>
  )
}
