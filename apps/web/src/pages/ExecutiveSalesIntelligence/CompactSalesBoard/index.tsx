import { CloseOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, DatePicker, Image, Input, message, Pagination, Segmented, Tooltip } from 'antd'
import type { TableProps } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAuthStore } from '../../../store/auth'
import { MeasuredTable } from '../../../components/MeasuredTable'
import {
  getCompactSalesBoard,
  type CompactSalesBoard,
  type CompactSalesBoardChinaSupplier,
  type CompactSalesBoardProduct,
  type CompactSalesBoardStore,
  type DateRange,
} from '../../../services/salesDashboardService'
import styles from './styles.module.css'
import { MAX_REPORT_DAYS, quickDateSelection, type QuickRange as ReportQuickRange } from '../ReportWorkbench/logic'
import { readCompactBoardCache, writeCompactBoardCache, type CompactBoardCacheEntry } from './cache'
import {
  buildPanelKeys,
  buildRequestKey,
  defaultProductSort,
  describeProductSort,
  formatShare,
  matchesSupplierSearch,
  pageRange,
  resolveProductSort,
  resolveSupplierToggle,
  shareOf,
  shouldHandleEscape,
  toAntdSortOrder,
  toggleSelection,
  type BoardFilterState,
  type BoardSelection,
  type PanelKeys,
  type ProductSort,
} from './logic'

const { RangePicker } = DatePicker

type QuickRange = Exclude<ReportQuickRange, 'custom'>
type CacheState = 'cached' | 'fresh' | 'refreshing' | 'error'

const quickRangeOptions: { label: string; value: QuickRange }[] = [
  { label: '今天', value: 'today' },
  { label: '昨天', value: 'yesterday' },
  { label: '本周', value: 'thisWeek' },
  { label: '上周', value: 'lastWeek' },
  { label: '本月', value: 'thisMonth' },
  { label: '上月', value: 'lastMonth' },
]

const compactCurrencyFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 })
const priceFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const integerFormatter = new Intl.NumberFormat('en-AU')
const compactBoardClientCacheMs = 30_000
// 中文注释：区间上限与销售明细共用（两年含闰日），后端同样按 731 天校验。
const maxSalesDateRangeDays = MAX_REPORT_DAYS
const keywordDebounceMs = 300
const pageSizeOptions = [50, 80, 120, 200]
// 中文注释：后台外壳（顶栏 + 标签页）约 160px，与其他全高页面一致；
// 再减去本页头部卡片、栏标题、表头与内边距（约 293px），三栏表格恰好撑满一屏不出现整页滚动。
const tableBodyHeight = 'calc(100vh - 453px)'
const productTableBodyHeight = 'calc(100vh - 497px)'
// 中文注释：按页面实际可用宽度（扣除左侧菜单后）切换紧凑列宽，1440 笔记本展开菜单时也能放下三栏。
const compactLayoutWidth = 1400

const emptyBoard: CompactSalesBoard = {
  stores: [],
  chinaSuppliers: [],
  productDetails: { data: [], total: 0, pageIndex: 1, pageSize: 80, scopeAmount: 0 },
  summary: { totalAmount: 0, totalQuantity: 0, productCount: 0, storeCount: 0, supplierCount: 0, overallAmount: 0, overallQuantity: 0 },
  fromCache: false,
}

// 中文注释：快捷区间与销售明细同一套规则（ISO 周、截止到今天）。
// 旧写法「本周/本月」包含未来日期，未来日期没有统计状态，服务端会判为未发布，看板整页不出数。
function resolveQuickRange(range: QuickRange): [Dayjs, Dayjs] {
  const selection = quickDateSelection(range)
  return [dayjs(selection.startDate), dayjs(selection.endDate)]
}

function isDisabledDate(date: Dayjs, info: { from?: Dayjs }) {
  return date.isAfter(dayjs(), 'day') || Boolean(info.from && Math.abs(date.diff(info.from, 'day')) >= maxSalesDateRangeDays)
}

function toDateRange(dateRange: [Dayjs, Dayjs]): DateRange {
  return { startDate: dateRange[0].format('YYYY-MM-DD'), endDate: dateRange[1].format('YYYY-MM-DD') }
}

