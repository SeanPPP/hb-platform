import { ClearOutlined, ReloadOutlined } from '@ant-design/icons'
import { Alert, Button, DatePicker, Image, message, Pagination, Segmented, Space, Tag } from 'antd'
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
import { readCompactBoardCache, writeCompactBoardCache, type CompactBoardCacheEntry } from './cache'

const { RangePicker } = DatePicker

type QuickRange = 'today' | 'yesterday' | 'thisWeek' | 'thisMonth'
type CacheState = 'cached' | 'fresh' | 'refreshing' | 'error'

const quickRangeOptions: { label: string; value: QuickRange }[] = [
  { label: '今天', value: 'today' },
  { label: '昨天', value: 'yesterday' },
  { label: '本周', value: 'thisWeek' },
  { label: '本月', value: 'thisMonth' },
]

const compactCurrencyFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 })
const priceFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const compactBoardClientCacheMs = 30_000
const maxSalesDateRangeDays = 366

const emptyBoard: CompactSalesBoard = {
  stores: [],
  chinaSuppliers: [],
  productDetails: { data: [], total: 0, pageIndex: 1, pageSize: 80 },
}

function resolveQuickRange(range: QuickRange): [Dayjs, Dayjs] {
  if (range === 'yesterday') {
    const date = dayjs().subtract(1, 'day')
    return [date.startOf('day'), date.endOf('day')]
  }
  if (range === 'thisWeek') return [dayjs().startOf('week'), dayjs().endOf('week')]
  if (range === 'thisMonth') return [dayjs().startOf('month'), dayjs().endOf('month')]
  return [dayjs().startOf('day'), dayjs().endOf('day')]
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

function isKeyboardSelection(event: React.KeyboardEvent) {
  return event.key === 'Enter' || event.key === ' '
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
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [cacheState, setCacheState] = useState<CacheState>('fresh')
  const [selectedBranch, setSelectedBranch] = useState<string | null>(null)
  const [selectedSupplier, setSelectedSupplier] = useState<string | null>(null)
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null)
  const [pageIndex, setPageIndex] = useState(1)
  const [pageSize, setPageSize] = useState(80)
  const [reloadKey, setReloadKey] = useState(0)
  const forceRefreshRef = useRef(false)
  const boardRequestAbortRef = useRef<AbortController | null>(null)
  const boardCacheRef = useRef(new Map<string, CompactBoardCacheEntry<CompactSalesBoard>>())

  const dateRangeParams = useMemo(() => toDateRange(dateRange), [dateRange])
  const selectedBranchCodes = useMemo(() => selectedBranch ? [selectedBranch] : managedStoreCodes, [managedStoreCodes, selectedBranch])

  const loadBoard = useCallback(async (signal?: AbortSignal) => {
    const forceRefresh = forceRefreshRef.current
    forceRefreshRef.current = false
    const requestKey = JSON.stringify({ dateRangeParams, selectedBranchCodes: selectedBranchCodes ?? null, selectedSupplier, selectedProduct, pageIndex, pageSize })
    const cachedBoard = !forceRefresh
      ? readCompactBoardCache(boardCacheRef.current, requestKey)
      : undefined
    if (cachedBoard) {
      setBoard(cachedBoard)
      setLoadError(null)
      setCacheState('cached')
      setLoading(false)
      return
    }

    setLoading(true)
    setLoadError(null)
    setCacheState(forceRefresh ? 'refreshing' : 'fresh')
    try {
      const result = await getCompactSalesBoard(
        dateRangeParams,
        selectedBranchCodes,
        selectedSupplier ? [selectedSupplier] : undefined,
        selectedProduct ?? undefined,
        pageIndex,
        pageSize,
        signal,
        forceRefresh,
      )
      if (!signal?.aborted) {
        setBoard(result)
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
        setLoadError('销售看板数据加载失败，请重试。')
        setCacheState('error')
        message.error('销售看板数据加载失败')
      }
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }, [dateRangeParams, pageIndex, pageSize, selectedBranchCodes, selectedProduct, selectedSupplier])

  useEffect(() => {
    boardRequestAbortRef.current?.abort()
    if (!active) {
      setLoading(false)
      return
    }

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

  const selectValue = useCallback((setter: React.Dispatch<React.SetStateAction<string | null>>, value: string) => {
    setter((current) => current === value ? null : value)
    setPageIndex(1)
  }, [])

  const clearFilters = useCallback(() => {
    setSelectedBranch(null)
    setSelectedSupplier(null)
    setSelectedProduct(null)
    setPageIndex(1)
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

  const branchColumns = useMemo<ColumnsType<CompactSalesBoardStore>>(() => [
    { title: '分店', dataIndex: 'branchName', key: 'branchName', width: 108, fixed: 'left', render: (_, record) => <div className={styles.primaryCell}><span>{record.branchName || record.branchCode}</span><small>{record.branchCode}</small></div> },
    { title: '国内营业额', dataIndex: 'totalAmount', key: 'totalAmount', align: 'right', width: 92, render: formatCurrency },
    { title: '数量', dataIndex: 'totalQuantity', key: 'totalQuantity', align: 'right', width: 72 },
    { title: '国内金额', dataIndex: 'domesticSupplierAmount', key: 'domesticSupplierAmount', align: 'right', width: 96, render: formatCurrency },
    { title: '澳洲供应商', dataIndex: 'australianSupplierName', key: 'australianSupplierName', width: 116, render: (value: string) => <Tag color="blue">{value || '200-hotbargain'}</Tag> },
  ], [])
  const supplierColumns = useMemo<ColumnsType<CompactSalesBoardChinaSupplier>>(() => [
    { title: '国内供应商', dataIndex: 'supplierName', key: 'supplierName', width: 140, fixed: 'left', render: (_, record) => <div className={styles.primaryCell}><span>{record.supplierName || record.supplierCode}</span><small>{record.supplierCode}</small></div> },
    { title: '营业额', dataIndex: 'totalAmount', key: 'totalAmount', align: 'right', width: 96, render: formatCurrency },
    { title: '数量', dataIndex: 'totalQuantity', key: 'totalQuantity', align: 'right', width: 72 },
  ], [])
  const productColumns = useMemo<ColumnsType<CompactSalesBoardProduct>>(() => [
    { title: '图片', dataIndex: 'productImage', key: 'productImage', width: 54, render: (value: string | undefined, record) => <div className={styles.productImageBox}>{value ? <Image src={value} alt={record.productName ?? record.itemNumber ?? record.productCode} width={36} height={36} loading="lazy" preview={false} className={styles.productImage} fallback="" /> : <span />}</div> },
    { title: '货号', dataIndex: 'itemNumber', key: 'itemNumber', width: 118, fixed: 'left', render: (_, record) => <div className={styles.primaryCell}><span>{record.itemNumber || record.productCode}</span><small>{record.productName || record.productCode}</small></div> },
    { title: '数量', dataIndex: 'totalQuantity', key: 'totalQuantity', align: 'right', width: 68 },
    { title: '单价', dataIndex: 'unitPrice', key: 'unitPrice', align: 'right', width: 82, render: formatPrice },
    { title: '金额', dataIndex: 'totalAmount', key: 'totalAmount', align: 'right', width: 96, render: formatCurrency },
  ], [])

  const selectableRow = (value: string, selected: boolean, setter: React.Dispatch<React.SetStateAction<string | null>>, disabled: boolean) => ({
    role: 'button',
    tabIndex: disabled ? -1 : 0,
    'aria-pressed': selected,
    'aria-disabled': disabled,
    ...(!disabled ? {
      onClick: () => selectValue(setter, value),
      onKeyDown: (event: React.KeyboardEvent) => {
        if (isKeyboardSelection(event)) {
          event.preventDefault()
          selectValue(setter, value)
        }
      },
    } : {}),
  })

  const isFilterActive = Boolean(selectedBranch || selectedSupplier || selectedProduct)
  const tableRowClass = (selected: boolean) => [
    loading ? styles.disabledRow : styles.clickableRow,
    selected ? styles.selectedRow : '',
  ].filter(Boolean).join(' ')
  return (
    <div className={styles.page} aria-busy={loading}>
      <div className={styles.toolbar}>
        <div className={styles.titleBlock}><h1>销售看板</h1><span>{dateRangeParams.startDate} ~ {dateRangeParams.endDate}</span></div>
        <Space className={styles.toolbarControls} wrap size={8}>
          <Segmented size="small" value={quickRange ?? undefined} options={quickRangeOptions} disabled={loading} onChange={(value) => { const nextRange = value as QuickRange; setQuickRange(nextRange); setDateRange(resolveQuickRange(nextRange)); setPageIndex(1) }} />
          <RangePicker size="small" value={dateRange} allowClear={false} disabled={loading} onChange={handleRangeChange} />
          <Button size="small" icon={<ClearOutlined />} aria-label="清除筛选" onClick={clearFilters} disabled={loading || !isFilterActive} />
          <Button size="small" type="primary" icon={<ReloadOutlined />} aria-label="强制刷新销售看板" loading={loading && cacheState === 'refreshing'} disabled={loading} onClick={() => { forceRefreshRef.current = true; setReloadKey((key) => key + 1) }} />
        </Space>
      </div>
      {loadError && <Alert className={styles.loadError} type="error" showIcon message={loadError} action={<Button size="small" disabled={loading} onClick={() => setReloadKey((key) => key + 1)}>重试</Button>} />}
      <div className={styles.filterBar} aria-label="当前筛选"><Tag color={selectedBranch ? 'blue' : 'default'}>分店：{selectedBranch || '全部'}</Tag><Tag color={selectedSupplier ? 'purple' : 'default'}>国内供应商：{selectedSupplier || '全部'}</Tag><Tag color={selectedProduct ? 'green' : 'default'}>商品：{selectedProduct || '全部'}</Tag>{!loadError && <Tag color={cacheState === 'cached' ? 'default' : 'blue'}>{cacheState === 'cached' ? '本地缓存' : cacheState === 'refreshing' ? '强制刷新中' : '最新查询'}</Tag>}{board.statisticStatus && <Tag color={board.statisticStatus === 'Fresh' ? 'green' : 'orange'}>{board.statisticStatus}</Tag>}{board.statisticMessage && <span className={styles.statusText}>{board.statisticMessage}</span>}</div>
      <div className={styles.grid}>
        <section className={styles.panel} aria-label="分店销售"><div className={styles.panelHeader}><h2>分店销售</h2><span>{board.stores.length} 家</span></div><MeasuredTable metricId="compact-sales-board.stores" rowKey="branchCode" size="small" loading={loading} columns={branchColumns} dataSource={board.stores} pagination={false} scroll={{ x: 484, y: 'calc(100vh - 334px)' }} rowClassName={(record) => tableRowClass(record.branchCode === selectedBranch)} onRow={(record) => selectableRow(record.branchCode, record.branchCode === selectedBranch, setSelectedBranch, loading)} /></section>
        <section className={styles.panel} aria-label="国内供应商销售"><div className={styles.panelHeader}><h2>国内供应商销售</h2><span>{board.chinaSuppliers.length} 个</span></div><MeasuredTable metricId="compact-sales-board.suppliers" rowKey="supplierCode" size="small" loading={loading} columns={supplierColumns} dataSource={board.chinaSuppliers} pagination={false} scroll={{ x: 320, y: 'calc(100vh - 334px)' }} rowClassName={(record) => tableRowClass(record.supplierCode === selectedSupplier)} onRow={(record) => selectableRow(record.supplierCode, record.supplierCode === selectedSupplier, setSelectedSupplier, loading)} /></section>
        <section className={styles.panel} aria-label="国内商品明细"><div className={styles.panelHeader}><h2>国内商品明细</h2><span>{board.productDetails.total} 条</span></div><MeasuredTable metricId="compact-sales-board.products" rowKey="productCode" size="small" loading={loading} columns={productColumns} dataSource={board.productDetails.data} pagination={false} scroll={{ x: 418, y: 'calc(100vh - 384px)' }} rowClassName={(record) => tableRowClass(record.productCode === selectedProduct)} onRow={(record) => selectableRow(record.productCode, record.productCode === selectedProduct, setSelectedProduct, loading)} /><Pagination size="small" className={styles.pagination} current={pageIndex} pageSize={pageSize} total={board.productDetails.total} showSizeChanger disabled={loading} pageSizeOptions={[50, 80, 120, 200]} onChange={(page, size) => { setPageIndex(page); setPageSize(size) }} /></section>
      </div>
    </div>
  )
}

export default CompactSalesBoardPage
