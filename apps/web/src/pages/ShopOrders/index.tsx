import {
  ClockCircleOutlined,
  ExportOutlined,
  HistoryOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShopOutlined,
} from '@ant-design/icons'
import {
  Button,
  DatePicker,
  Empty,
  Image,
  Input,
  Segmented,
  Skeleton,
  Spin,
  Switch,
  Tag,
  Typography,
} from 'antd'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'

import BarcodePreview from '../../components/BarcodePreview'
import { getStoreOrderDetail, getStoreOrderList } from '../../services/storeOrderService'
import { useShopStore } from '../../store/shop'
import {
  StoreOrderFlowStatus,
  StoreOrderStatusColorMap,
  StoreOrderStatusLabelMap,
  type StoreOrderDetail,
  type StoreOrderDetailLine,
  type StoreOrderListItem,
} from '../../types/storeOrder'

import styles from './ShopOrders.module.css'

const { Text, Title } = Typography
const { Search } = Input
const { RangePicker } = DatePicker

type StatusFilter = 'all' | 'active' | 'completed'
type DateRangeValue = [Dayjs, Dayjs]
type QuickRangeFilter = 'today' | 'week' | 'month' | 'custom'
type LineSortMode = 'shortage' | 'itemNumber'
type DetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'error'

const LINE_PAGE_SIZE = 24
const BARCODE_OPTIONS = { width: 1, height: 34, displayValue: false, margin: 0 }
const PRODUCT_IMAGE_FALLBACK = `data:image/svg+xml;charset=UTF-8,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="160" height="120" viewBox="0 0 160 120"><rect width="160" height="120" fill="#f5f7fa"/><path d="M52 82l23-27 15 18 12-14 17 23z" fill="#cbd5e1"/><circle cx="66" cy="40" r="8" fill="#94a3b8"/></svg>')}`

const statusQueryMap: Record<StatusFilter, StoreOrderFlowStatus[]> = {
  all: [
    StoreOrderFlowStatus.Submitted,
    StoreOrderFlowStatus.Picking,
    StoreOrderFlowStatus.Completed,
  ],
  active: [StoreOrderFlowStatus.Submitted, StoreOrderFlowStatus.Picking],
  completed: [StoreOrderFlowStatus.Completed],
}

