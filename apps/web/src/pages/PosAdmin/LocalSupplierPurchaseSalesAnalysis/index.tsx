import { ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Empty,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type {
  ColumnsType,
  FilterValue,
  SorterResult,
  TablePaginationConfig,
} from 'antd/es/table/interface'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getLocalSupplierPurchaseSalesAnalysis,
  getLocalSupplierPurchaseSalesAnalysisStoreOptions,
  getLocalSupplierPurchaseSalesAnalysisSupplierOptions,
} from '../../../services/localSupplierInvoiceService'
import type { StoreOption } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type {
  LocalSupplierPurchaseSalesAnalysisQueryDto,
  LocalSupplierPurchaseSalesAnalysisResponseDto,
  LocalSupplierPurchaseSalesAnalysisRowDto,
  LocalSupplierPurchaseSalesAnalysisSupplierOptionDto,
} from '../../../types/localSupplierInvoice'
import { buildStoreOptionsFromUserStores } from '../../../utils/managedStoreScope'
import {
  buildPurchaseSalesAnalysisImageSourceChain,
  DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE,
  getDefaultPurchaseSalesAnalysisDateRange,
  normalizePurchaseSalesAnalysisPageSize,
  PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY,
  PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER,
  PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS,
  TRANSPARENT_IMAGE_FALLBACK,
  toPurchaseSalesAnalysisSort,
} from './helpers'

const { RangePicker } = DatePicker
const { Text, Title } = Typography

type DateRangeValue = [Dayjs, Dayjs]
type SortOrderState = 'asc' | 'desc'
type MetricTone = 'interval' | 'between' | 'sales30' | 'sales60' | 'sales90'
type PurchaseTone = 'latest' | 'previous'

interface SearchFilters {
  storeCode?: string
  supplierCode?: string
  keyword: string
  orderDateRange: DateRangeValue
}

function formatDate(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD') : value
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}

function formatNumber(value?: number | null, digits = 0) {
  if (value === undefined || value === null || !Number.isFinite(value)) {
    return '--'
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })
}

const purchaseToneStyles: Record<PurchaseTone, { color: string; quantityColor: string; background: string }> = {
  latest: {
    color: '#1677ff',
    quantityColor: '#0958d9',
    background: '#e6f4ff',
  },
  previous: {
    color: '#d46b08',
    quantityColor: '#ad4e00',
    background: '#fff7e6',
  },
}

const metricToneStyles: Record<MetricTone, { color: string; background: string; borderColor: string }> = {
  interval: {
    color: '#ad6800',
    background: '#fff7e6',
    borderColor: '#ffd591',
  },
  between: {
    color: '#237804',
    background: '#f6ffed',
    borderColor: '#b7eb8f',
  },
  sales30: {
    color: '#0958d9',
    background: '#e6f4ff',
    borderColor: '#91caff',
  },
  sales60: {
    color: '#08979c',
    background: '#e6fffb',
    borderColor: '#87e8de',
  },
  sales90: {
    color: '#c41d7f',
    background: '#fff0f6',
    borderColor: '#ffadd2',
  },
}

function MetricBadge(props: { value?: number | null; tone: MetricTone; digits?: number }) {
  const style = metricToneStyles[props.tone]
  return (
    <Text
      style={{
        display: 'inline-flex',
        minWidth: 44,
        justifyContent: 'flex-end',
        padding: '1px 8px',
        borderRadius: 4,
        color: style.color,
        background: style.background,
        border: `1px solid ${style.borderColor}`,
        fontVariantNumeric: 'tabular-nums',
      }}
    >
      {formatNumber(props.value, props.digits)}
    </Text>
  )
}

