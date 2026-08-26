import { ClearOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, DatePicker, Empty, Input, Pagination, Select, Skeleton, Space, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import PageContainer from '../../../components/PageContainer'
import FlowTrendChart from '../ProductFlowShared/FlowTrendChart'
import ProductImage from '../ProductFlowShared/ProductImage'
import {
  getLocalSupplierProductSalesAnalysisOptions,
  queryLocalSupplierProductSalesAnalysisBootstrap,
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
  LocalSupplierProductSalesAnalysisRequest,
  LocalSupplierProductSalesAnalysisSelection,
  LocalSupplierProductSalesAnalysisSummary,
} from '../../../types/localSupplierProductSalesAnalysis'
import {
  applyCandidateSelection,
  applyLocalProductSalesAnalysisBootstrapResult,
  applyLocalProductSalesAnalysisSectionResult,
  buildBrisbaneDefaultRange,
  buildLocalProductSalesAnalysisBootstrapRequest,
  canSetCurrentProduct,
  clearLocalProductSalesAnalysisSectionError,
  clearLocalProductSalesAnalysisDetailSections,
  createEmptyLocalProductSalesAnalysisState,
  createIncludedSelection,
  createLatestRequestGuard,
  createPageRequestTimeout,
  formatAud,
  getDateRangeError,
  getCurrentProductAfterCancellation,
  isSelected,
  PAGE_BOOTSTRAP_TIMEOUT_SECONDS,
  setLocalProductSalesAnalysisSectionError,
  toFlowTrendData,
  type LocalProductSalesAnalysisBootstrapState,
  type LocalProductSalesAnalysisSectionKey,
  type PageRequestTimeout,
} from './logic'
import styles from './index.module.css'
import { MeasuredTable } from '../../../components/MeasuredTable'

const { RangePicker } = DatePicker
const quantityFormatter = new Intl.NumberFormat('en-AU')

type BootstrapMode = 'bootstrap' | 'refresh' | 'switch'
interface BootstrapContext { selection?: LocalSupplierProductSalesAnalysisSelection; currentProductCode?: string }

function formatQuantity(value: number) { return quantityFormatter.format(value) }
function errorText(error: unknown, fallback: string) { return error instanceof Error && error.message ? error.message : fallback }
function aborted(error: unknown) { return error instanceof Error && error.name === 'AbortError' }
function requestFilter(range: [Dayjs, Dayjs], keyword: string, categoryGuid?: string, supplierCode?: string, documentKeyword?: string): LocalSupplierProductSalesAnalysisFilter {
  return { startDate: range[0].format('YYYY-MM-DD'), endDate: range[1].format('YYYY-MM-DD'), keyword: keyword.trim() || undefined, categoryGuid, supplierCode, documentKeyword: documentKeyword?.trim() || undefined }
}
function hasSelection(selection: LocalSupplierProductSalesAnalysisSelection) { return selection.mode === 'allFiltered' || selection.includedProductCodes.length > 0 }

function queryAnalysisSection(key: LocalProductSalesAnalysisSectionKey, body: LocalSupplierProductSalesAnalysisRequest, signal: AbortSignal): Promise<{ data: unknown }> {
  if (key === 'options') return getLocalSupplierProductSalesAnalysisOptions(signal)
  if (key === 'summary') return queryLocalSupplierProductSalesAnalysisSummary(body, signal)
  if (key === 'invoiceDetails') return queryLocalSupplierProductSalesAnalysisInvoiceDetails(body, signal)
  if (key === 'productDaily') return queryLocalSupplierProductSalesAnalysisProductDaily(body, signal)
  return queryLocalSupplierProductSalesAnalysisBranches(body, signal)
}

function PanelState({ loading, error, empty, retry, children }: { loading: boolean; error?: string; empty?: boolean; retry: () => void; children: ReactNode }) {
  if (loading) return <div className={styles.state}><Skeleton active title={false} paragraph={{ rows: 3, width: ['92%', '76%', '84%'] }} /></div>
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
  const [candidatePage, setCandidatePage] = useState(1)
  const [candidatePageSize, setCandidatePageSize] = useState(20)
  const [loadPhase, setLoadPhase] = useState<BootstrapMode | 'idle'>('bootstrap')
  const [bootstrapError, setBootstrapError] = useState<string>()
  const [candidatePaging, setCandidatePaging] = useState(false)
  const [sectionLoading, setSectionLoading] = useState<Partial<Record<LocalProductSalesAnalysisSectionKey, boolean>>>({})
  const [analysis, setAnalysis] = useState<LocalProductSalesAnalysisBootstrapState>(() => createEmptyLocalProductSalesAnalysisState())
  const [branchDaily, setBranchDaily] = useState<LocalSupplierProductSalesAnalysisDaily[]>([])
  const [selectedBranchCode, setSelectedBranchCode] = useState<string>()
  const [branchDailyLoading, setBranchDailyLoading] = useState(false)
  const [branchDailyError, setBranchDailyError] = useState<string>()
  const analysisRef = useRef(analysis)
  const filterRef = useRef<LocalSupplierProductSalesAnalysisFilter>(requestFilter(defaultRange, ''))
  const selectionRef = useRef<LocalSupplierProductSalesAnalysisSelection>(createIncludedSelection())
  const currentProductRef = useRef<LocalSupplierProductSalesAnalysisCandidate | null>(null)
  const candidatePageRef = useRef(candidatePage)
  const candidatePageSizeRef = useRef(candidatePageSize)
  const migrateCurrentOnSummaryRef = useRef(false)
  const lastBootstrapRef = useRef<{ mode: BootstrapMode; context?: BootstrapContext }>({ mode: 'bootstrap' })
  const bootstrapGuardRef = useRef(createLatestRequestGuard())
  const paginationGuardRef = useRef(createLatestRequestGuard())
  const branchDailyGuardRef = useRef(createLatestRequestGuard())
  const bootstrapAbortRef = useRef<PageRequestTimeout>()
  const paginationAbortRef = useRef<PageRequestTimeout>()
  const branchDailyAbortRef = useRef<PageRequestTimeout>()
  const sectionAbortRefs = {
    options: useRef<PageRequestTimeout>(),
    summary: useRef<PageRequestTimeout>(),
    invoiceDetails: useRef<PageRequestTimeout>(),
    productDaily: useRef<PageRequestTimeout>(),
    branches: useRef<PageRequestTimeout>(),
  }
  const sectionGuards = {
    options: useRef(createLatestRequestGuard()).current,
    summary: useRef(createLatestRequestGuard()).current,
    invoiceDetails: useRef(createLatestRequestGuard()).current,
    productDaily: useRef(createLatestRequestGuard()).current,
    branches: useRef(createLatestRequestGuard()).current,
  }
  const brisbaneYesterday = dayjs(buildBrisbaneDefaultRange(1).endDate)

  // 统一 bootstrap：挂载/查询/重置 autoSelectFirst；刷新携带原选择/当前商品且 forceRefresh；
  // 新请求先作废旧请求与旧超时，竞态旧响应由 guard 丢弃，成功后一次状态提交原子替换。
  const runBootstrap = useCallback((mode: BootstrapMode, context?: BootstrapContext) => {
    const migrateCurrentOnSummary = mode === 'switch' && migrateCurrentOnSummaryRef.current
    // 迁移意图属于本次请求；立即清空共享标志，避免后续 refresh 继承被 guard 丢弃的旧请求状态。
    migrateCurrentOnSummaryRef.current = false
    lastBootstrapRef.current = { mode, context }
    bootstrapAbortRef.current?.abort()
    paginationAbortRef.current?.abort()
    paginationGuardRef.current.invalidate()
    setCandidatePaging(false)
    ;(['options', 'summary', 'invoiceDetails', 'productDaily', 'branches'] as const).forEach((key) => {
      sectionAbortRefs[key].current?.abort()
      sectionGuards[key].invalidate()
      setSectionLoading((prev) => ({ ...prev, [key]: false }))
    })
    branchDailyAbortRef.current?.abort()
    branchDailyGuardRef.current.invalidate()
    setBranchDailyLoading(false)
    const timeout = createPageRequestTimeout(PAGE_BOOTSTRAP_TIMEOUT_SECONDS)
    bootstrapAbortRef.current = timeout
    const token = bootstrapGuardRef.current.next()
    setBootstrapError(undefined)
    setLoadPhase(mode)
    if (mode !== 'refresh') {
      setSelectedBranchCode(undefined)
      setBranchDaily([])
      setBranchDailyError(undefined)
      setBranchDailyLoading(false)
      if (mode === 'bootstrap') setAnalysis(createEmptyLocalProductSalesAnalysisState())
      else setAnalysis(clearLocalProductSalesAnalysisDetailSections(analysisRef.current))
    }
    const autoSelectFirst = mode === 'bootstrap'
    const forceRefresh = mode === 'refresh'
    const selection = context?.selection ?? (mode === 'refresh' ? selectionRef.current : undefined)
    const requestedProductCode = context?.currentProductCode ?? (mode === 'refresh' ? currentProductRef.current?.productCode : undefined)
    const body = buildLocalProductSalesAnalysisBootstrapRequest({
      filter: filterRef.current,
      selection,
      currentProductCode: requestedProductCode,
      autoSelectFirst,
      forceRefresh,
      candidatePageNumber: candidatePageRef.current,
      candidatePageSize: candidatePageSizeRef.current,
      summaryPageNumber: 1,
      summaryPageSize: 50,
    })
    queryLocalSupplierProductSalesAnalysisBootstrap(body, timeout.signal).then((result) => {
      if (!bootstrapGuardRef.current.isCurrent(token)) return
      timeout.clear()
      const previousProductCode = currentProductRef.current?.productCode
      const next = applyLocalProductSalesAnalysisBootstrapResult(result.data, analysisRef.current)
      let currentProduct = next.currentProduct
      if (migrateCurrentOnSummary && !currentProduct) {
        currentProduct = getCurrentProductAfterCancellation(currentProduct, next.summary?.items ?? [], true)
      }
      selectionRef.current = next.effectiveSelection
      currentProductRef.current = currentProduct
      const committed = currentProduct === next.currentProduct ? next : { ...next, currentProduct }
      setAnalysis(committed)
      if (previousProductCode !== currentProduct?.productCode) {
        setSelectedBranchCode(undefined)
        setBranchDaily([])
        setBranchDailyError(undefined)
        setBranchDailyLoading(false)
      }
      setLoadPhase('idle')
    }).catch((error) => {
      if (!bootstrapGuardRef.current.isCurrent(token)) return
      timeout.clear()
      setLoadPhase('idle')
      setBootstrapError(aborted(error) ? '请求超时，请重试' : errorText(error, '加载失败，请重试'))
    })
  }, [])

  useEffect(() => {
    runBootstrap('bootstrap')
    return () => {
      bootstrapAbortRef.current?.abort()
      bootstrapGuardRef.current.invalidate()
      paginationAbortRef.current?.abort()
      branchDailyAbortRef.current?.abort()
      ;(['options', 'summary', 'invoiceDetails', 'productDaily', 'branches'] as const).forEach((key) => sectionAbortRefs[key].current?.abort())
    }
  }, [runBootstrap])
  useEffect(() => { analysisRef.current = analysis }, [analysis])

  const guardedRequest = <T,>(guard: ReturnType<typeof createLatestRequestGuard>, abortRef: { current?: PageRequestTimeout }, start: () => void, call: (signal: AbortSignal) => Promise<{ data: T }>, commit: (data: T) => void, fail: (message: string) => void, settle: () => void) => {
    abortRef.current?.abort()
    const timeout = createPageRequestTimeout(PAGE_BOOTSTRAP_TIMEOUT_SECONDS)
    abortRef.current = timeout
    const token = guard.next()
    start()
    call(timeout.signal).then((result) => {
      if (!guard.isCurrent(token)) return
      timeout.clear()
      commit(result.data)
    }).catch((error) => {
      if (!guard.isCurrent(token)) return
      timeout.clear()
      fail(aborted(error) ? '请求超时，请重试' : errorText(error, '加载失败，请重试'))
    }).finally(() => {
      if (guard.isCurrent(token)) settle()
    })
  }

  const stopBootstrapForSectionInteraction = () => {
    bootstrapAbortRef.current?.abort()
    bootstrapGuardRef.current.invalidate()
    setLoadPhase('idle')
    setBootstrapError(undefined)
  }

  const cancelAllAnalysisRequests = () => {
    stopBootstrapForSectionInteraction()
    ;(['options', 'summary', 'invoiceDetails', 'productDaily', 'branches'] as const).forEach((key) => {
      sectionAbortRefs[key].current?.abort()
      sectionGuards[key].invalidate()
    })
    branchDailyAbortRef.current?.abort()
    branchDailyGuardRef.current.invalidate()
    setBranchDailyLoading(false)
    setSectionLoading({})
  }

  const requestAnalysisSection = (
    key: LocalProductSalesAnalysisSectionKey,
    body: LocalSupplierProductSalesAnalysisRequest,
    onSuccess?: (data: unknown) => void,
  ) => {
    guardedRequest(sectionGuards[key], sectionAbortRefs[key],
      () => {
        setSectionLoading((prev) => ({ ...prev, [key]: true }))
        setAnalysis((prev) => clearLocalProductSalesAnalysisSectionError(prev, key))
      },
      (signal) => queryAnalysisSection(key, body, signal),
      (data) => {
        if (onSuccess) onSuccess(data)
        else setAnalysis((prev) => applyLocalProductSalesAnalysisSectionResult(prev, key, data))
      },
      (message) => setAnalysis((prev) => setLocalProductSalesAnalysisSectionError(prev, key, message)),
      () => setSectionLoading((prev) => ({ ...prev, [key]: false })),
    )
  }

  const loadSummaryForSelection = (selection: LocalSupplierProductSalesAnalysisSelection) => {
    stopBootstrapForSectionInteraction()
    setAnalysis((prev) => ({
      ...clearLocalProductSalesAnalysisSectionError(prev, 'summary'),
      effectiveSelection: selection,
      summary: null,
    }))
    requestAnalysisSection('summary', {
      filter: filterRef.current,
      selection,
      pageNumber: 1,
      pageSize: 50,
    })
  }

  const loadCurrentProductSections = (
    product: LocalSupplierProductSalesAnalysisCandidate,
    selection: LocalSupplierProductSalesAnalysisSelection,
  ) => {
    stopBootstrapForSectionInteraction()
    currentProductRef.current = product
    setSelectedBranchCode(undefined)
    setBranchDaily([])
    setBranchDailyError(undefined)
    setBranchDailyLoading(false)
    setAnalysis((prev) => {
      let next = clearLocalProductSalesAnalysisSectionError(prev, 'invoiceDetails')
      next = clearLocalProductSalesAnalysisSectionError(next, 'productDaily')
      next = clearLocalProductSalesAnalysisSectionError(next, 'branches')
      return {
        ...next,
        effectiveSelection: selection,
        currentProduct: product,
        invoiceDetails: null,
        productDaily: [],
        branches: [],
      }
    })
    const body = {
      filter: filterRef.current,
      selection,
      currentProductCode: product.productCode,
    }
    requestAnalysisSection('invoiceDetails', { ...body, pageNumber: 1, pageSize: 50 })
    requestAnalysisSection('productDaily', body)
    requestAnalysisSection('branches', body)
  }

  // 新筛选必须立即清空分店日趋势遗留，避免旧商品/旧筛选的钻取数据残留。
  const clearAnalysisState = () => {
    migrateCurrentOnSummaryRef.current = false
    setSelectedBranchCode(undefined)
    setBranchDaily([])
    setBranchDailyError(undefined)
    setBranchDailyLoading(false)
  }

  const applyFilters = () => {
    const rangeError = getDateRangeError(draftRange[0].format('YYYY-MM-DD'), draftRange[1].format('YYYY-MM-DD'), brisbaneYesterday.format('YYYY-MM-DD'))
    if (rangeError) { setLoadPhase('idle'); setBootstrapError(rangeError); return }
    const nextFilter = requestFilter(draftRange, draftKeyword, draftCategoryGuid, draftSupplierCode, draftDocumentKeyword)
    filterRef.current = nextFilter
    selectionRef.current = createIncludedSelection()
    currentProductRef.current = null
    migrateCurrentOnSummaryRef.current = false
    candidatePageRef.current = 1
    candidatePageSizeRef.current = 20
    setCandidatePage(1)
    setCandidatePageSize(20)
    clearAnalysisState()
    runBootstrap('bootstrap')
  }

  const resetFilters = () => {
    const range = (() => { const result = buildBrisbaneDefaultRange(30); return [dayjs(result.startDate), dayjs(result.endDate)] as [Dayjs, Dayjs] })()
    setDraftRange(range); setDraftKeyword(''); setDraftCategoryGuid(undefined); setDraftSupplierCode(undefined); setDraftDocumentKeyword(''); setQuickDays(30)
    const nextFilter = requestFilter(range, '')
    filterRef.current = nextFilter
    selectionRef.current = createIncludedSelection()
    currentProductRef.current = null
    migrateCurrentOnSummaryRef.current = false
    candidatePageRef.current = 1
    candidatePageSizeRef.current = 20
    setCandidatePage(1)
    setCandidatePageSize(20)
    clearAnalysisState()
    runBootstrap('bootstrap')
  }

  const setRangeDays = (days: number) => {
    const result = buildBrisbaneDefaultRange(days)
    setDraftRange([dayjs(result.startDate), dayjs(result.endDate)])
    setQuickDays(days)
  }

  const refresh = () => {
    runBootstrap('refresh')
  }
  const retryBootstrap = () => {
    const last = lastBootstrapRef.current
    runBootstrap(last.mode, last.context)
  }

  const updateCandidate = (candidate: LocalSupplierProductSalesAnalysisCandidate, checked: boolean) => {
    const next = applyCandidateSelection(selectionRef.current, candidate.productCode, checked)
    selectionRef.current = next
    const currentWasCancelled = !checked && currentProductRef.current?.productCode === candidate.productCode
    if (!hasSelection(next)) {
      cancelAllAnalysisRequests()
      setAnalysis((prev) => ({ ...clearLocalProductSalesAnalysisDetailSections(prev), effectiveSelection: next, currentProduct: null }))
      currentProductRef.current = null
      return
    }

    if (currentWasCancelled) {
      const fallback = [
        ...(analysisRef.current.candidates?.items ?? []),
        ...(analysisRef.current.summary?.items ?? []),
      ].find((item) => item.productCode !== candidate.productCode && isSelected(next, item.productCode))
      if (!fallback) {
        // 当前可见快照没有替代项时，交给 bootstrap 按稳定商品顺序解析跨页选择。
        migrateCurrentOnSummaryRef.current = true
        runBootstrap('switch', { selection: next, currentProductCode: candidate.productCode })
        return
      }
      migrateCurrentOnSummaryRef.current = false
      loadSummaryForSelection(next)
      loadCurrentProductSections(fallback, next)
      return
    }

    loadSummaryForSelection(next)
    if (!currentProductRef.current && checked) loadCurrentProductSections(candidate, next)
  }
  const selectAllFiltered = () => {
    const next = { mode: 'allFiltered' as const, includedProductCodes: [], excludedProductCodes: [] }
    selectionRef.current = next
    const current = currentProductRef.current
    if (current) {
      loadSummaryForSelection(next)
      return
    }
    const first = analysisRef.current.candidates?.items[0]
    if (first) {
      loadSummaryForSelection(next)
      loadCurrentProductSections(first, next)
      return
    }
    runBootstrap('switch', { selection: next })
  }
  const clearSelection = () => {
    const empty = createIncludedSelection()
    cancelAllAnalysisRequests()
    selectionRef.current = empty
    currentProductRef.current = null
    clearAnalysisState()
    setAnalysis((prev) => ({ ...clearLocalProductSalesAnalysisDetailSections(prev), effectiveSelection: empty, currentProduct: null }))
    setLoadPhase('idle')
  }

  // 候选分页只请求 candidates，不再触发 bootstrap 全量重查。
  const loadCandidatePage = (page: number, size: number) => {
    candidatePageRef.current = page
    candidatePageSizeRef.current = size
    setCandidatePage(page)
    setCandidatePageSize(size)
    bootstrapAbortRef.current?.abort()
    bootstrapGuardRef.current.invalidate()
    // 分页会取代仍在进行的 bootstrap/switch；同步收口加载态，避免按钮永久转圈。
    setLoadPhase('idle')
    guardedRequest(paginationGuardRef.current, paginationAbortRef,
      () => { setBootstrapError(undefined); setCandidatePaging(true) },
      (signal) => queryLocalSupplierProductSalesAnalysisCandidates({ filter: filterRef.current, selection: selectionRef.current, pageNumber: page, pageSize: size }, signal),
      (data) => { setBootstrapError(undefined); setAnalysis((prev) => ({ ...prev, candidates: data })) },
      (message) => setBootstrapError(message),
      () => setCandidatePaging(false),
    )
  }

  // 部分失败按卡片重试：保留分段 API，只替换目标分段。
  const retrySection = (key: LocalProductSalesAnalysisSectionKey) => {
    const body = {
      filter: filterRef.current,
      selection: selectionRef.current,
      currentProductCode: currentProductRef.current?.productCode,
    }
    requestAnalysisSection(
      key,
      key === 'summary' || key === 'invoiceDetails'
        ? { ...body, pageNumber: 1, pageSize: 50 }
        : body,
    )
  }

  // 分店钻取仍走 branch-daily 单端点。
  const loadBranchDaily = (branchCode?: string) => {
    const product = currentProductRef.current
    if (!product || !branchCode) { setBranchDaily([]); return }
    guardedRequest(branchDailyGuardRef.current, branchDailyAbortRef,
      () => { setBranchDailyError(undefined); setBranchDailyLoading(true) },
      (signal) => queryLocalSupplierProductSalesAnalysisBranchDaily({ filter: filterRef.current, selection: selectionRef.current, currentProductCode: product.productCode, branchCode }, signal),
      (data) => { setBranchDailyError(undefined); setBranchDaily(data) },
      (message) => setBranchDailyError(message),
      () => setBranchDailyLoading(false),
    )
  }

  const detailColumns: ColumnsType<LocalSupplierProductSalesAnalysisInvoiceDetail> = [
    { title: '单号', dataIndex: 'invoiceNo', width: 118, render: (value) => value || '—' }, { title: '分店', width: 130, render: (_, row) => row.storeName || row.storeCode || '—' },
    { title: '供应商', width: 140, render: (_, row) => row.supplierName || row.supplierCode || '—' }, { title: '日期', dataIndex: 'purchaseDate', width: 106, render: (value) => value || '—' },
    { title: '数量', dataIndex: 'quantity', align: 'right', width: 88, render: formatQuantity }, { title: '进货单价', dataIndex: 'purchasePrice', align: 'right', width: 108, render: formatAud }, { title: '金额', dataIndex: 'amount', align: 'right', width: 108, render: formatAud },
  ]
  const branchColumns: ColumnsType<LocalSupplierProductSalesAnalysisBranch> = [
    { title: '授权分店', render: (_, row) => <button type="button" className={styles.branchButton} onClick={() => { setSelectedBranchCode(row.branchCode); loadBranchDaily(row.branchCode) }}>{row.branchName || row.branchCode}</button> }, { title: '净销量', dataIndex: 'netSalesQuantity', align: 'right', render: formatQuantity }, { title: '均价', dataIndex: 'averageUnitPrice', align: 'right', render: formatAud },
  ]
  const analysisLoading = loadPhase === 'bootstrap' || loadPhase === 'switch'
  const currentName = analysis.currentProduct?.productName || analysis.currentProduct?.itemNumber || analysis.currentProduct?.productCode

  return <PageContainer title="澳洲本地商品分析">
    <Card className={styles.toolbar} bordered={false}>
      <Space wrap>
        <RangePicker value={draftRange} disabledDate={(date) => date.isAfter(brisbaneYesterday, 'day')} onChange={(value) => value?.[0] && value?.[1] && (setDraftRange([value[0], value[1]]), setQuickDays(null))} allowClear={false} />
        {[7, 30, 90].map((days) => <Button key={days} type={quickDays === days ? 'primary' : 'default'} onClick={() => setRangeDays(days)}>{days}天</Button>)}
        <Button icon={<SearchOutlined />} type="primary" onClick={applyFilters}>查询</Button>
        <Button icon={<ClearOutlined />} onClick={resetFilters}>重置</Button>
        <Button icon={<ReloadOutlined />} loading={loadPhase === 'refresh'} onClick={refresh}>刷新</Button>
      </Space>
    </Card>
    {analysis.sectionErrors.options ? <Alert className={styles.optionsAlert} type="warning" showIcon message="筛选选项加载失败" description={analysis.sectionErrors.options} action={<Button size="small" onClick={() => retrySection('options')}>重试</Button>} /> : null}
    {bootstrapError ? <Alert className={styles.optionsAlert} type="error" showIcon message="加载失败" description={bootstrapError} action={<Button size="small" onClick={retryBootstrap}>重试</Button>} /> : null}
    <Card className={styles.summaryCard} bordered={false}>
      <PanelState loading={analysisLoading || !!sectionLoading.summary} error={analysis.sectionErrors.summary} empty={!hasSelection(analysis.effectiveSelection)} retry={() => retrySection('summary')}><Totals summary={analysis.summary} /></PanelState>
    </Card>
    <div className={styles.layout}>
      <Card className={styles.panel} title="商品范围" bordered={false}>
        <div className={styles.filters}>
          <Input value={draftKeyword} onChange={(event) => setDraftKeyword(event.target.value)} placeholder="货号、中文/英文名称或条码" allowClear />
          <Select value={draftCategoryGuid} onChange={setDraftCategoryGuid} placeholder="仓库分类" allowClear options={analysis.options.warehouseCategories.map((item) => ({ value: item.guid, label: item.name || item.guid }))} notFoundContent="暂无可选分类" />
          <Select value={draftSupplierCode} onChange={setDraftSupplierCode} placeholder="澳洲本地供应商" allowClear options={analysis.options.suppliers.map((item) => ({ value: item.code, label: item.name ? `${item.name} (${item.code})` : item.code }))} notFoundContent="暂无可选供应商" />
          <Input value={draftDocumentKeyword} onChange={(event) => setDraftDocumentKeyword(event.target.value)} placeholder="本地进货单号" allowClear />
        </div>
        <div className={styles.selectionBar}><span>已选 {analysis.effectiveSelection.mode === 'included' ? analysis.effectiveSelection.includedProductCodes.length : '全部筛选结果'} 项</span><Space size={4}><Button type="link" size="small" onClick={selectAllFiltered}>全选筛选结果</Button><Button type="link" size="small" onClick={clearSelection}>清空选择</Button></Space></div>
        <PanelState loading={loadPhase === 'bootstrap' || candidatePaging} error={undefined} empty={analysis.candidates !== null && !analysis.candidates.items.length} retry={retryBootstrap}>
          <div className={styles.candidates}>{analysis.candidates?.items.map((candidate) => <div key={candidate.productCode} className={`${styles.candidate} ${analysis.currentProduct?.productCode === candidate.productCode ? styles.currentCandidate : ''}`}>
            <Checkbox checked={isSelected(analysis.effectiveSelection, candidate.productCode)} onClick={(event) => event.stopPropagation()} onChange={(event) => updateCandidate(candidate, event.target.checked)} />
            <button type="button" className={styles.candidateMain} disabled={!canSetCurrentProduct(analysis.effectiveSelection, candidate.productCode)} onClick={() => { if (canSetCurrentProduct(analysis.effectiveSelection, candidate.productCode)) loadCurrentProductSections(candidate, selectionRef.current) }}><ProductImage src={candidate.imageUrl} alt={candidate.productName || candidate.productCode} size={48} />
              <span className={styles.candidateText}><strong>{candidate.productName || candidate.itemNumber || candidate.productCode}</strong><span>{candidate.itemNumber || '—'} · {candidate.barcode || '—'}</span><small>{candidate.warehouseCategoryName || '未分类'}</small></span>
            </button>
          </div>)}</div>
          <Pagination className={styles.pagination} size="small" current={candidatePage} pageSize={candidatePageSize} total={analysis.candidates?.total ?? 0} showSizeChanger onChange={(page, size) => loadCandidatePage(page, size)} />
        </PanelState>
      </Card>
      <Card className={styles.panel} title="当前商品" bordered={false}>
        <PanelState loading={analysisLoading} error={undefined} empty={!analysis.currentProduct} retry={() => retrySection('summary')}>
          <div className={styles.productHeader}>{analysis.currentProduct ? <ProductImage src={analysis.currentProduct.imageUrl} alt={currentName || '商品'} size={64} /> : null}<div><Typography.Text type="secondary">当前商品</Typography.Text><Typography.Title level={4}>{currentName}</Typography.Title><span>{analysis.currentProduct?.productCode}</span></div></div>
          <Typography.Title level={5}>本地进货单明细</Typography.Title>
          <PanelState loading={analysisLoading || !!sectionLoading.invoiceDetails} error={analysis.sectionErrors.invoiceDetails} empty={analysis.invoiceDetails !== null && !analysis.invoiceDetails.items.length} retry={() => retrySection('invoiceDetails')}><MeasuredTable metricId="executive-sales-intelligence.local-product-sales-analysis.table-1" size="small" rowKey="detailGuid" columns={detailColumns} dataSource={analysis.invoiceDetails?.items} pagination={false} scroll={{ x: 'max-content' }} /></PanelState>
          <Typography.Title level={5} className={styles.trendTitle}>进销日趋势</Typography.Title>
          <PanelState loading={analysisLoading || !!sectionLoading.productDaily} error={analysis.sectionErrors.productDaily} empty={!analysis.productDaily.length} retry={() => retrySection('productDaily')}><DailyTrend data={analysis.productDaily} label={`${currentName || '当前商品'}进销日趋势`} /></PanelState>
        </PanelState>
      </Card>
      <Card className={`${styles.panel} ${styles.rightColumn}`} title="授权分店销量排行" bordered={false}>
        <PanelState loading={analysisLoading || !!sectionLoading.branches} error={analysis.sectionErrors.branches} empty={!analysis.branches.length} retry={() => retrySection('branches')}>
          <MeasuredTable metricId="executive-sales-intelligence.local-product-sales-analysis.table-2" size="small" rowKey="branchCode" columns={branchColumns} dataSource={analysis.branches} pagination={false} onRow={(record) => ({ className: selectedBranchCode === record.branchCode ? styles.currentBranch : '' })} />
          {selectedBranchCode ? <><Typography.Title level={5} className={styles.trendTitle}>{analysis.branches.find((item) => item.branchCode === selectedBranchCode)?.branchName || selectedBranchCode}日净销量与均价</Typography.Title><PanelState loading={branchDailyLoading} error={branchDailyError} empty={!branchDaily.length} retry={() => loadBranchDaily(selectedBranchCode)}><DailyTrend data={branchDaily} label="分店日净销量与均价" /></PanelState></> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="点击分店查看日趋势" />}
        </PanelState>
      </Card>
    </div>
  </PageContainer>
}