function formatDateTime(value?: string, locale: string = 'zh-CN') {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString(locale, {
    hour12: false,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatShortDateTime(value?: string, locale: string = 'zh-CN') {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString(locale, {
    hour12: false,
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatMoney(value?: number) {
  return `$${(value ?? 0).toFixed(2)}`
}

function formatVolume(value?: number) {
  return `${(value ?? 0).toFixed(4)} cbm`
}

function formatAmount(order: StoreOrderListItem) {
  const value =
    order.importTotalAmount ?? order.totalAmount ?? order.totalOrderAmount ?? 0
  return `$${value.toFixed(2)}`
}

function getOrderStatusMeta(status: number, t: (key: string, fb: string, opts?: Record<string, unknown>) => string) {
  return {
    label: StoreOrderStatusLabelMap[status as StoreOrderFlowStatus] ?? t('common.statusN', `状态 ${status}`, { n: status }),
    color: StoreOrderStatusColorMap[status as StoreOrderFlowStatus] ?? 'default',
  }
}

function getShortageQuantity(line: StoreOrderDetailLine) {
  return Math.max((line.quantity ?? 0) - (line.allocQuantity ?? 0), 0)
}

function getLineStatus(line: StoreOrderDetailLine, t: (key: string, fb: string) => string) {
  const allocQuantity = line.allocQuantity ?? 0
  if (allocQuantity === 0) {
    return { label: t('shopOrderDetail.pendingShip', '待发货'), color: 'default' as const }
  }

  if (allocQuantity < (line.quantity ?? 0)) {
    return { label: t('shopOrderDetail.partialShipped', '部分发货'), color: 'warning' as const }
  }

  return { label: t('shopOrderDetail.shipped', '已发货'), color: 'success' as const }
}

/** 商品行是否命中关键字：货号、条码、商品名任一包含即可。 */
function matchesLineKeyword(line: StoreOrderDetailLine, keyword: string) {
  if (!keyword) {
    return true
  }

  const needle = keyword.trim().toLowerCase()
  if (!needle) {
    return true
  }

  return [line.itemNumber, line.productCode, line.barcode, line.productName].some(
    (field) => !!field && field.toLowerCase().includes(needle),
  )
}

function createDefaultDateRange(): DateRangeValue {
  return [dayjs().subtract(59, 'day').startOf('day'), dayjs().endOf('day')]
}

function createQuickDateRange(filter: Exclude<QuickRangeFilter, 'custom'>): DateRangeValue {
  if (filter === 'today') {
    return [dayjs().startOf('day'), dayjs().endOf('day')]
  }

  if (filter === 'week') {
    return [dayjs().startOf('week'), dayjs().endOf('day')]
  }

  return [dayjs().startOf('month'), dayjs().endOf('day')]
}

export default function ShopOrdersPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const selectedStore = useShopStore((state) => state.selectedStore)
  const dateLocale = i18n.resolvedLanguage?.startsWith('zh') ? 'zh-CN' : 'en-US'
  const panelRef = useRef<HTMLElement | null>(null)

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [keywordInput, setKeywordInput] = useState('')
  const [keyword, setKeyword] = useState('')
  const [productKeywordInput, setProductKeywordInput] = useState('')
  const [productKeyword, setProductKeyword] = useState('')
  const [orders, setOrders] = useState<StoreOrderListItem[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize] = useState(12)
  const [dateRange, setDateRange] = useState<DateRangeValue>(createDefaultDateRange)
  const [quickRange, setQuickRange] = useState<QuickRangeFilter>('custom')

  const [selectedOrderGuid, setSelectedOrderGuid] = useState<string | null>(null)
  const [detail, setDetail] = useState<StoreOrderDetail | null>(null)
  const [detailStatus, setDetailStatus] = useState<DetailLoadStatus>('idle')
  const [detailReloadVersion, setDetailReloadVersion] = useState(0)
  const [lineKeyword, setLineKeyword] = useState('')
  const [sortMode, setSortMode] = useState<LineSortMode>('shortage')
  const [showShortageOnly, setShowShortageOnly] = useState(false)
  const [showBarcode, setShowBarcode] = useState(false)
  const [linePage, setLinePage] = useState(1)

  useEffect(() => {
    let cancelled = false

    const fetchOrders = async () => {
      setLoading(true)
      setLoadError(false)

      try {
        const result = await getStoreOrderList({
          pageNumber: currentPage,
          pageSize,
          keyword: keyword || undefined,
          productKeyword: productKeyword || undefined,
          storeCode: selectedStore?.storeCode || undefined,
          startDate: dateRange[0].format('YYYY-MM-DD'),
          endDate: dateRange[1].format('YYYY-MM-DD'),
          statusList: statusQueryMap[statusFilter],
          sortBy: 'OrderDate',
          sortDescending: true,
        })

        if (cancelled) {
          return
        }

        setOrders(result.items)
        setTotal(result.total)
        // 列表刷新后保留仍在结果中的选中订单，否则回落到第一条。
        setSelectedOrderGuid((current) =>
          current && result.items.some((item) => item.orderGUID === current)
            ? current
            : (result.items[0]?.orderGUID ?? null),
        )
      } catch {
        if (!cancelled) {
          setOrders([])
          setTotal(0)
          setSelectedOrderGuid(null)
          setLoadError(true)
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchOrders()

    return () => {
      cancelled = true
    }
  }, [
    currentPage,
    dateRange,
    keyword,
    pageSize,
    productKeyword,
    reloadVersion,
    selectedStore?.storeCode,
    statusFilter,
  ])

  // 选中订单后按需拉取明细；请求带 AbortController，快速切换订单时旧响应不会覆盖新数据。
  useEffect(() => {
    if (!selectedOrderGuid) {
      setDetail(null)
      setDetailStatus('idle')
      return
    }

    const controller = new AbortController()
    let cancelled = false

    const fetchDetail = async () => {
      setDetailStatus('loading')

      try {
        const result = await getStoreOrderDetail(selectedOrderGuid, undefined, controller.signal)
        if (cancelled) {
          return
        }

        setDetail(result)
        setDetailStatus('loaded')
      } catch {
        if (!cancelled) {
          setDetail(null)
          setDetailStatus('error')
        }
      }
    }

    void fetchDetail()

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [detailReloadVersion, selectedOrderGuid])

  // 顶部商品查询提交后带入面板内过滤框，用户仍可在面板内单独修改。
  useEffect(() => {
    setLineKeyword(productKeyword)
  }, [productKeyword, selectedOrderGuid])

  useEffect(() => {
    setLinePage(1)
  }, [lineKeyword, selectedOrderGuid, showShortageOnly, sortMode])

  const stats = useMemo(() => {
    const activeCount = orders.filter((item) =>
      [StoreOrderFlowStatus.Submitted, StoreOrderFlowStatus.Picking].includes(item.flowStatus),
    ).length
    const completedCount = orders.filter(
      (item) => item.flowStatus === StoreOrderFlowStatus.Completed,
    ).length
    const visibleAmount = orders.reduce((sum, item) => {
      const value = item.importTotalAmount ?? item.totalAmount ?? item.totalOrderAmount ?? 0
      return sum + value
    }, 0)

    return {
      totalOrders: total,
      activeCount,
      completedCount,
      visibleAmount,
    }
  }, [orders, total])

  const indexPageCount = Math.max(1, Math.ceil(total / pageSize))
  const selectedOrder = orders.find((item) => item.orderGUID === selectedOrderGuid) ?? null

  const detailStats = useMemo(() => {
    const items = detail?.items ?? []
    const shippedQuantity = items.reduce((sum, item) => sum + Number(item.allocQuantity ?? 0), 0)
    const shortageQuantity = items.reduce((sum, item) => sum + getShortageQuantity(item), 0)

    return {
      shippedQuantity,
      shortageQuantity,
      shortageLineCount: items.filter((item) => getShortageQuantity(item) > 0).length,
    }
  }, [detail])

  const visibleLines = useMemo(() => {
    const items = detail?.items ?? []
    const filtered = items.filter(
      (item) =>
        matchesLineKeyword(item, lineKeyword) &&
        (!showShortageOnly || getShortageQuantity(item) > 0),
    )

    return filtered.slice().sort((left, right) => {
      if (sortMode === 'itemNumber') {
        return (left.itemNumber || left.productCode || '').localeCompare(
          right.itemNumber || right.productCode || '',
          undefined,
          { sensitivity: 'base' },
        )
      }

      const shortageDiff = getShortageQuantity(right) - getShortageQuantity(left)
      if (shortageDiff !== 0) {
        return shortageDiff
      }

      return (left.itemNumber || left.productCode || '').localeCompare(
        right.itemNumber || right.productCode || '',
        undefined,
        { sensitivity: 'base' },
      )
    })
  }, [detail, lineKeyword, showShortageOnly, sortMode])

  const linePageCount = Math.max(1, Math.ceil(visibleLines.length / LINE_PAGE_SIZE))
  const pagedLines = visibleLines.slice((linePage - 1) * LINE_PAGE_SIZE, linePage * LINE_PAGE_SIZE)

  const changeLinePage = useCallback((page: number) => {
    setLinePage(page)
    // 明细翻页后回到面板顶部，避免停在长页底部。
    panelRef.current?.scrollIntoView({ block: 'start', behavior: 'auto' })
  }, [])

  const resetFilters = () => {
    setCurrentPage(1)
    setKeywordInput('')
    setKeyword('')
    setProductKeywordInput('')
    setProductKeyword('')
    setDateRange(createDefaultDateRange())
    setQuickRange('custom')
    setReloadVersion((current) => current + 1)
  }

  return (
    <div className="shop-orders-page">
      <section className={styles.section}>
        <div className={styles.header}>
          <div>
            <div className={styles.eyebrow}>
              <HistoryOutlined /> {t('shopOrders.storeHistory')}
            </div>
            <Title level={2} className={styles.title}>
              {t('shopOrders.orderList')}
            </Title>
            <Text className={styles.subtitle}>{t('shopOrders.workbenchDescription')}</Text>
          </div>

          <div className={styles.headerAside}>
            <div className={styles.summary}>
              <span>
                <strong>{loadError ? '--' : stats.totalOrders}</strong>
                {t('shopOrders.orderCount')}
              </span>
              <span>
                <strong>{loadError ? '--' : stats.activeCount}</strong>
                {t('shopOrders.inProgress')}
              </span>
              <span>
                <strong>{loadError ? '--' : stats.completedCount}</strong>
                {t('shopOrders.completed')}
              </span>
              <span>
                <strong className={styles.amount}>
                  {loadError ? '--' : `$${stats.visibleAmount.toFixed(2)}`}
                </strong>
                {t('shopOrders.currentPageAmount')}
              </span>
            </div>
            <div className={styles.storeBadge}>
              <ShopOutlined />
              <span>{selectedStore?.storeName || t('shopOrders.currentAccessibleStores')}</span>
            </div>
          </div>
        </div>

        <div className={styles.toolbar}>
          <div className={styles.toolbarFilters}>
            <span>{t('shopOrders.filterLabel')}</span>
            <Segmented<StatusFilter>
              value={statusFilter}
              onChange={(value) => {
                setCurrentPage(1)
                setStatusFilter(value)
              }}
              options={[
                { label: t('common.all'), value: 'all' },
                { label: t('shopOrders.inProgress'), value: 'active' },
                { label: t('shopOrders.completed'), value: 'completed' },
              ]}
            />
            <RangePicker
              value={dateRange}
              allowClear={false}
              className={styles.dateRange}
              presets={[
                { label: t('shopOrders.last7Days'), value: [dayjs().subtract(6, 'day').startOf('day'), dayjs().endOf('day')] },
                { label: t('shopOrders.last30Days'), value: [dayjs().subtract(29, 'day').startOf('day'), dayjs().endOf('day')] },
                { label: t('shopOrders.last60Days'), value: createDefaultDateRange() },
              ]}
              onChange={(value) => {
                if (!value || !value[0] || !value[1]) {
                  setDateRange(createDefaultDateRange())
                  setQuickRange('custom')
                  setCurrentPage(1)
                  return
                }

                setDateRange([value[0].startOf('day'), value[1].endOf('day')])
                setQuickRange('custom')
                setCurrentPage(1)
              }}
            />
            <Segmented<QuickRangeFilter>
              value={quickRange}
              onChange={(value) => {
                setQuickRange(value)
                setCurrentPage(1)
                if (value === 'custom') {
                  setDateRange(createDefaultDateRange())
                  return
                }

                setDateRange(createQuickDateRange(value))
              }}
              options={[
                { label: t('shopOrders.today'), value: 'today' },
                { label: t('shopOrders.thisWeek'), value: 'week' },
                { label: t('shopOrders.thisMonth'), value: 'month' },
                { label: t('shopOrders.last60Days'), value: 'custom' },
              ]}
            />
          </div>

          <div className={styles.toolbarActions}>
            <Input
              value={keywordInput}
              allowClear
              placeholder={t('shopOrders.searchByOrderNo')}
              className={styles.orderSearch}
              onChange={(event) => {
                const next = event.target.value
                setKeywordInput(next)
                // 点 allowClear 的叉只会触发 onChange，这里同步清空查询，
                // 否则输入框已空、列表却停在旧的过滤结果。
                if (!next) {
                  setCurrentPage(1)
                  setKeyword('')
                }
              }}
              onPressEnter={() => {
                setCurrentPage(1)
                setKeyword(keywordInput.trim())
              }}
              onBlur={() => {
                setCurrentPage(1)
                setKeyword(keywordInput.trim())
              }}
            />
            {/* 商品查询走服务端，按货号、条码或商品名找出含该商品的订单。 */}
            <Search
              value={productKeywordInput}
              allowClear
              addonBefore={t('shopOrders.productSearchPrefix')}
              placeholder={t('shopOrders.searchByProduct')}
              enterButton={<SearchOutlined />}
              className={`${styles.productSearch} ${productKeyword ? styles.productSearchActive : ''}`}
              onChange={(event) => {
                const next = event.target.value
                setProductKeywordInput(next)
                // 同上：清空输入框后立刻回到未过滤的订单列表。
                if (!next) {
                  setCurrentPage(1)
                  setProductKeyword('')
                }
              }}
              onSearch={(value) => {
                setCurrentPage(1)
                setProductKeyword(value.trim())
              }}
            />
            <Button icon={<ReloadOutlined />} onClick={resetFilters}>
              {t('common.reset')}
            </Button>
          </div>
        </div>

        {loading ? (
          <div className={styles.loading}>
            <Spin size="large" />
          </div>
        ) : loadError ? (
          <div className="shop-orders-empty">
            <Empty description={t('shopOrders.loadFailed')}>
              <Button
                type="primary"
                icon={<ReloadOutlined />}
                onClick={() => setReloadVersion((current) => current + 1)}
              >
                {t('common.retry')}
              </Button>
            </Empty>
          </div>
        ) : orders.length ? (
          <div className={styles.workspace}>
            <nav className={styles.index} aria-label={t('shopOrders.orderIndex')}>
              <div className={styles.indexLabel}>
                <span>{t('shopOrders.orderIndex')}</span>
                <span className={productKeyword ? styles.indexLabelFiltered : undefined}>
                  {productKeyword
                    ? `${t('shopOrders.indexFiltered', { keyword: productKeyword })} · ${total}`
                    : `${orders.length} / ${total}`}
                </span>
              </div>

              {orders.map((order) => {
                const statusMeta = getOrderStatusMeta(order.flowStatus, t)
                const active = order.orderGUID === selectedOrderGuid

                return (
                  <button
                    type="button"
                    key={order.orderGUID}
                    className={`${styles.indexItem} ${active ? styles.indexItemActive : ''}`}
                    aria-current={active ? 'true' : undefined}
                    onClick={() => setSelectedOrderGuid(order.orderGUID)}
                  >
                    <span className={styles.indexMain}>
                      <span className={styles.indexTop}>
                        <strong>{order.orderNo || order.orderGUID.slice(0, 8)}</strong>
                        <Tag color={statusMeta.color}>{statusMeta.label}</Tag>
                      </span>
                      <span className={styles.indexStore}>
                        {order.storeName || order.storeCode || t('shopOrders.unknownStore')}
                      </span>
                      <span className={styles.indexMeta}>
                        {formatShortDateTime(order.orderDate, dateLocale)} · {order.totalQuantity ?? 0} ·{' '}
                        {formatAmount(order)}
                      </span>
                    </span>
                  </button>
                )
              })}

              <div className={styles.indexPager}>
                <Button
                  size="small"
                  disabled={currentPage <= 1}
                  onClick={() => setCurrentPage(currentPage - 1)}
                >
                  {t('shopOrders.previousPage')}
                </Button>
                <span aria-live="polite">
                  {t('shopOrders.pageOf', { page: currentPage, pages: indexPageCount })}
                </span>
                <Button
                  size="small"
                  disabled={currentPage >= indexPageCount}
                  onClick={() => setCurrentPage(currentPage + 1)}
                >
                  {t('shopOrders.nextPage')}
                </Button>
              </div>
            </nav>

            {selectedOrder ? (
              <section
                ref={panelRef}
                className={styles.panel}
                aria-label={selectedOrder.orderNo || selectedOrder.orderGUID}
                aria-busy={detailStatus === 'loading'}
              >
                <div className={styles.panelHead}>
                  <div className={styles.panelTitle}>
                    <Title level={3}>
                      {selectedOrder.orderNo || selectedOrder.orderGUID.slice(0, 8)}
                    </Title>
                    <Tag color={getOrderStatusMeta(selectedOrder.flowStatus, t).color}>
                      {getOrderStatusMeta(selectedOrder.flowStatus, t).label}
                    </Tag>
                    <span className={styles.panelFact}>
                      <ShopOutlined />
                      {selectedOrder.storeName || selectedOrder.storeCode || t('shopOrders.unknownStore')}
                    </span>
                    <span className={styles.panelFact}>
                      <ClockCircleOutlined />
                      {formatDateTime(selectedOrder.orderDate, dateLocale)}
                    </span>
                  </div>
                  <Button
                    icon={<ExportOutlined />}
                    onClick={() => navigate(`/shop/orders/${selectedOrder.orderGUID}`)}
                  >
                    {t('shopOrders.openFullDetail')}
                  </Button>
                </div>

                <div className={styles.stats}>
                  <div className={styles.stat}>
                    <span>{t('shopOrders.orderQuantity')}</span>
                    <strong>{selectedOrder.totalQuantity ?? 0}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>{t('shopOrders.shipQuantity')}</span>
                    <strong>{selectedOrder.totalAllocQuantity ?? 0}</strong>
                  </div>
                  <div
                    className={`${styles.stat} ${detailStats.shortageQuantity > 0 ? styles.statDanger : ''}`}
                  >
                    <span>{t('shopOrderDetail.shortageQuantity')}</span>
                    <strong>{detailStatus === 'loaded' ? detailStats.shortageQuantity : '--'}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>{t('shopOrderDetail.orderVolume')}</span>
                    <strong>
                      {detailStatus === 'loaded' ? formatVolume(detail?.totalOrderVolume) : '--'}
                    </strong>
                  </div>
                  <div className={`${styles.stat} ${styles.statAccent}`}>
                    <span>{t('shopOrderDetail.purchaseAmount')}</span>
                    <strong>{formatAmount(selectedOrder)}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>{t('shopOrderDetail.retailAmount')}</span>
                    <strong>
                      {detailStatus === 'loaded' ? formatMoney(detail?.totalAmount) : '--'}
                    </strong>
                  </div>
                </div>

                {selectedOrder.remarks ? (
                  <div className={styles.remarks}>
                    <span className={styles.remarksLabel}>{t('common.remarks')}</span>
                    <p>{selectedOrder.remarks}</p>
                  </div>
                ) : null}

                <div className={styles.panelMeta}>
                  <div className={styles.panelMetaMain}>
                    {/* 面板内过滤纯前端，改关键字不会重新请求订单列表。 */}
                    <Input
                      value={lineKeyword}
                      allowClear
                      prefix={<SearchOutlined />}
                      placeholder={t('shopOrders.lineSearchPlaceholder')}
                      className={styles.lineSearch}
                      onChange={(event) => setLineKeyword(event.target.value)}
                    />
                    <Segmented<LineSortMode>
                      value={sortMode}
                      onChange={(value) => setSortMode(value)}
                      options={[
                        { label: t('shopOrderDetail.shortageFirst'), value: 'shortage' },
                        { label: t('shopOrderDetail.byItemNo'), value: 'itemNumber' },
                      ]}
                    />
                    <Button
                      type={showShortageOnly ? 'primary' : 'default'}
                      onClick={() => setShowShortageOnly((current) => !current)}
                    >
                      {showShortageOnly
                        ? t('shopOrderDetail.showAll')
                        : t('shopOrderDetail.onlyShortage')}
                    </Button>
                    {detailStatus === 'loaded' ? (
                      <span className={styles.lineCount}>
                        {lineKeyword || showShortageOnly
                          ? t('shopOrders.lineMatchCount', {
                              matched: visibleLines.length,
                              total: detail?.items?.length ?? 0,
                            })
                          : t('shopOrders.lineTotalCount', { total: detail?.items?.length ?? 0 })}
                        {' · '}
                        {t('shopOrders.shortageLineCount', { count: detailStats.shortageLineCount })}
                      </span>
                    ) : null}
                  </div>
                  <label className={styles.barcodeToggle}>
                    <Switch
                      checked={showBarcode}
                      onChange={setShowBarcode}
                      aria-label={t('shopOrders.barcode')}
                      size="small"
                    />
                    {t('shopOrders.barcode')}
                  </label>
                </div>

                {detailStatus === 'loading' || detailStatus === 'idle' ? (
                  <div className={styles.lineLoading}>
                    <Skeleton active paragraph={{ rows: 5 }} />
                  </div>
                ) : detailStatus === 'error' ? (
                  <div className={styles.panelEmpty}>
                    <Empty description={t('shopOrders.detailLoadFailed')}>
                      <Button
                        type="primary"
                        icon={<ReloadOutlined />}
                        onClick={() => setDetailReloadVersion((current) => current + 1)}
                      >
                        {t('common.retry')}
                      </Button>
                    </Empty>
                  </div>
                ) : visibleLines.length ? (
                  <>
                    <div className={styles.lineList}>
                      {pagedLines.map((line) => {
                        const shortageQuantity = getShortageQuantity(line)
                        const lineStatus = getLineStatus(line, t)

                        return (
                          <article
                            key={line.detailGUID}
                            className={`${styles.line} ${shortageQuantity > 0 ? styles.lineShortage : ''}`}
                          >
                            <div className={styles.lineMedia}>
                              <Image
                                src={line.productImage || PRODUCT_IMAGE_FALLBACK}
                                fallback={PRODUCT_IMAGE_FALLBACK}
                                alt={line.productName || t('shopOrderDetail.unnamedProduct')}
                                preview={false}
                                loading="lazy"
                              />
                            </div>

                            <div className={styles.lineMain}>
                              <div className={styles.lineName}>
                                {line.productName || t('shopOrderDetail.unnamedProduct')}
                              </div>
                              <div className={styles.lineTags}>
                                <Tag>{line.itemNumber || line.productCode}</Tag>
                                {line.locationCode ? <Tag>{line.locationCode}</Tag> : null}
                              </div>
                              {showBarcode ? (
                                <div className={styles.lineBarcode}>
                                  <BarcodePreview
                                    value={line.barcode}
                                    options={BARCODE_OPTIONS}
                                    showText
                                    showCopy={false}
                                    textNoWrap
                                    align="left"
                                  />
                                </div>
                              ) : null}
                            </div>

                            <div className={styles.lineMetric}>
                              <span>{t('shopOrderDetail.orderSlashShip')}</span>
                              <strong>
                                {line.quantity} / {line.allocQuantity ?? 0}
                              </strong>
                            </div>
                            <div
                              className={`${styles.lineMetric} ${shortageQuantity > 0 ? styles.lineMetricDanger : ''}`}
                            >
                              <span>{t('shopOrderDetail.shortage')}</span>
                              <strong>{shortageQuantity}</strong>
                            </div>
                            <div className={`${styles.lineMetric} ${styles.lineMetricOptional}`}>
                              <span>{t('shopOrders.purchasePrice')}</span>
                              <strong>{formatMoney(line.importPrice)}</strong>
                            </div>
                            <div className={styles.lineMetric}>
                              <span>{t('shopOrders.purchaseAmount')}</span>
                              <strong>
                                {formatMoney(line.allocatedImportAmount ?? line.importAmount)}
                              </strong>
                            </div>

                            <div className={styles.lineStatus}>
                              {shortageQuantity > 0 ? (
                                <Tag color="error">{t('shopOrderDetail.shortage')}</Tag>
                              ) : null}
                              <Tag color={lineStatus.color}>{lineStatus.label}</Tag>
                            </div>
                          </article>
                        )
                      })}
                    </div>

                    <div className={styles.pagination}>
                      <span>
                        {t('shopOrders.lineRangeInfo', {
                          from: (linePage - 1) * LINE_PAGE_SIZE + 1,
                          to: Math.min(linePage * LINE_PAGE_SIZE, visibleLines.length),
                          total: visibleLines.length,
                        })}
                      </span>
                      <div>
                        <Button
                          disabled={linePage <= 1}
                          onClick={() => changeLinePage(linePage - 1)}
                        >
                          {t('shopOrders.previousPage')}
                        </Button>
                        <span aria-live="polite">
                          {t('shopOrders.pageOf', { page: linePage, pages: linePageCount })}
                        </span>
                        <Button
                          disabled={linePage >= linePageCount}
                          onClick={() => changeLinePage(linePage + 1)}
                        >
                          {t('shopOrders.nextPage')}
                        </Button>
                      </div>
                    </div>
                  </>
                ) : (
                  <div className={styles.panelEmpty}>
                    <Empty
                      image={Empty.PRESENTED_IMAGE_SIMPLE}
                      description={
                        lineKeyword || showShortageOnly
                          ? t('shopOrders.noLineMatch')
                          : t('shopOrderDetail.noProductDetail')
                      }
                    />
                  </div>
                )}
              </section>
            ) : (
              <div className={styles.panel}>
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={t('shopOrders.selectOrderTip')}
                />
              </div>
            )}
          </div>
        ) : (
          <div className="shop-orders-empty">
            <Empty
              description={
                keyword || productKeyword
                  ? t('shopOrders.noMatchOrders')
                  : t('shopOrders.noHistoryOrders')
              }
            />
          </div>
        )}
      </section>
    </div>
  )
}
