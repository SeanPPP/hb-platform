import {
  CheckOutlined,
  ContainerOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  FileTextOutlined,
  PlusOutlined,
  PrinterOutlined,
  SaveOutlined,
  SearchOutlined,
  SortAscendingOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Descriptions,
  Empty,
  Grid,
  Image,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
  notification,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { SortOrder, SorterResult } from 'antd/es/table/interface'
import type { InputNumberRef } from 'rc-input-number'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { useAuthStore } from '../../../store/auth'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { getStores } from '../../../services/storeService'
import ContainerProductPicker from './components/ContainerProductPicker'
import {
  addStoreOrderLine,
  batchLookupStoreOrderProducts,
  batchAddStoreOrderLines,
  batchUpdateStoreOrderLines,
  batchUpdateStoreOrderProductStatus,
  createStoreOrderPasteReplaceJob,
  getStoreOrderDetail,
  getStoreOrderDetailFull,
  getStoreOrderDetailProductCodes,
  getStoreOrderPasteReplaceJob,
  getStoreOrderProducts,
  removeStoreOrderLine,
  refreshStoreOrderImportPrices,
  startPickingStoreOrder,
  updateStoreOrderHeader,
  updateStoreOrderLine,
  updateStoreOrderOutboundDate,
  updateStoreOrderStatus,
  updateStoreOrderStoreContact,
  updateStoreOrderProductStatus,
} from '../../../services/storeOrderService'
import type { StoreDto } from '../../../types/store'
import type { ChinaSupplierItem } from '../../../types/chinaSupplier'
import type {
  StoreOrderDetail,
  StoreOrderDetailLine,
  StoreOrderDetailQuery,
  StoreOrderDetailStatFilter,
  StoreOrderDetailSortField,
  StoreOrderPasteTargetField,
  StoreOrderProductItem,
} from '../../../types/storeOrder'
import { StoreOrderFlowStatus, StoreOrderStatusColorMap } from '../../../types/storeOrder'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { deriveStoreOrderDetailPermissions } from './storeOrderDetailPermissions'
import { shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'
import { shouldShowStoreOrderDetailInitialLoading } from './detailLoadState'
import { resolveStoreContactDraftValue } from './storeOrderStoreContact'
import {
  buildPasteSubmitItems,
  createPastePreviewItems,
  filterPastePreviewItems,
  formatPastePreviewQuantity,
  parseStoreOrderPasteRows,
  setExistingPastePreviewAction,
  type StoreOrderPasteAction,
  type StoreOrderPasteQuantityMode,
  type StoreOrderPastePreviewFilter,
  type StoreOrderPastePreviewItem,
} from './pastePreview'
import {
  StoreOrderPasteReplacePollingCancelledError,
  StoreOrderPasteReplacePollingTimeoutError,
  createStoreOrderPasteReplaceJobPoller,
} from './pasteReplaceJobPolling'
import {
  applyPasteOptimisticRowsToDetail,
  buildPasteOptimisticRows,
  resolvePasteOptimisticPendingAfterJob,
  type StoreOrderPasteOptimisticPending,
} from './pasteOptimisticRows'
import { formatStoreOrderVolume } from './volumeFormat'
import './compact.css'

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

function formatLocalDateForInput(value = new Date()) {
  const year = value.getFullYear()
  const month = String(value.getMonth() + 1).padStart(2, '0')
  const day = String(value.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function formatAmount(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }
  return value.toFixed(2)
}

function formatCurrencyAmount(value?: number) {
  const amount = formatAmount(value)
  return amount === '--' ? amount : `$${amount}`
}

type DetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error'
type DetailSortField = StoreOrderDetailSortField | null
type DetailEditableField = 'allocQuantity' | 'importPrice'
type StoreOrderPasteWriteTarget = StoreOrderPasteTargetField | 'allocQuantityByInner'

function resolvePasteTargetField(writeTarget: StoreOrderPasteWriteTarget): StoreOrderPasteTargetField {
  return writeTarget === 'allocQuantityByInner' ? 'allocQuantity' : writeTarget
}

function resolvePasteQuantityMode(writeTarget: StoreOrderPasteWriteTarget): StoreOrderPasteQuantityMode {
  return writeTarget === 'allocQuantityByInner' ? 'inner' : 'direct'
}

interface EditedLinePayload {
  detailGUID: string
  productCode: string
  quantity?: number
  importPrice?: number
  importPriceChanged: boolean
}

function toNumber(value?: number | null) {
  return Number(value ?? 0)
}

function isZeroOrEmpty(value: unknown) {
  return value === undefined || value === null || value === '' || value === 0
}

function isAbortError(error: unknown) {
  return typeof error === 'object' && error !== null && 'name' in error && error.name === 'AbortError'
}

function renderDangerValue(value: string) {
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '0 6px',
        borderRadius: 4,
        background: '#fff1f0',
        color: '#cf1322',
        fontWeight: 500,
      }}
    >
      {value}
    </span>
  )
}

function renderWarningValue(value: string) {
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '0 6px',
        borderRadius: 4,
        background: '#fff7e6',
        color: '#d46b08',
        fontWeight: 500,
      }}
    >
      {value}
    </span>
  )
}

function renderStoreOrderDetailNumericCell(value: ReactNode) {
  return <span className="store-order-numeric-cell">{value}</span>
}

function renderStoreOrderTwoLineText(value?: string) {
  if (!value) {
    return <>--</>
  }
  return <span className="store-order-two-line-text" title={value}>{value}</span>
}

function isOrderedNotShipped(line: StoreOrderDetailLine) {
  return toNumber(line.quantity) > 0 && toNumber(line.allocQuantity) === 0
}

function isShippedWithoutOrder(line: StoreOrderDetailLine) {
  return toNumber(line.quantity) <= 0 && toNumber(line.allocQuantity) > 0
}

interface ProductPickerModalProps {
  open: boolean
  orderGUID: string
  loading?: boolean
  onCancel: () => void
  onConfirm: (items: Array<{ productCode: string; quantity: number; importPrice?: number }>) => Promise<void>
}

const PRODUCT_PICKER_DEFAULT_PAGE_SIZE = 100
const PRODUCT_PICKER_PAGE_SIZE_OPTIONS = ['50', '100', '500']
const STORE_ORDER_DETAIL_DEFAULT_PAGE_SIZE = 200
const STORE_ORDER_DETAIL_PAGE_SIZE_OPTIONS = ['50', '100', '200', '500', '1000']

interface BatchEditModalProps {
  open: boolean
  loading?: boolean
  selectedCount: number
  onCancel: () => void
  onConfirm: (payload: {
    type: 'allocQuantity' | 'importPrice' | 'status'
    allocQuantity?: number
    importPrice?: number
    isActive?: boolean
  }) => Promise<void>
}

