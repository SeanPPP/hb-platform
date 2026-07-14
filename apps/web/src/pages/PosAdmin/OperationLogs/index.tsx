import {
  EyeOutlined,
  HolderOutlined,
  ReloadOutlined,
  SearchOutlined,
  ToolOutlined,
} from '@ant-design/icons'
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  SortableContext,
  horizontalListSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Input,
  Row,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { RangePickerProps } from 'antd/es/date-picker'
import type { ColumnsType, TableProps } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type HTMLAttributes,
} from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import {
  getOperationAuditDetail,
  getOperationAudits,
} from '../../../services/operationAuditService'
import { getActiveStores, type StoreOption } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type {
  OperationAuditDetail,
  OperationAuditDetailItem,
  OperationAuditListItem,
  OperationAuditOutcome,
  OperationAuditSortField,
} from '../../../types/operationAudit'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
} from '../../../utils/managedStoreScope'
import {
  OPERATION_TYPE_KEYS,
  DEFAULT_OPERATION_AUDIT_SORT,
  buildOperationAuditQuery,
  buildSystemLogLink,
  createLatestOperationAuditRequestGuard,
  formatMoney,
  formatSignedMoney,
  resolveOperationAuditTableChange,
  summarizeProducts,
  type OperationAuditTableSortOrder,
} from './operationLogsLogic'
import {
  DEFAULT_OPERATION_LOG_COLUMN_ORDER,
  createOperationLogDndAccessibility,
  dispatchOperationLogDragHandleKeyDown,
  isOperationLogColumnOrderCustomized,
  moveOperationLogColumnOrder,
  parseOperationLogColumnOrder,
  type OperationLogColumnKey,
} from './operationLogColumnOrder'

interface OperationAuditFormValues {
  timeRange: [Dayjs, Dayjs]
  storeCode?: string
  cashierKeyword?: string
  deviceCode?: string
  operationType?: string
  outcome?: string
  productKeyword?: string
  orderGuid?: string
  keyword?: string
}

const DEFAULT_PAGE_SIZE = 20
const OPERATION_LOG_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.operationLogs.columnOrder.v1'
// 连续编号通常没有空格，允许在任意位置折行，避免终端号等内容被省略。
const WRAPPED_TABLE_CELL_STYLE: CSSProperties = {
  whiteSpace: 'normal',
  overflowWrap: 'anywhere',
  wordBreak: 'break-word',
}

interface DraggableHeaderCellProps extends HTMLAttributes<HTMLTableCellElement> {
  'data-column-key'?: string
  'data-drag-label'?: string
}

function DraggableHeaderCell({ children, style, ...props }: DraggableHeaderCellProps) {
  const columnKey = props['data-column-key']
  const dragLabel = props['data-drag-label']
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: columnKey ?? '__operation-log-static-column__',
    disabled: !columnKey,
  })

  if (!columnKey) return <th style={style} {...props}>{children}</th>

  const headerStyle: CSSProperties = {
    ...style,
    transform: CSS.Translate.toString(transform),
    transition,
    zIndex: isDragging ? 3 : style?.zIndex,
    opacity: isDragging ? 0.8 : style?.opacity,
  }
  const { onKeyDown: dndKeyDownListener, ...pointerListeners } = listeners ?? {}

  return (
    <th ref={setNodeRef} style={headerStyle} {...props}>
      <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, width: '100%' }}>
        <button
          type="button"
          aria-label={dragLabel}
          title={dragLabel}
          style={{
            display: 'inline-flex',
            flex: '0 0 auto',
            alignItems: 'center',
            justifyContent: 'center',
            width: 20,
            height: 20,
            padding: 0,
            color: 'rgba(0, 0, 0, 0.45)',
            cursor: isDragging ? 'grabbing' : 'grab',
            touchAction: 'none',
            background: 'transparent',
            border: 0,
          }}
          {...attributes}
          {...pointerListeners}
          // 只有把手接收拖拽监听；鼠标松手和键盘操作都不能冒泡触发表头排序。
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => {
            dispatchOperationLogDragHandleKeyDown(event, (dragEvent) => {
              dndKeyDownListener?.(dragEvent)
            })
          }}
        >
          <HolderOutlined />
        </button>
        <div style={{ minWidth: 0 }}>{children}</div>
      </div>
    </th>
  )
}

