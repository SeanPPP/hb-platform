import { Button, Empty, Modal, Radio, Spin, Tag, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getStoreOrderProductActivityHistory } from '../../../services/storeOrderService'
import {
  StoreOrderFlowStatus,
  StoreOrderStatusColorMap,
} from '../../../types/storeOrder'
import type {
  StoreOrderProductActivityFilter,
  StoreOrderProductActivityHistoryItem,
  StoreOrderProductItem,
} from '../../../types/storeOrder'
import {
  createProductActivityHistoryRequestCoordinator,
  getProductActivityHistoryRequestIdentity,
  runProductActivityHistoryRequest,
} from '../productActivityHistoryRequestCoordinator'
import { formatOrderHistoryQuantity } from '../orderHistoryQuantity'
import {
  buildProductActivityTableRows,
  type ProductActivityTableRow,
} from '../productActivityHistoryRows'
import { MeasuredTable } from '../../../components/MeasuredTable'

const { Text } = Typography

// 商品订货 · 发货 · 销售合并记录分页固定 30 条，由后端分页返回。
const ACTIVITY_PAGE_SIZE = 30

interface ProductActivityHistoryModalProps {
  open: boolean
  product: StoreOrderProductItem | null
  storeCode?: string | null
  storeName?: string | null
  onClose: () => void
}

interface ActivitySummary {
  storeCode: string
  productCode: string
  lastArrivalDate: string | null
  latestOrderQuantity: number
  latestAllocQuantity: number
  totalSalesQuantity: number
}

function formatCalendarDate(value?: string | null): string {
  if (!value) {
    return '—'
  }

  const normalized = value.trim()
  const dateOnly = normalized.match(/^\d{4}-\d{2}-\d{2}/)?.[0]
  if (dateOnly) {
    return dateOnly
  }

  const parsed = new Date(normalized)
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleDateString()
}

function formatActivityInterval(
  startDate?: string | null,
  endDate?: string | null,
): string {
  return `${formatCalendarDate(startDate)} ~ ${formatCalendarDate(endDate)}`
}

function formatAudPrice(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—'
  }

  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
  }).format(value)
}

// 复用现有 storeOrders 状态文案，未知状态回退到通用“状态 N”。
const ORDER_STATUS_I18N_KEYS: Partial<Record<StoreOrderFlowStatus, string>> = {
  [StoreOrderFlowStatus.ShoppingCart]: 'storeOrders.statusShoppingCart',
  [StoreOrderFlowStatus.Submitted]: 'storeOrders.statusSubmitted',
  [StoreOrderFlowStatus.Completed]: 'storeOrders.statusCompleted',
  [StoreOrderFlowStatus.Picking]: 'storeOrders.statusPicking',
}