function ProductPickerModal({ open, orderGUID, loading, onCancel, onConfirm }: ProductPickerModalProps) {
  const { t } = useTranslation()
  const productRequestControllerRef = useRef<AbortController | null>(null)
  const supplierRequestControllerRef = useRef<AbortController | null>(null)
  const supplierOptionsLoadedRef = useRef(false)
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [products, setProducts] = useState<StoreOrderProductItem[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(PRODUCT_PICKER_DEFAULT_PAGE_SIZE)
  const [total, setTotal] = useState(0)
  const [supplierOptions, setSupplierOptions] = useState<Array<{ label: string; value: string }>>([])
  const [supplierCode, setSupplierCode] = useState<string>()
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [editingValues, setEditingValues] = useState<
    Record<string, { quantity?: number; importPrice?: number }>
  >({})

  const loadProducts = async (overrides?: {
    keyword?: string
    pageNumber?: number
    pageSize?: number
    supplierCode?: string
  }) => {
    const nextKeyword = overrides?.keyword ?? keyword
    const trimmedKeyword = nextKeyword.trim()
    const nextPageNumber = overrides?.pageNumber ?? pageNumber
    const nextPageSize = overrides?.pageSize ?? pageSize
    const nextSupplierCode = overrides?.supplierCode ?? supplierCode

    productRequestControllerRef.current?.abort()
    const currentController = new AbortController()
    productRequestControllerRef.current = currentController

    setFetching(true)
    try {
      const result = await getStoreOrderProducts(
        {
          // 商品弹窗只有一个搜索框，需要同时覆盖货号/条码和商品名称。
          itemNumber: trimmedKeyword || undefined,
          productName: trimmedKeyword || undefined,
          supplierCode: nextSupplierCode || undefined,
          excludeOrderGUID: orderGUID,
          pageNumber: nextPageNumber,
          pageSize: nextPageSize,
          sortBy: 'Default',
        },
        currentController.signal,
      )
      if (productRequestControllerRef.current !== currentController) {
        return
      }
      setProducts(result.items)
      setTotal(result.total)
      setPageNumber(nextPageNumber)
      setPageSize(nextPageSize)
    } catch (error) {
      if (isAbortError(error)) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.loadProductsFailed'))
    } finally {
      if (productRequestControllerRef.current === currentController) {
        productRequestControllerRef.current = null
        setFetching(false)
      }
    }
  }

  const loadSupplierOptions = async () => {
    if (supplierOptionsLoadedRef.current || supplierLoading) {
      return
    }

    supplierRequestControllerRef.current?.abort()
    const currentController = new AbortController()
    supplierRequestControllerRef.current = currentController

    setSupplierLoading(true)
    try {
      const suppliers: ChinaSupplierItem[] = await getActiveChinaSuppliers(currentController.signal)
      if (supplierRequestControllerRef.current !== currentController) {
        return
      }
      // 商品选择弹窗按国内供应商过滤，避免误用澳洲供应商编码导致候选商品为空。
      setSupplierOptions(
        suppliers
          .filter((item) => Boolean(item.supplierCode))
          .map((item) => ({
            label: item.supplierName || item.supplierCode,
            value: item.supplierCode,
          })),
      )
      supplierOptionsLoadedRef.current = true
    } catch (error) {
      if (isAbortError(error)) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.loadSuppliersFailed', '加载国内供应商失败'))
    } finally {
      if (supplierRequestControllerRef.current === currentController) {
        supplierRequestControllerRef.current = null
        setSupplierLoading(false)
      }
    }
  }

  useEffect(() => {
    if (!open) {
      return
    }
    void loadProducts({ pageNumber: 1 })
  }, [open])

  useEffect(() => {
    if (!open) {
      productRequestControllerRef.current?.abort()
      supplierRequestControllerRef.current?.abort()
      productRequestControllerRef.current = null
      supplierRequestControllerRef.current = null
      supplierOptionsLoadedRef.current = false
      setKeyword('')
      setProducts([])
      setSelectedRowKeys([])
      setPageNumber(1)
      setPageSize(PRODUCT_PICKER_DEFAULT_PAGE_SIZE)
      setTotal(0)
      setSupplierOptions([])
      setSupplierCode(undefined)
      setFetching(false)
      setSupplierLoading(false)
      setEditingValues({})
    }
  }, [open])

  const renderPickerCopyButton = (value: string, label: string) => (
    <Tooltip title={t('common.copy')}>
      <Button
        aria-label={label}
        className="store-order-picker-copy-button"
        icon={<CopyOutlined />}
        size="small"
        type="link"
        onClick={() => void copyTextToClipboard(value)}
      />
    </Tooltip>
  )

  const columns: ColumnsType<StoreOrderProductItem> = [
    {
      title: t('column.image'),
      dataIndex: 'productImage',
      width: 48,
      render: (value: string | undefined, record) => (
        <Image
          src={value}
          alt={record.productName}
          width={32}
          height={32}
          style={{ borderRadius: 4, objectFit: 'cover' }}
          fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
        />
      ),
    },
    {
      title: t('column.itemNumber'),
      dataIndex: 'itemNumber',
      width: 98,
      render: (value: string | undefined) =>
        value ? (
          <Space size={2} wrap={false} className="store-order-picker-inline-cell">
            <Typography.Text className="store-order-picker-nowrap" title={value}>{value}</Typography.Text>
            {renderPickerCopyButton(value, `${t('common.copy')} ${value}`)}
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: t('column.productName'),
      dataIndex: 'productName',
      width: 170,
      render: (value: string | undefined) => (
        <span className="store-order-picker-two-line" title={value}>
          {value || '--'}
        </span>
      ),
    },
    {
      title: t('column.supplierName', '供应商名称'),
      dataIndex: 'domesticSupplierName',
      width: 108,
      render: (value: string | undefined, record) => {
        const displayValue = value || record.domesticSupplierCode || '--'
        return (
          <span className="store-order-picker-two-line" title={displayValue}>
            {displayValue}
          </span>
        )
      },
    },
    {
      title: t('column.barcode'),
      dataIndex: 'barcode',
      width: 122,
      render: (value: string | undefined) =>
        value ? (
          <Space size={2} wrap={false} className="store-order-picker-inline-cell store-order-picker-barcode-cell">
            <Typography.Text className="store-order-picker-nowrap" title={value}>{value}</Typography.Text>
            {renderPickerCopyButton(value, `${t('common.copy')} ${value}`)}
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: t('column.stock'),
      dataIndex: 'stockQuantity',
      width: 56,
    },
    {
      title: t('column.minOrder'),
      dataIndex: 'minOrderQuantity',
      width: 62,
    },
    {
      title: t('column.defaultImportPrice'),
      dataIndex: 'importPrice',
      width: 84,
      render: (value: number | undefined) => formatCurrencyAmount(value),
    },
    {
      title: t('column.allocQuantity'),
      key: 'quantity',
      width: 70,
      render: (_, record) => (
        <InputNumber
          className="store-order-picker-number-input"
          min={0}
          precision={0}
          size="small"
          style={{ width: 58 }}
          value={editingValues[record.productCode]?.quantity ?? record.minOrderQuantity ?? 1}
          onChange={(value) =>
            setEditingValues((current) => ({
              ...current,
              [record.productCode]: {
                ...current[record.productCode],
                quantity: Number(value ?? record.minOrderQuantity ?? 1),
              },
            }))
          }
        />
      ),
    },
    {
      title: t('column.importPriceShort'),
      key: 'importPriceEdit',
      width: 82,
      render: (_, record) => (
        <InputNumber
          className="store-order-picker-number-input store-order-picker-price-input"
          min={0}
          // 商品弹窗价格直接显示 $，方便和数量列区分。
          prefix="$"
          precision={2}
          size="small"
          style={{ width: 70 }}
          value={editingValues[record.productCode]?.importPrice ?? record.importPrice}
          onChange={(value) =>
            setEditingValues((current) => ({
              ...current,
              [record.productCode]: {
                ...current[record.productCode],
                importPrice: value === null ? undefined : Number(value),
              },
            }))
          }
        />
      ),
    },
  ]

  const handleOk = async () => {
    if (!orderGUID) {
      message.error(t('storeOrders.detail.missingOrderNoError'))
      return
    }
    if (!selectedRowKeys.length) {
      message.warning(t('storeOrders.detail.selectProductsFirst'))
      return
    }

    const payload = selectedRowKeys
      .map((key) => products.find((item) => item.productCode === String(key)))
      .filter((item): item is StoreOrderProductItem => Boolean(item))
      .map((item) => ({
        productCode: item.productCode,
        quantity: editingValues[item.productCode]?.quantity ?? item.minOrderQuantity ?? 1,
        importPrice: editingValues[item.productCode]?.importPrice ?? item.importPrice,
      }))

    await onConfirm(payload)
  }

  return (
    <Modal
      className="store-order-product-picker-modal"
      title={t('storeOrders.selectProductTitle')}
      open={open}
      width={1080}
      destroyOnClose
      okText={t('storeOrders.addSelected', { count: selectedRowKeys.length })}
      cancelText={t('common.close')}
      confirmLoading={loading}
      onCancel={onCancel}
      onOk={() => void handleOk()}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space size={12} wrap>
          <Input.Search
            allowClear
            placeholder={t('storeOrders.detail.searchProductPlaceholder')}
            prefix={<SearchOutlined />}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            onSearch={(value) => void loadProducts({ keyword: value, pageNumber: 1 })}
            style={{ width: 320 }}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('storeOrders.detail.filterDomesticSupplier', '筛选国内供应商')}
            value={supplierCode}
            loading={supplierLoading}
            options={supplierOptions}
            style={{ width: 260 }}
            onOpenChange={(visible) => {
              if (visible) {
                void loadSupplierOptions()
                return
              }
              // 下拉收起时取消尚未完成的供应商请求，避免旧请求回写已关闭的筛选框。
              supplierRequestControllerRef.current?.abort()
              supplierRequestControllerRef.current = null
              setSupplierLoading(false)
            }}
            onChange={(value) => {
              // 切换供应商后回到第一页，避免旧分页落在空页。
              setSupplierCode(value)
              void loadProducts({ pageNumber: 1, supplierCode: value })
            }}
          />
        </Space>
        <Table
          className="store-order-product-picker-table"
          rowKey="productCode"
          loading={fetching}
          size="small"
          tableLayout="fixed"
          dataSource={products}
          columns={columns}
          rowSelection={{
            selectedRowKeys,
            onChange: setSelectedRowKeys,
            preserveSelectedRowKeys: true,
            columnWidth: 32,
          }}
          pagination={{
            current: pageNumber,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: PRODUCT_PICKER_PAGE_SIZE_OPTIONS,
            onChange: (nextPage, nextPageSize) =>
              void loadProducts({ pageNumber: nextPage, pageSize: nextPageSize }),
          }}
          scroll={{ y: 440 }}
        />
      </Space>
    </Modal>
  )
}

function BatchEditModal({ open, loading, selectedCount, onCancel, onConfirm }: BatchEditModalProps) {
  const { t } = useTranslation()
  const [type, setType] = useState<'allocQuantity' | 'importPrice' | 'status'>('allocQuantity')
  const [allocQuantity, setAllocQuantity] = useState<number>()
  const [importPrice, setImportPrice] = useState<number>()
  const [isActive, setIsActive] = useState<boolean>(true)

  useEffect(() => {
    if (!open) {
      setType('allocQuantity')
      setAllocQuantity(undefined)
      setImportPrice(undefined)
      setIsActive(true)
    }
  }, [open])

  const handleOk = async () => {
    if (selectedCount === 0) {
      message.warning(t('storeOrders.detail.selectLinesFirst'))
      return
    }

    if (type === 'allocQuantity' && (allocQuantity === undefined || allocQuantity < 0)) {
      message.warning(t('storeOrders.detail.enterValidAllocQty'))
      return
    }

    if (type === 'importPrice' && (importPrice === undefined || importPrice < 0)) {
      message.warning(t('storeOrders.detail.enterValidImportPrice'))
      return
    }

    await onConfirm({
      type,
      allocQuantity,
      importPrice,
      isActive,
    })
  }

  return (
    <Modal
      title={t('storeOrders.batchModifyTitle')}
      open={open}
      destroyOnClose
      okText={t('storeOrders.applyTo', { count: selectedCount })}
      cancelText={t('common.close')}
      confirmLoading={loading}
      onCancel={onCancel}
      onOk={() => void handleOk()}
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Select
          value={type}
          options={[
            { value: 'allocQuantity', label: t('storeOrders.batchAllocQty') },
            { value: 'importPrice', label: t('storeOrders.batchImportPrice') },
            { value: 'status', label: t('storeOrders.batchStatus') },
          ]}
          onChange={setType}
        />

        {type === 'allocQuantity' ? (
          <InputNumber
            min={0}
            precision={0}
            style={{ width: '100%' }}
            placeholder={t('storeOrders.newAllocQty')}
            value={allocQuantity}
            onChange={(value) => setAllocQuantity(value === null ? undefined : Number(value))}
          />
        ) : null}

        {type === 'importPrice' ? (
          <InputNumber
            min={0}
            precision={2}
            style={{ width: '100%' }}
            placeholder={t('storeOrders.newImportPrice')}
            value={importPrice}
            onChange={(value) => setImportPrice(value === null ? undefined : Number(value))}
          />
        ) : null}

        {type === 'status' ? (
          <Select
            value={isActive ? 'active' : 'inactive'}
            options={[
              { value: 'active', label: t('common.activeUpper') },
              { value: 'inactive', label: t('common.inactiveUpper') },
            ]}
            onChange={(value) => setIsActive(value === 'active')}
          />
        ) : null}
      </Space>
    </Modal>
  )
}

export default function StoreOrderDetailPage() {
  const { t } = useTranslation()
  const route = useStableRouteContext()
  const { active } = useKeepAliveContext()
  const location = useLocation()
  const navigate = useNavigate()
  const screens = Grid.useBreakpoint()
  const { access, currentUser } = useAuthStore()
  const canUseWarehouseManagerActions = access.isAdmin || access.isWarehouseManager
  // 只用于配货单等只读文档入口，不开放订单编辑、状态流转或明细写入能力。
  const canUseStoreOrderDocumentActions = access.isWarehouseStaff
  const canUseStoreOrderDetailExtraActions = canUseWarehouseManagerActions || canUseStoreOrderDocumentActions
  const id = route?.params.id || ''
  const isDesktop = Boolean(screens.xl)
  const detailRequestControllerRef = useRef<AbortController | null>(null)
  // 记录当前订单和查询条件已完成首次加载，保活 Tab 恢复时避免同条件自动刷新。
  const loadedDetailIdRef = useRef<string | null>(null)
  const visibleDetailIdRef = useRef<string | null>(null)
  const lastLoadedDetailQueryKeyRef = useRef<string | null>(null)
  const lastLoadedStoresQueryKeyRef = useRef<string | null>(null)
  const stopPasteReplacePollingRef = useRef<(() => void) | null>(null)
  const detailInputRefs = useRef<Record<string, InputNumberRef | null>>({})
  const [detailLoadStatus, setDetailLoadStatus] = useState<DetailLoadStatus>('idle')
  const [detailErrorMessage, setDetailErrorMessage] = useState('')
  const [detail, setDetail] = useState<StoreOrderDetail | null>(null)
  const [stores, setStores] = useState<StoreDto[]>([])
  const [storesLoading, setStoresLoading] = useState(false)
  const [savingHeader, setSavingHeader] = useState(false)
  const [statusChanging, setStatusChanging] = useState(false)
  const [lineActionLoading, setLineActionLoading] = useState(false)
  const [batchLoading, setBatchLoading] = useState(false)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [containerPickerOpen, setContainerPickerOpen] = useState(false)
  const [containerPickerLoading, setContainerPickerLoading] = useState(false)
  const [containerExistingProductCodes, setContainerExistingProductCodes] = useState<string[]>([])
  const [batchModalOpen, setBatchModalOpen] = useState(false)
  const [pasteModalOpen, setPasteModalOpen] = useState(false)
  const [quickAddItemNumber, setQuickAddItemNumber] = useState('')
  const [quickAddQuantity, setQuickAddQuantity] = useState<number>(1)
  const [pasteData, setPasteData] = useState('')
  const [pasteTargetField, setPasteTargetField] = useState<StoreOrderPasteWriteTarget>('allocQuantity')
  const [detailItemFilter, setDetailItemFilter] = useState('')
  const [columnMapping, setColumnMapping] = useState({
    itemNumber: 0,
    quantity: 1,
    price: -1,
  })
  const [pastePreviewItems, setPastePreviewItems] = useState<StoreOrderPastePreviewItem[]>([])
  const [pastePreviewFilter, setPastePreviewFilter] = useState<StoreOrderPastePreviewFilter>('all')
  const [parsingPaste, setParsingPaste] = useState(false)
  const [submittingPaste, setSubmittingPaste] = useState(false)
  const [pasteOptimisticPending, setPasteOptimisticPending] = useState<StoreOrderPasteOptimisticPending | null>(null)
  const [refreshImportPriceLoading, setRefreshImportPriceLoading] = useState(false)
  const [detailPage, setDetailPage] = useState(1)
  const [detailPageSize, setDetailPageSize] = useState(STORE_ORDER_DETAIL_DEFAULT_PAGE_SIZE)
  const [detailStatFilter, setDetailStatFilter] = useState<StoreOrderDetailStatFilter>('all')
  const [detailSortField, setDetailSortField] = useState<DetailSortField>('locationCode')
  const [detailSortOrder, setDetailSortOrder] = useState<SortOrder>('ascend')
  const [headerForm, setHeaderForm] = useState<{
    storeCode?: string
    orderDate?: string
    outboundDate?: string
    shippingFee?: number
    address: string
    contactEmail: string
    remarks: string
  }>({
    storeCode: undefined,
    orderDate: undefined,
    outboundDate: undefined,
    shippingFee: undefined,
    address: '',
    contactEmail: '',
    remarks: '',
  })
  const [storeContactBaseline, setStoreContactBaseline] = useState({
    address: '',
    contactEmail: '',
  })
  const [selectedLineKeys, setSelectedLineKeys] = useState<React.Key[]>([])
  const [editingRows, setEditingRows] = useState<Record<string, { allocQuantity?: number; importPrice?: number }>>({})
  const initialOrderNo =
    typeof location.state === 'object' &&
    location.state !== null &&
    'orderNo' in location.state &&
    typeof location.state.orderNo === 'string'
      ? location.state.orderNo
      : ''
  const tabTitle = detail?.orderNo || initialOrderNo || t('storeOrders.orderDetail')

  useDynamicTabTitle(tabTitle)

  // 明细表只请求当前页；翻页、筛选、排序都会带着 pageSize 重新向服务端取数。
  const detailQuery = useMemo<StoreOrderDetailQuery>(
    () => ({
      pageNumber: detailPage,
      pageSize: detailPageSize,
      keyword: detailItemFilter.trim() || undefined,
      statFilter: detailStatFilter === 'all' ? undefined : detailStatFilter,
      sortBy: detailSortField || undefined,
      sortDescending: detailSortField ? detailSortOrder === 'descend' : undefined,
    }),
    [detailItemFilter, detailPage, detailPageSize, detailSortField, detailSortOrder, detailStatFilter],
  )
  const detailQueryKey = useMemo(() => JSON.stringify(detailQuery), [detailQuery])
  const storesQueryKey = useMemo(
    () =>
      JSON.stringify({
        id,
        isAdmin: access.isAdmin,
        isWarehouseManager: access.isWarehouseManager,
        userGUID: currentUser?.userGUID ?? '',
      }),
    [access.isAdmin, access.isWarehouseManager, currentUser?.userGUID, id],
  )

  const loadDetail = async (showLoading = true) => {
    if (!id) {
      return
    }

    detailRequestControllerRef.current?.abort()
    const currentController = new AbortController()
    detailRequestControllerRef.current = currentController

    if (showLoading) {
      setDetailLoadStatus('loading')
    }

    try {
      const result = await getStoreOrderDetail(id, detailQuery, detailRequestControllerRef.current.signal)

      if (detailRequestControllerRef.current !== currentController) {
        return
      }

      if (!result) {
        loadedDetailIdRef.current = null
        visibleDetailIdRef.current = null
        lastLoadedDetailQueryKeyRef.current = null
        setDetail(null)
        setDetailLoadStatus('notFound')
        setDetailErrorMessage('')
        return
      }

      const maxPage = Math.max(1, Math.ceil((result.itemsTotal ?? result.items.length) / detailPageSize))
      if (detailPage > maxPage) {
        // 删除或筛选后当前页可能超过服务端总页数，回退后由 effect 重新请求有效页。
        setDetailPage(maxPage)
        return
      }

      loadedDetailIdRef.current = result.orderGUID || id
      visibleDetailIdRef.current = result.orderGUID || id
      lastLoadedDetailQueryKeyRef.current = detailQueryKey
      setDetail(result)
      setHeaderForm({
        storeCode: result?.storeCode,
        orderDate: result?.orderDate,
        outboundDate: result?.outboundDate,
        shippingFee: result?.shippingFee,
        address: result?.storeAddress || '',
        contactEmail: result?.storeContactEmail || '',
        remarks: result?.remarks || '',
      })
      setStoreContactBaseline({
        address: result?.storeAddress || '',
        contactEmail: result?.storeContactEmail || '',
      })
      setEditingRows({})
      setDetailLoadStatus('loaded')
      setDetailErrorMessage('')
    } catch (error) {
      if (isAbortError(error)) {
        return
      }

      if (detailRequestControllerRef.current !== currentController) {
        return
      }

      console.error(error)
      const errorMessage = error instanceof Error ? error.message : t('storeOrders.detail.loadDetailFailed')
      if (showLoading) {
        visibleDetailIdRef.current = null
        lastLoadedDetailQueryKeyRef.current = null
        setDetail(null)
        setDetailLoadStatus('error')
        setDetailErrorMessage(errorMessage)
      } else {
        setDetailErrorMessage(errorMessage)
      }
      message.error(errorMessage)
    } finally {
      if (detailRequestControllerRef.current === currentController) {
        detailRequestControllerRef.current = null
      }
    }
  }

  const loadStores = async () => {
    if (!canUseWarehouseManagerActions) {
      // 仓库员工只能查看当前订单分店，不能访问完整分店下拉接口；避免辅助数据 403 阻断明细展示。
      setStores([])
      lastLoadedStoresQueryKeyRef.current = storesQueryKey
      return
    }

    setStoresLoading(true)
    try {
      const result = await getStores({
        page: 1,
        pageSize: 300,
        isActive: true,
        sortField: 'storeName',
        sortOrder: 'ascend',
      })
      setStores(result.items)
      lastLoadedStoresQueryKeyRef.current = storesQueryKey
    } catch (error) {
      console.error(error)
      // 分店下拉是辅助数据，加载失败不应误导用户以为订货明细主数据失败。
      message.warning(t('storeOrders.detail.loadStoreOptionsFailed'))
    } finally {
      setStoresLoading(false)
    }
  }

  useEffect(() => {
    if (!active) return

    if (!id) {
      return
    }
    // 隐藏的 KeepAlive 节点也会收到全局路由变化，必须只让当前激活节点发起请求。
    // 保活 Tab 切回时只有同订单且同查询条件命中才跳过，分页/搜索/排序变化必须重新请求。
    if (shouldSkipDetailAutoReload({
      requestedDetailId: id,
      loadedDetailId: loadedDetailIdRef.current,
      visibleDetailId: visibleDetailIdRef.current,
      requestedDetailQueryKey: detailQueryKey,
      loadedDetailQueryKey: lastLoadedDetailQueryKeyRef.current,
    })) {
      return
    }

    const shouldShowInitialLoading = shouldShowStoreOrderDetailInitialLoading({
      requestedOrderId: id,
      loadedOrderId: loadedDetailIdRef.current,
      visibleDetailId: visibleDetailIdRef.current,
    })
    void loadDetail(shouldShowInitialLoading)
    return () => {
      detailRequestControllerRef.current?.abort()
    }
  }, [active, detailQuery, detailQueryKey, id])

  useEffect(() => {
    if (!active) return

    if (!id) {
      return
    }
    if (lastLoadedStoresQueryKeyRef.current === storesQueryKey) {
      return
    }
    void loadStores()
  }, [active, storesQueryKey, id])

  useEffect(() => {
    if (!containerPickerOpen) {
      setContainerExistingProductCodes([])
    }
  }, [containerPickerOpen])

  useEffect(() => {
    return () => {
      stopPasteReplacePollingRef.current?.()
      stopPasteReplacePollingRef.current = null
      setPasteOptimisticPending(null)
    }
  }, [id])

  const handleOpenContainerPicker = async () => {
    if (!detail?.orderGUID) {
      return
    }

    setContainerPickerLoading(true)
    try {
      // 必须先加载整单商品编码，避免分页详情页只按当前页做货柜选品去重。
      const productCodes = await getStoreOrderDetailProductCodes(detail.orderGUID)
      setContainerExistingProductCodes(productCodes)
      setContainerPickerOpen(true)
    } catch (error) {
      console.error(error)
      setContainerExistingProductCodes([])
      setContainerPickerOpen(false)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.loadDetailFailed'))
    } finally {
      setContainerPickerLoading(false)
    }
  }

  const storeOptions = useMemo(
    () => {
      const options = stores.map((item) => ({
        value: item.storeCode,
        label: `${item.storeName} (${item.storeCode})`,
      }))

      if (headerForm.storeCode && !options.some((item) => item.value === headerForm.storeCode)) {
        const currentStoreLabel = detail?.storeName
          ? `${detail.storeName} (${headerForm.storeCode})`
          : `${headerForm.storeCode} (${t('column.currentStore')})`

        options.push({
          value: headerForm.storeCode,
          label: currentStoreLabel,
        })
      }

      return options
    },
    [detail?.storeName, headerForm.storeCode, stores, t],
  )

  const totalAllocQuantity =
    detail?.totalAllocQuantity ?? detail?.items?.reduce((sum, item) => sum + (item.allocQuantity || 0), 0) ?? 0

  const totalOrderVolume =
    detail?.totalOrderVolume ??
    detail?.totalVolume ??
    detail?.items?.reduce(
      (sum, item) =>
        sum +
        (item.orderVolume ??
          item.totalVolume ??
          ((item.volume ?? 0) * Number(item.quantity ?? 0))),
      0,
    ) ??
    0

  const totalAllocVolume =
    detail?.totalAllocVolume ??
    detail?.items?.reduce(
      (sum, item) =>
        sum +
        (item.allocVolume ??
          ((item.volume ?? 0) * Number(item.allocQuantity ?? 0))),
      0,
    ) ??
    0

  const estimatedSalesAmount = useMemo(
    () =>
      detail?.items.reduce((sum, line) => {
        const allocQuantity = editingRows[line.detailGUID]?.allocQuantity ?? line.allocQuantity ?? 0
        return sum + Number(line.price ?? 0) * Number(allocQuantity)
      }, 0) ?? 0,
    [detail?.items, editingRows],
  )

  const gstAmount = useMemo(
    () => Number((Number(detail?.totalImportAmount ?? 0) * 0.1).toFixed(2)),
    [detail?.totalImportAmount],
  )

  const validPastePreviewCount = useMemo(
    () => buildPasteSubmitItems(pastePreviewItems).length,
    [pastePreviewItems],
  )
  const pasteQuantityMode = resolvePasteQuantityMode(pasteTargetField)
  const pasteApiTargetField = resolvePasteTargetField(pasteTargetField)

  const filteredPastePreviewItems = useMemo(
    () => filterPastePreviewItems(pastePreviewItems, pastePreviewFilter),
    [pastePreviewFilter, pastePreviewItems],
  )

  const existingPastePreviewCount = useMemo(
    () => pastePreviewItems.filter((item) => item.status === 'existing' && item.valid).length,
    [pastePreviewItems],
  )

  const selectedLines = useMemo(
    () => detail?.items.filter((item) => selectedLineKeys.includes(item.detailGUID)) ?? [],
    [detail?.items, selectedLineKeys],
  )

  const getEditedLinePayloads = (syncImportPrice = true) =>
    (detail?.items ?? []).reduce<EditedLinePayload[]>((payloads, line) => {
      const edited = editingRows[line.detailGUID]
      if (!edited) {
        return payloads
      }

      const allocQuantityChanged =
        edited.allocQuantity !== undefined && Number(edited.allocQuantity) !== Number(line.allocQuantity ?? 0)
      const importPriceChanged =
        edited.importPrice !== undefined && Number(edited.importPrice) !== Number(line.importPrice ?? 0)

      if (!allocQuantityChanged && !importPriceChanged) {
        return payloads
      }

      if (importPriceChanged && !syncImportPrice && !allocQuantityChanged) {
        return payloads
      }

      payloads.push({
        detailGUID: line.detailGUID,
        productCode: line.productCode,
        quantity: allocQuantityChanged ? edited.allocQuantity : undefined,
        importPrice: importPriceChanged && syncImportPrice ? edited.importPrice : undefined,
        importPriceChanged,
      })

      return payloads
    }, [])

  const editedLineCount = useMemo(() => getEditedLinePayloads().length, [detail?.items, editingRows])

  const statSummary = useMemo(() => {
    const items = detail?.items ?? []
    const total = detail?.itemsTotal ?? items.length

    return {
      all: total,
      orderedNotShipped: detail?.orderedNotShippedCount ?? items.filter(isOrderedNotShipped).length,
      shippedWithoutOrder: detail?.shippedWithoutOrderCount ?? items.filter(isShippedWithoutOrder).length,
    }
  }, [detail?.items, detail?.itemsTotal, detail?.orderedNotShippedCount, detail?.shippedWithoutOrderCount])

  const statusLabelMap = useMemo(
    () => ({
      0: t('storeOrders.statusShoppingCart'),
      1: t('storeOrders.statusSubmitted'),
      2: t('storeOrders.statusCompleted'),
      3: t('storeOrders.statusPicking'),
    }),
    [t],
  )

  const orderStatusChangeOptions = useMemo(
    () =>
      [
        StoreOrderFlowStatus.Submitted,
        StoreOrderFlowStatus.Picking,
        StoreOrderFlowStatus.Completed,
      ].map((value) => ({
        value,
        label: statusLabelMap[value],
        disabled: value === detail?.flowStatus,
      })),
    [detail?.flowStatus, statusLabelMap],
  )

  const {
    canEditOrder,
    canEditOutboundDate,
    canStartPicking,
    canCompleteOrder,
    isReadonlyOrder,
  } = deriveStoreOrderDetailPermissions(detail?.flowStatus)
  const isPasteOptimisticPreviewActive = pasteOptimisticPending?.orderGUID === detail?.orderGUID

  function ensureOrderEditable() {
    if (canUseWarehouseManagerActions && canEditOrder) {
      return true
    }

    message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
    return false
  }

  const hasImportPriceChanged = (line: StoreOrderDetailLine) => {
    const edited = editingRows[line.detailGUID]
    return edited?.importPrice !== undefined && Number(edited.importPrice) !== Number(line.importPrice ?? 0)
  }

  const confirmImportPriceSync = () =>
    new Promise<boolean | null>((resolve) => {
      let syncImportPrice = true
      Modal.confirm({
        title: t('storeOrders.detail.importPriceSyncConfirmTitle'),
        content: (
          <Space direction="vertical" size={8}>
            <Typography.Text>{t('storeOrders.detail.importPriceSyncConfirmContent')}</Typography.Text>
            <Checkbox
              defaultChecked
              onChange={(event) => {
                syncImportPrice = event.target.checked
              }}
            >
              {t('storeOrders.detail.syncImportPriceCheckbox')}
            </Checkbox>
          </Space>
        ),
        okText: t('common.confirm'),
        cancelText: t('common.cancel'),
        onOk: () => resolve(syncImportPrice),
        onCancel: () => resolve(null),
      })
    })

  const handleSaveHeader = async () => {
    if (!detail) {
      return
    }
    if (!canUseWarehouseManagerActions) {
      message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
      return
    }
    setSavingHeader(true)
    try {
      if (canEditOrder) {
        await updateStoreOrderHeader({
          orderGUID: detail.orderGUID,
          storeCode: headerForm.storeCode,
          orderDate: headerForm.orderDate,
          shippingFee: headerForm.shippingFee,
          remarks: headerForm.remarks,
        })
        const nextStoreAddress = headerForm.address.trim() ? headerForm.address : ''
        const nextStoreContactEmail = headerForm.contactEmail.trim() ? headerForm.contactEmail : ''
        const hasStoreContactChanged =
          (nextStoreAddress || '') !== storeContactBaseline.address ||
          (nextStoreContactEmail || '') !== storeContactBaseline.contactEmail

        if (hasStoreContactChanged && detail.orderGUID && headerForm.storeCode) {
          await updateStoreOrderStoreContact({
            orderGUID: detail.orderGUID,
            storeCode: headerForm.storeCode,
            address: nextStoreAddress,
            contactEmail: nextStoreContactEmail,
          })
          // 保存成功后把当前编辑值视为这家分店的最新默认值，避免继续误判成旧默认值。
          setStoreContactBaseline({
            address: nextStoreAddress,
            contactEmail: nextStoreContactEmail,
          })
        }
      }

      const currentOutboundDate = detail.outboundDate?.slice(0, 10) || ''
      const nextOutboundDate = headerForm.outboundDate?.slice(0, 10) || ''
      if (currentOutboundDate !== nextOutboundDate) {
        await updateStoreOrderOutboundDate({
          orderGUID: detail.orderGUID,
          outboundDate: nextOutboundDate || undefined,
          completeOrder: false,
        })
      }

      message.success(t('storeOrders.detail.headerSaveSuccess'))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.headerSaveFailed'))
    } finally {
      setSavingHeader(false)
    }
  }

  const handleQuickAdd = async () => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }
    const normalizedItemNumber = quickAddItemNumber.trim()
    if (!normalizedItemNumber) {
      message.warning(t('storeOrders.detail.enterItemNumber'))
      return
    }
    if (!quickAddQuantity || quickAddQuantity <= 0) {
      message.warning(t('storeOrders.detail.enterValidAllocQty'))
      return
    }

    setLineActionLoading(true)
    try {
      const result = await getStoreOrderProducts({
        itemNumber: normalizedItemNumber,
        // 快速加入允许下架商品按货号加入，但后端仍会排除已删除商品。
        includeInactiveWarehouseProducts: true,
        pageNumber: 1,
        pageSize: 50,
        sortBy: 'Default',
      })
      const exactMatches = result.items.filter(
        (item) => item.itemNumber?.trim().toLowerCase() === normalizedItemNumber.toLowerCase(),
      )

      if (exactMatches.length === 0) {
        message.warning(t('storeOrders.detail.noExactItemMatch'))
        return
      }

      if (exactMatches.length > 1) {
        message.warning(t('storeOrders.detail.multipleExactItemMatches'))
        return
      }

      const target = exactMatches[0]

      await addStoreOrderLine({
        orderGUID: detail.orderGUID,
        productCode: target.productCode,
        quantity: quickAddQuantity,
      })
      message.success(t('storeOrders.detail.productAdded'))
      setQuickAddItemNumber('')
      setQuickAddQuantity(1)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.quickAddFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handlePickerConfirm = async (items: Array<{ productCode: string; quantity: number; importPrice?: number }>) => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }
    setLineActionLoading(true)
    try {
      if (items.length === 1) {
        await addStoreOrderLine({
          orderGUID: detail.orderGUID,
          productCode: items[0].productCode,
          quantity: items[0].quantity,
        })
      } else {
        await batchAddStoreOrderLines({
          orderGUID: detail.orderGUID,
          items,
        })
      }
      message.success(t('storeOrders.addProductsSuccess', { count: items.length }))
      setPickerOpen(false)
      setContainerPickerOpen(false)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.addProductsFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleSaveLine = async (line: StoreOrderDetailLine) => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }

    const edited = editingRows[line.detailGUID]
    const allocQuantity = edited?.allocQuantity ?? line.allocQuantity ?? 0
    const importPrice = edited?.importPrice ?? line.importPrice

    if (allocQuantity < 0) {
      message.warning(t('storeOrders.detail.allocQtyNonNegative'))
      return
    }

    let syncImportPrice = true
    if (hasImportPriceChanged(line)) {
      const syncChoice = await confirmImportPriceSync()
      if (syncChoice === null) {
        return
      }
      syncImportPrice = syncChoice
      if (!syncImportPrice && (edited?.allocQuantity === undefined || Number(edited.allocQuantity) === Number(line.allocQuantity ?? 0))) {
        return
      }
    }

    setLineActionLoading(true)
    try {
      await updateStoreOrderLine({
        orderGUID: detail.orderGUID,
        productCode: line.productCode,
        allocQuantity,
        importPrice: syncImportPrice ? importPrice : undefined,
      })
      message.success(t('storeOrders.detail.lineSaved'))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.lineSaveFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleSaveEditedLines = async () => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }

    const payloads = getEditedLinePayloads()
    if (!payloads.length) {
      message.warning(t('storeOrders.detail.noEditedLines'))
      return
    }

    const invalidQuantity = payloads.find((item) => item.quantity !== undefined && item.quantity < 0)
    if (invalidQuantity) {
      message.warning(t('storeOrders.detail.allocQtyNonNegative'))
      return
    }

    let syncImportPrice = true
    if (payloads.some((item) => item.importPriceChanged)) {
      const syncChoice = await confirmImportPriceSync()
      if (syncChoice === null) {
        return
      }
      syncImportPrice = syncChoice
      if (!syncImportPrice) {
        const nextPayloads = getEditedLinePayloads(syncImportPrice)
        if (!nextPayloads.length) {
          return
        }
        payloads.splice(0, payloads.length, ...nextPayloads)
      }
      if (!syncImportPrice && !payloads.length) {
        return
      }
    }

    setLineActionLoading(true)
    try {
      await batchUpdateStoreOrderLines({
        orderGUID: detail.orderGUID,
        items: payloads.map((item) => ({
          productCode: item.productCode,
          quantity: item.quantity,
          importPrice: item.importPrice,
        })),
      })
      message.success(t('storeOrders.detail.editedLinesSaved', { count: payloads.length }))
      const savedDetailGUIDs = new Set(payloads.map((item) => item.detailGUID))
      setEditingRows((current) => {
        const next = { ...current }
        savedDetailGUIDs.forEach((detailGUID) => {
          delete next[detailGUID]
        })
        return next
      })
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.batchUpdateFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleRemoveLine = async (line: StoreOrderDetailLine) => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }
    setLineActionLoading(true)
    try {
      await removeStoreOrderLine({
        orderGUID: detail.orderGUID,
        detailGUID: line.detailGUID,
      })
      message.success(t('storeOrders.detail.lineDeleted'))
      setSelectedLineKeys((current) => current.filter((key) => key !== line.detailGUID))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.lineDeleteFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleToggleLineStatus = async (line: StoreOrderDetailLine) => {
    if (!ensureOrderEditable()) {
      return
    }
    setLineActionLoading(true)
    try {
      await updateStoreOrderProductStatus({
        productCode: line.productCode,
        isActive: !line.isActive,
      })
      message.success(t('storeOrders.detail.productStatusUpdated', { status: line.isActive ? t('common.inactiveUpper') : t('common.activeUpper') }))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.productStatusUpdateFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleBatchConfirm = async (payload: {
    type: 'allocQuantity' | 'importPrice' | 'status'
    allocQuantity?: number
    importPrice?: number
    isActive?: boolean
  }) => {
    if (!detail || selectedLines.length === 0) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }

    setBatchLoading(true)
    try {
      if (payload.type === 'status') {
        await batchUpdateStoreOrderProductStatus({
          productCodes: selectedLines.map((item) => item.productCode),
          isActive: payload.isActive ?? true,
        })
      } else {
        await batchUpdateStoreOrderLines({
          orderGUID: detail.orderGUID,
          items: selectedLines.map((item) => ({
            productCode: item.productCode,
            quantity: payload.type === 'allocQuantity' ? payload.allocQuantity : undefined,
            importPrice: payload.type === 'importPrice' ? payload.importPrice : undefined,
          })),
        })
      }

      message.success(t('storeOrders.batchUpdateSuccess', { count: selectedLines.length }))
      setBatchModalOpen(false)
      setSelectedLineKeys([])
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.batchUpdateFailed'))
    } finally {
      setBatchLoading(false)
    }
  }

  const handleResetDetailDefaultSort = () => {
    // 默认排序与配货单保持一致：空货位在前，再按货位升序查看整单明细。
    setSelectedLineKeys([])
    setDetailPage(1)
    setDetailSortField('locationCode')
    setDetailSortOrder('ascend')
  }

  const handleRefreshImportPricesFromWarehouse = () => {
    if (!detail || !canUseWarehouseManagerActions) {
      return
    }

    const targetDetailGUIDs = selectedLineKeys.map(String).filter(Boolean)
    const isSelectedScope = targetDetailGUIDs.length > 0
    const selectedCount = targetDetailGUIDs.length

    Modal.confirm({
      title: t('storeOrders.detail.refreshImportPricesTitle'),
      content: (
        <Space direction="vertical" size={8}>
          <Typography.Text>
            {isSelectedScope
              ? t('storeOrders.detail.refreshImportPricesSelectedContent', { count: selectedCount })
              : t('storeOrders.detail.refreshImportPricesWholeOrderContent')}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('storeOrders.detail.refreshImportPricesWarning')}
          </Typography.Text>
        </Space>
      ),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        setRefreshImportPriceLoading(true)
        try {
          const result = await refreshStoreOrderImportPrices({
            orderGUID: detail.orderGUID,
            detailGUIDs: isSelectedScope ? targetDetailGUIDs : undefined,
          })
          message.success(
            t('storeOrders.detail.refreshImportPricesSuccess', {
              updated: result.updatedCount,
              unchanged: result.unchangedCount,
              skipped: result.skippedCount,
              missing: result.missingWarehousePriceCount,
            }),
          )
          setSelectedLineKeys([])
          setEditingRows({})
          await loadDetail(false)
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('storeOrders.detail.refreshImportPricesFailed'))
        } finally {
          setRefreshImportPriceLoading(false)
        }
      },
    })
  }

  const resetPasteState = (targetField: StoreOrderPasteWriteTarget = 'allocQuantity') => {
    setPasteData('')
    setPasteTargetField(targetField)
    setColumnMapping({
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    setPastePreviewItems([])
    setPastePreviewFilter('all')
  }

  const handleParsePasteData = async () => {
    if (!detail) {
      return
    }
    if (!pasteData.trim()) {
      message.warning(t('storeOrders.detail.pasteExcelFirst'))
      return
    }

    setParsingPaste(true)
    try {
      const items = parseStoreOrderPasteRows(pasteData, columnMapping)

      if (!items.length) {
        message.warning(t('storeOrders.detail.noValidPasteItems'))
        setPastePreviewItems([])
        return
      }

      const [lookupResult, fullDetail] = await Promise.all([
        batchLookupStoreOrderProducts({
          codes: Array.from(new Set(items.map((item) => item.itemNumber.trim()).filter(Boolean))),
        }),
        getStoreOrderDetailFull(detail.orderGUID),
      ])
      const existingLines = (fullDetail?.items ?? []).map((item) => ({
        productCode: item.productCode,
        quantity: item.quantity,
        allocQuantity: item.allocQuantity,
      }))
      const preview = createPastePreviewItems(items, lookupResult, existingLines)

      setPastePreviewItems(preview)
      setPastePreviewFilter('all')
      message.success(t('storeOrders.detail.pasteParseSuccess', { total: items.length, valid: buildPasteSubmitItems(preview).length }))
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.pasteParseFailed'))
    } finally {
      setParsingPaste(false)
    }
  }

  const handleConfirmPaste = async () => {
    if (!detail) {
      return
    }
    if (!ensureOrderEditable()) {
      return
    }

    const validItems = buildPasteSubmitItems(pastePreviewItems, { quantityMode: pasteQuantityMode })

    if (!validItems.length) {
      message.warning(t('storeOrders.detail.noValidImportProducts'))
      return
    }

    setSubmittingPaste(true)
    try {
      const orderGUID = detail.orderGUID
      stopPasteReplacePollingRef.current?.()
      stopPasteReplacePollingRef.current = null
      setPasteOptimisticPending(null)

      const createdJob = await createStoreOrderPasteReplaceJob({
        orderGUID,
        targetField: pasteApiTargetField,
        items: validItems,
      })
      if (!createdJob.jobId) {
        message.error(createdJob.message || t('storeOrders.detail.pasteImportFailed'))
        return
      }

      const optimisticRows = buildPasteOptimisticRows({
        currentItems: detail.items,
        previewItems: pastePreviewItems,
        targetField: pasteApiTargetField,
        quantityMode: pasteQuantityMode,
      })

      // 后端批量导入改为后台 job，前端只负责等待终态并刷新当前订单。
      const poller = createStoreOrderPasteReplaceJobPoller({
        jobId: createdJob.jobId,
        getJob: getStoreOrderPasteReplaceJob,
      })
      stopPasteReplacePollingRef.current = poller.stop

      // Job 创建成功后先展示 Excel 预览行，避免大批量写入期间表格长时间没有反馈。
      setPasteOptimisticPending({
        jobId: createdJob.jobId,
        orderGUID,
      })
      setDetail((current) =>
        current?.orderGUID === orderGUID
          ? applyPasteOptimisticRowsToDetail(current, optimisticRows)
          : current,
      )
      setDetailPage(1)
      setSelectedLineKeys([])
      setEditingRows({})
      setPasteModalOpen(false)
      resetPasteState(pasteTargetField)
      message.success(t('storeOrders.detail.pasteJobSubmitted', '已先显示本次 Excel 预览，后台正在写入；完成后会自动刷新确认。'))

      void poller.promise
        .then(async (result) => {
          if (stopPasteReplacePollingRef.current === poller.stop) {
            stopPasteReplacePollingRef.current = null
          }
          setPasteOptimisticPending((current) => resolvePasteOptimisticPendingAfterJob(current, result))

          if (result.status === 'Failed') {
            notification.error({
              message: t('storeOrders.detail.pasteImportFailed'),
              description: result.message,
            })
            if (visibleDetailIdRef.current === orderGUID) {
              await loadDetail(false)
            }
            return
          }

          notification.success({
            message: t('storeOrders.pasteUpdateSuccess', { count: result.importedCount ?? validItems.length }),
            description: result.skippedCount
              ? t('storeOrders.detail.pasteSkippedCount', '已跳过 {{count}} 行', { count: result.skippedCount })
              : undefined,
          })
          if (visibleDetailIdRef.current === orderGUID) {
            await loadDetail(false)
          }
        })
        .catch(async (error) => {
          if (error instanceof StoreOrderPasteReplacePollingCancelledError) {
            return
          }
          if (stopPasteReplacePollingRef.current === poller.stop) {
            stopPasteReplacePollingRef.current = null
          }
          setPasteOptimisticPending((current) =>
            current?.jobId === createdJob.jobId ? null : current,
          )
          if (error instanceof StoreOrderPasteReplacePollingTimeoutError) {
            notification.warning({
              message: t('storeOrders.detail.pasteImportTimeout', 'Excel 粘贴导入仍在后台执行'),
              description: t('storeOrders.detail.pasteImportTimeoutDesc', '后台任务仍可能继续执行，请稍后刷新订单明细确认结果。'),
            })
            if (visibleDetailIdRef.current === orderGUID) {
              await loadDetail(false)
            }
            return
          }
          notification.error({
            message: t('storeOrders.detail.pasteImportFailed'),
            description: error instanceof Error ? error.message : undefined,
          })
          if (visibleDetailIdRef.current === orderGUID) {
            await loadDetail(false)
          }
        })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.pasteImportFailed'))
    } finally {
      setSubmittingPaste(false)
    }
  }

  const handleSetExistingPastePreviewAction = (action: StoreOrderPasteAction) => {
    setPastePreviewItems((current) => setExistingPastePreviewAction(current, action))
  }

  const handleChangePastePreviewAction = (rowIndex: number, action: StoreOrderPasteAction) => {
    setPastePreviewItems((current) => current.map((item) => (item.rowIndex === rowIndex ? { ...item, action } : item)))
  }

  const getDetailInputKey = (detailGUID: string, field: DetailEditableField) => `${detailGUID}:${field}`

  const registerDetailInput = (detailGUID: string, field: DetailEditableField, node: InputNumberRef | null) => {
    const key = getDetailInputKey(detailGUID, field)
    if (node) {
      detailInputRefs.current[key] = node
      return
    }
    delete detailInputRefs.current[key]
  }

  const focusDetailInput = (detailGUID: string, field: DetailEditableField) => {
    detailInputRefs.current[getDetailInputKey(detailGUID, field)]?.focus?.()
  }

  const handleDetailInputKeyDown = (
    event: KeyboardEvent<HTMLInputElement>,
    detailGUID: string,
    field: DetailEditableField,
  ) => {
    if (
      event.key !== 'ArrowRight' &&
      event.key !== 'ArrowLeft' &&
      event.key !== 'ArrowDown' &&
      event.key !== 'ArrowUp' &&
      event.key !== 'Enter'
    ) {
      return
    }

    event.preventDefault()
    event.stopPropagation()

    const rows = detail?.items ?? []
    const currentIndex = rows.findIndex((item) => item.detailGUID === detailGUID)
    if (currentIndex < 0) {
      return
    }

    let nextIndex = currentIndex
    let nextField: DetailEditableField = field

    if (event.key === 'ArrowRight') {
      nextField = field === 'allocQuantity' ? 'importPrice' : 'allocQuantity'
      nextIndex = field === 'allocQuantity' ? currentIndex : currentIndex + 1
    }
    if (event.key === 'ArrowLeft') {
      nextField = field === 'importPrice' ? 'allocQuantity' : 'importPrice'
      nextIndex = field === 'importPrice' ? currentIndex : currentIndex - 1
    }
    if (event.key === 'ArrowDown' || event.key === 'Enter') {
      nextIndex = currentIndex + 1
    }
    if (event.key === 'ArrowUp') {
      nextIndex = currentIndex - 1
    }

    const nextRow = rows[nextIndex]
    if (!nextRow) {
      return
    }

    focusDetailInput(nextRow.detailGUID, nextField)
  }

  const handleCompleteOrder = async () => {
    if (!detail) {
      return
    }
    if (!canUseWarehouseManagerActions || !canCompleteOrder) {
      message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
      return
    }
    Modal.confirm({
      title: t('storeOrders.detail.confirmCompleteTitle'),
      content: t('storeOrders.detail.confirmCompleteContent'),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          setLineActionLoading(true)
          const currentOutboundDate = headerForm.outboundDate?.slice(0, 10)
          // 完成订单时只在出库日期为空时补当天，避免覆盖已录入或刚在表单中填写的日期。
          const nextOutboundDate = currentOutboundDate || formatLocalDateForInput()
          await updateStoreOrderOutboundDate({
            orderGUID: detail.orderGUID,
            outboundDate: nextOutboundDate,
            completeOrder: true,
          })
          message.success(t('storeOrders.detail.orderCompleted'))
          await loadDetail(false)
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('storeOrders.detail.completeOrderFailed'))
        } finally {
          setLineActionLoading(false)
        }
      },
    })
  }

  const handleChangeOrderStatus = (newStatus: StoreOrderFlowStatus) => {
    if (!detail || detail.flowStatus === newStatus) {
      return
    }
    if (!canUseWarehouseManagerActions) {
      message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
      return
    }

    Modal.confirm({
      title: t('storeOrders.detail.statusChangeConfirmTitle'),
      content: t('storeOrders.detail.statusChangeConfirmContent', {
        orderNo: detail.orderNo || detail.orderGUID,
        status: statusLabelMap[newStatus],
      }),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          setStatusChanging(true)
          await updateStoreOrderStatus({
            orderGUID: detail.orderGUID,
            newStatus,
          })
          message.success(t('storeOrders.detail.statusChangeSuccess'))
          await loadDetail(false)
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('storeOrders.detail.statusChangeFailed'))
        } finally {
          setStatusChanging(false)
        }
      },
    })
  }

  const handleStartPicking = async () => {
    if (!detail) {
      return
    }
    if (!canUseWarehouseManagerActions || !canStartPicking) {
      message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
      return
    }
    try {
      setLineActionLoading(true)
      await startPickingStoreOrder(detail.orderGUID)
      message.success(t('storeOrders.detail.orderPickingStarted'))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.startPickingFailed'))
    } finally {
      setLineActionLoading(false)
    }
  }

  const renderZeroOrEmptyCell = (value: string | number | undefined | null) => {
    if (value === undefined || value === null || value === '') {
      return renderDangerValue('--')
    }
    if (value === 0) {
      return renderDangerValue('0')
    }
    return value
  }

  const getQuantityHighlight = (line: StoreOrderDetailLine) => {
    const quantity = toNumber(line.quantity)
    const allocQuantity = toNumber(editingRows[line.detailGUID]?.allocQuantity ?? line.allocQuantity)

    if (quantity === 0 || allocQuantity === 0 || isZeroOrEmpty(line.quantity) || isZeroOrEmpty(line.allocQuantity)) {
      return 'error' as const
    }
    if (quantity !== allocQuantity) {
      return 'warning' as const
    }
    return undefined
  }

  const renderQuantityText = (line: StoreOrderDetailLine) => {
    const quantity = toNumber(line.quantity)
    const allocQuantity = toNumber(editingRows[line.detailGUID]?.allocQuantity ?? line.allocQuantity)

    if (quantity === 0 || isZeroOrEmpty(line.quantity)) {
      return renderDangerValue(quantity.toString())
    }
    if (quantity !== allocQuantity) {
      return renderWarningValue(quantity.toString())
    }
    return quantity
  }

  const columns: ColumnsType<StoreOrderDetailLine> = ([
    {
      title: '#',
      dataIndex: 'index',
      width: 30,
      fixed: isDesktop ? 'left' : undefined,
      render: (_, __, index) => renderStoreOrderDetailNumericCell((detailPage - 1) * detailPageSize + index + 1),
    },
    {
      title: t('column.image'),
      dataIndex: 'productImage',
      width: 42,
      fixed: isDesktop ? 'left' : undefined,
      render: (value: string | undefined, record) => (
        <Image
          src={value}
          alt={record.productName}
          loading="lazy"
          width={30}
          height={30}
          style={{ borderRadius: 4, objectFit: 'cover' }}
          fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
        />
      ),
    },
    {
      title: t('column.itemNumber'),
      dataIndex: 'itemNumber',
      width: 78,
      fixed: isDesktop ? 'left' : undefined,
      sorter: true,
      sortOrder: detailSortField === 'itemNumber' ? detailSortOrder : null,
      render: (value: string | undefined) =>
        value ? (
          <Space size={4} wrap={false} className="store-order-nowrap">
            <Typography.Text>{value}</Typography.Text>
            <Button
              size="small"
              type="text"
              icon={<CopyOutlined />}
              title={t('common.copy')}
              aria-label={`${t('common.copy')} ${value}`}
              className="store-order-detail-copy-button"
              onClick={() => void copyTextToClipboard(value)}
            />
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: t('column.productName'),
      dataIndex: 'productName',
      width: 132,
      render: (value: string | undefined) =>
        value ? renderStoreOrderTwoLineText(value) : '--',
    },
    {
      title: t('column.barcode'),
      dataIndex: 'barcode',
      width: 104,
      render: (value: string | undefined) => (
        <BarcodePreview value={value} className="store-order-barcode-cell" textNoWrap showCopy={false} />
      ),
    },
    {
      title: t('column.oemPrice'),
      dataIndex: 'price',
      width: 62,
      render: (value: number | undefined) => renderStoreOrderDetailNumericCell(formatAmount(value)),
    },
    {
      title: t('column.location'),
      dataIndex: 'locationCode',
      width: 82,
      sorter: true,
      sortOrder: detailSortField === 'locationCode' ? detailSortOrder : null,
      render: (value: string | undefined) => <span className="store-order-nowrap">{renderZeroOrEmptyCell(value)}</span>,
    },
    {
      title: t('column.orderQuantity'),
      dataIndex: 'quantity',
      width: 58,
      render: (_, record) => renderStoreOrderDetailNumericCell(renderQuantityText(record)),
    },
    {
      title: t('column.allocQuantity'),
      dataIndex: 'allocQuantity',
      width: 70,
      render: (value: number | undefined, record) => (
        <InputNumber
          ref={(node) => registerDetailInput(record.detailGUID, 'allocQuantity', node)}
          min={0}
          precision={0}
          disabled={!canUseWarehouseManagerActions || isReadonlyOrder || isPasteOptimisticPreviewActive}
          status={getQuantityHighlight(record)}
          style={{ width: 60 }}
          value={editingRows[record.detailGUID]?.allocQuantity ?? value ?? 0}
          onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, 'allocQuantity')}
          onChange={(nextValue) =>
            setEditingRows((current) => ({
              ...current,
              [record.detailGUID]: {
                ...current[record.detailGUID],
                allocQuantity: nextValue === null ? undefined : Number(nextValue),
              },
            }))
          }
        />
      ),
    },
    {
      title: t('column.importPrice'),
      dataIndex: 'importPrice',
      width: 70,
      render: (value: number | undefined, record) => (
        <InputNumber
          ref={(node) => registerDetailInput(record.detailGUID, 'importPrice', node)}
          min={0}
          precision={2}
          disabled={!canUseWarehouseManagerActions || isReadonlyOrder || isPasteOptimisticPreviewActive}
          status={isZeroOrEmpty(editingRows[record.detailGUID]?.importPrice ?? value) ? 'error' : undefined}
          style={{ width: 60 }}
          value={editingRows[record.detailGUID]?.importPrice ?? value}
          onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, 'importPrice')}
          onChange={(nextValue) =>
            setEditingRows((current) => ({
              ...current,
              [record.detailGUID]: {
                ...current[record.detailGUID],
                importPrice: nextValue === null ? undefined : Number(nextValue),
              },
            }))
          }
        />
      ),
    },
    {
      title: t('column.importAmount'),
      dataIndex: 'importAmount',
      width: 76,
      render: (value: number | undefined) =>
        renderStoreOrderDetailNumericCell(value === undefined || value === null ? renderDangerValue('--') : value === 0 ? renderDangerValue('0.00') : formatAmount(value)),
    },
    {
      title: t('column.orderVolume'),
      dataIndex: 'orderVolume',
      width: 76,
      render: (value: number | undefined, record) => {
        const nextValue =
          value ??
          record.totalVolume ??
          (record.volume === undefined || record.volume === null
            ? undefined
            : Number(record.volume) * Number(record.quantity ?? 0))
        return nextValue === undefined || nextValue === null
          ? renderStoreOrderDetailNumericCell(renderDangerValue('--'))
          : nextValue === 0
            ? renderStoreOrderDetailNumericCell(renderDangerValue('0.00'))
            : renderStoreOrderDetailNumericCell(formatStoreOrderVolume(nextValue))
      },
    },
    {
      title: t('column.shipVolume'),
      dataIndex: 'allocVolume',
      width: 76,
      render: (value: number | undefined, record) => {
        const allocQuantity = editingRows[record.detailGUID]?.allocQuantity ?? record.allocQuantity ?? 0
        const nextValue =
          value ??
          (record.volume === undefined || record.volume === null
            ? undefined
            : Number(record.volume) * Number(allocQuantity))
        return nextValue === undefined || nextValue === null
          ? renderStoreOrderDetailNumericCell(renderDangerValue('--'))
          : nextValue === 0
            ? renderStoreOrderDetailNumericCell(renderDangerValue('0.00'))
            : renderStoreOrderDetailNumericCell(formatStoreOrderVolume(nextValue))
      },
    },
    {
      title: t('column.status'),
      dataIndex: 'isActive',
      width: 50,
      render: (value: boolean) => <Tag color={value ? 'success' : 'default'}>{value ? t('common.activeUpper') : t('common.inactiveUpper')}</Tag>,
    },
    {
      title: t('column.action'),
      key: 'actions',
      width: 86,
      fixed: isDesktop ? 'right' : undefined,
      render: (_, record) => (
        <Space size={4} wrap={false}>
          <Tooltip title={t('common.save')}>
            <Button
              size="small"
              type="text"
              icon={<SaveOutlined />}
              aria-label={t('common.save')}
              className="store-order-detail-action-button"
              disabled={!canUseWarehouseManagerActions || isReadonlyOrder || isPasteOptimisticPreviewActive}
              onClick={() => void handleSaveLine(record)}
            />
          </Tooltip>
          <Tooltip title={record.isActive ? t('common.inactiveUpper') : t('common.activeUpper')}>
            <Button
              size="small"
              type="text"
              icon={<EditOutlined />}
              aria-label={record.isActive ? t('common.inactiveUpper') : t('common.activeUpper')}
              className="store-order-detail-action-button"
              disabled={!canUseWarehouseManagerActions || isReadonlyOrder || isPasteOptimisticPreviewActive}
              onClick={() => void handleToggleLineStatus(record)}
            />
          </Tooltip>
          <Popconfirm
            title={t('storeOrders.detail.confirmDeleteLine')}
            okText={t('common.delete')}
            cancelText={t('common.cancel')}
            onConfirm={() => void handleRemoveLine(record)}
          >
            <Tooltip title={t('common.delete')}>
              <Button
                size="small"
                danger
                type="text"
                icon={<DeleteOutlined />}
                aria-label={t('common.delete')}
                className="store-order-detail-action-button"
                disabled={!canUseWarehouseManagerActions || isReadonlyOrder || isPasteOptimisticPreviewActive}
              />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ] as ColumnsType<StoreOrderDetailLine>).filter(
    (column) => canUseWarehouseManagerActions || column.key !== 'actions',
  )

  if (!id) {
    return (
      <PageContainer title={t('storeOrders.orderDetail')} subtitle={t('storeOrders.detail.missingOrderNoSubtitle')}>
        <Card>
          <Empty description={t('storeOrders.missingOrderNo')} />
        </Card>
      </PageContainer>
    )
  }

  return (
    <PageContainer
      title={t('storeOrders.detailTitleWithNo', { orderNo: tabTitle })}
      subtitle={t('storeOrders.orderDetailSubtitle')}
    >
      {detailLoadStatus === 'idle' || detailLoadStatus === 'loading' ? (
        <Card>
          <div
            style={{
              minHeight: 240,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Spin />
          </div>
        </Card>
      ) : detailLoadStatus === 'notFound' ? (
        <Card>
          <Empty description={t('storeOrders.detail.notFound')} />
        </Card>
      ) : detailLoadStatus === 'error' ? (
        <Card>
          <Empty description={detailErrorMessage || t('storeOrders.detail.loadDetailFailed')}>
            <Button type="primary" onClick={() => void loadDetail()}>
              {t('common.retry', '重试')}
            </Button>
          </Empty>
        </Card>
      ) : detail ? (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Card>
              {isReadonlyOrder ? (
                <Alert
                  type="warning"
                  showIcon
                  message={t('storeOrders.detail.orderReadonlyTitle')}
                  description={t('storeOrders.detail.orderReadonlyDescription')}
                  style={{ marginBottom: 16 }}
                />
              ) : null}
              <Descriptions
                column={3}
                size="small"
                extra={
                  canUseWarehouseManagerActions ? (
                    <Space wrap>
                      <Button
                        icon={<SaveOutlined />}
                        loading={savingHeader}
                        onClick={() => void handleSaveHeader()}
                      >
                        {t('storeOrders.saveOrderHeader')}
                      </Button>
                      <Button
                        icon={<CheckOutlined />}
                        loading={lineActionLoading}
                        disabled={isReadonlyOrder || !canStartPicking}
                        onClick={() => void handleStartPicking()}
                      >
                        {t('storeOrders.startPicking')}
                      </Button>
                      <Button
                        type="primary"
                        icon={<CheckOutlined />}
                        loading={lineActionLoading}
                        disabled={!canCompleteOrder}
                        onClick={() => void handleCompleteOrder()}
                      >
                        {t('storeOrders.completeOrder')}
                      </Button>
                      <Space size={4}>
                        <Typography.Text type="secondary">{t('storeOrders.detail.changeOrderStatus')}</Typography.Text>
                        <Select
                          style={{ width: 150 }}
                          placeholder={t('storeOrders.detail.changeOrderStatus')}
                          value={
                            orderStatusChangeOptions.some((item) => item.value === detail.flowStatus)
                              ? detail.flowStatus
                              : undefined
                          }
                          loading={statusChanging}
                          disabled={statusChanging}
                          options={orderStatusChangeOptions}
                          onChange={(value) => handleChangeOrderStatus(value)}
                        />
                      </Space>
                    </Space>
                  ) : undefined
                }
              >
                <Descriptions.Item label={t('storeOrders.orderNoLabel')}>{detail.orderNo || '--'}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.storeLabel')}>
                  <Select
                    showSearch
                    style={{ width: '100%' }}
                    loading={storesLoading}
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    value={headerForm.storeCode}
                    options={storeOptions}
                    optionFilterProp="label"
                    onChange={(value) => {
                      const nextStore = stores.find((item) => item.storeCode === value)
                      const nextStoreAddress = nextStore?.address || ''
                      const nextStoreContactEmail = nextStore?.contactEmail || ''

                      setHeaderForm((current) => ({
                        ...current,
                        storeCode: value,
                        address: resolveStoreContactDraftValue({
                          currentValue: current.address,
                          previousStoreValue: storeContactBaseline.address,
                          nextStoreValue: nextStoreAddress,
                        }),
                        contactEmail: resolveStoreContactDraftValue({
                          currentValue: current.contactEmail,
                          previousStoreValue: storeContactBaseline.contactEmail,
                          nextStoreValue: nextStoreContactEmail,
                        }),
                      }))
                      setStoreContactBaseline({
                        address: nextStoreAddress,
                        contactEmail: nextStoreContactEmail,
                      })
                    }}
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.statusLabel')}>
                  <Tag color={StoreOrderStatusColorMap[(detail.flowStatus || 0) as StoreOrderFlowStatus] || 'default'}>
                    {statusLabelMap[(detail.flowStatus || 0) as StoreOrderFlowStatus] || t('common.statusN', { n: detail.flowStatus ?? '--' })}
                  </Tag>
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.orderDateLabel')}>
                  <Input
                    type="date"
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    value={headerForm.orderDate ? headerForm.orderDate.slice(0, 10) : ''}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        orderDate: event.target.value ? new Date(event.target.value).toISOString() : undefined,
                      }))
                    }
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.outboundDate')}>
                  <Input
                    type="date"
                    disabled={!canUseWarehouseManagerActions || !canEditOutboundDate}
                    value={headerForm.outboundDate ? headerForm.outboundDate.slice(0, 10) : ''}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        outboundDate: event.target.value || undefined,
                      }))
                    }
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.orderQtyLabel')}>{detail.totalQuantity}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.shipQtyLabel')}>{totalAllocQuantity}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.orderVolumeLabel')}>{formatStoreOrderVolume(totalOrderVolume)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.shipVolumeLabel')}>{formatStoreOrderVolume(totalAllocVolume)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.orderAmountLabel')}>{formatAmount(estimatedSalesAmount)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.importAmountLabel')}>{formatAmount(detail.totalImportAmount)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.gstAmountLabel')}>{formatAmount(gstAmount)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.freightLabel')}>
                  <InputNumber
                    min={0}
                    precision={2}
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    style={{ width: '100%' }}
                    value={headerForm.shippingFee}
                    onChange={(value) =>
                      setHeaderForm((current) => ({
                        ...current,
                        shippingFee: value === null ? undefined : Number(value),
                      }))
                    }
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.addressLabel')} span={2}>
                  <Input.TextArea
                    rows={2}
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    value={headerForm.address}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        address: event.target.value,
                      }))
                    }
                    placeholder={t('storeOrders.addressLabel')}
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.contactEmailLabel')}>
                  <Input
                    type="email"
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    value={headerForm.contactEmail}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        contactEmail: event.target.value,
                      }))
                    }
                    placeholder={t('storeOrders.contactEmailLabel')}
                  />
                </Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.skuCountLabel')}>{detail.totalSKU ?? detail.items.length}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.remarksLabel')} span={3}>
                  <Input.TextArea
                    rows={3}
                    disabled={!canUseWarehouseManagerActions || isReadonlyOrder}
                    value={headerForm.remarks}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        remarks: event.target.value,
                      }))
                    }
                    placeholder={t('common.enterRemarks')}
                  />
                </Descriptions.Item>
              </Descriptions>
            </Card>

            <Card
              title={t('storeOrders.orderDetailSection')}
              extra={
                canUseStoreOrderDetailExtraActions ? (
                  <Space wrap>
                    {canUseWarehouseManagerActions ? (
                      <Input
                        allowClear
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive}
                        placeholder={t('storeOrders.quickAddPlaceholder')}
                        style={{ width: 220 }}
                        value={quickAddItemNumber}
                        onChange={(event) => setQuickAddItemNumber(event.target.value)}
                        onPressEnter={() => void handleQuickAdd()}
                      />
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <InputNumber
                        min={1}
                        precision={0}
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive}
                        placeholder={t('storeOrders.allocQtyPlaceholder')}
                        value={quickAddQuantity}
                        onChange={(value) => setQuickAddQuantity(Number(value ?? 1))}
                      />
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<PlusOutlined />}
                        loading={lineActionLoading}
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive}
                        onClick={() => void handleQuickAdd()}
                      >
                        {t('storeOrders.quickAdd')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button icon={<SearchOutlined />} disabled={isReadonlyOrder || isPasteOptimisticPreviewActive} onClick={() => setPickerOpen(true)}>
                        {t('storeOrders.selectProduct')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<ContainerOutlined />}
                        loading={containerPickerLoading}
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive}
                        onClick={() => void handleOpenContainerPicker()}
                      >
                        {t('storeOrders.containerPicker')}
                      </Button>
                    ) : null}
                    {detail ? (
                      <Button
                        icon={<PrinterOutlined />}
                        onClick={() => navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)}
                      >
                        {t('storeOrders.pickingList')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<FileTextOutlined />}
                        onClick={() => detail && navigate(`/warehouse/store-order/invoice/${detail.orderGUID}`)}
                      >
                        {t('storeOrders.invoice')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<CopyOutlined />}
                        className="store-order-excel-paste-button"
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive}
                        onClick={() => {
                          resetPasteState('allocQuantity')
                          setPasteModalOpen(true)
                        }}
                      >
                        {t('storeOrders.excelPaste')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<SaveOutlined />}
                        className="store-order-save-edited-lines-button"
                        loading={lineActionLoading}
                        disabled={isReadonlyOrder || isPasteOptimisticPreviewActive || editedLineCount === 0}
                        onClick={() => void handleSaveEditedLines()}
                      >
                        {t('storeOrders.detail.saveEditedLines')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Button
                        icon={<SyncOutlined />}
                        loading={refreshImportPriceLoading}
                        disabled={!detail || isPasteOptimisticPreviewActive || refreshImportPriceLoading}
                        onClick={handleRefreshImportPricesFromWarehouse}
                      >
                        {t('storeOrders.detail.refreshImportPrices')}
                      </Button>
                    ) : null}
                    <Button
                      icon={<SortAscendingOutlined />}
                      disabled={isPasteOptimisticPreviewActive}
                      onClick={handleResetDetailDefaultSort}
                    >
                      {t('storeOrders.detail.defaultSort')}
                    </Button>
                    {canUseWarehouseManagerActions ? (
                      <Button disabled={isReadonlyOrder || isPasteOptimisticPreviewActive || !selectedLineKeys.length} onClick={() => setBatchModalOpen(true)}>
                        {t('storeOrders.batchModify')}
                      </Button>
                    ) : null}
                    {canUseWarehouseManagerActions ? (
                      <Typography.Text type="secondary">{t('storeOrders.detail.selectedRows', { count: selectedLineKeys.length })}</Typography.Text>
                    ) : null}
                  </Space>
                ) : undefined
              }
            >
              {isPasteOptimisticPreviewActive ? (
                <Alert
                  type="info"
                  showIcon
                  message={t('storeOrders.detail.pasteOptimisticPreviewTitle', '已先显示本次 Excel 预览')}
                  description={t(
                    'storeOrders.detail.pasteOptimisticPreviewDescription',
                    '后台正在写入；完成后会自动刷新确认。失败时会刷新服务器数据并提示原因。',
                  )}
                  style={{ marginBottom: 12 }}
                />
              ) : null}
              <div className="store-order-detail-filter-bar">
                <Space wrap size={[8, 8]}>
                  <Typography.Text strong>{t('storeOrders.statsFilter')}</Typography.Text>
                  <Input
                    allowClear
                    placeholder={t('storeOrders.filterByItemNumber')}
                    style={{ width: 220 }}
                    value={detailItemFilter}
                    onChange={(event) => {
                      // 远程筛选会切换结果集，先清空勾选，避免对旧行执行批量操作。
                      setSelectedLineKeys([])
                      setDetailPage(1)
                      setDetailItemFilter(event.target.value)
                    }}
                  />
                  <Tag
                    color={detailStatFilter === 'all' ? 'processing' : 'default'}
                    style={{ cursor: 'pointer' }}
                    onClick={() => {
                      setSelectedLineKeys([])
                      setDetailPage(1)
                      setDetailStatFilter('all')
                    }}
                  >
                    {t('storeOrders.allItems', { count: statSummary.all })}
                  </Tag>
                  <Tag
                    color={detailStatFilter === 'orderedNotShipped' ? 'orange' : 'gold'}
                    style={{ cursor: 'pointer' }}
                    onClick={() => {
                      setSelectedLineKeys([])
                      setDetailPage(1)
                      setDetailStatFilter((current) => (current === 'orderedNotShipped' ? 'all' : 'orderedNotShipped'))
                    }}
                  >
                    {t('storeOrders.orderedNotShipped', { count: statSummary.orderedNotShipped })}
                  </Tag>
                  <Tag
                    color={detailStatFilter === 'shippedWithoutOrder' ? 'geekblue' : 'blue'}
                    style={{ cursor: 'pointer' }}
                    onClick={() => {
                      setSelectedLineKeys([])
                      setDetailPage(1)
                      setDetailStatFilter((current) => (current === 'shippedWithoutOrder' ? 'all' : 'shippedWithoutOrder'))
                    }}
                  >
                    {t('storeOrders.shippedWithoutOrder', { count: statSummary.shippedWithoutOrder })}
                  </Tag>
                  <Typography.Text type="secondary">
                    {t('storeOrders.detail.currentRows', { count: detail.itemsTotal ?? detail.items.length })}
                  </Typography.Text>
                </Space>
              </div>
              <Table
                className="store-order-detail-table"
                rowKey="detailGUID"
                virtual
                loading={lineActionLoading}
                columns={columns}
                dataSource={detail.items}
                rowSelection={
                  canUseWarehouseManagerActions
                    ? {
                        selectedRowKeys: selectedLineKeys,
                        onChange: setSelectedLineKeys,
                        preserveSelectedRowKeys: false,
                        columnWidth: 34,
                      }
                    : undefined
                }
                pagination={{
                  current: detailPage,
                  pageSize: detailPageSize,
                  total: detail.itemsTotal ?? detail.items.length,
                  showSizeChanger: true,
                  // 图片交给浏览器懒加载，表格本身按服务端分页分批请求商品明细。
                  pageSizeOptions: STORE_ORDER_DETAIL_PAGE_SIZE_OPTIONS,
                  onChange: (nextPage, nextPageSize) => {
                    setSelectedLineKeys([])
                    setDetailPage(nextPage)
                    setDetailPageSize(nextPageSize)
                  },
                }}
                onChange={(_, __, sorter, extra) => {
                  if (extra.action === 'paginate') {
                    return
                  }

                  const nextSorter = Array.isArray(sorter) ? sorter[0] : (sorter as SorterResult<StoreOrderDetailLine>)
                  const field = nextSorter?.field
                  setSelectedLineKeys([])
                  setDetailPage(1)
                  if ((field === 'itemNumber' || field === 'locationCode') && nextSorter.order) {
                    setDetailSortField(field)
                    setDetailSortOrder(nextSorter.order)
                    return
                  }
                  setDetailSortField(null)
                  setDetailSortOrder(null)
                }}
                scroll={{ x: 1290, y: 620 }}
              />
            </Card>

            <Card>
              <Typography.Text type="secondary">
                {t('storeOrders.stage2Note')}
              </Typography.Text>
              <div style={{ marginTop: 8 }}>
                <Typography.Text type="secondary">
                  {t('storeOrders.lastUpdateTime', { time: formatDateTime(new Date().toISOString()) })}
                </Typography.Text>
              </div>
            </Card>

            <ProductPickerModal
              open={pickerOpen}
              orderGUID={detail.orderGUID}
              loading={lineActionLoading}
              onCancel={() => setPickerOpen(false)}
              onConfirm={handlePickerConfirm}
            />

            <ContainerProductPicker
              open={containerPickerOpen}
              loading={lineActionLoading || containerPickerLoading}
              alreadySelectedCodes={containerExistingProductCodes}
              onClose={() => setContainerPickerOpen(false)}
              onConfirm={handlePickerConfirm}
            />

            <BatchEditModal
              open={batchModalOpen}
              loading={batchLoading}
              selectedCount={selectedLineKeys.length}
              onCancel={() => setBatchModalOpen(false)}
              onConfirm={handleBatchConfirm}
            />

            <Modal
              title={t('storeOrders.detail.excelPasteTitle')}
              open={pasteModalOpen}
              width={880}
              destroyOnClose
              onCancel={() => setPasteModalOpen(false)}
              footer={[
                <Button key="cancel" onClick={() => setPasteModalOpen(false)}>
                  {t('common.close')}
                </Button>,
                <Button key="parse" type="primary" loading={parsingPaste} onClick={() => void handleParsePasteData()}>
                  {t('storeOrders.detail.parseData')}
                </Button>,
                <Button
                  key="confirm"
                  type="primary"
                  loading={submittingPaste}
                  disabled={!canUseWarehouseManagerActions || isReadonlyOrder || validPastePreviewCount === 0}
                  onClick={() => void handleConfirmPaste()}
                >
                  {t('storeOrders.detail.importValidRows', { count: validPastePreviewCount })}
                </Button>,
              ]}
            >
              <Space direction="vertical" size={16} style={{ width: '100%' }}>
                <div>
                  <Typography.Text strong>{t('storeOrders.detail.writeTarget')}</Typography.Text>
                  <div style={{ marginTop: 8 }}>
                    <Radio.Group
                      value={pasteTargetField}
                      onChange={(event) => setPasteTargetField(event.target.value as StoreOrderPasteWriteTarget)}
                    >
                      <Radio value="allocQuantity">{t('storeOrders.detail.allocQuantityDefault')}</Radio>
                      <Radio value="allocQuantityByInner">{t('storeOrders.detail.allocQuantityByInner')}</Radio>
                      <Radio value="quantity">{t('storeOrders.detail.orderQuantity')}</Radio>
                    </Radio.Group>
                  </div>
                  <Typography.Text type="secondary" style={{ display: 'block', marginTop: 6 }}>
                    {pasteTargetField === 'allocQuantityByInner'
                      ? t('storeOrders.detail.allocQuantityByInnerHelp')
                      : t('storeOrders.detail.writeTargetHelp')}
                  </Typography.Text>
                </div>

                <div>
                  <Typography.Text strong>{t('storeOrders.detail.excelText')}</Typography.Text>
                  <Input.TextArea
                    rows={7}
                    value={pasteData}
                    onChange={(event) => setPasteData(event.target.value)}
                    placeholder={t('storeOrders.detail.excelPastePlaceholder')}
                    style={{ marginTop: 8 }}
                  />
                </div>

                <div>
                  <Typography.Text strong>{t('storeOrders.detail.columnMapping')}</Typography.Text>
                  <Space wrap size={[12, 12]} style={{ display: 'flex', marginTop: 8 }}>
                    <Space>
                      <Typography.Text>{t('storeOrders.detail.itemNumberColumn')}</Typography.Text>
                      <Select
                        style={{ width: 100 }}
                        value={columnMapping.itemNumber}
                        options={[0, 1, 2, 3, 4].map((index) => ({
                          value: index,
                          label: t('storeOrders.detail.columnNumber', { number: index + 1 }),
                        }))}
                        onChange={(value) =>
                          setColumnMapping((current) => ({
                            ...current,
                            itemNumber: Number(value),
                          }))
                        }
                      />
                    </Space>
                    <Space>
                      <Typography.Text>{t('storeOrders.detail.quantityColumn')}</Typography.Text>
                      <Select
                        style={{ width: 120 }}
                        value={columnMapping.quantity}
                        options={[
                          { value: -1, label: t('storeOrders.detail.noneDefaultOne') },
                          ...[0, 1, 2, 3, 4].map((index) => ({
                            value: index,
                            label: t('storeOrders.detail.columnNumber', { number: index + 1 }),
                          })),
                        ]}
                        onChange={(value) =>
                          setColumnMapping((current) => ({
                            ...current,
                            quantity: Number(value),
                          }))
                        }
                      />
                    </Space>
                    <Space>
                      <Typography.Text>{t('storeOrders.detail.priceColumn')}</Typography.Text>
                      <Select
                        style={{ width: 120 }}
                        value={columnMapping.price}
                        options={[
                          { value: -1, label: t('storeOrders.detail.none') },
                          ...[0, 1, 2, 3, 4].map((index) => ({
                            value: index,
                            label: t('storeOrders.detail.columnNumber', { number: index + 1 }),
                          })),
                        ]}
                        onChange={(value) =>
                          setColumnMapping((current) => ({
                            ...current,
                            price: Number(value),
                          }))
                        }
                      />
                    </Space>
                  </Space>
                </div>

                {pastePreviewItems.length ? (
                  <div>
                    <Space direction="vertical" size={8} style={{ width: '100%' }}>
                      <Space wrap size={[12, 8]} style={{ justifyContent: 'space-between', width: '100%' }}>
                        <Typography.Text strong>{t('storeOrders.detail.previewResult', { valid: validPastePreviewCount, total: pastePreviewItems.length })}</Typography.Text>
                        <Radio.Group
                          size="small"
                          value={pastePreviewFilter}
                          onChange={(event) => setPastePreviewFilter(event.target.value as StoreOrderPastePreviewFilter)}
                        >
                          <Radio.Button value="all">{t('storeOrders.detail.pasteFilterAll', '全部')}</Radio.Button>
                          <Radio.Button value="importable">{t('storeOrders.detail.pasteFilterImportable', '可导入')}</Radio.Button>
                          <Radio.Button value="invalid">{t('storeOrders.detail.pasteFilterInvalid', '异常')}</Radio.Button>
                          <Radio.Button value="unmatched">{t('storeOrders.detail.pasteFilterUnmatched', '未匹配')}</Radio.Button>
                          <Radio.Button value="existing">{t('storeOrders.detail.pasteFilterExisting', '已存在')}</Radio.Button>
                        </Radio.Group>
                      </Space>
                      <Space wrap size={[8, 8]}>
                        <Typography.Text type="secondary">
                          {t('storeOrders.detail.pasteExistingCount', '已存在 {{count}} 行', { count: existingPastePreviewCount })}
                        </Typography.Text>
                        <Button size="small" disabled={!existingPastePreviewCount} onClick={() => handleSetExistingPastePreviewAction('replace')}>
                          {t('storeOrders.detail.pasteActionReplaceAll', '全部覆盖')}
                        </Button>
                        <Button size="small" disabled={!existingPastePreviewCount} onClick={() => handleSetExistingPastePreviewAction('append')}>
                          {t('storeOrders.detail.pasteActionAppendAll', '全部追加')}
                        </Button>
                        <Button size="small" disabled={!existingPastePreviewCount} onClick={() => handleSetExistingPastePreviewAction('skip')}>
                          {t('storeOrders.detail.pasteActionSkipAll', '全部跳过')}
                        </Button>
                      </Space>
                    </Space>
                    <Table<StoreOrderPastePreviewItem>
                      size="small"
                      rowKey={(record) => `${record.itemNumber}-${record.rowIndex}`}
                      style={{ marginTop: 8 }}
                      dataSource={filteredPastePreviewItems}
                      pagination={false}
                      scroll={{ y: 280 }}
                      columns={[
                        {
                          title: '#',
                          key: 'rowIndex',
                          width: 48,
                          align: 'center',
                          // 显示 Excel 原始行号，筛选后也能快速定位粘贴文本中的异常行。
                          render: (_, record) => record.rowIndex + 1,
                        },
                        {
                          title: t('column.status'),
                          dataIndex: 'status',
                          width: 100,
                          render: (_, record) => {
                            if (record.status === 'invalidQuantity') {
                              return <Tag color="warning">{t('storeOrders.detail.invalidQuantity', '数量异常')}</Tag>
                            }
                            if (record.status === 'unmatched') {
                              return <Tag color="error">{t('storeOrders.detail.unmatched')}</Tag>
                            }
                            if (record.status === 'existing') {
                              return <Tag color="processing">{t('storeOrders.detail.existingLine', '已存在')}</Tag>
                            }
                            return <Tag color="success">{t('storeOrders.detail.valid')}</Tag>
                          },
                        },
                        {
                          title: t('column.itemNumber'),
                          dataIndex: 'itemNumber',
                          width: 140,
                        },
                        {
                          title: t('column.productName'),
                          key: 'productName',
                          ellipsis: true,
                          render: (_, record) => record.product?.productName || '--',
                        },
                        {
                          title: t('storeOrders.detail.currentQuantity', '当前数量'),
                          key: 'existingQuantity',
                          width: 100,
                          render: (_, record) => {
                            const value = pasteApiTargetField === 'allocQuantity' ? record.existingAllocQuantity : record.existingQuantity
                            return value === undefined ? '--' : value
                          },
                        },
                        {
                          title: pasteApiTargetField === 'allocQuantity' ? t('column.shipQuantity') : t('column.orderQuantity'),
                          dataIndex: 'quantity',
                          width: 110,
                          render: (_, record) => formatPastePreviewQuantity(record, pasteQuantityMode),
                        },
                        {
                          title: t('storeOrders.detail.pasteAction', '处理方式'),
                          dataIndex: 'action',
                          width: 130,
                          render: (value: StoreOrderPasteAction, record) =>
                            record.valid ? (
                              <Select
                                size="small"
                                style={{ width: 112 }}
                                value={value}
                                options={[
                                  { value: 'replace', label: t('storeOrders.detail.pasteActionReplace', '覆盖') },
                                  { value: 'append', label: t('storeOrders.detail.pasteActionAppend', '追加') },
                                  { value: 'skip', label: t('storeOrders.detail.pasteActionSkip', '跳过') },
                                ]}
                                onChange={(nextValue) => handleChangePastePreviewAction(record.rowIndex, nextValue)}
                              />
                            ) : (
                              '--'
                            ),
                        },
                        {
                          title: t('column.importPriceShort'),
                          dataIndex: 'price',
                          width: 110,
                          render: (value: number | undefined) => (value === undefined ? '--' : formatAmount(value)),
                        },
                      ]}
                    />
                  </div>
                ) : null}
              </Space>
            </Modal>
        </Space>
      ) : (
        <Card>
          <Empty description={t('storeOrders.detail.notFound')} />
        </Card>
      )}
    </PageContainer>
  )
}
