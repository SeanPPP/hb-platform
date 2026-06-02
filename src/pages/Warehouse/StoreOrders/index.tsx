import {
  CopyOutlined,
  DatabaseOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
import {
  App as AntdApp,
  Button,
  Card,
  DatePicker,
  Input,
  Modal,
  Popconfirm,
  Radio,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { refreshSession } from '../../../services/auth'
import {
  batchUpdateStoreOrderStatus,
  createStoreOrderFullHqSyncJob,
  createStoreOrderIncrementalHqSyncJob,
  copyStoreOrder,
  createStoreOrder,
  deleteStoreOrder,
  getStoreOrderHqSyncJob,
  getStoreOrderList,
  getUsedStoreOrderBranches,
  updateStoreOrderStatus,
} from '../../../services/storeOrderService'
import { getStores } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type { StoreDto } from '../../../types/store'
import type {
  CopyStoreOrderPayload,
  StoreOrderBranchOption,
  StoreOrderHqSyncPayload,
  StoreOrderSyncConflictStrategy,
  StoreOrderFlowStatus,
  StoreOrderListItem,
  StoreOrderListQuery,
} from '../../../types/storeOrder'
import {
  StoreOrderFlowStatus as FlowStatus,
  StoreOrderStatusColorMap,
  StoreOrderStatusOptions,
} from '../../../types/storeOrder'
import { getDateTagColor } from '../../../utils/tagColors'
import { getStoreColor } from '../../../utils/userTableColors'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { RequestError } from '../../../utils/request'
import {
  ensureStoreOrderSyncSession,
  STORE_ORDER_SYNC_AUTH_EXPIRED_MESSAGE,
} from './syncSessionGuard'
import {
  createStoreOrderSyncJobPoller,
  StoreOrderSyncPollingCancelledError,
  StoreOrderSyncPollingTimeoutError,
} from './syncJobPolling'

type RangeValue = [Dayjs | null, Dayjs | null] | null

interface StorePickerModalProps {
  open: boolean
  title: string
  loading?: boolean
  onCancel: () => void
  onSelect: (store: StoreDto) => void
}

interface CopyOrderModalProps {
  open: boolean
  loading?: boolean
  onCancel: () => void
  onConfirm: (payload: Omit<CopyStoreOrderPayload, 'sourceOrderGUID'>) => void
}

function getLocale(language?: string) {
  return language?.startsWith('zh') ? 'zh-CN' : 'en-US'
}

function formatDateTime(value?: string, language?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString(getLocale(language), { hour12: false })
}

function formatDate(value?: string, language?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleDateString(getLocale(language))
}

function formatAmount(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }
  return value.toFixed(2)
}

function formatVolume(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }
  return value.toFixed(4)
}

function renderDateTag(value?: string, language?: string) {
  const displayValue = formatDate(value, language)
  if (displayValue === '--') {
    return '--'
  }

  return <Tag color={getDateTagColor(displayValue)}>{displayValue}</Tag>
}

const DEFAULT_INCREMENTAL_CONFLICT_STRATEGY: StoreOrderSyncConflictStrategy = 'LatestWins'

function StorePickerModal({ open, title, loading, onCancel, onSelect }: StorePickerModalProps) {
  const { t } = useTranslation()
  const { message } = AntdApp.useApp()
  const [stores, setStores] = useState<StoreDto[]>([])
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')

  useEffect(() => {
    if (!open) {
      return
    }

    let cancelled = false

    const loadStores = async () => {
      setFetching(true)
      try {
        const result = await getStores({
          page: 1,
          pageSize: 200,
          isActive: true,
          search: keyword || undefined,
          sortField: 'storeName',
          sortOrder: 'ascend',
        })

        if (!cancelled) {
          setStores(result.items)
        }
      } catch (error) {
        console.error(error)
        if (!cancelled) {
          message.error(t('storeOrders.loadStoresFailed'))
        }
      } finally {
        if (!cancelled) {
          setFetching(false)
        }
      }
    }

    void loadStores()

    return () => {
      cancelled = true
    }
  }, [keyword, message, open, t])

  return (
    <Modal
      title={title}
      open={open}
      width={760}
      footer={null}
      destroyOnHidden
      onCancel={() => {
        setKeyword('')
        onCancel()
      }}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Input
          value={keyword}
          allowClear
          placeholder={t('storeOrders.searchStorePlaceholder')}
          prefix={<SearchOutlined />}
          onChange={(event) => setKeyword(event.target.value)}
        />
        <Table
          rowKey="storeGUID"
          loading={fetching || loading}
          size="small"
          pagination={false}
          dataSource={stores}
          scroll={{ y: 360 }}
          columns={[
            { title: t('column.storeName'), dataIndex: 'storeName' },
            { title: t('column.storeCode'), dataIndex: 'storeCode', width: 140 },
            {
              title: t('column.status'),
              dataIndex: 'isActive',
              width: 90,
              render: (value: boolean) => (
                <Tag color={value ? 'success' : 'default'}>
                  {value ? t('common.enable') : t('common.disable')}
                </Tag>
              ),
            },
            {
              title: t('common.address'),
              dataIndex: 'address',
              render: (value: string | undefined) => value || '--',
            },
          ]}
          onRow={(record) => ({
            onClick: () => onSelect(record),
            style: { cursor: 'pointer' },
          })}
        />
      </Space>
    </Modal>
  )
}

