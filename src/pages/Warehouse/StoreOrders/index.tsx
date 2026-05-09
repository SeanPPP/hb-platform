import {
  CopyOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
import {
  Button,
  Card,
  DatePicker,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { Dayjs } from 'dayjs'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import {
  batchUpdateStoreOrderStatus,
  copyStoreOrder,
  createStoreOrder,
  deleteStoreOrder,
  getStoreOrderList,
  getUsedStoreOrderBranches,
  syncMissingStoreOrders,
  updateStoreOrderStatus,
} from '../../../services/storeOrderService'
import { getStores } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type { StoreDto } from '../../../types/store'
import type {
  CopyStoreOrderPayload,
  StoreOrderBranchOption,
  StoreOrderFlowStatus,
  StoreOrderListItem,
  StoreOrderListQuery,
} from '../../../types/storeOrder'
import {
  StoreOrderStatusColorMap,
  StoreOrderStatusLabelMap,
  StoreOrderStatusOptions,
} from '../../../types/storeOrder'
import { getDateTagColor } from '../../../utils/tagColors'
import { getStoreColor } from '../../../utils/userTableColors'

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

function formatDateTime(value?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString('zh-CN', { hour12: false })
}

function formatDate(value?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleDateString('zh-CN')
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

function renderDateTag(value?: string) {
  const displayValue = formatDate(value)
  if (displayValue === '--') {
    return '--'
  }

  return <Tag color={getDateTagColor(displayValue)}>{displayValue}</Tag>
}

function StorePickerModal({ open, title, loading, onCancel, onSelect }: StorePickerModalProps) {
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
          message.error('加载分店失败')
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
  }, [keyword, open])

  return (
    <Modal
      title={title}
      open={open}
      width={760}
      footer={null}
      destroyOnClose
      onCancel={() => {
        setKeyword('')
        onCancel()
      }}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Input
          value={keyword}
          allowClear
          placeholder="搜索分店名称 / 编码"
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
            { title: '分店名称', dataIndex: 'storeName' },
            { title: '分店编码', dataIndex: 'storeCode', width: 140 },
            {
              title: '状态',
              dataIndex: 'isActive',
              width: 90,
              render: (value: boolean) => (
                <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
              ),
            },
            {
              title: '地址',
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
          message.error('加载分店失败')
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
  }, [keyword, open])

  const handleClose = () => {
    setKeyword('')
    setSelectedStore(null)
    setCopyOrderQuantity(true)
    setCopyAllocQuantity(false)
    onCancel()
  }

  return (
    <Modal
      title="复制订单"
      open={open}
      width={860}
      destroyOnClose
      confirmLoading={loading}
      okText="确认复制"
      cancelText="取消"
      okButtonProps={{ disabled: !selectedStore }}
      onCancel={handleClose}
      onOk={() => {
        if (!selectedStore) {
          message.warning('请选择目标分店')
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
            复制订货数量
          </Button>
          <Button
            type={copyAllocQuantity ? 'primary' : 'default'}
            onClick={() => setCopyAllocQuantity((current) => !current)}
          >
            复制发货数量
          </Button>
        </Space>
        <Input
          value={keyword}
          allowClear
          placeholder="搜索分店名称 / 编码"
          prefix={<SearchOutlined />}
          onChange={(event) => setKeyword(event.target.value)}
        />
        <Typography.Text type="secondary">
          当前选择：
          {selectedStore ? `${selectedStore.storeName} (${selectedStore.storeCode})` : '未选择'}
        </Typography.Text>
        <Table
          rowKey="storeGUID"
          loading={fetching || loading}
          size="small"
          pagination={false}
          dataSource={stores}
          scroll={{ y: 320 }}
          rowClassName={(record) => (record.storeGUID === selectedStore?.storeGUID ? 'ant-table-row-selected' : '')}
          columns={[
            { title: '分店名称', dataIndex: 'storeName' },
            { title: '分店编码', dataIndex: 'storeCode', width: 140 },
            {
              title: '地址',
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

export default function StoreOrdersPage() {
  const navigate = useNavigate()
  const { access } = useAuthStore()

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
    1 as StoreOrderFlowStatus,
    2 as StoreOrderFlowStatus,
  ])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [sortField, setSortField] = useState('orderDate')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend')
  const [syncLoading, setSyncLoading] = useState(false)
  const [storePickerOpen, setStorePickerOpen] = useState(false)
  const [copyModalOpen, setCopyModalOpen] = useState(false)

  const branchMap = useMemo(
    () =>
      Object.fromEntries(
        branches.map((item) => [item.code, item.name]),
      ) as Record<string, string>,
    [branches],
  )

  const buildQuery = (overrides: Partial<StoreOrderListQuery> = {}): StoreOrderListQuery => ({
    keyword: keyword || undefined,
    storeCodes: selectedStoreCodes.length ? selectedStoreCodes : undefined,
    startDate: dateRange?.[0]?.startOf('day').toISOString(),
    endDate: dateRange?.[1]?.endOf('day').toISOString(),
    statusList: statusList.length ? statusList : undefined,
    pageNumber: page,
    pageSize,
    sortBy: sortField,
    sortDescending: sortOrder === 'descend',
    ...overrides,
  })

  const openDetail = (record: Pick<StoreOrderListItem, 'orderGUID' | 'orderNo'>) => {
    navigate(`/warehouse/store-order/detail/${record.orderGUID}`, {
      state: {
        orderNo: record.orderNo,
      },
    })
  }

  const loadBranches = async () => {
    try {
      const result = await getUsedStoreOrderBranches()
      setBranches(result)
    } catch (error) {
      console.error(error)
      message.error('加载分店筛选失败')
    }
  }

  const loadData = async (overrides: Partial<StoreOrderListQuery> = {}) => {
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
      message.error(error instanceof Error ? error.message : '加载分店订货列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void Promise.all([loadData({ pageNumber: 1 }), loadBranches()])
  }, [])

  const handleSyncMissingOrders = async () => {
    setSyncLoading(true)
    try {
      const result = await syncMissingStoreOrders(
        selectedStoreCodes.length ? selectedStoreCodes[0] : undefined,
      )

      if (!result?.success) {
        message.error(result?.message || '同步订单失败')
        return
      }

      const ordersSynced = result.ordersSynced ?? 0
      const detailsSynced = result.detailsSynced ?? 0
      const ordersUpdated = result.ordersUpdated ?? 0
      const detailsUpdated = result.detailsUpdated ?? 0
      const hasChanges =
        ordersSynced > 0 || detailsSynced > 0 || ordersUpdated > 0 || detailsUpdated > 0

      if (hasChanges) {
        const parts: string[] = []
        if (ordersSynced > 0 || detailsSynced > 0) {
          parts.push(`新增同步 ${ordersSynced} 个订单、${detailsSynced} 条明细`)
        }
        if (ordersUpdated > 0 || detailsUpdated > 0) {
          parts.push(`更新 ${ordersUpdated} 个订单、${detailsUpdated} 条明细`)
        }
        message.success(parts.join('；'))
      } else {
        message.info(result.message || '未发现缺失订单，当前已是最新')
      }

      void loadData()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '同步订单失败')
    } finally {
      setSyncLoading(false)
    }
  }

  const handleStatusToggle = (record: StoreOrderListItem) => {
    if (record.flowStatus !== 1 && record.flowStatus !== 2) {
      return
    }

    const nextStatus = record.flowStatus === 1 ? 2 : 1
    const actionLabel = nextStatus === 2 ? '完成' : '恢复为已提交'

    Modal.confirm({
      title: '更新订单状态',
      content: `确认将订单 ${record.orderNo} ${actionLabel}吗？`,
      onOk: async () => {
        try {
          await updateStoreOrderStatus({
            orderGUID: record.orderGUID,
            newStatus: nextStatus,
          })
          message.success('状态更新成功')
          void loadData()
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : '状态更新失败')
        }
      },
    })
  }

  const handleBatchStatusChange = (newStatus: StoreOrderFlowStatus) => {
    if (!selectedRowKeys.length) {
      message.warning('请先选择订单')
      return
    }

    Modal.confirm({
      title: '批量更新状态',
      content: `确认将已选择的 ${selectedRowKeys.length} 个订单改为 ${StoreOrderStatusLabelMap[newStatus]} 吗？`,
      onOk: async () => {
        try {
          await batchUpdateStoreOrderStatus({
            orderGUIDs: selectedRowKeys.map(String),
            newStatus,
          })
          message.success('批量更新成功')
          void loadData()
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : '批量更新失败')
        }
      },
    })
  }

  const handleCopyOrderNo = async (orderNo: string) => {
    try {
      await navigator.clipboard.writeText(orderNo)
      message.success(`已复制订单号：${orderNo}`)
    } catch (error) {
      console.error(error)
      message.error('复制订单号失败')
    }
  }

  const columns = useMemo<ColumnsType<StoreOrderListItem>>(
    () => [
      {
        title: '#',
        dataIndex: 'index',
        width: 50,
        fixed: 'left',
        render: (_, __, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: '订单号',
        dataIndex: 'orderNo',
        width: 120,
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
        title: '分店',
        dataIndex: 'storeCode',
        width: 170,
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
        title: '订货日期',
        dataIndex: 'orderDate',
        width: 130,
        sorter: true,
        render: (value: string | undefined) => renderDateTag(value),
      },
      {
        title: '发货日期',
        dataIndex: 'outboundDate',
        width: 130,
        sorter: true,
        render: (value: string | undefined) => renderDateTag(value),
      },
      {
        title: '状态',
        dataIndex: 'flowStatus',
        width: 110,
        sorter: true,
        render: (value: StoreOrderFlowStatus, record) => (
          <Tag
            color={StoreOrderStatusColorMap[value] || 'default'}
            style={{ cursor: value === 1 || value === 2 ? 'pointer' : 'default' }}
            onClick={() => handleStatusToggle(record)}
          >
            {StoreOrderStatusLabelMap[value] || `状态 ${value}`}
          </Tag>
        ),
      },
      {
        title: '订货数量',
        dataIndex: 'totalQuantity',
        width: 110,
        sorter: true,
      },
      {
        title: '订货金额',
        dataIndex: 'totalOrderAmount',
        width: 120,
        sorter: true,
        render: (value: number) => formatAmount(value),
      },
      {
        title: '订货体积',
        dataIndex: 'totalOrderVolume',
        width: 120,
        render: (value: number | undefined) => formatVolume(value),
      },
      {
        title: '发货体积',
        dataIndex: 'totalAllocVolume',
        width: 120,
        render: (value: number | undefined) => formatVolume(value),
      },
      {
        title: '发货数量',
        dataIndex: 'totalAllocQuantity',
        width: 110,
        sorter: true,
      },
      {
        title: '发货金额',
        dataIndex: 'importTotalAmount',
        width: 120,
        sorter: true,
        render: (value: number) => formatAmount(value),
      },
      
      {
        title: '备注',
        dataIndex: 'remarks',
        width: 220,
        ellipsis: true,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: '创建时间',
        dataIndex: 'createdAt',
        width: 180,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: '更新人',
        dataIndex: 'updatedBy',
        width: 140,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: '更新时间',
        dataIndex: 'updatedAt',
        width: 180,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: '操作',
        key: 'action',
        fixed: 'right',
        width: 170,
        render: (_, record) => (
          <Space size={0}>
            <Button type="link" onClick={() => openDetail(record)}>
              查看
            </Button>
            {access.canDeleteOrder ? (
              <Popconfirm
                title={`确认删除订单 ${record.orderNo} 吗？`}
                okText="删除"
                cancelText="取消"
                onConfirm={async () => {
                  try {
                    await deleteStoreOrder(record.orderGUID)
                    message.success('删除成功')
                    void loadData({ pageNumber: 1 })
                  } catch (error) {
                    console.error(error)
                    message.error(error instanceof Error ? error.message : '删除失败')
                  }
                }}
              >
                <Button danger type="link">
                  删除
                </Button>
              </Popconfirm>
            ) : null}
          </Space>
        ),
      },
    ],
    [access.canDeleteOrder, branchMap, page, pageSize],
  )

  return (
    <PageContainer
      title="分店订货列表"
      subtitle="首版先迁列表主链：筛选、状态、门店多选、新建、复制、批量状态和跳转明细 Tab。"
      extra={
        <Space wrap>
          {access.canManageWarehouse ? (
            <Button
              icon={<SyncOutlined />}
              loading={syncLoading}
              disabled={syncLoading}
              onClick={() => void handleSyncMissingOrders()}
            >
              同步订单
            </Button>
          ) : null}
          <Button
            type="primary"
            icon={<PlusOutlined />}
            disabled={!access.canWriteOrder}
            onClick={() => setStorePickerOpen(true)}
          >
            新建订单
          </Button>
          <Button
            icon={<CopyOutlined />}
            disabled={!selectedRowKeys.length}
            onClick={() => setCopyModalOpen(true)}
          >
            复制订单 ({selectedRowKeys.length})
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setKeyword('')
              setDateRange(null)
              setSelectedStoreCodes([])
              setStatusList([1 as StoreOrderFlowStatus, 2 as StoreOrderFlowStatus])
              setSortField('orderDate')
              setSortOrder('descend')
              void loadData({
                keyword: undefined,
                startDate: undefined,
                endDate: undefined,
                storeCodes: undefined,
                statusList: [1 as StoreOrderFlowStatus, 2 as StoreOrderFlowStatus],
                pageNumber: 1,
                pageSize,
                sortBy: 'orderDate',
                sortDescending: true,
              })
            }}
          >
            重置
          </Button>
          <Button
            disabled={!selectedRowKeys.length}
            onClick={() => handleBatchStatusChange(1 as StoreOrderFlowStatus)}
          >
            批量改已提交
          </Button>
          <Button
            disabled={!selectedRowKeys.length}
            onClick={() => handleBatchStatusChange(2 as StoreOrderFlowStatus)}
          >
            批量改已完成
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
            placeholder="搜索订单号 / 分店编码"
            onChange={(event) => setKeyword(event.target.value)}
          />
          <DatePicker.RangePicker value={dateRange} onChange={(value) => setDateRange(value)} />
          <Select
            mode="multiple"
            value={selectedStoreCodes}
            allowClear
            style={{ width: 280 }}
            placeholder="全部分店"
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
            placeholder="全部状态"
            options={StoreOrderStatusOptions.map((item) => ({
              value: item.value,
              label: item.label,
            }))}
            onChange={(value) => setStatusList(value)}
          />
          <Button type="primary" onClick={() => void loadData({ pageNumber: 1 })}>
            查询
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
        title="选择分店创建订单"
        loading={creating}
        onCancel={() => setStorePickerOpen(false)}
        onSelect={async (store) => {
          setCreating(true)
          try {
            const orderGuid = await createStoreOrder({ storeCode: store.storeCode })
            message.success(`已为 ${store.storeName} 创建新订单`)
            setStorePickerOpen(false)
            navigate(`/warehouse/store-order/detail/${orderGuid}`)
            void loadData({ pageNumber: 1 })
          } catch (error) {
            console.error(error)
            message.error(error instanceof Error ? error.message : '创建订单失败')
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
            message.warning('请先选择要复制的订单')
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

            message.success(orderNo ? `订单复制成功：${orderNo}` : '订单复制成功')
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
            message.error(error instanceof Error ? error.message : '复制订单失败')
          } finally {
            setCopying(false)
          }
        }}
      />
    </PageContainer>
  )
}
