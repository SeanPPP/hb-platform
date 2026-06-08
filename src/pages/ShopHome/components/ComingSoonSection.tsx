import {
  CalendarOutlined,
  ClockCircleOutlined,
  InboxOutlined,
  ReloadOutlined,
  TagsOutlined,
} from '@ant-design/icons'
import { Alert, Badge, Button, Card, Empty, Image, Segmented, Skeleton, Spin, Tag, Typography } from 'antd'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getComingSoonContainerProducts,
  getComingSoonContainerSummaries,
} from '../../../services/containerService'
import BarcodePreview from '../../../components/BarcodePreview'
import type { ComingSoonHomeContainerSummary, ComingSoonHomeProduct } from '../../../types/container'

const { Text, Title } = Typography

const PRODUCT_ROW_HEIGHT = 176
const VIRTUAL_OVERSCAN = 4
const PRODUCT_LIST_HEIGHT = 560
const COMING_SOON_BARCODE_OPTIONS = {
  width: 1,
  height: 34,
  displayValue: false,
  margin: 0,
}

type FilterMode = 'all' | 'reorder' | 'new'
type ProductLoadStatus = 'idle' | 'loading' | 'loaded' | 'error'
type DateTone = 'arrived' | 'soon' | 'future' | 'unknown'

interface ContainerProductState {
  status: ProductLoadStatus
  products: ComingSoonHomeProduct[]
  error?: string
}

interface VirtualScrollState {
  scrollTop: number
  viewportHeight: number
}

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

function formatComingSoonRetailPrice(price?: number) {
  if (typeof price !== 'number' || Number.isNaN(price)) {
    return ''
  }

  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
  }).format(price)
}

function parseDate(dateStr?: string) {
  if (!dateStr) {
    return null
  }

  const date = new Date(dateStr)
  return Number.isNaN(date.getTime()) ? null : date
}