function CopyOrderModal({ open, loading, onCancel, onConfirm }: CopyOrderModalProps) {
  const { t } = useTranslation()
  const { message } = AntdApp.useApp()
  const [stores, setStores] = useState<StoreDto[]>([])
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [selectedStore, setSelectedStore] = useState<StoreDto | null>(null)
  const [copyOrderQuantity, setCopyOrderQuantity] = useState(true)
  const [copyAllocQuantity, setCopyAllocQuantity] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }

    let cancelled = false

    const loadStores = async () => {
      setFetching(true)
      try {
        const result = await getStores({
          page: 1,
          pageSize: 200,
          isActive: true,
          search: keyword || undefined,
          sortField: 'storeName',
          sortOrder: 'ascend',
        })

        if (!cancelled) {
          setStores(result.items)
        }
      } catch (error) {
        console.error(error)
        if (!cancelled) {
          message.error(t('storeOrders.loadStoresFailed'))
        }
      } finally {
        if (!cancelled) {
          setFetching(false)
        }
      }
    }

    void loadStores()

    return () => {
      cancelled = true
    }
  }, [keyword, message, open, t])

  const handleClose = () => {
    setKeyword('')
    setSelectedStore(null)
    setCopyOrderQuantity(true)
    setCopyAllocQuantity(false)
    onCancel()
  }

  return (
    <Modal
      title={t('storeOrders.copyOrderTitle')}
      open={open}
      width={860}
      destroyOnHidden
      confirmLoading={loading}
      okText={t('storeOrders.confirmCopy')}
      cancelText={t('common.cancel')}
      okButtonProps={{ disabled: !selectedStore }}
      onCancel={handleClose}
      onOk={() => {
        if (!selectedStore) {
          message.warning(t('storeOrders.selectTargetStore'))
          return
        }

        onConfirm({
          targetStoreCode: selectedStore.storeCode,
          copyOrderQuantity,
          copyAllocQuantity,
        })
      }}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space>
          <Button
            type={copyOrderQuantity ? 'primary' : 'default'}
            onClick={() => setCopyOrderQuantity((current) => !current)}
          >
            {t('storeOrders.copyOrderQty')}
          </Button>
          <Button
            type={copyAllocQuantity ? 'primary' : 'default'}
            onClick={() => setCopyAllocQuantity((current) => !current)}
          >
            {t('storeOrders.copyShipQty')}
          </Button>
        </Space>
        <Input
          value={keyword}
          allowClear
          placeholder={t('storeOrders.searchStorePlaceholder')}
          prefix={<SearchOutlined />}
          onChange={(event) => setKeyword(event.target.value)}
        />
        <Typography.Text type="secondary">
          {t('storeOrders.currentSelection')}
          {selectedStore
            ? `${selectedStore.storeName} (${selectedStore.storeCode})`
            : t('storeOrders.noneSelected')}
        </Typography.Text>
        <Table
          rowKey="storeGUID"
          loading={fetching || loading}
          size="small"
          pagination={false}
          dataSource={stores}
          scroll={{ y: 320 }}
          rowClassName={(record) =>
            record.storeGUID === selectedStore?.storeGUID ? 'ant-table-row-selected' : ''
          }
          columns={[
            { title: t('column.storeName'), dataIndex: 'storeName' },
            { title: t('column.storeCode'), dataIndex: 'storeCode', width: 140 },
            {
              title: t('common.address'),
              dataIndex: 'address',
              render: (value: string | undefined) => value || '--',
            },
          ]}
          onRow={(record) => ({
            onClick: () => setSelectedStore(record),
            style: { cursor: 'pointer' },
          })}
        />
      </Space>
    </Modal>
  )
}