function formatPurchase(date?: string | null, quantity?: number | null, tone: PurchaseTone = 'latest') {
  if (!date && (quantity === undefined || quantity === null)) {
    return '--'
  }

  const style = purchaseToneStyles[tone]
  return (
    <Space direction="vertical" size={0}>
      <Text strong style={{ color: style.color }}>
        {formatDate(date)}
      </Text>
      <Text
        style={{
          alignSelf: 'flex-start',
          padding: '1px 6px',
          borderRadius: 4,
          color: style.quantityColor,
          background: style.background,
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        {formatNumber(quantity, 2)}
      </Text>
    </Space>
  )
}

function ProductImageCell(props: {
  productImage?: string | null
  itemNumber?: string | null
  productCode?: string | null
  alt: string
}) {
  const sourceChain = useMemo(
    () =>
      buildPurchaseSalesAnalysisImageSourceChain(
        props.productImage,
        props.itemNumber,
        props.productCode,
      ),
    [props.itemNumber, props.productCode, props.productImage],
  )
  const [sourceIndex, setSourceIndex] = useState(0)

  useEffect(() => {
    setSourceIndex(0)
  }, [sourceChain])

  const currentSource =
    sourceChain[Math.min(sourceIndex, Math.max(sourceChain.length - 1, 0))] || TRANSPARENT_IMAGE_FALLBACK

  return (
    <img
      src={currentSource}
      alt={props.alt}
      loading="lazy"
      width={48}
      height={48}
      style={{
        width: 48,
        height: 48,
        objectFit: 'contain',
        borderRadius: 4,
        border: '1px solid #f0f0f0',
        background: '#fff',
      }}
      onError={() => {
        setSourceIndex((current) => (current < sourceChain.length - 1 ? current + 1 : current))
      }}
    />
  )
}

function buildInitialFilters(): SearchFilters {
  return {
    storeCode: undefined,
    supplierCode: undefined,
    keyword: '',
    orderDateRange: getDefaultPurchaseSalesAnalysisDateRange(),
  }
}

export default function LocalSupplierPurchaseSalesAnalysisPage() {
  const { t } = useTranslation()
  const currentUser = useAuthStore((state) => state.currentUser)
  const access = useAuthStore((state) => state.access)

  const scopedStoreCodes = useMemo(() => access.managedStoreCodes(), [access])
  const singleScopedStoreCode = useMemo(
    () => (Array.isArray(scopedStoreCodes) && scopedStoreCodes.length === 1 ? scopedStoreCodes[0] : undefined),
    [scopedStoreCodes],
  )
  const initialFilters = useMemo(() => {
    const filters = buildInitialFilters()
    filters.storeCode = singleScopedStoreCode
    return filters
  }, [singleScopedStoreCode])
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])
  const [supplierOptions, setSupplierOptions] = useState<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[]>([])
  const [storeOptionsLoading, setStoreOptionsLoading] = useState(false)
  const [supplierOptionsLoading, setSupplierOptionsLoading] = useState(false)
  const [draftFilters, setDraftFilters] = useState<SearchFilters>(initialFilters)
  const [filters, setFilters] = useState<SearchFilters>(initialFilters)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE)
  const [sortBy, setSortBy] = useState(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY)
  const [sortOrder, setSortOrder] = useState<SortOrderState>(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER)
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<LocalSupplierPurchaseSalesAnalysisResponseDto | null>(null)
  const [hasSearched, setHasSearched] = useState(false)
  const [queryVersion, setQueryVersion] = useState(0)
  const [tableScrollY, setTableScrollY] = useState<number>(520)

  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)

  const pageSizeOptions = useMemo(
    () => PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS.map((item) => String(item)),
    [],
  )
  const fallbackStoreOptions = useMemo(
    () => buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true }),
    [currentUser?.stores],
  )
  const requiresStoreSelectionBeforeSupplierOptions =
    Array.isArray(scopedStoreCodes) && scopedStoreCodes.length > 1 && !draftFilters.storeCode
  const hasRequiredDraftFilters = Boolean(draftFilters.storeCode && draftFilters.supplierCode)
  const hasRequiredCommittedFilters = Boolean(filters.storeCode && filters.supplierCode)

  useLayoutEffect(() => {
    const calc = () => {
      const containerHeight = wrapRef.current?.clientHeight || window.innerHeight
      const toolbarHeight = toolbarRef.current?.getBoundingClientRect().height || 0
      const available = containerHeight - toolbarHeight - 250
      setTableScrollY(available > 320 ? available : 320)
    }

    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [result?.items.length])

  useEffect(() => {
    let cancelled = false

    const loadStoreOptions = async () => {
      setStoreOptionsLoading(true)
      try {
        const stores = await getLocalSupplierPurchaseSalesAnalysisStoreOptions()
        if (!cancelled) {
          setStoreOptions(stores.length > 0 ? stores : fallbackStoreOptions)
        }
      } catch {
        if (!cancelled) {
          setStoreOptions(fallbackStoreOptions)
        }
      } finally {
        if (!cancelled) {
          setStoreOptionsLoading(false)
        }
      }
    }

    void loadStoreOptions()
    return () => {
      cancelled = true
    }
  }, [fallbackStoreOptions])

  useEffect(() => {
    if (!singleScopedStoreCode) {
      return
    }

    // 单门店用户默认带上唯一分店，避免页面看起来必须手动补一个已知条件。
    setDraftFilters((current) =>
      current.storeCode ? current : { ...current, storeCode: singleScopedStoreCode, supplierCode: undefined },
    )
    setFilters((current) =>
      current.storeCode ? current : { ...current, storeCode: singleScopedStoreCode, supplierCode: undefined },
    )
  }, [singleScopedStoreCode])

  useEffect(() => {
    let cancelled = false

    const loadSupplierOptions = async () => {
      if (requiresStoreSelectionBeforeSupplierOptions) {
        // 多门店普通用户必须先选分店；这里不预请求供应商，避免触发后端 STORE_REQUIRED。
        setSupplierOptionsLoading(false)
        setSupplierOptions([])
        setDraftFilters((current) =>
          current.supplierCode ? { ...current, supplierCode: undefined } : current,
        )
        return
      }

      setSupplierOptionsLoading(true)
      try {
        const suppliers = await getLocalSupplierPurchaseSalesAnalysisSupplierOptions(draftFilters.storeCode)
        if (cancelled) {
          return
        }

        setSupplierOptions(suppliers)
        setDraftFilters((current) => {
          if (!current.supplierCode || suppliers.some((supplier) => supplier.value === current.supplierCode)) {
            return current
          }

          // 分店变化后清掉不再属于该分店进货数据的供应商，避免提交出空结果。
          return { ...current, supplierCode: undefined }
        })
      } catch {
        if (!cancelled) {
          setSupplierOptions([])
        }
      } finally {
        if (!cancelled) {
          setSupplierOptionsLoading(false)
        }
      }
    }

    void loadSupplierOptions()
    return () => {
      cancelled = true
    }
  }, [draftFilters.storeCode, requiresStoreSelectionBeforeSupplierOptions])

  const loadData = useCallback(
    async (signal?: AbortSignal) => {
      if (!filters.storeCode || !filters.supplierCode) {
        setResult(null)
        return
      }

      const query: LocalSupplierPurchaseSalesAnalysisQueryDto = {
        storeCode: filters.storeCode,
        supplierCode: filters.supplierCode,
        orderDateStart: filters.orderDateRange[0].format('YYYY-MM-DD'),
        orderDateEnd: filters.orderDateRange[1].format('YYYY-MM-DD'),
        keyword: filters.keyword || undefined,
        sortBy,
        sortOrder,
        page,
        pageSize,
      }

      setLoading(true)
      try {
        const data = await getLocalSupplierPurchaseSalesAnalysis(query, signal)
        setResult(data)
      } catch (error) {
        if (!signal?.aborted) {
          message.error(
            error instanceof Error
              ? error.message
              : t(
                  'posAdmin.localSupplierPurchaseSalesAnalysis.loadFailed',
                  '分店供应商进货销量分析加载失败',
                ),
          )
        }
      } finally {
        if (!signal?.aborted) {
          setLoading(false)
        }
      }
    },
    [filters, page, pageSize, sortBy, sortOrder, t],
  )

  useEffect(() => {
    if (!hasSearched || !hasRequiredCommittedFilters) {
      setResult(null)
      return
    }

    const controller = new AbortController()
    void loadData(controller.signal)
    return () => controller.abort()
  }, [hasRequiredCommittedFilters, hasSearched, loadData, queryVersion])

  const columns = useMemo<ColumnsType<LocalSupplierPurchaseSalesAnalysisRowDto>>(
    () => [
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.image', '图片'),
        key: 'image',
        width: 76,
        fixed: 'left',
        render: (_value, record) => (
          <ProductImageCell
            productImage={record.productImage}
            itemNumber={record.itemNumber}
            productCode={record.productCode}
            alt={record.productName || record.productCode}
          />
        ),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.product', '货号 / 名称'),
        key: 'itemNumber',
        dataIndex: 'itemNumber',
        width: 260,
        fixed: 'left',
        sorter: true,
        sortOrder: sortBy === 'itemNumber' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (_value, record) => (
          <Space direction="vertical" size={0}>
            <Text strong>{record.productName || '--'}</Text>
            <Text style={{ color: '#0958d9' }}>{record.itemNumber || record.productCode}</Text>
            <Text type="secondary">{record.barcode || '--'}</Text>
          </Space>
        ),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.supplier', '供应商'),
        dataIndex: 'supplierCode',
        key: 'supplierName',
        width: 180,
        render: (_value, record) => (
          <Tag color="purple" style={{ marginInlineEnd: 0 }}>
            {record.supplierName || record.supplierCode}
          </Tag>
        ),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.latestPurchase', '最近进货'),
        dataIndex: 'latestPurchaseDate',
        key: 'latestPurchaseDate',
        width: 130,
        sorter: true,
        sortOrder: sortBy === 'latestPurchaseDate' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (_value, record) => formatPurchase(record.latestPurchaseDate, record.latestPurchaseQty, 'latest'),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.previousPurchase', '上次进货'),
        dataIndex: 'previousPurchaseDate',
        key: 'previousPurchaseDate',
        width: 130,
        sorter: true,
        sortOrder:
          sortBy === 'previousPurchaseDate' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (_value, record) => formatPurchase(record.previousPurchaseDate, record.previousPurchaseQty, 'previous'),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.intervalDays', '间隔天数'),
        dataIndex: 'purchaseIntervalDays',
        key: 'purchaseIntervalDays',
        width: 110,
        align: 'right',
        sorter: true,
        sortOrder:
          sortBy === 'purchaseIntervalDays' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (value: number | null | undefined) => <MetricBadge value={value} tone="interval" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.intervalSales', '间隔销量'),
        dataIndex: 'salesBetweenPurchases',
        key: 'salesBetweenPurchases',
        width: 110,
        align: 'right',
        sorter: true,
        sortOrder:
          sortBy === 'salesBetweenPurchases' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (value: number | null | undefined) => <MetricBadge value={value} tone="between" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.salesQty30', '30 天销量'),
        dataIndex: 'salesQty30',
        key: 'salesQty30',
        width: 110,
        align: 'right',
        sorter: true,
        sortOrder: sortBy === 'salesQty30' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (value: number) => <MetricBadge value={value} tone="sales30" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.salesQty60', '60 天销量'),
        dataIndex: 'salesQty60',
        key: 'salesQty60',
        width: 110,
        align: 'right',
        sorter: true,
        sortOrder: sortBy === 'salesQty60' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (value: number) => <MetricBadge value={value} tone="sales60" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.salesQty90', '90 天销量'),
        dataIndex: 'salesQty90',
        key: 'salesQty90',
        width: 110,
        align: 'right',
        sorter: true,
        sortOrder: sortBy === 'salesQty90' ? (sortOrder === 'asc' ? 'ascend' : 'descend') : null,
        render: (value: number) => <MetricBadge value={value} tone="sales90" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.updatedAt', '统计更新时间'),
        dataIndex: 'salesStatisticLastUpdate',
        key: 'salesStatisticLastUpdate',
        width: 170,
        render: (value: string | null | undefined) => formatDateTime(value),
      },
    ],
    [sortBy, sortOrder, t],
  )

  const handleSearch = () => {
    if (!draftFilters.storeCode || !draftFilters.supplierCode) {
      setResult(null)
      message.warning(
        t(
          'posAdmin.localSupplierPurchaseSalesAnalysis.validation.requiredFilters',
          '请先选择分店和供应商，再查询进货销量分析。',
        ),
      )
      return
    }

    // 查询按钮只在用户确认后提交关键词，避免输入过程频繁触发表格请求。
    setFilters({
      storeCode: draftFilters.storeCode,
      supplierCode: draftFilters.supplierCode,
      keyword: draftFilters.keyword.trim(),
      orderDateRange: draftFilters.orderDateRange,
    })
    setPage(1)
    setHasSearched(true)
    setQueryVersion((current) => current + 1)
  }

  const handleReset = () => {
    const nextFilters = buildInitialFilters()
    nextFilters.storeCode = singleScopedStoreCode
    setDraftFilters(nextFilters)
    setFilters(nextFilters)
    setResult(null)
    setHasSearched(false)
    setPage(1)
    setPageSize(DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE)
    setSortBy(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY)
    setSortOrder(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER)
  }

  const handleRefresh = () => {
    if (!hasRequiredCommittedFilters) {
      return
    }

    setQueryVersion((current) => current + 1)
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    _tableFilters: Record<string, FilterValue | null>,
    sorter:
      | SorterResult<LocalSupplierPurchaseSalesAnalysisRowDto>
      | SorterResult<LocalSupplierPurchaseSalesAnalysisRowDto>[],
  ) => {
    const singleSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const nextPageSize = normalizePurchaseSalesAnalysisPageSize(pagination.pageSize)
    const nextSort = toPurchaseSalesAnalysisSort(
      singleSorter?.field ? String(singleSorter.field) : undefined,
      singleSorter?.order,
    )

    // 列头排序必须透传到后端，不能只在当前页本地排序。
    setSortBy(nextSort.sortBy)
    setSortOrder(nextSort.sortOrder)

    if (nextPageSize !== pageSize) {
      setPage(1)
      setPageSize(nextPageSize)
      return
    }

    setPage(pagination.current ?? 1)
    setPageSize(nextPageSize)
  }

  return (
    <div ref={wrapRef} style={{ height: '100%' }}>
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space direction="vertical" size={4}>
          <Title level={4} style={{ margin: 0 }}>
            {t('posAdmin.localSupplierPurchaseSalesAnalysis.title', '分店供应商进货销量分析')}
          </Title>
          <Text type="secondary">
            {t(
              'posAdmin.localSupplierPurchaseSalesAnalysis.subtitle',
              '按分店、供应商和订单日期范围查看商品最近进货与后续销量表现。',
            )}
          </Text>
        </Space>

        <div ref={toolbarRef}>
          <Card size="small">
            <Space wrap size={8}>
              <Select
                allowClear
                showSearch
                style={{ width: 240 }}
                placeholder={t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.store', '分店')}
                optionFilterProp="label"
                value={draftFilters.storeCode}
                loading={storeOptionsLoading}
                notFoundContent={
                  storeOptionsLoading
                    ? t('common.loading', '加载中')
                    : t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.noStores', '暂无可选分店')
                }
                options={storeOptions}
                onChange={(value) => {
                  setHasSearched(false)
                  setResult(null)
                  setDraftFilters((current) => ({ ...current, storeCode: value, supplierCode: undefined }))
                }}
              />
              <Select
                allowClear
                showSearch
                style={{ width: 240 }}
                placeholder={t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.supplier', '供应商')}
                optionFilterProp="label"
                value={draftFilters.supplierCode}
                loading={supplierOptionsLoading}
                notFoundContent={
                  supplierOptionsLoading
                    ? t('common.loading', '加载中')
                    : t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.noSuppliers', '暂无可选供应商')
                }
                options={supplierOptions}
                onChange={(value) => {
                  setHasSearched(false)
                  setResult(null)
                  setDraftFilters((current) => ({ ...current, supplierCode: value }))
                }}
              />
              <RangePicker
                allowClear={false}
                value={draftFilters.orderDateRange}
                onChange={(value) => {
                  if (value?.[0] && value?.[1]) {
                    const [startDate, endDate] = value
                    setDraftFilters((current) => {
                      const nextRange: DateRangeValue = [startDate, endDate]
                      return {
                        ...current,
                        orderDateRange: nextRange,
                      }
                    })
                  }
                }}
              />
              <Input
                allowClear
                style={{ width: 280 }}
                placeholder={t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.keyword', '货号 / 条码 / 名称')}
                value={draftFilters.keyword}
                onChange={(event) => {
                  const nextKeyword = event.target.value
                  setDraftFilters((current) => ({ ...current, keyword: nextKeyword }))
                }}
                onPressEnter={handleSearch}
              />
              <Button
                icon={<SearchOutlined />}
                type="primary"
                disabled={!hasRequiredDraftFilters}
                onClick={handleSearch}
              >
                {t('common.search', '查询')}
              </Button>
              <Button onClick={handleReset}>{t('common.reset', '重置')}</Button>
              <Button
                icon={<ReloadOutlined />}
                disabled={!hasSearched || !hasRequiredCommittedFilters}
                onClick={handleRefresh}
              >
                {t('posAdmin.localSupplierPurchaseSalesAnalysis.refresh', '刷新')}
              </Button>
            </Space>
          </Card>
        </div>

        <Card size="small">
          <Space direction="vertical" size={8} style={{ width: '100%' }}>
            {hasSearched && result ? (
              <Space wrap>
                <Text type="secondary">{result.calculationNote}</Text>
                <Text type="secondary">
                  {t('posAdmin.localSupplierPurchaseSalesAnalysis.summary.updatedAt', '统计更新时间')}：
                  {formatDateTime(result.salesStatisticLastUpdate)}
                </Text>
              </Space>
            ) : (
              <Text type="secondary">
                {t(
                  'posAdmin.localSupplierPurchaseSalesAnalysis.emptyBeforeSearch',
                  '请选择分店和供应商后点击查询。',
                )}
              </Text>
            )}
            <Table<LocalSupplierPurchaseSalesAnalysisRowDto>
              size="small"
              rowKey={(record) =>
                `${record.storeCode}-${record.supplierCode}-${record.productCode}-${record.itemNumber || ''}`
              }
              loading={loading}
              columns={columns}
              dataSource={hasSearched ? result?.items ?? [] : []}
              locale={{
                emptyText: (
                  <Empty
                    description={
                      hasSearched
                        ? t(
                            'posAdmin.localSupplierPurchaseSalesAnalysis.empty',
                            '当前条件下暂无分店供应商进货销量数据。',
                          )
                        : t(
                            'posAdmin.localSupplierPurchaseSalesAnalysis.emptyBeforeSearch',
                            '请选择分店和供应商后点击查询。',
                          )
                    }
                  />
                ),
              }}
              scroll={{ x: 1560, y: tableScrollY }}
              virtual
              pagination={{
                current: page,
                pageSize,
                total: hasSearched ? result?.total ?? 0 : 0,
                showSizeChanger: true,
                pageSizeOptions,
                showTotal: (total) =>
                  t('posAdmin.localSupplierPurchaseSalesAnalysis.summary.total', '共 {{count}} 条', {
                    count: total,
                  }),
              }}
              onChange={handleTableChange}
            />
          </Space>
        </Card>
      </Space>
    </div>
  )
}