function formatCurrency(value: number) {
  return compactCurrencyFormatter.format(value || 0)
}

function formatPrice(value: number) {
  return priceFormatter.format(value || 0)
}

function formatInteger(value: number) {
  return integerFormatter.format(value || 0)
}

function formatStatisticTime(value?: string) {
  if (!value) return ''
  const time = dayjs(value)
  if (!time.isValid()) return ''
  return time.isSame(dayjs(), 'day') ? time.format('HH:mm') : time.format('MM-DD HH:mm')
}

function isKeyboardSelection(event: React.KeyboardEvent) {
  return event.key === 'Enter' || event.key === ' '
}

function ShareBar({ value, total, compact }: { value: number; total: number; compact: boolean }) {
  const share = shareOf(value, total)
  if (compact) return <span className={styles.shareText} title={`合计 ${formatCurrency(total)}`}>{formatShare(value, total)}</span>
  return (
    <div className={styles.share} title={`合计 ${formatCurrency(total)}`}>
      <div className={styles.shareTrack}><i style={{ width: `${total > 0 ? Math.max(2, share * 100) : 0}%` }} /></div>
      <span>{formatShare(value, total)}</span>
    </div>
  )
}

const CompactSalesBoardPage: React.FC = () => {
  const { active } = useKeepAliveContext()
  const access = useAuthStore((state) => state.access)
  const rawManagedStoreCodes = access.managedStoreCodes?.() ?? undefined
  const managedStoreCodesKey = rawManagedStoreCodes?.join('|') ?? 'ALL'
  const managedStoreCodes = useMemo(
    () => rawManagedStoreCodes ? [...rawManagedStoreCodes] : undefined,
    [managedStoreCodesKey],
  )
  const [quickRange, setQuickRange] = useState<QuickRange | null>('today')
  const [dateRange, setDateRange] = useState<[Dayjs, Dayjs]>(() => resolveQuickRange('today'))
  const [board, setBoard] = useState<CompactSalesBoard>(emptyBoard)
  const [loadedKeys, setLoadedKeys] = useState<PanelKeys | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [cacheState, setCacheState] = useState<CacheState>('fresh')
  const [queryMs, setQueryMs] = useState<number | null>(null)
  const [selectedBranch, setSelectedBranch] = useState<BoardSelection | null>(null)
  const [selectedSupplier, setSelectedSupplier] = useState<BoardSelection | null>(null)
  const [selectedProduct, setSelectedProduct] = useState<BoardSelection | null>(null)
  const [keywordInput, setKeywordInput] = useState('')
  const [keyword, setKeyword] = useState('')
  const [compositionTick, setCompositionTick] = useState(0)
  const [supplierSearch, setSupplierSearch] = useState('')
  const [productSort, setProductSort] = useState<ProductSort>(defaultProductSort)
  const [pageIndex, setPageIndex] = useState(1)
  const [pageSize, setPageSize] = useState(80)
  const [reloadKey, setReloadKey] = useState(0)
  const forceRefreshRef = useRef(false)
  const composingRef = useRef(false)
  const boardRequestAbortRef = useRef<AbortController | null>(null)
  const boardCacheRef = useRef(new Map<string, CompactBoardCacheEntry<CompactSalesBoard>>())
  const pageRef = useRef<HTMLDivElement>(null)
  const [compact, setCompact] = useState(false)

  useEffect(() => {
    const element = pageRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => setCompact(entry.contentRect.width < compactLayoutWidth))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const dateRangeParams = useMemo(() => toDateRange(dateRange), [dateRange])
  const filterState = useMemo<BoardFilterState>(() => ({
    dateRange: dateRangeParams,
    scopeKey: managedStoreCodesKey,
    branch: selectedBranch,
    supplier: selectedSupplier,
    product: selectedProduct,
    keyword,
    productSort,
    pageIndex,
    pageSize,
  }), [dateRangeParams, keyword, managedStoreCodesKey, pageIndex, pageSize, productSort, selectedBranch, selectedProduct, selectedSupplier])
  const panelKeys = useMemo(() => buildPanelKeys(filterState), [filterState])

  const loadBoard = useCallback(async (signal?: AbortSignal) => {
    const forceRefresh = forceRefreshRef.current
    forceRefreshRef.current = false
    const requestKey = buildRequestKey(filterState)
    const requestPanelKeys = buildPanelKeys(filterState)
    const cachedBoard = !forceRefresh
      ? readCompactBoardCache(boardCacheRef.current, requestKey)
      : undefined
    if (cachedBoard) {
      setBoard(cachedBoard)
      setLoadedKeys(requestPanelKeys)
      setLoadError(null)
      setCacheState('cached')
      setLoading(false)
      return
    }

    setLoading(true)
    setLoadError(null)
    setCacheState(forceRefresh ? 'refreshing' : 'fresh')
    const startedAt = performance.now()
    try {
      // 中文注释：branchCodes 只传授权范围；页面选中的分店单独传，服务端据此让分店栏不被自身收窄。
      const result = await getCompactSalesBoard({
        dateRange: filterState.dateRange,
        branchCodes: managedStoreCodes,
        selectedBranchCode: filterState.branch?.code,
        selectedChinaSupplierCode: filterState.supplier?.code,
        selectedProductCode: filterState.product?.code,
        keyword: filterState.keyword,
        sortField: filterState.productSort.field,
        sortOrder: filterState.productSort.order,
        pageIndex: filterState.pageIndex,
        pageSize: filterState.pageSize,
        forceRefresh,
      }, signal)
      if (!signal?.aborted) {
        setBoard(result)
        setLoadedKeys(requestPanelKeys)
        setQueryMs(Math.round(performance.now() - startedAt))
        setCacheState('fresh')
        // 中文注释：仅复用完全相同的交互参数；按钮刷新始终绕过前后端缓存。
        writeCompactBoardCache(boardCacheRef.current, requestKey, {
          expiresAt: Date.now() + compactBoardClientCacheMs,
          data: result,
        })
      }
    } catch (error) {
      if (!signal?.aborted && (error as { name?: string })?.name !== 'AbortError') {
        setBoard(emptyBoard)
        setLoadedKeys(requestPanelKeys)
        setLoadError('销售看板数据加载失败，请重试。')
        setCacheState('error')
        message.error('销售看板数据加载失败')
      }
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }, [filterState, managedStoreCodes])

  useEffect(() => {
    boardRequestAbortRef.current?.abort()
    if (!active) {
      setLoading(false)
      return
    }

    // 中文注释：新的点选会中止上一次请求；旧数据保留在界面上，只有受影响的栏显示加载条。
    const controller = new AbortController()
    boardRequestAbortRef.current = controller
    void loadBoard(controller.signal)
    return () => {
      controller.abort()
      if (boardRequestAbortRef.current === controller) {
        boardRequestAbortRef.current = null
      }
    }
  }, [active, loadBoard, reloadKey])

  // 中文注释：关键词防抖；中文输入法组词期间不触发查询，确认候选（compositionTick 变化）后再查。
  useEffect(() => {
    if (composingRef.current) return
    const nextKeyword = keywordInput.trim()
    if (nextKeyword === keyword) return
    const timer = window.setTimeout(() => {
      setKeyword(nextKeyword)
      setPageIndex(1)
    }, keywordDebounceMs)
    return () => window.clearTimeout(timer)
  }, [compositionTick, keyword, keywordInput])

  const isFilterActive = Boolean(selectedBranch || selectedSupplier || selectedProduct)
  const clearFilters = useCallback(() => {
    setSelectedBranch(null)
    setSelectedSupplier(null)
    setSelectedProduct(null)
    setPageIndex(1)
  }, [])

  useEffect(() => {
    if (!active || !isFilterActive) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && shouldHandleEscape(event.target)) clearFilters()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [active, clearFilters, isFilterActive])

  const selectBranch = useCallback((next: BoardSelection) => {
    setSelectedBranch((current) => toggleSelection(current, next))
    setPageIndex(1)
  }, [])
  const selectSupplier = useCallback((next: BoardSelection) => {
    const resolved = resolveSupplierToggle({ supplier: selectedSupplier, product: selectedProduct }, next)
    setSelectedSupplier(resolved.supplier)
    setSelectedProduct(resolved.product)
    setPageIndex(1)
  }, [selectedProduct, selectedSupplier])
  const selectProduct = useCallback((next: BoardSelection) => {
    setSelectedProduct((current) => toggleSelection(current, next))
  }, [])

  const handleRangeChange = useCallback((value: [Dayjs | null, Dayjs | null] | null) => {
    if (!value?.[0] || !value[1]) return
    if (value[1].diff(value[0], 'day') + 1 > maxSalesDateRangeDays) {
      message.warning(`日期范围最多 ${maxSalesDateRangeDays} 天`)
      return
    }
    setQuickRange(null)
    setDateRange([value[0], value[1]])
    setPageIndex(1)
  }, [])

  const forceRefresh = useCallback(() => {
    forceRefreshRef.current = true
    setReloadKey((key) => key + 1)
  }, [])

  const isFirstLoad = loadedKeys === null
  const storesStale = loading && loadedKeys?.stores !== panelKeys.stores
  const suppliersStale = loading && loadedKeys?.suppliers !== panelKeys.suppliers
  const productsStale = loading && loadedKeys?.products !== panelKeys.products
  const summaryStale = loading && loadedKeys?.summary !== panelKeys.summary

  const storeTotal = useMemo(() => board.stores.reduce((sum, row) => sum + row.totalAmount, 0), [board.stores])
  const supplierTotal = useMemo(() => board.chinaSuppliers.reduce((sum, row) => sum + row.totalAmount, 0), [board.chinaSuppliers])
  const visibleSuppliers = useMemo(
    () => board.chinaSuppliers.filter((row) => matchesSupplierSearch(row, supplierSearch)),
    [board.chinaSuppliers, supplierSearch],
  )

  const branchColumns = useMemo<ColumnsType<CompactSalesBoardStore>>(() => [
    {
      title: '分店', dataIndex: 'branchName', key: 'branchName', ellipsis: true,
      sorter: (a, b) => a.branchName.localeCompare(b.branchName), sortDirections: ['ascend', 'descend'],
      render: (_, record) => <div className={styles.primaryCell}><span>{record.branchName || record.branchCode}</span><small>{record.branchCode}<i>·</i>{formatInteger(record.productCount)} 款</small></div>,
    },
    { title: '金额', dataIndex: 'totalAmount', key: 'totalAmount', align: 'right', width: compact ? 74 : 84, defaultSortOrder: 'descend', sorter: (a, b) => a.totalAmount - b.totalAmount, sortDirections: ['descend', 'ascend'], render: (value: number) => <span className={styles.num}>{formatCurrency(value)}</span> },
    { title: '数量', dataIndex: 'totalQuantity', key: 'totalQuantity', align: 'right', width: compact ? 58 : 64, sorter: (a, b) => a.totalQuantity - b.totalQuantity, sortDirections: ['descend', 'ascend'], render: formatInteger },
    { title: <Tooltip title="分母为本栏合计">占比</Tooltip>, key: 'share', width: compact ? 60 : 96, render: (_, record) => <ShareBar value={record.totalAmount} total={storeTotal} compact={compact} /> },
  ], [compact, storeTotal])

  const supplierColumns = useMemo<ColumnsType<CompactSalesBoardChinaSupplier>>(() => [
    {
      title: '国内供应商', dataIndex: 'supplierName', key: 'supplierName', ellipsis: true,
      sorter: (a, b) => a.supplierName.localeCompare(b.supplierName, 'zh-CN'), sortDirections: ['ascend', 'descend'],
      render: (_, record) => <div className={styles.primaryCell}><span>{record.supplierName || record.supplierCode}</span><small>{record.supplierCode}<i>·</i>{formatInteger(record.productCount)} 款</small></div>,
    },
    { title: '金额', dataIndex: 'totalAmount', key: 'totalAmount', align: 'right', width: compact ? 74 : 84, defaultSortOrder: 'descend', sorter: (a, b) => a.totalAmount - b.totalAmount, sortDirections: ['descend', 'ascend'], render: (value: number) => <span className={styles.num}>{formatCurrency(value)}</span> },
    { title: '数量', dataIndex: 'totalQuantity', key: 'totalQuantity', align: 'right', width: compact ? 58 : 64, sorter: (a, b) => a.totalQuantity - b.totalQuantity, sortDirections: ['descend', 'ascend'], render: formatInteger },
    { title: <Tooltip title="分母为本栏合计">占比</Tooltip>, key: 'share', width: compact ? 60 : 96, render: (_, record) => <ShareBar value={record.totalAmount} total={supplierTotal} compact={compact} /> },
  ], [compact, supplierTotal])

  const productRankOffset = (board.productDetails.pageIndex - 1) * board.productDetails.pageSize
  const productColumns = useMemo<ColumnsType<CompactSalesBoardProduct>>(() => [
    { title: '#', key: 'rank', width: 40, align: 'right', render: (_, __, index) => <span className={styles.rank}>{productRankOffset + index + 1}</span> },
    { title: '图片', dataIndex: 'productImage', key: 'productImage', width: 52, render: (value: string | undefined, record) => <div className={styles.productImageBox}>{value ? <Image src={value} alt={record.productName ?? record.itemNumber ?? record.productCode} width={30} height={30} loading="lazy" preview={false} className={styles.productImage} fallback="" /> : <span />}</div> },
    {
      title: '货号 / 名称', dataIndex: 'itemNumber', key: 'itemNumber', ellipsis: true,
      sorter: true, sortOrder: toAntdSortOrder(productSort, 'itemNumber'), sortDirections: ['ascend', 'descend'],
      render: (_, record) => <div className={styles.primaryCell}><span>{record.itemNumber || record.productCode}</span><small title={record.productName}>{record.productName || record.productCode}</small></div>,
    },
    {
      title: '供应商', dataIndex: 'chinaSupplierCode', key: 'chinaSupplierCode', width: 78,
      render: (value: string | undefined, record) => value
        ? (
          <Tooltip title={`${record.chinaSupplierName ?? value}：点击按该供应商筛选`}>
            <button
              type="button"
              className={[styles.supplierCode, selectedSupplier?.code === value ? styles.supplierCodeActive : ''].filter(Boolean).join(' ')}
              onClick={(event) => {
                // 中文注释：只切换供应商，不触发整行的商品选中。
                event.stopPropagation()
                selectSupplier({ code: value, label: record.chinaSupplierName || value, detail: value })
              }}
              onKeyDown={(event) => event.stopPropagation()}
            >
              {value}
            </button>
          </Tooltip>
        )
        : '-',
    },
    { title: '数量', dataIndex: 'totalQuantity', key: 'quantity', align: 'right', width: 64, sorter: true, sortOrder: toAntdSortOrder(productSort, 'quantity'), sortDirections: ['descend', 'ascend'], render: formatInteger },
    { title: '单价', dataIndex: 'unitPrice', key: 'unitPrice', align: 'right', width: 76, sorter: true, sortOrder: toAntdSortOrder(productSort, 'unitPrice'), sortDirections: ['descend', 'ascend'], render: formatPrice },
    { title: '金额', dataIndex: 'totalAmount', key: 'amount', align: 'right', width: 84, sorter: true, sortOrder: toAntdSortOrder(productSort, 'amount'), sortDirections: ['descend', 'ascend'], render: (value: number) => <span className={styles.num}>{formatCurrency(value)}</span> },
    { title: <Tooltip title="分母为当前分店、供应商筛选下全部商品的合计（不受搜索影响）">占比</Tooltip>, key: 'share', width: compact ? 60 : 96, render: (_, record) => <ShareBar value={record.totalAmount} total={board.productDetails.scopeAmount} compact={compact} /> },
  ], [board.productDetails.scopeAmount, compact, productRankOffset, productSort, selectSupplier, selectedSupplier?.code])

  const handleProductTableChange = useCallback<NonNullable<TableProps<CompactSalesBoardProduct>['onChange']>>((_pagination, _filters, sorter, extra) => {
    if (extra.action !== 'sort') return
    const current = Array.isArray(sorter) ? sorter[0] : sorter
    // 中文注释：商品明细分页展示，排序必须交给服务端对全部结果排序，不能只排当前页。
    setProductSort(resolveProductSort(current?.columnKey, current?.order))
    setPageIndex(1)
  }, [])

  const selectableRow = (selection: BoardSelection, selected: boolean, onSelect: (next: BoardSelection) => void) => ({
    role: 'button',
    tabIndex: 0,
    'aria-pressed': selected,
    onClick: () => onSelect(selection),
    onKeyDown: (event: React.KeyboardEvent) => {
      if (isKeyboardSelection(event)) {
        event.preventDefault()
        onSelect(selection)
      }
    },
  })
  const rowClassName = (selected: boolean) => [styles.clickableRow, selected ? styles.selectedRow : ''].filter(Boolean).join(' ')

  const summary = board.summary
  const statisticFresh = !board.statisticStatus || board.statisticStatus === 'Fresh'
  const emptyText = (panelLoading: boolean) => {
    if (isFirstLoad || panelLoading) return <div className={styles.skeleton} aria-label="加载中">{Array.from({ length: 8 }, (_, index) => <span key={index} />)}</div>
    if (!statisticFresh) return <div className={styles.empty}>{board.statisticMessage || '商品统计尚未就绪'}</div>
    return (
      <div className={styles.empty}>
        <p>{isFilterActive ? '当前筛选组合下没有销售记录' : '当前日期范围没有国内商品销售'}</p>
        {isFilterActive && <Button size="small" onClick={clearFilters}>清除筛选</Button>}
      </div>
    )
  }
  const [rangeStart, rangeEnd] = pageRange(board.productDetails.pageIndex, board.productDetails.pageSize, board.productDetails.total)
  const statisticTime = formatStatisticTime(board.statisticUpdatedAt)
  const branchHint = selectedProduct ? '该商品在各分店的销售' : selectedSupplier ? '该供应商在各分店的销售' : ''

  const kpi = (label: string, value: string, sub: React.ReactNode, main = false) => (
    <div className={[styles.kpi, main ? styles.kpiMain : ''].filter(Boolean).join(' ')}>
      <span>{label}</span>
      <strong className={isFirstLoad ? styles.kpiSkeleton : undefined}>{isFirstLoad ? '' : value}</strong>
      <em>{sub}</em>
    </div>
  )

  return (
    <div ref={pageRef} className={styles.page} aria-busy={loading}>
      <header className={styles.boardHead}>
        <div className={styles.toolbar}>
          <div className={styles.titleBlock}>
            <h1>销售看板</h1>
            <span className={styles.scope}><b>口径</b>澳洲供应商 200-hotbargain · 已映射国内供应商的商品</span>
          </div>
          <div className={styles.toolbarControls}>
            <Segmented size="small" value={quickRange ?? undefined} options={quickRangeOptions} onChange={(value) => { const nextRange = value as QuickRange; setQuickRange(nextRange); setDateRange(resolveQuickRange(nextRange)); setPageIndex(1) }} />
            <RangePicker size="small" value={dateRange} allowClear={false} disabledDate={isDisabledDate} onChange={handleRangeChange} />
            <Tooltip title="强制刷新（绕过缓存）">
              <Button size="small" type="primary" icon={<ReloadOutlined />} aria-label="强制刷新销售看板" loading={loading && cacheState === 'refreshing'} onClick={forceRefresh} />
            </Tooltip>
          </div>
        </div>
        <div className={[styles.kpis, summaryStale ? styles.kpisStale : ''].filter(Boolean).join(' ')} aria-live="polite">
          {kpi('国内营业额', formatCurrency(summary.totalAmount), isFilterActive
            ? <span className={styles.kpiHighlight}>占全部 {formatShare(summary.totalAmount, summary.overallAmount)} · 全部 {formatCurrency(summary.overallAmount)}</span>
            : '全部分店 · 全部供应商', true)}
          {kpi('销量', formatInteger(summary.totalQuantity), '件')}
          {kpi('动销商品', formatInteger(summary.productCount), '款')}
          {kpi('平均单价', formatPrice(summary.totalQuantity > 0 ? summary.totalAmount / summary.totalQuantity : 0), '营业额 ÷ 销量')}
          {kpi('分店', formatInteger(summary.storeCount), '有销售')}
          {kpi('国内供应商', formatInteger(summary.supplierCount), '有销售')}
          <div className={styles.status}>
            {loadError
              ? <span className={styles.statusError}>加载失败</span>
              : isFirstLoad
                ? <span className={styles.statusMuted}>加载中…</span>
                : statisticFresh
                ? board.statisticMessage
                  // 中文注释：对账未通过、历史缺口等只提示不阻断（与销售明细一致）；全文放在提示框里，不挤占三栏高度。
                  ? <Tooltip title={board.statisticMessage}><span className={styles.statusWarn} tabIndex={0}><i />统计已更新{statisticTime ? ` · 截至 ${statisticTime}` : ''} · 含提示</span></Tooltip>
                  : <span className={styles.statusOk}><i />统计已更新{statisticTime ? ` · 截至 ${statisticTime}` : ''}</span>
                : <span className={styles.statusWarn}><i />{board.statisticMessage || board.statisticStatus}</span>}
            {!loadError && (
              <small>
                {cacheState === 'cached'
                  ? '本地缓存'
                  : loading
                    ? (cacheState === 'refreshing' ? '强制刷新中…' : '查询中…')
                    : queryMs !== null ? <>本次查询 <b>{queryMs} ms</b></> : null}
                {!loading && cacheState !== 'cached' && queryMs !== null && <span className={styles.badge}>{board.fromCache ? '命中缓存' : '实时聚合'}</span>}
              </small>
            )}
          </div>
        </div>
        <div className={styles.filterBar} aria-label="联动筛选">
          <span className={styles.filterLabel}>联动筛选</span>
          {isFilterActive ? (
            <>
              {selectedBranch && (
                <span className={`${styles.chip} ${styles.chipBranch}`}>
                  <i /><span>分店</span><b>{selectedBranch.label}</b><small>{selectedBranch.detail}</small>
                  <button type="button" aria-label="移除分店筛选" onClick={() => { setSelectedBranch(null); setPageIndex(1) }}><CloseOutlined /></button>
                </span>
              )}
              {selectedSupplier && (
                <span className={`${styles.chip} ${styles.chipSupplier}`}>
                  <i /><span>国内供应商</span><b>{selectedSupplier.label}</b><small>{selectedSupplier.detail}</small>
                  <button type="button" aria-label="移除国内供应商筛选" onClick={() => { setSelectedSupplier(null); setPageIndex(1) }}><CloseOutlined /></button>
                </span>
              )}
              {selectedProduct && (
                <span className={`${styles.chip} ${styles.chipProduct}`}>
                  <i /><span>商品</span><b>{selectedProduct.label}</b><small>{selectedProduct.detail}</small>
                  <button type="button" aria-label="移除商品筛选" onClick={() => setSelectedProduct(null)}><CloseOutlined /></button>
                </span>
              )}
              <Button type="link" size="small" aria-label="清除筛选" onClick={clearFilters}>清除全部</Button>
              <kbd className={styles.kbd}>Esc</kbd>
            </>
          ) : (
            <span className={styles.filterHint}>点击任意分店、供应商或商品行即可联动筛选，再点一次取消；各栏不会被自身的选中项收窄。</span>
          )}
        </div>
      </header>

      {loadError && <Alert className={styles.loadError} type="error" showIcon message={loadError} action={<Button size="small" onClick={() => setReloadKey((key) => key + 1)}>重试</Button>} />}

      <div className={styles.grid}>
        <section className={[styles.panel, styles.panelBranch, storesStale ? styles.panelLoading : ''].filter(Boolean).join(' ')} aria-label="分店销售" aria-busy={storesStale}>
          <div className={styles.progress} aria-hidden="true" />
          <div className={styles.panelHeader}>
            <div className={styles.panelTitle}><i /><h2>分店销售</h2><span><b>{board.stores.length}</b> 家</span></div>
            {branchHint && <span className={styles.panelHint}>{branchHint}</span>}
          </div>
          <MeasuredTable
            metricId="compact-sales-board.stores"
            rowKey="branchCode"
            size="small"
            columns={branchColumns}
            dataSource={board.stores}
            pagination={false}
            showSorterTooltip={false}
            locale={{ emptyText: emptyText(storesStale) }}
            scroll={{ y: tableBodyHeight }}
            rowClassName={(record) => rowClassName(record.branchCode === selectedBranch?.code)}
            onRow={(record) => selectableRow({ code: record.branchCode, label: record.branchName || record.branchCode, detail: record.branchCode }, record.branchCode === selectedBranch?.code, selectBranch)}
          />
        </section>

        <section className={[styles.panel, styles.panelSupplier, suppliersStale ? styles.panelLoading : ''].filter(Boolean).join(' ')} aria-label="国内供应商销售" aria-busy={suppliersStale}>
          <div className={styles.progress} aria-hidden="true" />
          <div className={styles.panelHeader}>
            <div className={styles.panelTitle}><i /><h2>国内供应商</h2><span><b>{visibleSuppliers.length}</b> 个</span></div>
            <Input size="small" allowClear className={styles.supplierSearch} prefix={<SearchOutlined />} placeholder="名称 / 代码" aria-label="搜索国内供应商" value={supplierSearch} onChange={(event) => setSupplierSearch(event.target.value)} />
          </div>
          <MeasuredTable
            metricId="compact-sales-board.suppliers"
            rowKey="supplierCode"
            size="small"
            columns={supplierColumns}
            dataSource={visibleSuppliers}
            pagination={false}
            showSorterTooltip={false}
            locale={{ emptyText: emptyText(suppliersStale) }}
            scroll={{ y: tableBodyHeight }}
            rowClassName={(record) => rowClassName(record.supplierCode === selectedSupplier?.code)}
            onRow={(record) => selectableRow({ code: record.supplierCode, label: record.supplierName || record.supplierCode, detail: record.supplierCode }, record.supplierCode === selectedSupplier?.code, selectSupplier)}
          />
        </section>

        <section className={[styles.panel, styles.panelProduct, productsStale ? styles.panelLoading : ''].filter(Boolean).join(' ')} aria-label="国内商品明细" aria-busy={productsStale}>
          <div className={styles.progress} aria-hidden="true" />
          <div className={styles.panelHeader}>
            <div className={styles.panelTitle}><i /><h2>国内商品明细</h2><span><b>{formatInteger(board.productDetails.total)}</b> 款</span></div>
            <Input
              size="small"
              allowClear
              className={styles.productSearch}
              prefix={<SearchOutlined />}
              placeholder="货号 / 名称，空格分隔多个词"
              aria-label="搜索商品"
              value={keywordInput}
              onChange={(event) => setKeywordInput(event.target.value)}
              onCompositionStart={() => { composingRef.current = true }}
              onCompositionEnd={(event) => { composingRef.current = false; setKeywordInput(event.currentTarget.value); setCompositionTick((tick) => tick + 1) }}
            />
          </div>
          <MeasuredTable
            metricId="compact-sales-board.products"
            rowKey="productCode"
            size="small"
            columns={productColumns}
            dataSource={board.productDetails.data}
            pagination={false}
            showSorterTooltip={false}
            locale={{ emptyText: emptyText(productsStale) }}
            scroll={{ x: 560, y: productTableBodyHeight }}
            onChange={handleProductTableChange}
            rowClassName={(record) => rowClassName(record.productCode === selectedProduct?.code)}
            onRow={(record) => selectableRow({
              code: record.productCode,
              label: record.itemNumber || record.productCode,
              detail: record.productName,
              supplierCode: record.chinaSupplierCode,
            }, record.productCode === selectedProduct?.code, selectProduct)}
          />
          <div className={styles.pager}>
            <span className={styles.pagerMeta}>
              第 <b>{rangeStart}–{rangeEnd}</b> 条 / 共 {formatInteger(board.productDetails.total)} 条 · {describeProductSort(productSort)}（全部结果排序）
            </span>
            <Pagination size="small" current={pageIndex} pageSize={pageSize} total={board.productDetails.total} showSizeChanger pageSizeOptions={pageSizeOptions} onChange={(page, size) => { setPageIndex(size !== pageSize ? 1 : page); setPageSize(size) }} />
          </div>
        </section>
      </div>
    </div>
  )
}

export default CompactSalesBoardPage
