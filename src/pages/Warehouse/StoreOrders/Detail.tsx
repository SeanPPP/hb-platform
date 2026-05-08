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
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { SortOrder, SorterResult } from 'antd/es/table/interface'
import { useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { useAuthStore } from '../../../store/auth'
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
  getStoreOrderProducts,
  pasteReplaceStoreOrderLines,
  removeStoreOrderLine,
  startPickingStoreOrder,
  updateStoreOrderHeader,
  updateStoreOrderLine,
  updateStoreOrderProductStatus,
} from '../../../services/storeOrderService'
import type { StoreDto } from '../../../types/store'
import type {
  StoreOrderBatchLookupItem,
  StoreOrderDetail,
  StoreOrderDetailLine,
  StoreOrderFlowStatus,
  StoreOrderPasteTargetField,
  StoreOrderProductItem,
} from '../../../types/storeOrder'
import { StoreOrderStatusColorMap, StoreOrderStatusLabelMap } from '../../../types/storeOrder'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'

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

type DetailStatFilter = 'all' | 'orderedNotShipped' | 'shippedWithoutOrder'
type DetailSortField = 'itemNumber' | 'locationCode' | null

function toNumber(value?: number | null) {
  return Number(value ?? 0)
}

function isZeroOrEmpty(value: unknown) {
  return value === undefined || value === null || value === '' || value === 0
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
  const [fetching, setFetching] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [products, setProducts] = useState<StoreOrderProductItem[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [editingValues, setEditingValues] = useState<
    Record<string, { quantity?: number; importPrice?: number }>
  >({})

  const loadProducts = async (overrides?: {
    keyword?: string
    pageNumber?: number
    pageSize?: number
  }) => {
    const nextKeyword = overrides?.keyword ?? keyword
    const nextPageNumber = overrides?.pageNumber ?? pageNumber
    const nextPageSize = overrides?.pageSize ?? pageSize

    setFetching(true)
    try {
      const result = await getStoreOrderProducts({
        itemNumber: nextKeyword.trim() || undefined,
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
      message.error(error instanceof Error ? error.message : '加载商品失败')
    } finally {
      setFetching(false)
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
      setKeyword('')
      setProducts([])
      setSelectedRowKeys([])
      setPageNumber(1)
      setPageSize(10)
      setTotal(0)
      setEditingValues({})
    }
  }, [open])

  const columns: ColumnsType<StoreOrderProductItem> = [
    {
      title: '图片',
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
      title: '货号',
      dataIndex: 'itemNumber',
      width: 140,
      render: (value: string | undefined) =>
        value ? (
          <Space size={4} wrap>
            <Typography.Text>{value}</Typography.Text>
            <Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>
              复制
            </Button>
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: '商品名称',
      dataIndex: 'productName',
      width: 240,
      ellipsis: true,
      render: (value: string | undefined) => value || '--',
    },
    {
      title: '条码',
      dataIndex: 'barcode',
      width: 170,
      render: (value: string | undefined) => <BarcodePreview value={value} textMaxWidth={110} />,
    },
    {
      title: '库存',
      dataIndex: 'stockQuantity',
      width: 90,
    },
    {
      title: '最小订货',
      dataIndex: 'minOrderQuantity',
      width: 110,
    },
    {
      title: '默认进口价',
      dataIndex: 'importPrice',
      width: 120,
      render: (value: number | undefined) => formatAmount(value),
    },
    {
      title: '发货数',
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
      title: '导入价',
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
      message.error('缺少订单编号')
      return
    }
    if (!selectedRowKeys.length) {
      message.warning('请先选择商品')
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
      title="选择商品"
      open={open}
      width={1280}
      destroyOnClose
      okText={`添加选中 (${selectedRowKeys.length})`}
      cancelText="关闭"
      confirmLoading={loading}
      onCancel={onCancel}
      onOk={() => void handleOk()}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Input.Search
          allowClear
          placeholder="搜索货号 / 商品名称"
          prefix={<SearchOutlined />}
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onSearch={(value) => void loadProducts({ keyword: value, pageNumber: 1 })}
        />
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
      message.warning('请先选择明细行')
      return
    }

    if (type === 'allocQuantity' && (allocQuantity === undefined || allocQuantity < 0)) {
      message.warning('请输入有效的发货数')
      return
    }

    if (type === 'importPrice' && (importPrice === undefined || importPrice < 0)) {
      message.warning('请输入有效的进口价')
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
      title="批量修改明细"
      open={open}
      destroyOnClose
      okText={`应用到 ${selectedCount} 行`}
      cancelText="关闭"
      confirmLoading={loading}
      onCancel={onCancel}
      onOk={() => void handleOk()}
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Select
          value={type}
          options={[
            { value: 'allocQuantity', label: '批量改发货数' },
            { value: 'importPrice', label: '批量改进口价' },
            { value: 'status', label: '批量改状态' },
          ]}
          onChange={setType}
        />

        {type === 'allocQuantity' ? (
          <InputNumber
            min={0}
            precision={0}
            style={{ width: '100%' }}
            placeholder="输入新的发货数"
            value={allocQuantity}
            onChange={(value) => setAllocQuantity(value === null ? undefined : Number(value))}
          />
        ) : null}

        {type === 'importPrice' ? (
          <InputNumber
            min={0}
            precision={2}
            style={{ width: '100%' }}
            placeholder="输入新的进口价"
            value={importPrice}
            onChange={(value) => setImportPrice(value === null ? undefined : Number(value))}
          />
        ) : null}

        {type === 'status' ? (
          <Select
            value={isActive ? 'active' : 'inactive'}
            options={[
              { value: 'active', label: '启用' },
              { value: 'inactive', label: '停用' },
            ]}
            onChange={(value) => setIsActive(value === 'active')}
          />
        ) : null}
      </Space>
    </Modal>
  )
}

export default function StoreOrderDetailPage() {
  const route = useStableRouteContext()
  const location = useLocation()
  const navigate = useNavigate()
  const screens = Grid.useBreakpoint()
  const { access, currentUser } = useAuthStore()
  const id = route?.params.id || ''
  const isDesktop = Boolean(screens.xl)
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<StoreOrderDetail | null>(null)
  const [stores, setStores] = useState<StoreDto[]>([])
  const [storesLoading, setStoresLoading] = useState(false)
  const [savingHeader, setSavingHeader] = useState(false)
  const [lineActionLoading, setLineActionLoading] = useState(false)
  const [batchLoading, setBatchLoading] = useState(false)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [containerPickerOpen, setContainerPickerOpen] = useState(false)
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
  const [detailStatFilter, setDetailStatFilter] = useState<DetailStatFilter>('all')
  const [detailSortField, setDetailSortField] = useState<DetailSortField>(null)
  const [detailSortOrder, setDetailSortOrder] = useState<SortOrder>(null)
  const [headerForm, setHeaderForm] = useState<{
    storeCode?: string
    orderDate?: string
    shippingFee?: number
    remarks: string
  }>({
    storeCode: undefined,
    orderDate: undefined,
    shippingFee: undefined,
    remarks: '',
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
  const tabTitle = detail?.orderNo || initialOrderNo || '订货明细'

  useDynamicTabTitle(tabTitle)

  const loadDetail = async (showLoading = true) => {
    if (!id) {
      return
    }

    if (showLoading) {
      setLoading(true)
    }

    try {
      const result = await getStoreOrderDetail(id)
      setDetail(result)
      setHeaderForm({
        storeCode: result?.storeCode,
        orderDate: result?.orderDate,
        shippingFee: result?.shippingFee,
        remarks: result?.remarks || '',
      })
      setDetailPage(1)
      setDetailPageSize(50)
      setDetailStatFilter('all')
      setDetailItemFilter('')
      setEditingRows({})
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载订货明细失败')
    } finally {
      if (showLoading) {
        setLoading(false)
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
      message.error(error instanceof Error ? error.message : '加载分店失败')
    } finally {
      setStoresLoading(false)
    }
  }

  useEffect(() => {
    if (!id) {
      return
    }
    void Promise.all([loadDetail(), loadStores()])
  }, [id, access.isAdmin, access.isWarehouseManager, currentUser?.userGUID])

  const storeOptions = useMemo(
    () => {
      const options = stores.map((item) => ({
        value: item.storeCode,
        label: `${item.storeName} (${item.storeCode})`,
      }))

      if (headerForm.storeCode && !options.some((item) => item.value === headerForm.storeCode)) {
        options.push({
          value: headerForm.storeCode,
          label: `${headerForm.storeCode} (当前订单分店)`,
        })
      }

      return options
    },
    [headerForm.storeCode, stores],
  )

  const currentStore = useMemo(
    () => stores.find((item) => item.storeCode === headerForm.storeCode),
    [headerForm.storeCode, stores],
  )

  const totalAllocQuantity =
    detail?.totalAllocQuantity ?? detail?.items?.reduce((sum, item) => sum + (item.allocQuantity || 0), 0) ?? 0

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
    return {
      all: items.length,
      orderedNotShipped: items.filter(isOrderedNotShipped).length,
      shippedWithoutOrder: items.filter(isShippedWithoutOrder).length,
    }
  }, [detail?.items])

  const filteredItems = useMemo(() => {
    const items = detail?.items ?? []
    const normalizedKeyword = detailItemFilter.trim().toLowerCase()
    const keywordFiltered = normalizedKeyword
      ? items.filter((item) => item.itemNumber?.toLowerCase().includes(normalizedKeyword))
      : items

    switch (detailStatFilter) {
      case 'orderedNotShipped':
        return keywordFiltered.filter(isOrderedNotShipped)
      case 'shippedWithoutOrder':
        return keywordFiltered.filter(isShippedWithoutOrder)
      default:
        return keywordFiltered
    }
  }, [detail?.items, detailItemFilter, detailStatFilter])

  const sortedItems = useMemo(() => {
    if (!detailSortField || !detailSortOrder) {
      return filteredItems
    }

    const items = [...filteredItems]
    items.sort((left, right) => {
      const leftValue = (left[detailSortField] || '') as string
      const rightValue = (right[detailSortField] || '') as string
      const compareResult = leftValue.localeCompare(rightValue, 'zh-CN', { numeric: true, sensitivity: 'base' })
      return detailSortOrder === 'ascend' ? compareResult : -compareResult
    })
    return items
  }, [detailSortField, detailSortOrder, filteredItems])

  const pagedItems = useMemo(() => {
    const start = (detailPage - 1) * detailPageSize
    return sortedItems.slice(start, start + detailPageSize)
  }, [detailPage, detailPageSize, sortedItems])

  useEffect(() => {
    const totalItems = sortedItems.length
    const maxPage = Math.max(1, Math.ceil(totalItems / detailPageSize))
    if (detailPage > maxPage) {
      setDetailPage(maxPage)
    }
  }, [detailPage, detailPageSize, sortedItems.length])

  useEffect(() => {
    setDetailPage(1)
  }, [detailItemFilter, detailStatFilter, detailSortField, detailSortOrder])

  const handleSaveHeader = async () => {
    if (!detail) {
      return
    }
    setSavingHeader(true)
    try {
      await updateStoreOrderHeader({
        orderGUID: detail.orderGUID,
        storeCode: headerForm.storeCode,
        orderDate: headerForm.orderDate,
        shippingFee: headerForm.shippingFee,
        remarks: headerForm.remarks,
      })
      message.success('订单头信息已保存')
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '保存订单头失败')
    } finally {
      setSavingHeader(false)
    }
  }

  const handleQuickAdd = async () => {
    if (!detail) {
      return
    }
    const normalizedItemNumber = quickAddItemNumber.trim()
    if (!normalizedItemNumber) {
      message.warning('请输入货号')
      return
    }
    if (!quickAddQuantity || quickAddQuantity <= 0) {
      message.warning('请输入有效的发货数')
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
        message.warning('未找到精确匹配的货号')
        return
      }

      if (exactMatches.length > 1) {
        message.warning('存在多个完全相同的货号，请使用“选择商品”确认后再添加')
        return
      }

      const target = exactMatches[0]

      await addStoreOrderLine({
        orderGUID: detail.orderGUID,
        productCode: target.productCode,
        quantity: quickAddQuantity,
      })
      message.success('商品已加入订单')
      setQuickAddItemNumber('')
      setQuickAddQuantity(1)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '快速加商品失败')
    } finally {
      setLineActionLoading(false)
    }
  }

  const handlePickerConfirm = async (items: Array<{ productCode: string; quantity: number; importPrice?: number }>) => {
    if (!detail) {
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
      message.success(`成功添加 ${items.length} 个商品`)
      setPickerOpen(false)
      setContainerPickerOpen(false)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '添加商品失败')
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleSaveLine = async (line: StoreOrderDetailLine) => {
    if (!detail) {
      return
    }

    const edited = editingRows[line.detailGUID]
    const allocQuantity = edited?.allocQuantity ?? line.allocQuantity ?? 0
    const importPrice = edited?.importPrice ?? line.importPrice

    if (allocQuantity < 0) {
      message.warning('发货数不能小于 0')
      return
    }

    setLineActionLoading(true)
    try {
      await updateStoreOrderLine({
        orderGUID: detail.orderGUID,
        productCode: line.productCode,
        quantity: allocQuantity,
        importPrice,
      })
      message.success('明细已保存')
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '保存明细失败')
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleRemoveLine = async (line: StoreOrderDetailLine) => {
    if (!detail) {
      return
    }
    setLineActionLoading(true)
    try {
      await removeStoreOrderLine({
        orderGUID: detail.orderGUID,
        detailGUID: line.detailGUID,
      })
      message.success('明细已删除')
      setSelectedLineKeys((current) => current.filter((key) => key !== line.detailGUID))
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '删除明细失败')
    } finally {
      setLineActionLoading(false)
    }
  }

  const handleToggleLineStatus = async (line: StoreOrderDetailLine) => {
    setLineActionLoading(true)
    try {
      await updateStoreOrderProductStatus({
        productCode: line.productCode,
        isActive: !line.isActive,
      })
      message.success(`商品已${line.isActive ? '停用' : '启用'}`)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '更新商品状态失败')
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

      message.success(`已批量更新 ${selectedLines.length} 行`)
      setBatchModalOpen(false)
      setSelectedLineKeys([])
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '批量更新失败')
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
      message.warning('请先粘贴 Excel 数据')
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
        message.warning('未解析出有效的货号数据')
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
      message.success(`已解析 ${items.length} 行，其中 ${preview.filter((item) => item.valid).length} 行可导入`)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '解析粘贴数据失败')
    } finally {
      setParsingPaste(false)
    }
  }

  const handleConfirmPaste = async () => {
    if (!detail) {
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
      message.warning('没有可导入的有效商品')
      return
    }

    setSubmittingPaste(true)
    try {
      await pasteReplaceStoreOrderLines({
        orderGUID: detail.orderGUID,
        targetField: pasteTargetField,
        items: validItems,
      })
      message.success(`已通过粘贴更新 ${validItems.length} 行`)
      setPasteModalOpen(false)
      resetPasteState(pasteTargetField)
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '粘贴导入失败')
    } finally {
      setSubmittingPaste(false)
    }
  }

  const handleCompleteOrder = async () => {
    if (!detail) {
      return
    }
    Modal.confirm({
      title: '确认完成订单？',
      content: '完成后订单状态将变为已完成。',
      okText: '确认',
      cancelText: '取消',
      onOk: async () => {
        try {
          setLineActionLoading(true)
          await completeStoreOrder(detail.orderGUID)
          message.success('订单已完成')
          await loadDetail(false)
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : '完成订单失败')
        } finally {
          setLineActionLoading(false)
        }
      },
    })
  }

  const handleStartPicking = async () => {
    if (!detail) {
      return
    }
    try {
      setLineActionLoading(true)
      await startPickingStoreOrder(detail.orderGUID)
      message.success('订单已进入配货中')
      await loadDetail(false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '开始配货失败')
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
      render: (_, __, index) => (detailPage - 1) * detailPageSize + index + 1,
    },
    {
      title: '图片',
      dataIndex: 'productImage',
      width: 50,
      fixed: isDesktop ? 'left' : undefined,
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
      title: '货号',
      dataIndex: 'itemNumber',
      width: 100,
      fixed: isDesktop ? 'left' : undefined,
      sorter: true,
      sortOrder: detailSortField === 'itemNumber' ? detailSortOrder : null,
      render: (value: string | undefined) =>
        value ? (
          <Space size={4} wrap>
            <Typography.Text>{value}</Typography.Text>
            <Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>
              复制
            </Button>
          </Space>
        ) : (
          renderDangerValue('--')
        ),
    },
    {
      title: '商品名称',
      dataIndex: 'productName',
      width: 100,
      render: (value: string | undefined) =>
        value ? (
          <div
            style={{
              display: '-webkit-box',
              overflow: 'hidden',
              lineHeight: 1.5,
              whiteSpace: 'normal',
              wordBreak: 'break-word',
              WebkitBoxOrient: 'vertical',
              WebkitLineClamp: 2,
            }}
            title={value}
          >
            {value}
          </div>
        ) : (
          '--'
        ),
    },
    {
      title: '条码',
      dataIndex: 'barcode',
      width: 110,
      render: (value: string | undefined) => <BarcodePreview value={value} textMaxWidth={150} />,
    },
    {
      title: '贴牌价',
      dataIndex: 'price',
      width: 80,
      render: (value: number | undefined) => formatAmount(value),
    },
    {
      title: '货位',
      dataIndex: 'locationCode',
      width: 100,
      sorter: true,
      sortOrder: detailSortField === 'locationCode' ? detailSortOrder : null,
      render: (value: string | undefined) => renderZeroOrEmptyCell(value),
    },
    {
      title: '订货数',
      dataIndex: 'quantity',
      width: 50,
      render: (_, record) => renderQuantityText(record),
    },
    {
      title: '发货数',
      dataIndex: 'allocQuantity',
      width: 80,
      render: (value: number | undefined, record) => (
        <InputNumber
          min={0}
          precision={0}
          status={getQuantityHighlight(record)}
          style={{ width: '100%' }}
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
      title: '进口价',
      dataIndex: 'importPrice',
      width: 80,
      render: (value: number | undefined, record) => (
        <InputNumber
          min={0}
          precision={2}
          status={isZeroOrEmpty(editingRows[record.detailGUID]?.importPrice ?? value) ? 'error' : undefined}
          style={{ width: '100%' }}
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
      title: '进口金额',
      dataIndex: 'importAmount',
      width: 80,
      render: (value: number | undefined) =>
        value === undefined || value === null ? renderDangerValue('--') : value === 0 ? renderDangerValue('0.00') : formatAmount(value),
    },
    {
      title: '体积',
      dataIndex: 'totalVolume',
      width: 50,
      render: (value: number | undefined) =>
        value === undefined || value === null
          ? renderDangerValue('--')
          : value === 0
            ? renderDangerValue('0.0000')
            : value.toFixed(4),
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 50,
      render: (value: boolean) => <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>,
    },
    {
      title: '操作',
      key: 'actions',
      width: 100,
      fixed: isDesktop ? 'right' : undefined,
      render: (_, record) => (
        <Space size={4} wrap>
          <Button
            size="small"
            type="link"
            icon={<SaveOutlined />}
            onClick={() => void handleSaveLine(record)}
          >
            保存
          </Button>
          <Button
            size="small"
            type="link"
            icon={<EditOutlined />}
            onClick={() => void handleToggleLineStatus(record)}
          >
            {record.isActive ? '停用' : '启用'}
          </Button>
          <Popconfirm
            title="确认删除这行商品？"
            okText="删除"
            cancelText="取消"
            onConfirm={() => void handleRemoveLine(record)}
          >
            <Button size="small" danger type="link" icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  if (!id) {
    return (
      <PageContainer title="订货明细" subtitle="缺少订单编号，无法加载明细。">
        <Card>
          <Empty description="未提供订单编号" />
        </Card>
      </PageContainer>
    )
  }

  return (
    <PageContainer
      title={`订货明细 - ${tabTitle}`}
      subtitle="阶段 2 已升级为核心可编辑版，已支持订单头编辑、明细维护、批量修改、商品选择、货柜选品和 Excel 粘贴。"
    >
      <Spin spinning={loading}>
        {detail ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Card>
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
                      保存订单头
                    </Button>
                    <Button
                      icon={<CheckOutlined />}
                      loading={lineActionLoading}
                      onClick={() => void handleStartPicking()}
                    >
                      开始配货
                    </Button>
                    <Button
                      type="primary"
                      icon={<CheckOutlined />}
                      loading={lineActionLoading}
                      onClick={() => void handleCompleteOrder()}
                    >
                      完成订单
                    </Button>
                  </Space>
                }
              >
                <Descriptions.Item label="订单号">{detail.orderNo || '--'}</Descriptions.Item>
                <Descriptions.Item label="分店">
                  <Select
                    showSearch
                    style={{ width: '100%' }}
                    loading={storesLoading}
                    value={headerForm.storeCode}
                    options={storeOptions}
                    optionFilterProp="label"
                    onChange={(value) => setHeaderForm((current) => ({ ...current, storeCode: value }))}
                  />
                </Descriptions.Item>
                <Descriptions.Item label="状态">
                  <Tag color={StoreOrderStatusColorMap[(detail.flowStatus || 0) as StoreOrderFlowStatus] || 'default'}>
                    {StoreOrderStatusLabelMap[(detail.flowStatus || 0) as StoreOrderFlowStatus] || `状态 ${detail.flowStatus ?? '--'}`}
                  </Tag>
                </Descriptions.Item>
                <Descriptions.Item label="订货日期">
                  <Input
                    type="date"
                    value={headerForm.orderDate ? headerForm.orderDate.slice(0, 10) : ''}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        orderDate: event.target.value ? new Date(event.target.value).toISOString() : undefined,
                      }))
                    }
                  />
                </Descriptions.Item>
                <Descriptions.Item label="订货数量">{detail.totalQuantity}</Descriptions.Item>
                <Descriptions.Item label="发货数量">{totalAllocQuantity}</Descriptions.Item>
                <Descriptions.Item label="订单金额">{formatAmount(detail.totalAmount)}</Descriptions.Item>
                <Descriptions.Item label="进口金额">{formatAmount(detail.totalImportAmount)}</Descriptions.Item>
                <Descriptions.Item label="运费">
                  <InputNumber
                    min={0}
                    precision={2}
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
                <Descriptions.Item label="地址" span={2}>
                  {currentStore?.address || detail.storeAddress || '--'}
                </Descriptions.Item>
                <Descriptions.Item label="SKU 数">{detail.totalSKU ?? detail.items.length}</Descriptions.Item>
                <Descriptions.Item label="备注" span={3}>
                  <Input.TextArea
                    rows={3}
                    value={headerForm.remarks}
                    onChange={(event) =>
                      setHeaderForm((current) => ({
                        ...current,
                        remarks: event.target.value,
                      }))
                    }
                    placeholder="请输入备注"
                  />
                </Descriptions.Item>
              </Descriptions>
            </Card>

            <Card
              title="订单明细"
              extra={
                <Space wrap>
                  <Input
                    allowClear
                    placeholder="输入货号快速加入"
                    style={{ width: 220 }}
                    value={quickAddItemNumber}
                    onChange={(event) => setQuickAddItemNumber(event.target.value)}
                    onPressEnter={() => void handleQuickAdd()}
                  />
                  <InputNumber
                    min={1}
                    precision={0}
                    placeholder="发货数"
                    value={quickAddQuantity}
                    onChange={(value) => setQuickAddQuantity(Number(value ?? 1))}
                  />
                  <Button
                    icon={<PlusOutlined />}
                    loading={lineActionLoading}
                    onClick={() => void handleQuickAdd()}
                  >
                    快速加入
                  </Button>
                  <Button icon={<SearchOutlined />} onClick={() => setPickerOpen(true)}>
                    选择商品
                  </Button>
                  <Button icon={<ContainerOutlined />} onClick={() => setContainerPickerOpen(true)}>
                    货柜选择器
                  </Button>
                  <Button
                    icon={<PrinterOutlined />}
                    onClick={() => detail && navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)}
                  >
                    配货单
                  </Button>
                  <Button
                    icon={<FileTextOutlined />}
                    onClick={() => detail && navigate(`/warehouse/store-order/invoice/${detail.orderGUID}`)}
                  >
                    发票
                  </Button>
                  <Button
                    icon={<CopyOutlined />}
                    onClick={() => {
                      resetPasteState('allocQuantity')
                      setPasteModalOpen(true)
                    }}
                  >
                    Excel 粘贴
                  </Button>
                  <Button disabled={!selectedLineKeys.length} onClick={() => setBatchModalOpen(true)}>
                    批量修改
                  </Button>
                  <Typography.Text type="secondary">已选 {selectedLineKeys.length} 行</Typography.Text>
                </Space>
              }
            >
              <div
                style={{
                  marginBottom: 12,
                  padding: 12,
                  borderRadius: 8,
                  background: '#fafafa',
                }}
              >
                <Space wrap size={[8, 8]}>
                  <Typography.Text strong>统计过滤</Typography.Text>
                  <Input
                    allowClear
                    placeholder="按货号过滤"
                    style={{ width: 220 }}
                    value={detailItemFilter}
                    onChange={(event) => setDetailItemFilter(event.target.value)}
                  />
                  <Tag
                    color={detailStatFilter === 'all' ? 'processing' : 'default'}
                    style={{ cursor: 'pointer' }}
                    onClick={() => setDetailStatFilter('all')}
                  >
                    全部 {statSummary.all}
                  </Tag>
                  <Tag
                    color={detailStatFilter === 'orderedNotShipped' ? 'orange' : 'gold'}
                    style={{ cursor: 'pointer' }}
                    onClick={() =>
                      setDetailStatFilter((current) => (current === 'orderedNotShipped' ? 'all' : 'orderedNotShipped'))
                    }
                  >
                    有订货未发 SKU {statSummary.orderedNotShipped}
                  </Tag>
                  <Tag
                    color={detailStatFilter === 'shippedWithoutOrder' ? 'geekblue' : 'blue'}
                    style={{ cursor: 'pointer' }}
                    onClick={() =>
                      setDetailStatFilter((current) => (current === 'shippedWithoutOrder' ? 'all' : 'shippedWithoutOrder'))
                    }
                  >
                    无订货主动发货 SKU {statSummary.shippedWithoutOrder}
                  </Tag>
                  <Typography.Text type="secondary">当前显示 {sortedItems.length} 行</Typography.Text>
                </Space>
              </div>
              <Table
                rowKey="detailGUID"
                virtual
                loading={lineActionLoading}
                columns={columns}
                dataSource={pagedItems}
                rowSelection={{
                  selectedRowKeys: selectedLineKeys,
                  onChange: setSelectedLineKeys,
                  preserveSelectedRowKeys: true,
                  columnWidth: 40,
                }}
                pagination={{
                  current: detailPage,
                  pageSize: detailPageSize,
                  total: sortedItems.length,
                  showSizeChanger: true,
                  pageSizeOptions: ['20', '50', '100', '500'],
                  onChange: (page, pageSize) => {
                    setDetailPage(page)
                    setDetailPageSize(pageSize)
                  },
                }}
                onChange={(_, __, sorter) => {
                  const nextSorter = Array.isArray(sorter) ? sorter[0] : (sorter as SorterResult<StoreOrderDetailLine>)
                  const field = nextSorter?.field
                  if ((field === 'itemNumber' || field === 'locationCode') && nextSorter.order) {
                    setDetailSortField(field)
                    setDetailSortOrder(nextSorter.order)
                    return
                  }
                  setDetailSortField(null)
                  setDetailSortOrder(null)
                }}
                scroll={{ x: 2100, y: 620 }}
              />
            </Card>

            <Card>
              <Typography.Text type="secondary">
                当前阶段 2 已支持订单头编辑、商品选择加入、货柜选品、Excel 粘贴、明细单行保存、批量改发货数/进口价/状态、配货单、发票，以及完成订单。
              </Typography.Text>
              <div style={{ marginTop: 8 }}>
                <Typography.Text type="secondary">
                  最近更新时间：{formatDateTime(new Date().toISOString())}
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
              loading={lineActionLoading}
              alreadySelectedCodes={detail.items.map((item) => item.productCode)}
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
              title="Excel 粘贴到订单明细"
              open={pasteModalOpen}
              width={880}
              destroyOnClose
              onCancel={() => setPasteModalOpen(false)}
              footer={[
                <Button key="cancel" onClick={() => setPasteModalOpen(false)}>
                  关闭
                </Button>,
                <Button key="parse" type="primary" loading={parsingPaste} onClick={() => void handleParsePasteData()}>
                  解析数据
                </Button>,
                <Button
                  key="confirm"
                  type="primary"
                  loading={submittingPaste}
                  disabled={validPastePreviewCount === 0}
                  onClick={() => void handleConfirmPaste()}
                >
                  导入有效行 ({validPastePreviewCount})
                </Button>,
              ]}
            >
              <Space direction="vertical" size={16} style={{ width: '100%' }}>
                <div>
                  <Typography.Text strong>写入目标</Typography.Text>
                  <div style={{ marginTop: 8 }}>
                    <Radio.Group
                      value={pasteTargetField}
                      onChange={(event) => setPasteTargetField(event.target.value as StoreOrderPasteTargetField)}
                    >
                      <Radio value="allocQuantity">发货数量（默认）</Radio>
                      <Radio value="quantity">订货数量</Radio>
                    </Radio.Group>
                  </div>
                </div>

                <div>
                  <Typography.Text strong>Excel 文本</Typography.Text>
                  <Input.TextArea
                    rows={7}
                    value={pasteData}
                    onChange={(event) => setPasteData(event.target.value)}
                    placeholder={'按行粘贴 Excel 数据，默认第 1 列为货号，第 2 列为数量。'}
                    style={{ marginTop: 8 }}
                  />
                </div>

                <div>
                  <Typography.Text strong>列映射</Typography.Text>
                  <Space wrap size={[12, 12]} style={{ display: 'flex', marginTop: 8 }}>
                    <Space>
                      <Typography.Text>货号列</Typography.Text>
                      <Select
                        style={{ width: 100 }}
                        value={columnMapping.itemNumber}
                        options={[0, 1, 2, 3, 4].map((index) => ({
                          value: index,
                          label: `第 ${index + 1} 列`,
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
                      <Typography.Text>数量列</Typography.Text>
                      <Select
                        style={{ width: 120 }}
                        value={columnMapping.quantity}
                        options={[
                          { value: -1, label: '无，默认 1' },
                          ...[0, 1, 2, 3, 4].map((index) => ({
                            value: index,
                            label: `第 ${index + 1} 列`,
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
                      <Typography.Text>价格列</Typography.Text>
                      <Select
                        style={{ width: 120 }}
                        value={columnMapping.price}
                        options={[
                          { value: -1, label: '无' },
                          ...[0, 1, 2, 3, 4].map((index) => ({
                            value: index,
                            label: `第 ${index + 1} 列`,
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
                    <Typography.Text strong>{`预览结果：${validPastePreviewCount} / ${pastePreviewItems.length} 行可导入`}</Typography.Text>
                    <Table<PastePreviewItem>
                      size="small"
                      rowKey={(record, index) => `${record.itemNumber}-${index ?? 0}`}
                      style={{ marginTop: 8 }}
                      dataSource={pastePreviewItems}
                      pagination={{ pageSize: 8, hideOnSinglePage: true }}
                      scroll={{ y: 280 }}
                      columns={[
                        {
                          title: '状态',
                          dataIndex: 'valid',
                          width: 90,
                          render: (value: boolean) =>
                            value ? <Tag color="success">有效</Tag> : <Tag color="error">未匹配</Tag>,
                        },
                        {
                          title: '货号',
                          dataIndex: 'itemNumber',
                          width: 140,
                        },
                        {
                          title: '商品名称',
                          key: 'productName',
                          ellipsis: true,
                          render: (_, record) => record.product?.productName || '--',
                        },
                        {
                          title: pasteTargetField === 'allocQuantity' ? '发货数量' : '订货数量',
                          dataIndex: 'quantity',
                          width: 110,
                        },
                        {
                          title: '导入价',
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
            <Empty description="未找到对应订单明细" />
          </Card>
        )}
      </Spin>
    </PageContainer>
  )
}
