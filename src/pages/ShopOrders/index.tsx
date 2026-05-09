import {
  CheckCircleOutlined,
  ClockCircleOutlined,
  HistoryOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShopOutlined,
} from '@ant-design/icons'
import { Button, DatePicker, Empty, Input, Pagination, Segmented, Space, Spin, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStoreOrderList } from '../../services/storeOrderService'
import { useShopStore } from '../../store/shop'
import { StoreOrderFlowStatus, type StoreOrderListItem } from '../../types/storeOrder'

const { Text, Title } = Typography
const { Search } = Input
const { RangePicker } = DatePicker

type StatusFilter = 'all' | 'active' | 'completed'
type DateRangeValue = [Dayjs, Dayjs]
type QuickRangeFilter = 'today' | 'week' | 'month' | 'custom'

const statusLabelMap: Record<number, string> = {
  [StoreOrderFlowStatus.Submitted]: 'Submitted',
  [StoreOrderFlowStatus.Completed]: 'Completed',
  [StoreOrderFlowStatus.Picking]: 'Picking',
}

const statusColorMap: Record<number, string> = {
  [StoreOrderFlowStatus.Submitted]: 'processing',
  [StoreOrderFlowStatus.Completed]: 'success',
  [StoreOrderFlowStatus.Picking]: 'warning',
}

const statusQueryMap: Record<StatusFilter, StoreOrderFlowStatus[]> = {
  all: [
    StoreOrderFlowStatus.Submitted,
    StoreOrderFlowStatus.Picking,
    StoreOrderFlowStatus.Completed,
  ],
  active: [StoreOrderFlowStatus.Submitted, StoreOrderFlowStatus.Picking],
  completed: [StoreOrderFlowStatus.Completed],
}

