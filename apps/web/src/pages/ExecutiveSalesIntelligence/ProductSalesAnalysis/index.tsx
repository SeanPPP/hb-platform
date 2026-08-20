import {
  ArrowLeftOutlined,
  ClearOutlined,
  LineChartOutlined,
  ReloadOutlined,
  SearchOutlined,
  SelectOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  DatePicker,
  Empty,
  Input,
  Segmented,
  Select,
  Space,
  Spin,
  Table,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import PageContainer from '../../../components/PageContainer'
import {
  getProductSalesAnalysisOptions,
  queryProductSalesBranchDaily,
  queryProductSalesBranches,
  queryProductSalesCandidates,
  queryProductSalesDaily,
  queryProductSalesSummary,
} from '../../../services/productSalesAnalysisService'
import type {
  ProductSalesAnalysisEnvelope,
  ProductSalesAnalysisFilter,
  ProductSalesAnalysisOptions,
  ProductSalesAnalysisPaged,
  ProductSalesAnalysisProduct,
  ProductSalesAnalysisScopeMode,
  ProductSalesAnalysisSelection,
  ProductSalesBranch,
  ProductSalesDaily,
  ProductSalesSummaryRow,
} from '../../../types/productSalesAnalysis'
import DailySalesChart from './DailySalesChart'
import styles from './index.module.css'
import {
  applyCandidateSelect,
  applyCandidateSelectAll,
  buildDateRange,
  clearProductSelection,
  createLatestRequestGuard,
  createProductSalesAnalysisViewState,
  formatAud,
  formatSupplierNames,
  getDateRangeError,
  getSelectedProductCodes,
  invalidateRequests,
  isProductSelectionEmpty,
  productSalesAnalysisViewReducer,
  resetProductSelection,
  shouldTriggerTableRowClick,
  type DailyChartInputPoint,
} from './logic'

const { RangePicker } = DatePicker

const quantityFormatter = new Intl.NumberFormat('en-AU')

function formatQuantity(value: number): string {
  return quantityFormatter.format(value)
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback
}

function isAbortError(error: unknown) {
  return error instanceof Error && error.name === 'AbortError'
}

function toDateString(value: Dayjs): string {
  return value.format('YYYY-MM-DD')
}

function makeFilter(
  range: [Dayjs, Dayjs],
  keyword: string,
  australianSupplierCodes: string[],
  chinaSupplierCodes: string[],
): ProductSalesAnalysisFilter {
  return {
    startDate: toDateString(range[0]),
    endDate: toDateString(range[1]),
    keyword: keyword.trim() || undefined,
    australianSupplierCodes,
    chinaSupplierCodes,
  }
}

interface FreshnessMeta {
  statisticStatus?: string
  statisticMessage?: string
  statisticUpdatedAt?: string
  cacheVersion?: string
}

function updateFreshness<TPayload>(
  envelope: ProductSalesAnalysisEnvelope<TPayload>,
  setFreshness: (value: FreshnessMeta) => void,
) {
  setFreshness({
    statisticStatus: envelope.statisticStatus,
    statisticMessage: envelope.statisticMessage,
    statisticUpdatedAt: envelope.statisticUpdatedAt,
    cacheVersion: envelope.cacheVersion,
  })
}

function toChartPoints(daily: ProductSalesDaily[]): DailyChartInputPoint[] {
  return daily.map((item) => ({
    date: item.date,
    quantity: item.quantity,
    averageUnitPrice: item.averageUnitPrice,
  }))
}

function DailyTable({ data }: { data: ProductSalesDaily[] }) {
  const columns: ColumnsType<ProductSalesDaily> = [
    {
      title: '日期',
      dataIndex: 'date',
      width: 112,
      fixed: 'left',
    },
    {
      title: '净销量',
      dataIndex: 'quantity',
      align: 'right',
      width: 110,
      render: (value: number) => formatQuantity(value),
    },
    {
      title: '净销售额',
      dataIndex: 'salesAmount',
      align: 'right',
      width: 130,
      render: (value: number) => formatAud(value),
    },
    {
      title: '均价',
      dataIndex: 'averageUnitPrice',
      align: 'right',
      width: 110,
      render: (value: number | null) => formatAud(value),
    },
  ]

  return (
    <div className={styles.tableWrap}>
      <Table
        size="small"
        rowKey="date"
        columns={columns}
        dataSource={data}
        pagination={false}
        scroll={{ x: 'max-content' }}
      />
    </div>
  )
}

function ProductInfoCell({ product }: { product: ProductSalesAnalysisProduct }) {
  return (
    <div className={styles.productCell}>
      <Typography.Text strong className={styles.productName}>
        {product.productName || product.englishName || product.itemNumber || product.productCode}
      </Typography.Text>
      {product.productName && product.englishName ? (
        <span className={styles.productSecondary}>{product.englishName}</span>
      ) : null}
      <span className={styles.productSecondary}>
        {product.itemNumber || '—'} · {product.barcode || '—'}
      </span>
      <span className={styles.productSecondary}>
        澳: {formatSupplierNames(product.australianSuppliers)}
      </span>
      <span className={styles.productSecondary}>
        国内: {formatSupplierNames(product.chinaSuppliers)}
      </span>
    </div>
  )
}

function PanelState({
  loading,
  error,
  empty,
  onRetry,
  children,
}: {
  loading: boolean
  error?: string
  empty?: boolean
  onRetry: () => void
  children: React.ReactNode
}) {
  if (loading) {
    return (
      <div className={styles.stateBlock}>
        <Spin />
      </div>
    )
  }

  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        message="加载失败"
        description={error}
        action={(
          <Button size="small" onClick={onRetry}>
            重试
          </Button>
        )}
      />
    )
  }

  if (empty) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无数据" />
  }

  return <>{children}</>
}

