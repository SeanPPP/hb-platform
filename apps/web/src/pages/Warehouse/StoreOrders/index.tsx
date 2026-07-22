import {
  CopyOutlined,
  DatabaseOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SyncOutlined,
  ToolOutlined,
} from '@ant-design/icons'
import {
  DndContext,
  PointerSensor,
  closestCenter,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  SortableContext,
  horizontalListSortingStrategy,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { FilterDropdownProps, SorterResult } from 'antd/es/table/interface'
import {
  App as AntdApp,
  Button,
  Card,
  Checkbox,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type HTMLAttributes, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { refreshSession } from '../../../services/auth'
import {
  batchMapStoreOrderStoreCode,
  batchUpdateStoreOrderStatus,
  createStoreOrderFullHqSyncJob,
  createStoreOrderIncrementalHqSyncJob,
  copyStoreOrder,
  createStoreOrder,
  deleteStoreOrder,
  getStoreOrderHqSyncJob,
  getStoreOrderList,
  getUnmatchedStoreOrderGroups,
  getUsedStoreOrderBranches,
  updateStoreOrderOutboundDate,
  updateStoreOrderStatus,
} from '../../../services/storeOrderService'
import { createStore, getNextStoreCode, getStores } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type { CreateStoreDto, StoreDto } from '../../../types/store'
import type {
  CopyStoreOrderPayload,
  StoreOrderBranchOption,
  StoreOrderHqSyncPayload,
  StoreOrderSyncConflictStrategy,
  StoreOrderFlowStatus,
  StoreOrderListColumnFilters,
  StoreOrderListItem,
  StoreOrderListQuery,
  UnmatchedStoreOrderGroup,
} from '../../../types/storeOrder'
import {
  StoreOrderFlowStatus as FlowStatus,
  StoreOrderStatusColorMap,
} from '../../../types/storeOrder'
import { getDateTagColor } from '../../../utils/tagColors'
import { getStoreColor } from '../../../utils/userTableColors'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { RequestError } from '../../../utils/request'
import { createLatestRequestGuard, runLatestGuardedRequest } from '../../../utils/latestRequestGuard'
import {
  ensureStoreOrderSyncSession,
  STORE_ORDER_SYNC_AUTH_EXPIRED_MESSAGE,
} from './syncSessionGuard'
import {
  createStoreOrderSyncJobPoller,
  StoreOrderSyncPollingCancelledError,
  StoreOrderSyncPollingTimeoutError,
} from './syncJobPolling'
import {
  isStoreOrderListColumnOrderCustomized,
  mergeStoreOrderListColumnOrder,
  moveStoreOrderListColumnOrder,
  type StoreOrderListTableColumnKey,
} from './columnOrder'
import { formatStoreOrderVolume } from './volumeFormat'
import './compact.css'

type RangeValue = [Dayjs | null, Dayjs | null] | null
type StoreOrderListTextFilterKey = 'orderNo' | 'remarks' | 'updatedBy'
type StoreOrderListDateStartKey = 'outboundDateStart' | 'createdAtStart' | 'updatedAtStart'
type StoreOrderListDateEndKey = 'outboundDateEnd' | 'createdAtEnd' | 'updatedAtEnd'
type StoreOrderListNumberRange = {
  min: keyof StoreOrderListColumnFilters
  max: keyof StoreOrderListColumnFilters
}
type StoreCashRegisterFilter = 'all' | 'enabled' | 'disabled'
const UNMATCHED_TARGET_STORE_PAGE_SIZE = 500

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

function renderStoreOrderNumericCell(value: ReactNode) {
  return <span className="store-order-numeric-cell">{value}</span>
}

function renderStoreOrderTwoLineText(value?: string) {
  if (!value) {
    return <>--</>
  }
  return <span className="store-order-two-line-text" title={value}>{value}</span>
}

function renderDateTag(value?: string, language?: string) {
  const displayValue = formatDate(value, language)
  if (displayValue === '--') {
    return '--'
  }

  return <Tag className="store-order-nowrap" color={getDateTagColor(displayValue)}>{displayValue}</Tag>
}

function normalizeStoreFilterText(value: unknown) {
  return String(value ?? '').trim().toLowerCase()
}

function filterStoreOption(input: string, option?: { label?: unknown; value?: unknown }) {
  const keyword = normalizeStoreFilterText(input)
  if (!keyword) {
    return true
  }

  return (
    normalizeStoreFilterText(option?.label).includes(keyword)
    || normalizeStoreFilterText(option?.value).includes(keyword)
  )
}

function getApiErrorCode(error: unknown) {
  if (!(error instanceof RequestError)) {
    return undefined
  }
  const payload = error.payload
  return typeof payload === 'object' && payload !== null && 'errorCode' in payload
    ? String(payload.errorCode)
    : undefined
}

function buildUnmatchedTargetStoreLabel(store: StoreDto) {
  const storeName = store.storeName || store.storeCode
  const displayName = store.isActive ? storeName : `${storeName}（停用）`
  const labelParts = [
    `${store.storeCode} - ${displayName}`,
    store.brandName?.trim(),
    store.address?.trim(),
  ].filter(Boolean)

  return labelParts.join('｜')
}

async function loadAllUnmatchedTargetStores() {
  const stores: StoreDto[] = []
  let page = 1

  while (true) {
    const result = await getStores({
      page,
      pageSize: UNMATCHED_TARGET_STORE_PAGE_SIZE,
      sortField: 'storeName',
      sortOrder: 'ascend',
    })
    stores.push(...result.items)

    if (stores.length >= result.total || result.items.length < UNMATCHED_TARGET_STORE_PAGE_SIZE) {
      break
    }

    page += 1
  }

  return stores
}

function hasStoreOrderListQueryOverride(
  overrides: Partial<StoreOrderListQuery>,
  key: keyof StoreOrderListQuery,
) {
  return Object.prototype.hasOwnProperty.call(overrides, key)
}

function cleanStoreOrderListColumnFilters(
  filters?: StoreOrderListColumnFilters,
): StoreOrderListColumnFilters | undefined {
  if (!filters) {
    return undefined
  }

  const next: StoreOrderListColumnFilters = {}
  const assignText = (key: StoreOrderListTextFilterKey) => {
    const value = filters[key]?.trim()
    if (value) {
      next[key] = value
    }
  }
  const assignDate = (key: StoreOrderListDateStartKey | StoreOrderListDateEndKey) => {
    const value = filters[key]
    if (value) {
      next[key] = value
    }
  }
  const assignNumber = (key: keyof StoreOrderListColumnFilters) => {
    const value = filters[key]
    if (typeof value === 'number' && Number.isFinite(value)) {
      next[key] = value as never
    }
  }

  assignText('orderNo')
  assignText('remarks')
  assignText('updatedBy')
  assignDate('outboundDateStart')
  assignDate('outboundDateEnd')
  assignDate('createdAtStart')
  assignDate('createdAtEnd')
  assignDate('updatedAtStart')
  assignDate('updatedAtEnd')
  assignNumber('totalQuantityMin')
  assignNumber('totalQuantityMax')
  assignNumber('totalOrderAmountMin')
  assignNumber('totalOrderAmountMax')
  assignNumber('totalOrderVolumeMin')
  assignNumber('totalOrderVolumeMax')
  assignNumber('totalAllocVolumeMin')
  assignNumber('totalAllocVolumeMax')
  assignNumber('totalAllocQuantityMin')
  assignNumber('totalAllocQuantityMax')
  assignNumber('importTotalAmountMin')
  assignNumber('importTotalAmountMax')

  return Object.keys(next).length ? next : undefined
}

const DEFAULT_INCREMENTAL_CONFLICT_STRATEGY: StoreOrderSyncConflictStrategy = 'LatestWins'
const DEFAULT_STATUS_LIST = [FlowStatus.Submitted, FlowStatus.Picking]
const STATUS_FILTER_ORDER = [FlowStatus.Submitted, FlowStatus.Picking, FlowStatus.Completed]
const STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.storeOrders.list.columnOrder.v1'

interface DraggableHeaderCellProps extends HTMLAttributes<HTMLTableCellElement> {
  'data-column-key'?: string
}

function DraggableHeaderCell({ children, style, ...props }: DraggableHeaderCellProps) {
  const columnKey = props['data-column-key']
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: columnKey ?? '__store-order-list-static-column__',
    disabled: !columnKey,
  })

  if (!columnKey) {
    return <th style={style} {...props}>{children}</th>
  }

  const headerStyle: CSSProperties = {
    ...style,
    transform: CSS.Translate.toString(transform),
    transition,
    cursor: 'move',
    zIndex: isDragging ? 3 : style?.zIndex,
    opacity: isDragging ? 0.85 : style?.opacity,
  }

  return (
    <th ref={setNodeRef} style={headerStyle} {...props} {...attributes} {...listeners}>
      <div className="store-order-list-draggable-header">
        {children}
      </div>
    </th>
  )
}

