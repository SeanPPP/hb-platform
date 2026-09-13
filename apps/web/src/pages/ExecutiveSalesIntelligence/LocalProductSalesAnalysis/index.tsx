import { ClearOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, DatePicker, Empty, Input, Pagination, Select, Skeleton, Space, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
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
  PAGE_SECTION_TIMEOUT_SECONDS,
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
function localErrorDescription(error: string, t: TFunction, language?: string) {
  const validationKeys: Record<string, string> = {
    '参数错误：开始日期不能晚于结束日期': 'localProductSalesAnalysis.errors.dateOrder',
    '参数错误：日期范围截至 Brisbane 昨天': 'localProductSalesAnalysis.errors.futureDate',
    '参数错误：日期范围不能超过 366 天': 'localProductSalesAnalysis.errors.dateLimit',
    '响应格式非法': 'localProductSalesAnalysis.errors.invalidResponse',
    '请求失败': 'localProductSalesAnalysis.errors.load',
  }
  if (validationKeys[error]) return t(validationKeys[error])
  if (error.startsWith('localProductSalesAnalysis.errors.')) return t(error)
  // 保留状态中的原始诊断，展示时随语言翻译；切换语言不会重新发起分析请求。
  if (language?.startsWith('en') && /[\u3400-\u9fff]/u.test(error)) {
    return t(/超时/.test(error) ? 'localProductSalesAnalysis.errors.timeout' : 'localProductSalesAnalysis.errors.load')
  }
  return error
}
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
  const { t, i18n } = useTranslation()
  if (loading) return <div className={styles.state}><Skeleton active title={false} paragraph={{ rows: 3, width: ['92%', '76%', '84%'] }} /></div>
  if (error) return <Alert type="error" showIcon message={t('localProductSalesAnalysis.errors.title')} description={localErrorDescription(error, t, i18n.resolvedLanguage)} action={<Button size="small" onClick={retry}>{t('common.retry')}</Button>} />
  if (empty) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('common.noData')} />
  return <>{children}</>
}

function DailyTrend({ data, label }: { data: LocalSupplierProductSalesAnalysisDaily[]; label: string }) {
  const { t } = useTranslation()
  if (!data.length) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('localProductSalesAnalysis.noTrendData')} />
  return <FlowTrendChart data={toFlowTrendData(data)} ariaLabel={label} />
}

function Totals({ summary }: { summary: LocalSupplierProductSalesAnalysisSummary | null }) {
  const { t } = useTranslation()
  const totals = summary?.totals
  const values: Array<[string, number | null | undefined, (value: number | null | undefined) => string]> = [
    [t('localProductSalesAnalysis.metrics.purchaseQuantity'), totals?.purchaseQuantity, (value) => formatQuantity(value ?? 0)],
    [t('localProductSalesAnalysis.metrics.purchaseAmount'), totals?.purchaseAmount, formatAud],
    [t('localProductSalesAnalysis.metrics.netSalesQuantity'), totals?.netSalesQuantity, (value) => formatQuantity(value ?? 0)],
    [t('localProductSalesAnalysis.metrics.netSalesAmount'), totals?.netSalesAmount, formatAud],
    [t('localProductSalesAnalysis.metrics.sellThroughRate'), totals?.sellThroughRate, (value) => value === null || value === undefined ? '—' : `${value.toFixed(1)}%`],
  ]
  return <div className={`${styles.totals} ${styles.topTotals}`}>{values.map(([label, value, render]) => <div key={label}><span>{label}</span><strong>{render(value)}</strong></div>)}</div>
}

