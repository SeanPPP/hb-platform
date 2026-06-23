import { SearchOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  App as AntdApp,
  Button,
  Card,
  Col,
  DatePicker,
  Form,
  Input,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
import type { Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getStoreOrderImportPriceVariance } from '../../../services/storeOrderService'
import type {
  StoreOrderImportPriceVarianceDirection,
  StoreOrderImportPriceVarianceItem,
  StoreOrderImportPriceVarianceQuery,
  StoreOrderImportPriceVarianceSummary,
} from '../../../types/storeOrder'

const { RangePicker } = DatePicker

type RangeValue = [Dayjs | null, Dayjs | null] | null

interface FilterValues {
  keyword?: string
  storeCode?: string
  orderNo?: string
  orderDateRange?: RangeValue
  varianceDirection?: StoreOrderImportPriceVarianceDirection
}

interface AppliedFilters {
  keyword?: string
  storeCode?: string
  orderNo?: string
  startDate?: string
  endDate?: string
  varianceDirection: StoreOrderImportPriceVarianceDirection
}

const DEFAULT_PAGE_SIZE = 20
const DEFAULT_SORT_BY = 'absoluteVarianceAmount'
const DEFAULT_SORT_DESCENDING = true

const emptySummary: StoreOrderImportPriceVarianceSummary = {
  totalRows: 0,
  originalImportAmountTotal: 0,
  baselineImportAmountTotal: 0,
  varianceAmountTotal: 0,
}

function trimText(value?: string) {
  const text = value?.trim()
  return text || undefined
}

function formatDate(value?: string, language?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleDateString(language?.startsWith('zh') ? 'zh-CN' : 'en-US')
}

function formatMoney(value?: number) {
  return (value ?? 0).toFixed(2)
}

function getRowKey(row: StoreOrderImportPriceVarianceItem) {
  return `${row.orderGUID || 'order'}-${row.detailGUID || row.productCode || row.itemNumber || 'detail'}`
}

function normalizeFilters(values: FilterValues): AppliedFilters {
  return {
    keyword: trimText(values.keyword),
    storeCode: trimText(values.storeCode),
    orderNo: trimText(values.orderNo),
    // 日期范围统一落成后端契约字段，避免页面状态保存 Dayjs 对象。
    startDate: values.orderDateRange?.[0]?.format('YYYY-MM-DD'),
    endDate: values.orderDateRange?.[1]?.format('YYYY-MM-DD'),
    varianceDirection: values.varianceDirection ?? 'all',
  }
}

export default function StoreOrderImportPriceVariancePage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { message } = AntdApp.useApp()
  const [form] = Form.useForm<FilterValues>()
  const [filters, setFilters] = useState<AppliedFilters>({ varianceDirection: 'all' })
  const [items, setItems] = useState<StoreOrderImportPriceVarianceItem[]>([])
  const [summary, setSummary] = useState<StoreOrderImportPriceVarianceSummary>(emptySummary)
  const [total, setTotal] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [sortBy, setSortBy] = useState(DEFAULT_SORT_BY)
  const [sortDescending, setSortDescending] = useState(DEFAULT_SORT_DESCENDING)
  const [loading, setLoading] = useState(false)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const query: StoreOrderImportPriceVarianceQuery = {
        ...filters,
        pageNumber,
        pageSize,
        sortBy,
        sortDescending,
      }
      const result = await getStoreOrderImportPriceVariance(query)
      setItems(result.items)
      setTotal(result.total)
      setPageNumber(result.page)
      setPageSize(result.pageSize)
      setSummary(result.summary)
    } catch (error) {
      console.error(error)
      void message.error(t('storeOrders.importPriceVariance.loadFailed'))
    } finally {
      setLoading(false)
    }
  }, [filters, message, pageNumber, pageSize, sortBy, sortDescending, t])

  useEffect(() => {
    void loadData()
  }, [loadData])

  const openOrderDetail = useCallback(
    (row: StoreOrderImportPriceVarianceItem) => {
      if (!row.orderGUID) {
        return
      }

      navigate(`/warehouse/store-order/detail/${row.orderGUID}`, {
        state: { orderNo: row.orderNo },
      })
    },
    [navigate],
  )

  const openContainerDetail = useCallback(
    (row: StoreOrderImportPriceVarianceItem) => {
      if (!row.firstContainerCode) {
        return
      }

      navigate(`/warehouse/container/detail/${row.firstContainerCode}`)
    },
    [navigate],
  )

  const columns = useMemo<ColumnsType<StoreOrderImportPriceVarianceItem>>(
    () => [
      {
        title: t('storeOrders.importPriceVariance.orderNo'),
        dataIndex: 'orderNo',
        key: 'orderNo',
        width: 150,
        sorter: true,
        render: (value: string | undefined, row) => {
          const text = value || '--'
          if (!row.orderGUID || !value) {
            return text
          }

          return (
            <Button type="link" size="small" onClick={() => openOrderDetail(row)}>
              {text}
            </Button>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.orderDate'),
        dataIndex: 'orderDate',
        key: 'orderDate',
        width: 130,
        sorter: true,
        render: (value?: string) => formatDate(value, i18n.language),
      },
      {
        title: t('storeOrders.importPriceVariance.store'),
        dataIndex: 'storeName',
        key: 'storeCode',
        width: 180,
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{row.storeName || '--'}</Typography.Text>
            <Typography.Text type="secondary">{row.storeCode || '--'}</Typography.Text>
          </Space>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.itemAndProduct'),
        dataIndex: 'productName',
        key: 'productCode',
        width: 240,
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{row.itemNumber || row.productCode || '--'}</Typography.Text>
            <Typography.Text type="secondary">{row.productName || '--'}</Typography.Text>
          </Space>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.orderImportPrice'),
        dataIndex: 'orderImportPrice',
        key: 'orderImportPrice',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerImportPrice'),
        dataIndex: 'firstContainerImportPrice',
        key: 'firstContainerImportPrice',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.allocQuantity'),
        dataIndex: 'allocQuantity',
        key: 'allocQuantity',
        align: 'right',
        width: 100,
        sorter: true,
        render: (value?: number) => value ?? 0,
      },
      {
        title: t('storeOrders.importPriceVariance.originalImportAmount'),
        dataIndex: 'originalImportAmount',
        key: 'originalImportAmount',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.baselineImportAmount'),
        dataIndex: 'baselineImportAmount',
        key: 'baselineImportAmount',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.varianceAmount'),
        dataIndex: 'varianceAmount',
        key: 'varianceAmount',
        align: 'right',
        width: 130,
        sorter: true,
        defaultSortOrder: 'descend',
        render: (value?: number) => {
          const amount = value ?? 0
          const color = amount > 0 ? 'red' : amount < 0 ? 'green' : 'default'
          return <Tag color={color}>{formatMoney(amount)}</Tag>
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerNumber'),
        dataIndex: 'firstContainerNumber',
        key: 'firstContainerNumber',
        width: 150,
        render: (_value, row) => {
          const text = row.firstContainerNumber || row.firstContainerCode || '--'
          if (!row.firstContainerCode) {
            return text
          }

          return (
            <Button type="link" size="small" onClick={() => openContainerDetail(row)}>
              {text}
            </Button>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerDate'),
        dataIndex: 'firstContainerDate',
        key: 'firstContainerDate',
        width: 130,
        sorter: true,
        render: (value?: string) => formatDate(value, i18n.language),
      },
    ],
    [i18n.language, openContainerDetail, openOrderDetail, t],
  )

  const handleSearch = (values: FilterValues) => {
    setFilters(normalizeFilters(values))
    setPageNumber(1)
  }

  const handleReset = () => {
    form.resetFields()
    setFilters({ varianceDirection: 'all' })
    setPageNumber(1)
    setPageSize(DEFAULT_PAGE_SIZE)
    setSortBy(DEFAULT_SORT_BY)
    setSortDescending(DEFAULT_SORT_DESCENDING)
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    _filters: Record<string, unknown>,
    sorter: SorterResult<StoreOrderImportPriceVarianceItem> | SorterResult<StoreOrderImportPriceVarianceItem>[],
  ) => {
    const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
    setPageNumber(pagination.current ?? 1)
    setPageSize(pagination.pageSize ?? DEFAULT_PAGE_SIZE)

    // Antd 清空排序时回到“绝对差额倒序”，保持统计页默认最关注差异最大的记录。
    if (nextSorter?.order) {
      setSortBy(String(nextSorter.columnKey ?? nextSorter.field ?? DEFAULT_SORT_BY))
      setSortDescending(nextSorter.order === 'descend')
    } else {
      setSortBy(DEFAULT_SORT_BY)
      setSortDescending(DEFAULT_SORT_DESCENDING)
    }
  }

  return (
    <PageContainer title={t('storeOrders.importPriceVariance.title')}>
      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ varianceDirection: 'all' }}
          onFinish={handleSearch}
        >
          <Row gutter={16}>
            <Col xs={24} md={8} xl={5}>
              <Form.Item name="keyword" label={t('storeOrders.importPriceVariance.keyword')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.keywordPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} xl={4}>
              <Form.Item name="storeCode" label={t('storeOrders.importPriceVariance.storeCode')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.storeCodePlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} xl={4}>
              <Form.Item name="orderNo" label={t('storeOrders.importPriceVariance.orderNo')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.orderNoPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12} xl={6}>
              <Form.Item name="orderDateRange" label={t('storeOrders.importPriceVariance.orderDateRange')}>
                <RangePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12} xl={5}>
              <Form.Item name="varianceDirection" label={t('storeOrders.importPriceVariance.varianceDirection')}>
                <Select
                  options={[
                    { value: 'all', label: t('storeOrders.importPriceVariance.directionAll') },
                    { value: 'increase', label: t('storeOrders.importPriceVariance.directionIncrease') },
                    { value: 'decrease', label: t('storeOrders.importPriceVariance.directionDecrease') },
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>
          <Space>
            <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
              {t('common.search')}
            </Button>
            <Button onClick={handleReset} icon={<ReloadOutlined />}>
              {t('common.reset')}
            </Button>
          </Space>
        </Form>
      </Card>

      <Row gutter={16} style={{ marginTop: 16 }}>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.originalImportAmountTotal')}
              value={formatMoney(summary.originalImportAmountTotal)}
            />
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.baselineImportAmountTotal')}
              value={formatMoney(summary.baselineImportAmountTotal)}
            />
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.varianceAmountTotal')}
              value={formatMoney(summary.varianceAmountTotal)}
              valueStyle={{
                color:
                  summary.varianceAmountTotal > 0
                    ? '#cf1322'
                    : summary.varianceAmountTotal < 0
                      ? '#389e0d'
                      : undefined,
              }}
            />
          </Card>
        </Col>
      </Row>

      <Card style={{ marginTop: 16 }}>
        <Table<StoreOrderImportPriceVarianceItem>
          rowKey={getRowKey}
          loading={loading}
          columns={columns}
          dataSource={items}
          scroll={{ x: 1600 }}
          onChange={handleTableChange}
          pagination={{
            current: pageNumber,
            pageSize,
            total,
            showSizeChanger: true,
            showTotal: (value) => t('storeOrders.importPriceVariance.totalRows', { total: value }),
          }}
        />
      </Card>
    </PageContainer>
  )
}
