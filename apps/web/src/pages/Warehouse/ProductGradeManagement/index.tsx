import {
  DeleteOutlined,
  DownloadOutlined,
  DollarOutlined,
  FileExcelOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShoppingCartOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Image,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Progress,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { FilterDropdownProps, FilterValue, SorterResult, TablePaginationConfig } from 'antd/es/table/interface'
import type { ColumnsType } from 'antd/es/table'
import type { Key } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { exportProductGradesToExcel } from '../../../services/exportService'
import { getActiveStores, type StoreOption } from '../../../services/storeService'
import {
  batchAssignProducts,
  getCategoryTree,
  type WarehouseCategoryNode,
} from '../../../services/warehouseCategoryService'
import {
  batchAddStoreOrderLines,
  createStoreOrder,
  getStoreOrderList,
} from '../../../services/storeOrderService'
import {
  batchUpdateGrades,
  createOrUpdateProductGrade,
  deleteProductGrade,
  getGradesByProductCodes,
  getProductGradeList,
} from '../../../services/productGradeService'
import { PRODUCT_GRADE_CONFIG, type ProductGradeListItem } from '../../../types/productGrade'
import {
  StoreOrderFlowStatus,
  StoreOrderStatusColorMap,
  type StoreOrderListItem,
} from '../../../types/storeOrder'
import {
  ALL_PRODUCTS_FILTER_KEY,
  UNCATEGORIZED_PRODUCTS_FILTER_KEY,
  buildFilterCategoryOptions,
} from '../Categories/categoryProductFilters'
import CategoryTreePicker from '../Products/CategoryTreePicker'
import { formatWarehouseCategoryNodeName } from '../Products/categoryPath'
import BatchPriceModal from './BatchPriceModal'
import PasteImportModal from './PasteImportModal'

const GRADE_TAG_COLOR: Record<string, string> = {
  A: 'purple',
  B: 'blue',
  C: 'orange',
  D: 'red',
}

interface SupplierOption {
  label: string
  value: string
}

type ProductGradeSortOrder = 'ascend' | 'descend' | null

interface ProductGradeColumnFilters {
  supplierCode?: string
  categoryGuid?: string
  uncategorizedOnly?: boolean
  grade?: string
  hbProductNo?: string
  warehouseIsActive?: boolean
  domesticPriceMin?: number
  domesticPriceMax?: number
  importPriceMin?: number
  importPriceMax?: number
  oemPriceMin?: number
  oemPriceMax?: number
}

interface LoadListOptions {
  filters?: ProductGradeColumnFilters
  sortField?: string
  sortOrder?: ProductGradeSortOrder
}

type AddToOrderMode = 'existing' | 'new'

const EDITABLE_STORE_ORDER_STATUSES = [
  StoreOrderFlowStatus.ShoppingCart,
  StoreOrderFlowStatus.Submitted,
]
const ORDER_DROPDOWN_PAGE_SIZE = 20

function formatPrice(value?: number, prefix = '¥') {
  if (value === undefined || value === null) return '--'
  return `${prefix}${value.toFixed(2)}`
}

function encodePriceRange(min?: number, max?: number) {
  if (min === undefined && max === undefined) return undefined
  return `${min ?? ''}|${max ?? ''}`
}

function parsePriceRange(value?: Key | boolean): { min?: number; max?: number } {
  if (typeof value !== 'string') return {}
  const [minText, maxText] = value.split('|')
  const min = minText === '' ? undefined : Number(minText)
  const max = maxText === '' ? undefined : Number(maxText)
  return {
    min: Number.isFinite(min) ? min : undefined,
    max: Number.isFinite(max) ? max : undefined,
  }
}

function getSingleFilterValue(value?: FilterValue | null) {
  const first = value?.[0]
  return first == null || first === '' ? undefined : String(first)
}

function normalizeFilterNumber(value: string | number | null) {
  if (value === null || value === '') return undefined
  const numberValue = Number(value)
  return Number.isFinite(numberValue) ? numberValue : undefined
}

function getBooleanFilterValue(value?: FilterValue | null) {
  const first = value?.[0]
  if (first === true || first === 'true') return true
  if (first === false || first === 'false') return false
  return undefined
}

function getOrderStatusI18nKey(status: StoreOrderFlowStatus) {
  return status === StoreOrderFlowStatus.ShoppingCart
    ? 'productGrade.orderStatusShoppingCart'
    : 'productGrade.orderStatusSubmitted'
}

function collectCategoryExpandedKeys(nodes: WarehouseCategoryNode[], maxLevel: number, level = 1): string[] {
  if (level > maxLevel) {
    return []
  }

  return nodes.flatMap((node) => [
    node.categoryGUID,
    ...collectCategoryExpandedKeys(node.children || [], maxLevel, level + 1),
  ])
}

function findWarehouseCategory(
  nodes: WarehouseCategoryNode[],
  targetGuid?: string,
): WarehouseCategoryNode | undefined {
  if (!targetGuid) {
    return undefined
  }

  for (const node of nodes) {
    if (node.categoryGUID === targetGuid) {
      return node
    }
    const child = findWarehouseCategory(node.children || [], targetGuid)
    if (child) {
      return child
    }
  }

  return undefined
}

function collectCategoryAndDescendantGuids(nodes: WarehouseCategoryNode[], targetGuid?: string): Set<string> {
  const target = findWarehouseCategory(nodes, targetGuid)
  const result = new Set<string>()

  const visit = (node?: WarehouseCategoryNode) => {
    if (!node) {
      return
    }
    result.add(node.categoryGUID)
    ;(node.children || []).forEach(visit)
  }

  visit(target)
  return result
}

interface CategoryFilterDropdownPanelProps {
  filterProps: FilterDropdownProps
  options: Array<{ label: string; value: string }>
  loading: boolean
  onLoad: () => void | Promise<void>
  placeholder: string
  queryText: string
  resetText: string
}

function CategoryFilterDropdownPanel({
  filterProps,
  options,
  loading,
  onLoad,
  placeholder,
  queryText,
  resetText,
}: CategoryFilterDropdownPanelProps) {
  const { selectedKeys, setSelectedKeys, confirm, clearFilters } = filterProps

  useEffect(() => {
    void onLoad()
  }, [onLoad])

  return (
    <Space direction="vertical" style={{ padding: 8, width: 260 }}>
      <Select
        showSearch
        allowClear
        placeholder={placeholder}
        value={selectedKeys[0] as string | undefined}
        options={options}
        loading={loading}
        optionFilterProp="label"
        style={{ width: '100%' }}
        onChange={(value) => {
          setSelectedKeys(value && value !== ALL_PRODUCTS_FILTER_KEY ? [value] : [])
        }}
      />
      <Space>
        <Button type="primary" size="small" onClick={() => confirm()}>
          {queryText}
        </Button>
        <Button
          size="small"
          onClick={() => {
            clearFilters?.()
            confirm()
          }}
        >
          {resetText}
        </Button>
      </Space>
    </Space>
  )
}

export default function ProductGradeManagementPage() {
  const { t, i18n } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<ProductGradeListItem[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [total, setTotal] = useState(0)
  const [search, setSearch] = useState('')
  const [columnFilters, setColumnFilters] = useState<ProductGradeColumnFilters>({})
  const [sortField, setSortField] = useState<string | undefined>(undefined)
  const [sortOrder, setSortOrder] = useState<ProductGradeSortOrder>(null)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [suppliersLoaded, setSuppliersLoaded] = useState(false)
  const [categoryTree, setCategoryTree] = useState<WarehouseCategoryNode[]>([])
  const [categoryLoading, setCategoryLoading] = useState(false)
  const [categoriesLoaded, setCategoriesLoaded] = useState(false)
  const [categoryEditOpen, setCategoryEditOpen] = useState(false)
  const [categoryEditRecord, setCategoryEditRecord] = useState<ProductGradeListItem | null>(null)
  const [batchCategoryOpen, setBatchCategoryOpen] = useState(false)
  const [targetCategoryGuid, setTargetCategoryGuid] = useState<string | undefined>(undefined)
  const [categoryExpandedKeys, setCategoryExpandedKeys] = useState<string[]>([])
  const [categorySaving, setCategorySaving] = useState(false)
  const [batchCategorySaving, setBatchCategorySaving] = useState(false)
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([])
  const [batchGrade, setBatchGrade] = useState<string | undefined>(undefined)
  const [pasteImportOpen, setPasteImportOpen] = useState(false)
  const [batchPriceOpen, setBatchPriceOpen] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const [exportIncludeImage, setExportIncludeImage] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportMessage, setExportMessage] = useState('')
  const [addOrderOpen, setAddOrderOpen] = useState(false)
  const [addOrderMode, setAddOrderMode] = useState<AddToOrderMode>('existing')
  const [editableOrders, setEditableOrders] = useState<StoreOrderListItem[]>([])
  const [orderPage, setOrderPage] = useState(0)
  const [orderTotal, setOrderTotal] = useState(0)
  const [orderOptionsLoaded, setOrderOptionsLoaded] = useState(false)
  const [orderHasMore, setOrderHasMore] = useState(false)
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])
  const [orderLoading, setOrderLoading] = useState(false)
  const [storeLoading, setStoreLoading] = useState(false)
  const [addOrderSubmitting, setAddOrderSubmitting] = useState(false)
  const [targetOrderGuid, setTargetOrderGuid] = useState<string | undefined>(undefined)
  const [targetStoreCode, setTargetStoreCode] = useState<string | undefined>(undefined)
  const [orderKeyword, setOrderKeyword] = useState('')
  const [newOrderRemarks, setNewOrderRemarks] = useState('')
  const listAbortRef = useRef<AbortController | null>(null)
  const listRequestSeqRef = useRef(0)
  const orderRequestSeqRef = useRef(0)
  const supplierAbortRef = useRef<AbortController | null>(null)
  const gradeFilterOptions = [
    { label: t('productGrade.allGrades'), value: '' },
    ...Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => ({
      label: t(`productGrade.${cfg.i18nKey}`),
      value: key,
    })),
  ]
  const categoryFilterOptions = useMemo(
    () => buildFilterCategoryOptions(categoryTree, t, i18n.language),
    [categoryTree, i18n.language, t],
  )
  const selectedTargetCategory = useMemo(
    () => findWarehouseCategory(categoryTree, targetCategoryGuid),
    [categoryTree, targetCategoryGuid],
  )

  const loadSuppliers = useCallback(async () => {
    if (suppliersLoaded || supplierLoading) {
      return
    }

    supplierAbortRef.current?.abort()
    const controller = new AbortController()
    supplierAbortRef.current = controller
    setSupplierLoading(true)
    try {
      const result = await getActiveChinaSuppliers(controller.signal)
      setSuppliers(
        result.map((item) => ({
          label: `${item.supplierCode} - ${item.supplierName}`,
          value: item.supplierCode,
        })),
      )
      setSuppliersLoaded(true)
    } catch (error) {
      if (controller.signal.aborted) return
      console.error(error)
      message.error(t('productGrade.loadSuppliersFailed'))
    } finally {
      if (supplierAbortRef.current === controller) {
        supplierAbortRef.current = null
        setSupplierLoading(false)
      }
    }
  }, [supplierLoading, suppliersLoaded, t])

  const loadCategories = useCallback(async () => {
    if (categoriesLoaded || categoryLoading) {
      return
    }

    setCategoryLoading(true)
    try {
      setCategoryTree(await getCategoryTree())
      setCategoriesLoaded(true)
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.loadCategoriesFailed'))
    } finally {
      setCategoryLoading(false)
    }
  }, [categoriesLoaded, categoryLoading, t])

  const ensureCategoriesLoaded = useCallback(async () => {
    if (categoriesLoaded || categoryLoading) {
      return categoryTree
    }

    setCategoryLoading(true)
    try {
      const tree = await getCategoryTree()
      setCategoryTree(tree)
      setCategoriesLoaded(true)
      return tree
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.loadCategoriesFailed'))
      return []
    } finally {
      setCategoryLoading(false)
    }
  }, [categoriesLoaded, categoryLoading, categoryTree, t])

  const loadList = useCallback(async (
    nextPage = page,
    nextPageSize = pageSize,
    options: LoadListOptions = {},
  ) => {
    const activeFilters = options.filters ?? columnFilters
    const activeSortField = options.sortField ?? sortField
    const activeSortOrder = options.sortOrder ?? sortOrder
    const requestSeq = listRequestSeqRef.current + 1
    listRequestSeqRef.current = requestSeq
    listAbortRef.current?.abort()
    const controller = new AbortController()
    listAbortRef.current = controller
    setLoading(true)
    try {
      const result = await getProductGradeList({
        page: nextPage,
        pageSize: nextPageSize,
        search: search || undefined,
        grade: activeFilters.grade || undefined,
        supplierCode: activeFilters.supplierCode,
        hbProductNo: activeFilters.hbProductNo,
        categoryGuid: activeFilters.categoryGuid,
        uncategorizedOnly: activeFilters.uncategorizedOnly,
        warehouseIsActive: activeFilters.warehouseIsActive,
        domesticPriceMin: activeFilters.domesticPriceMin,
        domesticPriceMax: activeFilters.domesticPriceMax,
        importPriceMin: activeFilters.importPriceMin,
        importPriceMax: activeFilters.importPriceMax,
        oemPriceMin: activeFilters.oemPriceMin,
        oemPriceMax: activeFilters.oemPriceMax,
        sortField: activeSortField,
        sortDirection: activeSortOrder === 'ascend' ? 'asc' : activeSortOrder === 'descend' ? 'desc' : undefined,
        signal: controller.signal,
      })
      if (requestSeq !== listRequestSeqRef.current) {
        return
      }
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      if (controller.signal.aborted) return
      console.error(error)
      message.error(t('productGrade.loadListFailed'))
    } finally {
      if (requestSeq === listRequestSeqRef.current) {
        setLoading(false)
      }
      if (listAbortRef.current === controller) {
        listAbortRef.current = null
      }
    }
  }, [columnFilters, page, pageSize, search, sortField, sortOrder, t])

  useEffect(() => {
    void loadList(1, pageSize)
    return () => {
      listAbortRef.current?.abort()
      supplierAbortRef.current?.abort()
    }
  }, [])

  const handleDelete = useCallback(async (id: string) => {
    try {
      await deleteProductGrade(id)
      message.success(t('common.deleteSuccess'))
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('common.deleteFailed'))
    }
  }, [loadList, page, pageSize, t])

  const handleBatchUpdate = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    if (!batchGrade) {
      message.warning(t('productGrade.selectTargetGrade'))
      return
    }
    try {
      await batchUpdateGrades({
        items: selectedRowKeys.map((productCode) => ({
          productCode,
          grade: batchGrade,
        })),
      })
      message.success(t('productGrade.batchUpdateSuccess', { count: selectedRowKeys.length }))
      setSelectedRowKeys([])
      setBatchGrade(undefined)
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.batchUpdateFailed'))
    }
  }

  const handleInlineGradeChange = useCallback(async (productCode: string, newGrade: string) => {
    try {
      await createOrUpdateProductGrade({ productCode, grade: newGrade })
      message.success(t('productGrade.updateSuccess'))
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.updateFailed'))
    }
  }, [loadList, page, pageSize, t])

  const openExportModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    setExportIncludeImage(true)
    setExportProgress(0)
    setExportMessage('')
    setExportOpen(true)
  }

  const handleExportExcel = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }

    try {
      setExporting(true)
      setExportProgress(0)
      setExportMessage(t('productGrade.exportPreparing'))

      const selectedProductCodes = selectedRowKeys.map(String)
      // 选中项可能来自跨页选择，导出前按商品编码重新拉完整字段，避免只导出当前页残缺数据。
      const exportRows = await getGradesByProductCodes(selectedProductCodes)
      const rowOrder = new Map(selectedProductCodes.map((code, index) => [code, index]))
      const orderedRows = exportRows
        .filter((item) => rowOrder.has(item.productCode))
        .sort((a, b) => (rowOrder.get(a.productCode) ?? 0) - (rowOrder.get(b.productCode) ?? 0))

      if (!orderedRows.length) {
        message.warning(t('productGrade.noDataToExport'))
        return
      }

      const result = await exportProductGradesToExcel(orderedRows, {
        includeProductImage: exportIncludeImage,
        fileName: t('productGrade.exportFileName'),
        onProgress: (progress, nextMessage) => {
          setExportProgress(progress)
          setExportMessage(nextMessage)
        },
      })

      if (result.failedProductImages.length) {
        message.warning(t('productGrade.exportImageFailed', { count: result.failedProductImages.length }))
      } else {
        message.success(t('productGrade.exportSuccess'))
      }
      setExportOpen(false)
      setExportProgress(0)
      setExportMessage('')
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productGrade.exportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const loadEditableOrders = useCallback(async (
    options: { keyword?: string; pageNumber?: number; append?: boolean } = {},
  ) => {
    const keyword = options.keyword ?? orderKeyword
    const pageNumber = options.pageNumber ?? 1
    const append = options.append ?? false
    const requestSeq = orderRequestSeqRef.current + 1
    orderRequestSeqRef.current = requestSeq
    if (!append) {
      setEditableOrders([])
      setOrderPage(0)
      setOrderTotal(0)
      setOrderHasMore(false)
      setOrderOptionsLoaded(false)
    }
    setOrderLoading(true)
    try {
      const result = await getStoreOrderList({
        keyword: keyword.trim() || undefined,
        pageNumber,
        pageSize: ORDER_DROPDOWN_PAGE_SIZE,
        statusList: EDITABLE_STORE_ORDER_STATUSES,
        // 商品等级弹窗只取下拉首屏，按创建时间倒序保证最新订单优先出现。
        sortBy: 'createdAt',
        sortDescending: true,
      })
      if (requestSeq !== orderRequestSeqRef.current) return
      const nextItems = result.items.filter((item) => EDITABLE_STORE_ORDER_STATUSES.includes(item.flowStatus))
      setEditableOrders((current) => {
        if (!append) return nextItems
        const byGuid = new Map(current.map((item) => [item.orderGUID, item]))
        nextItems.forEach((item) => byGuid.set(item.orderGUID, item))
        return Array.from(byGuid.values())
      })
      const nextPage = result.page || pageNumber
      const nextPageSize = result.pageSize || ORDER_DROPDOWN_PAGE_SIZE
      setOrderPage(nextPage)
      setOrderTotal(result.total)
      setOrderHasMore(nextPage * nextPageSize < result.total)
      setOrderOptionsLoaded(true)
    } catch (error) {
      if (requestSeq !== orderRequestSeqRef.current) return
      console.error(error)
      message.error(t('productGrade.loadStoreOrdersFailed'))
    } finally {
      if (requestSeq === orderRequestSeqRef.current) {
        setOrderLoading(false)
      }
    }
  }, [orderKeyword, t])

  const loadStores = useCallback(async () => {
    if (storeOptions.length > 0 || storeLoading) return
    setStoreLoading(true)
    try {
      setStoreOptions(await getActiveStores())
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.loadStoresFailed'))
    } finally {
      setStoreLoading(false)
    }
  }, [storeLoading, storeOptions.length, t])

  const openAddOrderModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    setAddOrderMode('existing')
    setTargetOrderGuid(undefined)
    setTargetStoreCode(undefined)
    setOrderKeyword('')
    setEditableOrders([])
    setOrderPage(0)
    setOrderTotal(0)
    setOrderOptionsLoaded(false)
    setOrderHasMore(false)
    setNewOrderRemarks('')
    setAddOrderOpen(true)
    void loadStores()
  }

  const handleAddToStoreOrder = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    if (addOrderMode === 'existing' && !targetOrderGuid) {
      message.warning(t('productGrade.selectTargetOrder'))
      return
    }
    if (addOrderMode === 'new' && !targetStoreCode) {
      message.warning(t('productGrade.selectTargetStore'))
      return
    }

    setAddOrderSubmitting(true)
    try {
      const selectedProductCodes = selectedRowKeys.map(String)
      // 跨页选择时当前表格不一定有完整行数据，提交前按商品编码回查最新商品字段。
      const latestRows = await getGradesByProductCodes(selectedProductCodes)
      const latestByCode = new Map(latestRows.map((item) => [item.productCode, item]))
      const missingProductCodes = selectedProductCodes.filter((productCode) => !latestByCode.has(productCode))
      if (missingProductCodes.length > 0) {
        message.warning(t('productGrade.selectedProductsMissing', {
          count: missingProductCodes.length,
          codes: missingProductCodes.slice(0, 10).join(', '),
        }))
        return
      }
      const items = selectedProductCodes
        .map((productCode) => {
          const item = latestByCode.get(productCode)!
          const minOrderQuantity = item?.minOrderQuantity
          const orderItem: { productCode: string; quantity: number; importPrice?: number } = {
            productCode,
            quantity: minOrderQuantity && minOrderQuantity > 0 ? minOrderQuantity : 1,
          }
          if (item?.importPrice !== undefined) {
            orderItem.importPrice = item.importPrice
          }
          return orderItem
        })
        .filter((item) => item.productCode)

      if (!items.length) {
        message.warning(t('productGrade.noMatchedProducts'))
        return
      }

      let orderGUID = targetOrderGuid!
      if (addOrderMode === 'new') {
        const createPayload = { storeCode: targetStoreCode!, remarks: newOrderRemarks.trim() }
        orderGUID = await createStoreOrder(createPayload.remarks ? createPayload : { storeCode: createPayload.storeCode })
      }

      await batchAddStoreOrderLines({ orderGUID, items })
      message.success(t('productGrade.addToStoreOrderSuccess', { count: items.length }))
      setAddOrderOpen(false)
      setSelectedRowKeys([])
      setTargetOrderGuid(undefined)
      setTargetStoreCode(undefined)
      setOrderKeyword('')
      setOrderOptionsLoaded(false)
      setOrderHasMore(false)
      setOrderPage(0)
      setOrderTotal(0)
      setNewOrderRemarks('')
      if (addOrderMode === 'new') {
        void loadEditableOrders({ keyword: '', pageNumber: 1 })
      }
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productGrade.addToStoreOrderFailed'))
    } finally {
      setAddOrderSubmitting(false)
    }
  }

  const openCategoryEditModal = useCallback(async (record: ProductGradeListItem) => {
    setCategoryEditRecord(record)
    setTargetCategoryGuid(record.categoryGuid)
    setCategoryEditOpen(true)
    const tree = await ensureCategoriesLoaded()
    setCategoryExpandedKeys(collectCategoryExpandedKeys(tree, 1))
  }, [ensureCategoriesLoaded])

  const handleCategoryEditCancel = () => {
    if (categorySaving) {
      return
    }
    setCategoryEditOpen(false)
    setCategoryEditRecord(null)
    setTargetCategoryGuid(undefined)
  }

  const openBatchCategoryModal = useCallback(async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }

    setTargetCategoryGuid(undefined)
    setBatchCategoryOpen(true)
    const tree = await ensureCategoriesLoaded()
    setCategoryExpandedKeys(collectCategoryExpandedKeys(tree, 1))
  }, [ensureCategoriesLoaded, selectedRowKeys.length, t])

  const handleBatchCategoryCancel = () => {
    if (batchCategorySaving) {
      return
    }
    setBatchCategoryOpen(false)
    setTargetCategoryGuid(undefined)
  }

  const handleBatchCategorySave = async () => {
    const selectedProductCodes = selectedRowKeys.map(String)
    if (selectedProductCodes.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    if (!targetCategoryGuid) {
      message.warning(t('productGrade.selectTargetCategory'))
      return
    }

    setBatchCategorySaving(true)
    try {
      // 批量分类只修改本地 Product.WarehouseCategoryGUID，不影响等级、价格和上下架状态。
      const affected = await batchAssignProducts(targetCategoryGuid, selectedProductCodes)
      if (affected < selectedProductCodes.length) {
        throw new Error(t('productGrade.batchCategoryPartialFailed', {
          affected,
          total: selectedProductCodes.length,
        }))
      }

      const nextCategory = findWarehouseCategory(categoryTree, targetCategoryGuid)
      const nextCategoryName = nextCategory
        ? formatWarehouseCategoryNodeName(nextCategory, i18n.language)
        : ''
      const selectedCodeSet = new Set(selectedProductCodes)
      const filteredCategoryGuids = collectCategoryAndDescendantGuids(
        categoryTree,
        columnFilters.categoryGuid,
      )
      const shouldRemoveFromCurrentPage = Boolean(
        columnFilters.uncategorizedOnly
          || (columnFilters.categoryGuid && !filteredCategoryGuids.has(targetCategoryGuid)),
      )

      if (shouldRemoveFromCurrentPage) {
        const currentPageSelectedCount = data.filter((item) => selectedCodeSet.has(item.productCode)).length
        setData((items) => items.filter((item) => !selectedCodeSet.has(item.productCode)))
        setTotal((current) => Math.max(0, current - currentPageSelectedCount))
      } else {
        setData((items) => items.map((item) => (
          selectedCodeSet.has(item.productCode)
            ? {
              ...item,
              categoryGuid: targetCategoryGuid,
              categoryName: nextCategory?.categoryName || nextCategoryName,
              categoryChineseName: nextCategory?.chineseName,
            }
            : item
        )))
      }

      message.success(t('productGrade.batchCategoryUpdateSuccess', { count: selectedProductCodes.length }))
      setBatchCategoryOpen(false)
      setSelectedRowKeys([])
      setTargetCategoryGuid(undefined)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productGrade.categoryUpdateFailed'))
    } finally {
      setBatchCategorySaving(false)
    }
  }

  const handleCategoryEditSave = async () => {
    if (!categoryEditRecord) {
      return
    }
    if (!targetCategoryGuid) {
      message.warning(t('productGrade.selectTargetCategory'))
      return
    }
    if (targetCategoryGuid === categoryEditRecord.categoryGuid) {
      message.info(t('productGrade.categoryUnchanged'))
      return
    }

    setCategorySaving(true)
    try {
      // 分类修改只提交商品编码和目标分类，不触碰等级、价格、上下架等其他字段。
      const affected = await batchAssignProducts(targetCategoryGuid, [categoryEditRecord.productCode])
      if (affected < 1) {
        // 后端没有实际更新本地 Product 行时，不做前端本地覆盖，避免刷新后分类回退。
        throw new Error(t('productGrade.categoryUpdateFailed'))
      }
      const nextCategory = findWarehouseCategory(categoryTree, targetCategoryGuid)
      const nextCategoryName = nextCategory
        ? formatWarehouseCategoryNodeName(nextCategory, i18n.language)
        : categoryEditRecord.categoryName
      const filteredCategoryGuids = collectCategoryAndDescendantGuids(
        categoryTree,
        columnFilters.categoryGuid,
      )
      const shouldRemoveFromCurrentPage = Boolean(
        columnFilters.uncategorizedOnly
          || (columnFilters.categoryGuid && !filteredCategoryGuids.has(targetCategoryGuid)),
      )

      if (shouldRemoveFromCurrentPage) {
        setData((items) => items.filter((item) => item.productCode !== categoryEditRecord.productCode))
        setTotal((current) => Math.max(0, current - 1))
      } else {
        setData((items) => items.map((item) => (
          item.productCode === categoryEditRecord.productCode
            ? {
              ...item,
              categoryGuid: targetCategoryGuid,
              categoryName: nextCategory?.categoryName || nextCategoryName,
              categoryChineseName: nextCategory?.chineseName,
            }
            : item
        )))
      }

      message.success(t('productGrade.categoryUpdateSuccess'))
      setCategoryEditOpen(false)
      setCategoryEditRecord(null)
      setTargetCategoryGuid(undefined)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productGrade.categoryUpdateFailed'))
    } finally {
      setCategorySaving(false)
    }
  }

  const getFiltersFromTable = (filters: Record<string, FilterValue | null>): ProductGradeColumnFilters => {
    const domesticRange = parsePriceRange(filters.domesticPrice?.[0])
    const importRange = parsePriceRange(filters.importPrice?.[0])
    const oemRange = parsePriceRange(filters.oemPrice?.[0])
    const categoryFilterValue = getSingleFilterValue(filters.categoryGuid)

    return {
      supplierCode: getSingleFilterValue(filters.supplierCode),
      categoryGuid: categoryFilterValue
        && categoryFilterValue !== UNCATEGORIZED_PRODUCTS_FILTER_KEY
        && categoryFilterValue !== ALL_PRODUCTS_FILTER_KEY
        ? categoryFilterValue
        : undefined,
      uncategorizedOnly: categoryFilterValue === UNCATEGORIZED_PRODUCTS_FILTER_KEY ? true : undefined,
      grade: getSingleFilterValue(filters.grade),
      hbProductNo: getSingleFilterValue(filters.hbProductNo),
      warehouseIsActive: getBooleanFilterValue(filters.warehouseIsActive),
      domesticPriceMin: domesticRange.min,
      domesticPriceMax: domesticRange.max,
      importPriceMin: importRange.min,
      importPriceMax: importRange.max,
      oemPriceMin: oemRange.min,
      oemPriceMax: oemRange.max,
    }
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    filters: Record<string, FilterValue | null>,
    sorter: SorterResult<ProductGradeListItem> | SorterResult<ProductGradeListItem>[],
    extra: { action: 'paginate' | 'sort' | 'filter' },
  ) => {
    const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const rawField = currentSorter?.field || currentSorter?.column?.dataIndex
    const field = Array.isArray(rawField) ? rawField.join('.') : rawField ? String(rawField) : undefined
    const order = currentSorter?.order as ProductGradeSortOrder | undefined
    const nextSortField = field && order ? field : undefined
    const nextSortOrder = field && order ? order : null
    const nextFilters = getFiltersFromTable(filters)
    // 列头排序/过滤都走服务端，变化时回到第一页，避免只处理当前页数据。
    const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1
    // 表头受控状态在发请求前同步，异步成功回调不再用旧闭包覆盖当前筛选/排序。
    setColumnFilters(nextFilters)
    setSortField(nextSortField)
    setSortOrder(nextSortOrder)

    void loadList(nextPage, pagination.pageSize ?? pageSize, {
      filters: nextFilters,
      sortField: nextSortField,
      sortOrder: nextSortOrder,
    })
  }

  const renderSupplierFilterDropdown = ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => (
    <Space direction="vertical" style={{ padding: 8, width: 240 }}>
      <Select
        showSearch
        allowClear
        placeholder={t('productGrade.filterSupplier')}
        value={selectedKeys[0] as string | undefined}
        options={suppliers}
        loading={supplierLoading}
        optionFilterProp="label"
        style={{ width: '100%' }}
        onDropdownVisibleChange={(open) => {
          if (open) void loadSuppliers()
        }}
        onChange={(value) => setSelectedKeys(value ? [value] : [])}
      />
      <Space>
        <Button type="primary" size="small" onClick={() => confirm()}>
          {t('common.query')}
        </Button>
        <Button
          size="small"
          onClick={() => {
            clearFilters?.()
            confirm()
          }}
        >
          {t('common.reset', '重置')}
        </Button>
      </Space>
    </Space>
  )

  const renderCategoryFilterDropdown = (filterProps: FilterDropdownProps) => (
    <CategoryFilterDropdownPanel
      filterProps={filterProps}
      options={categoryFilterOptions}
      loading={categoryLoading}
      onLoad={loadCategories}
      placeholder={t('productGrade.category')}
      queryText={t('common.query')}
      resetText={t('common.reset', '重置')}
    />
  )

  const formatProductGradeCategory = useCallback((record: ProductGradeListItem) => {
    const name = formatWarehouseCategoryNodeName({
      categoryName: record.categoryName || '',
      chineseName: record.categoryChineseName,
    }, i18n.language)

    return name || '--'
  }, [i18n.language])

  const renderTextFilterDropdown = ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => (
    <Space direction="vertical" style={{ padding: 8 }}>
      <Input
        autoFocus
        allowClear
        placeholder={t('productGrade.itemNumberFilterPlaceholder', '输入货号')}
        value={selectedKeys[0] as string | undefined}
        onChange={(event) => setSelectedKeys(event.target.value ? [event.target.value] : [])}
        onPressEnter={() => confirm()}
      />
      <Space>
        <Button type="primary" size="small" onClick={() => confirm()}>
          {t('common.query')}
        </Button>
        <Button
          size="small"
          onClick={() => {
            clearFilters?.()
            confirm()
          }}
        >
          {t('common.reset', '重置')}
        </Button>
      </Space>
    </Space>
  )

  const renderPriceFilterDropdown = (
    minValue: number | undefined,
    maxValue: number | undefined,
  ) => ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => {
    const selectedRangeValue = selectedKeys[0]
    const hasSelectedRange = typeof selectedRangeValue === 'string'
    const range = parsePriceRange(selectedRangeValue)
    const min = hasSelectedRange ? range.min : minValue
    const max = hasSelectedRange ? range.max : maxValue

    const updateRange = (nextMin?: number, nextMax?: number) => {
      const encoded = encodePriceRange(nextMin, nextMax)
      setSelectedKeys(encoded ? [encoded] : [])
    }

    return (
      <Space direction="vertical" style={{ padding: 8, width: 220 }}>
        <InputNumber
          placeholder={t('common.min', '最小值')}
          value={min}
          min={0}
          precision={2}
          style={{ width: '100%' }}
          onChange={(value) => updateRange(normalizeFilterNumber(value), max)}
        />
        <InputNumber
          placeholder={t('common.max', '最大值')}
          value={max}
          min={0}
          precision={2}
          style={{ width: '100%' }}
          onChange={(value) => updateRange(min, normalizeFilterNumber(value))}
        />
        <Space>
          <Button type="primary" size="small" onClick={() => confirm()}>
            {t('common.query')}
          </Button>
          <Button
            size="small"
            onClick={() => {
              clearFilters?.()
              confirm()
            }}
          >
            {t('common.reset', '重置')}
          </Button>
        </Space>
      </Space>
    )
  }

  const columns = useMemo<ColumnsType<ProductGradeListItem>>(
    () => [
      {
        title: t('column.index'),
        width: 50,
        render: (_v, _r, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: t('column.supplier'),
        dataIndex: 'supplierName',
        width: 150,
        sorter: true,
        sortOrder: sortField === 'supplierName' ? sortOrder : null,
        render: (_, record) => record.supplierName || record.supplierCode || '--',
      },
      {
        title: t('column.supplierCode'),
        dataIndex: 'supplierCode',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'supplierCode' ? sortOrder : null,
        filterDropdown: renderSupplierFilterDropdown,
        filteredValue: columnFilters.supplierCode ? [columnFilters.supplierCode] : null,
        render: (value?: string) => value || '--',
      },
      {
        title: t('productGrade.category'),
        dataIndex: 'categoryGuid',
        width: 150,
        filterDropdown: renderCategoryFilterDropdown,
        filteredValue: columnFilters.uncategorizedOnly
          ? [UNCATEGORIZED_PRODUCTS_FILTER_KEY]
          : columnFilters.categoryGuid
            ? [columnFilters.categoryGuid]
            : null,
        render: (_value: string | undefined, record) => (
          <Button
            type="link"
            size="small"
            style={{ padding: 0, height: 'auto', whiteSpace: 'normal', textAlign: 'left' }}
            onClick={(event) => {
              event.stopPropagation()
              void openCategoryEditModal(record)
            }}
          >
            {formatProductGradeCategory(record)}
          </Button>
        ),
      },
      {
        title: t('column.itemNumber'),
        dataIndex: 'hbProductNo',
        width: 140,
        sorter: true,
        sortOrder: sortField === 'hbProductNo' ? sortOrder : null,
        filterDropdown: renderTextFilterDropdown,
        filteredValue: columnFilters.hbProductNo ? [columnFilters.hbProductNo] : null,
        render: (value?: string) => value || '--',
      },
      {
        title: t('column.productName'),
        dataIndex: 'productName',
        width: 220,
        ellipsis: true,
        render: (value?: string) => (
          <Tooltip title={value || undefined}>
            <span>{value || '--'}</span>
          </Tooltip>
        ),
      },
      {
        title: t('column.image'),
        dataIndex: 'productImage',
        width: 80,
        render: (value: string | undefined, record) =>
          value ? (
            <Image
              src={value}
              alt={record.productName || record.hbProductNo || record.productCode}
              width={48}
              height={48}
              loading="lazy"
              decoding="async"
              style={{ objectFit: 'contain' }}
              preview={{ mask: '' }}
              fallback="data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDgiIGhlaWdodD0iNDgiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjQ4IiBoZWlnaHQ9IjQ4IiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjQiIHk9IjI4IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmb250LXNpemU9IjEyIiBmaWxsPSIjY2NjIj7ml6DnvKnnlaXimLQ8L3RleHQ+PC9zdmc+"
            />
          ) : (
            '--'
          ),
      },
      {
        title: t('column.levelLabel'),
        dataIndex: 'grade',
        width: 100,
        sorter: true,
        sortOrder: sortField === 'grade' ? sortOrder : null,
        filters: Object.keys(PRODUCT_GRADE_CONFIG).map((key) => ({ text: key, value: key })),
        filteredValue: columnFilters.grade ? [columnFilters.grade] : null,
        render: (grade: string, record) => (
          <Select
            value={grade}
            size="small"
            style={{ width: 80 }}
            onChange={(value) => void handleInlineGradeChange(record.productCode, value)}
          >
            {Object.keys(PRODUCT_GRADE_CONFIG).map((key) => (
              <Select.Option key={key} value={key}>
                <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 0 }}>
                  {key}
                </Tag>
              </Select.Option>
            ))}
          </Select>
        ),
      },
      {
        title: t('productGrade.warehouseStatus'),
        dataIndex: 'warehouseIsActive',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'warehouseIsActive' ? sortOrder : null,
        filters: [
          { text: t('productGrade.warehouseActive'), value: 'true' },
          { text: t('productGrade.warehouseInactive'), value: 'false' },
        ],
        filteredValue: columnFilters.warehouseIsActive === undefined
          ? null
          : [String(columnFilters.warehouseIsActive)],
        render: (value?: boolean | null) => {
          if (value === true) {
            return <Tag color="success">{t('productGrade.warehouseActive')}</Tag>
          }
          if (value === false) {
            return <Tag>{t('productGrade.warehouseInactive')}</Tag>
          }
          return <Tag color="default">{t('productGrade.warehouseStatusUnknown')}</Tag>
        },
      },
      {
        title: t('productGrade.domesticPriceRmb'),
        dataIndex: 'domesticPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'domesticPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.domesticPriceMin,
          columnFilters.domesticPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.domesticPriceMin, columnFilters.domesticPriceMax)
          ? [encodePriceRange(columnFilters.domesticPriceMin, columnFilters.domesticPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value),
      },
      {
        title: t('productGrade.importPriceAud'),
        dataIndex: 'importPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'importPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.importPriceMin,
          columnFilters.importPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.importPriceMin, columnFilters.importPriceMax)
          ? [encodePriceRange(columnFilters.importPriceMin, columnFilters.importPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: t('productGrade.retailPriceAud'),
        dataIndex: 'oemPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'oemPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.oemPriceMin,
          columnFilters.oemPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.oemPriceMin, columnFilters.oemPriceMax)
          ? [encodePriceRange(columnFilters.oemPriceMin, columnFilters.oemPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: t('column.action'),
        key: 'action',
        width: 80,
        fixed: 'right',
        render: (_, record) => (
          <Popconfirm
            title={t('productGrade.confirmDelete')}
            description={t('productGrade.deleteGradeHint')}
            onConfirm={() => void handleDelete(record.id)}
          >
            <Tooltip title={t('productGrade.deleteGrade')}>
              <Button type="link" danger icon={<DeleteOutlined />} size="small" />
            </Tooltip>
          </Popconfirm>
        ),
      },
    ],
    [
      categoryFilterOptions,
      categoryLoading,
      columnFilters,
      formatProductGradeCategory,
      handleDelete,
      handleInlineGradeChange,
      loadCategories,
      openCategoryEditModal,
      page,
      pageSize,
      sortField,
      sortOrder,
      supplierLoading,
      suppliers,
      t,
    ],
  )

  const categoryEditCurrentText = categoryEditRecord ? formatProductGradeCategory(categoryEditRecord) : '--'
  const categoryEditTargetText = selectedTargetCategory
    ? formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)
    : ''

  return (
    <PageContainer title={t('productGrade.title')} subtitle={t('productGrade.subtitle')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            showSearch
            allowClear
            placeholder={t('productGrade.filterSupplier')}
            value={columnFilters.supplierCode}
            onDropdownVisibleChange={(open) => {
              if (open) void loadSuppliers()
            }}
            onChange={(value) => {
              setColumnFilters((current) => ({ ...current, supplierCode: value }))
            }}
            options={suppliers}
            loading={supplierLoading}
            style={{ width: 220 }}
            optionFilterProp="label"
          />
          <Select
            value={columnFilters.grade ?? ''}
            onChange={(value) => {
              setColumnFilters((current) => ({ ...current, grade: value || undefined }))
            }}
            options={gradeFilterOptions}
            style={{ width: 180 }}
          />
          <Input
            placeholder={t('productGrade.searchPlaceholder')}
            prefix={<SearchOutlined />}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            allowClear
            style={{ width: 260 }}
          />
          <Button type="primary" onClick={() => void loadList(1, pageSize)}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadList(page, pageSize)}>
            {t('common.refresh')}
          </Button>
          <Button
            type="dashed"
            icon={<FileExcelOutlined />}
            onClick={() => setPasteImportOpen(true)}
          >
            {t('productGrade.pasteImport')}
          </Button>
          <Button
            icon={<DollarOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={() => setBatchPriceOpen(true)}
          >
            {t('productGrade.batchPrice')}
          </Button>
          <Button
            icon={<DownloadOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={openExportModal}
          >
            {t('productGrade.exportExcel')}
          </Button>
          <Button
            icon={<ShoppingCartOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={openAddOrderModal}
          >
            {t('productGrade.addToStoreOrder')}
          </Button>
        </Space>

        {selectedRowKeys.length > 0 && (
          <Card size="small" style={{ marginBottom: 12, background: '#fafafa' }}>
            <Space>
              <span>{t('productGrade.selectedProducts', { count: selectedRowKeys.length })}</span>
              <Select
                placeholder={t('productGrade.selectTargetGrade')}
                value={batchGrade}
                onChange={setBatchGrade}
                style={{ width: 160 }}
                allowClear
              >
                {Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => (
                  <Select.Option key={key} value={key}>
                    <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 4 }}>
                      {key}
                    </Tag>
                    {t(`productGrade.${cfg.i18nKey}`)}
                  </Select.Option>
                ))}
              </Select>
              <Button type="primary" size="small" onClick={() => void handleBatchUpdate()}>
                {t('productGrade.batchModify')}
              </Button>
              <Button size="small" onClick={() => void openBatchCategoryModal()}>
                {t('productGrade.batchCategory')}
              </Button>
              <Button size="small" icon={<ShoppingCartOutlined />} onClick={openAddOrderModal}>
                {t('productGrade.addToStoreOrder')}
              </Button>
              <Button size="small" onClick={() => setSelectedRowKeys([])}>
                {t('productGrade.cancelSelection')}
              </Button>
            </Space>
          </Card>
        )}

        <Table<ProductGradeListItem>
          rowKey="productCode"
          virtual
          loading={loading}
          columns={columns}
          dataSource={data}
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys as string[]),
            columnWidth: 48,
            fixed: true,
            preserveSelectedRowKeys: true,
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: [20, 50, 100, 200, 500, 1000],
            showQuickJumper: true,
            showTotal: (total) => t('common.total', { count: total }),
          }}
          scroll={{ x: 900, y: 600 }}
          onChange={handleTableChange}
        />
      </Card>

      <PasteImportModal
        open={pasteImportOpen}
        onClose={() => setPasteImportOpen(false)}
        onSuccess={() => void loadList(page, pageSize)}
      />

      <BatchPriceModal
        open={batchPriceOpen}
        selectedCount={selectedRowKeys.length}
        productCodes={selectedRowKeys}
        onClose={() => setBatchPriceOpen(false)}
        onSuccess={() => {
          setSelectedRowKeys([])
          setBatchGrade(undefined)
        }}
      />

      <Modal
        title={t('productGrade.addToStoreOrderTitle')}
        open={addOrderOpen}
        onCancel={() => {
          if (!addOrderSubmitting) setAddOrderOpen(false)
        }}
        onOk={() => void handleAddToStoreOrder()}
        okText={t('productGrade.confirmAddToStoreOrder')}
        cancelText={t('common.cancel')}
        confirmLoading={addOrderSubmitting}
        maskClosable={!addOrderSubmitting}
      >
        <Space direction="vertical" style={{ width: '100%' }} size={12}>
          <span>{t('productGrade.addToStoreOrderSelected', { count: selectedRowKeys.length })}</span>
          <Radio.Group
            value={addOrderMode}
            onChange={(event) => setAddOrderMode(event.target.value)}
            disabled={addOrderSubmitting}
          >
            <Radio.Button value="existing">{t('productGrade.useExistingOrder')}</Radio.Button>
            <Radio.Button value="new">{t('productGrade.createNewOrder')}</Radio.Button>
          </Radio.Group>

          {addOrderMode === 'existing' ? (
            <Select
              showSearch
              allowClear
              placeholder={t('productGrade.selectTargetOrder')}
              value={targetOrderGuid}
              loading={orderLoading}
              disabled={addOrderSubmitting}
              optionFilterProp="label"
              filterOption={false}
              onSearch={(value) => {
                setOrderKeyword(value)
                void loadEditableOrders({ keyword: value, pageNumber: 1 })
              }}
              style={{ width: '100%' }}
              onDropdownVisibleChange={(open) => {
                if (open && !orderOptionsLoaded) {
                  void loadEditableOrders({ keyword: orderKeyword, pageNumber: 1 })
                }
              }}
              onPopupScroll={(event) => {
                const target = event.currentTarget
                const nearBottom = target.scrollTop + target.clientHeight >= target.scrollHeight - 24
                if (nearBottom && orderHasMore && editableOrders.length < orderTotal && !orderLoading) {
                  void loadEditableOrders({ keyword: orderKeyword, pageNumber: orderPage + 1, append: true })
                }
              }}
              onChange={setTargetOrderGuid}
              options={editableOrders.map((order) => {
                const status = EDITABLE_STORE_ORDER_STATUSES.includes(order.flowStatus)
                  ? order.flowStatus
                  : StoreOrderFlowStatus.Submitted
                return {
                  value: order.orderGUID,
                  label: `${order.orderNo || order.orderGUID} - ${order.storeName || order.storeCode || '--'} - ${t(getOrderStatusI18nKey(status))}`,
                }
              })}
            />
          ) : (
            <>
              <Select
                showSearch
                allowClear
                placeholder={t('productGrade.selectTargetStore')}
                value={targetStoreCode}
                loading={storeLoading}
                disabled={addOrderSubmitting}
                optionFilterProp="label"
                style={{ width: '100%' }}
                onDropdownVisibleChange={(open) => {
                  if (open) void loadStores()
                }}
                onChange={setTargetStoreCode}
                options={storeOptions}
              />
              <Input.TextArea
                placeholder={t('productGrade.newOrderRemarksPlaceholder')}
                value={newOrderRemarks}
                disabled={addOrderSubmitting}
                autoSize={{ minRows: 2, maxRows: 4 }}
                onChange={(event) => setNewOrderRemarks(event.target.value)}
              />
            </>
          )}

          <Space wrap>
            {EDITABLE_STORE_ORDER_STATUSES.map((status) => (
              <Tag key={status} color={StoreOrderStatusColorMap[status]}>
                {t(getOrderStatusI18nKey(status))}
              </Tag>
            ))}
          </Space>
        </Space>
      </Modal>

      <Modal
        title={t('productGrade.batchCategoryTitle')}
        open={batchCategoryOpen}
        onCancel={handleBatchCategoryCancel}
        onOk={() => void handleBatchCategorySave()}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={batchCategorySaving}
        maskClosable={!batchCategorySaving}
        width={640}
        destroyOnHidden
        okButtonProps={{
          disabled:
            categoryLoading
            || !targetCategoryGuid
            || !categoryTree.length,
        }}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            {t('productGrade.batchCategorySelected', { count: selectedRowKeys.length })}
          </Typography.Paragraph>
          {selectedTargetCategory ? (
            <Tag color="blue">{t('productGrade.targetCategory')}: {categoryEditTargetText}</Tag>
          ) : null}
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('productGrade.batchCategoryHint')}
          </Typography.Paragraph>
          <CategoryTreePicker
            categories={categoryTree}
            selectedKey={targetCategoryGuid}
            expandedKeys={categoryExpandedKeys}
            onExpand={setCategoryExpandedKeys}
            onSelect={setTargetCategoryGuid}
            language={i18n.language}
            t={t}
            maxHeight={420}
          />
        </Space>
      </Modal>

      <Modal
        title={t('productGrade.editCategoryTitle')}
        open={categoryEditOpen}
        onCancel={handleCategoryEditCancel}
        onOk={() => void handleCategoryEditSave()}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={categorySaving}
        maskClosable={!categorySaving}
        width={640}
        destroyOnHidden
        okButtonProps={{
          disabled:
            categoryLoading
            || !targetCategoryGuid
            || targetCategoryGuid === categoryEditRecord?.categoryGuid
            || !categoryTree.length,
        }}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            <Typography.Text strong>{categoryEditRecord?.hbProductNo || categoryEditRecord?.productCode || '--'}</Typography.Text>
            {categoryEditRecord?.productName ? ` - ${categoryEditRecord.productName}` : ''}
          </Typography.Paragraph>
          <Space wrap>
            <Tag>{t('productGrade.currentCategory')}: {categoryEditCurrentText}</Tag>
            {selectedTargetCategory ? (
              <Tag color="blue">{t('productGrade.targetCategory')}: {categoryEditTargetText}</Tag>
            ) : null}
          </Space>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('productGrade.editCategoryHint')}
          </Typography.Paragraph>
          <CategoryTreePicker
            categories={categoryTree}
            selectedKey={targetCategoryGuid}
            expandedKeys={categoryExpandedKeys}
            onExpand={setCategoryExpandedKeys}
            onSelect={setTargetCategoryGuid}
            language={i18n.language}
            t={t}
            maxHeight={420}
          />
        </Space>
      </Modal>

      <Modal
        title={t('productGrade.exportExcelTitle')}
        open={exportOpen}
        onCancel={() => {
          if (!exporting) setExportOpen(false)
        }}
        onOk={() => void handleExportExcel()}
        okText={t('productGrade.startExport')}
        cancelText={t('common.cancel')}
        confirmLoading={exporting}
        maskClosable={!exporting}
      >
        <Space direction="vertical" style={{ width: '100%' }} size={12}>
          <span>{t('productGrade.exportSelectedProducts', { count: selectedRowKeys.length })}</span>
          <Checkbox
            checked={exportIncludeImage}
            disabled={exporting}
            onChange={(event) => setExportIncludeImage(event.target.checked)}
          >
            {t('productGrade.includeProductImage')}
          </Checkbox>
          {exporting && (
            <div>
              <Progress percent={exportProgress} size="small" />
              <div style={{ color: '#666', marginTop: 4 }}>{exportMessage}</div>
            </div>
          )}
        </Space>
      </Modal>
    </PageContainer>
  )
}