function getDefaultTimeRange(): [Dayjs, Dayjs] {
  return [dayjs().subtract(7, 'day'), dayjs()]
}

function getOutcomeColor(outcome: OperationAuditOutcome) {
  switch (outcome) {
    case 'Succeeded':
      return 'success'
    case 'Denied':
      return 'warning'
    case 'Failed':
      return 'error'
    default:
      return 'default'
  }
}

function formatValue(value: unknown) {
  if (value === null || value === undefined || value === '') {
    return '-'
  }
  return String(value)
}

function formatSafeProperties(value?: string) {
  if (!value) {
    return '-'
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

export default function PosAdminOperationLogsPage() {
  const { t } = useTranslation()
  const [form] = Form.useForm<OperationAuditFormValues>()
  const access = useAuthStore((state) => state.access)
  const currentUser = useAuthStore((state) => state.currentUser)
  const managedStoreCodes = access.managedStoreCodes?.()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [data, setData] = useState<OperationAuditListItem[]>([])
  const [total, setTotal] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [sortBy, setSortBy] = useState<OperationAuditSortField>(
    DEFAULT_OPERATION_AUDIT_SORT.sortBy,
  )
  const [sortOrder, setSortOrder] = useState<OperationAuditTableSortOrder>(
    DEFAULT_OPERATION_AUDIT_SORT.sortOrder,
  )
  const [columnOrder, setColumnOrder] = useState<OperationLogColumnKey[]>(() => {
    if (typeof window === 'undefined') return [...DEFAULT_OPERATION_LOG_COLUMN_ORDER]
    try {
      const saved = localStorage.getItem(OPERATION_LOG_COLUMN_ORDER_STORAGE_KEY)
      return parseOperationLogColumnOrder(saved)
    } catch {
      return [...DEFAULT_OPERATION_LOG_COLUMN_ORDER]
    }
  })
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailRecord, setDetailRecord] = useState<OperationAuditDetail | null>(null)
  const requestGuardRef = useRef(createLatestOperationAuditRequestGuard())

  const visibleStoreOptions = useMemo(
    () => filterStoreOptionsByManagedCodes(storeOptions, managedStoreCodes),
    [managedStoreCodes, storeOptions],
  )

  useEffect(() => {
    if (managedStoreCodes !== null) {
      // 店长直接使用当前会话已授权的可管理门店，避免依赖 Stores.View 全店列表权限。
      setStoreOptions(buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true }))
      return
    }

    let disposed = false
    void getActiveStores()
      .then((stores) => {
        if (!disposed) {
          setStoreOptions(stores)
        }
      })
      .catch((error) => {
        console.error(error)
        if (!disposed) {
          message.error(t('operationLogs.loadStoresFailed'))
        }
      })
    return () => {
      disposed = true
    }
  }, [currentUser?.stores, managedStoreCodeKey, t])

  const loadData = useCallback(
    async (
      nextPage = pageNumber,
      nextPageSize = pageSize,
      nextSortBy = sortBy,
      nextSortOrder = sortOrder,
    ) => {
      // 只有最新查询可提交结果，避免旧页码或旧排序请求晚到后覆盖当前表格。
      const requestId = requestGuardRef.current.begin()
      const values = form.getFieldsValue()
      const [from, to] = values.timeRange ?? getDefaultTimeRange()
      setLoading(true)
      setLoadError(false)
      try {
        // 查询范围最终仍由服务端按当前用户可管理门店强制收窄。
        const result = await getOperationAudits(
          buildOperationAuditQuery({
            startUtc: from.toISOString(),
            endUtc: to.toISOString(),
            storeCode: values.storeCode ?? '',
            cashierKeyword: values.cashierKeyword ?? '',
            deviceCode: values.deviceCode ?? '',
            operationType: values.operationType ?? '',
            outcome: values.outcome ?? '',
            productKeyword: values.productKeyword ?? '',
            orderGuid: values.orderGuid ?? '',
            keyword: values.keyword ?? '',
            page: nextPage,
            pageSize: nextPageSize,
            sortBy: nextSortBy,
            sortOrder: nextSortOrder === 'descend' ? 'desc' : 'asc',
          }),
        )
        if (!requestGuardRef.current.isLatest(requestId)) return
        setData(result.items)
        setTotal(result.total)
        setPageNumber(result.pageNumber)
        setPageSize(result.pageSize)
      } catch (error) {
        if (!requestGuardRef.current.isLatest(requestId)) return
        console.error(error)
        setLoadError(true)
        message.error(t('operationLogs.loadFailed'))
      } finally {
        if (requestGuardRef.current.isLatest(requestId)) setLoading(false)
      }
    },
    [form, pageNumber, pageSize, sortBy, sortOrder, t],
  )

  useEffect(() => {
    form.setFieldsValue({ timeRange: getDefaultTimeRange() })
    void loadData(
      1,
      DEFAULT_PAGE_SIZE,
      DEFAULT_OPERATION_AUDIT_SORT.sortBy,
      DEFAULT_OPERATION_AUDIT_SORT.sortOrder,
    )
    // 首次加载只执行一次，后续查询由用户或分页动作触发。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleReset = () => {
    form.resetFields()
    form.setFieldsValue({ timeRange: getDefaultTimeRange() })
    setPageNumber(1)
    setPageSize(DEFAULT_PAGE_SIZE)
    setSortBy(DEFAULT_OPERATION_AUDIT_SORT.sortBy)
    setSortOrder(DEFAULT_OPERATION_AUDIT_SORT.sortOrder)
    void loadData(
      1,
      DEFAULT_PAGE_SIZE,
      DEFAULT_OPERATION_AUDIT_SORT.sortBy,
      DEFAULT_OPERATION_AUDIT_SORT.sortOrder,
    )
  }

  const handleQuery = () => {
    setPageNumber(1)
    void loadData(1, pageSize, sortBy, sortOrder)
  }

  const handleOpenDetail = async (record: OperationAuditListItem) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailRecord(null)
    try {
      setDetailRecord(await getOperationAuditDetail(record.eventId))
    } catch (error) {
      console.error(error)
      message.error(t('operationLogs.loadDetailFailed'))
    } finally {
      setDetailLoading(false)
    }
  }

  const operationLabel = useCallback(
    (operationType: string) => {
      const key = OPERATION_TYPE_KEYS[operationType]
      return key ? t(key) : operationType
    },
    [t],
  )

  const outcomeLabel = useCallback(
    (outcome: OperationAuditOutcome) => t(`operationLogs.outcomes.${outcome.toLowerCase()}`),
    [t],
  )

  const columnDragSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const operationLogColumnLabels = useMemo<Record<OperationLogColumnKey, string>>(
    () => ({
      occurredAtUtc: t('operationLogs.columns.time'),
      storeCode: t('operationLogs.columns.store'),
      employee: t('operationLogs.columns.employee'),
      operationType: t('operationLogs.columns.operation'),
      products: t('operationLogs.columns.products'),
      amountDelta: t('operationLogs.columns.amountChange'),
      deviceCode: t('operationLogs.columns.device'),
      outcome: t('operationLogs.columns.outcome'),
    }),
    [t],
  )

  const dndAccessibility = useMemo(
    () => createOperationLogDndAccessibility(operationLogColumnLabels, {
      instructions: t('operationLogs.dnd.instructions'),
      unknownColumn: t('operationLogs.dnd.unknownColumn'),
      dragStart: (column) => t('operationLogs.dnd.dragStart', { column }),
      dragOver: (column, overColumn) =>
        t('operationLogs.dnd.dragOver', { column, overColumn }),
      dragOverNone: (column) => t('operationLogs.dnd.dragOverNone', { column }),
      dragEnd: (column, overColumn) =>
        t('operationLogs.dnd.dragEnd', { column, overColumn }),
      dragCancel: (column) => t('operationLogs.dnd.dragCancel', { column }),
    }),
    [operationLogColumnLabels, t],
  )

  const baseColumns = useMemo<ColumnsType<OperationAuditListItem>>(
    () => [
      {
        title: t('operationLogs.columns.time'),
        dataIndex: 'occurredAtUtc',
        key: 'occurredAtUtc',
        width: 175,
        sorter: true,
        sortOrder: sortBy === 'occurredAtUtc' ? sortOrder : null,
        render: (value: string) => dayjs(value).format('YYYY-MM-DD HH:mm:ss'),
      },
      {
        title: t('operationLogs.columns.store'),
        dataIndex: 'storeCode',
        key: 'storeCode',
        width: 100,
        sorter: true,
        sortOrder: sortBy === 'storeCode' ? sortOrder : null,
      },
      {
        title: t('operationLogs.columns.employee'),
        key: 'employee',
        width: 150,
        render: (_, record) => (
          <span style={WRAPPED_TABLE_CELL_STYLE}>
            {record.cashierName || record.cashierId || record.userGuid || '-'}
          </span>
        ),
      },
      {
        title: t('operationLogs.columns.operation'),
        dataIndex: 'operationType',
        key: 'operationType',
        width: 185,
        sorter: true,
        sortOrder: sortBy === 'operationType' ? sortOrder : null,
        render: (value: string) => operationLabel(value),
      },
      {
        title: t('operationLogs.columns.products'),
        key: 'products',
        minWidth: 180,
        render: (_, record) => (
          <span style={WRAPPED_TABLE_CELL_STYLE}>
            {summarizeProducts(record, t('operationLogs.detail.productFallback'))}
          </span>
        ),
      },
      {
        title: t('operationLogs.columns.amountChange'),
        dataIndex: 'amountDelta',
        key: 'amountDelta',
        width: 120,
        align: 'right',
        sorter: true,
        sortOrder: sortBy === 'amountDelta' ? sortOrder : null,
        render: (_, record) => formatSignedMoney(record.amountDelta, record.currencyCode || 'AUD'),
      },
      {
        title: t('operationLogs.columns.device'),
        dataIndex: 'deviceCode',
        key: 'deviceCode',
        width: 125,
        sorter: true,
        sortOrder: sortBy === 'deviceCode' ? sortOrder : null,
        render: (value: string) => (
          <span style={WRAPPED_TABLE_CELL_STYLE}>{value || '-'}</span>
        ),
      },
      {
        title: t('operationLogs.columns.outcome'),
        dataIndex: 'outcome',
        key: 'outcome',
        width: 105,
        sorter: true,
        sortOrder: sortBy === 'outcome' ? sortOrder : null,
        render: (value: OperationAuditOutcome) => (
          <Tag color={getOutcomeColor(value)}>{outcomeLabel(value)}</Tag>
        ),
      },
      {
        title: t('common.action'),
        key: 'actions',
        width: 90,
        fixed: 'right',
        render: (_, record) => (
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleOpenDetail(record)}>
            {t('common.view')}
          </Button>
        ),
      },
    ],
    [operationLabel, outcomeLabel, sortBy, sortOrder, t],
  )

  const isColumnOrderCustomized = isOperationLogColumnOrderCustomized(columnOrder)

  const columns = useMemo<ColumnsType<OperationAuditListItem>>(() => {
    const businessColumnMap = new Map(
      baseColumns
        .filter((column) => column.key !== 'actions')
        .map((column) => [String(column.key), column]),
    )
    const actionColumn = baseColumns.find((column) => column.key === 'actions')
    const orderedBusinessColumns = columnOrder
      .map((key) => businessColumnMap.get(key))
      .filter((column): column is ColumnsType<OperationAuditListItem>[number] => Boolean(column))
      .map((column) => ({
        ...column,
        onHeaderCell: () => ({
          'data-column-key': String(column.key),
          'data-drag-label': t('operationLogs.dragColumn', { column: String(column.title) }),
        } as DraggableHeaderCellProps),
      }))

    return actionColumn ? [...orderedBusinessColumns, actionColumn] : orderedBusinessColumns
  }, [baseColumns, columnOrder, t])

  const handleColumnDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    setColumnOrder((current) => {
      const nextOrder = moveOperationLogColumnOrder(current, active.id, over.id)
      try {
        localStorage.setItem(OPERATION_LOG_COLUMN_ORDER_STORAGE_KEY, JSON.stringify(nextOrder))
      } catch {
        // localStorage 不可用时仍保留当前页面内的列顺序。
      }
      return nextOrder
    })
  }

  const resetColumnOrder = () => {
    setColumnOrder([...DEFAULT_OPERATION_LOG_COLUMN_ORDER])
    try {
      localStorage.removeItem(OPERATION_LOG_COLUMN_ORDER_STORAGE_KEY)
    } catch {
      // localStorage 不可用时仍恢复当前页面内的默认列顺序。
    }
    message.success(t('operationLogs.columnOrderReset'))
  }

  const handleTableChange: NonNullable<TableProps<OperationAuditListItem>['onChange']> = (
    pagination,
    _filters,
    sorter,
    extra,
  ) => {
    const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const nextState = resolveOperationAuditTableChange(
      { page: pageNumber, pageSize, sortBy, sortOrder },
      {
        action: extra.action === 'sort' ? 'sort' : 'paginate',
        page: pagination.current ?? pageNumber,
        pageSize: pagination.pageSize ?? pageSize,
        sortBy: nextSorter?.field,
        sortOrder: nextSorter?.order,
      },
    )

    setPageNumber(nextState.page)
    setPageSize(nextState.pageSize)
    setSortBy(nextState.sortBy)
    setSortOrder(nextState.sortOrder)
    // 将本次表格状态显式传入请求，避免 setState 异步导致请求仍使用旧排序。
    void loadData(
      nextState.page,
      nextState.pageSize,
      nextState.sortBy,
      nextState.sortOrder,
    )
  }

  const itemColumns = useMemo<ColumnsType<OperationAuditDetailItem>>(
    () => [
      {
        title: t('operationLogs.detail.product'),
        key: 'product',
        width: 230,
        render: (_, item) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{item.displayName || item.productCode || '-'}</Typography.Text>
            <Typography.Text type="secondary">
              {[
                item.productCode,
                item.itemNumber ? `${t('operationLogs.detail.itemNumber')}: ${item.itemNumber}` : null,
                item.referenceCode,
                item.lookupCode,
                item.lineKind ? `${t('operationLogs.detail.lineKind')}: ${item.lineKind}` : null,
              ].filter(Boolean).join(' / ') || '-'}
            </Typography.Text>
          </Space>
        ),
      },
      {
        title: t('operationLogs.detail.quantity'),
        key: 'quantity',
        width: 165,
        render: (_, item) => `${formatValue(item.beforeQuantity)} → ${formatValue(item.afterQuantity)} (${formatValue(item.quantityDelta)})`,
      },
      {
        title: t('operationLogs.detail.unitPrice'),
        key: 'unitPrice',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeUnitPrice, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterUnitPrice, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.unitPriceDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.discount'),
        key: 'discount',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeDiscountAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterDiscountAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.discountAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.gross'),
        key: 'gross',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeGrossAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterGrossAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.grossAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.actual'),
        key: 'actual',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeActualAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterActualAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.actualAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
    ],
    [detailRecord?.currencyCode, t],
  )

  const timeRangePresets: RangePickerProps['presets'] = [
    { label: t('operationLogs.presets.today'), value: [dayjs().startOf('day'), dayjs()] },
    { label: t('operationLogs.presets.last7Days'), value: [dayjs().subtract(7, 'day'), dayjs()] },
    { label: t('operationLogs.presets.last30Days'), value: [dayjs().subtract(30, 'day'), dayjs()] },
  ]

  return (
    <PageContainer
      title={t('operationLogs.pageTitle')}
      subtitle={t('operationLogs.pageSubtitle')}
      extra={
        <Button
          icon={<ReloadOutlined />}
          onClick={() => void loadData(pageNumber, pageSize, sortBy, sortOrder)}
        >
          {t('common.refresh')}
        </Button>
      }
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Card>
          <Form
            form={form}
            layout="vertical"
            onFinish={handleQuery}
          >
            <Row gutter={16}>
              <Col xs={24} lg={8}>
                <Form.Item label={t('operationLogs.filters.timeRange')} name="timeRange">
                  <DatePicker.RangePicker
                    showTime
                    presets={timeRangePresets}
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.store')} name="storeCode">
                  <Select
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    options={visibleStoreOptions}
                    placeholder={t('operationLogs.filters.storePlaceholder')}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.employee')} name="cashierKeyword">
                  <Input allowClear placeholder={t('operationLogs.filters.employeePlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.device')} name="deviceCode">
                  <Input allowClear placeholder={t('operationLogs.filters.devicePlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.outcome')} name="outcome">
                  <Select
                    allowClear
                    options={(['Succeeded', 'Denied', 'Failed'] as OperationAuditOutcome[]).map((value) => ({
                      value,
                      label: outcomeLabel(value),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.operation')} name="operationType">
                  <Select
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    options={Object.keys(OPERATION_TYPE_KEYS).map((value) => ({
                      value,
                      label: operationLabel(value),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.product')} name="productKeyword">
                  <Input allowClear placeholder={t('operationLogs.filters.productPlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.order')} name="orderGuid">
                  <Input allowClear placeholder={t('operationLogs.filters.orderPlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.keyword')} name="keyword">
                  <Input allowClear placeholder={t('operationLogs.filters.keywordPlaceholder')} />
                </Form.Item>
              </Col>
            </Row>
            <Space>
              <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
                {t('common.query')}
              </Button>
              <Button onClick={handleReset}>{t('common.reset')}</Button>
            </Space>
          </Form>
        </Card>

        {loadError ? (
          <Alert
            type="error"
            showIcon
            message={t('operationLogs.loadFailed')}
            action={(
              <Button onClick={() => void loadData(pageNumber, pageSize, sortBy, sortOrder)}>
                {t('operationLogs.retry')}
              </Button>
            )}
          />
        ) : null}

        <Card>
          {isColumnOrderCustomized ? (
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
              <Button size="small" icon={<ReloadOutlined />} onClick={resetColumnOrder}>
                {t('operationLogs.resetColumns')}
              </Button>
            </div>
          ) : null}
          <DndContext
            sensors={columnDragSensors}
            collisionDetection={closestCenter}
            onDragEnd={handleColumnDragEnd}
            accessibility={dndAccessibility}
          >
            <SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>
              <Table<OperationAuditListItem>
                rowKey="eventId"
                loading={loading}
                components={{ header: { cell: DraggableHeaderCell } }}
                columns={columns}
                dataSource={data}
                scroll={{ x: 1280 }}
                locale={{ emptyText: t('operationLogs.empty') }}
                sortDirections={['descend', 'ascend', 'descend']}
                pagination={{
                  current: pageNumber,
                  pageSize,
                  total,
                  showSizeChanger: true,
                  pageSizeOptions: [20, 50, 100, 200],
                  showTotal: (value) => t('operationLogs.paginationTotal', { total: value }),
                }}
                onChange={handleTableChange}
              />
            </SortableContext>
          </DndContext>
        </Card>
      </Space>

      <Drawer
        title={t('operationLogs.detailTitle')}
        width={960}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailRecord(null)
        }}
        destroyOnHidden
      >
        {detailLoading ? <Typography.Text type="secondary">{t('operationLogs.loadingDetail')}</Typography.Text> : null}
        {detailRecord ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2} size="small">
              <Descriptions.Item label={t('operationLogs.columns.time')}>
                {dayjs(detailRecord.occurredAtUtc).format('YYYY-MM-DD HH:mm:ss')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.receivedAt')}>
                {dayjs(detailRecord.receivedAtUtc).format('YYYY-MM-DD HH:mm:ss')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.store')}>
                {detailRecord.storeCode}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.device')}>
                {detailRecord.deviceCode}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.employee')}>
                {detailRecord.cashierName || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.cashierId')}>
                {detailRecord.cashierId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.userGuid')}>
                <Typography.Text copyable={Boolean(detailRecord.userGuid)}>{detailRecord.userGuid || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.sessionFlags')}>
                <Space wrap size={[4, 4]}>
                  {detailRecord.isOfflineCached ? <Tag>{t('operationLogs.detail.offlineCached')}</Tag> : null}
                  {detailRecord.isEmergencyOverride ? <Tag color="warning">{t('operationLogs.detail.emergencyOverride')}</Tag> : null}
                  {!detailRecord.isOfflineCached && !detailRecord.isEmergencyOverride ? '-' : null}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.outcome')}>
                <Tag color={getOutcomeColor(detailRecord.outcome)}>{outcomeLabel(detailRecord.outcome)}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.operation')}>
                {operationLabel(detailRecord.operationType)}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.reason')}>
                {detailRecord.reasonCode || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.orderGuid')}>
                <Typography.Text copyable={Boolean(detailRecord.orderGuid)}>{detailRecord.orderGuid || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.receiptNumber')}>
                {detailRecord.receiptNumber || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.paymentMethod')}>
                {detailRecord.paymentMethod || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.paymentAmount')}>
                {formatMoney(detailRecord.paymentAmount, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.beforeAfterActual')}>
                {formatMoney(detailRecord.beforeActual, detailRecord.currencyCode || 'AUD')} →{' '}
                {formatMoney(detailRecord.afterActual, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.amountChange')}>
                {formatSignedMoney(detailRecord.amountDelta, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.appVersion')}>
                {detailRecord.appVersion || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.instanceId')}>
                {detailRecord.instanceId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.correlationId')}>
                <Typography.Text copyable={Boolean(detailRecord.correlationId)}>{detailRecord.correlationId || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.traceId')}>
                <Space>
                  <Typography.Text copyable={Boolean(detailRecord.traceId)}>{detailRecord.traceId || '-'}</Typography.Text>
                  {access.canViewSystemLogs ? (
                    <Link
                      to={buildSystemLogLink({
                        deviceCode: detailRecord.deviceCode,
                        traceId: detailRecord.traceId,
                        occurredAtUtc: detailRecord.occurredAtUtc,
                      })}
                    >
                      <ToolOutlined /> {t('operationLogs.detail.openSystemLogs')}
                    </Link>
                  ) : null}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.safeMessage')} span={2}>
                <Typography.Paragraph style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {detailRecord.safeMessage || '-'}
                </Typography.Paragraph>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.safeProperties')} span={2}>
                <Typography.Paragraph code style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {formatSafeProperties(detailRecord.propertiesJson)}
                </Typography.Paragraph>
              </Descriptions.Item>
            </Descriptions>

            <Table<OperationAuditDetailItem>
              rowKey={(item) => `${item.eventId}-${item.lineIndex}`}
              size="small"
              columns={itemColumns}
              dataSource={detailRecord.items ?? []}
              pagination={false}
              scroll={{ x: 1155 }}
              locale={{ emptyText: t('operationLogs.detail.noItems') }}
            />
          </Space>
        ) : null}
      </Drawer>
    </PageContainer>
  )
}
