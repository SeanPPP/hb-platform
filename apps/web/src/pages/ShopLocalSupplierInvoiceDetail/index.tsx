import {
  ArrowLeftOutlined,
  BarcodeOutlined,
  FileTextOutlined,
  InboxOutlined,
  ReloadOutlined,
  ShopOutlined,
  TagsOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { Button, Empty, Image, Pagination, Result, Space, Spin, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import {
  getShopLocalSupplierInvoice,
  getShopLocalSupplierInvoiceDetailsGrid,
} from '../../services/localSupplierInvoiceService'
import type {
  ShopLocalSupplierInvoiceDetailsPageSize,
  ShopLocalSupplierInvoiceDto,
  ShopLocalSupplierInvoiceItemDto,
} from '../../types/localSupplierInvoice'
import { RequestError } from '../../utils/request'

const { Text, Title } = Typography

type DetailLoadError = 'forbidden' | 'not-found' | 'failed' | null
const PRODUCT_IMAGE_FALLBACK = `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(`
  <svg xmlns="http://www.w3.org/2000/svg" width="120" height="120" viewBox="0 0 120 120">
    <rect width="120" height="120" rx="14" fill="#f1f5f9"/>
    <path d="M34 81l17-19 12 12 9-10 14 17H34z" fill="#cbd5e1"/>
    <circle cx="48" cy="45" r="7" fill="#94a3b8"/>
  </svg>
`)}`

const FLOW_STATUS_META: Record<number, { key: string; fallback: string; color: string }> = {
  0: { key: 'posAdmin.invoices.draft', fallback: '草稿', color: 'default' },
  1: { key: 'posAdmin.invoices.submitted', fallback: '已提交', color: 'processing' },
  2: { key: 'posAdmin.invoices.approved', fallback: '已审核', color: 'cyan' },
  3: { key: 'posAdmin.invoices.pushed', fallback: '已推送', color: 'success' },
}

const INBOUND_STATUS_META: Record<number, { key: string; fallback: string; color: string }> = {
  0: { key: 'posAdmin.invoices.notInbound', fallback: '未入库', color: 'default' },
  1: { key: 'posAdmin.invoices.partialInbound', fallback: '部分入库', color: 'warning' },
  2: { key: 'posAdmin.invoices.inbounded', fallback: '已入库', color: 'success' },
}

function getLoadError(errors: unknown[]): Exclude<DetailLoadError, null> {
  if (errors.some((error) => error instanceof RequestError && error.status === 403)) {
    return 'forbidden'
  }
  if (errors.some((error) => error instanceof RequestError && error.status === 404)) {
    return 'not-found'
  }
  return 'failed'
}

function getStatusMeta(
  status: number | undefined,
  definitions: Record<number, { key: string; fallback: string; color: string }>,
) {
  return status === undefined
    ? { key: 'shop.statusUnknown', fallback: '状态未知', color: 'default' }
    : definitions[status] ?? {
        key: 'shop.statusUnknown',
        fallback: '状态未知',
        color: 'default',
      }
}

function formatMoney(value?: number) {
  return `$${Number(value ?? 0).toFixed(2)}`
}

function formatQuantity(value?: number) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 3 })
}

function formatDate(value: string | undefined, locale: string) {
  if (!value) return '--'

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  return date.toLocaleDateString(locale, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  })
}

function getLineAmount(item: ShopLocalSupplierInvoiceItemDto) {
  return item.amount ?? Number(item.quantity ?? 0) * Number(item.purchasePrice ?? 0)
}

