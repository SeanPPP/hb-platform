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
import { useTranslation } from 'react-i18next'
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

function formatDateTime(value?: string, locale: string = 'zh-CN') {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString(locale, {
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

function getLineStatus(line: StoreOrderDetailLine, t: (key: string, fb: string) => string) {
  const allocQuantity = line.allocQuantity ?? 0
  if (allocQuantity === 0) {
    return { label: t('shopOrderDetail.pendingShip', '待发货'), color: 'default' as const }
  }

  if (allocQuantity < line.quantity) {
    return { label: t('shopOrderDetail.partialShipped', '部分发货'), color: 'warning' as const }
  }

  return { label: t('shopOrderDetail.shipped', '已发货'), color: 'success' as const }
}

function getShortageQuantity(line: StoreOrderDetailLine) {
  return Math.max((line.quantity ?? 0) - (line.allocQuantity ?? 0), 0)
}

export default function ShopOrderDetailPage() {
  const { t, i18n } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const dateLocale = i18n.resolvedLanguage?.startsWith('zh') ? 'zh-CN' : 'en-US'

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
      return selectedStore?.storeName || t('shopOrderDetail.unknownStore')
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
        label: t('shopOrderDetail.orderQuantity'),
        value: detail?.totalQuantity ?? 0,
      },
      {
        label: t('shopOrderDetail.shipQuantity'),
        value: autoAllocatedQuantity,
      },
      {
        label: t('shopOrderDetail.shortageQuantity'),
        value: shortageQuantity,
        danger: shortageQuantity > 0,
      },
      {
        label: t('shopOrderDetail.orderVolume'),
        value: formatVolume(detail?.totalOrderVolume),
      },
      {
        label: t('shopOrderDetail.shipVolume'),
        value: formatVolume(detail?.totalAllocVolume),
      },
      {
        label: t('shopOrderDetail.purchaseAmount'),
        value: formatMoney(detail?.totalImportAmount),
        accent: true,
      },
      {
        label: t('shopOrderDetail.retailAmount'),
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
        <Empty description={t('shopOrderDetail.notFound')}>
          <Button type="primary" onClick={() => navigate('/shop/orders')}>
            {t('shopOrderDetail.backToOrderList')}
          </Button>
        </Empty>
      </div>
    )
  }

  const currentStatus = (detail.flowStatus ?? StoreOrderFlowStatus.Submitted) as StoreOrderFlowStatus
  const statusMeta = {
    label: StoreOrderStatusLabelMap[currentStatus] ?? t('common.statusN', `状态 ${detail.flowStatus ?? '--'}`),
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
            {t('common.back')}
          </Button>

          <div className="shop-order-detail-eyebrow">
            <ShoppingOutlined /> {t('shopOrderDetail.title')}
          </div>

          <div className="shop-order-detail-title-row">
            <div>
              <Title level={2}>{detail.orderNo || detail.orderGUID}</Title>
              <Text type="secondary">
                {t('shopOrderDetail.description')}
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
              <span>{formatDateTime(detail.orderDate, dateLocale)}</span>
            </div>
            <div>
              <FileTextOutlined />
              <span>{detail.items?.length ?? 0} {t('shopOrderDetail.detailLines')}</span>
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
          <div className="shop-order-detail-panel-title">{t('shopOrderDetail.orderRemark')}</div>
          {detail.remarks ? (
            <div className="shop-order-detail-note">
              <FileTextOutlined />
              <p>{detail.remarks}</p>
            </div>
          ) : (
            <Text type="secondary">{t('shopOrderDetail.noRemark')}</Text>
          )}
        </section>

        <section className="shop-order-detail-panel">
          <div className="shop-order-detail-panel-title">{t('shopOrderDetail.deliveryInfo')}</div>
          <div className="shop-order-detail-info-list">
            <div>
              <span>{t('shopOrderDetail.freight')}</span>
              <strong>{formatMoney(detail.shippingFee)}</strong>
            </div>
            <div>
              <span>{t('shopOrderDetail.storeCode')}</span>
              <strong>{detail.storeCode || '--'}</strong>
            </div>
            <div>
              <span>{t('common.address')}</span>
              <strong>{detail.storeAddress || '--'}</strong>
            </div>
          </div>
        </section>
      </div>

      <section className="shop-order-lines-panel">
        <div className="shop-order-lines-header">
          <div>
            <div className="shop-order-detail-panel-title">{t('shopOrderDetail.productDetail')}</div>
            <Text type="secondary">{t('shopOrderDetail.productDetailTip')}</Text>
          </div>
          <div className="shop-order-lines-header-actions">
            <Segmented<LineSortMode>
              value={sortMode}
              options={[
                { label: t('shopOrderDetail.shortageFirst'), value: 'shortage' },
                { label: t('shopOrderDetail.byItemNo'), value: 'itemNumber' },
              ]}
              onChange={(value) => setSortMode(value)}
            />
            <Button
              type={showShortageOnly ? 'primary' : 'default'}
              onClick={() => setShowShortageOnly((current) => !current)}
            >
              {showShortageOnly ? t('shopOrderDetail.showAll') : t('shopOrderDetail.onlyShortage')}
            </Button>
            <div className="shop-order-lines-counter">{visibleItems.length} {t('shopOrderDetail.lines')}</div>
          </div>
        </div>

        <div className="shop-order-shortage-banner">
          <div className="shop-order-shortage-banner-main">
            <span className="shop-order-shortage-banner-label">{t('shopOrderDetail.shortageSummary')}</span>
            <strong>{shortageSummary.shortageQuantity}</strong>
          </div>
          <div className="shop-order-shortage-banner-meta">
            <span>{shortageSummary.shortageLineCount} {t('shopOrderDetail.linesNeedAttention')}</span>
            <span>{t('shopOrderDetail.shippedQuantity')}: {shortageSummary.autoAllocatedQuantity}</span>
          </div>
        </div>

        <div className="shop-order-lines-list">
          {visibleItems.map((item) => {
            const lineStatus = getLineStatus(item, t)
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
                        {item.productName || t('shopOrderDetail.unnamedProduct')}
                      </Title>
                      <Space size={8} wrap>
                        <Tag icon={<TagOutlined />}>{item.itemNumber || item.productCode}</Tag>
                        {item.locationCode ? (
                          <Tag icon={<EnvironmentOutlined />}>{item.locationCode}</Tag>
                        ) : null}
                      </Space>
                    </div>
                    <Space wrap size={8}>
                      {shortageQuantity > 0 ? <Tag color="error">{t('shopOrderDetail.shortage')}</Tag> : null}
                      <Tag color={lineStatus.color}>{lineStatus.label}</Tag>
                    </Space>
                  </div>

                  <div className="shop-order-line-metrics">
                    <div>
                      <span>{t('shopOrderDetail.orderSlashShip')}</span>
                      <strong>
                        {item.quantity} / {item.allocQuantity ?? 0}
                      </strong>
                    </div>
                    <div className={shortageQuantity > 0 ? 'shop-order-line-metric-danger' : undefined}>
                      <span>{t('shopOrderDetail.shortage')}</span>
                      <strong>{shortageQuantity}</strong>
                    </div>
                    <div>
                      <span>{t('shopOrderDetail.purchasePrice')}</span>
                      <strong>{formatMoney(item.importPrice)}</strong>
                    </div>
                    <div>
                      <span>{t('shopOrderDetail.purchaseAmount')}</span>
                      <strong>{formatMoney(item.importAmount)}</strong>
                    </div>
                    <div>
                      <span>{t('shopOrderDetail.retailAmount')}</span>
                      <strong>{formatMoney(item.amount)}</strong>
                    </div>
                  </div>

                  <div className="shop-order-line-footer">
                    <div className="shop-order-line-volume">
                      <CheckCircleOutlined />
                      <span>{t('shopOrderDetail.orderVolume')}: {formatVolume(item.orderVolume ?? item.totalVolume)}</span>
                    </div>
                    <div className="shop-order-line-volume">
                      <CheckCircleOutlined />
                      <span>{t('shopOrderDetail.shipVolume')}: {formatVolume(item.allocVolume)}</span>
                    </div>
                    <div className="shop-order-line-volume">
                      <InboxOutlined />
                      <span>{t('shopOrderDetail.shippedQuantity')}: {item.allocQuantity ?? 0}</span>
                    </div>
                  </div>
                </div>
              </article>
            )
          })}

          {!visibleItems.length ? (
            <div className="shop-order-lines-empty">
              <Empty description={showShortageOnly ? t('shopOrderDetail.noShortageDetail') : t('shopOrderDetail.noProductDetail')} />
            </div>
          ) : null}
        </div>
      </section>
    </div>
  )
}
