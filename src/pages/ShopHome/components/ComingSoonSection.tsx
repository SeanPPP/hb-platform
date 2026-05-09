import {
  CalendarOutlined,
  ClockCircleOutlined,
  InboxOutlined,
  TagsOutlined,
} from '@ant-design/icons'
import { Alert, Badge, Card, Empty, Image, Pagination, Segmented, Spin, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { getComingSoonContainers } from '../../../services/containerService'
import type { ComingSoonHomeContainer, ComingSoonHomeProduct } from '../../../types/container'

const { Text, Title } = Typography

const PAGE_SIZE = 4
const DEFAULT_PRODUCT_PAGE_SIZE = 20
const PRODUCT_PAGE_SIZE_OPTIONS = ['20', '50', '100']

type FilterMode = 'all' | 'reorder' | 'new'

function formatDate(dateStr?: string) {
  if (!dateStr) {
    return '-'
  }

  const date = new Date(dateStr)
  if (Number.isNaN(date.getTime())) {
    return dateStr
  }

  return date.toLocaleDateString('en-AU', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

function matchesFilter(product: ComingSoonHomeProduct, filterMode: FilterMode) {
  if (filterMode === 'new') {
    return product.isNewProduct
  }

  if (filterMode === 'reorder') {
    return !product.isNewProduct
  }

  return true
}

export default function ComingSoonSection() {
  const [containers, setContainers] = useState<ComingSoonHomeContainer[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filterMode, setFilterMode] = useState<FilterMode>('all')
  const [currentPage, setCurrentPage] = useState(1)
  const [productPages, setProductPages] = useState<Record<string, number>>({})
  const [productPageSizes, setProductPageSizes] = useState<Record<string, number>>({})

  useEffect(() => {
    let cancelled = false

    const fetchData = async () => {
      setLoading(true)
      setError(null)

      try {
        const result = await getComingSoonContainers()
        if (cancelled) {
          return
        }

        setContainers(result)
      } catch (fetchError) {
        if (cancelled) {
          return
        }

        setContainers([])
        setError(fetchError instanceof Error ? 'Failed to load coming soon data.' : 'Failed to load coming soon data.')
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
  }, [])

  const filteredContainers = useMemo(() => {
    return containers
      .map((container) => ({
        ...container,
        商品列表: (container.商品列表 || []).filter((product) => matchesFilter(product, filterMode)),
      }))
      .filter((container) => container.商品列表.length > 0)
  }, [containers, filterMode])

  const paginatedContainers = useMemo(() => {
    const startIndex = (currentPage - 1) * PAGE_SIZE
    return filteredContainers.slice(startIndex, startIndex + PAGE_SIZE)
  }, [currentPage, filteredContainers])

  useEffect(() => {
    const maxPage = Math.max(Math.ceil(filteredContainers.length / PAGE_SIZE), 1)
    if (currentPage > maxPage) {
      setCurrentPage(maxPage)
    }
  }, [currentPage, filteredContainers.length])

  const stats = useMemo(() => {
    return filteredContainers.reduce(
      (acc, container) => {
        acc.containers += 1
        acc.products += container.商品列表.length

        if (container.实际到货日期) {
          acc.arrived += 1
        } else {
          acc.incoming += 1
        }

        acc.newProducts += container.商品列表.filter((item) => item.isNewProduct).length
        return acc
      },
      { containers: 0, incoming: 0, arrived: 0, products: 0, newProducts: 0 },
    )
  }, [filteredContainers])

  return (
    <section className="shop-home-section">
      <div className="shop-home-section-head">
        <div>
          <div className="shop-home-section-eyebrow">
            <InboxOutlined />
            Arrival Tracking
          </div>
          <Title level={3} className="shop-home-section-title">
            Coming Soon
          </Title>
          <Text type="secondary">Review incoming containers with quick filters for reorder and new items.</Text>
        </div>

        <Segmented<FilterMode>
          value={filterMode}
          options={[
            { label: 'All', value: 'all' },
            { label: 'Reorder', value: 'reorder' },
            { label: 'New', value: 'new' },
          ]}
          onChange={(value) => {
            setFilterMode(value)
            setCurrentPage(1)
            setProductPages({})
          }}
        />
      </div>

      <div className="shop-home-stat-grid">
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Containers</Text>
          <Title level={4}>{stats.containers}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Incoming</Text>
          <Title level={4}>{stats.incoming}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">Arrived</Text>
          <Title level={4}>{stats.arrived}</Title>
        </Card>
        <Card size="small" className="shop-home-stat-card">
          <Text type="secondary">New Products</Text>
          <Title level={4}>{stats.newProducts}</Title>
        </Card>
      </div>

      {error ? <Alert type="error" showIcon message={error} className="shop-home-alert" /> : null}

      {loading && !containers.length ? (
        <div className="shop-home-section-loading">
          <Spin size="large" />
        </div>
      ) : paginatedContainers.length ? (
        <>
          <div className="shop-coming-soon-list">
            {paginatedContainers.map((container) => {
              const isArrived = !!container.实际到货日期
              const displayDate = isArrived ? container.实际到货日期 : container.预计到岸日期
              const productPageSize = productPageSizes[container.hguid] ?? DEFAULT_PRODUCT_PAGE_SIZE
              const maxProductPage = Math.max(Math.ceil(container.商品列表.length / productPageSize), 1)
              const productCurrentPage = Math.min(productPages[container.hguid] ?? 1, maxProductPage)
              const productStartIndex = (productCurrentPage - 1) * productPageSize
              const visibleProducts = container.商品列表.slice(productStartIndex, productStartIndex + productPageSize)
              const rangeStart = container.商品列表.length ? productStartIndex + 1 : 0
              const rangeEnd = Math.min(productStartIndex + visibleProducts.length, container.商品列表.length)

              return (
                <Card
                  key={container.hguid}
                  className="shop-coming-soon-card"
                  title={
                    <div className="shop-coming-soon-card-title">
                      <div className="shop-coming-soon-card-title-main">
                        <Text strong className="shop-coming-soon-card-code">
                          {container.货柜编号 || 'N/A'}
                        </Text>
                        <Badge
                          status={isArrived ? 'success' : 'processing'}
                          text={isArrived ? 'Arrived' : 'Incoming'}
                        />
                        <Tag color={filterMode === 'new' ? 'magenta' : 'blue'}>{filterMode === 'new' ? 'New View' : filterMode === 'reorder' ? 'Reorder View' : 'All Items'}</Tag>
                      </div>
                      <div className="shop-coming-soon-card-title-side">
                        <CalendarOutlined />
                        <span>{isArrived ? 'Arrival Date' : 'ETA'}</span>
                        <Text strong>{formatDate(displayDate)}</Text>
                      </div>
                    </div>
                  }
                >
                  <div className="shop-coming-soon-card-meta">
                    <div className="shop-coming-soon-card-meta-item">
                      <ClockCircleOutlined />
                      <span>{isArrived ? 'Arrived in the Last 7 Days' : 'Expected in the Next 8 Weeks'}</span>
                    </div>
                    <div className="shop-coming-soon-card-meta-item">
                      <TagsOutlined />
                      <span>{container.商品列表.length} Items</span>
                    </div>
                  </div>

                  <div className="shop-coming-soon-product-grid">
                    {visibleProducts.map((product) => (
                      <div key={`${container.hguid}-${product.id}-${product.productCode || product.itemNumber}`} className="shop-coming-soon-product-card">
                        <div className="shop-coming-soon-product-image-wrap">
                          <Image
                            src={product.productImage || 'https://via.placeholder.com/160x160?text=No+Image'}
                            alt={product.productName}
                            fallback="https://via.placeholder.com/160x160?text=No+Image"
                            preview={false}
                            className="shop-coming-soon-product-image"
                          />
                        </div>
                        <div className="shop-coming-soon-product-body">
                          <div className="shop-coming-soon-product-name">{product.productName || product.englishName || 'Unknown Product'}</div>
                          <div className="shop-coming-soon-product-row">
                            <Text type="secondary">Item No.</Text>
                            <Text copyable>{product.itemNumber || '-'}</Text>
                          </div>
                          <div className="shop-coming-soon-product-row">
                            <Text type="secondary">Container Qty</Text>
                            <Text strong>{product.quantity ?? 0}</Text>
                          </div>
                          <div className="shop-coming-soon-product-tags">
                            {product.isNewProduct ? <Tag color="magenta">New</Tag> : <Tag>Reorder</Tag>}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>

                  <div className="shop-coming-soon-card-footer">
                    <div className="shop-coming-soon-more-note">
                      Showing {rangeStart}-{rangeEnd} of {container.商品列表.length} items
                    </div>
                    <Pagination
                      current={productCurrentPage}
                      pageSize={productPageSize}
                      total={container.商品列表.length}
                      showSizeChanger
                      pageSizeOptions={PRODUCT_PAGE_SIZE_OPTIONS}
                      size="small"
                      onChange={(page, nextPageSize) => {
                        if (nextPageSize !== productPageSize) {
                          setProductPageSizes((prev) => ({
                            ...prev,
                            [container.hguid]: nextPageSize,
                          }))
                          setProductPages((prev) => ({
                            ...prev,
                            [container.hguid]: 1,
                          }))
                          return
                        }

                        setProductPages((prev) => ({
                          ...prev,
                          [container.hguid]: page,
                        }))
                      }}
                    />
                  </div>
                </Card>
              )
            })}
          </div>

          <div className="shop-home-section-pagination">
            <Pagination
              current={currentPage}
              pageSize={PAGE_SIZE}
              total={filteredContainers.length}
              showSizeChanger={false}
              onChange={(page) => {
                setCurrentPage(page)
                window.scrollTo({ top: 0, behavior: 'smooth' })
              }}
            />
          </div>
        </>
      ) : (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No incoming products found for the selected filter." />
      )}
    </section>
  )
}
