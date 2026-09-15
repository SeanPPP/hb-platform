import { DownloadOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, DatePicker, Dropdown, Empty, Input, Progress, Select, Skeleton, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { MeasuredTable } from '../../../components/MeasuredTable'
import { batchProductSalesApi } from '../../../services/batchProductSalesAnalysisService'
import type { BatchProductSalesApi, BatchSalesBranch, BatchSalesDaily, BatchSalesDetail, BatchSalesMetrics, BatchSalesProductSummary, BatchSalesQueryResult, BatchSalesScope } from '../../../types/batchProductSalesAnalysis'
import ProductImage from '../ProductFlowShared/ProductImage'
import { parsePastedItemNumbers, type ImportResult } from './import'
import { buildBatchProductSalesAnalysis, formatCsvRow, getBatchProductSalesClassifiedQuantity, getBatchProductSalesDateRangeError, getBatchProductSalesDiscountStateKey, hasBatchProductSalesDiscountStatisticsNotice, hasBatchProductSalesDailyActivity, runBatchProductSalesPool, shouldRefreshBatchProductSalesDiscountStatistics } from './logic'
import DiscountDailyChart from './DiscountDailyChart'
import ProductScopeModal from './ProductScopeModal'
import styles from './index.module.css'

const { RangePicker } = DatePicker
const ALL_PRODUCTS = '__batch-product-sales-all__'
const DETAIL_CONCURRENCY = 5
const quantityFormatter = new Intl.NumberFormat('en-AU')
const audFormatter = new Intl.NumberFormat('en-AU', {
  style: 'currency',
  currency: 'AUD',
})

interface BatchProductSalesAnalysisPageProps {
  api?: BatchProductSalesApi
}
interface BranchRankingRow extends BatchSalesBranch {
  rank: number
  contributingProductCount: number
  selectedProductCount: number
}

function businessToday() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Australia/Brisbane',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date())
  const value = (type: string) => parts.find((part) => part.type === type)?.value ?? ''
  return `${value('year')}-${value('month')}-${value('day')}`
}
function defaultRange(today: string): [Dayjs, Dayjs] {
  const end = dayjs(today)
  return [end.subtract(29, 'day'), end]
}
function number(value: number | null | undefined) {
  return quantityFormatter.format(value ?? 0)
}
function money(value: number | null | undefined) {
  return value === null || value === undefined || !Number.isFinite(value) ? '—' : audFormatter.format(value)
}
function errorKind(error: unknown) {
  const text = error instanceof Error ? error.message : String(error ?? '')
  if (/403|forbidden|权限/i.test(text)) return 'forbidden'
  if (/abort|timeout|超时/i.test(text)) return 'timeout'
  return 'load'
}
function isAbort(error: unknown) {
  return error instanceof Error && error.name === 'AbortError'
}
function isPartial(warnings: string[]) {
  return warnings.some((warning) => /partial|incomplete|missing|不完整|缺失/i.test(warning))
}
function classified(metrics: BatchSalesMetrics, field: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity', unavailable = false) {
  const value = getBatchProductSalesClassifiedQuantity(metrics, field, unavailable)
  return value === null ? '—' : number(value)
}
function discountRate(metrics: BatchSalesMetrics) {
  return metrics.quantity <= 0 || metrics.discountStatus !== 'complete' || metrics.unknownQuantity !== 0 || metrics.regularQuantity < 0 || metrics.discountQuantity < 0 ? '—' : `${((metrics.discountQuantity / metrics.quantity) * 100).toFixed(1)}%`
}
function share(value: number, total: number) {
  return total === 0 ? null : value / total
}
function percent(value: number | null) {
  return value === null ? '—' : `${(value * 100).toFixed(1)}%`
}
function range(value1: number | null, value2: number | null) {
  return value1 === null || value2 === null ? '—' : value1 === value2 ? money(value1) : `${money(value1)} – ${money(value2)}`
}
function downloadCsv(fileName: string, rows: unknown[][]) {
  const content = `\uFEFF${rows.map(formatCsvRow).join('\r\n')}\r\n`
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }))
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

/** 所有商品明细都完整返回时才能补零日，部分失败始终保留为缺失。 */
function fillKnownDays(data: BatchSalesDaily[], scope: BatchSalesScope, canFill: boolean): BatchSalesDaily[] {
  if (!canFill) return data
  const byDate = new Map(data.map((item) => [item.date, item]))
  const dates: BatchSalesDaily[] = []
  for (let date = dayjs(scope.startDate); !date.isAfter(scope.endDate, 'day'); date = date.add(1, 'day')) {
    const key = date.format('YYYY-MM-DD')
    dates.push(
      byDate.get(key) ?? {
        date: key,
        metrics: {
          quantity: 0,
          regularQuantity: 0,
          discountQuantity: 0,
          unknownQuantity: 0,
          returnQuantity: 0,
          salesAmount: 0,
          discountStatus: 'complete',
          originalPriceMin: null,
          originalPriceMax: null,
          discountPriceMin: null,
          discountPriceMax: null,
        },
      },
    )
  }
  return dates
}

function LoadState({ loading, error, empty, onRetry, children }: { loading: boolean; error?: string; empty?: boolean; onRetry?: () => void; children: React.ReactNode }) {
  const { t } = useTranslation()
  if (loading)
    return (
      <div className={styles.state}>
        <Skeleton active title={false} paragraph={{ rows: 4 }} />
      </div>
    )
  if (error)
    return (
      <Alert
        type="error"
        showIcon
        message={t('batchProductSalesAnalysis.errors.title')}
        description={t(`batchProductSalesAnalysis.errors.${error}`)}
        action={
          onRetry ? (
            <Button size="small" onClick={onRetry}>
              {t('batchProductSalesAnalysis.retry')}
            </Button>
          ) : undefined
        }
      />
    )
  if (empty) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noDailyData')} />
  return <>{children}</>
}

