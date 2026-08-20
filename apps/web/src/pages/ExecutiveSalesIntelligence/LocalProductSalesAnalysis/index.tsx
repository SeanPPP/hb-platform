import { ClearOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, DatePicker, Empty, Input, Pagination, Select, Space, Spin, Table, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import PageContainer from '../../../components/PageContainer'
import FlowTrendChart from '../ProductFlowShared/FlowTrendChart'
import ProductImage from '../ProductFlowShared/ProductImage'
import {
  getLocalSupplierProductSalesAnalysisOptions,
  queryLocalSupplierProductSalesAnalysisBranchDaily,
  queryLocalSupplierProductSalesAnalysisBranches,
  queryLocalSupplierProductSalesAnalysisCandidates,
  queryLocalSupplierProductSalesAnalysisInvoiceDetails,
  queryLocalSupplierProductSalesAnalysisProductDaily,
  queryLocalSupplierProductSalesAnalysisSummary,
} from '../../../services/localSupplierProductSalesAnalysisService'
import type {
  LocalSupplierProductSalesAnalysisBranch,
  LocalSupplierProductSalesAnalysisCandidate,
  LocalSupplierProductSalesAnalysisDaily,
  LocalSupplierProductSalesAnalysisFilter,
  LocalSupplierProductSalesAnalysisInvoiceDetail,
  LocalSupplierProductSalesAnalysisOptions,
  LocalSupplierProductSalesAnalysisPaged,
  LocalSupplierProductSalesAnalysisSelection,
  LocalSupplierProductSalesAnalysisSummary,
} from '../../../types/localSupplierProductSalesAnalysis'
import {
  applyCandidateSelection,
  buildBrisbaneDefaultRange,
  canSetCurrentProduct,
  createIncludedSelection,
  createLatestRequestGuard,
  formatAud,
  getDateRangeError,
  getCurrentProductAfterCancellation,
  isSelected,
  toFlowTrendData,
} from './logic'
import styles from './index.module.css'

const { RangePicker } = DatePicker
const quantityFormatter = new Intl.NumberFormat('en-AU')

function formatQuantity(value: number) { return quantityFormatter.format(value) }
function errorText(error: unknown, fallback: string) { return error instanceof Error && error.message ? error.message : fallback }
function aborted(error: unknown) { return error instanceof Error && error.name === 'AbortError' }
function requestFilter(range: [Dayjs, Dayjs], keyword: string, categoryGuid?: string, supplierCode?: string, documentKeyword?: string): LocalSupplierProductSalesAnalysisFilter {
  return { startDate: range[0].format('YYYY-MM-DD'), endDate: range[1].format('YYYY-MM-DD'), keyword: keyword.trim() || undefined, categoryGuid, supplierCode, documentKeyword: documentKeyword?.trim() || undefined }
}
function hasSelection(selection: LocalSupplierProductSalesAnalysisSelection) { return selection.mode === 'allFiltered' || selection.includedProductCodes.length > 0 }

function PanelState({ loading, error, empty, retry, children }: { loading: boolean; error?: string; empty?: boolean; retry: () => void; children: ReactNode }) {
  if (loading) return <div className={styles.state}><Spin /></div>
  if (error) return <Alert type="error" showIcon message="加载失败" description={error} action={<Button size="small" onClick={retry}>重试</Button>} />
  if (empty) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无数据" />
  return <>{children}</>
}

function DailyTrend({ data, label }: { data: LocalSupplierProductSalesAnalysisDaily[]; label: string }) {
  if (!data.length) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无趋势数据" />
  return <FlowTrendChart data={toFlowTrendData(data)} ariaLabel={label} />
}

function Totals({ summary }: { summary: LocalSupplierProductSalesAnalysisSummary | null }) {
  const totals = summary?.totals
  const values: Array<[string, number | null | undefined, (value: number | null | undefined) => string]> = [
    ['本地进货量', totals?.purchaseQuantity, (value) => formatQuantity(value ?? 0)],
    ['进货额', totals?.purchaseAmount, formatAud],
    ['分店净销量', totals?.netSalesQuantity, (value) => formatQuantity(value ?? 0)],
    ['净销售额', totals?.netSalesAmount, formatAud],
    ['售进比', totals?.sellThroughRate, (value) => value === null || value === undefined ? '—' : `${value.toFixed(1)}%`],
  ]
  return <div className={`${styles.totals} ${styles.topTotals}`}>{values.map(([label, value, render]) => <div key={label}><span>{label}</span><strong>{render(value)}</strong></div>)}</div>
}

export default function LocalProductSalesAnalysisPage() {
  const defaultRange = useMemo(() => {
    const range = buildBrisbaneDefaultRange(30)
    return [dayjs(range.startDate), dayjs(range.endDate)] as [Dayjs, Dayjs]
  }, [])
  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(defaultRange)
  const [draftKeyword, setDraftKeyword] = useState('')
  const [draftCategoryGuid, setDraftCategoryGuid] = useState<string>()
  const [draftSupplierCode, setDraftSupplierCode] = useState<string>()
  const [draftDocumentKeyword, setDraftDocumentKeyword] = useState('')
  const [quickDays, setQuickDays] = useState<number | null>(30)
  const [filter, setFilter] = useState(() => requestFilter(defaultRange, ''))
  const [selection, setSelection] = useState<LocalSupplierProductSalesAnalysisSelection>(() => createIncludedSelection())
  const selectionRef = useRef(selection)
  const [currentProduct, setCurrentProduct] = useState<LocalSupplierProductSalesAnalysisCandidate | null>(null)
  const currentProductRef = useRef(currentProduct)
  const [candidatePage, setCandidatePage] = useState(1)
  const [candidatePageSize, setCandidatePageSize] = useState(20)
  const [candidates, setCandidates] = useState<LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisCandidate> | null>(null)
  const [summary, setSummary] = useState<LocalSupplierProductSalesAnalysisSummary | null>(null)
  const [candidateLoading, setCandidateLoading] = useState(true)
  const [summaryLoading, setSummaryLoading] = useState(false)
  const [candidateError, setCandidateError] = useState<string>()
  const [summaryError, setSummaryError] = useState<string>()
  const [options, setOptions] = useState<LocalSupplierProductSalesAnalysisOptions>({ warehouseCategories: [], suppliers: [] })
  const [optionsError, setOptionsError] = useState<string>()
  const [details, setDetails] = useState<LocalSupplierProductSalesAnalysisPaged<LocalSupplierProductSalesAnalysisInvoiceDetail> | null>(null)
  const [daily, setDaily] = useState<LocalSupplierProductSalesAnalysisDaily[]>([])
  const [branches, setBranches] = useState<LocalSupplierProductSalesAnalysisBranch[]>([])
  const [branchDaily, setBranchDaily] = useState<LocalSupplierProductSalesAnalysisDaily[]>([])
  const [selectedBranchCode, setSelectedBranchCode] = useState<string>()
  const [detailLoading, setDetailLoading] = useState(false)
  const [dailyLoading, setDailyLoading] = useState(false)
  const [branchLoading, setBranchLoading] = useState(false)
  const [branchDailyLoading, setBranchDailyLoading] = useState(false)
  const [detailError, setDetailError] = useState<string>()
  const [dailyError, setDailyError] = useState<string>()
  const [branchError, setBranchError] = useState<string>()
  const [branchDailyError, setBranchDailyError] = useState<string>()
  const [refreshVersion, setRefreshVersion] = useState(0)
  const migrateCurrentOnSummaryRef = useRef(false)
  const shouldSelectFirstCandidateRef = useRef(true)

  const optionsGuardRef = useRef(createLatestRequestGuard())
  const candidateGuardRef = useRef(createLatestRequestGuard())
  const selectedSummaryGuardRef = useRef(createLatestRequestGuard())
  const detailGuardRef = useRef(createLatestRequestGuard())
  const dailyGuardRef = useRef(createLatestRequestGuard())
  const branchGuardRef = useRef(createLatestRequestGuard())
  const branchDailyGuardRef = useRef(createLatestRequestGuard())
  const optionsAbortRef = useRef<AbortController>()
  const candidateAbortRef = useRef<AbortController>()
  const summaryAbortRef = useRef<AbortController>()
  const detailAbortRef = useRef<AbortController>()
  const dailyAbortRef = useRef<AbortController>()
  const branchAbortRef = useRef<AbortController>()
  const branchDailyAbortRef = useRef<AbortController>()
  const forceRefreshPending = useRef(new Set<string>())
  const brisbaneYesterday = dayjs(buildBrisbaneDefaultRange(1).endDate)

  useEffect(() => { selectionRef.current = selection }, [selection])
  useEffect(() => { currentProductRef.current = currentProduct }, [currentProduct])
  useEffect(() => {
    branchDailyAbortRef.current?.abort(); branchDailyGuardRef.current.invalidate()
    setSelectedBranchCode(undefined); setBranchDaily([]); setBranchDailyError(undefined); setBranchDailyLoading(false)
  }, [currentProduct?.productCode])
  const consumeForceRefresh = (key: string) => {
    if (!forceRefreshPending.current.has(key)) return false
    forceRefreshPending.current.delete(key)
    return true
  }

  const invalidate = useCallback(() => {
    ;[
      [optionsAbortRef, optionsGuardRef], [candidateAbortRef, candidateGuardRef], [summaryAbortRef, selectedSummaryGuardRef],
      [detailAbortRef, detailGuardRef], [dailyAbortRef, dailyGuardRef], [branchAbortRef, branchGuardRef], [branchDailyAbortRef, branchDailyGuardRef],
    ].forEach(([controller, guard]) => {
      ;(controller as typeof optionsAbortRef).current?.abort()
      ;(guard as typeof optionsGuardRef).current.invalidate()
    })
  }, [])

  // 筛选上下文切换必须同步清空所有下游数据、错误和加载态，旧请求已由 guard 作废。
  const clearAnalysisState = useCallback(() => {
    migrateCurrentOnSummaryRef.current = false
    const emptySelection = createIncludedSelection()
    selectionRef.current = emptySelection
    currentProductRef.current = null
    setSelection(emptySelection)
    setCurrentProduct(null)
    setSummary(null); setDetails(null); setDaily([]); setBranches([]); setBranchDaily([]); setSelectedBranchCode(undefined)
    setCandidateError(undefined); setSummaryError(undefined); setDetailError(undefined); setDailyError(undefined); setBranchError(undefined); setBranchDailyError(undefined)
    setCandidateLoading(false); setSummaryLoading(false); setDetailLoading(false); setDailyLoading(false); setBranchLoading(false); setBranchDailyLoading(false)
  }, [])

  const applyFilters = useCallback(() => {
    const rangeError = getDateRangeError(draftRange[0].format('YYYY-MM-DD'), draftRange[1].format('YYYY-MM-DD'), brisbaneYesterday.format('YYYY-MM-DD'))
    if (rangeError) { setCandidateLoading(false); setCandidateError(rangeError); return }
    invalidate()
    shouldSelectFirstCandidateRef.current = true
    setFilter(requestFilter(draftRange, draftKeyword, draftCategoryGuid, draftSupplierCode, draftDocumentKeyword))
    clearAnalysisState()
    setCandidates(null)
    setCandidatePage(1)
  }, [brisbaneYesterday, clearAnalysisState, draftCategoryGuid, draftDocumentKeyword, draftKeyword, draftRange, draftSupplierCode, invalidate])

  const resetFilters = useCallback(() => {
    const range = (() => { const result = buildBrisbaneDefaultRange(30); return [dayjs(result.startDate), dayjs(result.endDate)] as [Dayjs, Dayjs] })()
    setDraftRange(range); setDraftKeyword(''); setDraftCategoryGuid(undefined); setDraftSupplierCode(undefined); setDraftDocumentKeyword(''); setQuickDays(30)
    invalidate(); shouldSelectFirstCandidateRef.current = true; clearAnalysisState(); setFilter(requestFilter(range, '')); setCandidatePage(1); setCandidates(null)
  }, [clearAnalysisState, invalidate])

  const setRangeDays = (days: number) => {
    const result = buildBrisbaneDefaultRange(days)
    setDraftRange([dayjs(result.startDate), dayjs(result.endDate)])
    setQuickDays(days)
  }

  useEffect(() => {
    const controller = new AbortController(); optionsAbortRef.current?.abort(); optionsAbortRef.current = controller
    const token = optionsGuardRef.current.next(); setOptionsError(undefined)
    getLocalSupplierProductSalesAnalysisOptions(controller.signal).then((result) => {
      if (!optionsGuardRef.current.isCurrent(token)) return
      setOptions(result.data)
    }).catch((error) => { if (!aborted(error) && optionsGuardRef.current.isCurrent(token)) setOptionsError(errorText(error, '选项加载失败')) })
    return () => controller.abort()
  }, [refreshVersion])

  const loadCandidates = useCallback(() => {
    candidateAbortRef.current?.abort(); const controller = new AbortController(); candidateAbortRef.current = controller
    const token = candidateGuardRef.current.next(); setCandidateLoading(true); setCandidateError(undefined)
    queryLocalSupplierProductSalesAnalysisCandidates({ filter, selection: selectionRef.current, pageNumber: candidatePage, pageSize: candidatePageSize, forceRefresh: consumeForceRefresh('candidates') }, controller.signal).then((result) => {
      if (!candidateGuardRef.current.isCurrent(token)) return
      setCandidates(result.data)
      // 首次或每次新筛选都由最新候选响应显式选中第一项，避免隐式全选。
      if (shouldSelectFirstCandidateRef.current && result.data.items[0]) {
        shouldSelectFirstCandidateRef.current = false
        const first = result.data.items[0]
        const next = createIncludedSelection([first.productCode])
        selectionRef.current = next
        setSelection(next)
        setCurrentProduct(first)
      }
    }).catch((error) => { if (!aborted(error) && candidateGuardRef.current.isCurrent(token)) setCandidateError(errorText(error, '商品加载失败')) }).finally(() => {
      if (candidateGuardRef.current.isCurrent(token)) setCandidateLoading(false)
    })
  }, [candidatePage, candidatePageSize, filter, refreshVersion])
  useEffect(() => { loadCandidates() }, [loadCandidates])

  const loadSummary = useCallback(() => {
    if (!hasSelection(selection)) { setSummary(null); setSummaryLoading(false); return }
    summaryAbortRef.current?.abort(); const controller = new AbortController(); summaryAbortRef.current = controller
    const token = selectedSummaryGuardRef.current.next(); setSummaryLoading(true); setSummaryError(undefined)
    queryLocalSupplierProductSalesAnalysisSummary({ filter, selection, pageNumber: 1, pageSize: 50, forceRefresh: consumeForceRefresh('summary') }, controller.signal).then((result) => {
      if (!selectedSummaryGuardRef.current.isCurrent(token)) return
      setSummary(result.data)
      if (migrateCurrentOnSummaryRef.current || !currentProductRef.current) {
        migrateCurrentOnSummaryRef.current = false
        setCurrentProduct(getCurrentProductAfterCancellation(currentProductRef.current, result.data.items, true))
      }
    }).catch((error) => { if (!aborted(error) && selectedSummaryGuardRef.current.isCurrent(token)) setSummaryError(errorText(error, '汇总加载失败')) }).finally(() => {
      if (selectedSummaryGuardRef.current.isCurrent(token)) setSummaryLoading(false)
    })
  }, [filter, refreshVersion, selection])
  useEffect(() => { loadSummary() }, [loadSummary])

  const loadCurrentProduct = useCallback(() => {
    if (!currentProduct || !hasSelection(selection)) { setDetails(null); setDaily([]); setBranches([]); setBranchDaily([]); setSelectedBranchCode(undefined); return }
    const body = { filter, selection, currentProductCode: currentProduct.productCode }
    const run = <T,>(guard: typeof detailGuardRef, abortRef: typeof detailAbortRef, start: (value: boolean) => void, fail: (value: string | undefined) => void, call: (signal: AbortSignal) => Promise<{ data: T }>, commit: (value: T) => void, fallback: string) => {
      abortRef.current?.abort(); const controller = new AbortController(); abortRef.current = controller; const token = guard.current.next(); start(true); fail(undefined)
      call(controller.signal).then((result) => { if (guard.current.isCurrent(token)) commit(result.data) }).catch((error) => { if (!aborted(error) && guard.current.isCurrent(token)) fail(errorText(error, fallback)) }).finally(() => { if (guard.current.isCurrent(token)) start(false) })
    }
    run(detailGuardRef, detailAbortRef, setDetailLoading, setDetailError, (signal) => queryLocalSupplierProductSalesAnalysisInvoiceDetails({ ...body, pageNumber: 1, pageSize: 50, forceRefresh: consumeForceRefresh('detail') }, signal), setDetails, '明细加载失败')
    run(dailyGuardRef, dailyAbortRef, setDailyLoading, setDailyError, (signal) => queryLocalSupplierProductSalesAnalysisProductDaily({ ...body, forceRefresh: consumeForceRefresh('daily') }, signal), setDaily, '趋势加载失败')
    run(branchGuardRef, branchAbortRef, setBranchLoading, setBranchError, (signal) => queryLocalSupplierProductSalesAnalysisBranches({ ...body, forceRefresh: consumeForceRefresh('branches') }, signal), setBranches, '分店加载失败')
  }, [currentProduct, filter, refreshVersion, selection])
  useEffect(() => { loadCurrentProduct() }, [loadCurrentProduct])

  const loadBranchDaily = useCallback((branchCode?: string) => {
    if (!currentProduct || !branchCode) { setBranchDaily([]); return }
    branchDailyAbortRef.current?.abort(); const controller = new AbortController(); branchDailyAbortRef.current = controller
    const token = branchDailyGuardRef.current.next(); setBranchDailyLoading(true); setBranchDailyError(undefined)
    queryLocalSupplierProductSalesAnalysisBranchDaily({ filter, selection, currentProductCode: currentProduct.productCode, branchCode, forceRefresh: consumeForceRefresh('branchDaily') }, controller.signal).then((result) => {
      if (branchDailyGuardRef.current.isCurrent(token)) setBranchDaily(result.data)
    }).catch((error) => { if (!aborted(error) && branchDailyGuardRef.current.isCurrent(token)) setBranchDailyError(errorText(error, '分店趋势加载失败')) }).finally(() => { if (branchDailyGuardRef.current.isCurrent(token)) setBranchDailyLoading(false) })
  }, [currentProduct, filter, refreshVersion, selection])
  useEffect(() => { loadBranchDaily(selectedBranchCode) }, [loadBranchDaily, selectedBranchCode])

  const updateCandidate = (candidate: LocalSupplierProductSalesAnalysisCandidate, checked: boolean) => {
    const next = applyCandidateSelection(selectionRef.current, candidate.productCode, checked)
    selectionRef.current = next; setSelection(next)
    if (!checked && currentProductRef.current?.productCode === candidate.productCode) {
      if (hasSelection(next)) migrateCurrentOnSummaryRef.current = true
      else setCurrentProduct(null)
    }
  }
  const selectAllFiltered = () => {
    const next = { mode: 'allFiltered' as const, includedProductCodes: [], excludedProductCodes: [] }
    selectionRef.current = next; setSelection(next)
  }
  const clearSelection = () => {
    shouldSelectFirstCandidateRef.current = false
    selectedSummaryGuardRef.current.invalidate(); detailGuardRef.current.invalidate(); dailyGuardRef.current.invalidate(); branchGuardRef.current.invalidate(); branchDailyGuardRef.current.invalidate()
    clearAnalysisState()
  }
  const refresh = () => {
    const keys = ['candidates']
    if (hasSelection(selectionRef.current)) keys.push('summary')
    if (currentProductRef.current && hasSelection(selectionRef.current)) keys.push('detail', 'daily', 'branches')
    if (currentProductRef.current && selectedBranchCode && hasSelection(selectionRef.current)) keys.push('branchDaily')
    forceRefreshPending.current = new Set(keys)
    setRefreshVersion((value) => value + 1)
  }
  const detailColumns: ColumnsType<LocalSupplierProductSalesAnalysisInvoiceDetail> = [
    { title: '单号', dataIndex: 'invoiceNo', width: 118, render: (value) => value || '—' }, { title: '分店', width: 130, render: (_, row) => row.storeName || row.storeCode || '—' },
    { title: '供应商', width: 140, render: (_, row) => row.supplierName || row.supplierCode || '—' }, { title: '日期', dataIndex: 'purchaseDate', width: 106, render: (value) => value || '—' },
    { title: '数量', dataIndex: 'quantity', align: 'right', width: 88, render: formatQuantity }, { title: '进货单价', dataIndex: 'purchasePrice', align: 'right', width: 108, render: formatAud }, { title: '金额', dataIndex: 'amount', align: 'right', width: 108, render: formatAud },
  ]
  const branchColumns: ColumnsType<LocalSupplierProductSalesAnalysisBranch> = [
    { title: '授权分店', render: (_, row) => <button type="button" className={styles.branchButton} onClick={() => setSelectedBranchCode(row.branchCode)}>{row.branchName || row.branchCode}</button> }, { title: '净销量', dataIndex: 'netSalesQuantity', align: 'right', render: formatQuantity }, { title: '均价', dataIndex: 'averageUnitPrice', align: 'right', render: formatAud },
  ]
  const currentName = currentProduct?.productName || currentProduct?.itemNumber || currentProduct?.productCode

  return <PageContainer title="澳洲本地商品分析">
    <Card className={styles.toolbar} bordered={false}>
      <Space wrap>
        <RangePicker value={draftRange} disabledDate={(date) => date.isAfter(brisbaneYesterday, 'day')} onChange={(value) => value?.[0] && value?.[1] && (setDraftRange([value[0], value[1]]), setQuickDays(null))} allowClear={false} />
        {[7, 30, 90].map((days) => <Button key={days} type={quickDays === days ? 'primary' : 'default'} onClick={() => setRangeDays(days)}>{days}天</Button>)}
        <Button icon={<SearchOutlined />} type="primary" onClick={applyFilters}>查询</Button>
        <Button icon={<ClearOutlined />} onClick={resetFilters}>重置</Button>
        <Button icon={<ReloadOutlined />} onClick={refresh}>刷新</Button>
      </Space>
    </Card>
    {optionsError ? <Alert className={styles.optionsAlert} type="warning" showIcon message="筛选选项加载失败" description={optionsError} action={<Button size="small" onClick={() => setRefreshVersion((value) => value + 1)}>重试</Button>} /> : null}
    <Card className={styles.summaryCard} bordered={false}>
      <PanelState loading={summaryLoading && !summary} error={summaryError} empty={!hasSelection(selection)} retry={loadSummary}><Totals summary={summary} /></PanelState>
    </Card>
    <div className={styles.layout}>
      <Card className={styles.panel} title="商品范围" bordered={false}>
        <div className={styles.filters}>
          <Input value={draftKeyword} onChange={(event) => setDraftKeyword(event.target.value)} placeholder="货号、中文/英文名称或条码" allowClear />
          <Select value={draftCategoryGuid} onChange={setDraftCategoryGuid} placeholder="仓库分类" allowClear options={options.warehouseCategories.map((item) => ({ value: item.guid, label: item.name || item.guid }))} notFoundContent="暂无可选分类" />
          <Select value={draftSupplierCode} onChange={setDraftSupplierCode} placeholder="澳洲本地供应商" allowClear options={options.suppliers.map((item) => ({ value: item.code, label: item.name ? `${item.name} (${item.code})` : item.code }))} notFoundContent="暂无可选供应商" />
          <Input value={draftDocumentKeyword} onChange={(event) => setDraftDocumentKeyword(event.target.value)} placeholder="本地进货单号" allowClear />
        </div>
        <div className={styles.selectionBar}><span>已选 {selection.mode === 'included' ? selection.includedProductCodes.length : '全部筛选结果'} 项</span><Space size={4}><Button type="link" size="small" onClick={selectAllFiltered}>全选筛选结果</Button><Button type="link" size="small" onClick={clearSelection}>清空选择</Button></Space></div>
        <PanelState loading={candidateLoading} error={candidateError} empty={!candidates?.items.length} retry={loadCandidates}>
          <div className={styles.candidates}>{candidates?.items.map((candidate) => <div key={candidate.productCode} className={`${styles.candidate} ${currentProduct?.productCode === candidate.productCode ? styles.currentCandidate : ''}`}>
            <Checkbox checked={isSelected(selection, candidate.productCode)} onClick={(event) => event.stopPropagation()} onChange={(event) => updateCandidate(candidate, event.target.checked)} />
            <button type="button" className={styles.candidateMain} disabled={!canSetCurrentProduct(selection, candidate.productCode)} onClick={() => setCurrentProduct(candidate)}><ProductImage src={candidate.imageUrl} alt={candidate.productName || candidate.productCode} size={48} />
              <span className={styles.candidateText}><strong>{candidate.productName || candidate.itemNumber || candidate.productCode}</strong><span>{candidate.itemNumber || '—'} · {candidate.barcode || '—'}</span><small>{candidate.warehouseCategoryName || '未分类'}</small></span>
            </button>
          </div>)}</div>
          <Pagination className={styles.pagination} size="small" current={candidatePage} pageSize={candidatePageSize} total={candidates?.total ?? 0} showSizeChanger onChange={(page, size) => { setCandidatePage(page); setCandidatePageSize(size) }} />
        </PanelState>
      </Card>
      <Card className={styles.panel} title="当前商品" bordered={false}>
        <PanelState loading={summaryLoading && !summary} error={summaryError} empty={!currentProduct} retry={loadSummary}>
          <div className={styles.productHeader}>{currentProduct ? <ProductImage src={currentProduct.imageUrl} alt={currentName || '商品'} size={64} /> : null}<div><Typography.Text type="secondary">当前商品</Typography.Text><Typography.Title level={4}>{currentName}</Typography.Title><span>{currentProduct?.productCode}</span></div></div>
          <Typography.Title level={5}>本地进货单明细</Typography.Title>
          <PanelState loading={detailLoading} error={detailError} empty={!details?.items.length} retry={loadCurrentProduct}><Table size="small" rowKey="detailGuid" columns={detailColumns} dataSource={details?.items} pagination={false} scroll={{ x: 'max-content' }} /></PanelState>
          <Typography.Title level={5} className={styles.trendTitle}>进销日趋势</Typography.Title>
          <PanelState loading={dailyLoading} error={dailyError} empty={!daily.length} retry={loadCurrentProduct}><DailyTrend data={daily} label={`${currentName || '当前商品'}进销日趋势`} /></PanelState>
        </PanelState>
      </Card>
      <Card className={`${styles.panel} ${styles.rightColumn}`} title="授权分店销量排行" bordered={false}>
        <PanelState loading={branchLoading} error={branchError} empty={!branches.length} retry={loadCurrentProduct}>
          <Table size="small" rowKey="branchCode" columns={branchColumns} dataSource={branches} pagination={false} onRow={(record) => ({ className: selectedBranchCode === record.branchCode ? styles.currentBranch : '' })} />
          {selectedBranchCode ? <><Typography.Title level={5} className={styles.trendTitle}>{branches.find((item) => item.branchCode === selectedBranchCode)?.branchName || selectedBranchCode}日净销量与均价</Typography.Title><PanelState loading={branchDailyLoading} error={branchDailyError} empty={!branchDaily.length} retry={() => loadBranchDaily(selectedBranchCode)}><DailyTrend data={branchDaily} label="分店日净销量与均价" /></PanelState></> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="点击分店查看日趋势" />}
        </PanelState>
      </Card>
    </div>
  </PageContainer>
}