export default function ShopLocalSupplierInvoiceDetailPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { invoiceGuid } = useParams<{ invoiceGuid: string }>()
  const dateLocale = i18n.resolvedLanguage?.startsWith('zh') ? 'zh-CN' : 'en-AU'

  const [invoice, setInvoice] = useState<ShopLocalSupplierInvoiceDto | null>(null)
  const [items, setItems] = useState<ShopLocalSupplierInvoiceItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState<ShopLocalSupplierInvoiceDetailsPageSize>(50)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<DetailLoadError>(null)
  const [reloadVersion, setReloadVersion] = useState(0)

  useEffect(() => {
    if (!invoiceGuid) {
      setInvoice(null)
      setItems([])
      setTotal(0)
      setLoadError('not-found')
      setLoading(false)
      return
    }

    let cancelled = false
    const controller = new AbortController()

    const fetchDetail = async () => {
      setLoading(true)
      setLoadError(null)

      const [invoiceResult, itemsResult] = await Promise.allSettled([
        getShopLocalSupplierInvoice(invoiceGuid, controller.signal),
        getShopLocalSupplierInvoiceDetailsGrid(invoiceGuid, { page: currentPage, pageSize }, controller.signal),
      ])

      if (cancelled) return

      const errors = [invoiceResult, itemsResult]
        .filter((result): result is PromiseRejectedResult => result.status === 'rejected')
        .map((result) => result.reason)

      if (errors.length) {
        setInvoice(null)
        setItems([])
        setTotal(0)
        setLoadError(getLoadError(errors))
        setLoading(false)
        return
      }

      if (invoiceResult.status === 'fulfilled' && itemsResult.status === 'fulfilled') {
        setInvoice(invoiceResult.value)
        setItems(itemsResult.value.items)
        setTotal(itemsResult.value.total)
      }
      setLoading(false)
    }

    void fetchDetail()
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [currentPage, invoiceGuid, pageSize, reloadVersion])

  if (loading) {
    return <div className="shop-order-detail-loading"><Spin size="large" /></div>
  }

  if (loadError) {
    const status = loadError === 'forbidden' ? '403' : loadError === 'not-found' ? '404' : 'error'
    const messageKey = loadError === 'forbidden'
      ? 'shopLocalSupplierInvoiceDetail.forbidden'
      : loadError === 'not-found'
        ? 'shopLocalSupplierInvoiceDetail.notFound'
        : 'shopLocalSupplierInvoiceDetail.loadFailed'

    return (
      <div className="shop-order-detail-empty shop-local-invoice-detail-error">
        <Result
          status={status}
          title={t(messageKey)}
          extra={(
            <Space wrap>
              <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/shop/local-supplier-invoices')}>
                {t('shopLocalSupplierInvoiceDetail.backToList')}
              </Button>
              {loadError === 'failed' ? (
                <Button type="primary" icon={<ReloadOutlined />} onClick={() => setReloadVersion((current) => current + 1)}>
                  {t('common.retry')}
                </Button>
              ) : null}
            </Space>
          )}
        />
      </div>
    )
  }

  if (!invoice) return null

  const flowStatus = getStatusMeta(invoice.flowStatus, FLOW_STATUS_META)
  const inboundStatus = getStatusMeta(invoice.inboundStatus, INBOUND_STATUS_META)

  return (
    <div className="shop-order-detail-page shop-local-invoice-detail-page">
      <div className="shop-order-detail-hero">
        <div className="shop-order-detail-hero-main">
          <Button
            icon={<ArrowLeftOutlined />}
            onClick={() => navigate('/shop/local-supplier-invoices')}
            className="shop-order-detail-back"
          >
            {t('shopLocalSupplierInvoiceDetail.backToList')}
          </Button>

          <div className="shop-order-detail-eyebrow">
            <FileTextOutlined /> {t('shopLocalSupplierInvoiceDetail.title')}
          </div>

          <div className="shop-order-detail-title-row">
            <div>
              <Title level={2}>{invoice.invoiceNo || invoice.invoiceGUID}</Title>
              <Text type="secondary">{t('shopLocalSupplierInvoiceDetail.description')}</Text>
            </div>
            <Space wrap>
              <Tag color={flowStatus.color}>{t(flowStatus.key, flowStatus.fallback)}</Tag>
              <Tag color={inboundStatus.color}>{t(inboundStatus.key, inboundStatus.fallback)}</Tag>
            </Space>
          </div>

          <div className="shop-order-detail-meta">
            <div><ShopOutlined /><span>{invoice.storeName || invoice.storeCode || '--'}</span></div>
            <div><TeamOutlined /><span>{invoice.supplierName || invoice.supplierCode || '--'}</span></div>
            <div><FileTextOutlined /><span>{total} {t('shopLocalSupplierInvoiceDetail.detailLines')}</span></div>
          </div>
        </div>
      </div>

      <div className="shop-order-detail-stats shop-local-invoice-detail-stats">
        <div className="shop-order-detail-stat accent">
          <span>{t('shopLocalSupplierInvoiceDetail.totalAmount')}</span>
          <strong>{formatMoney(invoice.totalAmount)}</strong>
        </div>
        <div className="shop-order-detail-stat">
          <span>{t('shopLocalSupplierInvoiceDetail.receivedAmount')}</span>
          <strong>{formatMoney(invoice.receivedTotalAmount)}</strong>
        </div>
        <div className="shop-order-detail-stat">
          <span>{t('shopLocalSupplierInvoiceDetail.orderDate')}</span>
          <strong>{formatDate(invoice.orderDate, dateLocale)}</strong>
        </div>
        <div className="shop-order-detail-stat">
          <span>{t('shopLocalSupplierInvoiceDetail.inboundDate')}</span>
          <strong>{formatDate(invoice.inboundDate, dateLocale)}</strong>
        </div>
      </div>

      <div className="shop-order-detail-info-grid shop-local-invoice-detail-info-grid">
        <section className="shop-order-detail-panel">
          <div className="shop-order-detail-panel-title">{t('shopLocalSupplierInvoiceDetail.storeInfo')}</div>
          <div className="shop-order-detail-info-list">
            <div><span>{t('shopLocalSupplierInvoiceDetail.storeInfo')}</span><strong>{invoice.storeName || '--'}</strong></div>
            <div><span>{t('shopOrderDetail.storeCode')}</span><strong>{invoice.storeCode || '--'}</strong></div>
            <div><span>{t('shopLocalSupplierInvoiceDetail.supplierInfo')}</span><strong>{invoice.supplierName || invoice.supplierCode || '--'}</strong></div>
          </div>
        </section>
      </div>

      <section className="shop-order-lines-panel shop-local-invoice-lines-panel">
        <div className="shop-order-lines-header">
          <div>
            <div className="shop-order-detail-panel-title">{t('shopLocalSupplierInvoiceDetail.productDetail')}</div>
            <Text type="secondary">{t('shopLocalSupplierInvoiceDetail.productDetailTip')}</Text>
          </div>
          <div className="shop-order-lines-counter">{total} {t('shopLocalSupplierInvoiceDetail.detailLines')}</div>
        </div>

        {items.length ? (
          <div className="shop-order-lines-list">
            {items.map((item) => (
              <article key={item.detailGUID} className="shop-order-line-card shop-local-invoice-line-card">
                <div className="shop-order-line-media">
                  <Image
                    src={item.productImage || PRODUCT_IMAGE_FALLBACK}
                    fallback={PRODUCT_IMAGE_FALLBACK}
                    alt={item.productName || item.itemNumber || item.productCode}
                    width={96}
                    height={96}
                    style={{ objectFit: 'contain' }}
                    preview={false}
                  />
                </div>

                <div className="shop-order-line-main">
                  <div className="shop-order-line-head">
                    <div>
                      <Title level={5} className="shop-order-line-title">
                        {item.productName || t('shopLocalSupplierInvoiceDetail.unnamedProduct')}
                      </Title>
                      <Space size={[6, 8]} wrap className="shop-local-invoice-line-identifiers">
                        <Tag icon={<TagsOutlined />}>{t('shopLocalSupplierInvoiceDetail.itemNumber')}: {item.itemNumber || item.productCode || '--'}</Tag>
                        <Tag icon={<BarcodeOutlined />}>{t('shopLocalSupplierInvoiceDetail.barcode')}: {item.barcode || '--'}</Tag>
                      </Space>
                    </div>
                  </div>

                  <div className="shop-local-invoice-line-attributes">
                    <div><span>{t('shopLocalSupplierInvoiceDetail.specification')}</span><strong>{item.specification || '--'}</strong></div>
                    <div><span>{t('shopLocalSupplierInvoiceDetail.unit')}</span><strong>{item.unit || '--'}</strong></div>
                  </div>

                  <div className="shop-order-line-metrics shop-local-invoice-line-metrics">
                    <div><span>{t('shopLocalSupplierInvoiceDetail.quantity')}</span><strong>{formatQuantity(item.quantity)}</strong></div>
                    <div><span>{t('shopLocalSupplierInvoiceDetail.purchasePrice')}</span><strong>{formatMoney(item.purchasePrice)}</strong></div>
                    {typeof item.lastPurchasePrice === 'number' ? (
                      <div><span>{t('shopLocalSupplierInvoiceDetail.lastPurchasePrice')}</span><strong>{formatMoney(item.lastPurchasePrice)}</strong></div>
                    ) : null}
                    <div><span>{t('shopLocalSupplierInvoiceDetail.retailPrice')}</span><strong>{formatMoney(item.retailPrice)}</strong></div>
                    {typeof item.newAutoRetailPrice === 'number' ? (
                      <div><span>{t('shopLocalSupplierInvoiceDetail.newAutoRetailPrice')}</span><strong>{formatMoney(item.newAutoRetailPrice)}</strong></div>
                    ) : null}
                    <div className="shop-local-invoice-line-amount"><span>{t('shopLocalSupplierInvoiceDetail.lineAmount')}</span><strong>{formatMoney(getLineAmount(item))}</strong></div>
                  </div>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <div className="shop-order-lines-empty">
            <Empty image={<InboxOutlined />} description={t('shopLocalSupplierInvoiceDetail.noProductDetail')} />
          </div>
        )}

        {total > 0 ? (
          <div className="shop-orders-pagination shop-local-invoice-detail-pagination">
            <Pagination
              current={currentPage}
              pageSize={pageSize}
              total={total}
              showSizeChanger
              pageSizeOptions={[50, 100, 200]}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size !== pageSize) setPageSize(size as ShopLocalSupplierInvoiceDetailsPageSize)
              }}
            />
          </div>
        ) : null}
      </section>
    </div>
  )
}