function StorePickerModal({ open, title, loading, onCancel, onSelect }: StorePickerModalProps) {
  const { t } = useTranslation()
  const { message } = AntdApp.useApp()
  const [createForm] = Form.useForm<CreateStoreDto>()
  const [stores, setStores] = useState<StoreDto[]>([])
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [cashRegisterFilter, setCashRegisterFilter] = useState<StoreCashRegisterFilter>('all')
  const [createOpen, setCreateOpen] = useState(false)
  const [createSaving, setCreateSaving] = useState(false)
  const [storeCodeLoading, setStoreCodeLoading] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

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
          ...(cashRegisterFilter === 'all' ? {} : { isActive: cashRegisterFilter === 'enabled' }),
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
  }, [cashRegisterFilter, keyword, message, open, reloadToken, t])

  const resetCreateForm = () => {
    createForm.resetFields()
    createForm.setFieldsValue({ isActive: false })
  }

  const loadNextStoreCode = async () => {
    setStoreCodeLoading(true)
    try {
      const nextCode = await getNextStoreCode()
      createForm.setFieldsValue({ storeCode: nextCode })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.loadNextStoreCodeFailed'))
    } finally {
      setStoreCodeLoading(false)
    }
  }

  const handleClose = () => {
    setKeyword('')
    setCashRegisterFilter('all')
    setCreateOpen(false)
    resetCreateForm()
    onCancel()
  }

  const handleOpenCreate = () => {
    setCreateOpen(true)
    resetCreateForm()
    void loadNextStoreCode()
  }

  const handleCreateStore = async () => {
    try {
      const values = await createForm.validateFields()
      setCreateSaving(true)
      // 创建订单弹窗中新建的分店默认不启用收银系统，避免误进入 POS 侧业务范围。
      const created = await createStore({ ...values, isActive: values.isActive ?? false })
      message.success(t('storeOrders.createStoreSuccess'))
      setCreateOpen(false)
      resetCreateForm()
      setReloadToken((current) => current + 1)
      onSelect(created)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(
        getApiErrorCode(error) === 'DUPLICATE_STORE_CODE'
          ? t('storeOrders.duplicateStoreCode')
          : error instanceof Error ? error.message : t('storeOrders.createStoreFailed'),
      )
    } finally {
      setCreateSaving(false)
    }
  }

  return (
    <Modal
      title={title}
      open={open}
      width={860}
      footer={null}
      destroyOnHidden
      onCancel={handleClose}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space wrap style={{ width: '100%', justifyContent: 'space-between' }}>
          <Space wrap>
            <Input
              value={keyword}
              allowClear
              placeholder={t('storeOrders.searchStorePlaceholder')}
              prefix={<SearchOutlined />}
              style={{ width: 260 }}
              onChange={(event) => setKeyword(event.target.value)}
            />
            <Radio.Group
              value={cashRegisterFilter}
              onChange={(event) => setCashRegisterFilter(event.target.value)}
            >
              <Radio.Button value="all">{t('storeOrders.storeCashRegisterAll')}</Radio.Button>
              <Radio.Button value="enabled">{t('storeOrders.storeCashRegisterEnabled')}</Radio.Button>
              <Radio.Button value="disabled">{t('storeOrders.storeCashRegisterDisabled')}</Radio.Button>
            </Radio.Group>
          </Space>
          <Button icon={<PlusOutlined />} onClick={handleOpenCreate}>
            {t('storeOrders.createStore')}
          </Button>
        </Space>

        {createOpen ? (
          <Form
            form={createForm}
            layout="vertical"
            initialValues={{ isActive: false }}
            style={{ padding: 12, border: '1px solid #f0f0f0', borderRadius: 6 }}
          >
            <Space direction="vertical" size={0} style={{ width: '100%' }}>
              <Space wrap style={{ width: '100%' }}>
                <Form.Item
                  label={t('system.stores.storeName')}
                  name="storeName"
                  rules={[
                    { required: true, message: t('system.stores.storeNameRequired') },
                    { max: 100, message: t('system.stores.storeNameMaxLength') },
                  ]}
                  style={{ width: 250 }}
                >
                  <Input />
                </Form.Item>
                <Form.Item
                  label={t('system.stores.storeCode')}
                  name="storeCode"
                  rules={[
                    { required: true, message: t('system.stores.storeCodeRequired') },
                    { max: 20, message: t('system.stores.storeCodeMaxLength') },
                  ]}
                  style={{ width: 250 }}
                >
                  <Input
                    addonAfter={(
                      <Button
                        type="link"
                        size="small"
                        icon={<ReloadOutlined />}
                        loading={storeCodeLoading}
                        onClick={() => void loadNextStoreCode()}
                      >
                        {t('storeOrders.regenerateStoreCode')}
                      </Button>
                    )}
                  />
                </Form.Item>
                <Form.Item
                  label={t('system.stores.brandName')}
                  name="brandName"
                  rules={[{ max: 100, message: t('system.stores.brandNameMaxLength') }]}
                  style={{ width: 180 }}
                >
                  <Input />
                </Form.Item>
                <Form.Item
                  label={t('system.stores.cashRegisterEnabled')}
                  name="isActive"
                  valuePropName="checked"
                  style={{ width: 160 }}
                >
                  <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
                </Form.Item>
              </Space>
              <Space wrap style={{ width: '100%' }}>
                <Form.Item
                  label={t('system.stores.contactPhone')}
                  name="contactPhone"
                  rules={[{ max: 20, message: t('system.stores.contactPhoneMaxLength') }]}
                  style={{ width: 200 }}
                >
                  <Input />
                </Form.Item>
                <Form.Item
                  label={t('system.stores.contactEmail')}
                  name="contactEmail"
                  rules={[{ type: 'email', message: t('system.users.emailInvalid') }]}
                  style={{ width: 260 }}
                >
                  <Input />
                </Form.Item>
                <Form.Item
                  label={t('system.stores.address')}
                  name="address"
                  rules={[{ max: 200, message: t('system.stores.addressMaxLength') }]}
                  style={{ width: 320 }}
                >
                  <Input />
                </Form.Item>
              </Space>
              <Form.Item
                label={t('column.description')}
                name="description"
                rules={[{ max: 500, message: t('system.stores.descriptionMaxLength') }]}
              >
                <Input.TextArea rows={2} />
              </Form.Item>
              <Space>
                <Button
                  type="primary"
                  loading={createSaving}
                  onClick={() => void handleCreateStore()}
                >
                  {t('common.confirm')}
                </Button>
                <Button
                  disabled={createSaving}
                  onClick={() => {
                    setCreateOpen(false)
                    resetCreateForm()
                  }}
                >
                  {t('common.cancel')}
                </Button>
              </Space>
            </Space>
          </Form>
        ) : null}

        <Table
          rowKey="storeGUID"
          loading={fetching || loading || createSaving}
          size="small"
          pagination={false}
          dataSource={stores}
          scroll={{ y: 360 }}
          columns={[
            {
              title: t('common.index'),
              key: 'rowIndex',
              width: 64,
              render: (_value, _record, index) => index + 1,
            },
            { title: t('column.storeName'), dataIndex: 'storeName' },
            { title: t('column.storeCode'), dataIndex: 'storeCode', width: 140 },
            {
              title: t('system.stores.cashRegisterEnabled'),
              dataIndex: 'isActive',
              width: 150,
              render: (value: boolean) => (
                <Tag color={value ? 'success' : 'default'}>
                  {value ? t('common.active') : t('common.inactive')}
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
  const isWarehouseStaffOnly =
    access.isWarehouseStaff &&
    !access.isAdmin &&
    !access.isWarehouseManager &&
    (access.hasRole('WarehouseStaff') || access.hasRole('仓库员工'))
  // 分店订货管理动作跟随 Warehouse.ManageOrders 权限；纯 WarehouseStaff 仅保留只读文档入口。
  const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly
  const canCreateStoreOrder = access.canWriteOrder || canUseWarehouseManagerActions
  const canDeleteStoreOrder = access.canDeleteOrder || canUseWarehouseManagerActions

  const [loading, setLoading] = useState(false)
  const [creating, setCreating] = useState(false)
  const [copying, setCopying] = useState(false)
  const [data, setData] = useState<StoreOrderListItem[]>([])
  const [branches, setBranches] = useState<StoreOrderBranchOption[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [keyword, setKeyword] = useState('')
  const [dateRange, setDateRange] = useState<RangeValue>(null)
  const [selectedStoreCodes, setSelectedStoreCodes] = useState<string[]>([])
  const [statusList, setStatusList] = useState<StoreOrderFlowStatus[]>(DEFAULT_STATUS_LIST)
  const [columnFilters, setColumnFilters] = useState<StoreOrderListColumnFilters>({})
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
  const [shippingOrder, setShippingOrder] = useState<StoreOrderListItem | null>(null)
  const [shippingDate, setShippingDate] = useState<Dayjs>(() => dayjs())
  const [shippingLoading, setShippingLoading] = useState(false)
  const [unmatchedStoreOpen, setUnmatchedStoreOpen] = useState(false)
  const [unmatchedStoreLoading, setUnmatchedStoreLoading] = useState(false)
  const [unmatchedStoreSaving, setUnmatchedStoreSaving] = useState(false)
  const [unmatchedStoreGroups, setUnmatchedStoreGroups] = useState<UnmatchedStoreOrderGroup[]>([])
  const [unmatchedStoreTargets, setUnmatchedStoreTargets] = useState<Record<string, string>>({})
  const [unmatchedTargetStores, setUnmatchedTargetStores] = useState<StoreDto[]>([])
  const [columnOrder, setColumnOrder] = useState<StoreOrderListTableColumnKey[]>([])
  // 记录当前轮询停止函数，确保重复触发和页面卸载时都能清理定时器。
  const stopSyncPollingRef = useRef<(() => void) | null>(null)
  // 避免卸载后继续 setState，防止轮询尾声触发无效更新。
  const isMountedRef = useRef(true)
  const loadDataRef = useRef<((
    overrides?: Partial<StoreOrderListQuery & { pageNumber: number; pageSize: number }>,
  ) => Promise<void>) | null>(null)
  const listRequestGuardRef = useRef(createLatestRequestGuard())

  const branchMap = useMemo(
    () => Object.fromEntries(branches.map((item) => [item.code, item.name])) as Record<string, string>,
    [branches],
  )

  const storeFilterOptions = useMemo(
    () =>
      [...branches]
        .sort((left, right) => {
          const nameCompare = (left.name || '').localeCompare(right.name || '', 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })
          return nameCompare || left.code.localeCompare(right.code, 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })
        })
        .map((item) => ({
          value: item.code,
          label: `${item.code} - ${item.name}`,
        })),
    [branches],
  )

  const unmatchedTargetStoreOptions = useMemo(
    () =>
      [...unmatchedTargetStores]
        .sort((left, right) => {
          const nameCompare = (left.storeName || '').localeCompare(right.storeName || '', 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })
          return nameCompare || left.storeCode.localeCompare(right.storeCode, 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })
        })
        .map((item) => ({
          value: item.storeCode,
          // 展示品牌和地址用于人工确认 GUID 对应分店，保存时仍只提交目标 StoreCode。
          label: buildUnmatchedTargetStoreLabel(item),
          title: buildUnmatchedTargetStoreLabel(item),
        })),
    [unmatchedTargetStores],
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
      STATUS_FILTER_ORDER.map((status) => ({
        value: status,
        label: statusLabelMap[status],
      })),
    [statusLabelMap],
  )

  const buildQuery = (
    overrides: Partial<StoreOrderListQuery & { pageNumber: number; pageSize: number }> = {},
  ): StoreOrderListQuery => ({
    keyword: hasStoreOrderListQueryOverride(overrides, 'keyword') ? overrides.keyword : keyword || undefined,
    storeCodes: hasStoreOrderListQueryOverride(overrides, 'storeCodes')
      ? overrides.storeCodes
      : selectedStoreCodes.length ? selectedStoreCodes : undefined,
    startDate: hasStoreOrderListQueryOverride(overrides, 'startDate')
      ? overrides.startDate
      : dateRange?.[0]?.startOf('day').toISOString(),
    endDate: hasStoreOrderListQueryOverride(overrides, 'endDate')
      ? overrides.endDate
      : dateRange?.[1]?.endOf('day').toISOString(),
    statusList: hasStoreOrderListQueryOverride(overrides, 'statusList')
      ? overrides.statusList
      : statusList.length ? statusList : undefined,
    columnFilters: cleanStoreOrderListColumnFilters(
      hasStoreOrderListQueryOverride(overrides, 'columnFilters')
        ? overrides.columnFilters
        : columnFilters,
    ),
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
    if (!isMountedRef.current) {
      return
    }
    const query = buildQuery(overrides)

    await runLatestGuardedRequest(listRequestGuardRef.current, () => getStoreOrderList(query), {
      onStart: () => setLoading(true),
      onSuccess: (result) => {
        setData(result.items)
        setTotal(result.total)
        setPage(result.page)
        setPageSize(result.pageSize)
        setSelectedRowKeys([])
      },
      onError: (error) => {
        console.error(error)
        message.error(error instanceof Error ? error.message : t('storeOrders.loadListFailed'))
      },
      onSettled: () => setLoading(false),
    })
  }

  useLayoutEffect(() => {
    loadDataRef.current = loadData
  })

  const refreshCurrentList = useCallback((
    overrides: Partial<StoreOrderListQuery & { pageNumber: number; pageSize: number }> = {},
  ) => {
    if (!isMountedRef.current) {
      return Promise.resolve()
    }
    return loadDataRef.current?.(overrides) ?? Promise.resolve()
  }, [])

  const loadUnmatchedStoreGroups = async () => {
    setUnmatchedStoreLoading(true)
    try {
      const [groups, stores] = await Promise.all([
        getUnmatchedStoreOrderGroups(),
        loadAllUnmatchedTargetStores(),
      ])
      setUnmatchedStoreGroups(groups)
      setUnmatchedTargetStores(stores)
      setUnmatchedStoreTargets((current) => {
        const availableSources = new Set(groups.map((item) => item.sourceStoreCode))
        return Object.fromEntries(
          Object.entries(current).filter(([sourceStoreCode]) => availableSources.has(sourceStoreCode)),
        )
      })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.loadUnmatchedStoresFailed', '加载未匹配分店失败'))
    } finally {
      setUnmatchedStoreLoading(false)
    }
  }

  const openUnmatchedStoreModal = () => {
    setUnmatchedStoreOpen(true)
    void loadUnmatchedStoreGroups()
  }

  const closeUnmatchedStoreModal = () => {
    setUnmatchedStoreOpen(false)
    setUnmatchedStoreTargets({})
  }

  const handleSaveUnmatchedStoreMappings = async () => {
    const mappings = unmatchedStoreGroups
      .map((group) => ({
        sourceStoreCode: group.sourceStoreCode,
        targetStoreCode: unmatchedStoreTargets[group.sourceStoreCode],
      }))
      .filter((item) => item.targetStoreCode)

    if (!mappings.length) {
      message.warning(t('storeOrders.selectStoreMappingsFirst', '请至少选择一个目标分店'))
      return
    }

    setUnmatchedStoreSaving(true)
    try {
      const result = await batchMapStoreOrderStoreCode({ mappings })
      message.success(
        t('storeOrders.fixStoreGuidSuccess', {
          updated: result.updatedCount ?? 0,
          skipped: result.skippedCount ?? 0,
        }),
      )
      await Promise.all([refreshCurrentList(), loadBranches(), loadUnmatchedStoreGroups()])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.fixStoreGuidFailed', '修复分店 GUID 失败'))
    } finally {
      setUnmatchedStoreSaving(false)
    }
  }

  const updateColumnFilters = (patch: StoreOrderListColumnFilters) => {
    setColumnFilters((current) => ({ ...current, ...patch }))
  }

  const applyColumnFilters = (
    confirm: FilterDropdownProps['confirm'],
    nextFilters: StoreOrderListColumnFilters = columnFilters,
  ) => {
    confirm()
    void loadData({ pageNumber: 1, columnFilters: nextFilters })
  }

  const clearColumnFilter = (
    keys: Array<keyof StoreOrderListColumnFilters>,
    confirm: FilterDropdownProps['confirm'],
  ) => {
    const nextFilters = { ...columnFilters }
    keys.forEach((key) => {
      delete nextFilters[key]
    })
    setColumnFilters(nextFilters)
    applyColumnFilters(confirm, nextFilters)
  }

  const applyTopFilter = (
    confirm: FilterDropdownProps['confirm'],
    overrides: Partial<StoreOrderListQuery>,
  ) => {
    confirm()
    void loadData({ ...overrides, pageNumber: 1 })
  }

  const getColumnDateRange = (
    startKey: StoreOrderListDateStartKey,
    endKey: StoreOrderListDateEndKey,
  ): RangeValue => [
    columnFilters[startKey] ? dayjs(columnFilters[startKey]) : null,
    columnFilters[endKey] ? dayjs(columnFilters[endKey]) : null,
  ]

  const setColumnDateRange = (
    startKey: StoreOrderListDateStartKey,
    endKey: StoreOrderListDateEndKey,
    value: RangeValue,
  ) => {
    updateColumnFilters({
      [startKey]: value?.[0]?.startOf('day').toISOString(),
      [endKey]: value?.[1]?.endOf('day').toISOString(),
    })
  }

  const makeTextFilterDropdown = (
    key: StoreOrderListTextFilterKey,
    placeholder: string,
  ) => ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <Input
        value={columnFilters[key] ?? ''}
        allowClear
        placeholder={placeholder}
        onChange={(event) => updateColumnFilters({ [key]: event.target.value })}
        onPressEnter={() => applyColumnFilters(confirm)}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => clearColumnFilter([key], confirm)}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeDateRangeFilterDropdown = (
    startKey: StoreOrderListDateStartKey,
    endKey: StoreOrderListDateEndKey,
  ) => ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <DatePicker.RangePicker
        value={getColumnDateRange(startKey, endKey)}
        onChange={(value) => setColumnDateRange(startKey, endKey, value)}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => clearColumnFilter([startKey, endKey], confirm)}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeNumberRangeFilterDropdown = (
    range: StoreOrderListNumberRange,
  ) => ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <Space.Compact>
        <InputNumber
          value={columnFilters[range.min] as number | undefined}
          placeholder={t('containers.placeholders.minValue', '最小值')}
          controls={false}
          onChange={(value) => updateColumnFilters({ [range.min]: value == null ? undefined : Number(value) })}
        />
        <InputNumber
          value={columnFilters[range.max] as number | undefined}
          placeholder={t('containers.placeholders.maxValue', '最大值')}
          controls={false}
          onChange={(value) => updateColumnFilters({ [range.max]: value == null ? undefined : Number(value) })}
        />
      </Space.Compact>
      <Space>
        <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => clearColumnFilter([range.min, range.max], confirm)}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeStoreFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <Select
        mode="multiple"
        value={selectedStoreCodes}
        allowClear
        showSearch
        style={{ minWidth: 240 }}
        placeholder={t('storeOrders.allStores')}
        optionFilterProp="label"
        filterOption={filterStoreOption}
        options={storeFilterOptions}
        onChange={(value) => setSelectedStoreCodes(value)}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => applyTopFilter(confirm, { storeCodes: selectedStoreCodes.length ? selectedStoreCodes : undefined })}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => {
          setSelectedStoreCodes([])
          applyTopFilter(confirm, { storeCodes: undefined })
        }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeOrderDateFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <DatePicker.RangePicker value={dateRange} onChange={(value) => setDateRange(value)} />
      <Space>
        <Button size="small" type="primary" onClick={() => applyTopFilter(confirm, {
          startDate: dateRange?.[0]?.startOf('day').toISOString(),
          endDate: dateRange?.[1]?.endOf('day').toISOString(),
        })}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => {
          setDateRange(null)
          applyTopFilter(confirm, { startDate: undefined, endDate: undefined })
        }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeStatusFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div className="store-order-list-column-filter" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
      <Checkbox.Group
        value={statusList}
        options={statusOptions}
        onChange={(value) => setStatusList(value as StoreOrderFlowStatus[])}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => applyTopFilter(confirm, { statusList: statusList.length ? statusList : undefined })}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => {
          setStatusList(DEFAULT_STATUS_LIST)
          applyTopFilter(confirm, { statusList: DEFAULT_STATUS_LIST })
        }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const filterIcon = (active?: boolean) => <SearchOutlined style={{ color: active ? '#1677ff' : undefined }} />
  const hasNumberRangeFilter = (range: StoreOrderListNumberRange) => (
    typeof columnFilters[range.min] === 'number' || typeof columnFilters[range.max] === 'number'
  )
  const textFilterProps = (key: StoreOrderListTextFilterKey, placeholder: string) => ({
    filterDropdown: makeTextFilterDropdown(key, placeholder),
    filterIcon,
    filtered: Boolean(columnFilters[key]?.trim()),
  })
  const dateRangeFilterProps = (
    startKey: StoreOrderListDateStartKey,
    endKey: StoreOrderListDateEndKey,
  ) => ({
    filterDropdown: makeDateRangeFilterDropdown(startKey, endKey),
    filterIcon,
    filtered: Boolean(columnFilters[startKey] || columnFilters[endKey]),
  })
  const numberFilterProps = (range: StoreOrderListNumberRange) => ({
    filterDropdown: makeNumberRangeFilterDropdown(range),
    filterIcon,
    filtered: hasNumberRangeFilter(range),
  })

  useEffect(() => {
    void Promise.all([loadData({ pageNumber: 1 }), loadBranches()])
  }, [])

  useLayoutEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
      listRequestGuardRef.current.invalidate()
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

      void refreshCurrentList()
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
    if (!canUseWarehouseManagerActions) {
      return
    }

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
          void refreshCurrentList()
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
          void refreshCurrentList()
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

  const openShippingModal = (record: StoreOrderListItem) => {
    setShippingOrder(record)
    setShippingDate(record.outboundDate ? dayjs(record.outboundDate) : dayjs())
  }

  const closeShippingModal = () => {
    setShippingOrder(null)
    setShippingDate(dayjs())
  }

  const handleConfirmShipping = async () => {
    if (!shippingOrder) {
      return
    }

    setShippingLoading(true)
    try {
      await updateStoreOrderOutboundDate({
        orderGUID: shippingOrder.orderGUID,
        outboundDate: shippingDate.format('YYYY-MM-DD'),
        completeOrder: true,
      })
      message.success(t('storeOrders.shipOrderSuccess'))
      closeShippingModal()
      void refreshCurrentList()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.shipOrderFailed'))
    } finally {
      setShippingLoading(false)
    }
  }

  const columnDragSensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 4,
      },
    }),
  )

  const baseColumns = useMemo<ColumnsType<StoreOrderListItem>>(
    () => [
      {
        key: 'index',
        title: t('column.index'),
        dataIndex: 'index',
        width: 52,
        fixed: 'left',
        render: (_, __, index) => renderStoreOrderNumericCell((page - 1) * pageSize + index + 1),
      },
      {
        key: 'orderNo',
        title: t('column.orderNo'),
        dataIndex: 'orderNo',
        width: 146,
        sorter: true,
        ...textFilterProps('orderNo', t('storeOrders.filterOrderNo', '过滤订单号')),
        fixed: 'left',
        render: (value: string, record) => (
          <Space size={4} wrap={false} className="store-order-list-order-cell">
            <Button type="link" className="store-order-list-order-no" onClick={() => openDetail(record)}>
              {value}
            </Button>
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              className="store-order-copy-button"
              aria-label={`${t('common.copy')} ${value}`}
              onClick={() => void handleCopyOrderNo(value)}
            />
          </Space>
        ),
      },
      {
        key: 'storeCode',
        title: t('column.store'),
        dataIndex: 'storeCode',
        width: 170,
        sorter: true,
        filterDropdown: makeStoreFilterDropdown,
        filterIcon,
        filtered: selectedStoreCodes.length > 0,
        render: (value: string | undefined, record) => {
          const code = value || '--'
          const name = record.storeName || (value ? branchMap[value] : undefined)
          return (
            <Tag
              className="store-order-store-tag"
              color={getStoreColor(code)}
              title={name ? `${code} - ${name}` : code}
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
        key: 'orderDate',
        title: t('column.orderDate'),
        dataIndex: 'orderDate',
        width: 112,
        sorter: true,
        filterDropdown: makeOrderDateFilterDropdown,
        filterIcon,
        filtered: Boolean(dateRange?.[0] || dateRange?.[1]),
        render: (value: string | undefined) => renderDateTag(value, i18n.language),
      },
      {
        key: 'outboundDate',
        title: t('storeOrders.outboundDate'),
        dataIndex: 'outboundDate',
        width: 112,
        sorter: true,
        ...dateRangeFilterProps('outboundDateStart', 'outboundDateEnd'),
        render: (value: string | undefined) => renderDateTag(value, i18n.language),
      },
      {
        key: 'flowStatus',
        title: t('column.status'),
        dataIndex: 'flowStatus',
        width: 92,
        sorter: true,
        filterDropdown: makeStatusFilterDropdown,
        filterIcon,
        filtered: statusList.length > 0,
        render: (value: StoreOrderFlowStatus, record) => (
          <Tag
            color={StoreOrderStatusColorMap[value] || 'default'}
            style={{
              cursor:
                canUseWarehouseManagerActions &&
                (value === FlowStatus.Submitted || value === FlowStatus.Completed)
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
        key: 'totalQuantity',
        title: t('storeOrders.orderQuantity'),
        dataIndex: 'totalQuantity',
        width: 88,
        sorter: true,
        ...numberFilterProps({ min: 'totalQuantityMin', max: 'totalQuantityMax' }),
        render: (value: number | undefined) => renderStoreOrderNumericCell(value ?? '--'),
      },
      {
        key: 'totalOrderAmount',
        title: t('storeOrders.orderAmount'),
        dataIndex: 'totalOrderAmount',
        width: 92,
        sorter: true,
        ...numberFilterProps({ min: 'totalOrderAmountMin', max: 'totalOrderAmountMax' }),
        render: (value: number) => renderStoreOrderNumericCell(formatAmount(value)),
      },
      {
        key: 'totalOrderVolume',
        title: t('storeOrders.orderVolume'),
        dataIndex: 'totalOrderVolume',
        width: 92,
        ...numberFilterProps({ min: 'totalOrderVolumeMin', max: 'totalOrderVolumeMax' }),
        render: (value: number | undefined) => renderStoreOrderNumericCell(formatStoreOrderVolume(value)),
      },
      {
        key: 'totalAllocVolume',
        title: t('storeOrders.shipVolume'),
        dataIndex: 'totalAllocVolume',
        width: 92,
        ...numberFilterProps({ min: 'totalAllocVolumeMin', max: 'totalAllocVolumeMax' }),
        render: (value: number | undefined) => renderStoreOrderNumericCell(formatStoreOrderVolume(value)),
      },
      {
        key: 'totalAllocQuantity',
        title: t('storeOrders.shipQuantity'),
        dataIndex: 'totalAllocQuantity',
        width: 88,
        sorter: true,
        ...numberFilterProps({ min: 'totalAllocQuantityMin', max: 'totalAllocQuantityMax' }),
        render: (value: number | undefined) => renderStoreOrderNumericCell(value ?? '--'),
      },
      {
        key: 'importTotalAmount',
        title: t('storeOrders.shipAmount'),
        dataIndex: 'importTotalAmount',
        width: 92,
        sorter: true,
        ...numberFilterProps({ min: 'importTotalAmountMin', max: 'importTotalAmountMax' }),
        render: (value: number) => renderStoreOrderNumericCell(formatAmount(value)),
      },
      {
        key: 'remarks',
        title: t('common.remarks'),
        dataIndex: 'remarks',
        width: 170,
        ...textFilterProps('remarks', t('storeOrders.filterRemarks', '过滤备注')),
        render: (value: string | undefined) => renderStoreOrderTwoLineText(value),
      },
      {
        key: 'createdAt',
        title: t('column.createTime'),
        dataIndex: 'createdAt',
        width: 150,
        ...dateRangeFilterProps('createdAtStart', 'createdAtEnd'),
        render: (value: string | undefined) => <span className="store-order-nowrap">{formatDateTime(value, i18n.language)}</span>,
      },
      {
        key: 'updatedBy',
        title: t('column.updater'),
        dataIndex: 'updatedBy',
        width: 112,
        ...textFilterProps('updatedBy', t('storeOrders.filterUpdatedBy', '过滤更新人')),
        render: (value: string | undefined) => <span className="store-order-nowrap">{value || '--'}</span>,
      },
      {
        key: 'updatedAt',
        title: t('column.updateTime'),
        dataIndex: 'updatedAt',
        width: 150,
        ...dateRangeFilterProps('updatedAtStart', 'updatedAtEnd'),
        render: (value: string | undefined) => <span className="store-order-nowrap">{formatDateTime(value, i18n.language)}</span>,
      },
      {
        title: t('column.action'),
        key: 'action',
        fixed: 'right',
        width: 172,
        render: (_, record) => (
          <Space size={0} wrap={false}>
            {canUseWarehouseManagerActions && (record.flowStatus === FlowStatus.Submitted || record.flowStatus === FlowStatus.Picking) ? (
              <Button size="small" type="link" onClick={() => openShippingModal(record)}>
                {t('storeOrders.shipOrder')}
              </Button>
            ) : null}
            <Button size="small" type="link" onClick={() => openDetail(record)}>
              {t('common.view')}
            </Button>
            {canDeleteStoreOrder ? (
              <Popconfirm
                title={t('storeOrders.confirmDeleteOrder', { orderNo: record.orderNo })}
                okText={t('common.delete')}
                cancelText={t('common.cancel')}
                onConfirm={async () => {
                  try {
                    await deleteStoreOrder(record.orderGUID)
                    message.success(t('common.deleteSuccess'))
                    void refreshCurrentList({ pageNumber: 1 })
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
    [
      branchMap,
      canUseWarehouseManagerActions,
      canDeleteStoreOrder,
      columnFilters,
      dateRange,
      i18n.language,
      page,
      pageSize,
      refreshCurrentList,
      selectedStoreCodes,
      statusLabelMap,
      statusList,
      statusOptions,
      storeFilterOptions,
      t,
    ],
  )

  const draggableColumnKeys = baseColumns.map((column) => String(column.key) as StoreOrderListTableColumnKey)
  const isColumnOrderCustomized = isStoreOrderListColumnOrderCustomized(columnOrder, draggableColumnKeys)

  useEffect(() => {
    setColumnOrder((current) => {
      let savedOrder: unknown[] | null = null
      if (!current.length && typeof window !== 'undefined') {
        try {
          const raw = localStorage.getItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY)
          savedOrder = raw ? JSON.parse(raw) : null
        } catch {
          savedOrder = null
        }
      }

      // 列顺序只管理业务列；选择列继续由 rowSelection 管理，新增/删除列在这里自动兼容。
      const nextOrder = mergeStoreOrderListColumnOrder(current.length ? current : savedOrder, draggableColumnKeys)
      if (current.length === nextOrder.length && current.every((key, index) => key === nextOrder[index])) {
        return current
      }
      return nextOrder
    })
  }, [draggableColumnKeys.join('|')])

  const handleColumnDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    setColumnOrder((current) => {
      const nextOrder = moveStoreOrderListColumnOrder(current, active.id, over.id)
      try {
        localStorage.setItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY, JSON.stringify(nextOrder))
      } catch {
        // localStorage 不可用时不影响当前页面内拖拽排序。
      }
      return nextOrder
    })
  }

  const resetColumnOrder = () => {
    setColumnOrder(draggableColumnKeys)
    try {
      localStorage.removeItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY)
    } catch {
      // localStorage 不可用时仍恢复当前页面内的默认列顺序。
    }
    message.success(t('containers.messages.columnOrderReset', '列顺序已恢复默认'))
  }

  const columns = useMemo(() => {
    const activeOrder = columnOrder.length ? columnOrder : draggableColumnKeys
    const columnMap = new Map(baseColumns.map((column) => [String(column.key), column]))
    return activeOrder
      .map((key) => columnMap.get(key))
      .filter((column): column is ColumnsType<StoreOrderListItem>[number] => Boolean(column))
      .map((column) => ({
        ...column,
        onHeaderCell: () => ({
          'data-column-key': String(column.key),
        }),
      })) as ColumnsType<StoreOrderListItem>
  }, [baseColumns, columnOrder, draggableColumnKeys])

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
          {canUseWarehouseManagerActions ? (
            <Button
              icon={<SyncOutlined />}
              loading={syncingMode === 'Incremental'}
              disabled={syncingMode !== null}
              onClick={handleOpenIncrementalHqSync}
            >
              {t('storeOrders.syncIncrementalOrders')}
            </Button>
          ) : null}
          {canUseWarehouseManagerActions ? (
            <Button
              icon={<ToolOutlined />}
              loading={unmatchedStoreLoading}
              onClick={openUnmatchedStoreModal}
            >
              {t('storeOrders.fixStoreGuid', '修复分店 GUID')}
            </Button>
          ) : null}
          {canUseWarehouseManagerActions ? (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!canCreateStoreOrder}
              onClick={() => setStorePickerOpen(true)}
            >
              {t('storeOrders.newOrder')}
            </Button>
          ) : null}
          {canUseWarehouseManagerActions ? (
            <Button
              icon={<CopyOutlined />}
              disabled={!selectedRowKeys.length}
              onClick={() => setCopyModalOpen(true)}
            >
              {t('storeOrders.copyOrder', { count: selectedRowKeys.length })}
            </Button>
          ) : null}
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setKeyword('')
              setDateRange(null)
              setSelectedStoreCodes([])
              setStatusList(DEFAULT_STATUS_LIST)
              setColumnFilters({})
              setSortField('orderDate')
              setSortOrder('descend')
              void loadData({
                keyword: undefined,
                startDate: undefined,
                endDate: undefined,
                storeCodes: undefined,
                statusList: DEFAULT_STATUS_LIST,
                columnFilters: undefined,
                pageNumber: 1,
                pageSize,
                sortBy: 'orderDate',
                sortDescending: true,
              })
            }}
          >
            {t('common.reset')}
          </Button>
          {isColumnOrderCustomized ? (
            <Button icon={<ReloadOutlined />} onClick={resetColumnOrder}>
              {t('containers.actions.resetColumns', '重置列')}
            </Button>
          ) : null}
          {canUseWarehouseManagerActions ? (
            <>
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
            </>
          ) : null}
        </Space>
      }
    >
      <Card>
        <Space wrap className="store-order-list-filter-bar">
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
            showSearch
            style={{ width: 280 }}
            placeholder={t('storeOrders.allStores')}
            optionFilterProp="label"
            filterOption={filterStoreOption}
            options={storeFilterOptions}
            onChange={(value) => setSelectedStoreCodes(value)}
          />
          <Checkbox.Group
            value={statusList}
            options={statusOptions}
            onChange={(value) => setStatusList(value as StoreOrderFlowStatus[])}
          />
          <Button type="primary" onClick={() => void loadData({ pageNumber: 1 })}>
            {t('common.query')}
          </Button>
        </Space>

        <DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>
          <SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>
            <Table
              className="store-order-list-table"
              rowKey="orderGUID"
              loading={loading}
              dataSource={data}
              components={{ header: { cell: DraggableHeaderCell } }}
              columns={columns}
              rowSelection={
                canUseWarehouseManagerActions
                  ? {
                      selectedRowKeys,
                      onChange: setSelectedRowKeys,
                    }
                  : undefined
              }
              scroll={{ x: 1640, y: 620 }}
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
          </SortableContext>
        </DndContext>
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
            void refreshCurrentList({ pageNumber: 1 })
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
            void refreshCurrentList({ pageNumber: 1 })
          } catch (error) {
            console.error(error)
            message.error(error instanceof Error ? error.message : t('storeOrders.copyOrderFailed'))
          } finally {
            setCopying(false)
          }
        }}
      />

      <Modal
        title={t('storeOrders.fixStoreGuidTitle', '修复未匹配分店 GUID')}
        open={unmatchedStoreOpen}
        width="min(1280px, calc(100vw - 48px))"
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={unmatchedStoreSaving}
        destroyOnHidden
        onCancel={closeUnmatchedStoreModal}
        onOk={() => void handleSaveUnmatchedStoreMappings()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text type="secondary">
            {t('storeOrders.fixStoreGuidHint', '按旧分店 GUID 聚合订单，为每个 GUID 选择对应本地分店后保存。')}
          </Typography.Text>
          <Table
            rowKey="sourceStoreCode"
            size="small"
            loading={unmatchedStoreLoading}
            dataSource={unmatchedStoreGroups}
            pagination={false}
            scroll={{ x: 1138, y: 420 }}
            locale={{ emptyText: t('storeOrders.noUnmatchedStoreGuid', '暂无未匹配分店 GUID') }}
            columns={[
              {
                title: t('storeOrders.sourceStoreCode', '旧 GUID / 标识'),
                dataIndex: 'sourceStoreCode',
                width: 260,
                render: (value: string) => (
                  <Typography.Text code copyable className="store-order-unmatched-source">
                    {value}
                  </Typography.Text>
                ),
              },
              {
                title: t('storeOrders.sourceStoreName', 'HQ 客户名称'),
                dataIndex: 'sourceStoreName',
                width: 150,
                render: (value: string | undefined) => value || '--',
              },
              {
                title: t('storeOrders.unmatchedOrderCount', '订单数量'),
                dataIndex: 'orderCount',
                width: 88,
                render: (value: number) => renderStoreOrderNumericCell(value),
              },
              {
                title: t('storeOrders.latestOrderDate', '最近订单日期'),
                dataIndex: 'latestOrderDate',
                width: 120,
                render: (value: string | undefined) => formatDate(value, i18n.language),
              },
              {
                title: t('storeOrders.targetStore', '目标本地分店'),
                dataIndex: 'sourceStoreCode',
                width: 520,
                render: (sourceStoreCode: string) => (
                  <Select
                    value={unmatchedStoreTargets[sourceStoreCode]}
                    allowClear
                    showSearch
                    style={{ width: '100%' }}
                    popupMatchSelectWidth={640}
                    classNames={{ popup: { root: 'store-order-unmatched-target-popup' } }}
                    placeholder={t('storeOrders.selectTargetStore')}
                    optionFilterProp="label"
                    filterOption={filterStoreOption}
                    options={unmatchedTargetStoreOptions}
                    onChange={(value) => {
                      setUnmatchedStoreTargets((current) => {
                        const next = { ...current }
                        if (value) {
                          next[sourceStoreCode] = value
                        } else {
                          delete next[sourceStoreCode]
                        }
                        return next
                      })
                    }}
                  />
                ),
              },
            ]}
          />
        </Space>
      </Modal>

      <Modal
        title={t('storeOrders.shipOrderTitle')}
        open={Boolean(shippingOrder)}
        okText={t('storeOrders.confirmShipOrder')}
        cancelText={t('common.cancel')}
        confirmLoading={shippingLoading}
        destroyOnHidden
        onCancel={closeShippingModal}
        onOk={() => void handleConfirmShipping()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text>
            {t('storeOrders.shipOrderConfirm', {
              orderNo: shippingOrder?.orderNo || '--',
            })}
          </Typography.Text>
          <DatePicker
            style={{ width: '100%' }}
            value={shippingDate}
            onChange={(value) => setShippingDate(value ?? dayjs())}
          />
        </Space>
      </Modal>

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