export default function LocalProductSalesAnalysisPage() {
  const { t, i18n } = useTranslation()
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
      setBootstrapError(aborted(error) ? 'localProductSalesAnalysis.errors.timeout' : errorText(error, 'localProductSalesAnalysis.errors.load'))
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
    const timeout = createPageRequestTimeout(PAGE_SECTION_TIMEOUT_SECONDS)
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
      fail(aborted(error) ? 'localProductSalesAnalysis.errors.timeout' : errorText(error, 'localProductSalesAnalysis.errors.load'))
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
    { title: t('localProductSalesAnalysis.columns.invoiceNo'), dataIndex: 'invoiceNo', width: 118, render: (value) => value || '—' }, { title: t('localProductSalesAnalysis.columns.store'), width: 130, render: (_, row) => row.storeName || row.storeCode || '—' },
    { title: t('localProductSalesAnalysis.columns.supplier'), width: 140, render: (_, row) => row.supplierName || row.supplierCode || '—' }, { title: t('localProductSalesAnalysis.columns.date'), dataIndex: 'purchaseDate', width: 106, render: (value) => value || '—' },
    { title: t('localProductSalesAnalysis.columns.quantity'), dataIndex: 'quantity', align: 'right', width: 88, render: formatQuantity }, { title: t('localProductSalesAnalysis.columns.purchasePrice'), dataIndex: 'purchasePrice', align: 'right', width: 108, render: formatAud }, { title: t('localProductSalesAnalysis.columns.amount'), dataIndex: 'amount', align: 'right', width: 108, render: formatAud },
  ]
  const branchColumns: ColumnsType<LocalSupplierProductSalesAnalysisBranch> = [
    { title: t('localProductSalesAnalysis.columns.authorizedStore'), render: (_, row) => <button type="button" className={styles.branchButton} onClick={() => { setSelectedBranchCode(row.branchCode); loadBranchDaily(row.branchCode) }}>{row.branchName || row.branchCode}</button> }, { title: t('localProductSalesAnalysis.columns.netSalesQuantity'), dataIndex: 'netSalesQuantity', align: 'right', render: formatQuantity }, { title: t('localProductSalesAnalysis.columns.averageUnitPrice'), dataIndex: 'averageUnitPrice', align: 'right', render: formatAud },
  ]
  const analysisLoading = loadPhase === 'bootstrap' || loadPhase === 'switch'
  const currentName = analysis.currentProduct?.productName || analysis.currentProduct?.itemNumber || analysis.currentProduct?.productCode
  const supplierOptions = useMemo(() => {
    // 复制后按供应商名称排序，避免改动接口返回的共享选项；无名称时使用编码。
    return [...analysis.options.suppliers]
      .sort((a, b) => (a.name || a.code).localeCompare(b.name || b.code, 'en-AU', { sensitivity: 'base', numeric: true }))
      .map((item) => ({ value: item.code, label: item.name ? `${item.name} (${item.code})` : item.code }))
  }, [analysis.options.suppliers])

  return <PageContainer title={t('localProductSalesAnalysis.title')}>
    <Card className={styles.toolbar} bordered={false}>
      <Space wrap>
        <RangePicker value={draftRange} disabledDate={(date) => date.isAfter(brisbaneYesterday, 'day')} onChange={(value) => value?.[0] && value?.[1] && (setDraftRange([value[0], value[1]]), setQuickDays(null))} allowClear={false} />
        {[7, 30, 90].map((days) => <Button key={days} type={quickDays === days ? 'primary' : 'default'} onClick={() => setRangeDays(days)}>{t('localProductSalesAnalysis.quickDays', { count: days })}</Button>)}
        <Button icon={<SearchOutlined />} type="primary" onClick={applyFilters}>{t('common.query')}</Button>
        <Button icon={<ClearOutlined />} onClick={resetFilters}>{t('common.reset')}</Button>
        <Button icon={<ReloadOutlined />} loading={loadPhase === 'refresh'} onClick={refresh}>{t('common.refresh')}</Button>
      </Space>
    </Card>
    {analysis.sectionErrors.options ? <Alert className={styles.optionsAlert} type="warning" showIcon message={t('localProductSalesAnalysis.errors.options')} description={localErrorDescription(analysis.sectionErrors.options, t, i18n.resolvedLanguage)} action={<Button size="small" onClick={() => retrySection('options')}>{t('common.retry')}</Button>} /> : null}
    {bootstrapError ? <Alert className={styles.optionsAlert} type="error" showIcon message={t('localProductSalesAnalysis.errors.title')} description={localErrorDescription(bootstrapError, t, i18n.resolvedLanguage)} action={<Button size="small" onClick={retryBootstrap}>{t('common.retry')}</Button>} /> : null}
    <Card className={styles.summaryCard} bordered={false}>
      <PanelState loading={analysisLoading || !!sectionLoading.summary} error={analysis.sectionErrors.summary} empty={!hasSelection(analysis.effectiveSelection)} retry={() => retrySection('summary')}><Totals summary={analysis.summary} /></PanelState>
    </Card>
    <div className={styles.layout}>
      <Card className={styles.panel} title={t('localProductSalesAnalysis.productScope')} bordered={false}>
        <div className={styles.filters}>
          <Input value={draftKeyword} onChange={(event) => setDraftKeyword(event.target.value)} placeholder={t('localProductSalesAnalysis.filters.keyword')} allowClear />
          <Select value={draftCategoryGuid} onChange={setDraftCategoryGuid} placeholder={t('localProductSalesAnalysis.filters.category')} allowClear options={analysis.options.warehouseCategories.map((item) => ({ value: item.guid, label: item.name || item.guid }))} notFoundContent={t('localProductSalesAnalysis.noCategories')} />
          <Select value={draftSupplierCode} onChange={setDraftSupplierCode} placeholder={t('localProductSalesAnalysis.filters.supplier')} allowClear showSearch optionFilterProp="label" options={supplierOptions} notFoundContent={t('localProductSalesAnalysis.noSuppliers')} />
          <Input value={draftDocumentKeyword} onChange={(event) => setDraftDocumentKeyword(event.target.value)} placeholder={t('localProductSalesAnalysis.filters.invoiceNo')} allowClear />
          <Space wrap>
            <Button icon={<SearchOutlined />} type="primary" onClick={applyFilters}>{t('common.query')}</Button>
            <Button icon={<ClearOutlined />} onClick={resetFilters}>{t('common.reset')}</Button>
          </Space>
        </div>
        <div className={styles.selectionBar}><span>{analysis.effectiveSelection.mode === 'included' ? t('localProductSalesAnalysis.selectedCount', { count: analysis.effectiveSelection.includedProductCodes.length }) : t('localProductSalesAnalysis.allFilteredSelected')}</span><Space size={4}><Button type="link" size="small" onClick={selectAllFiltered}>{t('localProductSalesAnalysis.selectAllFiltered')}</Button><Button type="link" size="small" onClick={clearSelection}>{t('common.clearSelection')}</Button></Space></div>
        <PanelState loading={loadPhase === 'bootstrap' || candidatePaging} error={undefined} empty={analysis.candidates !== null && !analysis.candidates.items.length} retry={retryBootstrap}>
          <div className={styles.candidates}>{analysis.candidates?.items.map((candidate) => <div key={candidate.productCode} className={`${styles.candidate} ${analysis.currentProduct?.productCode === candidate.productCode ? styles.currentCandidate : ''}`}>
            <Checkbox checked={isSelected(analysis.effectiveSelection, candidate.productCode)} onClick={(event) => event.stopPropagation()} onChange={(event) => updateCandidate(candidate, event.target.checked)} />
            <button type="button" className={styles.candidateMain} disabled={!canSetCurrentProduct(analysis.effectiveSelection, candidate.productCode)} onClick={() => { if (canSetCurrentProduct(analysis.effectiveSelection, candidate.productCode)) loadCurrentProductSections(candidate, selectionRef.current) }}><ProductImage src={candidate.imageUrl} alt={candidate.productName || candidate.productCode} size={48} />
              <span className={styles.candidateText}><strong>{candidate.productName || candidate.itemNumber || candidate.productCode}</strong><span>{candidate.itemNumber || '—'} · {candidate.barcode || '—'}</span><small>{candidate.warehouseCategoryName || t('localProductSalesAnalysis.uncategorized')}</small></span>
            </button>
          </div>)}</div>
          <Pagination className={styles.pagination} size="small" current={candidatePage} pageSize={candidatePageSize} total={analysis.candidates?.total ?? 0} showSizeChanger onChange={(page, size) => loadCandidatePage(page, size)} />
        </PanelState>
      </Card>
      <Card className={styles.panel} title={t('localProductSalesAnalysis.currentProduct')} bordered={false}>
        <PanelState loading={analysisLoading} error={undefined} empty={!analysis.currentProduct} retry={() => retrySection('summary')}>
          <div className={styles.productHeader}>{analysis.currentProduct ? <ProductImage src={analysis.currentProduct.imageUrl} alt={currentName || t('localProductSalesAnalysis.product')} size={64} /> : null}<div><Typography.Text type="secondary">{t('localProductSalesAnalysis.currentProduct')}</Typography.Text><Typography.Title level={4}>{currentName}</Typography.Title><span>{analysis.currentProduct?.productCode}</span></div></div>
          <Typography.Title level={5}>{t('localProductSalesAnalysis.invoiceDetails')}</Typography.Title>
          <PanelState loading={analysisLoading || !!sectionLoading.invoiceDetails} error={analysis.sectionErrors.invoiceDetails} empty={analysis.invoiceDetails !== null && !analysis.invoiceDetails.items.length} retry={() => retrySection('invoiceDetails')}><MeasuredTable metricId="executive-sales-intelligence.local-product-sales-analysis.table-1" size="small" rowKey="detailGuid" columns={detailColumns} dataSource={analysis.invoiceDetails?.items} pagination={false} scroll={{ x: 'max-content' }} /></PanelState>
          <Typography.Title level={5} className={styles.trendTitle}>{t('localProductSalesAnalysis.dailyTrend')}</Typography.Title>
          <PanelState loading={analysisLoading || !!sectionLoading.productDaily} error={analysis.sectionErrors.productDaily} empty={!analysis.productDaily.length} retry={() => retrySection('productDaily')}><DailyTrend data={analysis.productDaily} label={t('localProductSalesAnalysis.productTrendLabel', { product: currentName || t('localProductSalesAnalysis.currentProduct') })} /></PanelState>
        </PanelState>
      </Card>
      <Card className={`${styles.panel} ${styles.rightColumn}`} title={t('localProductSalesAnalysis.branchRanking')} bordered={false}>
        <PanelState loading={analysisLoading || !!sectionLoading.branches} error={analysis.sectionErrors.branches} empty={!analysis.branches.length} retry={() => retrySection('branches')}>
          <MeasuredTable metricId="executive-sales-intelligence.local-product-sales-analysis.table-2" size="small" rowKey="branchCode" columns={branchColumns} dataSource={analysis.branches} pagination={false} onRow={(record) => ({ className: selectedBranchCode === record.branchCode ? styles.currentBranch : '' })} />
          {selectedBranchCode ? <><Typography.Title level={5} className={styles.trendTitle}>{t('localProductSalesAnalysis.branchTrendTitle', { branch: analysis.branches.find((item) => item.branchCode === selectedBranchCode)?.branchName || selectedBranchCode })}</Typography.Title><PanelState loading={branchDailyLoading} error={branchDailyError} empty={!branchDaily.length} retry={() => loadBranchDaily(selectedBranchCode)}><DailyTrend data={branchDaily} label={t('localProductSalesAnalysis.branchTrendLabel')} /></PanelState></> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('localProductSalesAnalysis.selectBranch')} />}
        </PanelState>
      </Card>
    </div>
  </PageContainer>
}
