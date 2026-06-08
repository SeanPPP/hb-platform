import {
  CopyOutlined,
  FilterOutlined,
  FireOutlined,
  ShoppingCartOutlined,
} from '@ant-design/icons'
import { Alert, Button, Card, Empty, Popover, Select, Space, Table, Tag, Tooltip, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import { addStoreOrderCartItem } from '../../../services/storeOrderService'
import { getBestSellers } from '../../../services/salesDashboardService'
import { useShopStore } from '../../../store/shop'
import type { BestSellerBranchSale, BestSellerProduct } from '../../../types/salesDashboard'
import { copyTextToClipboard } from '../../../utils/clipboard'

const { Text, Title } = Typography

const DEFAULT_PAGE_SIZE = 50
const PAGE_SIZE_OPTIONS = ['20', '50', '100']
const NO_IMAGE_PLACEHOLDER = 'No Image'

const TIME_RANGES = [
  { label: 'Last 7 Days', value: 7 },
  { label: 'Last 30 Days', value: 30 },
  { label: 'Last 60 Days', value: 60 },
  { label: 'Last 90 Days', value: 90 },
]

const BRANCH_SALES_COLUMNS: ColumnsType<BestSellerBranchSale> = [
  {
    title: 'Store',
    key: 'store',
    width: 180,
    render: (_value, record) => record.branchName || record.branchCode || '-',
  },
  {
    title: 'Qty Sold',
    dataIndex: 'quantity',
    key: 'quantity',
    width: 90,
    align: 'right',
    sorter: (a, b) => (a.quantity ?? 0) - (b.quantity ?? 0),
    defaultSortOrder: 'descend',
    render: (value: number | undefined) => value ?? 0,
  },
  {
    title: 'Sales Amount',
    dataIndex: 'salesAmount',
    key: 'salesAmount',
    width: 120,
    align: 'right',
    render: (value: number | undefined) => formatCurrency(value),
  },
  {
    title: 'Gross Profit',
    dataIndex: 'grossProfit',
    key: 'grossProfit',
    width: 120,
    align: 'right',
    render: (value: number | undefined) => formatOptionalCurrency(value),
  },
  {
    title: 'Gross Margin',
    dataIndex: 'grossMarginRate',
    key: 'grossMarginRate',
    width: 110,
    align: 'right',
    render: (value: number | undefined) => formatPercent(value),
  },
]

function formatCurrency(amount?: number) {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
  }).format(amount ?? 0)
}

function formatOptionalCurrency(amount?: number) {
  if (typeof amount !== 'number' || !Number.isFinite(amount)) {
    return '--'
  }

  return formatCurrency(amount)
}

function formatPercent(value?: number) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return new Intl.NumberFormat('en-AU', {
    style: 'percent',
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value)
}

function getStatisticStatusColor(status?: string) {
  if (status === 'Fresh') return 'green'
  if (status === 'Stale') return 'orange'
  if (status === 'Failed') return 'red'
  if (status === 'Pending') return 'blue'
  return 'default'
}

function getBranchSalesRows(product: BestSellerProduct) {
  return [...(product.branchSales ?? [])].sort((a, b) => (b.quantity ?? 0) - (a.quantity ?? 0))
}

function getBranchSalesCount(product: BestSellerProduct) {
  return product.branchSalesCount ?? product.branchSales?.length ?? 0
}

function getAddQuantity(product: BestSellerProduct) {
  return product.minOrderQuantity && product.minOrderQuantity > 0 ? product.minOrderQuantity : 1
}

