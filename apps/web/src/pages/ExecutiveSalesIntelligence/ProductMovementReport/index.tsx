import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Input,
  Row,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  getProductMovementReport,
  getProductMovementStoreOptions,
} from '../../../services/productMovementReportService'
import type { StoreOption } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type { ProductMovementReportResponse, ProductMovementReportRow } from '../../../types/productMovementReport'
import type { UserStoreDto } from '../../../types/user'
import {
  PRODUCT_MOVEMENT_ACTION_HINTS,
  PRODUCT_MOVEMENT_CREDIBILITIES,
  PRODUCT_MOVEMENT_SUGGESTIONS,
  formatAud,
  formatNumber,
  formatPercent,
  getCredibilityTagColor,
  getSuggestionTagColor,
} from './logic'

const { Text, Title } = Typography

const DEFAULT_PAGE_SIZE = 50
const PAGE_SIZE_OPTIONS = ['20', '50', '100', '200']

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

  const handleSearch = () => {
    setPage(1)
    setKeyword(keywordInput.trim())
  }

  const columns = useMemo<ColumnsType<ProductMovementReportRow>>(
    () => [
      {
        title: '分店',
        key: 'store',
        width: 150,
        fixed: 'left',
        render: (_value, record) => record.storeName || record.storeCode,
      },
      {
        title: '商品',
        key: 'product',
        width: 260,
        fixed: 'left',
        render: (_value, record) => (
          <Space direction="vertical" size={0}>
            <Text strong>{record.productName || '--'}</Text>
            <Text type="secondary">{record.productCode}</Text>
            <Text type="secondary">{record.barcode || '--'}</Text>
          </Space>
        ),
      },
      {
        title: '近30天销量',
        dataIndex: 'salesQty30',
        width: 110,
        align: 'right',
        render: (value: number) => formatNumber(value),
      },
      {
        title: '近90天销量',
        dataIndex: 'salesQty90',
        width: 110,
        align: 'right',
        render: (value: number) => formatNumber(value),
      },
      {
        title: '30天日均',
        dataIndex: 'dailySalesQty30',
        width: 100,
        align: 'right',
        render: (value: number) => formatNumber(value, 2),
      },
      {
        title: '90天销售额',
        dataIndex: 'salesAmount90Aud',
        width: 130,
        align: 'right',
        render: (value: number) => formatAud(value),
      },
      {
        title: '90天毛利',
        dataIndex: 'grossProfit90Aud',
        width: 120,
        align: 'right',
        render: (value: number | null) => formatAud(value),
      },
      {
        title: '毛利率',
        dataIndex: 'grossMarginRate90',
        width: 90,
        align: 'right',
        render: (value: number | null) => formatPercent(value),
      },
      {
        title: '最近销售',
        dataIndex: 'lastSaleDate',
        width: 110,
        render: (value: string | null) => formatDate(value),
      },
      {
        title: '无销售天数',
        dataIndex: 'noSaleDays',
        width: 110,
        align: 'right',
        render: (value: number | null) => (value === null ? '--' : formatNumber(value)),
      },
      {
        title: '180天进货',
        dataIndex: 'purchaseQty180',
        width: 110,
        align: 'right',
        render: (value: number) => formatNumber(value, 2),
      },
      {
        title: '180天销售',
        dataIndex: 'salesQty180',
        width: 110,
        align: 'right',
        render: (value: number) => formatNumber(value),
      },
      {
        title: (
          <Tooltip title="估算剩余量不是真实库存，只是近180天进货单数量减近180天销售数量。">
            估算剩余量
          </Tooltip>
        ),
        dataIndex: 'estimatedRemainingQty',
        width: 120,
        align: 'right',
        render: (value: number) => formatNumber(value, 2),
      },
      {
        title: '估算可卖天数',
        dataIndex: 'estimatedCoverDays',
        width: 130,
        align: 'right',
        render: (value: number | null) => (value === null ? '--' : formatNumber(value, 1)),
      },
      {
        title: '可信度',
        dataIndex: 'dataCredibility',
        width: 90,
        render: (value: string) => <Tag color={getCredibilityTagColor(value)}>{value}</Tag>,
      },
      {
        title: '异常标记',
        dataIndex: 'dataExceptionFlag',
        width: 240,
        ellipsis: true,
        render: (value: string) => (
          <Tooltip title={value}>
            <Text type={value === '正常' ? 'secondary' : 'warning'}>{value}</Text>
          </Tooltip>
        ),
      },
      {
        title: '系统建议',
        dataIndex: 'systemSuggestion',
        width: 110,
        fixed: 'right',
        render: (value: string) => <Tag color={getSuggestionTagColor(value)}>{value}</Tag>,
      },
      {
        title: '店长动作',
        dataIndex: 'storeManagerAction',
        width: 280,
        fixed: 'right',
      },
      {
        title: '统计更新时间',
        dataIndex: 'salesStatisticLastUpdate',
        width: 160,
        render: (value: string | null) => formatDateTime(value),
      },
    ],
    [],
  )

  const handleTableChange = (pagination: TablePaginationConfig) => {
    setPage(pagination.current ?? 1)
    setPageSize(pagination.pageSize ?? DEFAULT_PAGE_SIZE)
  }

  const summaryCards = [
    { key: '需要订货', title: '需要订货', count: getSummaryCount(result, '需要订货') },
    { key: '需要备货', title: '需要备货', count: getSummaryCount(result, '需要备货') },
    { key: '需要清仓', title: '需要清仓', count: getSummaryCount(result, '需要清仓') },
    { key: '值得囤货', title: '值得囤货', count: getSummaryCount(result, '值得囤货') },
    { key: '观察', title: '观察', count: getSummaryCount(result, '观察') },
  ]

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      <Space direction="vertical" size={4}>
        <Title level={4} style={{ margin: 0 }}>
          商品经营分析
        </Title>
        <Text type="secondary">
          基于销售统计和近180天进货单明细，给店长提供备货、订货、清仓和观察建议。
        </Text>
      </Space>

      <Alert
        type="warning"
        showIcon
        message="估算剩余量不是库存"
        description="系统没有货架库存和后仓库存，本页不能判断从后仓补到货架。需要备货时请先检查货架和后仓；有货先上架，无货再订货。"
      />

      <Card size="small">
        <Space wrap size={8}>
          <Select
            allowClear={canQueryAllStores}
            showSearch
            style={{ width: 260 }}
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
            style={{ width: 140 }}
            placeholder="系统建议"
            value={suggestion}
            options={PRODUCT_MOVEMENT_SUGGESTIONS.map((item) => ({ label: item, value: item }))}
            onChange={(value) => {
              setPage(1)
              setSuggestion(value)
            }}
          />
          <Select
            allowClear
            style={{ width: 130 }}
            placeholder="可信度"
            value={dataCredibility}
            options={PRODUCT_MOVEMENT_CREDIBILITIES.map((item) => ({ label: item, value: item }))}
            onChange={(value) => {
              setPage(1)
              setDataCredibility(value)
            }}
          />
          <Input
            allowClear
            style={{ width: 280 }}
            placeholder="商品编码 / 条码 / 名称"
            value={keywordInput}
            onChange={(event) => setKeywordInput(event.target.value)}
            onPressEnter={handleSearch}
            prefix={<SearchOutlined />}
          />
          <Button icon={<SearchOutlined />} type="primary" onClick={handleSearch}>
            查询
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>
            刷新
          </Button>
        </Space>
      </Card>

      <Row gutter={[12, 12]}>
        {summaryCards.map((item) => (
          <Col key={item.key} xs={12} sm={8} md={6} lg={4}>
            <Card size="small">
              <Space direction="vertical" size={2}>
                <Tag color={getSuggestionTagColor(item.key)}>{item.title}</Tag>
                <Text strong style={{ fontSize: 22 }}>
                  {formatNumber(item.count)}
                </Text>
              </Space>
            </Card>
          </Col>
        ))}
      </Row>

      {requiresStoreSelection ? (
        <Card>
          <Empty description="当前账号关联多个门店，请先选择一个门店查看商品经营分析。" />
        </Card>
      ) : (
        <Card size="small">
          <Space direction="vertical" size={8} style={{ width: '100%' }}>
            <Space wrap>
              <Text type="secondary">{result?.calculationNote}</Text>
              <Text type="secondary">销售统计最后更新时间：{formatDateTime(result?.salesStatisticLastUpdate)}</Text>
            </Space>
            <Space wrap>
              {Object.entries(PRODUCT_MOVEMENT_ACTION_HINTS).map(([key, text]) => (
                <Tooltip key={key} title={text}>
                  <Tag color={getSuggestionTagColor(key)}>{key}</Tag>
                </Tooltip>
              ))}
            </Space>
            <Table<ProductMovementReportRow>
              size="small"
              rowKey={(record) => `${record.storeCode}-${record.productCode}`}
              loading={loading}
              columns={columns}
              dataSource={result?.items ?? []}
              scroll={{ x: 2500 }}
              pagination={{
                current: page,
                pageSize,
                total: result?.total ?? 0,
                showSizeChanger: true,
                pageSizeOptions: PAGE_SIZE_OPTIONS,
                showTotal: (total) => `共 ${total} 个商品`,
              }}
              onChange={handleTableChange}
            />
          </Space>
        </Card>
      )}
    </Space>
  )
}