function formatDateTime(value?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString('en-AU', {
    hour12: false,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatAmount(order: StoreOrderListItem) {
  const value =
    order.importTotalAmount ?? order.totalAmount ?? order.totalOrderAmount ?? 0
  return `$${value.toFixed(2)}`
}

function getOrderStatusMeta(status: number) {
  return {
    label: statusLabelMap[status] ?? 'Unknown',
    color: statusColorMap[status] ?? 'default',
  }
}

function createDefaultDateRange(): DateRangeValue {
  return [dayjs().subtract(59, 'day').startOf('day'), dayjs().endOf('day')]
}

function createQuickDateRange(filter: Exclude<QuickRangeFilter, 'custom'>): DateRangeValue {
  if (filter === 'today') {
    return [dayjs().startOf('day'), dayjs().endOf('day')]
  }

  if (filter === 'week') {
    return [dayjs().startOf('week'), dayjs().endOf('day')]
  }

  return [dayjs().startOf('month'), dayjs().endOf('day')]
}

export default function ShopOrdersPage() {
  const navigate = useNavigate()
  const selectedStore = useShopStore((state) => state.selectedStore)

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [keywordInput, setKeywordInput] = useState('')
  const [keyword, setKeyword] = useState('')
  const [orders, setOrders] = useState<StoreOrderListItem[]>([])
  const [loading, setLoading] = useState(false)
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(12)
  const [dateRange, setDateRange] = useState<DateRangeValue>(createDefaultDateRange)
  const [quickRange, setQuickRange] = useState<QuickRangeFilter>('custom')

  useEffect(() => {
    let cancelled = false

    const fetchOrders = async () => {
      setLoading(true)

      try {
        const result = await getStoreOrderList({
          pageNumber: currentPage,
          pageSize,
          keyword: keyword || undefined,
          storeCodes: selectedStore?.storeCode ? [selectedStore.storeCode] : undefined,
          startDate: dateRange[0].format('YYYY-MM-DD'),
          endDate: dateRange[1].format('YYYY-MM-DD'),
          statusList: statusQueryMap[statusFilter],
          sortBy: 'OrderDate',
          sortDescending: true,
        })

        if (cancelled) {
          return
        }

        setOrders(result.items)
        setTotal(result.total)
      } catch (error) {
        if (!cancelled) {
          setOrders([])
          setTotal(0)
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchOrders()

    return () => {
      cancelled = true
    }
  }, [currentPage, dateRange, keyword, pageSize, selectedStore?.storeCode, statusFilter])

  const stats = useMemo(() => {
    const activeCount = orders.filter((item) =>
      [StoreOrderFlowStatus.Submitted, StoreOrderFlowStatus.Picking].includes(item.flowStatus),
    ).length
    const completedCount = orders.filter(
      (item) => item.flowStatus === StoreOrderFlowStatus.Completed,
    ).length
    const visibleAmount = orders.reduce((sum, item) => {
      const value = item.importTotalAmount ?? item.totalAmount ?? item.totalOrderAmount ?? 0
      return sum + value
    }, 0)

    return {
      totalOrders: total,
      activeCount,
      completedCount,
      visibleAmount,
    }
  }, [orders, total])

  return (
    <div className="shop-orders-page">
      <div className="shop-orders-hero">
        <div>
          <div className="shop-orders-eyebrow">
            <HistoryOutlined /> Order Timeline
          </div>
          <Title level={2}>Order History</Title>
          <Text type="secondary">
            Browse recent submissions and monitor fulfilment progress from the new storefront.
          </Text>
        </div>
        <div className="shop-orders-store-badge">
          <ShopOutlined />
          <span>{selectedStore?.storeName || 'All accessible stores'}</span>
        </div>
      </div>

      <div className="shop-orders-stats">
        <div className="shop-orders-stat-card">
          <span className="shop-orders-stat-label">Orders</span>
          <strong>{stats.totalOrders}</strong>
        </div>
        <div className="shop-orders-stat-card">
          <span className="shop-orders-stat-label">In Progress</span>
          <strong>{stats.activeCount}</strong>
        </div>
        <div className="shop-orders-stat-card">
          <span className="shop-orders-stat-label">Completed</span>
          <strong>{stats.completedCount}</strong>
        </div>
        <div className="shop-orders-stat-card accent">
          <span className="shop-orders-stat-label">Visible Total</span>
          <strong>${stats.visibleAmount.toFixed(2)}</strong>
        </div>
      </div>

      <div className="shop-orders-toolbar">
        <div className="shop-orders-toolbar-filters">
          <Segmented<StatusFilter>
            value={statusFilter}
            onChange={(value) => {
              setCurrentPage(1)
              setStatusFilter(value)
            }}
            options={[
              { label: 'All', value: 'all' },
              { label: 'In Progress', value: 'active' },
              { label: 'Completed', value: 'completed' },
            ]}
          />
          <RangePicker
            value={dateRange}
            allowClear={false}
            className="shop-orders-date-range"
            presets={[
              { label: 'Last 7 Days', value: [dayjs().subtract(6, 'day').startOf('day'), dayjs().endOf('day')] },
              { label: 'Last 30 Days', value: [dayjs().subtract(29, 'day').startOf('day'), dayjs().endOf('day')] },
              { label: 'Last 60 Days', value: createDefaultDateRange() },
            ]}
            onChange={(value) => {
              if (!value || !value[0] || !value[1]) {
                setDateRange(createDefaultDateRange())
                setQuickRange('custom')
                setCurrentPage(1)
                return
              }

              setDateRange([value[0].startOf('day'), value[1].endOf('day')])
              setQuickRange('custom')
              setCurrentPage(1)
            }}
          />
          <Segmented<QuickRangeFilter>
            value={quickRange}
            className="shop-orders-quick-range"
            onChange={(value) => {
              setQuickRange(value)
              setCurrentPage(1)
              if (value === 'custom') {
                setDateRange(createDefaultDateRange())
                return
              }

              setDateRange(createQuickDateRange(value))
            }}
            options={[
              { label: 'Today', value: 'today' },
              { label: 'This Week', value: 'week' },
              { label: 'This Month', value: 'month' },
              { label: 'Last 60 Days', value: 'custom' },
            ]}
          />
        </div>

        <div className="shop-orders-toolbar-actions">
          <Search
            value={keywordInput}
            allowClear
            placeholder="Search by order number"
            enterButton={<SearchOutlined />}
            onChange={(event) => setKeywordInput(event.target.value)}
            onSearch={(value) => {
              setCurrentPage(1)
              setKeyword(value.trim())
            }}
            className="shop-orders-search"
          />
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setCurrentPage(1)
              setKeywordInput('')
              setKeyword('')
              setDateRange(createDefaultDateRange())
              setQuickRange('custom')
            }}
          >
            Reset
          </Button>
        </div>
      </div>

      <div className="shop-orders-filter-note">
        Showing {selectedStore?.storeName || 'all accessible stores'}.
        Change the store selector in the header to switch history scope. Current range:
        {' '}
        {dateRange[0].format('DD MMM YYYY')}
        {' - '}
        {dateRange[1].format('DD MMM YYYY')}
      </div>

      {loading ? (
        <div className="shop-orders-loading">
          <Spin size="large" />
        </div>
      ) : orders.length ? (
        <>
          <div className="shop-orders-grid">
            {orders.map((order) => {
              const statusMeta = getOrderStatusMeta(order.flowStatus)

              return (
                <article key={order.orderGUID} className="shop-order-card">
                  <div className="shop-order-card-top">
                    <div className="shop-order-card-headline">
                      <div className="shop-order-card-label">Order No.</div>
                      <Title level={4} className="shop-order-card-title">
                        {order.orderNo || order.orderGUID.slice(0, 8)}
                      </Title>
                    </div>
                    <Tag color={statusMeta.color}>{statusMeta.label}</Tag>
                  </div>

                  <div className="shop-order-card-meta">
                    <div>
                      <ShopOutlined />
                      <span>{order.storeName || order.storeCode || 'Unknown Store'}</span>
                    </div>
                    <div>
                      <ClockCircleOutlined />
                      <span>{formatDateTime(order.orderDate)}</span>
                    </div>
                  </div>

                  <div className="shop-order-card-metrics">
                    <div className="shop-order-metric">
                      <span>Ordered</span>
                      <strong>{order.totalQuantity ?? 0}</strong>
                    </div>
                    <div className="shop-order-metric">
                      <span>Allocated</span>
                      <strong>{order.totalAllocQuantity ?? 0}</strong>
                    </div>
                    <div className="shop-order-metric amount">
                      <span>Total</span>
                      <strong>{formatAmount(order)}</strong>
                    </div>
                  </div>

                  {order.remarks ? (
                    <div className="shop-order-card-remarks">
                      <span className="shop-order-card-label">Remarks</span>
                      <p>{order.remarks}</p>
                    </div>
                  ) : null}

                  <button
                    type="button"
                    className="shop-order-card-footer"
                    onClick={() => navigate(`/shop/orders/${order.orderGUID}`)}
                  >
                    <Space size={6}>
                      <CheckCircleOutlined />
                      <Text>View Order Detail</Text>
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
              pageSizeOptions={[12, 24, 48]}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size && size !== pageSize) {
                  setPageSize(size)
                }
              }}
            />
          </div>
        </>
      ) : (
        <div className="shop-orders-empty">
          <Empty
            description={
              keyword
                ? 'No orders matched your search.'
                : 'No order history is available for the current filter.'
            }
          />
        </div>
      )}
    </div>
  )
}
