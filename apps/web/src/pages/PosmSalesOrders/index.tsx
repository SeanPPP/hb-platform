import { ClockCircleOutlined, DownloadOutlined, FilePdfOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  DatePicker,
  Input,
  InputNumber,
  Modal,
  Pagination,
  Segmented,
  Select,
  TimePicker,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import ActiveFilterBar, { type ActiveFilterItem } from '../../components/listToolbar/ActiveFilterBar'
import MoreFiltersButton from '../../components/listToolbar/MoreFiltersButton'
import { MeasuredTable } from '../../components/MeasuredTable'
import PageContainer from '../../components/PageContainer'
import {
  fetchTaxInvoicePdf,
  getSalesOrderDetail,
  getSalesOrderList,
  getTaxInvoicePdfUrl,
} from '../../services/posmSalesOrderService'
import { getActiveStores } from '../../services/storeService'
import { useAuthStore } from '../../store/auth'
import type {
  PosmSalesOrder,
  PosmSalesOrderDetailResponse,
  PosmSalesOrderSortState,
  PosmSalesOrderStatusSummary,
} from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

import OrderDetailDrawer from './OrderDetailDrawer'
import OrderStatusTag, { PaymentMethodsText } from './OrderStatusTag'
import {
  DATE_PRESETS,
  DEFAULT_PAGE_SIZE,
  DEFAULT_SORT,
  DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS,
  EMPTY_MORE_FILTERS,
  MAX_RANGE_DAYS,
  SLOW_QUERY_NOTICE_MS,
  buildFilterChips,
  buildListQuery,
  countDays,
  countMoreFilters,
  createDefaultFilters,
  detectDatePreset,
  formatMoney,
  isLatestRequest,
  mapTableSort,
  normalizeFilterNumber,
  orderActualAmount,
  resolveDatePreset,
  shortOrderNo,
  stepOrderIndex,
  summarizeStatuses,
  tableSortOrder,
  validateFilters,
  type PosmSalesOrderDatePreset,
  type PosmSalesOrderFilters,
  type PosmSalesOrderMoreFilters,
} from './posmSalesOrdersLogic'
import StatusSummaryStrip from './StatusSummaryStrip'
import { formatPosmSalesOrderTime } from './time'
import './posmSalesOrders.css'

const LIST_BOTTOM_GAP = 16
const LIST_MIN_HEIGHT = 420

const PRESET_FALLBACK: Record<PosmSalesOrderDatePreset, string> = {
  today: '今天',
  yesterday: '昨天',
  last7: '近 7 天',
  thisMonth: '本月',
}

interface QueryState {
  filters: PosmSalesOrderFilters
  sort: PosmSalesOrderSortState
  page: number
  pageSize: number
}

function pickMoreFilters(filters: PosmSalesOrderFilters): PosmSalesOrderMoreFilters {
  return {
    deviceCode: filters.deviceCode,
    timeStart: filters.timeStart,
    timeEnd: filters.timeEnd,
    actualPayMin: filters.actualPayMin,
    actualPayMax: filters.actualPayMax,
    quantityMin: filters.quantityMin,
    quantityMax: filters.quantityMax,
    skuCountMin: filters.skuCountMin,
    skuCountMax: filters.skuCountMax,
  }
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

export default function PosmSalesOrdersPage() {
  const { t } = useTranslation()
  const access = useAuthStore((s) => s.access)
  const currentUser = useAuthStore((s) => s.currentUser)
  const managedStoreCodes = access.managedStoreCodes?.()
  // 授权分店列表转成字符串键作为副作用依赖：null 表示不限分店（管理员）。
  const managedStoreKey = managedStoreCodes === null || managedStoreCodes === undefined ? null : managedStoreCodes.join(',')
  const today = dayjs().format('YYYY-MM-DD')

  const [query, setQuery] = useState<QueryState>(() => ({
    filters: createDefaultFilters(today),
    sort: DEFAULT_SORT,
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
  }))
  const queryRef = useRef(query)
  queryRef.current = query

  const [keywordInput, setKeywordInput] = useState('')
  const [moreDraft, setMoreDraft] = useState<PosmSalesOrderMoreFilters>(EMPTY_MORE_FILTERS)
  const [moreOpen, setMoreOpen] = useState(false)

  const [items, setItems] = useState<PosmSalesOrder[]>([])
  const [total, setTotal] = useState(0)
  const [summary, setSummary] = useState<PosmSalesOrderStatusSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [slowSeconds, setSlowSeconds] = useState<number | null>(null)
  const [lastRun, setLastRun] = useState<{ at: string; elapsedMs: number } | null>(null)

  const [stores, setStores] = useState<{ label: string; value: string }[]>([])
  const [storesLoaded, setStoresLoaded] = useState(false)

  const [drawerIndex, setDrawerIndex] = useState(-1)
  const [detail, setDetail] = useState<PosmSalesOrderDetailResponse | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const detailCacheRef = useRef(new Map<string, PosmSalesOrderDetailResponse>())
  const drawerGuidRef = useRef('')
  const branchInitializedRef = useRef(false)

  const [pdfModalVisible, setPdfModalVisible] = useState(false)
  const [pdfBlobUrl, setPdfBlobUrl] = useState('')
  const [pdfLoading, setPdfLoading] = useState(false)
  const [pdfOrderGuid, setPdfOrderGuid] = useState('')

  const latestRequestIdRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [listHeight, setListHeight] = useState<number | undefined>(undefined)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const { filters, sort, page, pageSize } = query
  const multiDay = filters.startDate !== filters.endDate
  const summaryView = useMemo(() => summarizeStatuses(summary), [summary])
  const storeName = useCallback(
    (code: string) => stores.find((store) => store.value === code)?.label ?? code,
    [stores],
  )

  /**
   * 唯一的查询入口：先校验（与后端同口径），再取消上一次未返回的请求。
   * 查询中保留旧数据，只把表格变淡；超过 3 秒给出提示并允许取消。
   */
  const runQuery = useCallback(
    async (changes: Partial<QueryState> = {}) => {
      const next: QueryState = { ...queryRef.current, ...changes }
      const validation = validateFilters(next.filters)
      if (!validation.ok) {
        const messages: Record<typeof validation.reason, string> = {
          rangeRequired: t('posmOrders.validation.rangeRequired', '请选择有效的日期范围'),
          rangeTooLong: t('posmOrders.validation.rangeTooLong', { days: MAX_RANGE_DAYS, defaultValue: '日期范围最长 {{days}} 天' }),
          detailRangeTooLong: t('posmOrders.validation.detailRangeTooLong', {
            days: DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS,
            defaultValue: '按件数、种数筛选时，全部分店最长 {{days}} 天；请选择分店或缩短日期',
          }),
          invalidNumberRange: t('posmOrders.validation.invalidNumberRange', '最小值不能大于最大值'),
        }
        message.warning(messages[validation.reason])
        return false
      }

      setQuery(next)
      queryRef.current = next
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller
      const requestId = ++latestRequestIdRef.current
      const startedAt = performance.now()
      setLoading(true)
      setSlowSeconds(null)
      const slowTimer = window.setInterval(() => {
        const elapsed = performance.now() - startedAt
        if (elapsed >= SLOW_QUERY_NOTICE_MS && isLatestRequest(requestId, latestRequestIdRef.current)) {
          setSlowSeconds(Math.floor(elapsed / 100) / 10)
        }
      }, 500)

      try {
        const result = await getSalesOrderList(
          buildListQuery(next.filters, next.sort, next.page, next.pageSize),
          controller.signal,
        )
        if (!isLatestRequest(requestId, latestRequestIdRef.current)) return true
        setItems(result.items)
        setTotal(result.total)
        setSummary(result.summary)
        setDrawerIndex(-1)
        setLastRun({ at: dayjs().format('HH:mm:ss'), elapsedMs: performance.now() - startedAt })
      } catch (error) {
        if (isAbortError(error) || !isLatestRequest(requestId, latestRequestIdRef.current)) return true
        // 后端校验（日期范围、关键词过宽等）会带回可读原因，直接展示。
        message.error(error instanceof Error && error.message ? error.message : t('posmOrders.loadFailed', '加载收银记录失败'))
      } finally {
        window.clearInterval(slowTimer)
        if (isLatestRequest(requestId, latestRequestIdRef.current)) {
          setLoading(false)
          setSlowSeconds(null)
        }
      }
      return true
    },
    [t],
  )

  const cancelQuery = () => {
    abortRef.current?.abort()
    latestRequestIdRef.current++
    setLoading(false)
    setSlowSeconds(null)
    message.info(t('posmOrders.slow.cancelled', '已取消查询，列表保留上一次的结果'))
  }

  const applyFilters = (changes: Partial<PosmSalesOrderFilters>) =>
    runQuery({ filters: { ...queryRef.current.filters, ...changes }, page: 1 })

  // 分店范围：管理员看全部分店；限定分店的账号只能选授权分店，并默认选中第一家。
  useEffect(() => {
    let cancelled = false
    const load = async () => {
      let defaultBranch = ''
      const codes = managedStoreKey === null ? null : managedStoreKey.split(',').filter(Boolean)
      try {
        if (codes === null) {
          const options = await getActiveStores()
          if (!cancelled) setStores(options)
        } else if (codes.length && currentUser?.stores?.length) {
          const visible = currentUser.stores
            .filter((store) => codes.includes(store.storeCode))
            .map((store) => ({ label: store.storeName || store.storeCode, value: store.storeCode }))
          if (!cancelled) setStores(visible)
          defaultBranch = visible[0]?.value ?? ''
        } else if (!cancelled) {
          setStores([])
        }
      } catch {
        // 分店列表只影响下拉选项，加载失败时仍可查询全部授权范围。
      }
      if (cancelled) return
      setStoresLoaded(true)
      // 默认分店只在首次确定分店范围时设置，登录信息刷新不能覆盖用户已选的分店。
      if (defaultBranch && !branchInitializedRef.current) {
        branchInitializedRef.current = true
        const nextFilters = { ...queryRef.current.filters, branchCode: defaultBranch }
        setQuery((current) => ({ ...current, filters: nextFilters }))
        queryRef.current = { ...queryRef.current, filters: nextFilters }
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [managedStoreKey, currentUser?.stores])

  useEffect(() => {
    if (storesLoaded) void runQuery()
    // 只在分店范围确定后首查一次，之后由用户操作触发。
  }, [storesLoaded, runQuery])

  useEffect(() => () => abortRef.current?.abort(), [])

  // 「更多筛选」草稿跟随已生效条件，打开弹层时总是从当前条件开始改。
  useEffect(() => {
    setMoreDraft(pickMoreFilters(filters))
  }, [filters])

  useLayoutEffect(() => {
    const calc = () => {
      // 容器高度按它在视口里的实际起点计算，表格高度扣掉工具栏与分页栏，整页不出现第二条滚动条。
      const wrapTop = wrapRef.current?.getBoundingClientRect().top ?? 0
      const containerH = Math.max(window.innerHeight - wrapTop - LIST_BOTTOM_GAP, LIST_MIN_HEIGHT)
      setListHeight(containerH)
      const toolbarH = toolbarRef.current?.getBoundingClientRect().height ?? 0
      const pagerH = pagerRef.current?.getBoundingClientRect().height ?? 0
      // 40 为表头高度。
      setTableScrollY(Math.max(containerH - toolbarH - pagerH - 40, 200))
    }
    calc()
    window.addEventListener('resize', calc)
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => calc())
    if (toolbarRef.current) observer?.observe(toolbarRef.current)
    if (pagerRef.current) observer?.observe(pagerRef.current)
    return () => {
      window.removeEventListener('resize', calc)
      observer?.disconnect()
    }
  }, [])

  const drawerOrder = drawerIndex >= 0 ? items[drawerIndex] ?? null : null

  const openDrawer = useCallback(
    async (index: number) => {
      const order = items[index]
      if (!order?.orderGuid) return
      const orderGuid = order.orderGuid
      drawerGuidRef.current = orderGuid
      setDrawerIndex(index)
      const cached = detailCacheRef.current.get(orderGuid)
      setDetail(cached ?? null)
      if (cached) return
      setDetailLoading(true)
      try {
        const result = await getSalesOrderDetail(orderGuid)
        detailCacheRef.current.set(orderGuid, result)
        // 快速切换订单时，只接受抽屉当前这一单的详情。
        if (drawerGuidRef.current === orderGuid) setDetail(result)
      } catch {
        if (drawerGuidRef.current === orderGuid) message.error(t('posmOrders.loadDetailFailed', '加载订单详情失败'))
      } finally {
        if (drawerGuidRef.current === orderGuid) setDetailLoading(false)
      }
    },
    [items, t],
  )

  const stepDrawer = useCallback(
    (delta: -1 | 1) => {
      const nextIndex = stepOrderIndex(drawerIndex, delta, items.length)
      if (nextIndex !== drawerIndex && nextIndex >= 0) void openDrawer(nextIndex)
    },
    [drawerIndex, items.length, openDrawer],
  )

  // 抽屉打开时 ↑ ↓ 切换订单；焦点在输入框里时不拦截方向键。
  useEffect(() => {
    if (drawerIndex < 0) return
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        stepDrawer(-1)
      } else if (event.key === 'ArrowDown') {
        event.preventDefault()
        stepDrawer(1)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [drawerIndex, stepDrawer])

  const handlePreviewPdf = async (orderGuid: string) => {
    setPdfOrderGuid(orderGuid)
    setPdfLoading(true)
    setPdfModalVisible(true)
    try {
      const blobUrl = await fetchTaxInvoicePdf(orderGuid)
      if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
      setPdfBlobUrl(blobUrl)
    } catch {
      message.error(t('posmOrders.getInvoiceFailed', '获取发票失败'))
    } finally {
      setPdfLoading(false)
    }
  }

  const handleDownloadPdf = (orderGuid: string) => {
    const link = document.createElement('a')
    link.href = getTaxInvoicePdfUrl(orderGuid)
    link.download = `TaxInvoice_${orderGuid}.pdf`
    link.target = '_blank'
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
  }

  const closePdfModal = () => {
    setPdfModalVisible(false)
    if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
    setPdfBlobUrl('')
    setPdfOrderGuid('')
  }

  const handleReset = () => {
    const defaultBranch = managedStoreCodes?.length ? stores[0]?.value ?? '' : ''
    setKeywordInput('')
    void runQuery({ filters: createDefaultFilters(today, defaultBranch), sort: DEFAULT_SORT, page: 1 })
  }

  const activePreset = detectDatePreset(filters.startDate, filters.endDate, today)
  const detailFilterHint = !filters.branchCode && countDays(filters.startDate, filters.endDate) > DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS

  const filterChipLabels: Record<string, string> = {
    branch: t('posmOrders.columns.branch', '分店'),
    keyword: t('posmOrders.keyword', '关键词'),
    device: t('posmOrders.columns.device', '收银机'),
    time: t('posmOrders.more.timeRange', '时段'),
    actualPay: t('posmOrders.more.actualPay', '实收金额'),
    quantity: t('posmOrders.more.quantity', '件数'),
    skuCount: t('posmOrders.more.skuCount', '种数'),
  }
  const activeFilterItems: ActiveFilterItem[] = buildFilterChips(filters, storeName).map((chip) => ({
    key: chip.key,
    label: filterChipLabels[chip.field],
    value: chip.value,
    source: 'toolbar',
    onRemove: () => {
      if (chip.field === 'keyword') setKeywordInput('')
      void applyFilters(chip.clear)
    },
  }))

  const clearAllFilters = () => {
    setKeywordInput('')
    void applyFilters({ branchCode: '', keyword: '', ...EMPTY_MORE_FILTERS })
  }

  const columns: ColumnsType<PosmSalesOrder> = [
    {
      key: 'time',
      title: t('posmOrders.columns.time', '时间'),
      width: multiDay ? 128 : 88,
      fixed: 'left',
      sorter: true,
      sortOrder: tableSortOrder(sort, 'time'),
      sortDirections: ['descend', 'ascend'],
      render: (_, record) => formatPosmSalesOrderTime(record.orderTime, multiDay ? 'MM-DD HH:mm:ss' : 'HH:mm:ss'),
    },
    {
      key: 'orderNo',
      title: t('posmOrders.columns.orderNo', '订单号'),
      width: 88,
      render: (_, record, index) => (
        <button
          type="button"
          className="posm-orders-order-no"
          aria-label={t('posmOrders.viewOrder', { orderNo: shortOrderNo(record.orderGuid), defaultValue: '查看订单 {{orderNo}} 详情' })}
          onClick={(event) => {
            event.stopPropagation()
            void openDrawer(index)
          }}
        >
          …{shortOrderNo(record.orderGuid)}
        </button>
      ),
    },
    {
      key: 'branch',
      title: t('posmOrders.columns.branch', '分店'),
      width: 140,
      ellipsis: true,
      sorter: true,
      sortOrder: tableSortOrder(sort, 'branch'),
      sortDirections: ['ascend', 'descend'],
      render: (_, record) => record.branchName || record.branchCode || '-',
    },
    {
      key: 'device',
      title: t('posmOrders.columns.device', '收银机'),
      width: 132,
      render: (_, record) => <span className="posm-orders-mono">{record.deviceCode || '-'}</span>,
    },
    {
      key: 'status',
      title: t('posmOrders.columns.status', '状态'),
      width: 88,
      render: (_, record) => <OrderStatusTag status={record.status} />,
    },
    {
      key: 'payment',
      title: t('posmOrders.columns.payment', '支付'),
      width: 84,
      render: (_, record) => <PaymentMethodsText methods={record.paymentMethods} />,
    },
    ...(filters.keyword.trim()
      ? [
          {
            key: 'matched',
            title: t('posmOrders.columns.matched', '命中商品'),
            width: 220,
            render: (_: unknown, record: PosmSalesOrder) => {
              const hits = record.matchedProducts ?? []
              if (!hits.length) return <span className="posm-orders-muted">{t('posmOrders.matchedOrderNo', '订单号匹配')}</span>
              const first = hits[0]
              return (
                <Tooltip
                  title={hits.map((hit) => `${hit.itemNumber || hit.productCode} ${hit.productName ?? ''} ×${hit.quantity}`).join('\n')}
                >
                  <span className="posm-orders-hit">
                    <span className="posm-orders-hit-code">{first.itemNumber || first.productCode}</span>
                    <span className="posm-orders-hit-name">{first.productName}</span>
                    <span className="posm-orders-muted">×{first.quantity}</span>
                    {hits.length > 1 ? <span className="posm-orders-muted">+{hits.length - 1}</span> : null}
                  </span>
                </Tooltip>
              )
            },
          },
        ]
      : []),
    {
      key: 'goods',
      title: t('posmOrders.columns.goods', '商品'),
      width: 104,
      align: 'right',
      render: (_, record) => (
        <span>
          {record.quantityTotal ?? 0} {t('posmOrders.unitPieces', '件')}
          <span className="posm-orders-muted">
            {' · '}
            {record.skuCount ?? 0} {t('posmOrders.unitKinds', '种')}
          </span>
        </span>
      ),
    },
    {
      key: 'totalAmount',
      title: t('posmOrders.columns.totalAmount', '金额'),
      width: 100,
      align: 'right',
      sorter: true,
      sortOrder: tableSortOrder(sort, 'totalAmount'),
      sortDirections: ['descend', 'ascend'],
      render: (_, record) => (
        <span
          className={[
            (record.totalAmount ?? 0) < 0 ? 'posm-orders-negative' : '',
            record.status === OrderType.Cancelled ? 'posm-orders-strike' : '',
          ].join(' ')}
        >
          {formatMoney(record.totalAmount)}
        </span>
      ),
    },
    {
      key: 'discountAmount',
      title: t('posmOrders.columns.discount', '折扣'),
      width: 88,
      align: 'right',
      sorter: true,
      sortOrder: tableSortOrder(sort, 'discountAmount'),
      sortDirections: ['descend', 'ascend'],
      render: (_, record) =>
        record.discountAmount ? (
          <span className="posm-orders-muted">{formatMoney(-Math.abs(record.discountAmount))}</span>
        ) : (
          <span className="posm-orders-dash">—</span>
        ),
    },
    {
      key: 'actualPay',
      title: t('posmOrders.columns.actualPay', '实收'),
      width: 104,
      fixed: 'right',
      align: 'right',
      sorter: true,
      sortOrder: tableSortOrder(sort, 'actualPay'),
      sortDirections: ['descend', 'ascend'],
      render: (_, record) => {
        const actual = orderActualAmount(record.totalAmount, record.discountAmount)
        return (
          <strong
            className={[
              actual < 0 ? 'posm-orders-negative' : '',
              record.status === OrderType.Cancelled ? 'posm-orders-strike posm-orders-muted' : '',
            ].join(' ')}
          >
            {formatMoney(actual)}
          </strong>
        )
      },
    },
    {
      key: 'invoice',
      title: t('posmOrders.columns.invoice', '发票'),
      width: 56,
      fixed: 'right',
      align: 'center',
      render: (_, record) => (
        <Button
          type="text"
          size="small"
          icon={<FilePdfOutlined />}
          aria-label={t('posmOrders.previewInvoice', '预览发票')}
          onClick={(event) => {
            event.stopPropagation()
            if (record.orderGuid) void handlePreviewPdf(record.orderGuid)
          }}
        />
      ),
    },
  ]

  const rangeFrom = total === 0 ? 0 : (page - 1) * pageSize + 1
  const rangeTo = Math.min(page * pageSize, total)
  const headerMeta = loading
    ? t('posmOrders.querying', '查询中…')
    : lastRun
      ? `${t('posmOrders.updatedAt', { time: lastRun.at, defaultValue: '更新于 {{time}}' })} · ${t('posmOrders.elapsed', {
          seconds: (lastRun.elapsedMs / 1000).toFixed(1),
          defaultValue: '用时 {{seconds}} 秒',
        })}`
      : ''

  const emptyState = (
    <div className="posm-orders-empty">
      <span className="posm-orders-empty-title">{t('posmOrders.empty.title', '没有符合条件的订单')}</span>
      <span>
        {filters.keyword.trim() ? (
          <Button
            size="small"
            onClick={() => {
              setKeywordInput('')
              void applyFilters({ keyword: '' })
            }}
          >
            {t('posmOrders.empty.clearKeyword', '清除关键词')}
          </Button>
        ) : null}{' '}
        {activeFilterItems.length ? (
          <Button size="small" type="link" onClick={clearAllFilters}>
            {t('common.listToolbar.clearAllFilters', '清空全部')}
          </Button>
        ) : null}
      </span>
    </div>
  )

  return (
    <PageContainer
      compact
      title={t('posmOrders.cashierRecords', '收银记录')}
      subtitle={
        lastRun === null ? undefined : t('posmOrders.orderCount', { value: total.toLocaleString('en-AU'), defaultValue: '{{value}} 单' })
      }
      extra={
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span className="posm-orders-meta" aria-live="polite">
            {headerMeta}
          </span>
          <Tooltip title={t('posmOrders.requery', '重新查询')}>
            <Button icon={<ReloadOutlined />} aria-label={t('posmOrders.requery', '重新查询')} onClick={() => void runQuery()} />
          </Tooltip>
        </div>
      }
    >
      <div ref={wrapRef} className="posm-orders-panel" style={{ height: listHeight ?? 'calc(100vh - 180px)' }}>
        <div ref={toolbarRef} className="posm-orders-toolbar">
          <div className="list-toolbar-filter-row">
            <Segmented<PosmSalesOrderDatePreset | ''>
              className="posm-orders-presets"
              aria-label={t('posmOrders.dateRange', '日期范围')}
              value={activePreset ?? ''}
              options={DATE_PRESETS.map((preset) => ({
                value: preset,
                label: t(`posmOrders.presets.${preset}`, PRESET_FALLBACK[preset]),
              }))}
              onChange={(preset) => {
                if (!preset) return
                const [startDate, endDate] = resolveDatePreset(preset, today)
                void applyFilters({ startDate, endDate })
              }}
            />
            <DatePicker.RangePicker
              aria-label={t('posmOrders.dateRange', '日期范围')}
              value={[dayjs(filters.startDate), dayjs(filters.endDate)]}
              allowClear={false}
              format="YYYY-MM-DD"
              style={{ width: 240 }}
              // 与后端同口径：区间最长 92 天，未来日期没有数据。
              disabledDate={(current: Dayjs, info?: { from?: Dayjs }) =>
                current.isAfter(dayjs(), 'day') ||
                (info?.from ? Math.abs(current.diff(info.from, 'day')) >= MAX_RANGE_DAYS : false)
              }
              onChange={(dates) => {
                if (!dates?.[0] || !dates?.[1]) return
                void applyFilters({ startDate: dates[0].format('YYYY-MM-DD'), endDate: dates[1].format('YYYY-MM-DD') })
              }}
            />
            <Select
              aria-label={t('posmOrders.columns.branch', '分店')}
              placeholder={t('posmOrders.allStores', '全部分店')}
              value={filters.branchCode || undefined}
              // 清空表示「全部可见分店」：限定分店的账号由后端收窄到授权分店。
              allowClear
              showSearch
              optionFilterProp="label"
              style={{ width: 200 }}
              options={stores}
              onChange={(value?: string) => void applyFilters({ branchCode: value ?? '' })}
            />
            <Input
              aria-label={t('posmOrders.keyword', '关键词')}
              allowClear
              prefix={<SearchOutlined className="posm-orders-muted" />}
              placeholder={t('posmOrders.keywordPlaceholder', '订单号 / 货号 / 条码 / 商品名')}
              style={{ width: 300 }}
              value={keywordInput}
              onChange={(event) => {
                setKeywordInput(event.target.value)
                // 点清除图标后立即回到不带关键词的结果。
                if (!event.target.value && filters.keyword) void applyFilters({ keyword: '' })
              }}
              onPressEnter={() => void applyFilters({ keyword: keywordInput })}
            />
            <Button type="primary" onClick={() => void applyFilters({ keyword: keywordInput })}>
              {t('common.query', '查询')}
            </Button>
            <MoreFiltersButton activeCount={countMoreFilters(filters)} open={moreOpen} onOpenChange={setMoreOpen}>
              <div className="posm-orders-more-field">
                <span className="posm-orders-more-label">{t('posmOrders.columns.device', '收银机')}</span>
                <Input
                  allowClear
                  aria-label={t('posmOrders.columns.device', '收银机')}
                  placeholder={t('posmOrders.more.devicePlaceholder', '如 POS_1005_1231')}
                  value={moreDraft.deviceCode ?? ''}
                  onChange={(event) => setMoreDraft((draft) => ({ ...draft, deviceCode: event.target.value || undefined }))}
                />
              </div>
              <div className="posm-orders-more-field">
                <span className="posm-orders-more-label">{t('posmOrders.more.timeRange', '时段')}</span>
                <TimePicker.RangePicker
                  aria-label={t('posmOrders.more.timeRange', '时段')}
                  format="HH:mm"
                  value={
                    moreDraft.timeStart && moreDraft.timeEnd
                      ? [dayjs(`2000-01-01T${moreDraft.timeStart}`), dayjs(`2000-01-01T${moreDraft.timeEnd}`)]
                      : null
                  }
                  onChange={(times) =>
                    setMoreDraft((draft) => ({
                      ...draft,
                      timeStart: times?.[0]?.format('HH:mm:00'),
                      // 结束时间含整分钟：15:00–17:00 包括 17:00:59 之前的订单。
                      timeEnd: times?.[1]?.format('HH:mm:59'),
                    }))
                  }
                />
              </div>
              {(
                [
                  ['actualPay', t('posmOrders.more.actualPay', '实收金额'), false],
                  ['quantity', t('posmOrders.more.quantity', '件数'), true],
                  ['skuCount', t('posmOrders.more.skuCount', '种数'), true],
                ] as const
              ).map(([field, label, integer]) => (
                <div key={field} className="posm-orders-more-field">
                  <span className="posm-orders-more-label">{label}</span>
                  <span className="posm-orders-more-range">
                    <InputNumber
                      aria-label={`${label} ${t('posmOrders.more.min', '最小')}`}
                      placeholder={t('posmOrders.more.unlimited', '不限')}
                      controls={false}
                      precision={integer ? 0 : 2}
                      value={moreDraft[`${field}Min`] ?? null}
                      onChange={(value) =>
                        setMoreDraft((draft) => ({ ...draft, [`${field}Min`]: normalizeFilterNumber(value, integer) }))
                      }
                    />
                    <span className="posm-orders-muted">—</span>
                    <InputNumber
                      aria-label={`${label} ${t('posmOrders.more.max', '最大')}`}
                      placeholder={t('posmOrders.more.unlimited', '不限')}
                      controls={false}
                      precision={integer ? 0 : 2}
                      value={moreDraft[`${field}Max`] ?? null}
                      onChange={(value) =>
                        setMoreDraft((draft) => ({ ...draft, [`${field}Max`]: normalizeFilterNumber(value, integer) }))
                      }
                    />
                  </span>
                </div>
              ))}
              {detailFilterHint ? (
                <span className="posm-orders-more-hint">
                  {t('posmOrders.more.detailRangeHint', {
                    days: DETAIL_AGGREGATE_ALL_STORES_MAX_DAYS,
                    defaultValue: '件数、种数条件在全部分店时最长 {{days}} 天',
                  })}
                </span>
              ) : null}
              <div className="posm-orders-more-actions">
                <Button
                  onClick={() => {
                    setMoreDraft(EMPTY_MORE_FILTERS)
                    setMoreOpen(false)
                    void applyFilters(EMPTY_MORE_FILTERS)
                  }}
                >
                  {t('posmOrders.more.clear', '清空')}
                </Button>
                <Button
                  type="primary"
                  onClick={async () => {
                    const applied = await applyFilters(moreDraft)
                    if (applied) setMoreOpen(false)
                  }}
                >
                  {t('posmOrders.more.apply', '应用')}
                </Button>
              </div>
            </MoreFiltersButton>
            <span className="list-toolbar-filter-spacer" />
            <Button type="text" onClick={handleReset}>
              {t('common.reset', '重置')}
            </Button>
          </div>

          {activeFilterItems.length ? <ActiveFilterBar items={activeFilterItems} onClearAll={clearAllFilters} /> : null}

          {slowSeconds !== null ? (
            <div className="posm-orders-slow" role="status">
              <ClockCircleOutlined style={{ color: '#d48806' }} aria-hidden="true" />
              <span className="posm-orders-slow-text">
                <strong>
                  {t('posmOrders.slow.title', { seconds: slowSeconds.toFixed(1), defaultValue: '已查询 {{seconds}} 秒，数据量较大' })}
                </strong>
                <span className="posm-orders-slow-hint">{t('posmOrders.slow.hint', '缩小日期范围或指定分店会更快')}</span>
              </span>
              <Button size="small" onClick={cancelQuery}>
                {t('posmOrders.slow.cancel', '取消查询')}
              </Button>
            </div>
          ) : null}

          <StatusSummaryStrip
            view={summaryView}
            activeStatus={filters.status}
            pending={lastRun === null}
            onSelect={(status) => void applyFilters({ status })}
          />
        </div>

        <div className={`posm-orders-table-wrap${loading ? ' is-loading' : ''}`} aria-busy={loading}>
          {loading ? <div className="posm-orders-progress" aria-hidden="true" /> : null}
          <MeasuredTable<PosmSalesOrder>
            metricId="posm-sales-orders.table-1"
            className="posm-orders-table"
            size="small"
            rowKey={(record) => record.orderGuid ?? ''}
            dataSource={items}
            columns={columns}
            pagination={false}
            tableLayout="fixed"
            // 窄屏时横向滚动，时间固定在左、实收与发票固定在右，关键金额始终可见。
            scroll={{ x: filters.keyword.trim() ? 1290 : 1070, y: tableScrollY }}
            locale={{ emptyText: loading ? ' ' : emptyState }}
            rowClassName={(record, index) =>
              [index === drawerIndex ? 'is-active' : '', record.status === OrderType.Cancelled ? 'is-cancelled' : '']
                .filter(Boolean)
                .join(' ')
            }
            onRow={(_, index) => ({
              onClick: () => {
                if (typeof index === 'number') void openDrawer(index)
              },
            })}
            onChange={(_pagination, _filters, sorter, extra) => {
              if (extra.action !== 'sort') return
              const active = Array.isArray(sorter) ? sorter[0] : sorter
              void runQuery({ sort: mapTableSort(active?.columnKey, active?.order), page: 1 })
            }}
          />
        </div>

        <div ref={pagerRef} className="posm-orders-pager">
          <span className="posm-orders-pager-range">
            {t('posmOrders.pagerRange', {
              from: rangeFrom.toLocaleString('en-AU'),
              to: rangeTo.toLocaleString('en-AU'),
              total: total.toLocaleString('en-AU'),
              defaultValue: '第 {{from}}–{{to}} 条 · 共 {{total}} 单',
            })}
          </span>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            showSizeChanger
            responsive={false}
            pageSizeOptions={[20, 50, 100]}
            onChange={(nextPage, nextPageSize) =>
              void runQuery({ page: nextPageSize !== pageSize ? 1 : nextPage, pageSize: nextPageSize })
            }
          />
        </div>
      </div>

      <OrderDetailDrawer
        open={drawerIndex >= 0}
        order={drawerOrder}
        detail={detail}
        loading={detailLoading}
        hasPrev={drawerIndex > 0}
        hasNext={drawerIndex >= 0 && drawerIndex < items.length - 1}
        onPrev={() => stepDrawer(-1)}
        onNext={() => stepDrawer(1)}
        onClose={() => {
          drawerGuidRef.current = ''
          setDrawerIndex(-1)
        }}
        onPreviewInvoice={(orderGuid) => void handlePreviewPdf(orderGuid)}
        onDownloadInvoice={handleDownloadPdf}
      />

      <Modal
        title={t('posmOrders.invoicePreview', '发票预览')}
        open={pdfModalVisible}
        onCancel={closePdfModal}
        footer={[
          <Button
            key="download"
            type="primary"
            icon={<DownloadOutlined />}
            onClick={() => {
              if (pdfOrderGuid) handleDownloadPdf(pdfOrderGuid)
            }}
          >
            {t('common.download', '下载')}
          </Button>,
          <Button key="close" onClick={closePdfModal}>
            {t('common.close', '关闭')}
          </Button>,
        ]}
        width={900}
        centered
        destroyOnHidden
      >
        <div style={{ height: 600, overflow: 'auto' }}>
          {pdfLoading ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              {t('common.loading', '加载中')}
            </div>
          ) : pdfBlobUrl ? (
            <iframe src={pdfBlobUrl} style={{ width: '100%', height: '100%', border: 'none' }} title={t('posmOrders.invoicePreview', '发票预览')} />
          ) : null}
        </div>
      </Modal>
    </PageContainer>
  )
}