function isUnauthorizedError(error: unknown) {
  return error instanceof RequestError && error.status === 401
}

export default function StoreOrdersPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { message, modal } = AntdApp.useApp()
  const { access, clearAuth } = useAuthStore()

  const [loading, setLoading] = useState(false)
  const [creating, setCreating] = useState(false)
  const [copying, setCopying] = useState(false)
  const [data, setData] = useState<StoreOrderListItem[]>([])
  const [branches, setBranches] = useState<StoreOrderBranchOption[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [keyword, setKeyword] = useState('')
  const [dateRange, setDateRange] = useState<RangeValue>(null)
  const [selectedStoreCodes, setSelectedStoreCodes] = useState<string[]>([])
  const [statusList, setStatusList] = useState<StoreOrderFlowStatus[]>([
    FlowStatus.Submitted,
    FlowStatus.Completed,
  ])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [sortField, setSortField] = useState('orderDate')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend')
  const [syncingMode, setSyncingMode] = useState<'Full' | 'Incremental' | null>(null)
  const [incrementalSyncOpen, setIncrementalSyncOpen] = useState(false)
  const [incrementalSyncRange, setIncrementalSyncRange] = useState<RangeValue>(() => [
    dayjs().subtract(30, 'day'),
    dayjs(),
  ])
  const [incrementalConflictStrategy, setIncrementalConflictStrategy] =
    useState<StoreOrderSyncConflictStrategy>(DEFAULT_INCREMENTAL_CONFLICT_STRATEGY)
  const [storePickerOpen, setStorePickerOpen] = useState(false)
  const [copyModalOpen, setCopyModalOpen] = useState(false)
  // 记录当前轮询停止函数，确保重复触发和页面卸载时都能清理定时器。
  const stopSyncPollingRef = useRef<(() => void) | null>(null)
  // 避免卸载后继续 setState，防止轮询尾声触发无效更新。
  const isMountedRef = useRef(true)

  const branchMap = useMemo(
    () => Object.fromEntries(branches.map((item) => [item.code, item.name])) as Record<string, string>,
    [branches],
  )

  const statusLabelMap = useMemo(
    () =>
      ({
        [FlowStatus.ShoppingCart]: t('storeOrders.statusShoppingCart'),
        [FlowStatus.Submitted]: t('storeOrders.statusSubmitted'),
        [FlowStatus.Completed]: t('storeOrders.statusCompleted'),
        [FlowStatus.Picking]: t('storeOrders.statusPicking'),
      }) as Record<StoreOrderFlowStatus, string>,
    [t],
  )

  const statusOptions = useMemo(
    () =>
      StoreOrderStatusOptions.map((item) => ({
        value: item.value,
        label: statusLabelMap[item.value],
      })),
    [statusLabelMap],
  )

  const buildQuery = (
    overrides: Partial<StoreOrderListQuery & { pageNumber: number; pageSize: number }> = {},
  ): StoreOrderListQuery => ({
    keyword: keyword || undefined,
    storeCodes: selectedStoreCodes.length ? selectedStoreCodes : undefined,
    startDate: dateRange?.[0]?.startOf('day').toISOString(),
    endDate: dateRange?.[1]?.endOf('day').toISOString(),
    statusList: statusList.length ? statusList : undefined,
    pageNumber: overrides.pageNumber ?? page,
    pageSize: overrides.pageSize ?? pageSize,
    sortBy: overrides.sortBy ?? sortField,
    sortDescending: overrides.sortDescending ?? sortOrder === 'descend',
  })

  const openDetail = (record: Pick<StoreOrderListItem, 'orderGUID' | 'orderNo'>) => {
    navigate(`/warehouse/store-order/detail/${record.orderGUID}`, {
      state: { orderNo: record.orderNo },
    })
  }

  const loadBranches = async () => {
    try {
      const result = await getUsedStoreOrderBranches()
      setBranches(result)
    } catch (error) {
      console.error(error)
      message.error(t('storeOrders.loadBranchFiltersFailed'))
    }
  }

  const loadData = async (
    overrides: Partial<StoreOrderListQuery & { pageNumber: number; pageSize: number }> = {},
  ) => {
    setLoading(true)
    try {
      const result = await getStoreOrderList(buildQuery(overrides))
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setSelectedRowKeys([])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void Promise.all([loadData({ pageNumber: 1 }), loadBranches()])
  }, [])

  useEffect(() => {
    return () => {
      isMountedRef.current = false
      stopSyncPollingRef.current?.()
      stopSyncPollingRef.current = null
    }
  }, [])

  const runStoreOrderHqSync = async (
    mode: 'Full' | 'Incremental',
    options: StoreOrderHqSyncPayload = {},
  ) => {
    if (isMountedRef.current) {
      setSyncingMode(mode)
    }
    try {
      const hasSession = await ensureStoreOrderSyncSession({
        refreshSession,
        clearAuth,
        redirectToLogin: (target) => navigate(target, { replace: true }),
        currentPath:
          typeof window === 'undefined'
            ? '/warehouse/store-orders'
            : `${window.location.pathname}${window.location.search}`,
      })

      if (!hasSession) {
        message.warning(STORE_ORDER_SYNC_AUTH_EXPIRED_MESSAGE)
        return
      }

      const syncJob =
        mode === 'Full'
          ? await createStoreOrderFullHqSyncJob()
          : await createStoreOrderIncrementalHqSyncJob(options)

      if (!syncJob.jobId) {
        message.error(syncJob.message || t('storeOrders.syncJobCreateFailed'))
        return
      }

      const poller = createStoreOrderSyncJobPoller({
        jobId: syncJob.jobId,
        getJob: getStoreOrderHqSyncJob,
      })
      stopSyncPollingRef.current = poller.stop

      const result = await poller.promise
      stopSyncPollingRef.current = null

      if (result.status === 'Failed') {
        message.error(result?.message || t('storeOrders.syncFailed'))
        return
      }

      const parts: string[] = []
      if ((result.ordersSynced ?? 0) > 0 || (result.detailsSynced ?? 0) > 0) {
        parts.push(
          t('storeOrders.syncCreatedSummary', {
            orders: result.ordersSynced ?? 0,
            details: result.detailsSynced ?? 0,
          }),
        )
      }
      if ((result.ordersUpdated ?? 0) > 0 || (result.detailsUpdated ?? 0) > 0) {
        parts.push(
          t('storeOrders.syncUpdatedSummary', {
            orders: result.ordersUpdated ?? 0,
            details: result.detailsUpdated ?? 0,
          }),
        )
      }
      if ((result.ordersSoftDeleted ?? 0) > 0 || (result.detailsSoftDeleted ?? 0) > 0) {
        parts.push(
          t('storeOrders.syncDeletedSummary', {
            orders: result.ordersSoftDeleted ?? 0,
            details: result.detailsSoftDeleted ?? 0,
          }),
        )
      }
      if (
        (result.skippedOrdersBecauseLocalNewer ?? 0) > 0 ||
        (result.skippedDetailsBecauseLocalNewer ?? 0) > 0
      ) {
        parts.push(
          t('storeOrders.syncSkippedSummary', {
            orders: result.skippedOrdersBecauseLocalNewer ?? 0,
            details: result.skippedDetailsBecauseLocalNewer ?? 0,
          }),
        )
      }

      if (parts.length) {
        message.success(parts.join(', '))
      } else {
        message.info(result.message || t('storeOrders.alreadyLatest'))
      }

      void loadData()
    } catch (error) {
      if (isUnauthorizedError(error)) {
        message.warning(STORE_ORDER_SYNC_AUTH_EXPIRED_MESSAGE)
        return
      }
      if (error instanceof StoreOrderSyncPollingCancelledError) {
        return
      }
      if (error instanceof StoreOrderSyncPollingTimeoutError) {
        message.warning(t('storeOrders.syncTimeout'))
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.syncFailed'))
    } finally {
      stopSyncPollingRef.current = null
      if (isMountedRef.current) {
        setSyncingMode(null)
      }
    }
  }

  const handleFullHqSync = () => {
    modal.confirm({
      title: t('storeOrders.syncFullTitle'),
      content: t('storeOrders.syncFullContent'),
      okText: t('storeOrders.syncFullConfirm'),
      cancelText: t('common.cancel'),
      okButtonProps: { danger: true },
      onOk: () => runStoreOrderHqSync('Full'),
    })
  }

  const handleOpenIncrementalHqSync = () => {
    // 每次打开弹窗都恢复安全默认值，避免上次选择 HQ 优先后被无意沿用。
    setIncrementalConflictStrategy(DEFAULT_INCREMENTAL_CONFLICT_STRATEGY)
    setIncrementalSyncOpen(true)
  }

  const handleIncrementalHqSync = async () => {
    if (!incrementalSyncRange?.[0] || !incrementalSyncRange?.[1]) {
      message.warning(t('storeOrders.syncDateRangeRequired'))
      return
    }

    setIncrementalSyncOpen(false)
    await runStoreOrderHqSync('Incremental', {
      storeCodes: selectedStoreCodes.length ? selectedStoreCodes : undefined,
      startDate: incrementalSyncRange[0].startOf('day').toISOString(),
      endDate: incrementalSyncRange[1].endOf('day').toISOString(),
      // 增量同步需明确告知后端冲突策略，避免默认行为随接口演进漂移。
      conflictStrategy: incrementalConflictStrategy,
    })
  }

  const handleStatusToggle = (record: StoreOrderListItem) => {
    if (record.flowStatus !== FlowStatus.Submitted && record.flowStatus !== FlowStatus.Completed) {
      return
    }

    const nextStatus =
      record.flowStatus === FlowStatus.Submitted ? FlowStatus.Completed : FlowStatus.Submitted
    const actionLabel =
      nextStatus === FlowStatus.Completed
        ? t('storeOrders.markCompleted')
        : t('storeOrders.markSubmitted')

    modal.confirm({
      title: t('storeOrders.updateStatusTitle'),
      content: t('storeOrders.updateStatusConfirm', {
        orderNo: record.orderNo,
        action: actionLabel,
      }),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          await updateStoreOrderStatus({
            orderGUID: record.orderGUID,
            newStatus: nextStatus,
          })
          message.success(t('storeOrders.updateStatusSuccess'))
          void loadData()
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('storeOrders.updateStatusFailed'))
        }
      },
    })
  }

  const handleBatchStatusChange = (newStatus: StoreOrderFlowStatus) => {
    if (!selectedRowKeys.length) {
      message.warning(t('storeOrders.selectOrdersFirst'))
      return
    }

    modal.confirm({
      title: t('storeOrders.batchUpdateStatusTitle'),
      content: t('storeOrders.batchUpdateStatusConfirm', {
        count: selectedRowKeys.length,
        status: statusLabelMap[newStatus],
      }),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          await batchUpdateStoreOrderStatus({
            orderGUIDs: selectedRowKeys.map(String),
            newStatus,
          })
          message.success(t('storeOrders.batchUpdateStatusSuccess'))
          void loadData()
        } catch (error) {
          console.error(error)
          message.error(
            error instanceof Error ? error.message : t('storeOrders.batchUpdateStatusFailed'),
          )
        }
      },
    })
  }

  const handleCopyOrderNo = async (orderNo: string) => {
    await copyTextToClipboard(orderNo, {
      successMessage: t('storeOrders.copyOrderNoSuccess', { orderNo }),
      failureMessage: t('storeOrders.copyOrderNoFailed'),
    })
  }

  const columns = useMemo<ColumnsType<StoreOrderListItem>>(
    () => [
      {
        title: t('column.index'),
        dataIndex: 'index',
        width: 60,
        fixed: 'left',
        render: (_, __, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: t('column.orderNo'),
        dataIndex: 'orderNo',
        width: 140,
        sorter: true,
        fixed: 'left',
        render: (value: string, record) => (
          <Space size={4}>
            <Button type="link" style={{ padding: 0 }} onClick={() => openDetail(record)}>
              {value}
            </Button>
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              onClick={() => void handleCopyOrderNo(value)}
            />
          </Space>
        ),
      },
      {
        title: t('column.store'),
        dataIndex: 'storeCode',
        width: 180,
        sorter: true,
        render: (value: string | undefined, record) => {
          const code = value || '--'
          const name = record.storeName || (value ? branchMap[value] : undefined)
          return (
            <Tag
              color={getStoreColor(code)}
              style={{ cursor: value ? 'pointer' : 'default' }}
              onClick={() => {
                if (!value) {
                  return
                }
                setSelectedStoreCodes([value])
                void loadData({ pageNumber: 1, storeCodes: [value] })
              }}
            >
              {name ? `${code} - ${name}` : code}
            </Tag>
          )
        },
      },
      {
        title: t('column.orderDate'),
        dataIndex: 'orderDate',
        width: 130,
        sorter: true,
        render: (value: string | undefined) => renderDateTag(value, i18n.language),
      },
      {
        title: t('storeOrders.outboundDate'),
        dataIndex: 'outboundDate',
        width: 130,
        sorter: true,
        render: (value: string | undefined) => renderDateTag(value, i18n.language),
      },
      {
        title: t('column.status'),
        dataIndex: 'flowStatus',
        width: 110,
        sorter: true,
        render: (value: StoreOrderFlowStatus, record) => (
          <Tag
            color={StoreOrderStatusColorMap[value] || 'default'}
            style={{
              cursor:
                value === FlowStatus.Submitted || value === FlowStatus.Completed
                  ? 'pointer'
                  : 'default',
            }}
            onClick={() => handleStatusToggle(record)}
          >
            {statusLabelMap[value] || `${t('column.status')} ${value}`}
          </Tag>
        ),
      },
      {
        title: t('storeOrders.orderQuantity'),
        dataIndex: 'totalQuantity',
        width: 110,
        sorter: true,
      },
      {
        title: t('storeOrders.orderAmount'),
        dataIndex: 'totalOrderAmount',
        width: 120,
        sorter: true,
        render: (value: number) => formatAmount(value),
      },
      {
        title: t('storeOrders.orderVolume'),
        dataIndex: 'totalOrderVolume',
        width: 120,
        render: (value: number | undefined) => formatVolume(value),
      },
      {
        title: t('storeOrders.shipVolume'),
        dataIndex: 'totalAllocVolume',
        width: 120,
        render: (value: number | undefined) => formatVolume(value),
      },
      {
        title: t('storeOrders.shipQuantity'),
        dataIndex: 'totalAllocQuantity',
        width: 110,
        sorter: true,
      },
      {
        title: t('storeOrders.shipAmount'),
        dataIndex: 'importTotalAmount',
        width: 120,
        sorter: true,
        render: (value: number) => formatAmount(value),
      },
      {
        title: t('common.remarks'),
        dataIndex: 'remarks',
        width: 220,
        ellipsis: true,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: t('column.createTime'),
        dataIndex: 'createdAt',
        width: 180,
        render: (value: string | undefined) => formatDateTime(value, i18n.language),
      },
      {
        title: t('column.updater'),
        dataIndex: 'updatedBy',
        width: 140,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: t('column.updateTime'),
        dataIndex: 'updatedAt',
        width: 180,
        render: (value: string | undefined) => formatDateTime(value, i18n.language),
      },
      {
        title: t('column.action'),
        key: 'action',
        fixed: 'right',
        width: 170,
        render: (_, record) => (
          <Space size={0}>
            <Button type="link" onClick={() => openDetail(record)}>
              {t('common.view')}
            </Button>
            {access.canDeleteOrder ? (
              <Popconfirm
                title={t('storeOrders.confirmDeleteOrder', { orderNo: record.orderNo })}
                okText={t('common.delete')}
                cancelText={t('common.cancel')}
                onConfirm={async () => {
                  try {
                    await deleteStoreOrder(record.orderGUID)
                    message.success(t('common.deleteSuccess'))
                    void loadData({ pageNumber: 1 })
                  } catch (error) {
                    console.error(error)
                    message.error(
                      error instanceof Error ? error.message : t('storeOrders.deleteFailed'),
                    )
                  }
                }}
              >
                <Button danger type="link">
                  {t('common.delete')}
                </Button>
              </Popconfirm>
            ) : null}
          </Space>
        ),
      },
    ],
    [access.canDeleteOrder, branchMap, i18n.language, page, pageSize, statusLabelMap, t],
  )

  return (
    <PageContainer
      title={t('storeOrders.title')}
      subtitle={t('storeOrders.subtitle')}
      extra={
        <Space wrap>
          {access.isAdmin ? (
            <Button
              danger
              icon={<DatabaseOutlined />}
              loading={syncingMode === 'Full'}
              disabled={syncingMode !== null}
              onClick={handleFullHqSync}
            >
              {t('storeOrders.syncFullOrders')}
            </Button>
          ) : null}
          {access.canManageWarehouseOrders ? (
            <Button
              icon={<SyncOutlined />}
              loading={syncingMode === 'Incremental'}
              disabled={syncingMode !== null}
              onClick={handleOpenIncrementalHqSync}
            >
              {t('storeOrders.syncIncrementalOrders')}
            </Button>
          ) : null}
          <Button
            type="primary"
            icon={<PlusOutlined />}
            disabled={!access.canWriteOrder}
            onClick={() => setStorePickerOpen(true)}
          >
            {t('storeOrders.newOrder')}
          </Button>
          <Button
            icon={<CopyOutlined />}
            disabled={!selectedRowKeys.length}
            onClick={() => setCopyModalOpen(true)}
          >
            {t('storeOrders.copyOrder', { count: selectedRowKeys.length })}
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setKeyword('')
              setDateRange(null)
              setSelectedStoreCodes([])
              setStatusList([FlowStatus.Submitted, FlowStatus.Completed])
              setSortField('orderDate')
              setSortOrder('descend')
              void loadData({
                keyword: undefined,
                startDate: undefined,
                endDate: undefined,
                storeCodes: undefined,
                statusList: [FlowStatus.Submitted, FlowStatus.Completed],
                pageNumber: 1,
                pageSize,
                sortBy: 'orderDate',
                sortDescending: true,
              })
            }}
          >
            {t('common.reset')}
          </Button>
          <Button
            disabled={!selectedRowKeys.length}
            onClick={() => handleBatchStatusChange(FlowStatus.Submitted)}
          >
            {t('storeOrders.batchSubmitted')}
          </Button>
          <Button
            disabled={!selectedRowKeys.length}
            onClick={() => handleBatchStatusChange(FlowStatus.Completed)}
          >
            {t('storeOrders.batchCompleted')}
          </Button>
        </Space>
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            value={keyword}
            style={{ width: 260 }}
            allowClear
            prefix={<SearchOutlined />}
            placeholder={t('storeOrders.searchPlaceholder')}
            onChange={(event) => setKeyword(event.target.value)}
          />
          <DatePicker.RangePicker value={dateRange} onChange={(value) => setDateRange(value)} />
          <Select
            mode="multiple"
            value={selectedStoreCodes}
            allowClear
            style={{ width: 280 }}
            placeholder={t('storeOrders.allStores')}
            options={branches.map((item) => ({
              value: item.code,
              label: `${item.code} - ${item.name}`,
            }))}
            onChange={(value) => setSelectedStoreCodes(value)}
          />
          <Select
            mode="multiple"
            value={statusList}
            allowClear
            style={{ width: 220 }}
            placeholder={t('storeOrders.allStatuses')}
            options={statusOptions}
            onChange={(value) => setStatusList(value)}
          />
          <Button type="primary" onClick={() => void loadData({ pageNumber: 1 })}>
            {t('common.query')}
          </Button>
        </Space>

        <Table
          rowKey="orderGUID"
          loading={loading}
          dataSource={data}
          columns={columns}
          rowSelection={{
            selectedRowKeys,
            onChange: setSelectedRowKeys,
          }}
          scroll={{ x: 1900, y: 620 }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showTotal: (value) => t('common.total', { count: value }),
          }}
          onChange={(
            pagination: TablePaginationConfig,
            _,
            sorter: SorterResult<StoreOrderListItem> | SorterResult<StoreOrderListItem>[],
          ) => {
            const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
            const nextSortField =
              typeof nextSorter?.field === 'string' ? nextSorter.field : sortField
            const nextSortOrder =
              nextSorter?.order === 'ascend' || nextSorter?.order === 'descend'
                ? nextSorter.order
                : sortOrder

            setSortField(nextSortField)
            setSortOrder(nextSortOrder)
            void loadData({
              pageNumber: pagination.current || 1,
              pageSize: pagination.pageSize || pageSize,
              sortBy: nextSortField,
              sortDescending: nextSortOrder === 'descend',
            })
          }}
        />
      </Card>

      <StorePickerModal
        open={storePickerOpen}
        title={t('storeOrders.selectStoreCreate')}
        loading={creating}
        onCancel={() => setStorePickerOpen(false)}
        onSelect={async (store) => {
          setCreating(true)
          try {
            const orderGuid = await createStoreOrder({ storeCode: store.storeCode })
            message.success(
              t('storeOrders.createOrderSuccess', { storeName: store.storeName || store.storeCode }),
            )
            setStorePickerOpen(false)
            navigate(`/warehouse/store-order/detail/${orderGuid}`)
            void loadData({ pageNumber: 1 })
          } catch (error) {
            console.error(error)
            message.error(error instanceof Error ? error.message : t('storeOrders.createOrderFailed'))
          } finally {
            setCreating(false)
          }
        }}
      />

      <CopyOrderModal
        open={copyModalOpen}
        loading={copying}
        onCancel={() => setCopyModalOpen(false)}
        onConfirm={async (payload) => {
          if (!selectedRowKeys.length) {
            message.warning(t('storeOrders.selectOrdersFirst'))
            return
          }

          setCopying(true)
          try {
            const sourceOrderGUID = String(selectedRowKeys[0])
            const result = await copyStoreOrder({
              sourceOrderGUID,
              ...payload,
            })

            const orderGuid = typeof result === 'string' ? result : result.orderGUID
            const orderNo = typeof result === 'string' ? '' : result.orderNo

            message.success(
              orderNo
                ? t('storeOrders.copyOrderSuccessWithNo', { orderNo })
                : t('storeOrders.copyOrderSuccess'),
            )
            setCopyModalOpen(false)
            setSelectedRowKeys([])
            navigate(`/warehouse/store-order/detail/${orderGuid}`, {
              state: {
                orderNo: orderNo || undefined,
              },
            })
            void loadData({ pageNumber: 1 })
          } catch (error) {
            console.error(error)
            message.error(error instanceof Error ? error.message : t('storeOrders.copyOrderFailed'))
          } finally {
            setCopying(false)
          }
        }}
      />

      <Modal
        title={t('storeOrders.syncIncrementalTitle')}
        open={incrementalSyncOpen}
        confirmLoading={syncingMode === 'Incremental'}
        okText={t('storeOrders.syncIncrementalOrders')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        onCancel={() => {
          setIncrementalSyncOpen(false)
          setIncrementalConflictStrategy(DEFAULT_INCREMENTAL_CONFLICT_STRATEGY)
        }}
        onOk={() => void handleIncrementalHqSync()}
      >
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text type="secondary">
            {t('storeOrders.syncDefaultRangeHint')}
          </Typography.Text>
          <DatePicker.RangePicker
            value={incrementalSyncRange}
            style={{ width: '100%' }}
            showTime
            onChange={(value) => setIncrementalSyncRange(value)}
          />
          <Space direction="vertical" size={8} style={{ width: '100%' }}>
            <Typography.Text>{t('storeOrders.syncConflictStrategy')}</Typography.Text>
            <Radio.Group
              value={incrementalConflictStrategy}
              onChange={(event) =>
                setIncrementalConflictStrategy(event.target.value as StoreOrderSyncConflictStrategy)
              }
            >
              <Space direction="vertical" size={8}>
                <Radio value="LatestWins">{t('storeOrders.syncConflictLatestWins')}</Radio>
                <Radio value="HqWins">{t('storeOrders.syncConflictHqWins')}</Radio>
              </Space>
            </Radio.Group>
          </Space>
          {selectedStoreCodes.length ? (
            <Typography.Text type="secondary">
              {t('storeOrders.syncIncrementalScope', { count: selectedStoreCodes.length })}
            </Typography.Text>
          ) : (
            <Typography.Text type="secondary">
              {t('storeOrders.syncIncrementalAllScope')}
            </Typography.Text>
          )}
        </Space>
      </Modal>
    </PageContainer>
  )
}
