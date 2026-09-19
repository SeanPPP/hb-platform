import {
  ClockCircleOutlined,
  ExclamationCircleFilled,
  PictureOutlined,
  ReloadOutlined,
  SearchOutlined,
  ThunderboltOutlined,
  WarningOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Empty,
  Image,
  Input,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'

import { MeasuredTable } from '../../../components/MeasuredTable'
import {
  getProductMovementReport,
  getProductMovementStoreOptions,
} from '../../../services/productMovementReportService'
import type { StoreOption } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type { ProductMovementReportResponse, ProductMovementReportRow } from '../../../types/productMovementReport'
import type { UserStoreDto } from '../../../types/user'

import styles from './index.module.css'
import {
  LOW_COVER_DAYS,
  PRODUCT_MOVEMENT_ACTION_HINTS,
  PRODUCT_MOVEMENT_CREDIBILITIES,
  PRODUCT_MOVEMENT_SUGGESTION_CARDS,
  STALE_STATISTIC_DAYS,
  formatAud,
  formatNumber,
  formatPercent,
  getCoverDaysRatio,
  getCredibilityTagColor,
  getSuggestionTagColor,
  isCoverDaysTight,
} from './logic'

const { Text, Title } = Typography

const DEFAULT_PAGE_SIZE = 50
const PAGE_SIZE_OPTIONS = ['20', '50', '100', '200']

/** 建议标签与卡片圆点共用的颜色，和 antd Tag 的色板对齐。 */
const SUGGESTION_DOT_COLORS: Record<string, string> = {
  需要订货: '#cf1322',
  需要备货: '#d46b08',
  需要清仓: '#d4380d',
  值得囤货: '#722ed1',
  好卖: '#389e0d',
  观察: '#1677ff',
}

const SUGGESTION_ACTIVE_STYLES: Record<string, { border: string; background: string; text: string }> = {
  需要订货: { border: '#cf1322', background: '#fff7f6', text: '#a8071a' },
  需要备货: { border: '#d46b08', background: '#fffaf5', text: '#ad4e00' },
  需要清仓: { border: '#d4380d', background: '#fff7f5', text: '#ad2102' },
  值得囤货: { border: '#722ed1', background: '#faf7ff', text: '#531dab' },
  好卖: { border: '#389e0d', background: '#f8fff5', text: '#237804' },
  观察: { border: '#1677ff', background: '#f7faff', text: '#0958d9' },
}

function formatDate(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD') : value
}

function formatShortDate(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('MM-DD') : value
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}

function buildUserStoreOptions(userStores?: UserStoreDto[]) {
  return (userStores ?? [])
    .filter((store) => store.storeCode)
    .map((store) => ({
      label: store.storeName ? `${store.storeCode} - ${store.storeName}` : store.storeCode,
      value: store.storeCode,
    }))
}

function getSummaryCount(result: ProductMovementReportResponse | null, key: string) {
  return result?.suggestionSummary.find((item) => item.key === key)?.count ?? 0
}

/** 「全部商品」没有独立计数，取各建议计数之和；汇总不受建议筛选影响，所以切换卡片时它保持稳定。 */
function getTotalSummaryCount(result: ProductMovementReportResponse | null) {
  return (result?.suggestionSummary ?? []).reduce((sum, item) => sum + item.count, 0)
}

/** 商品图：无图或加载失败都落到同尺寸虚线占位，避免行高跳动。 */
function ProductThumb({ src, alt }: { src?: string; alt: string }) {
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setFailed(false)
  }, [src])

  if (!src || failed) {
    return (
      <span className={styles.thumbEmpty} aria-label={`${alt}：暂无图片`}>
        <PictureOutlined />
        <span className={styles.thumbEmptyText}>暂无图</span>
      </span>
    )
  }

  return (
    <span className={styles.thumb}>
      <Image src={src} alt={alt} width={56} height={56} preview={false} onError={() => setFailed(true)} />
    </span>
  )
}

/** 展开区大图，点击可放大；失败态与列表保持一致的占位。 */
function ProductDetailThumb({ src, alt }: { src?: string; alt: string }) {
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setFailed(false)
  }, [src])

  if (!src || failed) {
    return (
      <span className={styles.detailThumbEmpty} aria-label={`${alt}：暂无图片`}>
        <PictureOutlined style={{ fontSize: 32 }} />
        <span>暂无图片</span>
      </span>
    )
  }

  return (
    <span className={styles.detailThumb}>
      <Image src={src} alt={alt} width={176} height={176} onError={() => setFailed(true)} />
    </span>
  )
}