export default function BestSellersSection() {
  const { t } = useTranslation()
  const [products, setProducts] = useState<BestSellerProduct[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [total, setTotal] = useState(0)
  const [statisticStatus, setStatisticStatus] = useState<string | undefined>()
  const [statisticMessage, setStatisticMessage] = useState<string | undefined>()
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [timeRange, setTimeRange] = useState(30)
  const selectedStore = useShopStore((state) => state.selectedStore)
  const setCart = useShopStore((state) => state.setCart)

  const { startDate, endDate } = useMemo(() => {
    const end = new Date()
    const start = new Date()
    start.setDate(start.getDate() - timeRange)

    return {
      startDate: start.toISOString().slice(0, 10),
      endDate: end.toISOString().slice(0, 10),
    }
  }, [timeRange])

  useEffect(() => {
    let cancelled = false
    const controller = new AbortController()

    const fetchData = async () => {
      setLoading(true)
      setError(null)

      try {
        const result = await getBestSellers(startDate, endDate, undefined, currentPage, pageSize, controller.signal)
        if (cancelled) {
          return
        }

        setProducts(result.products)
        setTotal(result.total)
        setStatisticStatus(result.statisticStatus)
        setStatisticMessage(result.statisticMessage)
      } catch (fetchError) {
        if (cancelled || (fetchError instanceof DOMException && fetchError.name === 'AbortError')) {
          return
        }

        setProducts([])
        setTotal(0)
        setStatisticStatus(undefined)
        setStatisticMessage(undefined)
        setError(fetchError instanceof Error ? 'Failed to load best sellers.' : 'Failed to load best sellers.')
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchData()

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [currentPage, endDate, pageSize, startDate])

  const totalSales = useMemo(
    () => products.reduce((sum, item) => sum + (item.salesAmount ?? 0), 0),
    [products],
  )

  const totalQuantity = useMemo(
    () => products.reduce((sum, item) => sum + (item.quantity ?? 0), 0),
    [products],
  )

  const totalGrossProfit = useMemo(
    () => {
      const rowsWithGrossProfit = products.filter((item) => typeof item.grossProfit === 'number')
      if (!rowsWithGrossProfit.length) {
        return undefined
      }

      return rowsWithGrossProfit.reduce((sum, item) => sum + (item.grossProfit ?? 0), 0)
    },
    [products],
  )

  const grossMarginRate = totalSales > 0 && typeof totalGrossProfit === 'number' ? totalGrossProfit / totalSales : undefined

  const handleAddToCart = useCallback(
    async (product: BestSellerProduct) => {
      if (!selectedStore?.storeCode) {
        message.warning(t('shop.selectStoreFirst', 'Please select a store first'))
        return
      }

      const quantity = getAddQuantity(product)

      try {
        const nextCart = await addStoreOrderCartItem({
          storeCode: selectedStore.storeCode,
          productCode: product.productCode,
          quantity,
        })
        setCart(nextCart)
        message.success(t('shop.addedToCart', { quantity, name: product.productName || product.productCode }))
      } catch {
        message.error(t('shop.addToCartFailed', 'Failed to add to cart'))
      }
    },
    [selectedStore?.storeCode, setCart, t],
  )

  const columns = useMemo<ColumnsType<BestSellerProduct>>(
    () => [
      {
        title: 'Rank',
        dataIndex: 'rank',
        key: 'rank',
        width: 44,
        align: 'center',
        render: (_rank, _record, index) => {
          const rank = (currentPage - 1) * pageSize + index + 1
          return <span className="shop-best-sellers-rank">#{rank}</span>
        },
      },
      {
        title: 'Image',
        dataIndex: 'productImage',
        key: 'productImage',
        width: 50,
        align: 'center',
        render: (value: string | undefined, record) => (
          <div className="shop-best-sellers-image-cell">
            {value ? (
              <img
                src={value}
                alt={record.productName || record.itemNumber || NO_IMAGE_PLACEHOLDER}
                className="shop-best-sellers-image"
                loading="lazy"
                decoding="async"
              />
            ) : (
              <span className="shop-best-sellers-image-placeholder">{NO_IMAGE_PLACEHOLDER}</span>
            )}
          </div>
        ),
      },
      {
        title: 'Barcode',
        dataIndex: 'barcode',
        key: 'barcode',
        width: 104,
        render: (value: string | undefined) => (
          <BarcodePreview
            value={value}
            align="left"
            className="shop-best-sellers-barcode-cell"
            compactCopy={false}
            gap={2}
            options={{ height: 18, width: 1, margin: 0 }}
            showCopy={false}
            textMaxWidth={96}
            textNoWrap
          />
        ),
      },
      {
        title: 'Item No.',
        dataIndex: 'itemNumber',
        key: 'itemNumber',
        width: 92,
        render: (value: string | undefined) =>
          value ? (
            <Space size={4} className="shop-best-sellers-item-number">
              <Text>{value}</Text>
              <Tooltip title={t('common.copy', 'Copy')}>
                <Button
                  size="small"
                  type="text"
                  icon={<CopyOutlined />}
                  onClick={() => void copyTextToClipboard(value)}
                />
              </Tooltip>
            </Space>
          ) : (
            '-'
          ),
      },
      {
        title: 'Product Name',
        dataIndex: 'productName',
        key: 'productName',
        width: 155,
        render: (value: string | undefined) => (
          <div className="shop-best-sellers-product-name">{value || 'Unknown Product'}</div>
        ),
      },
      {
        title: 'Units Sold',
        dataIndex: 'quantity',
        key: 'quantity',
        width: 76,
        align: 'right',
        render: (value: number | undefined) => value ?? 0,
      },
      {
        title: 'Sales Amount',
        dataIndex: 'salesAmount',
        key: 'salesAmount',
        width: 96,
        align: 'right',
        render: (value: number | undefined) => <Text strong>{formatCurrency(value)}</Text>,
      },
      {
        title: 'Gross Profit',
        dataIndex: 'grossProfit',
        key: 'grossProfit',
        width: 84,
        align: 'right',
        render: (value: number | undefined) => <Text>{formatOptionalCurrency(value)}</Text>,
      },
      {
        title: 'Gross Margin',
        dataIndex: 'grossMarginRate',
        key: 'grossMarginRate',
        width: 80,
        align: 'right',
        render: (value: number | undefined) => <Text>{formatPercent(value)}</Text>,
      },
      {
        title: 'Stats',
        dataIndex: 'statisticStatus',
        key: 'statisticStatus',
        width: 64,
        align: 'center',
        render: (value: string | undefined) => (
          <Tag color={getStatisticStatusColor(value || statisticStatus)}>
            {value || statisticStatus || 'Live'}
          </Tag>
        ),
      },
      {
        title: 'Status',
        dataIndex: 'isActive',
        key: 'isActive',
        width: 68,
        align: 'center',
        render: (value: boolean | undefined) => {
          if (value === true) {
            return <Tag color="green">{t('common.activeUpper', 'On Shelf')}</Tag>
          }

          if (value === false) {
            return <Tag color="red">{t('common.inactiveUpper', 'Off Shelf')}</Tag>
          }

          return <Tag>{t('shop.statusUnknown', 'Unknown')}</Tag>
        },
      },
      {
        title: 'Stores Sold',
        key: 'branchSalesCount',
        width: 66,
        align: 'center',
        render: (_value, record) => {
          const rows = getBranchSalesRows(record)
          const count = getBranchSalesCount(record)

          return (
            <Popover
              trigger="click"
              placement="leftTop"
              title={t('shop.branchSalesTitle', 'Store Sales')}
              content={
                <div className="shop-best-sellers-branch-sales-popover">
                  <Table<BestSellerBranchSale>
                    size="small"
                    rowKey={(row) => row.branchCode}
                    columns={BRANCH_SALES_COLUMNS}
                    dataSource={rows}
                    pagination={false}
                    scroll={{ x: 520, y: 320 }}
                    locale={{
                      emptyText: t('shop.noBranchSales', 'No sales by store'),
                    }}
                  />
                </div>
              }
            >
              <Button type="link" size="small" className="shop-best-sellers-store-count">
                {count}
              </Button>
            </Popover>
          )
        },
      },
      {
        title: 'Action',
        key: 'action',
        width: 72,
        align: 'center',
        render: (_value, record) => {
          const disabled = record.isActive !== true || !selectedStore?.storeCode
          const tooltipTitle = !selectedStore?.storeCode
            ? t('shop.selectStoreFirst', 'Please select a store first')
            : record.isActive !== true
              ? t('common.inactiveUpper', 'Off Shelf')
              : ''

          return (
            <Tooltip title={tooltipTitle}>
              <span>
                <Button
                  type="primary"
                  size="small"
                  icon={<ShoppingCartOutlined />}
                  disabled={disabled}
                  onClick={() => void handleAddToCart(record)}
                >
                  {t('common.add', 'Add')}
                </Button>
              </span>
            </Tooltip>
          )
        },
      },
    ],
    [currentPage, handleAddToCart, pageSize, selectedStore?.storeCode, statisticStatus, t],
  )

  return (
    <section className="shop-home-section">
      <div className="shop-home-section-head">
        <div>
          <div className="shop-home-section-eyebrow">
            <FireOutlined />
            Sales Highlights
          </div>
          <Title level={3} className="shop-home-section-title">
            Best Sellers
          </Title>
          <Text type="secondary">Track the top-performing items with sales volume front and center.</Text>
        </div>

        <div className="shop-best-sellers-toolbar">
          {statisticStatus ? (
            <Tooltip title={statisticMessage}>
              <Tag color={getStatisticStatusColor(statisticStatus)}>{statisticStatus}</Tag>
            </Tooltip>
          ) : null}
          <div className="shop-best-sellers-filter">
            <FilterOutlined />
            <span>Time Range</span>
            <Select
              value={timeRange}
              style={{ width: 140 }}
              options={TIME_RANGES}
              onChange={(value) => {
                setTimeRange(value)
                setCurrentPage(1)
              }}
            />
          </div>
        </div>
      </div>

      <div className="shop-home-stat-grid">
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Items on Page</Text>
          <Title level={4}>{products.length}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Units Sold</Text>
          <Title level={4}>{totalQuantity}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Sales Amount</Text>
          <Title level={4}>{formatCurrency(totalSales)}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Gross Profit</Text>
          <Title level={4}>{formatOptionalCurrency(totalGrossProfit)}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Gross Margin</Text>
          <Title level={4}>{formatPercent(grossMarginRate)}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Total Ranked Items</Text>
          <Title level={4}>{total}</Title>
        </Card>
      </div>

      {error ? <Alert type="error" showIcon message={error} className="shop-home-alert" /> : null}

      <Table<BestSellerProduct>
        rowKey={(record) => record.productCode || record.itemNumber || String(record.rank)}
        className="shop-best-sellers-table"
        columns={columns}
        dataSource={products}
        loading={loading}
        size="small"
        virtual
        scroll={{ x: 1080, y: 560 }}
        pagination={{
          current: currentPage,
          pageSize,
          total,
          showSizeChanger: true,
          pageSizeOptions: PAGE_SIZE_OPTIONS,
          onChange: (page, nextPageSize) => {
            if (nextPageSize !== pageSize) {
              setPageSize(nextPageSize)
              setCurrentPage(1)
              return
            }

            setCurrentPage(page)
          },
        }}
        locale={{
          emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No best-seller data found." />,
        }}
      />
    </section>
  )
}