function getComingSoonDateTone(container: ComingSoonHomeContainerSummary): DateTone {
  const arrivalDate = parseDate(container.实际到货日期)
  if (arrivalDate) {
    return 'arrived'
  }

  const etaDate = parseDate(container.预计到岸日期)
  if (!etaDate) {
    return 'unknown'
  }

  const today = new Date()
  today.setHours(0, 0, 0, 0)
  etaDate.setHours(0, 0, 0, 0)

  const daysUntilEta = Math.round((etaDate.getTime() - today.getTime()) / 86_400_000)
  if (daysUntilEta >= 0 && daysUntilEta <= 7) {
    return 'soon'
  }

  if (daysUntilEta > 7) {
    return 'future'
  }

  return 'unknown'
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

function getFilterLabel(filterMode: FilterMode) {
  if (filterMode === 'new') {
    return 'New'
  }

  if (filterMode === 'reorder') {
    return 'Reorder'
  }

  return 'All'
}

function getContainerFilterStats(products: ComingSoonHomeProduct[]) {
  return products.reduce(
    (acc, product) => {
      acc.all += 1
      if (product.isNewProduct) {
        acc.new += 1
      } else {
        acc.reorder += 1
      }
      return acc
    },
    { all: 0, reorder: 0, new: 0 },
  )
}

function getContainerProductState(
  productStates: Record<string, ContainerProductState>,
  containerGuid: string,
): ContainerProductState {
  return productStates[containerGuid] ?? { status: 'idle', products: [] }
}

function getVisibleProductWindow(products: ComingSoonHomeProduct[], scrollState?: VirtualScrollState) {
  const viewportHeight = scrollState?.viewportHeight || PRODUCT_LIST_HEIGHT
  const scrollTop = scrollState?.scrollTop || 0
  const startIndex = Math.max(Math.floor(scrollTop / PRODUCT_ROW_HEIGHT) - VIRTUAL_OVERSCAN, 0)
  const endIndex = Math.min(
    Math.ceil((scrollTop + viewportHeight) / PRODUCT_ROW_HEIGHT) + VIRTUAL_OVERSCAN,
    products.length,
  )

  return {
    startIndex,
    endIndex,
    beforeHeight: startIndex * PRODUCT_ROW_HEIGHT,
    afterHeight: Math.max((products.length - endIndex) * PRODUCT_ROW_HEIGHT, 0),
    visibleProducts: products.slice(startIndex, endIndex),
  }
}

export default function ComingSoonSection() {
  const { t } = useTranslation()
  const [containers, setContainers] = useState<ComingSoonHomeContainerSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filterMode, setFilterMode] = useState<FilterMode>('all')
  const [containerFilterModes, setContainerFilterModes] = useState<Record<string, FilterMode>>({})
  const [productStates, setProductStates] = useState<Record<string, ContainerProductState>>({})
  const [virtualScrollStates, setVirtualScrollStates] = useState<Record<string, VirtualScrollState>>({})
  const listRef = useRef<HTMLDivElement | null>(null)
  const observerRef = useRef<IntersectionObserver | null>(null)
  const loadStartedRef = useRef<Set<string>>(new Set())
  const cardElementsRef = useRef<Map<string, HTMLDivElement>>(new Map())
  const productListElementsRef = useRef<Map<string, HTMLDivElement>>(new Map())

  const loadContainerProducts = useCallback(async (containerGuid: string) => {
    if (loadStartedRef.current.has(containerGuid)) {
      return
    }

    loadStartedRef.current.add(containerGuid)
    setProductStates((prev) => ({
      ...prev,
      [containerGuid]: { status: 'loading', products: prev[containerGuid]?.products ?? [] },
    }))

    try {
      const products = await getComingSoonContainerProducts(containerGuid)
      setProductStates((prev) => ({
        ...prev,
        [containerGuid]: { status: 'loaded', products },
      }))
    } catch {
      loadStartedRef.current.delete(containerGuid)
      setProductStates((prev) => ({
        ...prev,
        [containerGuid]: {
          status: 'error',
          products: prev[containerGuid]?.products ?? [],
          error: 'Failed to load products.',
        },
      }))
    }
  }, [])

  useEffect(() => {
    let cancelled = false

    const fetchSummaries = async () => {
      setLoading(true)
      setError(null)

      try {
        const result = await getComingSoonContainerSummaries()
        if (cancelled) {
          return
        }

        setContainers(result)
        setProductStates({})
        setContainerFilterModes({})
        setVirtualScrollStates({})
        loadStartedRef.current.clear()
      } catch {
        if (cancelled) {
          return
        }

        setContainers([])
        setProductStates({})
        setContainerFilterModes({})
        setError('Failed to load coming soon data.')
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchSummaries()

    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (!containers.length) {
      return
    }

    observerRef.current?.disconnect()

    if (typeof IntersectionObserver === 'undefined') {
      containers.forEach((container) => {
        void loadContainerProducts(container.hguid)
      })
      return
    }

    // 货柜横向滚动时，只给进入可视区域的货柜加载商品，避免首屏一次拉完整明细。
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) {
            return
          }

          const containerGuid = (entry.target as HTMLElement).dataset.containerGuid
          if (containerGuid) {
            void loadContainerProducts(containerGuid)
            observer.unobserve(entry.target)
          }
        })
      },
      {
        root: listRef.current,
        rootMargin: '160px',
        threshold: 0.08,
      },
    )

    observerRef.current = observer
    cardElementsRef.current.forEach((element) => observer.observe(element))

    return () => {
      observer.disconnect()
    }
  }, [containers, loadContainerProducts])

  const setContainerCardRef = useCallback((containerGuid: string, element: HTMLDivElement | null) => {
    const previousElement = cardElementsRef.current.get(containerGuid)
    if (previousElement) {
      observerRef.current?.unobserve(previousElement)
      cardElementsRef.current.delete(containerGuid)
    }

    if (!element) {
      return
    }

    cardElementsRef.current.set(containerGuid, element)
    observerRef.current?.observe(element)
  }, [])

  const setProductListRef = useCallback((containerGuid: string, element: HTMLDivElement | null) => {
    if (!element) {
      productListElementsRef.current.delete(containerGuid)
      return
    }

    productListElementsRef.current.set(containerGuid, element)
  }, [])

  const resetProductScrollPositions = useCallback(() => {
    // 筛选会改变虚拟列表数据集，DOM 滚动位置也要同步归零，避免 spacer 和可见窗口错位。
    productListElementsRef.current.forEach((element) => {
      element.scrollTop = 0
    })
    setVirtualScrollStates({})
  }, [])

  const resetContainerProductScrollPosition = useCallback((containerGuid: string) => {
    // 单个货柜筛选只重置自己的虚拟列表，避免影响其他货柜的浏览位置。
    const element = productListElementsRef.current.get(containerGuid)
    if (element) {
      element.scrollTop = 0
    }

    setVirtualScrollStates((prev) => {
      const { [containerGuid]: _removed, ...next } = prev
      return next
    })
  }, [])

  const handleContainerFilterChange = useCallback((containerGuid: string, nextFilterMode: FilterMode) => {
    setContainerFilterModes((prev) => ({
      ...prev,
      [containerGuid]: nextFilterMode,
    }))
    resetContainerProductScrollPosition(containerGuid)
  }, [resetContainerProductScrollPosition])

  const visibleContainers = useMemo(() => {
    return containers.filter((container) => {
      if (filterMode === 'all') {
        return true
      }

      const state = getContainerProductState(productStates, container.hguid)
      if (state.status !== 'loaded') {
        return true
      }

      return state.products.some((product) => matchesFilter(product, filterMode))
    })
  }, [containers, filterMode, productStates])

  const stats = useMemo(() => {
    return containers.reduce(
      (acc, container) => {
        const state = getContainerProductState(productStates, container.hguid)
        const filteredProducts = state.products.filter((product) => matchesFilter(product, filterMode))

        acc.containers += 1
        acc.products += filteredProducts.length

        if (container.实际到货日期) {
          acc.arrived += 1
        } else {
          acc.incoming += 1
        }

        acc.newProducts += state.products.filter((item) => item.isNewProduct).length
        return acc
      },
      { containers: 0, incoming: 0, arrived: 0, products: 0, newProducts: 0 },
    )
  }, [containers, filterMode, productStates])

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
            setContainerFilterModes({})
            resetProductScrollPositions()
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
      ) : visibleContainers.length ? (
        <div className="shop-coming-soon-list" ref={listRef}>
          {visibleContainers.map((container) => {
            const isArrived = !!container.实际到货日期
            const displayDate = isArrived ? container.实际到货日期 : container.预计到岸日期
            const dateTone = getComingSoonDateTone(container)
            const state = getContainerProductState(productStates, container.hguid)
            const containerFilterMode = containerFilterModes[container.hguid] ?? filterMode
            const containerFilterStats = getContainerFilterStats(state.products)
            const filteredProducts = state.products.filter((product) => matchesFilter(product, containerFilterMode))
            const virtualWindow = getVisibleProductWindow(filteredProducts, virtualScrollStates[container.hguid])
            const { visibleProducts } = virtualWindow

            return (
              <Card
                key={container.hguid}
                className="shop-coming-soon-card"
                ref={(node) => setContainerCardRef(container.hguid, node)}
                data-container-guid={container.hguid}
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
                      <Tag color={containerFilterMode === 'new' ? 'magenta' : 'blue'}>{`${getFilterLabel(containerFilterMode)} View`}</Tag>
                    </div>
                    <div className="shop-coming-soon-card-title-side">
                      <span className={`shop-coming-soon-date-badge shop-coming-soon-date-badge--${dateTone}`}>
                        <CalendarOutlined />
                        <span>{isArrived ? 'Arrival Date' : 'ETA'}</span>
                        <strong>{formatDate(displayDate)}</strong>
                      </span>
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
                    <span>{state.status === 'loaded' ? `${filteredProducts.length} Items` : 'Loading on view'}</span>
                  </div>
                </div>

                <div className="shop-coming-soon-container-filters" aria-label={`${container.货柜编号 || 'Container'} product filters`}>
                  {([
                    ['all', t('shop.comingSoonFilterAll', 'All'), containerFilterStats.all],
                    ['reorder', t('shop.comingSoonFilterReorder', 'Reorder'), containerFilterStats.reorder],
                    ['new', t('shop.comingSoonFilterNew', 'New'), containerFilterStats.new],
                  ] as const).map(([mode, label, count]) => (
                    <Button
                      key={mode}
                      size="small"
                      type={containerFilterMode === mode ? 'primary' : 'default'}
                      disabled={state.status !== 'loaded'}
                      onClick={() => handleContainerFilterChange(container.hguid, mode)}
                    >
                      <span>{label}</span>
                      <strong>{state.status === 'loaded' ? count : '-'}</strong>
                    </Button>
                  ))}
                </div>

                {state.status === 'idle' || state.status === 'loading' ? (
                  <div className="shop-coming-soon-products-placeholder">
                    <Skeleton.Image active />
                    <Skeleton active paragraph={{ rows: 8 }} title={false} />
                  </div>
                ) : state.status === 'error' ? (
                  <Alert
                    type="warning"
                    showIcon
                    message={state.error}
                    action={
                      <Button size="small" icon={<ReloadOutlined />} onClick={() => void loadContainerProducts(container.hguid)}>
                        Retry
                      </Button>
                    }
                  />
                ) : filteredProducts.length ? (
                  <div
                    className="shop-coming-soon-product-grid"
                    ref={(node) => setProductListRef(container.hguid, node)}
                    onScroll={(event) => {
                      const target = event.currentTarget
                      setVirtualScrollStates((prev) => ({
                        ...prev,
                        [container.hguid]: {
                          scrollTop: target.scrollTop,
                          viewportHeight: target.clientHeight,
                        },
                      }))
                    }}
                  >
                    <div
                      className="shop-coming-soon-virtual-spacer"
                      style={{ height: virtualWindow.beforeHeight }}
                    />
                    {visibleProducts.map((product) => (
                      <div key={`${container.hguid}-${product.id}-${product.productCode || product.itemNumber}`} className="shop-coming-soon-product-card">
                        <div className="shop-coming-soon-product-image-wrap">
                          <Image
                            src={product.productImage || 'https://via.placeholder.com/160x160?text=No+Image'}
                            alt={product.productName}
                            fallback="https://via.placeholder.com/160x160?text=No+Image"
                            preview={false}
                            loading="lazy"
                            className="shop-coming-soon-product-image"
                          />
                        </div>
                        <div className="shop-coming-soon-product-body">
                          <div className="shop-coming-soon-product-name">{product.productName || product.englishName || 'Unknown Product'}</div>
                          <div className="shop-coming-soon-product-row">
                            <Text type="secondary">Item No.</Text>
                            <Text copyable className="shop-coming-soon-item-number-value">{product.itemNumber || '-'}</Text>
                          </div>
                          <div className="shop-coming-soon-product-row">
                            <Text type="secondary">Container Qty</Text>
                            <Text strong>{product.quantity ?? 0}</Text>
                          </div>
                          <div className="shop-coming-soon-product-row">
                            <Text type="secondary">{t('shop.rrp', 'RRP')}</Text>
                            <Text strong>{formatComingSoonRetailPrice(product.retailPrice)}</Text>
                          </div>
                          <div className="shop-coming-soon-product-tags">
                            {product.isNewProduct ? <Tag color="magenta">New</Tag> : <Tag>Reorder</Tag>}
                          </div>
                        </div>
                        <div className="shop-coming-soon-barcode-cell">
                          <BarcodePreview
                            value={product.barcode}
                            options={COMING_SOON_BARCODE_OPTIONS}
                            align="center"
                            showText
                            showCopy={false}
                            textNoWrap
                            className="shop-coming-soon-barcode-preview"
                          />
                        </div>
                      </div>
                    ))}
                    <div
                      className="shop-coming-soon-virtual-spacer"
                      style={{ height: virtualWindow.afterHeight }}
                    />
                  </div>
                ) : (
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No products found for this filter." />
                )}

                <div className="shop-coming-soon-card-footer">
                  <div className="shop-coming-soon-more-note">
                    {state.status === 'loaded'
                      ? `Showing ${visibleProducts.length} visible of ${filteredProducts.length} items`
                      : 'Products load when the container card becomes visible'}
                  </div>
                </div>
              </Card>
            )
          })}
        </div>
      ) : (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No incoming products found for the selected filter." />
      )}
    </section>
  )
}
