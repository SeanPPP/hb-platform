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
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
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
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { SortOrder, SorterResult } from 'antd/es/table/interface'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { useAuthStore } from '../../../store/auth'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import { getStores } from '../../../services/storeService'
import ContainerProductPicker from './components/ContainerProductPicker'
import {
  addStoreOrderLine,
  batchLookupStoreOrderProducts,
  batchAddStoreOrderLines,
  batchUpdateStoreOrderLines,
  batchUpdateStoreOrderProductStatus,
  completeStoreOrder,
  getStoreOrderDetail,
  getStoreOrderDetailProductCodes,
  getStoreOrderProducts,
  pasteReplaceStoreOrderLines,
  removeStoreOrderLine,
  startPickingStoreOrder,
  updateStoreOrderHeader,
  updateStoreOrderLine,
  updateStoreOrderOutboundDate,
  updateStoreOrderStatus,
  updateStoreOrderStoreContact,
  updateStoreOrderProductStatus,
} from '../../../services/storeOrderService'
import type { StoreDto } from '../../../types/store'
import type { LocalSupplierDto } from '../../../types/localSupplier'
import type {
  StoreOrderBatchLookupItem,
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

type DetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error'
type DetailSortField = StoreOrderDetailSortField | null

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

interface ParsedPasteItem {
  itemNumber: string
  quantity: number
  price?: number
}

interface PastePreviewItem extends ParsedPasteItem {
  product?: StoreOrderProductItem
  valid: boolean
}

function ProductPickerModal({ open, orderGUID, loading, onCancel, onConfirm }: ProductPickerModalProps) {
  const { t } = useTranslation()
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [products, setProducts] = useState<StoreOrderProductItem[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(10)
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
    const nextPageNumber = overrides?.pageNumber ?? pageNumber
    const nextPageSize = overrides?.pageSize ?? pageSize
    const nextSupplierCode = overrides?.supplierCode ?? supplierCode

    setFetching(true)
    try {
      const result = await getStoreOrderProducts({
        itemNumber: nextKeyword.trim() || undefined,
        localSupplierCode: nextSupplierCode || undefined,
        excludeExistingWarehouseProducts: true,
        excludeOrderGUID: orderGUID,
        pageNumber: nextPageNumber,
        pageSize: nextPageSize,
        sortBy: 'Default',
      })
      setProducts(result.items)
      setTotal(result.total)
      setPageNumber(nextPageNumber)
      setPageSize(nextPageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.loadProductsFailed'))
    } finally {
      setFetching(false)
    }
  }

  const loadSupplierOptions = async () => {
    setSupplierLoading(true)
    try {
      const suppliers: LocalSupplierDto[] = await getActiveLocalSuppliers()
      // 供应商下拉只保留有效编码，便于按编码筛选商品列表。
      setSupplierOptions(
        suppliers
          .filter((item) => Boolean(item.localSupplierCode))
          .map((item) => ({
            label: item.name || item.localSupplierCode,
            value: item.localSupplierCode,
          })),
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.loadSuppliersFailed', '加载澳洲供应商失败'))
    } finally {
      setSupplierLoading(false)
    }
  }

  useEffect(() => {
    if (!open) {
      return
    }
    void loadSupplierOptions()
    void loadProducts({ pageNumber: 1 })
  }, [open])

  useEffect(() => {
    if (!open) {
      setKeyword('')
      setProducts([])
      setSelectedRowKeys([])
      setPageNumber(1)
      setPageSize(10)
      setTotal(0)
      setSupplierOptions([])
      setSupplierCode(undefined)
      setSupplierLoading(false)
      setEditingValues({})
    }
  }, [open])

  const columns: ColumnsType<StoreOrderProductItem> = [
    {
      title: t('column.image'),
      dataIndex: 'productImage',
      width: 84,
      render: (value: string | undefined, record) => (
        <Image
          src={value}
          alt={record.productName}
          width={40}
          height={40}
          style={{ borderRadius: 4, objectFit: 'cover' }}
          fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
        />
      ),
    },
    {
      title: t('column.itemNumber'),
      dataIndex: 'itemNumber',
      width: 140,
      render: (value: string | undefined) =>
        value ? (
          <Space size={4} wrap>
            <Typography.Text>{value}</Typography.Text>
            <Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>
              {t('common.copy')}
            </Button>
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: t('column.productName'),
      dataIndex: 'productName',
      width: 240,
      ellipsis: true,
      render: (value: string | undefined) => value || '--',
    },
    {
      title: t('column.supplierName', '供应商名称'),
      dataIndex: 'localSupplierName',
      width: 180,
      ellipsis: true,
      render: (value: string | undefined, record) => value || record.localSupplierCode || '--',
    },
    {
      title: t('column.barcode'),
      dataIndex: 'barcode',
      width: 170,
      render: (value: string | undefined) => <BarcodePreview value={value} textMaxWidth={110} />,
    },
    {
      title: t('column.stock'),
      dataIndex: 'stockQuantity',
      width: 90,
    },
    {
      title: t('column.minOrder'),
      dataIndex: 'minOrderQuantity',
      width: 110,
    },
    {
      title: t('column.defaultImportPrice'),
      dataIndex: 'importPrice',
      width: 120,
      render: (value: number | undefined) => formatAmount(value),
    },
    {
      title: t('column.allocQuantity'),
      key: 'quantity',
      width: 120,
      render: (_, record) => (
        <InputNumber
          min={0}
          precision={0}
          style={{ width: '100%' }}
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
      width: 120,
      render: (_, record) => (
        <InputNumber
          min={0}
          precision={2}
          style={{ width: '100%' }}
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
      title={t('storeOrders.selectProductTitle')}
      open={open}
      width={1280}
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
            placeholder={t('storeOrders.detail.filterLocalSupplier', '筛选澳洲供应商')}
            value={supplierCode}
            loading={supplierLoading}
            options={supplierOptions}
            style={{ width: 260 }}
            onChange={(value) => {
              // 切换供应商后回到第一页，避免旧分页落在空页。
              setSupplierCode(value)
              void loadProducts({ pageNumber: 1, supplierCode: value })
            }}
          />
        </Space>
        <Table
          rowKey="productCode"
          loading={fetching}
          size="small"
          dataSource={products}
          columns={columns}
          rowSelection={{
            selectedRowKeys,
            onChange: setSelectedRowKeys,
            preserveSelectedRowKeys: true,
            columnWidth: 40,
          }}
          pagination={{
            current: pageNumber,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) =>
              void loadProducts({ pageNumber: nextPage, pageSize: nextPageSize }),
          }}
          scroll={{ x: 1200, y: 480 }}
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
  const location = useLocation()
  const navigate = useNavigate()
  const screens = Grid.useBreakpoint()
  const { access, currentUser } = useAuthStore()
  const id = route?.params.id || ''
  const isDesktop = Boolean(screens.xl)
  const detailRequestControllerRef = useRef<AbortController | null>(null)
  // 记录当前订单和查询条件已完成首次加载，保活 Tab 恢复时避免同条件自动刷新。
  const loadedDetailIdRef = useRef<string | null>(null)
  const visibleDetailIdRef = useRef<string | null>(null)
  const lastLoadedDetailQueryKeyRef = useRef<string | null>(null)
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
  const [pasteTargetField, setPasteTargetField] = useState<StoreOrderPasteTargetField>('allocQuantity')
  const [detailItemFilter, setDetailItemFilter] = useState('')
  const [columnMapping, setColumnMapping] = useState({
    itemNumber: 0,
    quantity: 1,
    price: -1,
  })
  const [parsedPasteItems, setParsedPasteItems] = useState<ParsedPasteItem[]>([])
  const [pastePreviewItems, setPastePreviewItems] = useState<PastePreviewItem[]>([])
  const [parsingPaste, setParsingPaste] = useState(false)
  const [submittingPaste, setSubmittingPaste] = useState(false)
  const [detailPage, setDetailPage] = useState(1)
  const [detailPageSize, setDetailPageSize] = useState(50)
  const [detailStatFilter, setDetailStatFilter] = useState<StoreOrderDetailStatFilter>('all')
  const [detailSortField, setDetailSortField] = useState<DetailSortField>(null)
  const [detailSortOrder, setDetailSortOrder] = useState<SortOrder>(null)
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
    setStoresLoading(true)
    try {
      const canViewAllStores = access.isAdmin || access.isWarehouseManager
      const result = await getStores({
        page: 1,
        pageSize: 300,
        isActive: true,
        userGUID: canViewAllStores ? undefined : currentUser?.userGUID,
        sortField: 'storeName',
        sortOrder: 'ascend',
      })
      setStores(result.items)
    } catch (error) {
      console.error(error)
      // 分店下拉是辅助数据，加载失败不应误导用户以为订货明细主数据失败。
      message.warning(t('storeOrders.detail.loadStoreOptionsFailed'))
    } finally {
      setStoresLoading(false)
    }
  }

  useEffect(() => {
    if (!id) {
      return
    }
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
  }, [detailQuery, detailQueryKey, id])

  useEffect(() => {
    if (!id) {
      return
    }
    void loadStores()
  }, [access.isAdmin, access.isWarehouseManager, currentUser?.userGUID, id])

  useEffect(() => {
    if (!containerPickerOpen) {
      setContainerExistingProductCodes([])
    }
  }, [containerPickerOpen])

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
        options.push({
          value: headerForm.storeCode,
          label: `${headerForm.storeCode} (${t('column.currentStore')})`,
        })
      }

      return options
    },
    [headerForm.storeCode, stores],
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

  const validPastePreviewCount = useMemo(
    () => pastePreviewItems.filter((item) => item.valid).length,
    [pastePreviewItems],
  )

  const selectedLines = useMemo(
    () => detail?.items.filter((item) => selectedLineKeys.includes(item.detailGUID)) ?? [],
    [detail?.items, selectedLineKeys],
  )

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

  function ensureOrderEditable() {
    if (canEditOrder) {
      return true
    }

    message.warning(t('storeOrders.detail.orderReadonlyRefresh'))
    return false
  }

  const handleSaveHeader = async () => {
    if (!detail) {
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

    setLineActionLoading(true)
    try {
      await updateStoreOrderLine({
        orderGUID: detail.orderGUID,
        productCode: line.productCode,
        allocQuantity,
        importPrice,
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

  const resetPasteState = (targetField: StoreOrderPasteTargetField = 'allocQuantity') => {
    setPasteData('')
    setPasteTargetField(targetField)
    setColumnMapping({
      itemNumber: 0,
      quantity: 1,
      price: -1,
    })
    setParsedPasteItems([])
    setPastePreviewItems([])
  }

  const handleParsePasteData = async () => {
    if (!pasteData.trim()) {
      message.warning(t('storeOrders.detail.pasteExcelFirst'))
      return
    }

    setParsingPaste(true)
    try {
      const rows = pasteData
        .split(/\r?\n/)
        .map((row) => row.trim())
        .filter(Boolean)

      const items: ParsedPasteItem[] = []

      rows.forEach((row) => {
        const cols = row.split('\t').map((col) => col.trim())
        const itemNumber = cols[columnMapping.itemNumber] || cols[0]
        if (!itemNumber) {
          return
        }

        const rawQuantity = columnMapping.quantity >= 0 ? cols[columnMapping.quantity] : undefined
        const parsedQuantity = rawQuantity === undefined ? Number.NaN : Number.parseInt(rawQuantity, 10)
        const quantity = Number.isFinite(parsedQuantity) && parsedQuantity >= 0 ? parsedQuantity : 1

        const rawPrice = columnMapping.price >= 0 ? cols[columnMapping.price] : undefined
        const parsedPrice = rawPrice === undefined ? Number.NaN : Number.parseFloat(rawPrice)

        items.push({
          itemNumber,
          quantity,
          price: Number.isFinite(parsedPrice) ? parsedPrice : undefined,
        })
      })

      if (!items.length) {
        message.warning(t('storeOrders.detail.noValidPasteItems'))
        setParsedPasteItems([])
        setPastePreviewItems([])
        return
      }

      setParsedPasteItems(items)

      const lookupResult = await batchLookupStoreOrderProducts({
        codes: Array.from(new Set(items.map((item) => item.itemNumber.trim()).filter(Boolean))),
      })
      const productMap = new Map<string, StoreOrderProductItem>()

      lookupResult.forEach((entry: StoreOrderBatchLookupItem) => {
        if (entry.lookupCode && entry.product) {
          productMap.set(entry.lookupCode.trim().toLowerCase(), entry.product)
        }
      })

      const preview = items.map((item) => {
        const product = productMap.get(item.itemNumber.trim().toLowerCase())
        return {
          ...item,
          product,
          valid: Boolean(product),
        }
      })

      setPastePreviewItems(preview)
      message.success(t('storeOrders.detail.pasteParseSuccess', { total: items.length, valid: preview.filter((item) => item.valid).length }))
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

    const validItems: Array<{ productCode: string; quantity: number; importPrice?: number }> = []

    parsedPasteItems.forEach((item) => {
      const matched = pastePreviewItems.find((preview) => preview.itemNumber === item.itemNumber && preview.valid)
      if (!matched?.product) {
        return
      }

      validItems.push({
        productCode: matched.product.productCode,
        quantity: item.quantity,
        importPrice: item.price,
      })
    })

    if (!validItems.length) {
      message.warning(t('storeOrders.detail.noValidImportProducts'))
      return
    }

    setSubmittingPaste(true)
    try {
      await pasteReplaceStoreOrderLines({
        orderGUID: detail.orderGUID,
        targetField: pasteTargetField,
        items: validItems,
      })
      message.success(t('storeOrders.pasteUpdateSuccess', { count: validItems.length }))
      setPasteModalOpen(false)
      resetPasteState(pasteTargetField)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('storeOrders.detail.pasteImportFailed'))
    } finally {
      setSubmittingPaste(false)
    }
  }

  const handleCompleteOrder = async () => {
    if (!detail) {
      return
    }
    if (!canCompleteOrder) {
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
          await completeStoreOrder(detail.orderGUID)
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
    if (!canStartPicking) {
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

  const columns: ColumnsType<StoreOrderDetailLine> = [
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
          min={0}
          precision={0}
          disabled={isReadonlyOrder}
          status={getQuantityHighlight(record)}
          style={{ width: 60 }}
          value={editingRows[record.detailGUID]?.allocQuantity ?? value ?? 0}
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
          min={0}
          precision={2}
          disabled={isReadonlyOrder}
          status={isZeroOrEmpty(editingRows[record.detailGUID]?.importPrice ?? value) ? 'error' : undefined}
          style={{ width: 60 }}
          value={editingRows[record.detailGUID]?.importPrice ?? value}
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
            ? renderStoreOrderDetailNumericCell(renderDangerValue('0.0000'))
            : renderStoreOrderDetailNumericCell(formatVolume(nextValue))
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
            ? renderStoreOrderDetailNumericCell(renderDangerValue('0.0000'))
            : renderStoreOrderDetailNumericCell(formatVolume(nextValue))
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
              disabled={isReadonlyOrder}
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
              disabled={isReadonlyOrder}
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
                disabled={isReadonlyOrder}
              />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ]

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
                }
              >
                <Descriptions.Item label={t('storeOrders.orderNoLabel')}>{detail.orderNo || '--'}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.storeLabel')}>
                  <Select
                    showSearch
                    style={{ width: '100%' }}
                    loading={storesLoading}
                    disabled={isReadonlyOrder}
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
                    disabled={isReadonlyOrder}
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
                    disabled={!canEditOutboundDate}
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
                <Descriptions.Item label={t('storeOrders.orderVolumeLabel')}>{formatVolume(totalOrderVolume)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.shipVolumeLabel')}>{formatVolume(totalAllocVolume)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.orderAmountLabel')}>{formatAmount(detail.totalAmount)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.importAmountLabel')}>{formatAmount(detail.totalImportAmount)}</Descriptions.Item>
                <Descriptions.Item label={t('storeOrders.freightLabel')}>
                  <InputNumber
                    min={0}
                    precision={2}
                    disabled={isReadonlyOrder}
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
                    disabled={isReadonlyOrder}
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
                    disabled={isReadonlyOrder}
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
                    disabled={isReadonlyOrder}
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
                <Space wrap>
                  <Input
                    allowClear
                    disabled={isReadonlyOrder}
                    placeholder={t('storeOrders.quickAddPlaceholder')}
                    style={{ width: 220 }}
                    value={quickAddItemNumber}
                    onChange={(event) => setQuickAddItemNumber(event.target.value)}
                    onPressEnter={() => void handleQuickAdd()}
                  />
                  <InputNumber
                    min={1}
                    precision={0}
                    disabled={isReadonlyOrder}
                    placeholder={t('storeOrders.allocQtyPlaceholder')}
                    value={quickAddQuantity}
                    onChange={(value) => setQuickAddQuantity(Number(value ?? 1))}
                  />
                  <Button
                    icon={<PlusOutlined />}
                    loading={lineActionLoading}
                    disabled={isReadonlyOrder}
                    onClick={() => void handleQuickAdd()}
                  >
                    {t('storeOrders.quickAdd')}
                  </Button>
                  <Button icon={<SearchOutlined />} disabled={isReadonlyOrder} onClick={() => setPickerOpen(true)}>
                    {t('storeOrders.selectProduct')}
                  </Button>
                  <Button
                    icon={<ContainerOutlined />}
                    loading={containerPickerLoading}
                    disabled={isReadonlyOrder}
                    onClick={() => void handleOpenContainerPicker()}
                  >
                    {t('storeOrders.containerPicker')}
                  </Button>
                  <Button
                    icon={<PrinterOutlined />}
                    onClick={() => detail && navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)}
                  >
                    {t('storeOrders.pickingList')}
                  </Button>
                  <Button
                    icon={<FileTextOutlined />}
                    onClick={() => detail && navigate(`/warehouse/store-order/invoice/${detail.orderGUID}`)}
                  >
                    {t('storeOrders.invoice')}
                  </Button>
                  <Button
                    icon={<CopyOutlined />}
                    disabled={isReadonlyOrder}
                    onClick={() => {
                      resetPasteState('allocQuantity')
                      setPasteModalOpen(true)
                    }}
                  >
                    {t('storeOrders.excelPaste')}
                  </Button>
                  <Button disabled={isReadonlyOrder || !selectedLineKeys.length} onClick={() => setBatchModalOpen(true)}>
                    {t('storeOrders.batchModify')}
                  </Button>
                  <Typography.Text type="secondary">{t('storeOrders.detail.selectedRows', { count: selectedLineKeys.length })}</Typography.Text>
                </Space>
              }
            >
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
                rowSelection={{
                  selectedRowKeys: selectedLineKeys,
                  onChange: setSelectedLineKeys,
                  preserveSelectedRowKeys: false,
                  columnWidth: 34,
                }}
                pagination={{
                  current: detailPage,
                  pageSize: detailPageSize,
                  total: detail.itemsTotal ?? detail.items.length,
                  showSizeChanger: true,
                  pageSizeOptions: ['20', '50', '100', '500'],
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
                  disabled={isReadonlyOrder || validPastePreviewCount === 0}
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
                      onChange={(event) => setPasteTargetField(event.target.value as StoreOrderPasteTargetField)}
                    >
                      <Radio value="allocQuantity">{t('storeOrders.detail.allocQuantityDefault')}</Radio>
                      <Radio value="quantity">{t('storeOrders.detail.orderQuantity')}</Radio>
                    </Radio.Group>
                  </div>
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
                    <Typography.Text strong>{t('storeOrders.detail.previewResult', { valid: validPastePreviewCount, total: pastePreviewItems.length })}</Typography.Text>
                    <Table<PastePreviewItem>
                      size="small"
                      rowKey={(record, index) => `${record.itemNumber}-${index ?? 0}`}
                      style={{ marginTop: 8 }}
                      dataSource={pastePreviewItems}
                      pagination={{ pageSize: 8, hideOnSinglePage: true }}
                      scroll={{ y: 280 }}
                      columns={[
                        {
                          title: t('column.status'),
                          dataIndex: 'valid',
                          width: 90,
                          render: (value: boolean) =>
                            value ? <Tag color="success">{t('storeOrders.detail.valid')}</Tag> : <Tag color="error">{t('storeOrders.detail.unmatched')}</Tag>,
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
                          title: pasteTargetField === 'allocQuantity' ? t('column.shipQuantity') : t('column.orderQuantity'),
                          dataIndex: 'quantity',
                          width: 110,
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
