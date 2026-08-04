import {
  CalendarOutlined,
  CheckCircleOutlined,
  FileSearchOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShopOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { Alert, Button, Empty, Input, Pagination, Select, Space, Spin, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import {
  getShopLocalSupplierInvoiceGrid,
  getShopLocalSupplierInvoiceFilterOptions,
} from '../../services/localSupplierInvoiceService'
import { useShopStore } from '../../store/shop'
import type {
  ShopLocalSupplierInvoiceFilterOptionDto,
  ShopLocalSupplierInvoiceListItemDto,
} from '../../types/localSupplierInvoice'
import { RequestError } from '../../utils/request'
import { buildShopLocalSupplierInvoiceGridRequest } from './logic'

const { Search } = Input
const { Text, Title } = Typography
const ALL_STORES_VALUE = '__all__'

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

function formatMoney(value?: number) {
  return `$${Number(value ?? 0).toFixed(2)}`
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

export default function ShopLocalSupplierInvoicesPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const dateLocale = i18n.resolvedLanguage?.startsWith('zh') ? 'zh-CN' : 'en-AU'
  const userStores = useShopStore((state) => state.userStores)
  const selectedStore = useShopStore((state) => state.selectedStore)
  const setSelectedStore = useShopStore((state) => state.setSelectedStore)

  const [storeCode, setStoreCode] = useState<string | null>(selectedStore?.storeCode ?? null)
  const [supplierCode, setSupplierCode] = useState<string | undefined>()
  const [supplierOptions, setSupplierOptions] = useState<ShopLocalSupplierInvoiceFilterOptionDto[]>([])
  const [supplierOptionsLoading, setSupplierOptionsLoading] = useState(false)
  const [supplierLoadError, setSupplierLoadError] = useState(false)
  const [keywordInput, setKeywordInput] = useState('')
  const [productKeyword, setProductKeyword] = useState('')
  const [invoices, setInvoices] = useState<ShopLocalSupplierInvoiceListItemDto[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<'forbidden' | 'failed' | null>(null)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)

  useEffect(() => {
    setStoreCode(selectedStore?.storeCode ?? null)
    setCurrentPage(1)
  }, [selectedStore?.storeCode])

  useEffect(() => {
    const controller = new AbortController()
    setSupplierCode(undefined)
    setSupplierOptions([])
    setSupplierOptionsLoading(true)
    setSupplierLoadError(false)

    void getShopLocalSupplierInvoiceFilterOptions(storeCode ?? undefined, controller.signal)
      .then((result) => {
        setSupplierOptions(result.suppliers)
      })
      .catch((error: unknown) => {
        if ((error as { name?: string } | null)?.name !== 'AbortError') {
          setSupplierLoadError(true)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setSupplierOptionsLoading(false)
        }
      })

    return () => controller.abort()
  }, [storeCode, reloadVersion])

  useEffect(() => {
    let cancelled = false
    const controller = new AbortController()

    const fetchInvoices = async () => {
      setLoading(true)
      setLoadError(null)

      try {
        const request = buildShopLocalSupplierInvoiceGridRequest({
          page: currentPage,
          pageSize,
          storeCode: storeCode ?? undefined,
          supplierCode,
          productKeyword,
        })
        const result = await getShopLocalSupplierInvoiceGrid(request, controller.signal)

        if (!cancelled) {
          setInvoices(result.items ?? [])
          setTotal(result.total ?? 0)
        }
      } catch (error) {
        if (!cancelled) {
          setInvoices([])
          setTotal(0)
          setLoadError(error instanceof RequestError && error.status === 403 ? 'forbidden' : 'failed')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchInvoices()
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [currentPage, pageSize, productKeyword, reloadVersion, storeCode, supplierCode])

  const currentPageAmount = useMemo(
    () => invoices.reduce((sum, invoice) => sum + Number(invoice.totalAmount ?? 0), 0),
    [invoices],
  )
  const currentStoreName = storeCode
    ? userStores.find((store) => store.storeCode === storeCode)?.storeName ?? storeCode
    : t('shopLocalSupplierInvoices.allStores')
  const currentSupplierName = supplierCode
    ? supplierOptions.find((supplier) => supplier.value === supplierCode)?.label ?? supplierCode
    : t('shopLocalSupplierInvoices.allSuppliers')

  const handleStoreChange = (value: string) => {
    const nextStoreCode = value === ALL_STORES_VALUE ? null : value
    const nextStore = nextStoreCode
      ? userStores.find((store) => store.storeCode === nextStoreCode) ?? null
      : null
    setStoreCode(nextStoreCode)
    setSelectedStore(nextStore)
    setCurrentPage(1)
  }

  const handleReset = () => {
    setSupplierCode(undefined)
    setKeywordInput('')
    setProductKeyword('')
    setCurrentPage(1)
    setReloadVersion((current) => current + 1)
  }

  return (
    <div className="shop-orders-page shop-local-invoices-page">
      <div className="shop-orders-hero">
        <div>
          <div className="shop-orders-eyebrow">
            <FileSearchOutlined /> {t('shopLocalSupplierInvoices.eyebrow')}
          </div>
          <Title level={2}>{t('shopLocalSupplierInvoices.title')}</Title>
          <Text type="secondary">{t('shopLocalSupplierInvoices.description')}</Text>
        </div>
        <div className="shop-orders-store-badge">
          <ShopOutlined />
          <span>{selectedStore?.storeName || t('shopLocalSupplierInvoices.currentAccessibleStores')}</span>
        </div>
      </div>

      <div className="shop-orders-stats shop-local-invoice-stats">
        <div className="shop-orders-stat-card">
          <span className="shop-orders-stat-label">{t('shopLocalSupplierInvoices.invoiceCount')}</span>
          <strong>{loadError ? '--' : total}</strong>
        </div>
        <div className="shop-orders-stat-card accent">
          <span className="shop-orders-stat-label">{t('shopLocalSupplierInvoices.currentPageAmount')}</span>
          <strong>{loadError ? '--' : formatMoney(currentPageAmount)}</strong>
        </div>
      </div>

      <div className="shop-orders-toolbar shop-local-invoice-toolbar">
        <div className="shop-local-invoice-filter-field">
          <label htmlFor="shop-local-invoice-store">{t('shopLocalSupplierInvoices.store')}</label>
          <Select
            id="shop-local-invoice-store"
            value={storeCode ?? '__all__'}
            onChange={handleStoreChange}
            options={[
              { value: ALL_STORES_VALUE, label: t('shopLocalSupplierInvoices.allStores') },
              ...userStores.map((store) => ({ value: store.storeCode, label: store.storeName })),
            ]}
          />
        </div>
        <div className="shop-local-invoice-filter-field">
          <label htmlFor="shop-local-invoice-supplier">{t('shopLocalSupplierInvoices.supplier')}</label>
          <Select
            id="shop-local-invoice-supplier"
            value={supplierCode}
            loading={supplierOptionsLoading}
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('shopLocalSupplierInvoices.allSuppliers')}
            onChange={(value) => {
              setSupplierCode(value)
              setCurrentPage(1)
            }}
            options={supplierOptions}
          />
        </div>
        <div className="shop-local-invoice-filter-field product-keyword">
          <label htmlFor="shop-local-invoice-product">{t('shopLocalSupplierInvoices.productKeyword')}</label>
          <Search
            id="shop-local-invoice-product"
            value={keywordInput}
            allowClear
            enterButton={<SearchOutlined aria-label={t('shopLocalSupplierInvoices.search')} />}
            placeholder={t('shopLocalSupplierInvoices.productKeywordPlaceholder')}
            onChange={(event) => setKeywordInput(event.target.value)}
            onSearch={(value) => {
              setProductKeyword(value.trim())
              setCurrentPage(1)
            }}
          />
        </div>
        <Button icon={<ReloadOutlined />} onClick={handleReset} className="shop-local-invoice-reset">
          {t('common.reset')}
        </Button>
      </div>

      {supplierLoadError ? (
        <Alert
          type="warning"
          showIcon
          message={t('shopLocalSupplierInvoices.supplierLoadFailed')}
          action={<Button size="small" onClick={() => setReloadVersion((current) => current + 1)}>{t('common.retry')}</Button>}
        />
      ) : null}

      <div className="shop-orders-filter-note">
        {t('shopLocalSupplierInvoices.filterSummary', {
          store: currentStoreName,
          supplier: currentSupplierName,
        })}
      </div>

      {loading ? (
        <div className="shop-orders-loading"><Spin size="large" /></div>
      ) : loadError ? (
        <div className="shop-orders-empty">
          <Empty
            description={t(
              loadError === 'forbidden'
                ? 'shopLocalSupplierInvoices.forbidden'
                : 'shopLocalSupplierInvoices.loadFailed',
            )}
          >
            <Button type="primary" icon={<ReloadOutlined />} onClick={() => setReloadVersion((current) => current + 1)}>
              {t('common.retry')}
            </Button>
          </Empty>
        </div>
      ) : invoices.length ? (
        <>
          <div className="shop-orders-grid shop-local-invoice-grid">
            {invoices.map((invoice) => {
              const flowStatus = getStatusMeta(invoice.flowStatus, FLOW_STATUS_META)
              const inboundStatus = getStatusMeta(invoice.inboundStatus, INBOUND_STATUS_META)

              return (
                <article key={invoice.invoiceGUID} className="shop-order-card shop-local-invoice-card">
                  <div className="shop-order-card-top">
                    <div className="shop-order-card-headline">
                      <div className="shop-order-card-label">{t('shopLocalSupplierInvoices.invoiceNo')}</div>
                      <Title level={4} className="shop-order-card-title">
                        {invoice.invoiceNo || t('shopLocalSupplierInvoices.unknownInvoice')}
                      </Title>
                    </div>
                    <Space wrap size={[4, 6]} className="shop-local-invoice-statuses">
                      <Tag color={flowStatus.color}>{t(flowStatus.key, flowStatus.fallback)}</Tag>
                      <Tag color={inboundStatus.color}>{t(inboundStatus.key, inboundStatus.fallback)}</Tag>
                    </Space>
                  </div>

                  <div className="shop-order-card-meta">
                    <div><ShopOutlined /><span>{invoice.storeName || invoice.storeCode || t('shopLocalSupplierInvoices.unknownStore')}</span></div>
                    <div><TeamOutlined /><span>{invoice.supplierName || invoice.supplierCode || t('shopLocalSupplierInvoices.unknownSupplier')}</span></div>
                    <div><CalendarOutlined /><span>{t('shopLocalSupplierInvoices.orderDate')}: {formatDate(invoice.orderDate, dateLocale)}</span></div>
                    <div><CalendarOutlined /><span>{t('shopLocalSupplierInvoices.inboundDate')}: {formatDate(invoice.inboundDate, dateLocale)}</span></div>
                  </div>

                  <div className="shop-order-card-metrics">
                    <div className="shop-order-metric amount">
                      <span>{t('shopLocalSupplierInvoices.totalAmount')}</span>
                      <strong>{formatMoney(invoice.totalAmount)}</strong>
                    </div>
                    <div className="shop-order-metric">
                      <span>{t('shopLocalSupplierInvoices.receivedAmount')}</span>
                      <strong>{formatMoney(invoice.receivedTotalAmount)}</strong>
                    </div>
                  </div>

                  <button
                    type="button"
                    className="shop-order-card-footer shop-local-invoice-card-footer"
                    onClick={() => navigate(`/shop/local-supplier-invoices/${encodeURIComponent(invoice.invoiceGUID)}`)}
                  >
                    <Space size={6}>
                      <CheckCircleOutlined />
                      <Text>{t('shopLocalSupplierInvoices.viewDetail')}</Text>
                    </Space>
                  </button>
                </article>
              )
            })}
          </div>

          <div className="shop-orders-pagination">
            <Pagination
              current={currentPage}
              pageSize={pageSize}
              total={total}
              showSizeChanger
              pageSizeOptions={[20, 50, 100]}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size !== pageSize) setPageSize(size)
              }}
            />
          </div>
        </>
      ) : (
        <div className="shop-orders-empty">
          <Empty description={t('shopLocalSupplierInvoices.noMatchingInvoices')} />
        </div>
      )}
    </div>
  )
}
