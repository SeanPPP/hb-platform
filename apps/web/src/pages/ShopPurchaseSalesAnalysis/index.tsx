import { ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Empty, Input, Select, Space, Spin, Table, Tag, Typography, message } from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { FilterValue, SorterResult } from 'antd/es/table/interface'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  PurchaseSalesDailyChart,
  PurchaseSalesSparkline,
  buildPurchaseSalesTrendMetrics,
  toWholeQuantity,
} from '../../components/PurchaseSalesTrend'
import {
  getShopLocalSupplierPurchaseSalesAnalysis,
  getShopLocalSupplierPurchaseSalesAnalysisSupplierOptions,
} from '../../services/localSupplierInvoiceService'
import { useShopStore } from '../../store/shop'
import type {
  LocalSupplierPurchaseSalesAnalysisResponseDto,
  LocalSupplierPurchaseSalesAnalysisRowDto,
  LocalSupplierPurchaseSalesAnalysisSupplierOptionDto,
} from '../../types/localSupplierInvoice'
import { RequestError } from '../../utils/request'
import ProductImageCell from '../PosAdmin/LocalSupplierPurchaseSalesAnalysis/ProductImageCell'
import {
  DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE,
  PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY,
  PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER,
  PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS,
  getDefaultPurchaseSalesAnalysisDateRange,
  normalizePurchaseSalesAnalysisPageSize,
  toPurchaseSalesAnalysisSort,
} from '../PosAdmin/LocalSupplierPurchaseSalesAnalysis/helpers'

const { RangePicker } = DatePicker
const { Text, Title } = Typography

type Row = LocalSupplierPurchaseSalesAnalysisRowDto
type DateRangeValue = [Dayjs, Dayjs]

interface CommittedQuery {
  supplierCode: string
  keyword: string
  range: DateRangeValue
}

const PURCHASE_TONE = {
  latest: { color: '#1677ff', quantityColor: '#0958d9', background: '#e6f4ff' },
  previous: { color: '#d46b08', quantityColor: '#ad4e00', background: '#fff7e6' },
} as const
const BADGE_TONE = {
  interval: { color: '#ad6800', background: '#fff7e6', borderColor: '#ffd591' },
  between: { color: '#237804', background: '#f6ffed', borderColor: '#b7eb8f' },
} as const

function formatWhole(value?: number | null) {
  const whole = toWholeQuantity(value)
  return whole === null ? '--' : whole.toLocaleString()
}

function PurchaseCell({ date, quantity, tone }: { date?: string | null; quantity?: number | null; tone: keyof typeof PURCHASE_TONE }) {
  if (!date && (quantity === undefined || quantity === null)) {
    return <>--</>
  }
  const style = PURCHASE_TONE[tone]
  return (
    <Space direction="vertical" size={0}>
      <Text strong style={{ color: style.color }}>{date ? dayjs(date).format('YYYY-MM-DD') : '--'}</Text>
      <Text style={{ alignSelf: 'flex-start', padding: '1px 6px', borderRadius: 4, color: style.quantityColor, background: style.background, fontVariantNumeric: 'tabular-nums' }}>
        {formatWhole(quantity)}
      </Text>
    </Space>
  )
}

function Badge({ value, tone }: { value?: number | null; tone: keyof typeof BADGE_TONE }) {
  const style = BADGE_TONE[tone]
  return (
    <Text style={{ display: 'inline-flex', minWidth: 44, justifyContent: 'flex-end', padding: '1px 8px', borderRadius: 4, color: style.color, background: style.background, border: `1px solid ${style.borderColor}`, fontVariantNumeric: 'tabular-nums' }}>
      {formatWhole(value)}
    </Text>
  )
}