export default function ProductSalesAnalysisPage() {
  const initialRange = useMemo<[Dayjs, Dayjs]>(() => {
    const range = buildDateRange(30)
    return [dayjs(range.startDate), dayjs(range.endDate)]
  }, [])
  // 默认统计范围截至昨天，但自定义日期仍允许选择今天，只禁止未来日期。
  const today = initialRange[1].add(1, 'day')

  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(initialRange)
  const [draftKeyword, setDraftKeyword] = useState('')
  const [draftAustralianCodes, setDraftAustralianCodes] = useState<string[]>([])
  const [draftChinaCodes, setDraftChinaCodes] = useState<string[]>([])
  const [activeQuickDays, setActiveQuickDays] = useState<number | null>(30)
  const [appliedFilter, setAppliedFilter] = useState<ProductSalesAnalysisFilter>(() =>
    makeFilter(initialRange, '', [], []),
  )
  const [view, dispatchView] = useReducer(
    productSalesAnalysisViewReducer,
    resetProductSelection(),
    createProductSalesAnalysisViewState,
  )
  const { selection, summaryPage, currentProductCode, middleView, selectedBranchCode } = view
  const [refreshVersion, setRefreshVersion] = useState(0)

  const [options, setOptions] = useState<ProductSalesAnalysisOptions>({
    australianSuppliers: [],
    chinaSuppliers: [],
  })
  const [optionsLoading, setOptionsLoading] = useState(true)
  const [optionsError, setOptionsError] = useState<string>()

  const [candidates, setCandidates] =
    useState<ProductSalesAnalysisPaged<ProductSalesAnalysisProduct> | null>(null)
  const [candidatePage, setCandidatePage] = useState(1)
  const [candidatePageSize, setCandidatePageSize] = useState(20)
  const [candidatesLoading, setCandidatesLoading] = useState(true)
  const [candidatesError, setCandidatesError] = useState<string>()

  const [summary, setSummary] =
    useState<ProductSalesAnalysisPaged<ProductSalesSummaryRow> | null>(null)
  const [summaryPageSize, setSummaryPageSize] = useState(20)
  const [summaryLoading, setSummaryLoading] = useState(true)
  const [summaryError, setSummaryError] = useState<string>()

  const [productDaily, setProductDaily] = useState<ProductSalesDaily[]>([])
  const [productDailyLoading, setProductDailyLoading] = useState(false)
  const [productDailyError, setProductDailyError] = useState<string>()

  const [rightScope, setRightScope] = useState<ProductSalesAnalysisScopeMode>('currentProduct')
  const [branches, setBranches] = useState<ProductSalesBranch[]>([])
  const [branchesLoading, setBranchesLoading] = useState(true)
  const [branchesError, setBranchesError] = useState<string>()
  const [branchDaily, setBranchDaily] = useState<ProductSalesDaily[]>([])
  const [branchDailyLoading, setBranchDailyLoading] = useState(false)
  const [branchDailyError, setBranchDailyError] = useState<string>()

  const [freshness, setFreshness] = useState<FreshnessMeta>({})

  const optionsGuardRef = useRef(createLatestRequestGuard())
  const optionsAbortRef = useRef<AbortController>()
  const candidatesGuardRef = useRef(createLatestRequestGuard())
  const candidatesAbortRef = useRef<AbortController>()
  const summaryGuardRef = useRef(createLatestRequestGuard())
  const summaryAbortRef = useRef<AbortController>()
  const productDailyGuardRef = useRef(createLatestRequestGuard())
  const productDailyAbortRef = useRef<AbortController>()
  const branchesGuardRef = useRef(createLatestRequestGuard())
  const branchesAbortRef = useRef<AbortController>()
  const branchDailyGuardRef = useRef(createLatestRequestGuard())
  const branchDailyAbortRef = useRef<AbortController>()

  const selectionEmpty = isProductSelectionEmpty(selection)

  const commitFilters = useCallback(
    (range: [Dayjs, Dayjs], keyword: string, australianCodes: string[], chinaCodes: string[]) => {
      const rangeError = getDateRangeError(toDateString(range[0]), toDateString(range[1]))
      if (rangeError) {
        message.warning(rangeError)
        return
      }

      invalidateRequests([
        { controller: optionsAbortRef, guard: optionsGuardRef },
        { controller: candidatesAbortRef, guard: candidatesGuardRef },
        { controller: summaryAbortRef, guard: summaryGuardRef },
        { controller: productDailyAbortRef, guard: productDailyGuardRef },
        { controller: branchesAbortRef, guard: branchesGuardRef },
        { controller: branchDailyAbortRef, guard: branchDailyGuardRef },
      ])
      setDraftRange(range)
      setDraftKeyword(keyword)
      setDraftAustralianCodes(australianCodes)
      setDraftChinaCodes(chinaCodes)
      setAppliedFilter(makeFilter(range, keyword, australianCodes, chinaCodes))
      dispatchView({ type: 'commitFilter', selection: resetProductSelection() })
      setCandidatePage(1)
      setSummary(null)
      setSummaryError(undefined)
      setProductDaily([])
      setProductDailyError(undefined)
      setBranches([])
      setBranchesError(undefined)
      setBranchDaily([])
      setBranchDailyError(undefined)
      setRefreshVersion((value) => value + 1)
    },
    [],
  )

  const commitSelection = useCallback((nextSelection: ProductSalesAnalysisSelection) => {
    invalidateRequests([
      { controller: summaryAbortRef, guard: summaryGuardRef },
      { controller: productDailyAbortRef, guard: productDailyGuardRef },
      { controller: branchesAbortRef, guard: branchesGuardRef },
      { controller: branchDailyAbortRef, guard: branchDailyGuardRef },
    ])
    dispatchView({ type: 'commitSelection', selection: nextSelection })
    setSummary(null)
    setSummaryError(undefined)
    setProductDaily([])
    setProductDailyError(undefined)
    setBranches([])
    setBranchesError(undefined)
    setBranchDaily([])
    setBranchDailyError(undefined)
  }, [])

  const applyQuickRange = (days: number) => {
    const quickRange = buildDateRange(days)
    setActiveQuickDays(days)
    commitFilters(
      [dayjs(quickRange.startDate), dayjs(quickRange.endDate)],
      draftKeyword,
      draftAustralianCodes,
      draftChinaCodes,
    )
  }

  const resetFilters = () => {
    setActiveQuickDays(30)
    commitFilters(initialRange, '', [], [])
  }

  const refreshPage = () => {
    setRefreshVersion((value) => value + 1)
  }

  useEffect(() => {
    const abortController = new AbortController()
    optionsAbortRef.current?.abort()
    optionsAbortRef.current = abortController
    const requestId = optionsGuardRef.current.begin()
    setOptionsLoading(true)
    setOptionsError(undefined)

    getProductSalesAnalysisOptions(appliedFilter, abortController.signal)
      .then((envelope) => {
        if (!optionsGuardRef.current.isLatest(requestId)) return
        setOptions(envelope.data)
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!optionsGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setOptionsError(getErrorMessage(error, '供应商选项加载失败'))
      })
      .finally(() => {
        if (optionsGuardRef.current.isLatest(requestId)) setOptionsLoading(false)
      })

    return () => {
      abortController.abort()
      optionsGuardRef.current.invalidate()
    }
  }, [appliedFilter, refreshVersion])

  useEffect(() => {
    const abortController = new AbortController()
    candidatesAbortRef.current?.abort()
    candidatesAbortRef.current = abortController
    const requestId = candidatesGuardRef.current.begin()
    setCandidatesLoading(true)
    setCandidatesError(undefined)

    queryProductSalesCandidates(
      appliedFilter,
      resetProductSelection(),
      {
        pageNumber: candidatePage,
        pageSize: candidatePageSize,
        sortBy: 'salesAmount',
        sortDirection: 'desc',
      },
      abortController.signal,
    )
      .then((envelope) => {
        if (!candidatesGuardRef.current.isLatest(requestId)) return
        setCandidates(envelope.data)
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!candidatesGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setCandidatesError(getErrorMessage(error, '商品选择器加载失败'))
      })
      .finally(() => {
        if (candidatesGuardRef.current.isLatest(requestId)) setCandidatesLoading(false)
      })

    return () => {
      abortController.abort()
      candidatesGuardRef.current.invalidate()
    }
  }, [appliedFilter, candidatePage, candidatePageSize, refreshVersion])

  useEffect(() => {
    if (selectionEmpty) {
      summaryAbortRef.current?.abort()
      summaryGuardRef.current.invalidate()
      setSummary(null)
      setSummaryLoading(false)
      setSummaryError(undefined)
      return
    }

    const abortController = new AbortController()
    summaryAbortRef.current?.abort()
    summaryAbortRef.current = abortController
    const requestId = summaryGuardRef.current.begin()
    setSummaryLoading(true)
    setSummaryError(undefined)

    queryProductSalesSummary(
      appliedFilter,
      selection,
      { mode: 'selectedProducts' },
      {
        pageNumber: summaryPage,
        pageSize: summaryPageSize,
        sortBy: 'salesAmount',
        sortDirection: 'desc',
      },
      abortController.signal,
    )
      .then((envelope) => {
        if (!summaryGuardRef.current.isLatest(requestId)) return
        setSummary(envelope.data)
        dispatchView({ type: 'settleCurrentProduct', summaryItems: envelope.data.items })
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!summaryGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setSummaryError(getErrorMessage(error, '汇总加载失败'))
      })
      .finally(() => {
        if (summaryGuardRef.current.isLatest(requestId)) setSummaryLoading(false)
      })

    return () => {
      abortController.abort()
      summaryGuardRef.current.invalidate()
    }
  }, [
    appliedFilter,
    selection,
    summaryPage,
    summaryPageSize,
    refreshVersion,
    selectionEmpty,
  ])

  const productDailyScope = currentProductCode
    ? { mode: 'currentProduct' as const, productCode: currentProductCode }
    : null
  const shouldLoadProductDaily = middleView === 'daily' && productDailyScope && !selectionEmpty

  useEffect(() => {
    if (!shouldLoadProductDaily || !productDailyScope) {
      productDailyAbortRef.current?.abort()
      productDailyGuardRef.current.invalidate()
      setProductDaily([])
      setProductDailyLoading(false)
      setProductDailyError(undefined)
      return
    }

    const abortController = new AbortController()
    productDailyAbortRef.current?.abort()
    productDailyAbortRef.current = abortController
    const requestId = productDailyGuardRef.current.begin()
    setProductDailyLoading(true)
    setProductDailyError(undefined)

    queryProductSalesDaily(
      appliedFilter,
      selection,
      productDailyScope,
      abortController.signal,
    )
      .then((envelope) => {
        if (!productDailyGuardRef.current.isLatest(requestId)) return
        setProductDaily(envelope.data)
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!productDailyGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setProductDailyError(getErrorMessage(error, '商品日视图加载失败'))
      })
      .finally(() => {
        if (productDailyGuardRef.current.isLatest(requestId)) setProductDailyLoading(false)
      })

    return () => {
      abortController.abort()
      productDailyGuardRef.current.invalidate()
    }
  }, [appliedFilter, selection, currentProductCode, middleView, refreshVersion, selectionEmpty])

  const branchesScope = rightScope === 'currentProduct'
    ? currentProductCode
      ? { mode: 'currentProduct' as const, productCode: currentProductCode }
      : null
    : { mode: 'selectedProducts' as const }
  const shouldLoadBranches = branchesScope && (rightScope === 'selectedProducts' || currentProductCode)

  useEffect(() => {
    if (!shouldLoadBranches || !branchesScope || selectionEmpty) {
      branchesAbortRef.current?.abort()
      branchesGuardRef.current.invalidate()
      setBranches([])
      setBranchesLoading(false)
      setBranchesError(undefined)
      return
    }

    const abortController = new AbortController()
    branchesAbortRef.current?.abort()
    branchesAbortRef.current = abortController
    const requestId = branchesGuardRef.current.begin()
    setBranchesLoading(true)
    setBranchesError(undefined)

    queryProductSalesBranches(
      appliedFilter,
      selection,
      branchesScope,
      abortController.signal,
    )
      .then((envelope) => {
        if (!branchesGuardRef.current.isLatest(requestId)) return
        setBranches(envelope.data)
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!branchesGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setBranchesError(getErrorMessage(error, '分店明细加载失败'))
      })
      .finally(() => {
        if (branchesGuardRef.current.isLatest(requestId)) setBranchesLoading(false)
      })

    return () => {
      abortController.abort()
      branchesGuardRef.current.invalidate()
    }
  }, [
    appliedFilter,
    selection,
    rightScope,
    currentProductCode,
    refreshVersion,
    selectionEmpty,
  ])

  useEffect(() => {
    if (!selectedBranchCode || !branchesScope || selectionEmpty) {
      branchDailyAbortRef.current?.abort()
      branchDailyGuardRef.current.invalidate()
      setBranchDaily([])
      setBranchDailyLoading(false)
      setBranchDailyError(undefined)
      return
    }

    const abortController = new AbortController()
    branchDailyAbortRef.current?.abort()
    branchDailyAbortRef.current = abortController
    const requestId = branchDailyGuardRef.current.begin()
    setBranchDailyLoading(true)
    setBranchDailyError(undefined)

    queryProductSalesBranchDaily(
      appliedFilter,
      selection,
      branchesScope,
      selectedBranchCode,
      abortController.signal,
    )
      .then((envelope) => {
        if (!branchDailyGuardRef.current.isLatest(requestId)) return
        setBranchDaily(envelope.data)
        updateFreshness(envelope, setFreshness)
      })
      .catch((error) => {
        if (!branchDailyGuardRef.current.isLatest(requestId) || isAbortError(error)) return
        setBranchDailyError(getErrorMessage(error, '分店日视图加载失败'))
      })
      .finally(() => {
        if (branchDailyGuardRef.current.isLatest(requestId)) setBranchDailyLoading(false)
      })

    return () => {
      abortController.abort()
      branchDailyGuardRef.current.invalidate()
    }
  }, [
    appliedFilter,
    selection,
    rightScope,
    currentProductCode,
    selectedBranchCode,
    refreshVersion,
    selectionEmpty,
  ])

  const candidateCodes = useMemo(
    () => candidates?.items.map((item) => item.productCode) ?? [],
    [candidates?.items],
  )
  const selectedCandidateCodes = useMemo(
    () => getSelectedProductCodes(selection, candidateCodes),
    [candidateCodes, selection],
  )

  const handleSummaryRowClick = useCallback((product: ProductSalesSummaryRow) => {
    dispatchView({ type: 'setCurrentProduct', productCode: product.productCode })
    setRightScope('currentProduct')
    dispatchView({ type: 'setSelectedBranch', branchCode: null })
    setBranchDaily([])
    dispatchView({ type: 'setMiddleView', view: 'daily' })
  }, [])

  const handleBranchRowClick = useCallback((branch: ProductSalesBranch) => {
    dispatchView({ type: 'setSelectedBranch', branchCode: branch.branchCode })
    setBranchDaily([])
  }, [])

  const candidateColumns = useMemo<ColumnsType<ProductSalesAnalysisProduct>>(() => [
    {
      title: '商品',
      key: 'product',
      width: 240,
      render: (_, product) => <ProductInfoCell product={product} />,
    },
  ], [])

  const summaryColumns = useMemo<ColumnsType<ProductSalesSummaryRow>>(() => [
    {
      title: '商品',
      key: 'product',
      width: 220,
      render: (_, product) => <ProductInfoCell product={product} />,
    },
    {
      title: '净销量',
      dataIndex: ['metrics', 'quantity'],
      align: 'right',
      width: 100,
      render: (value: number) => <span className={styles.metricCell}>{formatQuantity(value)}</span>,
    },
    {
      title: '净销售额',
      dataIndex: ['metrics', 'salesAmount'],
      align: 'right',
      width: 130,
      render: (value: number) => <span className={styles.metricCell}>{formatAud(value)}</span>,
    },
    {
      title: '均价',
      dataIndex: ['metrics', 'averageUnitPrice'],
      align: 'right',
      width: 100,
      render: (value: number | null) => <span className={styles.metricCell}>{formatAud(value)}</span>,
    },
    {
      title: '',
      key: 'drilldown',
      align: 'center',
      width: 48,
      fixed: 'right',
      render: (_, product) => (
        <Button
          type="text"
          size="small"
          icon={<LineChartOutlined />}
          aria-label={`查看商品 ${product.productCode} 每日销量与均价`}
          onClick={() => handleSummaryRowClick(product)}
        />
      ),
    },
  ], [handleSummaryRowClick])

  const branchColumns = useMemo<ColumnsType<ProductSalesBranch>>(() => [
    {
      title: '分店',
      key: 'branch',
      width: 180,
      render: (_, branch) => (
        <div>
          <Typography.Text strong className={styles.productName}>
            {branch.branchName || branch.branchCode}
          </Typography.Text>
          <span className={styles.productSecondary}>{branch.branchCode}</span>
        </div>
      ),
    },
    {
      title: '净销量',
      dataIndex: ['metrics', 'quantity'],
      align: 'right',
      width: 90,
      render: (value: number) => formatQuantity(value),
    },
    {
      title: '净销售额',
      dataIndex: ['metrics', 'salesAmount'],
      align: 'right',
      width: 120,
      render: (value: number) => formatAud(value),
    },
    {
      title: '均价',
      dataIndex: ['metrics', 'averageUnitPrice'],
      align: 'right',
      width: 90,
      render: (value: number | null) => formatAud(value),
    },
    {
      title: '',
      key: 'drilldown',
      align: 'center',
      width: 48,
      fixed: 'right',
      render: (_, branch) => (
        <Button
          type="text"
          size="small"
          icon={<LineChartOutlined />}
          aria-label={`查看分店 ${branch.branchCode} 每日销量与均价`}
          onClick={() => handleBranchRowClick(branch)}
        />
      ),
    },
  ], [handleBranchRowClick])

  const supplierOptions = options.australianSuppliers.map((item) => ({
    label: item.name && item.name !== item.code ? `${item.code} · ${item.name}` : item.code,
    value: item.code,
  }))
  const chinaSupplierOptions = options.chinaSuppliers.map((item) => ({
    label: item.name && item.name !== item.code ? `${item.code} · ${item.name}` : item.code,
    value: item.code,
  }))

  const handleCandidateSelect = (product: ProductSalesAnalysisProduct, checked: boolean) => {
    commitSelection(applyCandidateSelect(selection, product.productCode, checked))
  }

  const handleCandidateSelectAll = (
    checked: boolean,
    _selectedRows: ProductSalesAnalysisProduct[],
    changeRows: ProductSalesAnalysisProduct[],
  ) => {
    commitSelection(
      applyCandidateSelectAll(
        selection,
        changeRows.map((product) => product.productCode),
        checked,
      ),
    )
  }

  const handleScopeChange = (value: ProductSalesAnalysisScopeMode) => {
    setRightScope(value)
    dispatchView({ type: 'setSelectedBranch', branchCode: null })
    setBranchDaily([])
  }

  const currentSummaryProduct = summary?.items.find((item) => item.productCode === currentProductCode)

  const freshnessText = [
    freshness.statisticStatus ? `状态 ${freshness.statisticStatus}` : '',
    freshness.statisticUpdatedAt ? `更新 ${freshness.statisticUpdatedAt}` : '',
    freshness.cacheVersion ? `水位 ${freshness.cacheVersion.slice(0, 8)}` : '',
  ].filter(Boolean).join(' · ')
  const freshnessNeedsAttention = Boolean(
    freshness.statisticStatus
    && freshness.statisticStatus.toLowerCase() !== 'fresh',
  )

  return (
    <PageContainer
      title="商品销量分析"
      subtitle="按商品、日期和分店查看净销量、净销售额与平均单价。"
    >
      <div className={styles.page}>
        <div className={styles.toolbar}>
          <div className={styles.toolbarItem}>
            <span className={styles.toolbarLabel}>日期范围</span>
            <RangePicker
              size="small"
              value={draftRange}
              allowClear={false}
              disabledDate={(date) => date.isAfter(today, 'day')}
              onChange={(value) => {
                if (!value?.[0] || !value[1]) return
                setDraftRange([value[0].startOf('day'), value[1].endOf('day')])
                setActiveQuickDays(null)
              }}
            />
          </div>

          <div className={styles.toolbarItem}>
            <span className={styles.toolbarLabel}>快捷</span>
            <div className={styles.quickButtons}>
              {[7, 30, 90].map((days) => (
                <button
                  key={days}
                  type="button"
                  className={`${styles.quickButton} ${activeQuickDays === days ? styles.quickButtonActive : ''}`}
                  onClick={() => applyQuickRange(days)}
                >
                  {days}天
                </button>
              ))}
            </div>
          </div>

          <Space>
            <Button
              size="small"
              type="primary"
              icon={<SearchOutlined />}
              onClick={() => commitFilters(draftRange, draftKeyword, draftAustralianCodes, draftChinaCodes)}
            >
              查询
            </Button>
            <Button size="small" onClick={resetFilters}>重置</Button>
            <Button size="small" icon={<ReloadOutlined />} onClick={refreshPage}>刷新</Button>
          </Space>

          <div className={`${styles.freshness} ${freshnessNeedsAttention ? styles.freshnessWarning : ''}`}>
            {freshnessText || '统计新鲜度加载中'}
            {freshness.statisticMessage ? (
              <div>{freshness.statisticMessage}</div>
            ) : null}
          </div>
        </div>

        <div className={styles.layout}>
          <div className={styles.column}>
            <div className={styles.panel}>
              <div className={styles.panelHeader}>
                <h3 className={styles.panelTitle}>商品选择</h3>
                <div className={styles.panelToolbar}>
                  <Button
                    size="small"
                    type="text"
                    icon={<SelectOutlined />}
                    onClick={() => commitSelection(resetProductSelection())}
                  >
                    全选筛选结果
                  </Button>
                  <Button
                    size="small"
                    type="text"
                    icon={<ClearOutlined />}
                    onClick={() => commitSelection(clearProductSelection())}
                  >
                    清空选择
                  </Button>
                </div>
              </div>

              <div className={styles.filterStack}>
                <label>
                  <span className={styles.toolbarLabel}>货号 / 中英文名称 / 条码</span>
                  <Input
                    size="small"
                    value={draftKeyword}
                    placeholder="货号 / 中英文名称 / 条码"
                    onChange={(event) => setDraftKeyword(event.target.value)}
                    onPressEnter={() => commitFilters(
                      draftRange,
                      draftKeyword,
                      draftAustralianCodes,
                      draftChinaCodes,
                    )}
                  />
                </label>
                <label>
                  <span className={styles.toolbarLabel}>澳洲供应商</span>
                  <Select
                    size="small"
                    mode="multiple"
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    value={draftAustralianCodes}
                    options={supplierOptions}
                    className={styles.filterSelect}
                    placeholder="全部"
                    onChange={setDraftAustralianCodes}
                  />
                </label>
                <label>
                  <span className={styles.toolbarLabel}>国内供应商</span>
                  <Select
                    size="small"
                    mode="multiple"
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    value={draftChinaCodes}
                    options={chinaSupplierOptions}
                    className={styles.filterSelect}
                    placeholder="全部"
                    onChange={setDraftChinaCodes}
                  />
                </label>
              </div>

              <div className={styles.selectorBar}>
                <span className={styles.selectorHint}>
                  {selection.mode === 'allFiltered'
                    ? '当前为筛选结果全选'
                    : '当前为指定商品'}
                </span>
              </div>

              <PanelState
                loading={candidatesLoading || optionsLoading}
                error={candidatesError || optionsError}
                empty={!candidatesLoading && candidates?.items.length === 0}
                onRetry={refreshPage}
              >
                <div className={`${styles.tableWrap} ${styles.candidateTableWrap}`}>
                  <Table
                    size="small"
                    rowKey="productCode"
                    columns={candidateColumns}
                    dataSource={candidates?.items ?? []}
                    loading={false}
                    pagination={{
                      current: candidatePage,
                      pageSize: candidatePageSize,
                      total: candidates?.total ?? 0,
                      showSizeChanger: true,
                      pageSizeOptions: [10, 20, 50, 100],
                      onChange: (page, pageSize) => {
                        setCandidatePage(page)
                        setCandidatePageSize(pageSize)
                      },
                    }}
                    rowSelection={{
                      selectedRowKeys: selectedCandidateCodes,
                      preserveSelectedRowKeys: true,
                      onSelect: handleCandidateSelect,
                      onSelectAll: handleCandidateSelectAll,
                    }}
                    scroll={{ x: 'max-content' }}
                  />
                </div>
              </PanelState>
            </div>
          </div>

          <div className={styles.column}>
            <div className={styles.panel}>
              <div className={styles.panelHeader}>
                <h3 className={styles.panelTitle}>选中商品汇总</h3>
                <Segmented
                  size="small"
                  value={middleView}
                  options={[
                    { label: '汇总', value: 'summary' },
                    { label: '当前商品日视图', value: 'daily' },
                  ]}
                  disabled={!currentProductCode}
                  onChange={(value) => dispatchView({ type: 'setMiddleView', view: value as 'summary' | 'daily' })}
                />
              </div>

              {selectionEmpty ? (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="请先选择商品" />
              ) : middleView === 'summary' ? (
                <PanelState
                  loading={summaryLoading}
                  error={summaryError}
                  empty={!summaryLoading && summary?.items.length === 0}
                  onRetry={refreshPage}
                >
                  <div className={styles.tableWrap}>
                    <Table
                      size="small"
                      rowKey="productCode"
                      columns={summaryColumns}
                      dataSource={summary?.items ?? []}
                      rowClassName={(row) => (
                        row.productCode === currentProductCode
                          ? `${styles.summaryRow} ${styles.summaryRowCurrent}`
                          : styles.summaryRow
                      )}
                      onRow={(row) => ({
                        onClick: (event) => {
                          if (!shouldTriggerTableRowClick(event.target, event.currentTarget)) return
                          handleSummaryRowClick(row)
                        },
                      })}
                      pagination={{
                        current: summaryPage,
                        pageSize: summaryPageSize,
                        total: summary?.total ?? 0,
                        showSizeChanger: true,
                        pageSizeOptions: [10, 20, 50, 100],
                        onChange: (page, pageSize) => {
                          dispatchView({ type: 'setSummaryPage', page })
                          setSummaryPageSize(pageSize)
                        },
                      }}
                      scroll={{ x: 'max-content' }}
                    />
                  </div>
                </PanelState>
              ) : (
                <PanelState
                  loading={productDailyLoading}
                  error={productDailyError}
                  empty={!productDailyLoading && productDaily.length === 0}
                  onRetry={refreshPage}
                >
                  <div className={styles.backBar}>
                    <Button
                      size="small"
                      type="text"
                      icon={<ArrowLeftOutlined />}
                      onClick={() => dispatchView({ type: 'setMiddleView', view: 'summary' })}
                    >
                      返回汇总
                    </Button>
                    <Typography.Text strong>
                      {currentSummaryProduct?.productName || currentSummaryProduct?.englishName || currentProductCode}
                    </Typography.Text>
                  </div>
                  <div className={styles.chartWrap}>
                    <DailySalesChart
                      data={toChartPoints(productDaily)}
                      ariaLabel={`${currentSummaryProduct?.productName || currentProductCode || '当前商品'} 每日销量与均价`}
                    />
                  </div>
                  <DailyTable data={productDaily} />
                </PanelState>
              )}
            </div>
          </div>

          <div className={`${styles.column} ${styles.rightColumn}`}>
            <div className={styles.panel}>
              <div className={styles.panelHeader}>
                <h3 className={styles.panelTitle}>分店明细</h3>
                <Segmented
                  size="small"
                  value={rightScope}
                  options={[
                    { label: '当前商品', value: 'currentProduct' },
                    { label: '所选商品合计', value: 'selectedProducts' },
                  ]}
                  onChange={(value) => handleScopeChange(value as ProductSalesAnalysisScopeMode)}
                />
              </div>

              {selectionEmpty || (rightScope === 'currentProduct' && !currentProductCode) ? (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={
                    rightScope === 'currentProduct' && !currentProductCode
                      ? '请先选择当前商品'
                      : '请先选择商品'
                  }
                />
              ) : selectedBranchCode ? (
                <PanelState
                  loading={branchDailyLoading}
                  error={branchDailyError}
                  empty={!branchDailyLoading && branchDaily.length === 0}
                  onRetry={refreshPage}
                >
                  <div className={styles.backBar}>
                    <Button
                      size="small"
                      type="text"
                      icon={<ArrowLeftOutlined />}
                      onClick={() => {
                        dispatchView({ type: 'setSelectedBranch', branchCode: null })
                        setBranchDaily([])
                      }}
                    >
                      返回分店
                    </Button>
                    <Typography.Text strong>
                      {branches.find((branch) => branch.branchCode === selectedBranchCode)?.branchName
                        || selectedBranchCode}
                    </Typography.Text>
                  </div>
                  <div className={styles.chartWrap}>
                    <DailySalesChart
                      data={toChartPoints(branchDaily)}
                      ariaLabel={`${branches.find((branch) => branch.branchCode === selectedBranchCode)?.branchName || selectedBranchCode} 每日销量与均价`}
                    />
                  </div>
                  <DailyTable data={branchDaily} />
                </PanelState>
              ) : (
                <PanelState
                  loading={branchesLoading}
                  error={branchesError}
                  empty={!branchesLoading && branches.length === 0}
                  onRetry={refreshPage}
                >
                  <div className={styles.tableWrap}>
                    <Table
                      size="small"
                      rowKey="branchCode"
                      columns={branchColumns}
                      dataSource={branches}
                      pagination={false}
                      onRow={(row) => ({
                        onClick: (event) => {
                          if (!shouldTriggerTableRowClick(event.target, event.currentTarget)) return
                          handleBranchRowClick(row)
                        },
                      })}
                      rowClassName={styles.summaryRow}
                      scroll={{ x: 'max-content' }}
                    />
                  </div>
                </PanelState>
              )}
            </div>
          </div>
        </div>
      </div>
    </PageContainer>
  )
}
