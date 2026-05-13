import {
  ArrowLeftOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  EnvironmentOutlined,
  FileTextOutlined,
  InboxOutlined,
  ShoppingOutlined,
  ShopOutlined,
  TagOutlined,
} from '@ant-design/icons'
import { Button, Empty, Image, Segmented, Space, Spin, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import BarcodePreview from '../../components/BarcodePreview'
import { getStoreOrderDetail } from '../../services/storeOrderService'
import { useShopStore } from '../../store/shop'
import {
  StoreOrderFlowStatus,
  StoreOrderStatusColorMap,
  StoreOrderStatusLabelMap,
  type StoreOrderDetail,
  type StoreOrderDetailLine,
} from '../../types/storeOrder'

const { Text, Title } = Typography
type LineSortMode = 'shortage' | 'itemNumber'

function formatDateTime(value?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString('zh-CN', {
    hour12: false,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatMoney(value?: number) {
  return `$${(value ?? 0).toFixed(2)}`
}

function formatVolume(value?: number) {
  return `${(value ?? 0).toFixed(4)} cbm`
}

function getLineStatus(line: StoreOrderDetailLine) {
  const allocQuantity = line.allocQuantity ?? 0
  if (allocQuantity === 0) {
    return { label: '待发货', color: 'default' as const }
  }

  if (allocQuantity < line.quantity) {
    return { label: '部分发货', color: 'warning' as const }
  }

  return { label: '已发货', color: 'success' as const }
}

function getShortageQuantity(line: StoreOrderDetailLine) {
  return Math.max((line.quantity ?? 0) - (line.allocQuantity ?? 0), 0)
}

export default function ShopOrderDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const userStores = useShopStore((state) => state.userStores)
  const selectedStore = useShopStore((state) => state.selectedStore)

  const [detail, setDetail] = useState<StoreOrderDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [showShortageOnly, setShowShortageOnly] = useState(false)
  const [sortMode, setSortMode] = useState<LineSortMode>('shortage')

  useEffect(() => {
    let cancelled = false

    const fetchDetail = async () => {
      if (!id) {
        setLoading(false)
        return
      }

      setLoading(true)
      try {
        const result = await getStoreOrderDetail(id)
        if (!cancelled) {
          setDetail(result)
        }
      } catch (error) {
        if (!cancelled) {
          setDetail(null)
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchDetail()

    return () => {
      cancelled = true
    }
  }, [id])

  const storeName = useMemo(() => {
    if (!detail?.storeCode) {
      return selectedStore?.storeName || '未知分店'
    }

    return (
      userStores.find((item) => item.storeCode === detail.storeCode)?.storeName ||
      selectedStore?.storeName ||
      detail.storeCode
    )
  }, [detail?.storeCode, selectedStore?.storeName, userStores])

  const summaryStats = useMemo(() => {
    const items = detail?.items ?? []
    const autoAllocatedQuantity = items.reduce((sum, item) => sum + Number(item.allocQuantity ?? 0), 0)
    const shortageQuantity = items.reduce((sum, item) => sum + getShortageQuantity(item), 0)

    return [
      {
        label: '订货数量',
        value: detail?.totalQuantity ?? 0,
      },
      {
        label: '发货数量',
        value: autoAllocatedQuantity,
      },
      {
        label: '缺货数量',
        value: shortageQuantity,
        danger: shortageQuantity > 0,
      },
      {
        label: '订货体积',
        value: formatVolume(detail?.totalOrderVolume),
      },
      {
        label: '发货体积',
        value: formatVolume(detail?.totalAllocVolume),
      },
      {
        label: '进货金额',
        value: formatMoney(detail?.totalImportAmount),
        accent: true,
      },
      {
        label: '零售金额',
        value: formatMoney(detail?.totalAmount),
      },
    ]
  }, [detail])

  const shortageSummary = useMemo(() => {
    const items = detail?.items ?? []
    const shortageItems = items.filter((item) => getShortageQuantity(item) > 0)
    const shortageQuantity = shortageItems.reduce((sum, item) => sum + getShortageQuantity(item), 0)
    const autoAllocatedQuantity = items.reduce((sum, item) => sum + Number(item.allocQuantity ?? 0), 0)

    return {
      shortageItems,
      shortageQuantity,
      shortageLineCount: shortageItems.length,
      autoAllocatedQuantity,
    }
  }, [detail])

  const visibleItems = useMemo(() => {
    const items = detail?.items ?? []
    const filteredItems = showShortageOnly
      ? items.filter((item) => getShortageQuantity(item) > 0)
      : items

    return filteredItems.slice().sort((left, right) => {
      if (sortMode === 'itemNumber') {
        return (left.itemNumber || left.productCode || '').localeCompare(
          right.itemNumber || right.productCode || '',
          undefined,
          { sensitivity: 'base' },
        )
      }

      const shortageDiff = getShortageQuantity(right) - getShortageQuantity(left)
      if (shortageDiff !== 0) {
        return shortageDiff
      }

      const allocDiff = Number(left.allocQuantity ?? 0) - Number(right.allocQuantity ?? 0)
      if (allocDiff !== 0) {
        return allocDiff
      }

      return (left.itemNumber || left.productCode || '').localeCompare(
        right.itemNumber || right.productCode || '',
        undefined,
        { sensitivity: 'base' },
      )
    })
  }, [detail, showShortageOnly, sortMode])

  if (loading) {
    return (
      <div className="shop-order-detail-loading">
        <Spin size="large" />
      </div>
    )
  }

  if (!detail) {
    return (
      <div className="shop-order-detail-empty">
        <Empty description="未找到对应订单明细。">
          <Button type="primary" onClick={() => navigate('/shop/orders')}>
            返回订单列表
          </Button>
        </Empty>
      </div>
    )
  }

  const currentStatus = (detail.flowStatus ?? StoreOrderFlowStatus.Submitted) as StoreOrderFlowStatus
  const statusMeta = {
    label: StoreOrderStatusLabelMap[currentStatus] ?? `状态 ${detail.flowStatus ?? '--'}`,
    color: StoreOrderStatusColorMap[currentStatus] ?? 'default',
  }

  return (
    <div className="shop-order-detail-page">
      <div className="shop-order-detail-hero">
        <div className="shop-order-detail-hero-main">
          <Button
            icon={<ArrowLeftOutlined />}
            onClick={() => navigate('/shop/orders')}
            className="shop-order-detail-back"
          >
            返回
          </Button>

          <div className="shop-order-detail-eyebrow">
            <ShoppingOutlined /> 分店订单明细
          </div>

          <div className="shop-order-detail-title-row">
            <div>
              <Title level={2}>{detail.orderNo || detail.orderGUID}</Title>
              <Text type="secondary">
                只读查看订单头信息、状态、数量和商品明细，不提供编辑操作。
              </Text>
            </div>
            <Tag color={statusMeta.color}>{statusMeta.label}</Tag>
          </div>

          <div className="shop-order-detail-meta">
            <div>
              <ShopOutlined />
              <span>{storeName}</span>
            </div>
            <div>
              <ClockCircleOutlined />
              <span>{formatDateTime(detail.orderDate)}</span>
            </div>
            <div>
              <FileTextOutlined />
              <span>{detail.items?.length ?? 0} 条明细</span>
            </div>
          </div>
        </div>
      </div>

      <div className="shop-order-detail-stats">
        {summaryStats.map((item) => (
          <div
            key={item.label}
            className={`shop-order-detail-stat${item.accent ? ' accent' : ''}${item.danger ? ' danger' : ''}`}
          >
            <span>{item.label}</span>
            <strong>{item.value}</strong>
          </div>
        ))}
      </div>

      <div className="shop-order-detail-info-grid">
        <section className="shop-order-detail-panel">
          <div className="shop-order-detail-panel-title">订单备注</div>
          {detail.remarks ? (
            <div className="shop-order-detail-note">
              <FileTextOutlined />
              <p>{detail.remarks}</p>
            </div>
          ) : (
            <Text type="secondary">该订单没有备注。</Text>
          )}
        </section>

        <section className="shop-order-detail-panel">
          <div className="shop-order-detail-panel-title">配送信息</div>
          <div className="shop-order-detail-info-list">
            <div>
              <span>运费</span>
              <strong>{formatMoney(detail.shippingFee)}</strong>
            </div>
            <div>
              <span>分店编码</span>
              <strong>{detail.storeCode || '--'}</strong>
            </div>
            <div>
              <span>地址</span>
              <strong>{detail.storeAddress || '--'}</strong>
            </div>
          </div>
        </section>
      </div>

      <section className="shop-order-lines-panel">
        <div className="shop-order-lines-header">
          <div>
            <div className="shop-order-detail-panel-title">商品明细</div>
            <Text type="secondary">每一行显示订货、发货、缺货和金额信息。</Text>
          </div>
          <div className="shop-order-lines-header-actions">
            <Segmented<LineSortMode>
              value={sortMode}
              options={[
                { label: '缺货优先', value: 'shortage' },
                { label: '按货号', value: 'itemNumber' },
              ]}
              onChange={(value) => setSortMode(value)}
            />
            <Button
              type={showShortageOnly ? 'primary' : 'default'}
              onClick={() => setShowShortageOnly((current) => !current)}
            >
              {showShortageOnly ? '显示全部' : '只看缺货'}
            </Button>
            <div className="shop-order-lines-counter">{visibleItems.length} 条</div>
          </div>
        </div>

        <div className="shop-order-shortage-banner">
          <div className="shop-order-shortage-banner-main">
            <span className="shop-order-shortage-banner-label">缺货汇总</span>
            <strong>{shortageSummary.shortageQuantity}</strong>
          </div>
          <div className="shop-order-shortage-banner-meta">
            <span>{shortageSummary.shortageLineCount} 条明细需要关注</span>
            <span>已发货数量：{shortageSummary.autoAllocatedQuantity}</span>
          </div>
        </div>

        <div className="shop-order-lines-list">
          {visibleItems.map((item) => {
            const lineStatus = getLineStatus(item)
            const shortageQuantity = getShortageQuantity(item)

            return (
              <article
                key={item.detailGUID}
                className={`shop-order-line-card${shortageQuantity > 0 ? ' shortage' : ''}`}
              >
                <div className="shop-order-line-media">
                  <Image
                    src={item.productImage || 'https://via.placeholder.com/120?text=No+Image'}
                    alt={item.productName || item.itemNumber || item.productCode}
                    width={96}
                    height={96}
                    style={{ objectFit: 'contain' }}
                    fallback="https://via.placeholder.com/120?text=No+Image"
                    preview={false}
                  />
                  <div className="shop-order-line-barcode">
                    <BarcodePreview
                      value={item.barcode}
                      align="center"
                      textMaxWidth={120}
                      compactCopy
                    />
                  </div>
                </div>

                <div className="shop-order-line-main">
                  <div className="shop-order-line-head">
                    <div>
                      <Title level={5} className="shop-order-line-title">
                        {item.productName || '未命名商品'}
                      </Title>
                      <Space size={8} wrap>
                        <Tag icon={<TagOutlined />}>{item.itemNumber || item.productCode}</Tag>
                        {item.locationCode ? (
                          <Tag icon={<EnvironmentOutlined />}>{item.locationCode}</Tag>
                        ) : null}
                      </Space>
                    </div>
                    <Space wrap size={8}>
                      {shortageQuantity > 0 ? <Tag color="error">缺货</Tag> : null}
                      <Tag color={lineStatus.color}>{lineStatus.label}</Tag>
                    </Space>
                  </div>

                  <div className="shop-order-line-metrics">
                    <div>
                      <span>订货 / 发货</span>
                      <strong>
                        {item.quantity} / {item.allocQuantity ?? 0}
                      </strong>
                    </div>
                    <div className={shortageQuantity > 0 ? 'shop-order-line-metric-danger' : undefined}>
                      <span>缺货</span>
                      <strong>{shortageQuantity}</strong>
                    </div>
                    <div>
                      <span>进货价</span>
                      <strong>{formatMoney(item.importPrice)}</strong>
                    </div>
                    <div>
                      <span>进货金额</span>
                      <strong>{formatMoney(item.importAmount)}</strong>
                    </div>
                    <div>
                      <span>零售金额</span>
                      <strong>{formatMoney(item.amount)}</strong>
                    </div>
                  </div>

                  <div className="shop-order-line-footer">
                    <div className="shop-order-line-volume">
                      <CheckCircleOutlined />
                      <span>订货体积：{formatVolume(item.orderVolume ?? item.totalVolume)}</span>
                    </div>
                    <div className="shop-order-line-volume">
                      <CheckCircleOutlined />
                      <span>发货体积：{formatVolume(item.allocVolume)}</span>
                    </div>
                    <div className="shop-order-line-volume">
                      <InboxOutlined />
                      <span>已发货数量：{item.allocQuantity ?? 0}</span>
                    </div>
                  </div>
                </div>
              </article>
            )
          })}

          {!visibleItems.length ? (
            <div className="shop-order-lines-empty">
              <Empty description={showShortageOnly ? '当前订单没有缺货明细。' : '当前订单没有商品明细。'} />
            </div>
          ) : null}
        </div>
      </section>
    </div>
  )
}