export default function ProductMovementReportPage() {
  const access = useAuthStore((state) => state.access)
  const currentUser = useAuthStore((state) => state.currentUser)
  const canQueryAllStores = access.isAdmin || access.isWarehouseManager

  const [storeOptions, setStoreOptions] = useState<StoreOption[]>(() => buildUserStoreOptions(currentUser?.stores))
  const [storeCode, setStoreCode] = useState<string | undefined>()
  const [suggestion, setSuggestion] = useState<string | undefined>()
  const [dataCredibility, setDataCredibility] = useState<string | undefined>()
  const [keywordInput, setKeywordInput] = useState('')
  const [keyword, setKeyword] = useState('')
  const [asOfDate, setAsOfDate] = useState<Dayjs>(() => dayjs())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<ProductMovementReportResponse | null>(null)
  const [expandedKeys, setExpandedKeys] = useState<readonly string[]>([])

  const userStoreOptions = useMemo(() => buildUserStoreOptions(currentUser?.stores), [currentUser?.stores])
  const requiresStoreSelection = !canQueryAllStores && userStoreOptions.length > 1 && !storeCode

  useEffect(() => {
    if (!canQueryAllStores) {
      setStoreOptions(userStoreOptions)
      if (userStoreOptions.length === 1) {
        setStoreCode(userStoreOptions[0].value)
      }
      return
    }

    let cancelled = false
    getProductMovementStoreOptions()
      .then((stores) => {
        if (!cancelled) {
          setStoreOptions(stores)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setStoreOptions(userStoreOptions)
        }
      })

    return () => {
      cancelled = true
    }
  }, [canQueryAllStores, userStoreOptions])

  const loadData = useCallback(
    async (signal?: AbortSignal) => {
      if (requiresStoreSelection) {
        setResult(null)
        return
      }

      setLoading(true)
      try {
        const data = await getProductMovementReport(
          {
            storeCode,
            suggestion,
            dataCredibility,
            keyword,
            asOfDate: asOfDate.format('YYYY-MM-DD'),
            page,
            pageSize,
          },
          signal,
        )
        setResult(data)
      } catch (error) {
        if (signal?.aborted) {
          return
        }
        message.error(error instanceof Error ? error.message : '商品经营分析加载失败')
      } finally {
        if (!signal?.aborted) {
          setLoading(false)
        }
      }
    },
    [asOfDate, dataCredibility, keyword, page, pageSize, requiresStoreSelection, storeCode, suggestion],
  )

  useEffect(() => {
    const controller = new AbortController()
    void loadData(controller.signal)
    return () => controller.abort()
  }, [loadData])

  // 翻页或换筛选后，上一页展开的行不应留在展开状态。
  useEffect(() => {
    setExpandedKeys([])
  }, [page, pageSize, storeCode, suggestion, dataCredibility, keyword, asOfDate])

  const handleSearch = () => {
    setPage(1)
    setKeyword(keywordInput.trim())
  }

  const handleReset = () => {
    setPage(1)
    setSuggestion(undefined)
    setDataCredibility(undefined)
    setKeywordInput('')
    setKeyword('')
    setAsOfDate(dayjs())
    if (canQueryAllStores) {
      setStoreCode(undefined)
    }
  }

  const handleSelectSuggestion = (key: string) => {
    setPage(1)
    // 再次点击选中的卡片即取消该筛选。
    setSuggestion((previous) => (key && previous !== key ? key : undefined))
  }

  // 单店口径下分店列是冗余的（筛选栏已经指明门店），只有跨店查询才显示。
  const showStoreColumn = canQueryAllStores && !storeCode

  const columns = useMemo<ColumnsType<ProductMovementReportRow>>(() => {
    const storeColumn: ColumnsType<ProductMovementReportRow> = showStoreColumn
      ? [
          {
            title: '分店',
            key: 'store',
            width: 140,
            fixed: 'left',
            render: (_value, record) => record.storeName || record.storeCode,
          },
        ]
      : []

    return [
      ...storeColumn,
      {
        title: '商品',
        key: 'product',
        width: 300,
        fixed: 'left',
        render: (_value, record) => (
          <div className={styles.productCell}>
            <ProductThumb src={record.imageUrl} alt={record.productName || record.productCode} />
            <div className={styles.productText}>
              <Tooltip title={record.productName || record.productCode}>
                <div className={styles.productName}>{record.productName || '--'}</div>
              </Tooltip>
              <div className={styles.productMeta}>
                {record.productCode}
                {record.barcode ? ` · ${record.barcode}` : ''}
              </div>
            </div>
          </div>
        ),
      },
      {
        title: '近30天销量',
        dataIndex: 'salesQty30',
        key: 'salesQty30',
        width: 140,
        align: 'right',
        render: (_value, record) => (
          <>
            <div className={styles.metricMain}>{formatNumber(record.salesQty30)}</div>
            <div className={styles.metricSub}>
              {`90天 ${formatNumber(record.salesQty90)} · 日均 ${formatNumber(record.dailySalesQty30, 2)}`}
            </div>
          </>
        ),
      },
      {
        title: '90天销售额 / 毛利',
        dataIndex: 'salesAmount90Aud',
        key: 'salesAmount90Aud',
        width: 160,
        align: 'right',
        render: (_value, record) => (
          <>
            <div className={styles.metricMoney}>{formatAud(record.salesAmount90Aud)}</div>
            <div className={styles.metricSub}>
              {`毛利 ${formatAud(record.grossProfit90Aud)} · ${formatPercent(record.grossMarginRate90)}`}
            </div>
          </>
        ),
      },
      {
        title: '最近销售',
        dataIndex: 'lastSaleDate',
        key: 'lastSaleDate',
        width: 120,
        render: (_value, record) => {
          const noSaleDays = record.noSaleDays
          const hasNoSaleDays = typeof noSaleDays === 'number' && Number.isFinite(noSaleDays)
          return (
            <>
              <div className={styles.metricPlain}>{formatShortDate(record.lastSaleDate)}</div>
              <div
                className={
                  hasNoSaleDays && noSaleDays >= LOW_COVER_DAYS ? styles.metricSubWarn : styles.metricSub
                }
              >
                {hasNoSaleDays ? `${formatNumber(noSaleDays)} 天未销售` : '无销售记录'}
              </div>
            </>
          )
        },
      },
      {
        title: '180天进货 / 销售',
        key: 'flow180',
        width: 170,
        render: (_value, record) => {
          // 两条以同一最大值为基准，长度差直观对应估算剩余量。
          const scale = Math.max(record.purchaseQty180, record.salesQty180, 1)
          const purchaseWidth = Math.round((record.purchaseQty180 / scale) * 84)
          const salesWidth = Math.round((record.salesQty180 / scale) * 84)
          return (
            <Space direction="vertical" size={5} style={{ width: '100%' }}>
              <div className={styles.flowRow}>
                <span className={styles.flowLabel}>进</span>
                <span
                  className={`${styles.flowTrack} ${styles.flowPurchase}`}
                  style={{ width: Math.max(purchaseWidth, 2) }}
                />
                <span className={styles.flowValue}>{formatNumber(record.purchaseQty180, 2)}</span>
              </div>
              <div className={styles.flowRow}>
                <span className={styles.flowLabel}>销</span>
                <span
                  className={`${styles.flowTrack} ${styles.flowSales}`}
                  style={{ width: Math.max(salesWidth, 2) }}
                />
                <span className={styles.flowValue}>{formatNumber(record.salesQty180)}</span>
              </div>
            </Space>
          )
        },
      },
      {
        title: (
          <Tooltip title="估算剩余量不是真实库存，只是近180天进货单数量减近180天销售数量。">
            估算剩余量 / 可卖天数
          </Tooltip>
        ),
        dataIndex: 'estimatedRemainingQty',
        key: 'estimatedRemainingQty',
        width: 180,
        render: (_value, record) => {
          const tight = isCoverDaysTight(record.estimatedCoverDays) || record.estimatedRemainingQty <= 0
          const ratio = getCoverDaysRatio(record.estimatedCoverDays)
          return (
            <div className={styles.coverCell}>
              <div className={styles.coverTop}>
                <span className={`${styles.coverQty} ${tight ? styles.coverQtyTight : ''}`}>
                  {formatNumber(record.estimatedRemainingQty, 2)}
                </span>
                <span className={styles.metricSub}>
                  {typeof record.estimatedCoverDays === 'number' && Number.isFinite(record.estimatedCoverDays)
                    ? `约可卖 ${formatNumber(record.estimatedCoverDays, 1)} 天`
                    : '近30天无销量'}
                </span>
              </div>
              <div className={styles.coverBar}>
                <div
                  className={`${styles.coverBarFill} ${tight ? styles.coverBarFillTight : ''}`}
                  style={{ width: `${Math.round(ratio * 100)}%` }}
                />
              </div>
            </div>
          )
        },
      },
      {
        title: '可信度',
        dataIndex: 'dataCredibility',
        key: 'dataCredibility',
        width: 110,
        render: (value: string, record) => (
          <Space size={6}>
            <Tag color={getCredibilityTagColor(value)}>{value}</Tag>
            {record.dataExceptionFlag && record.dataExceptionFlag !== '正常' ? (
              <Tooltip title={record.dataExceptionFlag}>
                <WarningOutlined style={{ color: '#ad6800' }} aria-label="有异常标记，展开查看" />
              </Tooltip>
            ) : null}
          </Space>
        ),
      },
      {
        title: '系统建议',
        dataIndex: 'systemSuggestion',
        key: 'systemSuggestion',
        width: 110,
        fixed: 'right',
        render: (value: string) => <Tag color={getSuggestionTagColor(value)}>{value}</Tag>,
      },
    ]
  }, [showStoreColumn])

  const handleTableChange = (pagination: TablePaginationConfig) => {
    setPage(pagination.current ?? 1)
    setPageSize(pagination.pageSize ?? DEFAULT_PAGE_SIZE)
  }

  const lastUpdate = result?.salesStatisticLastUpdate
  const staleDays = lastUpdate && dayjs(lastUpdate).isValid() ? dayjs().diff(dayjs(lastUpdate), 'day') : null
  const isStale = staleDays !== null && staleDays > STALE_STATISTIC_DAYS
  const snapshotGeneratedAt =
    result?.snapshotGeneratedAtUtc && dayjs(result.snapshotGeneratedAtUtc).isValid()
      ? dayjs(result.snapshotGeneratedAtUtc).format('HH:mm')
      : null

  const totalSummaryCount = getTotalSummaryCount(result)
  const listTitle = suggestion ?? '全部商品'
  const listCount = suggestion ? getSummaryCount(result, suggestion) : totalSummaryCount

  return (
    <div className={styles.page}>
      <div className={styles.pageHead}>
        <Space direction="vertical" size={4}>
          <Title level={4} className={styles.pageTitle}>
            商品经营分析
          </Title>
          <Text className={styles.pageSubtitle}>
            基于销售统计和近180天进货单明细，告诉店长今天该订货、备货、清仓还是观察哪些商品。
          </Text>
        </Space>
        <Space size={8} wrap>
          {snapshotGeneratedAt ? (
            // 快照每小时刷新：明确告诉店长这不是实时数据，刚录的进货单可能还没算进来。
            <Tooltip title="为保证打开速度，今天的数据由后台每小时预先计算一次；刚录入的进货单最多约 1 小时后反映到这里。">
              <span className={styles.freshness}>
                <ThunderboltOutlined />
                <span>数据生成于 {snapshotGeneratedAt}</span>
              </span>
            </Tooltip>
          ) : null}
          <span className={`${styles.freshness} ${isStale ? styles.freshnessStale : ''}`}>
            <ClockCircleOutlined />
            <span>销售统计更新于 {formatDateTime(lastUpdate)}</span>
            {isStale ? <span className={styles.freshnessStaleText}>· 已 {staleDays} 天未更新</span> : null}
          </span>
        </Space>
      </div>

      <div className={styles.scopeNote} role="note">
        <ExclamationCircleFilled className={styles.scopeNoteIcon} />
        <span className={styles.scopeNoteTitle}>估算剩余量不是库存</span>
        <span className={styles.scopeNoteBody}>
          {result?.dataScopeNote ??
            '系统没有货架库存和后仓库存，本页不能判断从后仓补到货架。需要备货时请先检查货架和后仓：有货先上架，无货再订货。'}
        </span>
        <span className={styles.scopeNoteFormula}>估算剩余量 = 近180天进货 − 近180天销售</span>
      </div>

      <div className={styles.filterBar}>
        <Select
          allowClear={canQueryAllStores}
          showSearch
          style={{ width: 240 }}
          placeholder={canQueryAllStores ? '全部分店' : '请选择分店'}
          optionFilterProp="label"
          value={storeCode}
          options={storeOptions}
          onChange={(value) => {
            setPage(1)
            setStoreCode(value)
          }}
        />
        <DatePicker
          allowClear={false}
          value={asOfDate}
          onChange={(value) => {
            if (value) {
              setPage(1)
              setAsOfDate(value)
            }
          }}
        />
        <Select
          allowClear
          style={{ width: 130 }}
          placeholder="数据可信度"
          value={dataCredibility}
          options={PRODUCT_MOVEMENT_CREDIBILITIES.map((item) => ({ label: item, value: item }))}
          onChange={(value) => {
            setPage(1)
            setDataCredibility(value)
          }}
        />
        <Input
          allowClear
          className={styles.filterKeyword}
          placeholder="商品编码 / 条码 / 名称"
          value={keywordInput}
          onChange={(event) => setKeywordInput(event.target.value)}
          onPressEnter={handleSearch}
          prefix={<SearchOutlined />}
        />
        <Button icon={<SearchOutlined />} type="primary" onClick={handleSearch}>
          查询
        </Button>
        <Button onClick={handleReset}>重置</Button>
        <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>
          刷新
        </Button>
      </div>

      <div className={styles.cards}>
        {PRODUCT_MOVEMENT_SUGGESTION_CARDS.map((item) => {
          const active = item.key ? suggestion === item.key : !suggestion
          const activeStyle = item.key ? SUGGESTION_ACTIVE_STYLES[item.key] : undefined
          const count = item.key ? getSummaryCount(result, item.key) : totalSummaryCount
          const hint = item.key ? PRODUCT_MOVEMENT_ACTION_HINTS[item.key] : undefined

          return (
            <button
              key={item.key || 'all'}
              type="button"
              aria-pressed={active}
              className={`${styles.card} ${active ? styles.cardActive : ''}`}
              style={
                active && activeStyle
                  ? { borderColor: activeStyle.border, background: activeStyle.background }
                  : active
                    ? { borderColor: '#1677ff', background: '#f7faff', borderWidth: 1.5 }
                    : undefined
              }
              onClick={() => handleSelectSuggestion(item.key)}
            >
              <span
                className={styles.cardLabel}
                style={{ color: active && activeStyle ? activeStyle.text : undefined }}
              >
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  {item.key ? (
                    <span
                      className={styles.cardDot}
                      style={{ background: SUGGESTION_DOT_COLORS[item.key] ?? '#8c8c8c' }}
                    />
                  ) : null}
                  {item.label}
                </span>
              </span>
              <span className={styles.cardCount}>{formatNumber(count)}</span>
              <Tooltip title={hint}>
                <span className={styles.cardHint}>{item.hint}</span>
              </Tooltip>
            </button>
          )
        })}
      </div>

      {requiresStoreSelection ? (
        <Card>
          <Empty description="当前账号关联多个门店，请先选择一个门店查看商品经营分析。" />
        </Card>
      ) : (
        <Card size="small" styles={{ body: { padding: 0 } }}>
          <div className={styles.listHead}>
            <span className={styles.listHeadTitle}>{listTitle}</span>
            <span className={styles.listHeadMeta}>{formatNumber(listCount)} 个商品</span>
            <span className={styles.listHeadDivider} />
            <span className={styles.listHeadMeta}>
              {suggestion ? '按近30天销量从高到低排列' : '按建议紧急程度排列，订货和备货在前'}
            </span>
            <span className={styles.listHeadDivider} />
            <span className={styles.listHeadMeta}>{result?.calculationNote}</span>
          </div>
          <MeasuredTable<ProductMovementReportRow>
            metricId="executive-sales-intelligence.product-movement-report.table-1"
            size="small"
            rowKey={(record) => `${record.storeCode}-${record.productCode}`}
            loading={loading}
            columns={columns}
            dataSource={result?.items ?? []}
            scroll={{ x: showStoreColumn ? 1440 : 1300 }}
            expandable={{
              expandedRowKeys: expandedKeys,
              onExpandedRowsChange: (keys) => setExpandedKeys(keys as string[]),
              expandedRowRender: (record) => (
                <div className={styles.detail}>
                  <ProductDetailThumb
                    src={record.imageUrl}
                    alt={record.productName || record.productCode}
                  />
                  <div className={styles.detailGrid}>
                    <div className={styles.detailGroup}>
                      <div className={styles.detailGroupTitle}>销售</div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>近30天销量</span>
                        <span className={styles.detailValue}>{formatNumber(record.salesQty30)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>近90天销量</span>
                        <span className={styles.detailValue}>{formatNumber(record.salesQty90)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>30天日均</span>
                        <span className={styles.detailValue}>{formatNumber(record.dailySalesQty30, 2)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>最近销售</span>
                        <span className={styles.detailValue}>{formatDate(record.lastSaleDate)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>无销售天数</span>
                        <span className={styles.detailValue}>
                          {record.noSaleDays === null || record.noSaleDays === undefined
                            ? '--'
                            : formatNumber(record.noSaleDays)}
                        </span>
                      </div>
                    </div>

                    <div className={styles.detailGroup}>
                      <div className={styles.detailGroupTitle}>收益（近90天）</div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>销售额</span>
                        <span className={styles.detailValue}>{formatAud(record.salesAmount90Aud)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>毛利</span>
                        <span className={styles.detailValue}>{formatAud(record.grossProfit90Aud)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>毛利率</span>
                        <span className={styles.detailValue}>{formatPercent(record.grossMarginRate90)}</span>
                      </div>
                      <div className={styles.detailGroupTitle} style={{ marginTop: 6 }}>
                        数据
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>异常标记</span>
                        <span
                          className={
                            record.dataExceptionFlag === '正常' ? styles.detailValue : styles.detailValueWarn
                          }
                        >
                          {record.dataExceptionFlag}
                        </span>
                      </div>
                    </div>

                    <div className={styles.detailGroup}>
                      <div className={styles.detailGroupTitle}>进销与剩余（近180天）</div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>进货数量</span>
                        <span className={styles.detailValue}>{formatNumber(record.purchaseQty180, 2)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>销售数量</span>
                        <span className={styles.detailValue}>{formatNumber(record.salesQty180)}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>估算剩余量</span>
                        <span
                          className={
                            record.estimatedRemainingQty <= 0 ? styles.detailValueTight : styles.detailValue
                          }
                        >
                          {formatNumber(record.estimatedRemainingQty, 2)}
                        </span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>估算可卖天数</span>
                        <span
                          className={
                            isCoverDaysTight(record.estimatedCoverDays)
                              ? styles.detailValueTight
                              : styles.detailValue
                          }
                        >
                          {record.estimatedCoverDays === null || record.estimatedCoverDays === undefined
                            ? '--'
                            : formatNumber(record.estimatedCoverDays, 1)}
                        </span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>统计更新时间</span>
                        <span className={styles.detailValue}>
                          {formatDateTime(record.salesStatisticLastUpdate)}
                        </span>
                      </div>
                    </div>

                    <div className={styles.detailGroup}>
                      <div className={styles.detailGroupTitle}>分店</div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>分店名称</span>
                        <span className={styles.detailValue}>{record.storeName || '--'}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>分店编码</span>
                        <span className={styles.detailValue}>{record.storeCode}</span>
                      </div>
                      <div className={styles.detailItem}>
                        <span className={styles.detailLabel}>条码</span>
                        <span className={styles.detailValue}>{record.barcode || '--'}</span>
                      </div>
                    </div>
                  </div>
                </div>
              ),
            }}
            pagination={{
              current: page,
              pageSize,
              total: result?.total ?? 0,
              showSizeChanger: true,
              pageSizeOptions: PAGE_SIZE_OPTIONS,
              showTotal: (total) => `共 ${total} 个商品`,
              style: { padding: '0 16px' },
            }}
            onChange={handleTableChange}
          />
        </Card>
      )}
    </div>
  )
}