/** 订货前台「进货销量分析」：分店跟随顶部当前分店，数据走前台只读接口（只认订货前台权限、限本人门店）。 */
export default function ShopPurchaseSalesAnalysisPage() {
  const { t } = useTranslation()
  const selectedStore = useShopStore((state) => state.selectedStore)
  const storeCode = selectedStore?.storeCode

  const [supplierOptions, setSupplierOptions] = useState<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto[]>([])
  const [supplierOptionsLoading, setSupplierOptionsLoading] = useState(false)
  const [supplierCode, setSupplierCode] = useState<string>()
  const [range, setRange] = useState<DateRangeValue>(() => getDefaultPurchaseSalesAnalysisDateRange())
  const [keyword, setKeyword] = useState('')
  const [committed, setCommitted] = useState<CommittedQuery | null>(null)
  const [queryVersion, setQueryVersion] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE)
  const [sortBy, setSortBy] = useState<string>(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY)
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER)
  const [result, setResult] = useState<LocalSupplierPurchaseSalesAnalysisResponseDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [forbidden, setForbidden] = useState(false)
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])

  const rowKey = useCallback((row: Row) => `${row.storeCode}-${row.supplierCode}-${row.productCode}`, [])

  // 分店跟随顶部「当前分店」：切店即重载供应商并清空上一家店的结果，避免跨店数据残留。
  useEffect(() => {
    setSupplierOptions([])
    setSupplierCode(undefined)
    setCommitted(null)
    setResult(null)
    setForbidden(false)
    setPage(1)
    if (!storeCode) {
      return undefined
    }

    const controller = new AbortController()
    setSupplierOptionsLoading(true)
    getShopLocalSupplierPurchaseSalesAnalysisSupplierOptions(storeCode, controller.signal)
      .then((options) => setSupplierOptions(options))
      .catch((error) => {
        if (controller.signal.aborted) return
        if (error instanceof RequestError && (error.status === 401 || error.status === 403)) {
          setForbidden(true)
        }
        setSupplierOptions([])
      })
      .finally(() => {
        if (!controller.signal.aborted) setSupplierOptionsLoading(false)
      })
    return () => controller.abort()
  }, [storeCode])

  useEffect(() => {
    if (!storeCode || !committed) {
      return undefined
    }

    const controller = new AbortController()
    setLoading(true)
    getShopLocalSupplierPurchaseSalesAnalysis(
      {
        storeCode,
        supplierCode: committed.supplierCode,
        orderDateStart: committed.range[0].format('YYYY-MM-DD'),
        orderDateEnd: committed.range[1].format('YYYY-MM-DD'),
        keyword: committed.keyword || undefined,
        sortBy,
        sortOrder,
        page,
        pageSize,
      },
      controller.signal,
    )
      .then((data) => {
        setResult(data)
        setForbidden(false)
        // 每次新结果默认展开第一行，让用户直接看到大图的读法。
        setExpandedKeys(data.items[0]?.dailySales.length ? [rowKey(data.items[0])] : [])
      })
      .catch((error) => {
        if (controller.signal.aborted) return
        if (error instanceof RequestError && (error.status === 401 || error.status === 403)) {
          setForbidden(true)
          setResult(null)
          return
        }
        message.error(
          error instanceof Error && error.message
            ? error.message
            : t('posAdmin.localSupplierPurchaseSalesAnalysis.loadFailed', '分店供应商进货销量分析加载失败'),
        )
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [committed, page, pageSize, queryVersion, rowKey, sortBy, sortOrder, storeCode, t])

  const handleSearch = () => {
    if (!supplierCode) return
    // 只在点击搜索时提交关键词与日期，输入过程不触发请求。
    setCommitted({ supplierCode, keyword: keyword.trim(), range })
    setPage(1)
    setQueryVersion((current) => current + 1)
  }

  const handleReset = () => {
    setSupplierCode(undefined)
    setKeyword('')
    setRange(getDefaultPurchaseSalesAnalysisDateRange())
    setCommitted(null)
    setResult(null)
    setPage(1)
    setPageSize(DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE)
    setSortBy(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY)
    setSortOrder(PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER)
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    _filters: Record<string, FilterValue | null>,
    sorter: SorterResult<Row> | SorterResult<Row>[],
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter
    // 排序与分页都交给后端，不能只排当前页。
    const nextSort = toPurchaseSalesAnalysisSort(single?.field ? String(single.field) : undefined, single?.order)
    setSortBy(nextSort.sortBy)
    setSortOrder(nextSort.sortOrder)
    const nextPageSize = normalizePurchaseSalesAnalysisPageSize(pagination.pageSize)
    if (nextPageSize !== pageSize) {
      setPageSize(nextPageSize)
      setPage(1)
      return
    }
    setPage(pagination.current ?? 1)
  }

  const sortOrderOf = useCallback(
    (field: string) => (sortBy === field ? (sortOrder === 'asc' ? ('ascend' as const) : ('descend' as const)) : null),
    [sortBy, sortOrder],
  )

  const columns = useMemo<ColumnsType<Row>>(
    () => [
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.image', '图片'),
        key: 'image',
        width: 72,
        render: (_value, row) => (
          <ProductImageCell productImage={row.productImage} itemNumber={row.itemNumber} productCode={row.productCode} alt={row.productName || row.productCode} />
        ),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.product', '货号 / 名称'),
        dataIndex: 'itemNumber',
        key: 'itemNumber',
        width: 250,
        sorter: true,
        sortOrder: sortOrderOf('itemNumber'),
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Text strong>{row.productName || '--'}</Text>
            <Text style={{ color: '#0958d9' }}>{row.itemNumber || row.productCode}</Text>
            <Text type="secondary" style={{ fontSize: 12 }}>{row.barcode || '--'}</Text>
          </Space>
        ),
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.supplier', '供应商'),
        key: 'supplierName',
        width: 130,
        render: (_value, row) => <Tag color="purple" style={{ marginInlineEnd: 0 }}>{row.supplierName || row.supplierCode}</Tag>,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.previousPurchase', '上次进货'),
        dataIndex: 'previousPurchaseDate',
        key: 'previousPurchaseDate',
        width: 124,
        sorter: true,
        sortOrder: sortOrderOf('previousPurchaseDate'),
        render: (_value, row) => <PurchaseCell date={row.previousPurchaseDate} quantity={row.previousPurchaseQty} tone="previous" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.latestPurchase', '最近进货'),
        dataIndex: 'latestPurchaseDate',
        key: 'latestPurchaseDate',
        width: 124,
        sorter: true,
        sortOrder: sortOrderOf('latestPurchaseDate'),
        render: (_value, row) => <PurchaseCell date={row.latestPurchaseDate} quantity={row.latestPurchaseQty} tone="latest" />,
      },
      {
        title: t('posAdmin.localSupplierPurchaseSalesAnalysis.columns.intervalSales', '间隔销量'),
        dataIndex: 'salesBetweenPurchases',
        key: 'salesBetweenPurchases',
        width: 104,
        align: 'center',
        sorter: true,
        sortOrder: sortOrderOf('salesBetweenPurchases'),
        render: (value: number | null | undefined) => <Badge value={value} tone="between" />,
      },
      {
        title: t('purchaseSalesTrend.columns.dailyTrend', '日销量与进货'),
        key: 'dailyTrend',
        // 排序按后端聚合的总销量（最近进货后累计销量）执行；销量类字段点击先看最高，故降序优先。
        dataIndex: 'totalSalesSinceLatestPurchase',
        sorter: true,
        sortDirections: ['descend', 'ascend'],
        sortOrder: sortOrderOf('totalSalesSinceLatestPurchase'),
        width: 340,
        render: (_value, row) => {
          if (!row.dailySales.length) {
            return <Text type="secondary">{t('purchaseSalesTrend.chart.noDaily', '该商品尚无逐日销量统计。')}</Text>
          }
          const metrics = buildPurchaseSalesTrendMetrics(row)
          return (
            <div>
              <PurchaseSalesSparkline row={row} />
              <Text type="secondary" style={{ fontSize: 12 }}>
                {t('purchaseSalesTrend.columns.trendCaption', '最近进货 {{purchased}} 件 · 其后 {{days}} 天售出 {{total}} 件 · 日均 {{average}}', {
                  purchased: metrics.purchasedQuantity.toLocaleString(),
                  days: metrics.sinceLatest.length,
                  total: metrics.totalSinceLatest.toLocaleString(),
                  average: metrics.averagePerDay.toFixed(1),
                })}
              </Text>
            </div>
          )
        },
      },
      {
        title: t('purchaseSalesTrend.columns.sellThrough', '售出比'),
        key: 'sellThrough',
        width: 96,
        align: 'right',
        render: (_value, row) => {
          const ratio = buildPurchaseSalesTrendMetrics(row).sellThrough
          if (ratio === null) return '--'
          return (
            <Text strong style={{ color: ratio >= 1 ? '#cf1322' : ratio >= 0.7 ? '#d46b08' : undefined, fontVariantNumeric: 'tabular-nums' }}>
              {Math.round(ratio * 100)}%
            </Text>
          )
        },
      },
    ],
    [sortOrderOf, t],
  )

  return (
    <div className="shop-feature-page">
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space direction="vertical" size={4}>
          <Title level={4} style={{ margin: 0 }}>{t('shop.purchaseSalesAnalysisTitle', '进货销量分析')}</Title>
          <Text type="secondary">{t('shop.purchaseSalesAnalysisSubtitle', '按供应商和订单日期范围查看本店商品最近进货与进货后的每日销量。')}</Text>
        </Space>

        {forbidden ? <Alert type="error" showIcon message={t('forbidden.subTitle', '你当前没有权限访问这个页面。')} /> : null}
        {!storeCode ? <Alert type="info" showIcon message={t('shop.purchaseSalesAnalysisNoStore', '请先在顶部选择当前分店。')} /> : null}

        <Card size="small">
          <Space wrap size={8}>
            <Select
              allowClear
              showSearch
              style={{ width: 240 }}
              placeholder={t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.supplier', '供应商')}
              optionFilterProp="label"
              options={supplierOptions}
              loading={supplierOptionsLoading}
              disabled={!storeCode}
              value={supplierCode}
              onChange={(value) => {
                setSupplierCode(value)
                setCommitted(null)
                setResult(null)
              }}
            />
            <RangePicker allowClear={false} value={range} onChange={(value) => value?.[0] && value?.[1] && setRange([value[0], value[1]])} />
            <Input
              allowClear
              style={{ width: 260 }}
              placeholder={t('posAdmin.localSupplierPurchaseSalesAnalysis.filters.keyword', '货号 / 条码 / 名称')}
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              onPressEnter={handleSearch}
            />
            <Button type="primary" icon={<SearchOutlined />} disabled={!storeCode || !supplierCode} loading={loading} onClick={handleSearch}>
              {t('common.search', '查询')}
            </Button>
            <Button onClick={handleReset}>{t('common.reset', '重置')}</Button>
            <Button icon={<ReloadOutlined />} disabled={!committed} onClick={() => setQueryVersion((current) => current + 1)}>
              {t('posAdmin.localSupplierPurchaseSalesAnalysis.refresh', '刷新')}
            </Button>
          </Space>
        </Card>

        <Card size="small">
          {committed && result ? (
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <Space wrap size={16}>
                <Text type="secondary">{t('purchaseSalesTrend.calculationNote', '进货按订单日期范围过滤、按进货发生日期汇总；日销量从上次进货起逐日展示，售出比与累计销量从最近进货当天起统计。')}</Text>
                <Text type="secondary">
                  {t('posAdmin.localSupplierPurchaseSalesAnalysis.summary.updatedAt', '统计更新时间')}：
                  {result.salesStatisticLastUpdate ? dayjs(result.salesStatisticLastUpdate).format('YYYY-MM-DD HH:mm:ss') : '--'}
                </Text>
              </Space>
              <Table<Row>
                size="small"
                rowKey={rowKey}
                columns={columns}
                dataSource={result.items}
                loading={loading}
                scroll={{ x: 1250 }}
                onChange={handleTableChange}
                locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('posAdmin.localSupplierPurchaseSalesAnalysis.empty', '当前条件下暂无分店供应商进货销量数据。')} /> }}
                pagination={{
                  current: page,
                  pageSize,
                  total: result.total,
                  showSizeChanger: true,
                  pageSizeOptions: PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS.map(String),
                  showTotal: (total) => t('posAdmin.localSupplierPurchaseSalesAnalysis.summary.total', '共 {{count}} 条', { count: total }),
                }}
                expandable={{
                  expandRowByClick: true,
                  expandedRowKeys: expandedKeys,
                  onExpandedRowsChange: (keys) => setExpandedKeys(keys.map(String)),
                  rowExpandable: (row) => row.dailySales.length > 0,
                  expandedRowRender: (row) => (
                    <div style={{ padding: '4px 8px 8px', overflowX: 'auto' }}>
                      <PurchaseSalesDailyChart row={row} title={`${row.itemNumber || row.productCode} · ${row.productName || ''}`.trim()} />
                    </div>
                  ),
                }}
              />
            </Space>
          ) : (
            loading ? (
              // 主查询在大数据量下要数秒，给出明确的进度反馈而不是空状态。
              <div style={{ padding: '32px 0', textAlign: 'center' }}>
                <Spin />
                <div style={{ marginTop: 12, color: '#718096' }}>
                  {t('purchaseSalesTrend.loadingHint', '正在查询本店进货与日销量，数据量大时需要几秒…')}
                </div>
              </div>
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={t('shop.purchaseSalesAnalysisPickSupplier', '选择供应商后点击搜索，查看本店商品的进货与每日销量。')}
              />
            )
          )}
        </Card>
      </Space>
    </div>
  )
}