export default function BatchProductSalesAnalysisPage({ api = batchProductSalesApi }: BatchProductSalesAnalysisPageProps) {
  const { t } = useTranslation()
  const today = useMemo(businessToday, [])
  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(() => defaultRange(today))
  const [draftStores, setDraftStores] = useState<string[]>([])
  const [pastedText, setPastedText] = useState('')
  const [importResult, setImportResult] = useState<ImportResult>(() => parsePastedItemNumbers(''))
  const [options, setOptions] = useState<{
    stores: { code: string; name: string }[]
    maxItemNumbers: number
    maxDays: number
  }>()
  const [optionsLoading, setOptionsLoading] = useState(true)
  const [optionsError, setOptionsError] = useState<string>()
  const [queryResult, setQueryResult] = useState<BatchSalesQueryResult>()
  const [appliedScope, setAppliedScope] = useState<BatchSalesScope>()
  const [submittedInput, setSubmittedInput] = useState<{
    scope: BatchSalesScope
    itemNumbers: string[]
  }>()
  const [queryLoading, setQueryLoading] = useState(false)
  const [queryError, setQueryError] = useState<string>()
  const [detailsByProduct, setDetailsByProduct] = useState<Record<string, BatchSalesDetail>>({})
  const [detailFailures, setDetailFailures] = useState<Record<string, string>>({})
  const [detailProgress, setDetailProgress] = useState({
    completed: 0,
    total: 0,
  })
  const [detailLoading, setDetailLoading] = useState(false)
  const [selectedProductCode, setSelectedProductCode] = useState(ALL_PRODUCTS)
  const [selectedBranchCode, setSelectedBranchCode] = useState<string>()
  const [branchSelectionTouched, setBranchSelectionTouched] = useState(false)
  const [scopeOpen, setScopeOpen] = useState(false)
  const [productSearch, setProductSearch] = useState('')
  const [branchSearch, setBranchSearch] = useState('')
  const [branchTopN, setBranchTopN] = useState(20)
  const [onlyBranchesWithSales, setOnlyBranchesWithSales] = useState(false)
  const [discountPollRevision, setDiscountPollRevision] = useState(0)
  const queryAbortRef = useRef<AbortController>()
  const detailAbortRef = useRef<AbortController>()
  const refreshAbortRefs = useRef(new Map<string, AbortController>())
  const queryRequestRef = useRef(0)
  const detailSessionRef = useRef(0)
  const refreshRequestRefs = useRef(new Map<string, number>())
  const optionsRequestRef = useRef(0)

  const loadOptions = useCallback(() => {
    const controller = new AbortController()
    const request = ++optionsRequestRef.current
    setOptionsLoading(true)
    setOptionsError(undefined)
    api
      .getOptions(controller.signal)
      .then((result) => {
        if (request === optionsRequestRef.current) setOptions(result)
      })
      .catch((error) => {
        if (!isAbort(error) && request === optionsRequestRef.current) setOptionsError(errorKind(error))
      })
      .finally(() => {
        if (request === optionsRequestRef.current) setOptionsLoading(false)
      })
    return () => {
      controller.abort()
      if (request === optionsRequestRef.current) optionsRequestRef.current += 1
    }
  }, [api])
  useEffect(() => loadOptions(), [loadOptions])
  useEffect(
    () => () => {
      queryAbortRef.current?.abort()
      detailAbortRef.current?.abort()
      refreshAbortRefs.current.forEach((controller) => controller.abort())
    },
    [],
  )

  const loadAllDetails = useCallback(
    async (products: BatchSalesProductSummary[], scope: BatchSalesScope, replace = true) => {
      detailAbortRef.current?.abort()
      const controller = new AbortController()
      detailAbortRef.current = controller
      const session = ++detailSessionRef.current
      if (replace) {
        setDetailsByProduct({})
        setDetailFailures({})
      } else {
        setDetailFailures((current) => {
          const next = { ...current }
          products.forEach((product) => delete next[product.productCode])
          return next
        })
      }
      setDetailProgress({ completed: 0, total: products.length })
      setDetailLoading(products.length > 0)
      await runBatchProductSalesPool(products, DETAIL_CONCURRENCY, async (currentProduct) => {
        try {
          const result = await api.getDetail({ ...scope, productCode: currentProduct.productCode }, controller.signal)
          if (session !== detailSessionRef.current) return
          if (result.statisticStatus && result.statisticStatus.toLowerCase() !== 'fresh') throw new Error('statistics')
          const branches = [...result.branches].sort((left, right) => right.metrics.quantity - left.metrics.quantity)
          setDetailsByProduct((current) => ({
            ...current,
            [currentProduct.productCode]: { ...result, branches },
          }))
        } catch (error) {
          if (isAbort(error) || session !== detailSessionRef.current) return
          const kind = error instanceof Error && error.message === 'statistics' ? 'statistics' : errorKind(error)
          setDetailFailures((current) => ({
            ...current,
            [currentProduct.productCode]: kind,
          }))
        } finally {
          if (session === detailSessionRef.current && !controller.signal.aborted)
            setDetailProgress((current) => ({
              ...current,
              completed: current.completed + 1,
            }))
        }
      })
      if (session === detailSessionRef.current) setDetailLoading(false)
    },
    [api],
  )
  const refreshDetail = useCallback(
    async (productCode: string, scope: BatchSalesScope) => {
      refreshAbortRefs.current.get(productCode)?.abort()
      const controller = new AbortController()
      refreshAbortRefs.current.set(productCode, controller)
      const request = (refreshRequestRefs.current.get(productCode) ?? 0) + 1
      refreshRequestRefs.current.set(productCode, request)
      const session = detailSessionRef.current

      try {
        const result = await api.getDetail({ ...scope, productCode }, controller.signal)
        if (session !== detailSessionRef.current || refreshRequestRefs.current.get(productCode) !== request || (result.statisticStatus && result.statisticStatus.toLowerCase() !== 'fresh')) return
        const branches = [...result.branches].sort((left, right) => right.metrics.quantity - left.metrics.quantity)
        setDetailsByProduct((current) => ({
          ...current,
          [productCode]: { ...result, branches },
        }))
        setDetailFailures((current) => {
          const { [productCode]: _removed, ...remaining } = current
          return remaining
        })
      } catch {
        // 后台轮询失败时保留上一次可靠结果，下一轮继续尝试。
      } finally {
        if (refreshAbortRefs.current.get(productCode) === controller) refreshAbortRefs.current.delete(productCode)
      }
    },
    [api],
  )

  const refreshPendingDetails = useCallback(
    async (productCodes: string[], scope: BatchSalesScope) => {
      await runBatchProductSalesPool(productCodes, DETAIL_CONCURRENCY, (productCode) => refreshDetail(productCode, scope))
    },
    [refreshDetail],
  )

  const dirty = !!submittedInput && (submittedInput.scope.startDate !== draftRange[0].format('YYYY-MM-DD') || submittedInput.scope.endDate !== draftRange[1].format('YYYY-MM-DD') || submittedInput.scope.storeCodes.join('|') !== draftStores.join('|') || submittedInput.itemNumbers.join('\u0001') !== importResult.itemNumbers.join('\u0001'))
  const productFilter = selectedProductCode === ALL_PRODUCTS ? undefined : selectedProductCode
  const rankAnalysis = useMemo(
    () =>
      buildBatchProductSalesAnalysis(Object.values(detailsByProduct), {
        productCode: productFilter,
      }),
    [detailsByProduct, productFilter],
  )
  const activeBranchCode = branchSelectionTouched ? selectedBranchCode : rankAnalysis.branches[0]?.branchCode
  const analysis = useMemo(
    () =>
      buildBatchProductSalesAnalysis(Object.values(detailsByProduct), {
        productCode: productFilter,
        branchCode: activeBranchCode,
      }),
    [activeBranchCode, detailsByProduct, productFilter],
  )
  const selectedProduct = queryResult?.products.find((product) => product.productCode === productFilter)
  const selectedBranch = rankAnalysis.branches.find((branch) => branch.branchCode === activeBranchCode)
  const selectedDetails = useMemo(() => Object.values(detailsByProduct).filter((detail) => !productFilter || detail.product.productCode === productFilter), [detailsByProduct, productFilter])
  const discountNotice = useMemo(() => {
    const priority = ['Failed', 'OutOfSync', 'Superseded', 'Unavailable', 'Partial', 'Refreshing', 'Backfilling', 'Running', 'Queued', 'Pending'] as const
    const counts = new Map<(typeof priority)[number], number>()
    selectedDetails.forEach((detail) => {
      if (!hasBatchProductSalesDiscountStatisticsNotice(detail.discountStatisticStatus)) return
      const state = getBatchProductSalesDiscountStateKey(detail.discountStatisticStatus)
      counts.set(state, (counts.get(state) ?? 0) + 1)
    })
    const primary = priority.find((state) => counts.has(state))
    if (!primary) return undefined
    const severeStates: ReadonlyArray<(typeof priority)[number]> = ['Failed', 'OutOfSync', 'Superseded', 'Unavailable', 'Partial']
    return {
      type: severeStates.includes(primary) ? ('warning' as const) : ('info' as const),
      message: priority
        .filter((state) => counts.has(state))
        .map((state) => `${t(`batchProductSalesAnalysis.discountStates.${state}`)} × ${counts.get(state)}`)
        .join(' · '),
    }
  }, [selectedDetails, t])
  const completeSelectedDetails = productFilter ? !!detailsByProduct[productFilter] && !detailFailures[productFilter] : !!queryResult && Object.keys(detailsByProduct).length + Object.keys(detailFailures).length === queryResult.products.length && !Object.keys(detailFailures).length
  const classificationUnavailable = selectedDetails.some((detail) => ['OutOfSync', 'Unavailable', 'Superseded'].includes(getBatchProductSalesDiscountStateKey(detail.discountStatisticStatus)))
  const totalDaily = appliedScope ? fillKnownDays(rankAnalysis.daily, appliedScope, completeSelectedDetails && !selectedDetails.some((detail) => isPartial(detail.warnings))) : []
  const branchDaily = appliedScope && activeBranchCode ? fillKnownDays(analysis.daily, appliedScope, completeSelectedDetails && !selectedDetails.some((detail) => isPartial(detail.warnings))) : []
  const appliedStoreText = useMemo(() => {
    if (!appliedScope?.storeCodes.length) return t('batchProductSalesAnalysis.allStores')
    const byCode = new Map((options?.stores ?? []).map((store) => [store.code, store]))
    if (appliedScope.storeCodes.length > 3)
      return t('batchProductSalesAnalysis.storeCount', {
        count: appliedScope.storeCodes.length,
      })
    return appliedScope.storeCodes
      .map((code) => {
        const store = byCode.get(code)
        return store ? `${store.name} (${code})` : code
      })
      .join(' · ')
  }, [appliedScope, options?.stores, t])
  useEffect(() => {
    if (detailLoading || !appliedScope) return
    const pendingProductCodes = selectedDetails.filter((detail) => shouldRefreshBatchProductSalesDiscountStatistics(detail.discountStatisticStatus)).map((detail) => detail.product.productCode)
    if (!pendingProductCodes.length) return
    const session = detailSessionRef.current
    const timer = window.setTimeout(() => {
      void refreshPendingDetails(pendingProductCodes, appliedScope).finally(() => {
        if (session === detailSessionRef.current) setDiscountPollRevision((current) => current + 1)
      })
    }, 5000)
    return () => window.clearTimeout(timer)
  }, [appliedScope, detailLoading, discountPollRevision, refreshPendingDetails, selectedDetails])

  const query = useCallback(() => {
    const startDate = draftRange[0].format('YYYY-MM-DD')
    const endDate = draftRange[1].format('YYYY-MM-DD')
    if (!importResult.itemNumbers.length) {
      setQueryError('load')
      return
    }
    if (getBatchProductSalesDateRangeError(startDate, endDate, today)) {
      setQueryError('date')
      return
    }
    queryAbortRef.current?.abort()
    detailAbortRef.current?.abort()
    refreshAbortRefs.current.forEach((controller) => controller.abort())
    refreshAbortRefs.current.clear()
    ++detailSessionRef.current
    const controller = new AbortController()
    queryAbortRef.current = controller
    const request = ++queryRequestRef.current
    const scope = { startDate, endDate, storeCodes: [...draftStores] }
    setProductSearch('')
    setBranchSearch('')
    setBranchTopN(20)
    setOnlyBranchesWithSales(false)
    setQueryLoading(true)
    setQueryError(undefined)
    setQueryResult(undefined)
    setAppliedScope(undefined)
    setDetailsByProduct({})
    setDetailFailures({})
    setDetailProgress({ completed: 0, total: 0 })
    setSelectedProductCode(ALL_PRODUCTS)
    setSelectedBranchCode(undefined)
    setBranchSelectionTouched(false)
    api
      .query({ ...scope, itemNumbers: importResult.itemNumbers }, controller.signal)
      .then((result) => {
        if (request !== queryRequestRef.current) return
        const resultScope = {
          startDate: result.startDate,
          endDate: result.endDate,
          storeCodes: result.storeCodes,
        }
        setQueryResult(result)
        setAppliedScope(resultScope)
        setSubmittedInput({
          scope,
          itemNumbers: [...importResult.itemNumbers],
        })
        void loadAllDetails(result.products, resultScope)
      })
      .catch((error) => {
        if (!isAbort(error) && request === queryRequestRef.current) setQueryError(errorKind(error))
      })
      .finally(() => {
        if (request === queryRequestRef.current) setQueryLoading(false)
      })
  }, [api, draftRange, draftStores, importResult.itemNumbers, loadAllDetails, today])
  const chooseProduct = (productCode: string) => {
    setSelectedProductCode(productCode)
    setSelectedBranchCode(undefined)
    setBranchSelectionTouched(false)
  }
  const chooseBranch = (branchCode: string) => {
    setSelectedBranchCode(branchCode)
    setBranchSelectionTouched(true)
  }
  const clearFilters = () => {
    setSelectedProductCode(ALL_PRODUCTS)
    setSelectedBranchCode(undefined)
    setBranchSelectionTouched(false)
    setProductSearch('')
    setBranchSearch('')
    setBranchTopN(20)
    setOnlyBranchesWithSales(false)
  }
  const contributingProductCounts = useMemo(() => {
    const counts = new Map<string, number>()
    selectedDetails.forEach((detail) => {
      detail.branches.forEach((branch) => {
        if (branch.metrics.quantity !== 0) counts.set(branch.branchCode, (counts.get(branch.branchCode) ?? 0) + 1)
      })
    })
    return counts
  }, [selectedDetails])
  const selectedProductCount = productFilter ? 1 : (queryResult?.products.length ?? 0)
  const branchRows = useMemo<BranchRankingRow[]>(
    () =>
      rankAnalysis.branches
        .map((branch, index) => ({
          ...branch,
          rank: index + 1,
          contributingProductCount: contributingProductCounts.get(branch.branchCode) ?? 0,
          selectedProductCount,
        }))
        .filter((branch) => `${branch.branchName} ${branch.branchCode}`.toLocaleLowerCase().includes(branchSearch.trim().toLocaleLowerCase()))
        .filter((branch) => !onlyBranchesWithSales || branch.metrics.quantity !== 0)
        .slice(0, branchTopN),
    [branchSearch, branchTopN, contributingProductCounts, onlyBranchesWithSales, rankAnalysis, selectedProductCount],
  )
  const branchColumns: ColumnsType<BranchRankingRow> = [
    {
      title: t('batchProductSalesAnalysis.columns.rank'),
      dataIndex: 'rank',
      width: 58,
      align: 'right',
    },
    {
      title: t('batchProductSalesAnalysis.columns.branch'),
      width: 130,
      render: (_, row) => (
        <button
          className={styles.branchButton}
          aria-pressed={row.branchCode === activeBranchCode}
          aria-controls="batch-product-sales-branch-trend batch-product-sales-branch-contribution"
          onClick={() => chooseBranch(row.branchCode)}
        >
          {row.branchName || row.branchCode}
        </button>
      ),
    },
    {
      title: t('batchProductSalesAnalysis.columns.quantity'),
      width: 72,
      align: 'right',
      render: (_, row) => number(row.metrics.quantity),
    },
    {
      title: t('batchProductSalesAnalysis.columns.amount'),
      width: 106,
      align: 'right',
      render: (_, row) => money(row.metrics.salesAmount),
    },
    {
      title: t('batchProductSalesAnalysis.columns.regular'),
      width: 68,
      align: 'right',
      render: (_, row) => classified(row.metrics, 'regularQuantity', classificationUnavailable),
    },
    {
      title: t('batchProductSalesAnalysis.columns.discount'),
      width: 68,
      align: 'right',
      render: (_, row) => classified(row.metrics, 'discountQuantity', classificationUnavailable),
    },
    {
      title: t('batchProductSalesAnalysis.columns.rate'),
      width: 84,
      align: 'right',
      render: (_, row) => discountRate(row.metrics),
    },
    {
      title: t('batchProductSalesAnalysis.columns.contributingProducts'),
      width: 108,
      align: 'right',
      render: (_, row) => `${number(row.contributingProductCount)} / ${number(row.selectedProductCount)}`,
    },
  ]
  const contributionColumns: ColumnsType<(typeof analysis.productContributions)[number]> = [
    {
      title: t('batchProductSalesAnalysis.columns.itemNumber'),
      width: 104,
      render: (_, row) => row.product.itemNumber,
    },
    {
      title: t('batchProductSalesAnalysis.columns.product'),
      width: 156,
      render: (_, row) => row.product.productName || row.product.englishName || row.product.productCode,
    },
    {
      title: t('batchProductSalesAnalysis.columns.quantity'),
      width: 72,
      align: 'right',
      render: (_, row) => number(row.metrics.quantity),
    },
    {
      title: t('batchProductSalesAnalysis.columns.amount'),
      width: 104,
      align: 'right',
      render: (_, row) => money(row.metrics.salesAmount),
    },
    {
      title: t('batchProductSalesAnalysis.columns.regular'),
      width: 66,
      align: 'right',
      render: (_, row) => classified(row.metrics, 'regularQuantity', classificationUnavailable),
    },
    {
      title: t('batchProductSalesAnalysis.columns.discount'),
      width: 66,
      align: 'right',
      render: (_, row) => classified(row.metrics, 'discountQuantity', classificationUnavailable),
    },
    {
      title: t('batchProductSalesAnalysis.columns.productShare'),
      width: 84,
      align: 'right',
      render: (_, row) => percent(share(row.metrics.quantity, analysis.metrics.quantity)),
    },
  ]
  const statisticsPending = !!queryResult?.statisticStatus && queryResult.statisticStatus.toLowerCase() !== 'fresh'
  const detailExportReady = completeSelectedDetails && !detailLoading && selectedDetails.length > 0 && selectedDetails.every((detail) => detail.metrics.discountStatus === 'complete' && !isPartial(detail.warnings))
  const exportClassifiedQuantity = (metrics: BatchSalesMetrics, field: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity') => getBatchProductSalesClassifiedQuantity(metrics, field, classificationUnavailable) ?? ''
  const exportSummary = () => queryResult && downloadCsv('batch-product-sales-summary.csv', [[t('batchProductSalesAnalysis.export.scope'), appliedScope?.startDate ?? '', appliedScope?.endDate ?? '', appliedScope?.storeCodes.join(' | ') || t('batchProductSalesAnalysis.allStores')], [t('batchProductSalesAnalysis.columns.itemNumber'), t('batchProductSalesAnalysis.columns.product'), t('batchProductSalesAnalysis.columns.quantity')], ...queryResult.products.map((product) => [product.itemNumber, product.productName || product.englishName || product.productCode, product.quantity])])
  const exportDetail = () =>
    appliedScope &&
    detailExportReady &&
    downloadCsv('batch-product-sales-detail.csv', [
      [t('batchProductSalesAnalysis.export.product'), selectedProduct?.productCode ?? t('batchProductSalesAnalysis.allProducts'), selectedProduct?.itemNumber ?? '', selectedProduct?.productName || selectedProduct?.englishName || ''],
      [t('batchProductSalesAnalysis.export.scope'), appliedScope.startDate, appliedScope.endDate, appliedScope.storeCodes.join(' | ') || t('batchProductSalesAnalysis.allStores')],
      [t('batchProductSalesAnalysis.export.daily')],
      [t('batchProductSalesAnalysis.columns.date'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
      ...totalDaily.filter(hasBatchProductSalesDailyActivity).map((day) => [day.date, day.metrics.quantity, exportClassifiedQuantity(day.metrics, 'regularQuantity'), exportClassifiedQuantity(day.metrics, 'discountQuantity'), exportClassifiedQuantity(day.metrics, 'unknownQuantity'), day.metrics.salesAmount]),
      [],
      [t('batchProductSalesAnalysis.export.branches')],
      [t('batchProductSalesAnalysis.columns.branch'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
      ...rankAnalysis.branches.map((branch) => [branch.branchName || branch.branchCode, branch.metrics.quantity, exportClassifiedQuantity(branch.metrics, 'regularQuantity'), exportClassifiedQuantity(branch.metrics, 'discountQuantity'), exportClassifiedQuantity(branch.metrics, 'unknownQuantity'), branch.metrics.salesAmount]),
      [],
      [t('batchProductSalesAnalysis.export.branchDaily')],
      [t('batchProductSalesAnalysis.columns.branch'), t('batchProductSalesAnalysis.columns.date'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
      ...rankAnalysis.branches.flatMap((branch) =>
        fillKnownDays(branch.daily, appliedScope, detailExportReady)
          .filter(hasBatchProductSalesDailyActivity)
          .map((day) => [branch.branchName || branch.branchCode, day.date, day.metrics.quantity, exportClassifiedQuantity(day.metrics, 'regularQuantity'), exportClassifiedQuantity(day.metrics, 'discountQuantity'), exportClassifiedQuantity(day.metrics, 'unknownQuantity'), day.metrics.salesAmount]),
      ),
    ])
  const detailWarnings = [...new Set(selectedDetails.flatMap((detail) => detail.warnings))]
  const filtersAreDefault = selectedProductCode === ALL_PRODUCTS && !branchSelectionTouched && !productSearch && !branchSearch && branchTopN === 20 && !onlyBranchesWithSales

  return (
    <div className={styles.screen}>
      <PageContainer title={t('batchProductSalesAnalysis.title')} subtitle={t('batchProductSalesAnalysis.subtitle')}>
        <div className={styles.page}>
          <section className={styles.toolbar} aria-label={t('batchProductSalesAnalysis.query')}>
            <Button onClick={() => setScopeOpen(true)}>
              {t('batchProductSalesAnalysis.selectItems', {
                count: importResult.itemNumbers.length,
              })}
            </Button>
            <label className={styles.field}>
              <span>{t('batchProductSalesAnalysis.dateRange')}</span>
              <RangePicker value={draftRange} allowClear={false} disabledDate={(date) => date.isAfter(dayjs(today), 'day')} onChange={(value) => value?.[0] && value?.[1] && setDraftRange([value[0], value[1]])} />
            </label>
            <label className={styles.field}>
              <span>{t('batchProductSalesAnalysis.stores')}</span>
              <Select
                mode="multiple"
                value={draftStores}
                loading={optionsLoading}
                className={styles.storeSelect}
                maxTagCount="responsive"
                placeholder={t('batchProductSalesAnalysis.allStores')}
                options={options?.stores.map((store) => ({
                  value: store.code,
                  label: `${store.name} (${store.code})`,
                }))}
                onChange={setDraftStores}
              />
            </label>
            <Button type="primary" icon={<SearchOutlined />} loading={queryLoading} disabled={!importResult.itemNumbers.length} onClick={query}>
              {queryLoading ? t('batchProductSalesAnalysis.querying') : t('batchProductSalesAnalysis.query')}
            </Button>
            <Dropdown
              trigger={['click']}
              menu={{
                items: [
                  {
                    key: 'summary',
                    label: t('batchProductSalesAnalysis.downloadSummary'),
                    disabled: !queryResult || statisticsPending,
                    onClick: exportSummary,
                  },
                  {
                    key: 'detail',
                    label: t('batchProductSalesAnalysis.downloadDetail'),
                    disabled: !detailExportReady,
                    onClick: exportDetail,
                  },
                ],
              }}
            >
              <Button icon={<DownloadOutlined />}>{t('batchProductSalesAnalysis.exportResults')}</Button>
            </Dropdown>
          </section>
          {optionsError ? (
            <Alert
              type="warning"
              showIcon
              message={t('batchProductSalesAnalysis.errors.load')}
              action={
                <Button size="small" icon={<ReloadOutlined />} onClick={loadOptions}>
                  {t('batchProductSalesAnalysis.retry')}
                </Button>
              }
            />
          ) : null}
          {dirty ? <Alert type="info" showIcon message={t('batchProductSalesAnalysis.pendingQuery')} /> : null}
          {appliedScope ? (
            <div className={styles.appliedScope}>
              {t('batchProductSalesAnalysis.appliedScope', {
                startDate: appliedScope.startDate,
                endDate: appliedScope.endDate,
                stores: appliedStoreText,
              })}
            </div>
          ) : null}
          {!statisticsPending && queryResult?.warnings.length ? (
            <details className={styles.hint}>
              <summary>{t('batchProductSalesAnalysis.dataNotes')}</summary>
              {queryResult.warnings.map((warning) => (
                <p key={warning}>{warning}</p>
              ))}
            </details>
          ) : null}
          {detailLoading ? <Alert type="info" showIcon message={t('batchProductSalesAnalysis.detailLoadingProgress', detailProgress)} description={<Progress percent={detailProgress.total ? Math.round((detailProgress.completed / detailProgress.total) * 100) : 0} size="small" showInfo />} /> : null}
          {!detailLoading && Object.keys(detailFailures).length ? (
            <Alert
              type="warning"
              showIcon
              message={t('batchProductSalesAnalysis.partialDetailFailure', {
                count: Object.keys(detailFailures).length,
              })}
              action={
                queryResult && appliedScope ? (
                  <Button
                    size="small"
                    onClick={() =>
                      void loadAllDetails(
                        queryResult.products.filter((product) => detailFailures[product.productCode]),
                        appliedScope,
                        false,
                      )
                    }
                  >
                    {t('batchProductSalesAnalysis.retry')}
                  </Button>
                ) : undefined
              }
            />
          ) : null}
          {discountNotice ? (
            <Alert
              type={discountNotice.type}
              showIcon
              message={discountNotice.message}
              action={
                productFilter && appliedScope ? (
                  <Button size="small" onClick={() => refreshDetail(productFilter, appliedScope)}>
                    {t('batchProductSalesAnalysis.refreshStatus')}
                  </Button>
                ) : undefined
              }
            />
          ) : null}
          <main className={styles.layout}>
            <aside className={`${styles.column} ${styles.leftColumn}`}>
              <section className={`${styles.panel} ${styles.productPanel}`}>
                <header className={styles.panelHeader}>
                  <h2>
                    {t('batchProductSalesAnalysis.productList', {
                      count: queryResult?.products.length ?? 0,
                    })}
                  </h2>
                  {queryResult && !statisticsPending ? (
                    <span className={styles.panelMeta}>
                      {t('batchProductSalesAnalysis.totalQuantity', {
                        value: number(queryResult.products.reduce((sum, product) => sum + product.quantity, 0)),
                      })}
                    </span>
                  ) : null}
                </header>
                <Input allowClear prefix={<SearchOutlined />} aria-label={t('batchProductSalesAnalysis.searchProducts')} placeholder={t('batchProductSalesAnalysis.searchProducts')} value={productSearch} onChange={(event) => setProductSearch(event.target.value)} />
                {statisticsPending && !queryLoading ? (
                  <Alert
                    type="info"
                    showIcon
                    message={t('batchProductSalesAnalysis.statisticsPending')}
                    description={queryResult?.warnings.map((warning) => (
                      <div key={warning}>{warning}</div>
                    ))}
                    action={
                      <Button size="small" onClick={query}>
                        {t('batchProductSalesAnalysis.retry')}
                      </Button>
                    }
                  />
                ) : (
                  <LoadState loading={queryLoading} error={queryError} empty={!!queryResult && !queryResult.products.length} onRetry={query}>
                    {queryResult ? (
                      <div
                        className={styles.productList}
                        role="region"
                        aria-label={t('batchProductSalesAnalysis.productList', {
                          count: queryResult.products.length,
                        })}
                        tabIndex={0}
                      >
                        <button className={`${styles.productRow} ${selectedProductCode === ALL_PRODUCTS ? styles.productCurrent : ''}`} aria-pressed={selectedProductCode === ALL_PRODUCTS} onClick={() => chooseProduct(ALL_PRODUCTS)}>
                          <span className={styles.allProductsIcon}>Σ</span>
                          <span className={styles.productInfo}>
                            <strong>{t('batchProductSalesAnalysis.allProducts')}</strong>
                            <span>{t('batchProductSalesAnalysis.allProductsHint')}</span>
                          </span>
                          <b>{number(queryResult.products.reduce((sum, product) => sum + product.quantity, 0))}</b>
                        </button>
                        {queryResult.products
                          .filter((product) => `${product.itemNumber} ${product.productName} ${product.englishName ?? ''}`.toLocaleLowerCase().includes(productSearch.trim().toLocaleLowerCase()))
                          .map((product) => (
                            <button key={product.productCode} className={`${styles.productRow} ${selectedProductCode === product.productCode ? styles.productCurrent : ''}`} aria-pressed={selectedProductCode === product.productCode} onClick={() => chooseProduct(product.productCode)}>
                              <ProductImage src={product.imageUrl} alt={product.productName || product.itemNumber} />
                              <span className={styles.productInfo}>
                                <strong>{product.itemNumber}</strong>
                                <span>{product.productName || product.englishName || product.productCode}</span>
                              </span>
                              <b>{number(product.quantity)}</b>
                            </button>
                          ))}
                      </div>
                    ) : (
                      <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noQueryResult')} />
                    )}
                  </LoadState>
                )}
                {queryResult?.matches.length ? (
                  <details className={styles.details}>
                    <summary>
                      {t('batchProductSalesAnalysis.matched')} {queryResult.matches.filter((match) => match.status === 'matched').length}
                      {' · '}
                      {t('batchProductSalesAnalysis.notFound')} {queryResult.matches.filter((match) => match.status === 'notFound').length}
                      {' · '}
                      {t('batchProductSalesAnalysis.ambiguous')} {queryResult.matches.filter((match) => match.status === 'ambiguous').length}
                    </summary>
                    {queryResult.matches.map((match) => (
                      <div key={match.itemNumber}>
                        <span>{match.itemNumber}</span>
                        <span>{match.productCodes.join(', ') || '—'}</span>
                        <Tag color={match.status === 'matched' ? 'success' : match.status === 'ambiguous' ? 'warning' : 'error'}>{t(`batchProductSalesAnalysis.${match.status === 'notFound' ? 'notFound' : match.status}`)}</Tag>
                      </div>
                    ))}
                  </details>
                ) : null}
              </section>
            </aside>
            <section className={`${styles.panel} ${styles.totalTrendPanel}`}>
              <header className={styles.panelHeader}>
                <h2>{t('batchProductSalesAnalysis.dailyTrend')}</h2>
                <span className={styles.panelMeta}>{selectedProduct ? `${selectedProduct.itemNumber} · ${selectedProduct.productName || selectedProduct.englishName || selectedProduct.productCode}` : t('batchProductSalesAnalysis.allProducts')}</span>
              </header>
              <LoadState loading={detailLoading && !selectedDetails.length} empty={!detailLoading && !!queryResult && !totalDaily.length}>
                {selectedDetails.length ? (
                  <>
                    <DiscountDailyChart data={totalDaily} ariaLabel={t('batchProductSalesAnalysis.dailyTrend')} classificationUnavailable={classificationUnavailable} />
                    <div className={styles.branchTotals}>
                      <span>
                        {t('batchProductSalesAnalysis.metrics.quantity')} <b>{number(rankAnalysis.metrics.quantity)}</b>
                      </span>
                      <span className={styles.amount}>
                        {t('batchProductSalesAnalysis.metrics.amount')} <b>{money(rankAnalysis.metrics.salesAmount)}</b>
                      </span>
                      <span className={styles.regular}>
                        {t('batchProductSalesAnalysis.metrics.regular')} <b>{classified(rankAnalysis.metrics, 'regularQuantity', classificationUnavailable)}</b>
                      </span>
                      <span className={styles.discount}>
                        {t('batchProductSalesAnalysis.metrics.discount')} <b>{classified(rankAnalysis.metrics, 'discountQuantity', classificationUnavailable)}</b>
                      </span>
                      <span>
                        {t('batchProductSalesAnalysis.discountRate')} <b>{discountRate(rankAnalysis.metrics)}</b>
                      </span>
                    </div>
                    <p className={styles.priceHint}>
                      {t('batchProductSalesAnalysis.priceRange', {
                        original: range(rankAnalysis.metrics.originalPriceMin, rankAnalysis.metrics.originalPriceMax),
                        discount: range(rankAnalysis.metrics.discountPriceMin, rankAnalysis.metrics.discountPriceMax),
                      })}
                    </p>
                  </>
                ) : (
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noQueryResult')} />
                )}
              </LoadState>
            </section>
            <section id="batch-product-sales-branch-trend" className={`${styles.panel} ${styles.branchTrendPanel}`}>
              <header className={styles.panelHeader}>
                <h2>{t('batchProductSalesAnalysis.branchDailyTrend')}</h2>
                {selectedBranch ? <span className={styles.panelMeta}>{selectedBranch.branchName || selectedBranch.branchCode}</span> : null}
              </header>
              {selectedBranch && activeBranchCode ? (
                <>
                  <DiscountDailyChart
                    data={branchDaily}
                    ariaLabel={t('batchProductSalesAnalysis.branchTrend', {
                      branch: selectedBranch.branchName || selectedBranch.branchCode,
                    })}
                    classificationUnavailable={classificationUnavailable}
                  />
                  <div className={styles.branchTotals}>
                    <span>
                      {t('batchProductSalesAnalysis.metrics.quantity')} <b>{number(analysis.metrics.quantity)}</b>
                    </span>
                    <span className={styles.amount}>
                      {t('batchProductSalesAnalysis.metrics.amount')} <b>{money(analysis.metrics.salesAmount)}</b>
                    </span>
                    <span className={styles.regular}>
                      {t('batchProductSalesAnalysis.metrics.regular')} <b>{classified(analysis.metrics, 'regularQuantity', classificationUnavailable)}</b>
                    </span>
                    <span className={styles.discount}>
                      {t('batchProductSalesAnalysis.metrics.discount')} <b>{classified(analysis.metrics, 'discountQuantity', classificationUnavailable)}</b>
                    </span>
                  </div>
                </>
              ) : (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.selectBranch')} />
              )}
            </section>
            <section className={`${styles.panel} ${styles.dailyDetailPanel}`}>
              <header className={styles.panelHeader}>
                <h2>{t('batchProductSalesAnalysis.branchRanking')}</h2>
                <Button type="link" size="small" disabled={filtersAreDefault} onClick={clearFilters}>
                  {t('batchProductSalesAnalysis.clearFilters')}
                </Button>
              </header>
              {detailWarnings.map((warning) => (
                <Alert key={warning} className={styles.warning} type="warning" showIcon message={warning} />
              ))}
              <div className={styles.tableFilters}>
                <Input allowClear size="small" prefix={<SearchOutlined />} placeholder={t('batchProductSalesAnalysis.searchBranches')} value={branchSearch} onChange={(event) => setBranchSearch(event.target.value)} />
                <Select
                  size="small"
                  value={branchTopN}
                  options={[10, 20, 50, 100].map((value) => ({
                    value,
                    label: t('batchProductSalesAnalysis.topN', {
                      count: value,
                    }),
                  }))}
                  onChange={setBranchTopN}
                />
                <label>
                  <input type="checkbox" checked={onlyBranchesWithSales} onChange={(event) => setOnlyBranchesWithSales(event.target.checked)} /> {t('batchProductSalesAnalysis.onlyBranchesWithSales')}
                </label>
              </div>
              <div className={styles.tableWrap}>{branchRows.length ? <MeasuredTable metricId="executive-sales-intelligence.batch-product-sales-analysis.branch-ranking" size="small" rowKey="branchCode" columns={branchColumns} dataSource={branchRows} rowClassName={(row) => (row.branchCode === activeBranchCode ? styles.branchCurrent : '')} pagination={false} scroll={{ x: 674 }} /> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noBranchData')} />}</div>
            </section>
            <section id="batch-product-sales-branch-contribution" className={`${styles.panel} ${styles.branchSalesPanel}`}>
              <header className={styles.panelHeader}>
                <h2>{t('batchProductSalesAnalysis.branchProductContribution')}</h2>
                {selectedBranch ? <span className={styles.panelMeta}>{selectedBranch.branchName || selectedBranch.branchCode}</span> : null}
              </header>
              {activeBranchCode ? <div className={styles.tableWrap}>{analysis.productContributions.length ? <MeasuredTable metricId="executive-sales-intelligence.batch-product-sales-analysis.branch-product-contribution" size="small" rowKey={(row) => row.product.productCode} columns={contributionColumns} dataSource={analysis.productContributions} pagination={false} scroll={{ x: 652 }} /> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noBranchData')} />}</div> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.selectBranch')} />}
            </section>
          </main>
          {scopeOpen ? (
            <ProductScopeModal
              initialText={pastedText}
              initialResult={importResult}
              maxItems={options?.maxItemNumbers ?? 500}
              onCancel={() => setScopeOpen(false)}
              onApply={(text, result) => {
                setPastedText(text)
                setImportResult(result)
                setScopeOpen(false)
              }}
            />
          ) : null}
        </div>
      </PageContainer>
    </div>
  )
}