export default function ProductActivityHistoryModal({
  open,
  product,
  storeCode,
  storeName,
  onClose,
}: ProductActivityHistoryModalProps) {
  const { t } = useTranslation()
  const [items, setItems] = useState<StoreOrderProductActivityHistoryItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [filter, setFilter] = useState<StoreOrderProductActivityFilter>('all')
  const [expandedSalesPeriodKeys, setExpandedSalesPeriodKeys] = useState<string[]>([])
  const [summary, setSummary] = useState<ActivitySummary | null>(null)
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const [reloadToken, setReloadToken] = useState(0)
  const requestCoordinatorRef = useRef(createProductActivityHistoryRequestCoordinator())

  const productCode = product?.productCode ?? null
  const currentStoreCode = storeCode ?? null

  // 摘要必须绑定门店 + 商品，只渲染当前实体摘要；实体变化时同步重置分页、筛选与状态，
  // 防止旧摘要与新商品/门店同屏，或新请求失败后残留旧内容。
  const activityEntityKey = currentStoreCode && productCode ? `${currentStoreCode}::${productCode}` : null
  const [lastActivityEntityKey, setLastActivityEntityKey] = useState(activityEntityKey)
  if (lastActivityEntityKey !== activityEntityKey) {
    setLastActivityEntityKey(activityEntityKey)
    setPage(1)
    setFilter('all')
    setExpandedSalesPeriodKeys([])
    setItems([])
    setTotal(0)
    setSummary(null)
    setStatus('idle')
  }

  const requestIdentity = getProductActivityHistoryRequestIdentity({
    open,
    storeCode,
    productCode,
    page,
    recordType: filter,
    retryVersion: reloadToken,
  })

  const currentSummary =
    summary && summary.storeCode === currentStoreCode && summary.productCode === productCode
      ? summary
      : null

  // 在浏览器绘制前切换请求身份，避免 props 已换而旧 Promise 先回填的窗口。
  useLayoutEffect(() => {
    const coordinator = requestCoordinatorRef.current
    coordinator.activate(requestIdentity)

    return () => {
      coordinator.invalidate(requestIdentity)
    }
  }, [requestIdentity])

  // 关闭时立即清空并丢弃所有未完成请求，避免旧响应回填。
  useEffect(() => {
    if (!open) {
      setItems([])
      setTotal(0)
      setPage(1)
      setFilter('all')
      setExpandedSalesPeriodKeys([])
      setSummary(null)
      setStatus('idle')
    }
  }, [open])

  useEffect(() => {
    if (!open || !storeCode || !productCode || !requestIdentity) {
      return
    }

    const controller = new AbortController()

    setStatus('loading')

    void runProductActivityHistoryRequest({
      coordinator: requestCoordinatorRef.current,
      identity: requestIdentity,
      signal: controller.signal,
      request: () =>
        getStoreOrderProductActivityHistory(
          {
            storeCode,
            productCode,
            pageNumber: page,
            pageSize: ACTIVITY_PAGE_SIZE,
            recordType: filter,
          },
          controller.signal,
        ),
      onSuccess: (result) => {
        setItems(result.items)
        setTotal(result.total)
        setSummary({
          storeCode,
          productCode,
          lastArrivalDate: result.lastArrivalDate ?? null,
          latestOrderQuantity: result.latestOrderQuantity ?? 0,
          latestAllocQuantity: result.latestAllocQuantity ?? 0,
          totalSalesQuantity: result.totalSalesQuantity ?? 0,
        })
        setStatus('ready')
      },
      onError: () => {
        setStatus('error')
      },
    })

    // 新请求、关闭、切实体或卸载时取消旧 HTTP，避免旧响应回填当前状态。
    return () => {
      controller.abort()
    }
  }, [open, storeCode, productCode, page, filter, requestIdentity])

  const handleFilterChange = (nextFilter: StoreOrderProductActivityFilter) => {
    setFilter(nextFilter)
    // 切换筛选必须回到第一页，旧页码由 identity 守卫丢弃。
    setPage(1)
    setExpandedSalesPeriodKeys([])
  }

  const tableRows = useMemo(() => buildProductActivityTableRows(items), [items])

  const getFlowStatusMeta = (value?: number) => {
    if (typeof value !== 'number') {
      return { label: '—', color: 'default' as const }
    }

    const key = ORDER_STATUS_I18N_KEYS[value as StoreOrderFlowStatus]
    if (!key) {
      return {
        label: t('common.statusN', `Status ${value}`, { n: value }),
        color: 'default' as const,
      }
    }

    return {
      label: t(key),
      color: StoreOrderStatusColorMap[value as StoreOrderFlowStatus] ?? 'default',
    }
  }

  const renderOrderValue = (
    value: number | undefined,
    record: ProductActivityTableRow,
    className: string,
  ) => {
    if (record.recordType !== 'order') {
      return '—'
    }

    return <span className={className}>{formatOrderHistoryQuantity(value)}</span>
  }

  const getActivityRowClassName = (record: ProductActivityTableRow) =>
    record.recordType === 'salesSubtotal' ? 'shop-product-activity-subtotal-row' : ''

  const columns: TableColumnsType<ProductActivityTableRow> = [
    {
      title: t('shop.productActivityHistory.date'),
      dataIndex: 'recordDate',
      key: 'recordDate',
      render: (value: string | undefined, record) =>
        record.recordType === 'salesSubtotal'
          ? formatActivityInterval(record.periodStartDate, record.periodEndDate)
          : formatCalendarDate(value),
    },
    {
      title: t('shop.productActivityHistory.type'),
      dataIndex: 'recordType',
      key: 'recordType',
      render: (value: StoreOrderProductActivityHistoryItem['recordType'], record) =>
        record.isSalesContinuation ? (
          <Tag color="purple">{t('shop.productActivityHistory.typeSalesDetails')}</Tag>
        ) : value === 'salesSubtotal' ? (
          <Tag color="purple">{t('shop.productActivityHistory.typeSubtotal')}</Tag>
        ) : value === 'sales' ? (
          <Tag color="blue">{t('shop.productActivityHistory.typeSales')}</Tag>
        ) : (
          <Tag color="orange">{t('shop.productActivityHistory.typeOrder')}</Tag>
        ),
    },
    {
      title: t('shop.productActivityHistory.orderNo'),
      dataIndex: 'orderNo',
      key: 'orderNo',
      render: (value: string | undefined, record) =>
        record.recordType === 'order' ? value || '—' : '—',
    },
    {
      title: t('shop.productActivityHistory.orderQuantity'),
      dataIndex: 'quantity',
      key: 'quantity',
      align: 'right',
      render: (value: number | undefined, record) =>
        renderOrderValue(value, record, 'shop-product-activity-qty-order'),
    },
    {
      title: t('shop.productActivityHistory.shipQuantity'),
      dataIndex: 'allocQuantity',
      key: 'allocQuantity',
      align: 'right',
      render: (value: number | undefined, record) =>
        renderOrderValue(value, record, 'shop-product-activity-qty-ship'),
    },
    {
      title: t('shop.productActivityHistory.outboundDate'),
      dataIndex: 'outboundDate',
      key: 'outboundDate',
      render: (value: string | undefined, record) =>
        record.recordType === 'order' ? formatCalendarDate(value) : '—',
    },
    {
      title: t('shop.productActivityHistory.salesQuantity'),
      dataIndex: 'salesQuantity',
      key: 'salesQuantity',
      align: 'right',
      render: (value: number | undefined, record) =>
        record.recordType === 'salesSubtotal' && !record.isSalesContinuation ? (
          <span className="shop-product-activity-qty-sales">
            {formatOrderHistoryQuantity(value ?? 0)}
          </span>
        ) : record.recordType === 'sales' ? (
          <span className="shop-product-activity-qty-sales">
            {formatOrderHistoryQuantity(value)}
          </span>
        ) : (
          '—'
        ),
    },
    {
      title: t('shop.productActivityHistory.averagePrice'),
      dataIndex: 'averagePrice',
      key: 'averagePrice',
      align: 'right',
      render: (value: number | null | undefined, record) =>
        record.recordType === 'salesSubtotal' && !record.isSalesContinuation
          ? value == null || (record.salesQuantity ?? 0) === 0
            ? '—'
            : formatAudPrice(value)
          : record.recordType === 'sales'
            ? formatAudPrice(value)
            : '—',
    },
    {
      title: t('shop.productActivityHistory.status'),
      dataIndex: 'flowStatus',
      key: 'flowStatus',
      render: (value: number | undefined, record) => {
        if (record.recordType !== 'order') {
          return '—'
        }

        const meta = getFlowStatusMeta(value)
        return <Tag color={meta.color}>{meta.label}</Tag>
      },
    },
  ]

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      width={1120}
      className="shop-product-activity-modal"
      title={t('shop.productActivityHistory.title')}
    >
      {product ? (
        <div className="shop-product-activity-modal-body">
          <div className="shop-product-activity-summary">
            <div className="shop-product-activity-meta">
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.productName')}: </Text>
                <Text strong>{product.productName || '—'}</Text>
              </span>
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.itemNumber')}: </Text>
                <Text strong>{product.itemNumber || '—'}</Text>
              </span>
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.store')}: </Text>
                <Text>{storeName || '—'}</Text>
              </span>
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.lastArrival')}: </Text>
                <Text>{formatCalendarDate(currentSummary?.lastArrivalDate)}</Text>
              </span>
            </div>
            <div className="shop-product-activity-compact">
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.latestOrder')}: </Text>
                <Text className="shop-product-activity-qty-order">
                  {formatOrderHistoryQuantity(currentSummary?.latestOrderQuantity)}
                </Text>
              </span>
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.latestShipment')}: </Text>
                <Text className="shop-product-activity-qty-ship">
                  {formatOrderHistoryQuantity(currentSummary?.latestAllocQuantity)}
                </Text>
              </span>
              <span>
                <Text type="secondary">{t('shop.productActivityHistory.salesSinceArrival')}: </Text>
                <Text className="shop-product-activity-qty-sales">
                  {formatOrderHistoryQuantity(currentSummary?.totalSalesQuantity)}
                </Text>
              </span>
            </div>
            <Text type="secondary" className="shop-product-activity-note">
              {t('shop.productActivityHistory.notRealtime')}
            </Text>
          </div>

          <div className="shop-product-activity-details-content">
            <div className="shop-product-activity-filter">
              <Text type="secondary">{t('shop.productActivityHistory.filter')}: </Text>
              <Radio.Group
                size="small"
                optionType="button"
                aria-label={t('shop.productActivityHistory.filter')}
                value={filter}
                onChange={(event) =>
                  handleFilterChange(event.target.value as StoreOrderProductActivityFilter)
                }
              >
                <Radio.Button value="all">
                  {t('shop.productActivityHistory.filterAll')}
                </Radio.Button>
                <Radio.Button value="order">
                  {t('shop.productActivityHistory.filterOrder')}
                </Radio.Button>
                <Radio.Button value="sales">
                  {t('shop.productActivityHistory.filterSales')}
                </Radio.Button>
              </Radio.Group>
            </div>

            {status === 'loading' ? (
              <div className="shop-product-activity-state">
                <Spin />
              </div>
            ) : status === 'error' ? (
              <div className="shop-product-activity-state">
                <Text type="danger">{t('shop.productActivityHistory.loadFailed')}</Text>
                <Button size="small" onClick={() => setReloadToken((value) => value + 1)}>
                  {t('shop.productActivityHistory.retry')}
                </Button>
              </div>
            ) : (
              <MeasuredTable metricId="shop-home.product-activity-history-modal.table-1"
                size="small"
                className="shop-product-activity-table"
                columns={columns}
                dataSource={tableRows}
                rowKey={(record) => record.tableKey}
                rowClassName={getActivityRowClassName}
                expandable={{
                  expandedRowKeys: expandedSalesPeriodKeys,
                  onExpandedRowsChange: (keys) =>
                    setExpandedSalesPeriodKeys(keys.map((key) => String(key))),
                  rowExpandable: (record) => (record.children?.length ?? 0) > 0,
                  childrenColumnName: 'children',
                  indentSize: 12,
                }}
                pagination={{
                  current: page,
                  pageSize: ACTIVITY_PAGE_SIZE,
                  total,
                  showSizeChanger: false,
                  onChange: (nextPage) => {
                    setExpandedSalesPeriodKeys([])
                    setPage(nextPage)
                  },
                }}
                locale={{
                  emptyText: (
                    <Empty
                      image={Empty.PRESENTED_IMAGE_SIMPLE}
                      description={t('shop.productActivityHistory.empty')}
                    />
                  ),
                }}
                scroll={{ x: 1040 }}
              />
            )}
          </div>
        </div>
      ) : null}
    </Modal>
  )
}
