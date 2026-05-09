import {
  FilterOutlined,
  FireOutlined,
} from '@ant-design/icons'
import { Alert, Badge, Card, Empty, Image, Pagination, Select, Spin, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { getBestSellers } from '../../../services/salesDashboardService'
import type { BestSellerProduct } from '../../../types/salesDashboard'

const { Text, Title } = Typography

const DEFAULT_PAGE_SIZE = 50
const PAGE_SIZE_OPTIONS = ['20', '50', '100']

const TIME_RANGES = [
  { label: 'Last 7 Days', value: 7 },
  { label: 'Last 30 Days', value: 30 },
  { label: 'Last 60 Days', value: 60 },
  { label: 'Last 90 Days', value: 90 },
]

function formatCurrency(amount?: number) {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
  }).format(amount ?? 0)
}

export default function BestSellersSection() {
  const [products, setProducts] = useState<BestSellerProduct[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [timeRange, setTimeRange] = useState(30)

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

    const fetchData = async () => {
      setLoading(true)
      setError(null)

      try {
        const result = await getBestSellers(startDate, endDate, undefined, currentPage, pageSize)
        if (cancelled) {
          return
        }

        setProducts(result.products)
        setTotal(result.total)
      } catch (fetchError) {
        if (cancelled) {
          return
        }

        setProducts([])
        setTotal(0)
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
          <Text type="secondary">Total Ranked Items</Text>
          <Title level={4}>{total}</Title>
        </Card>
      </div>

      {error ? <Alert type="error" showIcon message={error} className="shop-home-alert" /> : null}

      {loading && !products.length ? (
        <div className="shop-home-section-loading">
          <Spin size="large" />
        </div>
      ) : products.length ? (
        <>
          <div className="shop-best-sellers-grid">
            {products.map((product, index) => {
              const rank = (currentPage - 1) * pageSize + index + 1
              const ribbonColor =
                rank === 1 ? '#f59e0b' : rank === 2 ? '#6366f1' : rank === 3 ? '#14b8a6' : '#64748b'

              return (
                <Badge.Ribbon key={product.productCode} text={`#${rank}`} color={ribbonColor}>
                  <Card hoverable className="shop-best-seller-card">
                    <div className="shop-best-seller-card-top">
                      <div className="shop-best-seller-image-wrap">
                        <Image
                          src={product.productImage || 'https://via.placeholder.com/240x240?text=No+Image'}
                          alt={product.productName}
                          preview={false}
                          fallback="https://via.placeholder.com/240x240?text=No+Image"
                          className="shop-best-seller-image"
                        />
                      </div>
                      <div className="shop-best-seller-sales">
                        <div className="shop-best-seller-sales-label">Units Sold</div>
                        <div className="shop-best-seller-sales-value">{product.quantity ?? 0}</div>
                      </div>
                    </div>

                    <div className="shop-best-seller-card-body">
                      <div className="shop-best-seller-name">{product.productName || 'Unknown Product'}</div>
                      <div className="shop-best-seller-meta">
                        <Text type="secondary">Item No.</Text>
                        <Text copyable>{product.itemNumber || '-'}</Text>
                      </div>
                      <div className="shop-best-seller-meta">
                        <Text type="secondary">Sales Amount</Text>
                        <Text strong>{formatCurrency(product.salesAmount)}</Text>
                      </div>
                    </div>
                  </Card>
                </Badge.Ribbon>
              )
            })}
          </div>

          <div className="shop-home-section-pagination">
            <Pagination
              current={currentPage}
              pageSize={pageSize}
              total={total}
              showSizeChanger
              pageSizeOptions={PAGE_SIZE_OPTIONS}
              onChange={(page, nextPageSize) => {
                if (nextPageSize !== pageSize) {
                  setPageSize(nextPageSize)
                  setCurrentPage(1)
                  window.scrollTo({ top: 0, behavior: 'smooth' })
                  return
                }
                setCurrentPage(page)
                window.scrollTo({ top: 0, behavior: 'smooth' })
              }}
            />
          </div>
        </>
      ) : (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No best-seller data found." />
      )}
    </section>
  )
}
