import {
  CloudDownloadOutlined,
  CloudSyncOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  FileImageOutlined,
  PlusOutlined,
  ReloadOutlined,
  SafetyCertificateOutlined,
  SearchOutlined,
  SettingOutlined,
  TranslationOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Cascader,
  Checkbox,
  Col,
  DatePicker,
  Descriptions,
  Divider,
  Form,
  Image,
  Input,
  InputNumber,
  message,
  Modal,
  Pagination,
  Popconfirm,
  Radio,
  Row,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
  Tree,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { FilterDropdownProps, FilterValue } from 'antd/es/table/interface'
import dayjs from 'dayjs'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import {
  batchCreateSetCodes,
  batchDelete as batchDeleteSetCodes,
  batchUpdateBarcodes as batchUpdateSetBarcodes,
  batchUpdatePrices as batchUpdateSetPrices,
  getGridData,
} from '../../../services/multiCodeSetService'
import {
  batchUpdateProductStoreRecords,
  batchUpdateProducts,
  buildProductHqSyncOperationId,
  buildSupplierImageBatchUpdateOperationId,
  createProductHqSyncJobPoller,
  createProductWithPrices,
  createProductFullHqSyncJob,
  createProductIncrementalHqSyncJob,
  createSupplierImageBatchUpdateJob,
  getSyncProductsToStoresJob,
  getSupplierImageBatchUpdateJob,
  getProductStoreRecords,
  getProductHqSyncJob,
  getProducts,
  HqProductSyncPollingTimeoutError,
  pushProductsToHq,
  startSyncProductsToStoresJob,
  syncSelectedProductsFromHq,
  updateProduct,
} from '../../../services/posProductService'
import { createHqSyncJobPoller } from '../../../services/productHqSyncPolling'
import {
  createProductCategory,
  deleteProductCategory,
  getProductCategoryTree,
  updateProductCategory,
} from '../../../services/productCategoryService'
import { checkIntegrity, fixIntegrity } from '../../../services/productIntegrityService'
import { getActiveStores } from '../../../services/storeService'
import { batchTranslate } from '../../../services/translationService'
import { useAuthStore } from '../../../store/auth'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { RequestError } from '../../../utils/request'
import {
  defaultPushProductsToHqUpdateFields,
  pushProductsToHqUpdateFieldOptions,
} from '../../../types/posProduct'
import type { BatchUpdatePosProductDto, BatchUpdateProductStoreRecordsChanges, BatchUpdateSupplierImagesJobResult, BatchUpdateSupplierImagesResult, HqProductSyncJobResult, HqProductSyncJobStatus, HqProductSyncResult, PosProductColumnFilters, PosProductDateFilterOperator, PosProductDto, PosProductFilterParams, PosProductNumberFilterOperator, PosProductTextFilterOperator, ProductStoreRecordDto, PushProductsToHqResult, PushProductsToHqUpdateField, SyncProductsToStoresField, SyncProductsToStoresJobResult, SyncProductsToStoresRequest, SyncProductsToStoresResult } from '../../../types/posProduct'
import type { ProductCategoryDto } from '../../../types/productCategory'
import type { ProductIntegrityCheckResultDto, ProductIntegrityFixResultDto } from '../../../types/productIntegrity'
import type { MulticodeSetItem } from '../../../types/multiCodeSet'
import type { StoreOption } from '../../../services/storeService'
import {
  compareProductStoreRecordsByActive,
  compareProductStoreRecordsByAutoPricing,
  compareProductStoreRecordsByDiscountRate,
  compareProductStoreRecordsByName,
  compareProductStoreRecordsByPurchasePrice,
  compareProductStoreRecordsByRetailPrice,
  compareProductStoreRecordsBySpecialProduct,
  compareProductStoreRecordsByStoreCode,
  compareProductStoreRecordsByStoreProductCode,
  compareProductStoreRecordsByUpdatedAt,
  compareProductStoreRecordsByUpdatedBy,
} from './storeRecordSorting'
import {
  clearActiveSupplierImageBatchJob,
  normalizeSupplierImageBatchJobKey,
  readActiveSupplierImageBatchJobs,
  saveActiveSupplierImageBatchJobs,
  type ActiveSupplierImageBatchJob,
  type ActiveSupplierImageBatchJobMap,
} from './activeSupplierImageBatchJobs'
import {
  buildSupplierImageUrl,
  getDefaultSupplierImageTemplate,
  validateSupplierImageTemplate,
  type SupplierImageMode,
} from './productImageTemplate'
import {
  buildSupplierImageBatchScopeRequest,
  getDefaultSupplierImageBatchScope,
  type SupplierImageBatchScope,
} from './productImageBatchScope'
import {
  buildProductIntegrityFixSummary,
  buildProductIntegritySummary,
  type ProductIntegrityIssueRow,
} from './productIntegrityReport'

type ProductRow = PosProductDto & { key: string }
type HqSyncMode = Parameters<typeof buildProductHqSyncOperationId>[0]
type SupplierOption = { label: string; value: string; localSupplierCode: string; name?: string; imageBaseUrl?: string }
type StoreRecordBatchEditFormValues = {
  updatePurchasePrice?: boolean
  purchasePrice?: number
  updateStoreRetailPriceValue?: boolean
  storeRetailPriceValue?: number
  updateDiscountRate?: boolean
  discountRate?: number
  updateIsAutoPricing?: boolean
  isAutoPricing?: boolean
  updateIsSpecialProduct?: boolean
  isSpecialProduct?: boolean
  updateIsActive?: boolean
  isActive?: boolean
}

type ActiveProductHqSyncJob = {
  jobId: string
  mode: HqSyncMode
  operationId: string
  createdAt: string
  status?: HqProductSyncJobStatus | string
  message?: string
  startDate?: string
}

const PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY = 'posAdmin.products.activeHqSyncJob'
const PRODUCT_HQ_SYNC_POLL_INTERVAL_MS = 2000
const PRODUCT_HQ_SYNC_TIMEOUT_MS = 10 * 60 * 1000
const SUPPLIER_IMAGE_BATCH_POLL_INTERVAL_MS = 2000
const SUPPLIER_IMAGE_BATCH_TIMEOUT_MS = 30 * 60 * 1000
const PRODUCT_IMAGE_FALLBACK = 'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDAiIGhlaWdodD0iNDAiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjQwIiBoZWlnaHQ9IjQwIiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjAiIHk9IjI0IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmb250LXNpemU9IjEwIiBmaWxsPSIjY2NjIj7ml6DnvKk8L3RleHQ+PC9zdmc+'
const DEFAULT_PRODUCT_IMAGE_BASE_URL = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200'
const CHINESE_TEXT_PATTERN = /[\u4e00-\u9fff]/
const PRODUCT_TEXT_FILTER_OPERATORS: PosProductTextFilterOperator[] = ['contains', 'equals', 'startsWith', 'endsWith']
const PRODUCT_NUMBER_FILTER_OPERATORS: PosProductNumberFilterOperator[] = ['equals', 'between', 'gte', 'lte']
const PRODUCT_DATE_FILTER_OPERATORS: PosProductDateFilterOperator[] = ['equals', 'between', 'gte', 'lte']

function readProductColumnFilterToken(value: unknown): Record<string, unknown> {
  if (typeof value !== 'string' || !value) return {}
  try {
    const parsed = JSON.parse(value)
    return typeof parsed === 'object' && parsed !== null ? parsed as Record<string, unknown> : {}
  } catch {
    return {}
  }
}

function normalizeProductTableFilters(filters: Record<string, FilterValue | null>): PosProductColumnFilters {
  return Object.entries(filters).reduce<PosProductColumnFilters>((next, [key, values]) => {
    const normalizedValues = (values ?? [])
      .map((value) => String(value).trim())
      .filter(hasProductColumnFilterValue)
    if (normalizedValues.length) {
      next[key] = normalizedValues
    }
    return next
  }, {})
}

function hasProductColumnFilterValue(value: string) {
  const trimmedValue = value.trim()
  if (!trimmedValue) return false
  const token = readProductColumnFilterToken(trimmedValue)
  const operator = typeof token.operator === 'string' ? token.operator : ''
  if (!operator) return true

  if (PRODUCT_TEXT_FILTER_OPERATORS.includes(operator as PosProductTextFilterOperator)) {
    return typeof token.value === 'string' && Boolean(token.value.trim())
  }
  if (PRODUCT_NUMBER_FILTER_OPERATORS.includes(operator as PosProductNumberFilterOperator)) {
    if (operator === 'between') {
      return Boolean(String(token.min ?? '').trim() || String(token.max ?? '').trim())
    }
    return Boolean(String(token.value ?? '').trim())
  }
  if (PRODUCT_DATE_FILTER_OPERATORS.includes(operator as PosProductDateFilterOperator)) {
    if (operator === 'between') {
      return Boolean(String(token.start ?? '').trim() || String(token.end ?? '').trim())
    }
    return Boolean(String(token.value ?? '').trim())
  }
  return true
}

function getProductFilterTokenValue(filters: PosProductColumnFilters, key: string) {
  return filters[key]?.[0]
}

function productFilterActive(filters: PosProductColumnFilters, key: string) {
  return Boolean(filters[key]?.length)
}

function buildProductTextFilterToken(operator: PosProductTextFilterOperator, value: string) {
  // 下拉编辑期间保留空值 token，避免先选操作符时被 AntD 清空；提交查询时再过滤空筛选。
  return JSON.stringify({ operator, value: value.trim() })
}

function buildProductNumberFilterToken(operator: PosProductNumberFilterOperator, values: { value?: number | string | null; min?: number | string | null; max?: number | string | null }) {
  const payload = operator === 'between'
    ? {
        operator,
        min: values.min === undefined || values.min === null ? '' : String(values.min).trim(),
        max: values.max === undefined || values.max === null ? '' : String(values.max).trim(),
      }
    : {
        operator,
        value: values.value === undefined || values.value === null ? '' : String(values.value).trim(),
      }
  return JSON.stringify(payload)
}

function buildProductDateFilterToken(operator: PosProductDateFilterOperator, values: { value?: string; start?: string; end?: string }) {
  const payload = operator === 'between'
    ? { operator, start: values.start || '', end: values.end || '' }
    : { operator, value: values.value || '' }
  return JSON.stringify(payload)
}

function containsChineseText(value?: string) {
  return Boolean(value && CHINESE_TEXT_PATTERN.test(value))
}

function buildProductNameTranslationUpdates(
  rows: ProductRow[],
  translations: Record<string, string>,
): BatchUpdatePosProductDto[] {
  return rows.reduce<BatchUpdatePosProductDto[]>((updates, row) => {
    const originalName = row.productName.trim()
    const translatedName = translations[originalName]?.trim()
    // 批量翻译会同时覆盖商品名称和英文名称，提交前过滤空结果、未变化结果和仍包含中文的结果。
    if (translatedName && translatedName !== originalName && !containsChineseText(translatedName)) {
      updates.push({
        productCode: row.productCode,
        productName: translatedName,
        englishName: translatedName,
      })
    }
    return updates
  }, [])
}

function readActiveProductHqSyncJob(): ActiveProductHqSyncJob | null {
  if (typeof window === 'undefined') return null
  try {
    const raw = window.localStorage.getItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<ActiveProductHqSyncJob>
    if (!parsed.jobId || !parsed.operationId || !parsed.mode || !parsed.createdAt) return null
    return parsed as ActiveProductHqSyncJob
  } catch {
    return null
  }
}

function saveActiveProductHqSyncJob(job: ActiveProductHqSyncJob | null) {
  if (typeof window === 'undefined') return
  if (!job) {
    window.localStorage.removeItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY)
    return
  }
  window.localStorage.setItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY, JSON.stringify(job))
}

function resolveCascaderLeafValue(value: unknown): string | undefined {
  if (Array.isArray(value)) {
    const leaf = value[value.length - 1]
    return leaf === undefined || leaf === null || leaf === '' ? undefined : String(leaf)
  }
  return typeof value === 'string' && value ? value : undefined
}

function buildDefaultProductImageUrl(itemNumber: unknown): string {
  const normalizedItemNumber = String(itemNumber ?? '').trim()
  return normalizedItemNumber
    ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(normalizedItemNumber)}.jpg`
    : ''
}

function normalizeProductType(productType: unknown): 0 | 1 | 2 {
  return productType === 1 || productType === 2 ? productType : 0
}

function getProductTypeColor(productType: unknown): string {
  const normalizedType = normalizeProductType(productType)
  if (normalizedType === 1) return 'blue'
  if (normalizedType === 2) return 'purple'
  return 'default'
}

function isBarcodeManagedProduct(productType: unknown): boolean {
  const normalizedType = normalizeProductType(productType)
  return normalizedType === 1 || normalizedType === 2
}

function collectCategoryGuids(nodes: ProductCategoryDto[] | undefined): string[] {
  return (nodes ?? []).flatMap((node) => [
    node.guid,
    ...collectCategoryGuids(node.children),
  ])
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function normalizePushToHqPayload(raw: unknown, fallbackMessage?: string): PushProductsToHqResult | null {
  if (!isPlainRecord(raw)) return null
  const errors = Array.isArray(raw.errors)
    ? raw.errors.filter((item): item is string => typeof item === 'string')
    : []
  const affectedRowCount =
    Number(raw.affectedRowCount ?? 0) ||
    Number(raw.productsAdded ?? 0) +
      Number(raw.productsUpdated ?? 0) +
      Number(raw.storeRetailPricesCreated ?? 0) +
      Number(raw.storeRetailPricesUpdated ?? 0) +
      Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0) +
      Number(raw.productSetCodesUpdated ?? 0) +
      Number(raw.storeMultiCodesCreated ?? 0) +
      Number(raw.storeMultiCodesUpdated ?? 0)

  return {
    ...(raw as Partial<PushProductsToHqResult>),
    successCount: Number(raw.successCount ?? raw.productsAdded ?? 0) +
      Number(raw.successCount === undefined ? raw.productsUpdated ?? 0 : 0),
    failedCount: Number(raw.failedCount ?? raw.errorCount ?? errors.length),
    totalCount: Number(raw.totalCount ?? 0),
    affectedRowCount,
    errors,
    message: typeof raw.message === 'string' ? raw.message : fallbackMessage,
  }
}

function extractPushToHqErrorResult(error: unknown): PushProductsToHqResult | null {
  if (!isPlainRecord(error) || !('payload' in error)) return null
  const payload = error.payload
  if (!isPlainRecord(payload)) return null
  const fallbackMessage = typeof payload.message === 'string'
    ? payload.message
    : error instanceof Error
      ? error.message
      : undefined
  return (
    normalizePushToHqPayload(payload.data, fallbackMessage) ??
    normalizePushToHqPayload(payload.details, fallbackMessage)
  )
}

const SORT_FIELD_MAP: Record<string, string> = {
  productCode: 'productcode',
  itemNumber: 'itemnumber',
  barcode: 'barcode',
  productName: 'productname',
  localSupplierCode: 'localsuppliercode',
  categoryGuid: 'productcategoryguid',
  categoryName: 'productcategoryguid',
  purchasePrice: 'purchaseprice',
  retailPrice: 'retailprice',
  isAutoPricing: 'isautopricing',
  isActive: 'isactive',
  productType: 'producttype',
  storeRecordCount: 'storerecordcount',
  createdAt: 'createdat',
  updatedAt: 'updatedat',
}

export default function ProductManagementPage() {
  const { t } = useTranslation()
  const isAdmin = useAuthStore((state) => state.access.isAdmin)
  const canManagePosProducts = useAuthStore((state) => state.access.canManagePosProducts)
  const canManageStoreProducts = useAuthStore((state) => state.access.canManageStoreProducts)
  const canEditStoreProducts = useAuthStore((state) => state.access.canEditStoreProducts)
  const canCreateStoreProducts = useAuthStore((state) => state.access.canCreateStoreProducts)

  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<ProductRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [keyword, setKeyword] = useState('')
  const [keywordInput, setKeywordInput] = useState('')
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [supplierCodeInput, setSupplierCodeInput] = useState<string | undefined>(undefined)
  const [categoryGuid, setCategoryGuid] = useState<string | undefined>(undefined)
  const [categoryGuidInput, setCategoryGuidInput] = useState<string | undefined>(undefined)
  const [isActiveFilter, setIsActiveFilter] = useState<boolean | undefined>(undefined)
  const [isActiveFilterInput, setIsActiveFilterInput] = useState<boolean | undefined>(undefined)
  const [isSetFilter, setIsSetFilter] = useState<boolean | undefined>(undefined)
  const [isSetFilterInput, setIsSetFilterInput] = useState<boolean | undefined>(undefined)
  const [storeRecordCountMode, setStoreRecordCountMode] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')
  const [storeRecordCountModeInput, setStoreRecordCountModeInput] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')
  const [storeRecordCountMin, setStoreRecordCountMin] = useState<number | undefined>(undefined)
  const [storeRecordCountMax, setStoreRecordCountMax] = useState<number | undefined>(undefined)
  const [storeRecordCountMinInput, setStoreRecordCountMinInput] = useState<number | undefined>(undefined)
  const [storeRecordCountMaxInput, setStoreRecordCountMaxInput] = useState<number | undefined>(undefined)
  const [columnFilters, setColumnFilters] = useState<PosProductColumnFilters>({})
  const [queryVersion, setQueryVersion] = useState(0)
  const [sortBy, setSortBy] = useState('productCode')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('ascend')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [hqSyncSubmitting, setHqSyncSubmitting] = useState(false)
  const hqSyncSubmittingRef = useRef(false)
  const stopHqSyncPollingRef = useRef<(() => void) | null>(null)
  const stopSyncToStorePollingRef = useRef<(() => void) | null>(null)
  const stopSupplierImageBatchPollingRef = useRef<Record<string, () => void>>({})
  const isMountedRef = useRef(true)
  const [activeHqSyncJob, setActiveHqSyncJob] = useState<ActiveProductHqSyncJob | null>(() => readActiveProductHqSyncJob())
  const [activeImageBatchJobs, setActiveImageBatchJobs] = useState<ActiveSupplierImageBatchJobMap>(() => readActiveSupplierImageBatchJobs())
  const [hqSyncMode, setHqSyncMode] = useState<HqSyncMode>('incremental')
  const [hqSyncVisible, setHqSyncVisible] = useState(false)
  const [hqSyncForm] = Form.useForm()

  const [supplierOptions, setSupplierOptions] = useState<SupplierOption[]>([])
  const [categoryTree, setCategoryTree] = useState<ProductCategoryDto[]>([])
  const [categoryLoadFailed, setCategoryLoadFailed] = useState(false)
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])

  const [editVisible, setEditVisible] = useState(false)
  const [editingProduct, setEditingProduct] = useState<PosProductDto | null>(null)
  const [editForm] = Form.useForm()
  const [createVisible, setCreateVisible] = useState(false)
  const [createSubmitting, setCreateSubmitting] = useState(false)
  const [createForm] = Form.useForm()

  const [batchEditVisible, setBatchEditVisible] = useState(false)
  const [batchEditForm] = Form.useForm()
  const [translating, setTranslating] = useState(false)
  const [imageBatchVisible, setImageBatchVisible] = useState(false)
  const [imageBatchLoading, setImageBatchLoading] = useState(false)
  const [imageBatchForm] = Form.useForm()

  const [syncToStoreVisible, setSyncToStoreVisible] = useState(false)
  const [syncToStoreForm] = Form.useForm()
  const [syncToStoreLoading, setSyncToStoreLoading] = useState(false)
  const [syncSelectAll, setSyncSelectAll] = useState(false)
  const [pushToHqLoading, setPushToHqLoading] = useState(false)
  const pushToHqLoadingRef = useRef(false)
  const [selectedFromHqLoading, setSelectedFromHqLoading] = useState(false)
  const selectedFromHqLoadingRef = useRef(false)

  const productTypeWatch = Form.useWatch('productType', editForm)
  const imageBatchSupplierCode = Form.useWatch('localSupplierCode', imageBatchForm)
  const imageBatchTemplate = Form.useWatch('urlTemplate', imageBatchForm)
  const [editSetCodes, setEditSetCodes] = useState<any[]>([])
  const [editSetCodesLoading, setEditSetCodesLoading] = useState(false)
  const [editSetPriceEdits, setEditSetPriceEdits] = useState<Record<string, { setItemNumber?: string; setBarcode?: string; setPurchasePrice?: number; setRetailPrice?: number }>>({})
  const [editPendingDeletes, setEditPendingDeletes] = useState<Record<string, any>>({})
  const editSetCodesRequestSeqRef = useRef(0)

  const [setCodeVisible, setSetCodeVisible] = useState(false)
  const [setCodeProduct, setSetCodeProduct] = useState<PosProductDto | null>(null)
  const [setCodeData, setSetCodeData] = useState<MulticodeSetItem[]>([])
  const [setCodeLoading, setSetCodeLoading] = useState(false)
  const [setCodeEditingKey, setSetCodeEditingKey] = useState<string | null>(null)

  const [storeRecordsVisible, setStoreRecordsVisible] = useState(false)
  const [storeRecordsProduct, setStoreRecordsProduct] = useState<PosProductDto | null>(null)
  const [storeRecordsData, setStoreRecordsData] = useState<ProductStoreRecordDto[]>([])
  const [storeRecordsLoading, setStoreRecordsLoading] = useState(false)
  const [storeRecordSelectedRowKeys, setStoreRecordSelectedRowKeys] = useState<React.Key[]>([])
  const [storeRecordBatchEditVisible, setStoreRecordBatchEditVisible] = useState(false)
  const [storeRecordBatchUpdating, setStoreRecordBatchUpdating] = useState(false)
  const [storeRecordBatchEditForm] = Form.useForm()
  const storeRecordsRequestSeqRef = useRef(0)

  const [categoryModalVisible, setCategoryModalVisible] = useState(false)
  const [categoryEditForm] = Form.useForm()
  const [editingCategory, setEditingCategory] = useState<ProductCategoryDto | null>(null)

  const [integrityVisible, setIntegrityVisible] = useState(false)
  const [integrityLoading, setIntegrityLoading] = useState(false)
  const [integrityResult, setIntegrityResult] = useState<ProductIntegrityCheckResultDto | null>(null)
  const [fixLoading, setFixLoading] = useState(false)
  const integritySummary = useMemo(
    () => buildProductIntegritySummary(integrityResult),
    [integrityResult],
  )

  // 供应商列表接口有时只返回编码，使用筛选下拉中的供应商资料补齐名称。
  const supplierNameMap = useMemo(
    () => new Map(supplierOptions.map((option) => [option.value, option.name || option.label])),
    [supplierOptions],
  )
  const imageBatchSupplier = useMemo(
    () => supplierOptions.find((option) => option.value === imageBatchSupplierCode),
    [imageBatchSupplierCode, supplierOptions],
  )
  const imageBatchPreviewUrl = useMemo(() => {
    if (!imageBatchTemplate || !imageBatchSupplierCode) return ''
    return buildSupplierImageUrl(imageBatchTemplate, {
      localSupplierCode: imageBatchSupplierCode,
      itemNumber: 'HB313-129',
    })
  }, [imageBatchSupplierCode, imageBatchTemplate])
  const isStoreRecordCountCustomMode = storeRecordCountModeInput === 'custom'
  const hasAppliedStoreRecordCountFilter = storeRecordCountMode !== 'all'
  const supplierColumnFilterOptions = useMemo(
    () => supplierOptions.map((option) => ({
      text: option.label,
      value: option.value,
    })),
    [supplierOptions],
  )
  const categoryColumnFilterOptions = useMemo(() => {
    const flatten = (nodes: ProductCategoryDto[]): Array<{ text: string; value: string }> => nodes.flatMap((node) => [
      { text: node.name, value: node.guid },
      ...flatten(node.children ?? []),
    ])
    return flatten(categoryTree)
  }, [categoryTree])
  const getProductTypeLabel = useCallback((productType: unknown) => {
    const normalizedType = normalizeProductType(productType)
    if (normalizedType === 1) return t('posAdmin.products.setProduct', '套装')
    if (normalizedType === 2) return t('posAdmin.products.multiCodeProductShort', '多码')
    return t('posAdmin.products.normalProduct', '普通')
  }, [t])
  const productTypeColumnFilterOptions = useMemo(() => [
    { text: getProductTypeLabel(0), value: '0' },
    { text: getProductTypeLabel(1), value: '1' },
    { text: getProductTypeLabel(2), value: '2' },
  ], [getProductTypeLabel])
  const booleanColumnFilterOptions = useMemo(() => [
    { text: t('common.yes', '是'), value: 'true' },
    { text: t('common.no', '否'), value: 'false' },
  ], [t])
  const activeColumnFilterOptions = useMemo(() => [
    { text: t('posAdmin.products.enable', '启用'), value: 'true' },
    { text: t('posAdmin.products.disable', '禁用'), value: 'false' },
  ], [t])

  const productColumnFilterIcon = (filtered?: boolean) => (
    <SearchOutlined style={{ color: filtered ? '#1677ff' : undefined }} />
  )
  const textOperatorOptions = PRODUCT_TEXT_FILTER_OPERATORS.map((operator) => ({
    value: operator,
    label: {
      contains: t('posAdmin.products.columnFilterContains', '包含'),
      equals: t('posAdmin.products.columnFilterEquals', '等于'),
      startsWith: t('posAdmin.products.columnFilterStartsWith', '开头是'),
      endsWith: t('posAdmin.products.columnFilterEndsWith', '结尾是'),
    }[operator],
  }))
  const numberOperatorOptions = PRODUCT_NUMBER_FILTER_OPERATORS.map((operator) => ({
    value: operator,
    label: {
      equals: t('posAdmin.products.columnFilterEquals', '等于'),
      between: t('posAdmin.products.columnFilterBetween', '范围'),
      gte: t('posAdmin.products.columnFilterGreaterThanOrEqual', '大于等于'),
      lte: t('posAdmin.products.columnFilterLessThanOrEqual', '小于等于'),
    }[operator],
  }))
  const dateOperatorOptions = PRODUCT_DATE_FILTER_OPERATORS.map((operator) => ({
    value: operator,
    label: {
      equals: t('posAdmin.products.columnFilterDateEquals', '等于某日'),
      between: t('posAdmin.products.columnFilterBetween', '范围'),
      gte: t('posAdmin.products.columnFilterDateGreaterThanOrEqual', '晚于/等于'),
      lte: t('posAdmin.products.columnFilterDateLessThanOrEqual', '早于/等于'),
    }[operator],
  }))

  const renderColumnFilterPanel = (
    title: string,
    content: ReactNode,
    actions: { confirm: () => void; clear: () => void },
    width: 280 | 320 = 280,
  ) => (
    <div
      className={`pos-products-column-filter-panel${width === 320 ? ' pos-products-column-filter-panel-wide' : ''}`}
      onKeyDown={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <div className="pos-products-column-filter-title">{title}</div>
      <div className="pos-products-column-filter-body">{content}</div>
      <div className="pos-products-column-filter-actions">
        <Button size="small" onClick={actions.clear}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
        <Button size="small" type="primary" onClick={actions.confirm}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
      </div>
    </div>
  )

  const buildTextFilterDropdown = (filterKey: string, placeholder: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
    const token = readProductColumnFilterToken(String(selectedKeys[0] ?? getProductFilterTokenValue(columnFilters, filterKey) ?? ''))
    const operator = PRODUCT_TEXT_FILTER_OPERATORS.includes(token.operator as PosProductTextFilterOperator)
      ? token.operator as PosProductTextFilterOperator
      : 'contains'
    const value = typeof token.value === 'string' ? token.value : ''
    const updateToken = (nextOperator: PosProductTextFilterOperator, nextValue: string) => {
      const nextToken = buildProductTextFilterToken(nextOperator, nextValue)
      setSelectedKeys(nextToken ? [nextToken] : [])
    }

    return renderColumnFilterPanel(
      t('posAdmin.products.columnFilterTitle', '{{name}}筛选', { name: placeholder }),
      <>
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterMatchMode', '匹配方式')}</label>
        <Select size="small" value={operator} options={textOperatorOptions} onChange={(nextOperator) => updateToken(nextOperator, value)} />
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterValue', '筛选值')}</label>
        <Input
          size="small"
          allowClear
          placeholder={placeholder}
          value={value}
          onChange={(event) => updateToken(operator, event.target.value)}
          onPressEnter={() => confirm()}
        />
      </>,
      {
        confirm,
        clear: () => {
          setSelectedKeys([])
          clearFilters?.()
          confirm()
        },
      },
    )
  }

  const buildNumberFilterDropdown = (filterKey: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
    const token = readProductColumnFilterToken(String(selectedKeys[0] ?? getProductFilterTokenValue(columnFilters, filterKey) ?? ''))
    const operator = PRODUCT_NUMBER_FILTER_OPERATORS.includes(token.operator as PosProductNumberFilterOperator)
      ? token.operator as PosProductNumberFilterOperator
      : 'between'
    const value = token.value !== undefined && token.value !== null && String(token.value) ? Number(token.value) : undefined
    const min = token.min !== undefined && token.min !== null && String(token.min) ? Number(token.min) : undefined
    const max = token.max !== undefined && token.max !== null && String(token.max) ? Number(token.max) : undefined
    const updateToken = (nextOperator: PosProductNumberFilterOperator, nextValues: { value?: number | string | null; min?: number | string | null; max?: number | string | null }) => {
      const nextToken = buildProductNumberFilterToken(nextOperator, nextValues)
      setSelectedKeys(nextToken ? [nextToken] : [])
    }

    return renderColumnFilterPanel(
      t('posAdmin.products.columnFilterNumberTitle', '数值筛选'),
      <>
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterCondition', '条件')}</label>
        <Select size="small" value={operator} options={numberOperatorOptions} onChange={(nextOperator) => updateToken(nextOperator, { value, min, max })} />
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterNumericValue', '数值')}</label>
        {operator === 'between' ? (
          <Space.Compact className="pos-products-column-filter-range">
            <InputNumber size="small" controls={false} value={min} placeholder={t('containers.placeholders.minValue', '最小值')} onChange={(nextMin) => updateToken(operator, { min: nextMin, max })} />
            <InputNumber size="small" controls={false} value={max} placeholder={t('containers.placeholders.maxValue', '最大值')} onChange={(nextMax) => updateToken(operator, { min, max: nextMax })} />
          </Space.Compact>
        ) : (
          <InputNumber size="small" controls={false} value={value} placeholder={t('posAdmin.products.columnFilterValue', '筛选值')} onChange={(nextValue) => updateToken(operator, { value: nextValue })} />
        )}
      </>,
      {
        confirm,
        clear: () => {
          setSelectedKeys([])
          clearFilters?.()
          confirm()
        },
      },
    )
  }

  const buildDateFilterDropdown = (filterKey: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
    const token = readProductColumnFilterToken(String(selectedKeys[0] ?? getProductFilterTokenValue(columnFilters, filterKey) ?? ''))
    const operator = PRODUCT_DATE_FILTER_OPERATORS.includes(token.operator as PosProductDateFilterOperator)
      ? token.operator as PosProductDateFilterOperator
      : 'between'
    const value = typeof token.value === 'string' ? token.value : ''
    const start = typeof token.start === 'string' ? token.start : ''
    const end = typeof token.end === 'string' ? token.end : ''
    const updateToken = (nextOperator: PosProductDateFilterOperator, nextValues: { value?: string; start?: string; end?: string }) => {
      const nextToken = buildProductDateFilterToken(nextOperator, nextValues)
      setSelectedKeys(nextToken ? [nextToken] : [])
    }

    return renderColumnFilterPanel(
      t('posAdmin.products.columnFilterDateTitle', '日期筛选'),
      <>
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterCondition', '条件')}</label>
        <Select size="small" value={operator} options={dateOperatorOptions} onChange={(nextOperator) => updateToken(nextOperator, { value, start, end })} />
        <label className="pos-products-column-filter-label">{t('posAdmin.products.columnFilterDate', '日期')}</label>
        {operator === 'between' ? (
          <DatePicker.RangePicker
            size="small"
            value={start || end ? [start ? dayjs(start) : null, end ? dayjs(end) : null] : null}
            onChange={(dates) => updateToken(operator, {
              start: dates?.[0]?.format('YYYY-MM-DD') ?? '',
              end: dates?.[1]?.format('YYYY-MM-DD') ?? '',
            })}
          />
        ) : (
          <DatePicker
            size="small"
            value={value ? dayjs(value) : null}
            onChange={(date) => updateToken(operator, { value: date?.format('YYYY-MM-DD') ?? '' })}
          />
        )}
      </>,
      {
        confirm,
        clear: () => {
          setSelectedKeys([])
          clearFilters?.()
          confirm()
        },
      },
      320,
    )
  }

  const textFilterProps = (filterKey: string, placeholder: string) => ({
    filterDropdown: buildTextFilterDropdown(filterKey, placeholder),
    filterIcon: productColumnFilterIcon,
    filtered: productFilterActive(columnFilters, filterKey),
    filteredValue: columnFilters[filterKey] ?? null,
  })
  const numberFilterProps = (filterKey: string) => ({
    filterDropdown: buildNumberFilterDropdown(filterKey),
    filterIcon: productColumnFilterIcon,
    filtered: productFilterActive(columnFilters, filterKey),
    filteredValue: columnFilters[filterKey] ?? null,
  })
  const dateFilterProps = (filterKey: string) => ({
    filterDropdown: buildDateFilterDropdown(filterKey),
    filterIcon: productColumnFilterIcon,
    filtered: productFilterActive(columnFilters, filterKey),
    filteredValue: columnFilters[filterKey] ?? null,
  })
  const enumFilterProps = (filterKey: string, options: Array<{ text: string; value: string }>) => ({
    filters: options,
    filterIcon: productColumnFilterIcon,
    filtered: productFilterActive(columnFilters, filterKey),
    filteredValue: columnFilters[filterKey] ?? null,
    filterMultiple: true,
  })

  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)
  const storeRecordSelectedRows = useMemo(
    () => storeRecordsData.filter((record) => storeRecordSelectedRowKeys.includes(`${record.storeCode || ''}-${record.storeProductCode || ''}`)),
    [storeRecordsData, storeRecordSelectedRowKeys],
  )
  const editingProductCode = editingProduct?.productCode

  const resetEditSetCodeState = useCallback(() => {
    // 编辑弹窗会复用组件实例，打开新商品前必须清空条码明细，避免上一个商品残留。
    setEditSetCodes([])
    setEditSetPriceEdits({})
    setEditPendingDeletes({})
  }, [])

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const params: PosProductFilterParams = {
        pageIndex: page,
        pageSize,
        keyword: keyword || undefined,
        supplierCode: supplierCode || undefined,
        categoryGuid: categoryGuid || undefined,
        isActive: isActiveFilter,
        isSet: isSetFilter,
        storeRecordCountMin: storeRecordCountMin,
        storeRecordCountMax: storeRecordCountMax,
        sortBy: SORT_FIELD_MAP[sortBy] || sortBy,
        sortOrder,
        columnFilters,
      }
      const result = await getProducts(params)
      const items = (result?.items ?? []).map((it) => ({ ...it, key: it.productCode }))
      setData(items)
      setTotal(result?.total ?? 0)
    } catch {
      message.error(t('posAdmin.products.loadFailed', '加载商品列表失败'))
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, keyword, supplierCode, categoryGuid, isActiveFilter, isSetFilter, storeRecordCountMin, storeRecordCountMax, sortBy, sortOrder, columnFilters, queryVersion])

  const stopHqSyncJobPolling = useCallback(() => {
    stopHqSyncPollingRef.current?.()
    stopHqSyncPollingRef.current = null
  }, [])

  const stopSyncToStorePolling = useCallback(() => {
    stopSyncToStorePollingRef.current?.()
    stopSyncToStorePollingRef.current = null
  }, [])

  const saveActiveHqSyncJob = useCallback((job: ActiveProductHqSyncJob) => {
    setActiveHqSyncJob(job)
    saveActiveProductHqSyncJob(job)
  }, [])

  const clearActiveHqSyncJob = useCallback(() => {
    setActiveHqSyncJob(null)
    saveActiveProductHqSyncJob(null)
  }, [])

  const stopSupplierImageBatchPolling = useCallback((localSupplierCode?: string) => {
    const jobKey = normalizeSupplierImageBatchJobKey(localSupplierCode)
    if (jobKey) {
      stopSupplierImageBatchPollingRef.current[jobKey]?.()
      delete stopSupplierImageBatchPollingRef.current[jobKey]
      return
    }
    Object.values(stopSupplierImageBatchPollingRef.current).forEach((stop) => stop())
    stopSupplierImageBatchPollingRef.current = {}
  }, [])

  const saveActiveImageBatchJob = useCallback((job: ActiveSupplierImageBatchJob) => {
    const jobKey = normalizeSupplierImageBatchJobKey(job.localSupplierCode)
    if (!jobKey) return
    setActiveImageBatchJobs((prev) => {
      const next = { ...prev, [jobKey]: job }
      saveActiveSupplierImageBatchJobs(next)
      return next
    })
  }, [])

  const clearActiveImageBatchJob = useCallback((localSupplierCode: string) => {
    const jobKey = normalizeSupplierImageBatchJobKey(localSupplierCode)
    if (!jobKey) return
    setActiveImageBatchJobs((prev) => {
      if (!prev[jobKey]) return prev
      const next = clearActiveSupplierImageBatchJob(prev, localSupplierCode)
      saveActiveSupplierImageBatchJobs(next)
      return next
    })
  }, [])

  const getActiveImageBatchJobBySupplier = useCallback((localSupplierCode: string | undefined) => {
    const jobKey = normalizeSupplierImageBatchJobKey(localSupplierCode)
    if (!jobKey) return null
    return activeImageBatchJobs[jobKey] ?? readActiveSupplierImageBatchJobs()[jobKey] ?? null
  }, [activeImageBatchJobs])

  const buildHqSyncResultLines = useCallback((result: HqProductSyncResult) => {
    const lines = [
      t('posAdmin.products.hqSyncResult', '同步完成：新增 {{added}}，更新 {{updated}}，软删 {{deleted}}', {
        added: result.productsAdded ?? 0,
        updated: result.productsUpdated ?? 0,
        deleted: result.productsDeleted ?? 0,
      }),
    ]

    const relationStats = [
      { label: t('posAdmin.products.storeRetailPricesCreated', '门店零售价新增'), value: result.storeRetailPricesCreated ?? 0 },
      { label: t('posAdmin.products.storeRetailPricesDeleted', '门店零售价删除'), value: result.storeRetailPricesDeleted ?? 0 },
      { label: t('posAdmin.products.productSetCodesCreated', '套装编码新增'), value: result.productSetCodesCreated ?? 0 },
      { label: t('posAdmin.products.productSetCodesUpdated', '套装编码更新'), value: result.productSetCodesUpdated ?? 0 },
      { label: t('posAdmin.products.productSetCodesDeleted', '套装编码删除'), value: result.productSetCodesDeleted ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesCreated', '门店多码新增'), value: result.storeMultiCodesCreated ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesDeleted', '门店多码删除'), value: result.storeMultiCodesDeleted ?? 0 },
    ].filter((item) => item.value > 0)

    relationStats.forEach((item) => {
      lines.push(`${item.label}: ${item.value}`)
    })

    return lines
  }, [t])

  const showHqSyncJobResult = useCallback((result: HqProductSyncJobResult) => {
    const displayResult: HqProductSyncResult & Pick<Partial<HqProductSyncJobResult>, 'status' | 'message'> = {
      ...(result.result ?? result),
      status: result.status,
      message: result.message ?? result.result?.message,
      errors: result.errors?.length ? result.errors : result.result?.errors,
    }
    const content = (
      <Space direction="vertical" size={6}>
        {displayResult.message && <div>{displayResult.message}</div>}
        {buildHqSyncResultLines(displayResult).map((line) => (
          <div key={line}>{line}</div>
        ))}
        {displayResult.errors?.length ? (
          <div>
            {t('posAdmin.products.partialSyncError', '部分同步错误')}：{displayResult.errors.join('\n')}
          </div>
        ) : null}
      </Space>
    )

    if (result.status === 'Failed') {
      Modal.error({
        title: t('posAdmin.products.hqSyncJobFailed', '商品 HQ 同步失败'),
        content,
      })
      return
    }

    if (displayResult.errors?.length) {
      Modal.warning({
        title: t('posAdmin.products.hqSyncJobPartialSucceeded', '商品 HQ 同步部分成功'),
        content,
      })
      void loadData()
      return
    }

    Modal.success({
      title: t('posAdmin.products.hqSyncJobSucceeded', '商品 HQ 同步完成'),
      content,
    })
    void loadData()
  }, [buildHqSyncResultLines, loadData, t])

  const refreshSupplierOptions = useCallback(async () => {
    try {
      const suppliers = await getActiveLocalSuppliers()
      setSupplierOptions(suppliers.map((s) => ({
        label: `${s.name} (${s.localSupplierCode})`,
        value: s.localSupplierCode,
        localSupplierCode: s.localSupplierCode,
        name: s.name,
        imageBaseUrl: s.imageBaseUrl,
      })))
    } catch { /* ignore */ }
  }, [])

  const showSupplierImageBatchResult = useCallback((job: BatchUpdateSupplierImagesJobResult) => {
    const result: BatchUpdateSupplierImagesResult = job.result ?? {
        totalCount: 0,
        hbwebUpdatedCount: 0,
        hqUpdatedCount: 0,
        hbwebSkippedExistingImageCount: 0,
        hqSkippedExistingImageCount: 0,
        skippedCount: 0,
        hqFailedCount: 0,
        errors: job.errors ?? [],
      message: job.message || job.errorMessage,
    }
    const errors = result.errors ?? job.errors ?? []
    const content = (
      <Space direction="vertical" size={6}>
        {(job.message || result.message || job.errorMessage) ? <div>{job.message || result.message || job.errorMessage}</div> : null}
        <div>{t('posAdmin.products.batchImageTotal', '供应商商品: {{count}} 个', { count: result.totalCount ?? 0 })}</div>
        <div>{t('posAdmin.products.batchImageHbwebUpdated', 'Hbweb 更新: {{count}} 个', { count: result.hbwebUpdatedCount ?? 0 })}</div>
        <div>{t('posAdmin.products.batchImageHqUpdated', 'HQ 更新: {{count}} 个', { count: result.hqUpdatedCount ?? 0 })}</div>
        <div>{t('posAdmin.products.batchImageHbwebSkippedExistingImage', 'Hbweb 已有图片跳过: {{count}} 个', { count: result.hbwebSkippedExistingImageCount ?? 0 })}</div>
        <div>{t('posAdmin.products.batchImageHqSkippedExistingImage', 'HQ 已有图片跳过: {{count}} 个', { count: result.hqSkippedExistingImageCount ?? 0 })}</div>
        <div>{t('posAdmin.products.batchImageSkipped', '跳过: {{count}} 个', { count: result.skippedCount ?? 0 })}</div>
        {result.hqFailedCount ? <div>{t('posAdmin.products.batchImageHqFailed', 'HQ 缺失或失败: {{count}} 个', { count: result.hqFailedCount })}</div> : null}
        {errors.length ? <div style={{ whiteSpace: 'pre-wrap' }}>{errors.join('\n')}</div> : null}
      </Space>
    )

    if (job.status === 'Failed') {
      Modal.error({
        title: t('posAdmin.products.batchImageUpdateFailed', '图片批量修改失败'),
        content,
      })
      return
    }

    if (errors.length || (result.hqFailedCount ?? 0) > 0) {
      Modal.warning({
        title: t('posAdmin.products.batchImageUpdatePartialSucceeded', '图片批量修改部分完成'),
        content,
      })
      void refreshSupplierOptions()
      void loadData()
      return
    }

    Modal.success({
      title: t('posAdmin.products.batchImageUpdateComplete', '图片批量修改完成'),
      content,
    })
    void refreshSupplierOptions()
    void loadData()
  }, [loadData, refreshSupplierOptions, t])

  const startHqSyncJobPolling = useCallback((job: ActiveProductHqSyncJob) => {
    stopHqSyncJobPolling()

    const showPollingTimeout = () => {
      clearActiveHqSyncJob()
      Modal.warning({
        title: t('posAdmin.products.hqSyncJobTimeoutTitle', '商品 HQ 同步仍在后台执行'),
        content: t('posAdmin.products.hqSyncJobTimeout', '前端已停止轮询该同步任务。你可以稍后刷新列表，或重新提交同一同步范围以接管后端已有任务。'),
      })
    }

    saveActiveHqSyncJob(job)
    const poller = createProductHqSyncJobPoller({
      jobId: job.jobId,
      getJob: async (jobId) => {
        try {
          const result = await getProductHqSyncJob(jobId)
          saveActiveHqSyncJob({
            ...job,
            status: result.status,
            message: result.message,
          })
          return result
        } catch (error) {
          const errorMessage = error instanceof Error ? error.message : ''
          if (errorMessage.includes('未知同步任务状态') || errorMessage.includes('未知商品同步任务状态')) {
            throw error
          }
          if (isMountedRef.current) {
            message.warning(error instanceof Error ? error.message : t('posAdmin.products.hqSyncJobPollingFailed', '同步任务状态获取失败，将继续在后台重试'))
          }
          return {
            jobId,
            status: 'Running',
            message: errorMessage,
          }
        }
      },
      pollIntervalMs: PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
      timeoutMs: PRODUCT_HQ_SYNC_TIMEOUT_MS,
    })
    stopHqSyncPollingRef.current = poller.stop

    void poller.promise
      .then((result) => {
        if (!isMountedRef.current) {
          return
        }
        clearActiveHqSyncJob()
        showHqSyncJobResult(result)
      })
      .catch((error) => {
        if (!isMountedRef.current) {
          return
        }

        const errorMessage = error instanceof Error ? error.message : ''
        if (error instanceof HqProductSyncPollingTimeoutError) {
          showPollingTimeout()
          return
        }
        if (errorMessage === '商品同步任务轮询已取消') {
          return
        }
        if (errorMessage.includes('未知同步任务状态') || errorMessage.includes('未知商品同步任务状态')) {
          clearActiveHqSyncJob()
          Modal.error({
            title: t('posAdmin.products.hqSyncJobFailed', '商品 HQ 同步失败'),
            content: errorMessage,
          })
          return
        }
        message.warning(error instanceof Error ? error.message : t('posAdmin.products.hqSyncJobPollingFailed', '同步任务状态获取失败，将继续在后台重试'))
      })
  }, [clearActiveHqSyncJob, saveActiveHqSyncJob, showHqSyncJobResult, stopHqSyncJobPolling, t])

  const startSyncToStoreJobPolling = useCallback((job: SyncProductsToStoresJobResult) => {
    stopSyncToStorePolling()
    let consecutivePollingFailures = 0

    const showPollingTimeout = () => {
      Modal.warning({
        title: t('posAdmin.products.syncToStoreJobTimeoutTitle', '同步到分店仍在后台执行'),
        content: t('posAdmin.products.syncToStoreJobTimeout', '后台仍在执行，请稍后刷新查看'),
      })
    }

    const poller = createHqSyncJobPoller<SyncProductsToStoresJobResult>({
      jobId: job.jobId,
      getJob: async (jobId) => {
        try {
          const result = await getSyncProductsToStoresJob(jobId)
          consecutivePollingFailures = 0
          return result
        } catch (error) {
          const errorMessage = error instanceof Error ? error.message : ''
          if (error instanceof RequestError && (error.status === 404 || error.status === 401 || error.status === 403)) {
            throw error
          }
          consecutivePollingFailures += 1
          if (consecutivePollingFailures >= 3) {
            throw error
          }
          if (isMountedRef.current) {
            message.warning(error instanceof Error ? error.message : t('posAdmin.products.syncToStoreJobPollingFailed', '同步到分店任务状态获取失败，将继续在后台重试'))
          }
          return {
            jobId,
            status: 'Running',
            result: job.result,
            message: errorMessage || undefined,
          }
        }
      },
      pollIntervalMs: PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
      timeoutMs: PRODUCT_HQ_SYNC_TIMEOUT_MS,
    })
    stopSyncToStorePollingRef.current = poller.stop

    void poller.promise
      .then((result) => {
        if (!isMountedRef.current) {
          return
        }
        stopSyncToStorePollingRef.current = null
        showSyncToStoreJobResult(result)
      })
      .catch((error) => {
        if (!isMountedRef.current) {
          return
        }
        stopSyncToStorePollingRef.current = null
        if (error instanceof HqProductSyncPollingTimeoutError) {
          showPollingTimeout()
          return
        }
        if (error instanceof Error && error.message === '商品同步任务轮询已取消') {
          return
        }
        if (error instanceof RequestError && error.status === 404) {
          Modal.warning({
            title: t('posAdmin.products.syncToStoreJobMissingTitle', '同步到分店任务不存在'),
            content: error.message || t('posAdmin.products.syncToStoreJobMissing', '后台任务不存在或已过期，请确认后重新提交。'),
          })
          return
        }
        if (error instanceof RequestError && (error.status === 401 || error.status === 403)) {
          Modal.error({
            title: t('posAdmin.products.syncToStoreJobAuthFailedTitle', '无法查询同步到分店任务'),
            content: error.message || t('posAdmin.products.syncToStoreJobAuthFailed', '登录状态或权限不足，请重新登录后再查看任务状态。'),
          })
          return
        }
        Modal.warning({
          title: t('posAdmin.products.syncToStoreJobPollingStoppedTitle', '同步到分店任务状态获取失败'),
          content: error instanceof Error ? error.message : t('posAdmin.products.syncToStoreJobPollingStopped', '前端已停止轮询，请稍后刷新列表确认结果。'),
        })
      })
  }, [showSyncToStoreJobResult, stopSyncToStorePolling, t])

  const startSupplierImageBatchPolling = useCallback((job: ActiveSupplierImageBatchJob) => {
    const jobKey = normalizeSupplierImageBatchJobKey(job.localSupplierCode)
    if (!jobKey) return
    stopSupplierImageBatchPolling(job.localSupplierCode)
    let consecutivePollingFailures = 0

    const showPollingTimeout = () => {
      clearActiveImageBatchJob(job.localSupplierCode)
      Modal.warning({
        title: t('posAdmin.products.batchImageJobTimeoutTitle', '图片批量修改仍在后台执行'),
        content: t('posAdmin.products.batchImageJobTimeout', '前端已停止轮询该任务。你可以稍后刷新列表，或重新提交同一供应商和模板来接管后端已有任务。'),
      })
    }

    saveActiveImageBatchJob(job)
    const poller = createHqSyncJobPoller<BatchUpdateSupplierImagesJobResult>({
      jobId: job.jobId,
      getJob: async (jobId) => {
        try {
          const result = await getSupplierImageBatchUpdateJob(jobId)
          consecutivePollingFailures = 0
          saveActiveImageBatchJob({
            ...job,
            status: result.status,
            message: result.message || result.errorMessage,
          })
          return result
        } catch (error) {
          const errorMessage = error instanceof Error ? error.message : ''
          if (error instanceof RequestError && (error.status === 404 || error.status === 401 || error.status === 403)) {
            clearActiveImageBatchJob(job.localSupplierCode)
            throw error
          }
          consecutivePollingFailures += 1
          if (consecutivePollingFailures >= 3) {
            clearActiveImageBatchJob(job.localSupplierCode)
            throw error
          }
          if (isMountedRef.current) {
            message.warning(error instanceof Error ? error.message : t('posAdmin.products.batchImageJobPollingFailed', '图片批量修改任务状态获取失败，将继续在后台重试'))
          }
          return {
            jobId,
            status: 'Running',
            message: errorMessage,
          }
        }
      },
      pollIntervalMs: SUPPLIER_IMAGE_BATCH_POLL_INTERVAL_MS,
      timeoutMs: SUPPLIER_IMAGE_BATCH_TIMEOUT_MS,
    })
    stopSupplierImageBatchPollingRef.current[jobKey] = poller.stop

    void poller.promise
      .then((result) => {
        if (!isMountedRef.current) return
        clearActiveImageBatchJob(job.localSupplierCode)
        showSupplierImageBatchResult(result)
      })
      .catch((error) => {
        if (!isMountedRef.current) return

        const errorMessage = error instanceof Error ? error.message : ''
        if (error instanceof HqProductSyncPollingTimeoutError) {
          showPollingTimeout()
          return
        }
        if (error instanceof RequestError && error.status === 404) {
          Modal.warning({
            title: t('posAdmin.products.batchImageJobMissingTitle', '图片批量修改任务不存在'),
            content: error.message || t('posAdmin.products.batchImageJobMissing', '后台任务不存在或已过期，请确认后重新提交。'),
          })
          return
        }
        if (error instanceof RequestError && (error.status === 401 || error.status === 403)) {
          Modal.error({
            title: t('posAdmin.products.batchImageJobAuthFailedTitle', '无法查询图片批量修改任务'),
            content: error.message || t('posAdmin.products.batchImageJobAuthFailed', '登录状态或权限不足，请重新登录后再查看任务状态。'),
          })
          return
        }
        if (errorMessage === '商品同步任务轮询已取消' || errorMessage === '供应商图片批量修改任务轮询已取消') {
          return
        }
        Modal.warning({
          title: t('posAdmin.products.batchImageJobPollingStoppedTitle', '图片批量修改任务状态获取失败'),
          content: error instanceof Error ? error.message : t('posAdmin.products.batchImageJobPollingStopped', '前端已停止轮询，请稍后刷新列表确认结果。'),
        })
      })
  }, [clearActiveImageBatchJob, saveActiveImageBatchJob, showSupplierImageBatchResult, stopSupplierImageBatchPolling, t])

  const restoreActiveHqSyncJob = useCallback(() => {
    const restoredJob = readActiveProductHqSyncJob()
    if (!restoredJob?.jobId) return
    startHqSyncJobPolling(restoredJob)
  }, [startHqSyncJobPolling])

  const restoreActiveSupplierImageBatchJobs = useCallback(() => {
    Object.values(readActiveSupplierImageBatchJobs()).forEach((restoredJob) => {
      if (!restoredJob?.jobId) return
      startSupplierImageBatchPolling(restoredJob)
    })
  }, [startSupplierImageBatchPolling])

  const showActiveHqSyncJobStatus = useCallback((job: ActiveProductHqSyncJob | null = activeHqSyncJob) => {
    if (!job) return

    Modal.info({
      title: t('posAdmin.products.hqSyncJobStatusTitle', '商品 HQ 同步正在后台执行'),
      content: (
        <Descriptions size="small" column={1}>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobId', '任务 ID')}>
            {job.jobId}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobMode', '同步类型')}>
            {job.mode === 'full' ? t('posAdmin.products.fullSyncFromHQ', '全量同步') : t('posAdmin.products.incrementalSyncFromHQ', '增量同步')}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobStatus', '任务状态')}>
            {job.status || t('posAdmin.products.hqSyncJobQueued', '排队中')}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobStartedAt', '提交时间')}>
            {dayjs(job.createdAt).format('YYYY-MM-DD HH:mm:ss')}
          </Descriptions.Item>
        </Descriptions>
      ),
    })
    message.info(t('posAdmin.products.hqSyncJobStatusContent', '同步任务已在后台执行，请等待完成提示。'))
  }, [activeHqSyncJob, t])

  const showActiveSupplierImageBatchStatus = useCallback((job: ActiveSupplierImageBatchJob | null) => {
    if (!job) return

    Modal.info({
      title: t('posAdmin.products.batchImageJobStatusTitle', '图片批量修改正在后台执行'),
      content: (
        <Descriptions size="small" column={1}>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobId', '任务 ID')}>
            {job.jobId}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.supplier', '供应商')}>
            {supplierNameMap.get(job.localSupplierCode) || job.localSupplierCode}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobStatus', '任务状态')}>
            {job.status || t('posAdmin.products.hqSyncJobQueued', '排队中')}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.products.hqSyncJobStartedAt', '提交时间')}>
            {dayjs(job.createdAt).format('YYYY-MM-DD HH:mm:ss')}
          </Descriptions.Item>
        </Descriptions>
      ),
    })
    message.info(t('posAdmin.products.batchImageJobStatusContent', '图片批量修改任务已在后台执行，请等待完成提示。'))
  }, [supplierNameMap, t])

  const loadOptions = useCallback(async () => {
    await refreshSupplierOptions()
    try {
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
      setCategoryLoadFailed(false)
    } catch {
      setCategoryLoadFailed(true)
      message.error(t('posAdmin.products.categoryLoadFailed', '商品分类加载失败，请刷新后重试'))
    }
    try {
      const stores = await getActiveStores()
      setStoreOptions(stores)
    } catch { /* ignore */ }
  }, [refreshSupplierOptions, t])

  useEffect(() => {
    loadOptions()
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  useEffect(() => {
    return () => {
      isMountedRef.current = false
      stopHqSyncJobPolling()
    }
  }, [stopHqSyncJobPolling])

  useEffect(() => {
    return () => {
      stopSyncToStorePolling()
    }
  }, [stopSyncToStorePolling])

  useEffect(() => {
    return () => {
      stopSupplierImageBatchPolling()
    }
  }, [stopSupplierImageBatchPolling])

  useEffect(() => {
    restoreActiveHqSyncJob()
  }, [restoreActiveHqSyncJob])

  useEffect(() => {
    restoreActiveSupplierImageBatchJobs()
  }, [restoreActiveSupplierImageBatchJobs])

  useLayoutEffect(() => {
    const calc = () => {
      const containerH = wrapRef.current?.clientHeight || window.innerHeight
      const tbarH = toolbarRef.current?.getBoundingClientRect().height || 0
      const pagerH = pagerRef.current?.getBoundingClientRect().height || 0
      const available = containerH - tbarH - pagerH - 8
      setTableScrollY(available > 200 ? available : 200)
    }
    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [pageSize, total])

  const handleSearch = () => {
    // 分店记录筛选保持“输入态”和“生效态”分离，只在查询时折算成后端范围参数。
    let nextStoreRecordCountMode = storeRecordCountModeInput
    let nextStoreRecordCountMin = storeRecordCountMinInput
    let nextStoreRecordCountMax = storeRecordCountMaxInput
    if (storeRecordCountModeInput === 'all') {
      nextStoreRecordCountMin = undefined
      nextStoreRecordCountMax = undefined
    } else if (storeRecordCountModeInput === 'hasRecords') {
      nextStoreRecordCountMin = 1
      nextStoreRecordCountMax = undefined
    } else if (storeRecordCountModeInput === 'noRecords') {
      nextStoreRecordCountMin = 0
      nextStoreRecordCountMax = 0
    } else if (storeRecordCountModeInput === 'custom') {
      if (storeRecordCountMinInput === undefined && storeRecordCountMaxInput === undefined) {
        nextStoreRecordCountMode = 'all'
        nextStoreRecordCountMin = undefined
        nextStoreRecordCountMax = undefined
      } else if (
        storeRecordCountMinInput !== undefined &&
        storeRecordCountMaxInput !== undefined &&
        storeRecordCountMinInput > storeRecordCountMaxInput
      ) {
        message.warning(t('posAdmin.products.storeRecordFilterInvalidRange', '最小数量不能大于最大数量'))
        return
      }
    }
    setKeyword(keywordInput.trim())
    setSupplierCode(supplierCodeInput)
    setCategoryGuid(categoryGuidInput)
    setIsActiveFilter(isActiveFilterInput)
    setIsSetFilter(isSetFilterInput)
    setStoreRecordCountMode(nextStoreRecordCountMode)
    setStoreRecordCountMin(nextStoreRecordCountMin)
    setStoreRecordCountMax(nextStoreRecordCountMax)
    setPage(1)
    setSelectedRowKeys([])
    setQueryVersion((value) => value + 1)
  }

  const handleReset = () => {
    setKeywordInput('')
    setKeyword('')
    setSupplierCodeInput(undefined)
    setSupplierCode(undefined)
    setCategoryGuidInput(undefined)
    setCategoryGuid(undefined)
    setIsActiveFilterInput(undefined)
    setIsActiveFilter(undefined)
    setIsSetFilterInput(undefined)
    setIsSetFilter(undefined)
    setStoreRecordCountModeInput('all')
    setStoreRecordCountMode('all')
    setStoreRecordCountMinInput(undefined)
    setStoreRecordCountMaxInput(undefined)
    setStoreRecordCountMin(undefined)
    setStoreRecordCountMax(undefined)
    setColumnFilters({})
    setSortBy('productCode')
    setSortOrder('ascend')
    setPage(1)
    setSelectedRowKeys([])
    setQueryVersion((value) => value + 1)
  }

  const buildCategoryCascaderOptions = (nodes: ProductCategoryDto[], disabledGuids = new Set<string>()): any[] => {
    return nodes.map((node) => ({
      value: node.guid,
      label: node.name,
      disabled: disabledGuids.has(node.guid),
      children: node.children?.length ? buildCategoryCascaderOptions(node.children, disabledGuids) : undefined,
    }))
  }

  const getCategoryValueFromGuid = (guid: string | undefined, nodes: ProductCategoryDto[]): string[] | undefined => {
    if (!guid) return undefined
    const findPath = (items: ProductCategoryDto[], path: string[]): string[] | undefined => {
      for (const item of items) {
        const currentPath = [...path, item.guid]
        if (item.guid === guid) return currentPath
        if (item.children?.length) {
          const found = findPath(item.children, currentPath)
          if (found) return found
        }
      }
      return undefined
    }
    return findPath(nodes, [])
  }

  const categoryParentDisabledGuids = useMemo(
    () => new Set(editingCategory ? [editingCategory.guid, ...collectCategoryGuids(editingCategory.children)] : []),
    [editingCategory],
  )

  const ensureCanManagePosProducts = () => {
    if (canManagePosProducts) return true
    message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
    return false
  }

  const ensureCanCreateStoreProducts = () => {
    if (canCreateStoreProducts) return true
    message.warning(t('posAdmin.products.noCreatePermission', '无权限创建商品'))
    return false
  }

  const buildSyncProductsToStoresFields = (values: Record<string, unknown>): SyncProductsToStoresField[] => {
    const fields: SyncProductsToStoresField[] = []
    if (values.syncPurchasePrice) fields.push('purchasePrice')
    if (values.syncRetailPrice) fields.push('retailPrice')
    if (values.syncIsAutoPricing) fields.push('isAutoPricing')
    if (values.syncIsSpecialProduct) fields.push('isSpecialProduct')
    return fields
  }

  const showPushToHqResult = useCallback((result: PushProductsToHqResult) => {
    const errors = result.errors ?? []
    const detailStats = [
      { label: t('posAdmin.products.pushToHqAffectedRows', 'HQ影响记录'), value: result.affectedRowCount ?? 0 },
      { label: t('posAdmin.products.productsAdded', '商品新增'), value: result.productsAdded ?? 0 },
      { label: t('posAdmin.products.productsUpdated', '商品更新'), value: result.productsUpdated ?? 0 },
      { label: t('posAdmin.products.storeRetailPricesCreated', '门店零售价新增'), value: result.storeRetailPricesCreated ?? 0 },
      { label: t('posAdmin.products.storeRetailPricesUpdated', '门店零售价更新'), value: result.storeRetailPricesUpdated ?? 0 },
      { label: t('posAdmin.products.productSetCodesCreated', '套装编码新增'), value: result.productSetCodesCreated ?? 0 },
      { label: t('posAdmin.products.productSetCodesUpdated', '套装编码更新'), value: result.productSetCodesUpdated ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesCreated', '门店多码新增'), value: result.storeMultiCodesCreated ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesUpdated', '门店多码更新'), value: result.storeMultiCodesUpdated ?? 0 },
    ].filter((item) => item.value > 0)
    const content = (
      <Space direction="vertical" size={6}>
        {result.message && <div>{result.message}</div>}
        <div>
          {t('posAdmin.products.pushToHqResult', '发送完成：商品成功 {{success}}，失败 {{failed}}，合计 {{total}}', {
            success: result.successCount ?? 0,
            failed: result.failedCount ?? 0,
            total: result.totalCount ?? (result.successCount ?? 0) + (result.failedCount ?? 0),
          })}
        </div>
        {detailStats.map((item) => (
          <div key={item.label}>{item.label}: {item.value}</div>
        ))}
        {errors.length ? (
          <div style={{ whiteSpace: 'pre-wrap' }}>
            {t('posAdmin.products.partialSyncError', '部分同步错误')}：{errors.join('\n')}
          </div>
        ) : null}
      </Space>
    )

    if (errors.length || (result.failedCount ?? 0) > 0) {
      Modal.warning({
        title: t('posAdmin.products.pushToHqPartialSucceeded', '发送到 HQ 部分成功'),
        content,
      })
      return
    }

    Modal.success({
      title: t('posAdmin.products.pushToHqSucceeded', '发送到 HQ 完成'),
      content,
    })
  }, [t])

  const confirmPushToHqUpdateFields = (count: number): Promise<PushProductsToHqUpdateField[] | null> => {
    let selectedFields = [...defaultPushProductsToHqUpdateFields]
    return new Promise((resolve) => {
      Modal.confirm({
        title: t('posAdmin.products.pushToHq', '发送到HQ'),
        width: 640,
        okText: t('common.confirm', '确定'),
        cancelText: t('common.cancel', '取消'),
        content: (
          <Space direction="vertical" size={10} style={{ width: '100%' }}>
            <div>
              {t('posAdmin.products.pushToHqUpdateFieldsHint', '已选择 {{count}} 个商品，请勾选要更新到 HQ 的字段。', { count })}
            </div>
            <Checkbox.Group
              defaultValue={selectedFields}
              onChange={(values) => {
                selectedFields = values.map(String) as PushProductsToHqUpdateField[]
              }}
            >
              <Row gutter={[8, 6]}>
                {pushProductsToHqUpdateFieldOptions.map((field) => (
                  <Col span={12} key={field.value}>
                    <Checkbox value={field.value}>{t(field.labelKey, field.fallbackLabel)}</Checkbox>
                  </Col>
                ))}
              </Row>
            </Checkbox.Group>
            <div style={{ color: '#8c8c8c', fontSize: 12 }}>
              {t(
                'containers.updateFields.hqCreateHint',
                '字段选择主要限制已有 HQ 记录更新；如果目标表需要新增记录，系统仍会写入创建该记录所需的完整字段。',
              )}
            </div>
          </Space>
        ),
        onOk: () => {
          if (!selectedFields.length) {
            message.warning(t('containers.updateFields.selectAtLeastOne', '请至少选择一个更新字段'))
            return Promise.reject()
          }
          resolve(selectedFields)
          return undefined
        },
        onCancel: () => resolve(null),
      })
    })
  }

  const showSelectedFromHqResult = useCallback((result: HqProductSyncResult) => {
    const content = (
      <Space direction="vertical" size={6}>
        {result.message && <div>{result.message}</div>}
        {buildHqSyncResultLines(result).map((line) => (
          <div key={line}>{line}</div>
        ))}
        {result.errors?.length ? (
          <div style={{ whiteSpace: 'pre-wrap' }}>
            {t('posAdmin.products.partialSyncError', '部分同步错误')}：{result.errors.join('\n')}
          </div>
        ) : null}
      </Space>
    )

    if (result.errors?.length) {
      Modal.warning({
        title: t('posAdmin.products.syncSelectedFromHqPartialSucceeded', '从 HQ 同步选中商品部分成功'),
        content,
      })
      return
    }

    Modal.success({
      title: t('posAdmin.products.syncSelectedFromHqSucceeded', '从 HQ 同步选中商品完成'),
      content,
    })
  }, [buildHqSyncResultLines, t])

  function showSyncToStoreJobResult(job: SyncProductsToStoresJobResult) {
    const result: SyncProductsToStoresResult = job.result ?? {
      createdCount: 0,
      updatedCount: 0,
      failedCount: 0,
      errors: job.errors ?? [],
      message: job.message,
    }
    const errors = result.errors ?? job.errors ?? []
    if (!result.errors?.length && errors.length) {
      result.errors = errors
    }
    const content = (
      <Space direction="vertical" size={6}>
        {(job.message || result.message) ? <div>{job.message || result.message}</div> : null}
        <div>
          {t('posAdmin.products.syncToStoreJobResult', '同步完成：新建 {{created}}，更新 {{updated}}，失败 {{failed}}', {
            created: result.createdCount ?? 0,
            updated: result.updatedCount ?? 0,
            failed: result.failedCount ?? 0,
          })}
        </div>
        {errors.length ? (
          <div style={{ whiteSpace: 'pre-wrap' }}>
            {t('posAdmin.products.partialSyncError', '部分同步错误')}：{result.errors.join('\n')}
          </div>
        ) : null}
      </Space>
    )

    if (job.status === 'Failed') {
      Modal.error({
        title: t('posAdmin.products.syncToStoreFailed', '同步到分店失败'),
        content,
      })
      return
    }

    if (errors.length || (result.failedCount ?? 0) > 0) {
      Modal.warning({
        title: t('posAdmin.products.syncToStorePartialSucceeded', '同步到分店部分成功'),
        content,
      })
      setSelectedRowKeys([])
      void loadData()
      return
    }

    Modal.success({
      title: t('posAdmin.products.syncToStoreSucceeded', '同步到分店完成'),
      content,
    })
    setSelectedRowKeys([])
    void loadData()
  }

  const ensureCanSyncProductsFromHq = () => {
    if (isAdmin) return true
    setHqSyncVisible(false)
    message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
    return false
  }

  const openEdit = (record: PosProductDto) => {
    if (!ensureCanManagePosProducts()) return
    resetEditSetCodeState()
    setEditingProduct(record)
    editForm.setFieldsValue({
      productCode: record.productCode,
      barcode: record.barcode,
      productName: record.productName,
      productNameCn: record.productNameCn,
      productImage: record.productImage || buildDefaultProductImageUrl(record.itemNumber || record.productCode),
      itemNumber: record.itemNumber,
      localSupplierCode: record.localSupplierCode,
      productType: record.productType ?? 0,
      purchasePrice: record.purchasePrice,
      retailPrice: record.retailPrice,
      unitWeight: record.unitWeight ?? 1,
      middlePackageQuantity: record.middlePackageQuantity,
      isAutoPricing: record.isAutoPricing ?? false,
      isSpecialProduct: record.isSpecialProduct ?? false,
      isActive: record.isActive,
      categoryGuid: getCategoryValueFromGuid(record.categoryGuid, categoryTree),
    })
    setEditVisible(true)
  }

  const openCreateModal = () => {
    if (!ensureCanCreateStoreProducts()) return
    createForm.resetFields()
    createForm.setFieldsValue({
      productType: 0,
      isActive: true,
      isAutoPricing: true,
      isSpecialProduct: false,
    })
    setCreateVisible(true)
  }

  const openStoreRecords = async (record: PosProductDto) => {
    if (!canManageStoreProducts) return

    const requestSeq = storeRecordsRequestSeqRef.current + 1
    storeRecordsRequestSeqRef.current = requestSeq
    setStoreRecordSelectedRowKeys([])
    storeRecordBatchEditForm.resetFields()
    setStoreRecordBatchEditVisible(false)
    setStoreRecordBatchUpdating(false)
    setStoreRecordsProduct(record)
    setStoreRecordsVisible(true)
    setStoreRecordsLoading(true)
    setStoreRecordsData([])
    try {
      const records = await getProductStoreRecords(record.productCode)
      if (requestSeq === storeRecordsRequestSeqRef.current) {
        setStoreRecordsData(records)
      }
    } catch {
      if (requestSeq === storeRecordsRequestSeqRef.current) {
        message.error(t('posAdmin.products.loadStoreRecordsFailed', '加载分店记录失败'))
      }
    } finally {
      if (requestSeq === storeRecordsRequestSeqRef.current) {
        setStoreRecordsLoading(false)
      }
    }
  }

  const closeStoreRecords = () => {
    storeRecordsRequestSeqRef.current += 1
    setStoreRecordsVisible(false)
    setStoreRecordsLoading(false)
    setStoreRecordsProduct(null)
    setStoreRecordsData([])
    setStoreRecordSelectedRowKeys([])
    storeRecordBatchEditForm.resetFields()
    setStoreRecordBatchEditVisible(false)
    setStoreRecordBatchUpdating(false)
  }

  const openStoreRecordBatchEdit = () => {
    if (!canEditStoreProducts || !storeRecordSelectedRowKeys.length) return
    storeRecordBatchEditForm.resetFields()
    setStoreRecordBatchEditVisible(true)
  }

  const handleStoreRecordBatchEditSave = async () => {
    if (!storeRecordsProduct || storeRecordBatchUpdating) return
    if (!storeRecordSelectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectStoreRecordsFirst', '请先选择分店记录'))
      return
    }

    try {
      const values = await storeRecordBatchEditForm.validateFields() as StoreRecordBatchEditFormValues
      const fieldConfigs: Array<{
        enabled: boolean
        field: keyof BatchUpdateProductStoreRecordsChanges
        value: number | boolean | undefined
      }> = [
        { enabled: !!values.updatePurchasePrice, field: 'purchasePrice', value: values.purchasePrice },
        { enabled: !!values.updateStoreRetailPriceValue, field: 'storeRetailPriceValue', value: values.storeRetailPriceValue },
        { enabled: !!values.updateDiscountRate, field: 'discountRate', value: values.discountRate },
        { enabled: !!values.updateIsAutoPricing, field: 'isAutoPricing', value: values.isAutoPricing },
        { enabled: !!values.updateIsSpecialProduct, field: 'isSpecialProduct', value: values.isSpecialProduct },
        { enabled: !!values.updateIsActive, field: 'isActive', value: values.isActive },
      ]

      const enabledFieldConfigs = fieldConfigs.filter((config) => config.enabled)
      if (!enabledFieldConfigs.length) {
        message.warning(t('posAdmin.products.selectAtLeastOneStoreRecordField', '请至少选择一个要修改的字段'))
        return
      }

      if (enabledFieldConfigs.some((config) => config.value === undefined || config.value === null)) {
        message.warning(t('posAdmin.products.completeStoreRecordFields', '请填写已勾选的字段值'))
        return
      }

      // 分店记录行 key 里可能带 storeProductCode，真正提交时只取非空分店代码。
      const selectedStoreCodes = storeRecordSelectedRows
        .map((record) => record.storeCode)
        .filter((storeCode): storeCode is string => !!storeCode)
      if (!selectedStoreCodes.length) {
        message.warning(t('posAdmin.products.selectStoreRecordsFirst', '请先选择分店记录'))
        return
      }

      const changes = enabledFieldConfigs.reduce<BatchUpdateProductStoreRecordsChanges>((acc, config) => {
        acc[config.field] = config.value as never
        return acc
      }, {})

      setStoreRecordBatchUpdating(true)
      const result = await batchUpdateProductStoreRecords(storeRecordsProduct.productCode, {
        storeCodes: selectedStoreCodes,
        changes,
      })
      message.success(t('posAdmin.products.batchUpdateStoreRecordsResult', '批量修改完成：成功 {{success}}，失败 {{failed}}', {
        success: result.successCount,
        failed: result.failedCount,
      }))
      if (result.errors.length) {
        Modal.error({
          title: t('posAdmin.products.batchUpdateStoreRecordsErrors', '批量修改存在失败记录'),
          content: result.errors.join('\n'),
        })
      }
      setStoreRecordSelectedRowKeys([])
      storeRecordBatchEditForm.resetFields()
      setStoreRecordBatchEditVisible(false)
      await openStoreRecords(storeRecordsProduct)
      await loadData()
    } catch {
      message.error(t('posAdmin.products.batchUpdateStoreRecordsFailed', '批量修改分店记录失败'))
    } finally {
      setStoreRecordBatchUpdating(false)
    }
  }

  const handleEditSave = async () => {
    if (!editingProduct) return
    if (!ensureCanManagePosProducts()) return
    try {
      const values = await editForm.validateFields()
      if (!validateEditSetCodes()) return
      const resolvedCategoryGuid = resolveCascaderLeafValue(values.categoryGuid)
      const updateData: Partial<PosProductDto> = {
        productName: values.productName,
        itemNumber: values.itemNumber,
        barcode: values.barcode,
        localSupplierCode: values.localSupplierCode,
        purchasePrice: values.purchasePrice,
        retailPrice: values.retailPrice,
        unitWeight: values.unitWeight,
        middlePackageQuantity: values.middlePackageQuantity,
        productType: values.productType,
        isAutoPricing: values.isAutoPricing,
        isSpecialProduct: values.isSpecialProduct,
        isActive: values.isActive,
        categoryGuid: resolvedCategoryGuid,
        // 后端商品更新可能是覆盖式 PUT，保存多码前必须带回原图片字段。
        productImage: values.productImage ?? editingProduct.productImage ?? '',
      }
      await updateProduct(editingProduct.productCode, updateData)

      if (productTypeWatch === 1 || productTypeWatch === 2) {
        const deleteIds = Object.values(editPendingDeletes).filter((c: any) => c.id).map((c: any) => c.id)
        if (deleteIds.length) {
          await batchDeleteSetCodes({ ids: deleteIds })
        }

        const newRows = editSetCodes.filter((r: any) => !r.id)
        if (newRows.length) {
          await batchCreateSetCodes({
            items: newRows.map((r: any) => {
              const rowId = r._rowId
              const edit = editSetPriceEdits[rowId] || {}
              return {
                productCode: editingProduct.productCode,
                setItemNumber: (edit.setItemNumber ?? r.setItemNumber) || undefined,
                setBarcode: (edit.setBarcode ?? r.setBarcode) || undefined,
                setPurchasePrice: edit.setPurchasePrice ?? r.setPurchasePrice,
                setRetailPrice: edit.setRetailPrice ?? r.setRetailPrice,
                isActive: true,
              }
            }).filter((x: any) => (x.setItemNumber ?? '') !== '' || (x.setBarcode ?? '') !== '' || x.setPurchasePrice !== undefined || x.setRetailPrice !== undefined),
          })
        }

        const barcodeUpdates = Object.entries(editSetPriceEdits)
          .filter(([id, p]: [string, any]) => p.setBarcode !== undefined && editSetCodes.find((r: any) => (r.id || r._rowId) === id && r.id))
          .map(([id, p]: [string, any]) => ({ id, setBarcode: p.setBarcode }))
        if (barcodeUpdates.length) {
          await batchUpdateSetBarcodes({ items: barcodeUpdates })
        }

        const priceUpdates = Object.entries(editSetPriceEdits)
          .filter(([id, p]: [string, any]) => (p.setPurchasePrice !== undefined || p.setRetailPrice !== undefined) && editSetCodes.find((r: any) => (r.id || r._rowId) === id && r.id))
          .map(([id, p]: [string, any]) => ({ id, setPurchasePrice: p.setPurchasePrice, setRetailPrice: p.setRetailPrice }))
        if (priceUpdates.length) {
          await batchUpdateSetPrices({ items: priceUpdates })
        }
      }

      message.success(t('message.saveSuccess', '保存成功'))
      setEditVisible(false)
      setEditingProduct(null)
      resetEditSetCodeState()
      await loadData()
    } catch {
      message.error(t('message.saveFailed', '保存失败'))
    }
  }

  const handleCreateSave = async () => {
    if (!ensureCanCreateStoreProducts()) return
    setCreateSubmitting(true)
    try {
      const values = await createForm.validateFields()
      const resolvedCategoryGuid = resolveCascaderLeafValue(values.productCategoryGUID)
      const result = await createProductWithPrices({
        productName: values.productName,
        productCategoryGUID: resolvedCategoryGuid,
        localSupplierCode: values.localSupplierCode,
        itemNumber: values.itemNumber,
        barcode: values.barcode,
        productImage: values.productImage,
        purchasePrice: values.purchasePrice,
        retailPrice: values.retailPrice,
        isAutoPricing: values.isAutoPricing ?? true,
        isSpecialProduct: values.isSpecialProduct ?? false,
        isActive: true,
        productType: 0,
      })
      const createdStoreCount = Object.keys(result.storeProductCodes ?? {}).length
      message.success(t('posAdmin.products.createProductSuccess', '创建成功：商品 {{code}}，已创建 {{count}} 条分店记录', {
        code: result.productCode,
        count: createdStoreCount,
      }))
      setCreateVisible(false)
      createForm.resetFields()
      void loadData()
    } catch {
      message.error(t('posAdmin.products.createProductFailed', '创建商品失败'))
    } finally {
      setCreateSubmitting(false)
    }
  }

  const handleBatchEnable = async (enable: boolean) => {
    if (!ensureCanManagePosProducts()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectProductsFirst', '请先选择商品'))
      return
    }
    try {
      const items: BatchUpdatePosProductDto[] = selectedRowKeys.map((code) => ({
        productCode: String(code),
        isActive: enable,
      }))
      const result = await batchUpdateProducts(items)
      message.success(t('posAdmin.products.batchUpdateSuccess', '成功更新 {{count}} 个商品', { count: result.successCount }))
      if (result.failedCount > 0) {
        message.warning(t('posAdmin.products.batchUpdatePartialFailed', '{{count}} 个商品更新失败', { count: result.failedCount }))
      }
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error(t('posAdmin.products.batchUpdateFailed', '批量更新失败'))
    }
  }

  const openBatchEdit = () => {
    if (!ensureCanManagePosProducts()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectProductsFirst', '请先选择商品'))
      return
    }
    batchEditForm.resetFields()
    setBatchEditVisible(true)
  }

  const setImageBatchTemplate = (supplierCode: string | undefined, mode: SupplierImageMode) => {
    const supplier = supplierOptions.find((option) => option.value === supplierCode)
    imageBatchForm.setFieldValue('urlTemplate', getDefaultSupplierImageTemplate(supplier, mode))
  }

  const openImageBatch = () => {
    if (!ensureCanManagePosProducts()) return
    const defaultMode: SupplierImageMode = 'supplier'
    const defaultSupplierCode = supplierCodeInput || supplierCode
    const defaultScope = getDefaultSupplierImageBatchScope(selectedRowKeys)
    imageBatchForm.resetFields()
    imageBatchForm.setFieldsValue({
      localSupplierCode: defaultSupplierCode,
      mode: defaultMode,
      scope: defaultScope,
      updateTargets: ['hbweb', 'hq'],
      saveSupplierImageBaseUrl: false,
    })
    setImageBatchTemplate(defaultSupplierCode, defaultMode)
    setImageBatchVisible(true)
  }

  const handleBatchTranslate = async () => {
    if (!ensureCanManagePosProducts()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectProductsFirst', '请先选择商品'))
      return
    }

    const selectedRows = data.filter((row) => selectedRowKeys.includes(row.key))
    // 只翻译当前页选中的中文商品名，去重后减少翻译接口压力。
    const names = Array.from(new Set(selectedRows.map((row) => row.productName.trim()).filter((name) => name && containsChineseText(name))))
    if (!names.length) {
      message.warning(t('posAdmin.products.noNamesToTranslate', '没有可翻译的商品名称'))
      return
    }

    try {
      setTranslating(true)
      const translations = await batchTranslate(names)
      const updates = buildProductNameTranslationUpdates(selectedRows, translations)
      if (!updates.length) {
        message.warning(t('posAdmin.products.noValidTranslatedNames', '没有可保存的英文翻译结果'))
        return
      }

      const result = await batchUpdateProducts(updates)
      message.success(t('posAdmin.products.batchTranslateSuccess', '成功翻译 {{count}} 个商品名称', { count: result.successCount }))
      if (result.failedCount > 0) {
        message.warning(t('posAdmin.products.batchUpdatePartialFailed', '{{count}} 个商品更新失败', { count: result.failedCount }))
      }
      if (updates.length < selectedRows.length) {
        message.warning(t('posAdmin.products.invalidTranslatedNamesSkipped', '有 {{count}} 条翻译结果仍包含中文或无变化，已跳过', { count: selectedRows.length - updates.length }))
      }
      setSelectedRowKeys([])
      await loadData()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.products.batchTranslateFailed', '批量翻译失败'))
    } finally {
      setTranslating(false)
    }
  }

  const handleImageBatchSave = async () => {
    if (!ensureCanManagePosProducts()) return
    try {
      const values = await imageBatchForm.validateFields()
      const targets: string[] = values.updateTargets || []
      if (!targets.length && !values.saveSupplierImageBaseUrl) {
        message.warning(t('posAdmin.products.selectImageUpdateTarget', '请选择写入目标'))
        return
      }
      const existingActiveJob = getActiveImageBatchJobBySupplier(values.localSupplierCode)
      if (existingActiveJob) {
        startSupplierImageBatchPolling(existingActiveJob)
        showActiveSupplierImageBatchStatus(existingActiveJob)
        return
      }
      setImageBatchLoading(true)
      const request = {
        localSupplierCode: values.localSupplierCode,
        urlTemplate: values.urlTemplate.trim(),
        updateHbweb: targets.includes('hbweb'),
        updateHq: targets.includes('hq'),
        saveSupplierImageBaseUrl: values.saveSupplierImageBaseUrl ?? false,
        ...buildSupplierImageBatchScopeRequest(
          (values.scope || 'supplier') as SupplierImageBatchScope,
          selectedRowKeys,
        ),
      }
      const operationId = buildSupplierImageBatchUpdateOperationId(request)
      const job = await createSupplierImageBatchUpdateJob({
        ...request,
        operationId,
      })
      if (!job.jobId) {
        message.error(job.message || job.errorMessage || t('posAdmin.products.batchImageJobCreateFailed', '创建图片批量修改任务失败'))
        return
      }
      const activeJob: ActiveSupplierImageBatchJob = {
        jobId: job.jobId,
        operationId: job.operationId || operationId,
        localSupplierCode: request.localSupplierCode,
        createdAt: new Date().toISOString(),
        status: job.status ?? 'Queued',
        message: job.message || job.errorMessage,
      }
      setImageBatchVisible(false)
      message.success(t('posAdmin.products.batchImageJobSubmitted', '图片批量修改任务已提交，正在后台执行。完成后会自动提示结果。'))
      startSupplierImageBatchPolling(activeJob)
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.products.batchImageJobCreateFailed', '创建图片批量修改任务失败'))
    } finally {
      setImageBatchLoading(false)
    }
  }

  const handleBatchEditSave = async () => {
    if (!ensureCanManagePosProducts()) return
    try {
      const values = await batchEditForm.validateFields()
      const resolvedCategoryGuid = resolveCascaderLeafValue(values.categoryGuid)
      const items: BatchUpdatePosProductDto[] = selectedRowKeys.map((code) => ({
        productCode: String(code),
        retailPrice: values.retailPrice ?? undefined,
        purchasePrice: values.purchasePrice ?? undefined,
        middlePackageQuantity: values.middlePackageQuantity ?? undefined,
        isAutoPricing: values.isAutoPricing,
        isSpecialProduct: values.isSpecialProduct,
        isActive: values.isActive,
        categoryGuid: resolvedCategoryGuid ?? undefined,
        localSupplierCode: values.localSupplierCode ?? undefined,
      }))
      const result = await batchUpdateProducts(items)
      message.success(t('posAdmin.products.batchUpdateSuccess', '成功更新 {{count}} 个商品', { count: result.successCount }))
      if (result.failedCount > 0) {
        message.warning(t('posAdmin.products.batchUpdatePartialFailed', '{{count}} 个商品更新失败', { count: result.failedCount }))
      }
      setBatchEditVisible(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error(t('posAdmin.products.batchEditFailed', '批量编辑失败'))
    }
  }

  const openHqSyncModal = (mode: HqSyncMode) => {
    if (!ensureCanSyncProductsFromHq()) return
    const storedActiveJob = activeHqSyncJob ?? readActiveProductHqSyncJob()
    if (storedActiveJob) {
      if (!activeHqSyncJob) {
        setActiveHqSyncJob(storedActiveJob)
        startHqSyncJobPolling(storedActiveJob)
      }
      showActiveHqSyncJobStatus(storedActiveJob)
      return
    }
    setHqSyncMode(mode)
    hqSyncForm.resetFields()
    if (mode === 'incremental') {
      hqSyncForm.setFieldsValue({ startDate: dayjs().subtract(100, 'day') })
    }
    setHqSyncVisible(true)
  }

  const openSyncToStoreModal = () => {
    if (!ensureCanManagePosProducts()) return
    syncToStoreForm.resetFields()
    setSyncSelectAll(false)
    syncToStoreForm.setFieldsValue({
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncIsAutoPricing: true,
      syncIsSpecialProduct: true,
    })
    if (selectedRowKeys.length) {
      syncToStoreForm.setFieldsValue({
        productCodes: selectedRowKeys.map(String),
      })
    }
    setSyncToStoreVisible(true)
  }

  const handleSyncFromHq = async () => {
    if (!ensureCanSyncProductsFromHq()) return
    const storedActiveJob = activeHqSyncJob ?? readActiveProductHqSyncJob()
    if (hqSyncSubmitting || storedActiveJob) {
      if (storedActiveJob) {
        if (!activeHqSyncJob) {
          setActiveHqSyncJob(storedActiveJob)
          startHqSyncJobPolling(storedActiveJob)
        }
        showActiveHqSyncJobStatus(storedActiveJob)
      }
      return
    }
    if (hqSyncSubmittingRef.current) return

    try {
      hqSyncSubmittingRef.current = true
      setHqSyncSubmitting(true)
      const values = hqSyncMode === 'incremental' ? await hqSyncForm.validateFields() : {}
      const startDate = values.startDate ? values.startDate.format('YYYY-MM-DD') : undefined
      const operationId = buildProductHqSyncOperationId(hqSyncMode, startDate)
      const syncJob = hqSyncMode === 'full'
        ? await createProductFullHqSyncJob({ operationId })
        : await createProductIncrementalHqSyncJob({
          operationId,
          startDate,
        })

      if (!syncJob.jobId) {
        message.error(syncJob.message || t('posAdmin.products.hqSyncJobCreateFailed', '创建商品 HQ 同步任务失败'))
        return
      }

      const activeJob: ActiveProductHqSyncJob = {
        jobId: syncJob.jobId,
        mode: hqSyncMode,
        operationId: syncJob.operationId || operationId,
        createdAt: new Date().toISOString(),
        status: syncJob.status ?? 'Queued',
        message: syncJob.message,
        startDate,
      }

      setHqSyncVisible(false)
      message.success(t('posAdmin.products.hqSyncJobSubmitted', '同步任务已提交，正在后台执行。完成后会自动提示结果。'))
      startHqSyncJobPolling(activeJob)
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.products.hqSyncJobCreateFailed', '创建商品 HQ 同步任务失败'))
    } finally {
      hqSyncSubmittingRef.current = false
      setHqSyncSubmitting(false)
    }
  }

  const handleSyncToStores = async () => {
    if (!ensureCanManagePosProducts()) return
    try {
      const values = await syncToStoreForm.validateFields()
      const syncFields = buildSyncProductsToStoresFields(values)
      if (!syncFields.length) {
        message.warning(t('posAdmin.products.selectSyncFields', '请选择要同步的字段'))
        return
      }
      setSyncToStoreLoading(true)
      const req: SyncProductsToStoresRequest = {
        productCodes: values.productCodes || [],
        storeCodes: values.storeCodes || [],
        overwrite: values.overwrite ?? false,
        fields: syncFields,
      }
      if (!req.productCodes.length && selectedRowKeys.length) {
        req.productCodes = selectedRowKeys.map(String)
      }
      if (!req.productCodes.length) {
        message.warning(t('posAdmin.products.selectProductsToSync', '请选择要同步的商品'))
        return
      }
      if (!req.storeCodes.length) {
        message.warning(t('posAdmin.productPrice.selectTargetStore', '请选择目标分店'))
        return
      }
      // 同步到分店改为后台 job，避免前端等待长任务后只能拿到不准确的即时统计。
      const job = await startSyncProductsToStoresJob(req)
      if (!job.jobId) {
        message.error(job.message || t('posAdmin.products.syncToStoreFailed', '同步到分店失败'))
        return
      }
      setSyncToStoreVisible(false)
      syncToStoreForm.resetFields()
      if (job.status === 'Succeeded' || job.status === 'Failed') {
        showSyncToStoreJobResult(job)
        return
      }
      message.success(t('posAdmin.products.syncToStoreJobSubmitted', '同步任务已提交，正在后台执行。完成后会自动提示结果。'))
      startSyncToStoreJobPolling(job)
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.products.syncToStoreFailed', '同步到分店失败'))
    } finally {
      setSyncToStoreLoading(false)
    }
  }

  const handlePushToHq = async () => {
    if (!ensureCanManagePosProducts()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectProductsFirst', '请先选择商品'))
      return
    }
    // 使用 ref 作为即时锁，避免 React 状态尚未刷新时连续点击重复提交。
    if (pushToHqLoadingRef.current) return
    const productCodes = selectedRowKeys.map(String)
    pushToHqLoadingRef.current = true
    setPushToHqLoading(true)

    try {
      const updateFields = await confirmPushToHqUpdateFields(productCodes.length)
      if (!updateFields) return
      const result = await pushProductsToHq({
        productCodes,
        updateFields,
      })
      showPushToHqResult(result)
      setSelectedRowKeys([])
      await loadData()
    } catch (error) {
      const errorResult = extractPushToHqErrorResult(error)
      if (errorResult) {
        Modal.error({
          title: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
          content: (
            <div>
              <div>{errorResult.message || (error instanceof Error ? error.message : t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'))}</div>
              {errorResult.errors.length ? (
                <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>
                  {errorResult.errors.join('\n')}
                </div>
              ) : null}
            </div>
          ),
        })
      } else {
        message.error(error instanceof Error ? error.message : t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'))
      }
    } finally {
      pushToHqLoadingRef.current = false
      setPushToHqLoading(false)
    }
  }

  const handleSyncSelectedFromHq = async () => {
    if (!ensureCanSyncProductsFromHq()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.products.selectProductsFirst', '请先选择商品'))
      return
    }
    // 使用 ref 作为即时锁，避免连续点击在状态刷新前重复提交同一批商品。
    if (selectedFromHqLoadingRef.current) return

    try {
      selectedFromHqLoadingRef.current = true
      setSelectedFromHqLoading(true)
      const result = await syncSelectedProductsFromHq({
        productCodes: selectedRowKeys.map(String),
      })
      showSelectedFromHqResult(result)
      setSelectedRowKeys([])
      await loadData()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.products.syncSelectedFromHqFailed', '从 HQ 同步选中商品失败'))
    } finally {
      selectedFromHqLoadingRef.current = false
      setSelectedFromHqLoading(false)
    }
  }

  useEffect(() => {
    const requestSeq = editSetCodesRequestSeqRef.current + 1
    editSetCodesRequestSeqRef.current = requestSeq

    if (editVisible && (productTypeWatch === 1 || productTypeWatch === 2) && editingProductCode) {
      resetEditSetCodeState()
      setEditSetCodesLoading(true)
      getGridData({ productCode: editingProductCode, pageIndex: 1, pageSize: 200 })
        .then((result) => {
          if (requestSeq !== editSetCodesRequestSeqRef.current) return
          const items = (result?.items ?? []).map((r: any) => ({ ...r, _rowId: r.id || `loaded_${Date.now()}_${Math.random().toString(36).slice(2)}` }))
          setEditSetCodes(items)
        })
        .catch(() => {
          if (requestSeq === editSetCodesRequestSeqRef.current) {
            message.error(t('posAdmin.products.loadBarcodeDataFailed', '加载条码数据失败'))
          }
        })
        .finally(() => {
          if (requestSeq === editSetCodesRequestSeqRef.current) {
            setEditSetCodesLoading(false)
          }
        })
    } else {
      resetEditSetCodeState()
      setEditSetCodesLoading(false)
    }
  }, [editVisible, productTypeWatch, editingProductCode, resetEditSetCodeState, t])

  const handleProductTypeChange = (newType: number) => {
    if (newType === 0 && editSetCodes.length > 0) {
      message.warning(t('posAdmin.products.deleteBarcodesFirst', '请先删除所有条码后再切换为普通商品'))
      const current = editForm.getFieldValue('productType')
      setTimeout(() => editForm.setFieldValue('productType', current), 0)
    }
  }

  const editAddSetCodeRow = () => {
    const tempId = `tmp_${Date.now()}_${Math.random().toString(36).slice(2)}`
    let defaults: any = { setPurchasePrice: undefined, setRetailPrice: undefined }
    if (editingProduct) {
      if (productTypeWatch === 2) {
        defaults = { setPurchasePrice: editingProduct.purchasePrice, setRetailPrice: editingProduct.retailPrice }
      }
    }
    const newRow = { _rowId: tempId, id: undefined, productCode: editingProduct?.productCode || '', setItemNumber: '', setBarcode: '', ...defaults, isActive: true }
    setEditSetCodes((prev) => [...prev, newRow])
    setEditSetPriceEdits((prev) => ({ ...prev, [tempId]: { setItemNumber: '', setBarcode: '', ...defaults } }))
  }

  const editDeleteSetCodeRow = (row: any) => {
    const rowId = row.id || row._rowId
    if (row.id) {
      setEditPendingDeletes((prev) => ({ ...prev, [rowId]: row }))
    }
    setEditSetCodes((prev) => prev.filter((r) => (r.id || r._rowId) !== rowId))
    setEditSetPriceEdits((prev) => { const next = { ...prev }; delete next[rowId]; return next })
  }

  const editHandleRetailPriceChange = (row: any, retailPrice: number) => {
    const rowId = row.id || row._rowId
    if (productTypeWatch === 1 && editingProduct) {
      const mainPP = editingProduct.purchasePrice || 0
      const mainRP = editingProduct.retailPrice || 0
      let calcPP: number | undefined
      if (mainRP > 0 && retailPrice > 0) {
        calcPP = parseFloat((retailPrice * (mainPP / mainRP)).toFixed(2))
      }
      setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setRetailPrice: retailPrice, setPurchasePrice: calcPP } }))
    } else {
      setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setRetailPrice: retailPrice } }))
    }
  }

  const editHandlePurchasePriceChange = (row: any, purchasePrice: number) => {
    const rowId = row.id || row._rowId
    setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setPurchasePrice: purchasePrice } }))
  }

  const editHandleBarcodeChange = (row: any, barcode: string) => {
    const rowId = row.id || row._rowId
    setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setBarcode: barcode } }))
  }

  const validateEditSetCodes = (): boolean => {
    if (productTypeWatch !== 1 && productTypeWatch !== 2) return true
    for (const code of editSetCodes) {
      const rowId = code.id || code._rowId
      const edit = editSetPriceEdits[rowId] || {}
      const barcode = edit.setBarcode ?? code.setBarcode
      const retailPrice = edit.setRetailPrice ?? code.setRetailPrice
      if (!barcode || barcode.trim() === '') { message.error(t('posAdmin.products.barcodeRequired', '条码不能为空')); return false }
      if (retailPrice === undefined || retailPrice === null) { message.error(t('posAdmin.products.retailPriceRequired', '零售价不能为空')); return false }
    }
    return true
  }

  const openSetCodeManager = async (product: PosProductDto) => {
    if (!ensureCanManagePosProducts()) return
    setSetCodeProduct(product)
    setSetCodeVisible(true)
    setSetCodeLoading(true)
    try {
      const result = await getGridData({ productCode: product.productCode, pageIndex: 1, pageSize: 200 })
      setSetCodeData(result?.items ?? [])
    } catch {
      message.error(t('posAdmin.products.loadSetBarcodeFailed', '加载套装条码失败'))
    } finally {
      setSetCodeLoading(false)
    }
  }

  const handleAddSetCode = () => {
    if (!ensureCanManagePosProducts()) return
    const newItem: MulticodeSetItem = {
      id: `new_${Date.now()}`,
      productCode: setCodeProduct?.productCode || '',
      setItemNumber: '',
      setBarcode: '',
      setPurchasePrice: 0,
      setRetailPrice: 0,
      isActive: true,
    }
    setSetCodeData((prev) => [...prev, newItem])
  }

  const handleSetCodeChange = (id: string, field: keyof MulticodeSetItem, value: any) => {
    setSetCodeData((prev) =>
      prev.map((item) => (item.id === id ? { ...item, [field]: value } : item)),
    )
  }

  const handleDeleteSetCode = async (item: MulticodeSetItem) => {
    if (!ensureCanManagePosProducts()) return
    if (!item.id || item.id.startsWith('new_')) {
      setSetCodeData((prev) => prev.filter((i) => i.id !== item.id))
      return
    }
    try {
      await batchDeleteSetCodes({ ids: [item.id!] })
      message.success(t('message.deleteSuccess', '删除成功'))
      setSetCodeData((prev) => prev.filter((i) => i.id !== item.id))
    } catch {
      message.error(t('message.deleteFailed', '删除失败'))
    }
  }

  const handleSaveSetCodes = async () => {
    if (!ensureCanManagePosProducts()) return
    try {
      const newItems = setCodeData.filter((i) => i.id?.startsWith('new_'))
      const existingItems = setCodeData.filter((i) => !i.id?.startsWith('new_'))

      if (newItems.length) {
        await batchCreateSetCodes({
          items: newItems.map((i) => ({
            productCode: i.productCode,
            setItemNumber: i.setItemNumber || undefined,
            setBarcode: i.setBarcode || undefined,
            setPurchasePrice: i.setPurchasePrice,
            setRetailPrice: i.setRetailPrice,
            isActive: i.isActive ?? true,
          })),
        })
      }

      if (existingItems.length) {
        await batchUpdateSetBarcodes({
          items: existingItems.map((i) => ({
            id: i.id!,
            setBarcode: i.setBarcode || undefined,
          })),
        })
        await batchUpdateSetPrices({
          items: existingItems.map((i) => ({
            id: i.id!,
            setPurchasePrice: i.setPurchasePrice,
            setRetailPrice: i.setRetailPrice,
          })),
        })
      }

      message.success(t('message.saveSuccess', '保存成功'))
      if (setCodeProduct) {
        const result = await getGridData({ productCode: setCodeProduct.productCode, pageIndex: 1, pageSize: 200 })
        setSetCodeData(result?.items ?? [])
      }
    } catch {
      message.error(t('posAdmin.products.saveSetBarcodeFailed', '保存套装条码失败'))
    }
  }

  const handleOpenCategoryModal = () => {
    if (!ensureCanManagePosProducts()) return
    setEditingCategory(null)
    categoryEditForm.resetFields()
    setCategoryModalVisible(true)
  }

  const handleEditCategory = (node: ProductCategoryDto) => {
    if (!ensureCanManagePosProducts()) return
    setEditingCategory(node)
    categoryEditForm.setFieldsValue({
      name: node.name,
      parentGuid: getCategoryValueFromGuid(node.parentGuid, categoryTree),
      sortOrder: node.sortOrder,
    })
    setCategoryModalVisible(true)
  }

  const handleSaveCategory = async () => {
    if (!ensureCanManagePosProducts()) return
    try {
      const values = await categoryEditForm.validateFields()
      const parentGuid = resolveCascaderLeafValue(values.parentGuid)
      if (editingCategory && parentGuid && categoryParentDisabledGuids.has(parentGuid)) {
        message.error(t('posAdmin.products.invalidParentCategory', '父分类不能选择当前分类或其子分类'))
        return
      }
      if (editingCategory) {
        await updateProductCategory(editingCategory.guid, {
          name: values.name,
          parentGuid,
          sortOrder: values.sortOrder,
        })
        message.success(t('posAdmin.products.updateCategorySuccess', '更新分类成功'))
      } else {
        await createProductCategory({
          name: values.name,
          parentGuid,
          sortOrder: values.sortOrder,
        })
        message.success(t('posAdmin.products.createCategorySuccess', '创建分类成功'))
      }
      setCategoryModalVisible(false)
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
    } catch {
      message.error(t('posAdmin.products.saveCategoryFailed', '保存分类失败'))
    }
  }

  const handleDeleteCategory = async (guid: string) => {
    if (!ensureCanManagePosProducts()) return
    try {
      await deleteProductCategory(guid)
      message.success(t('posAdmin.products.deleteCategorySuccess', '删除分类成功'))
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
    } catch {
      message.error(t('posAdmin.products.deleteCategoryFailed', '删除分类失败'))
    }
  }

  const handleCheckIntegrity = async () => {
    setIntegrityLoading(true)
    setIntegrityResult(null)
    try {
      const result = await checkIntegrity()
      setIntegrityResult(result)
      const summary = buildProductIntegritySummary(result)
      if (summary.issueCount === 0) {
        message.success(t('posAdmin.products.integrityCheckPassed', '数据一致性检查通过，没有发现问题'))
      } else {
        message.warning(t('posAdmin.products.foundIssues', '发现 {{count}} 个问题', { count: summary.issueCount }))
      }
    } catch {
      message.error(t('posAdmin.products.integrityCheckFailed', '一致性检查失败'))
    } finally {
      setIntegrityLoading(false)
    }
  }

  const handleFixIntegrity = async () => {
    if (!ensureCanManagePosProducts()) return
    setFixLoading(true)
    try {
      const result: ProductIntegrityFixResultDto = await fixIntegrity({
        fixStoreRetailPrice: true,
        fixStoreMultiCodeProduct: true,
        fixProductSetCode: true,
        dryRun: false,
      })
      const summary = buildProductIntegrityFixSummary(result)
      message.success(t('posAdmin.products.fixComplete', '修复完成：删除 {{deleted}}，新增 {{added}}，错误 {{errors}}', {
        deleted: summary.deletedCount,
        added: summary.addedCount,
        errors: summary.errorCount,
      }))
      if (summary.errors.length) {
        Modal.error({
          title: t('posAdmin.products.partialFixError', '部分修复错误'),
          content: summary.errors.join('\n'),
        })
      }
      await handleCheckIntegrity()
    } catch {
      message.error(t('posAdmin.products.fixFailed', '修复失败'))
    } finally {
      setFixLoading(false)
    }
  }

  const buildCategoryTreeData = (nodes: ProductCategoryDto[]): any[] => {
    return nodes.map((node) => ({
      key: node.guid,
      title: (
        <Space>
          <span>{node.name}</span>
          <Button size="small" type="link" icon={<EditOutlined />} onClick={() => handleEditCategory(node)} />
          <Popconfirm title={t('posAdmin.products.confirmDeleteCategory', '确认删除该分类？')} onConfirm={() => handleDeleteCategory(node.guid)} okText={t('common.delete', '删除')} cancelText={t('common.cancel', '取消')}>
            <Button size="small" type="link" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
      children: node.children?.length ? buildCategoryTreeData(node.children) : [],
    }))
  }

  const columns: ColumnsType<ProductRow> = [
    {
      title: t('posAdmin.invoiceDetail.seqNo', '序号'),
      width: 48,
      align: 'right',
      fixed: 'left',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('posAdmin.invoiceDetail.itemNumber', '货号'),
      dataIndex: 'itemNumber',
      key: 'itemNumber',
      width: 116,
      fixed: 'left',
      sorter: true,
      sortOrder: sortBy === 'itemNumber' ? sortOrder : undefined,
      ...textFilterProps('itemNumber', t('posAdmin.invoiceDetail.itemNumber', '货号')),
      render: (v: string, record) => (
        <Space size={4} className="pos-products-code-cell">
          <a onClick={() => copyTextToClipboard(v || record.productCode)}>{v || record.productCode}</a>
          <Tooltip title={t('common.copy')}>
            <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => copyTextToClipboard(v || record.productCode)} />
          </Tooltip>
        </Space>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.image', '图片'),
      dataIndex: 'productImage',
      width: 52,
      align: 'center',
      render: (v: string) =>
        v ? (
          <Image className="pos-products-image-cell" src={v} width={34} height={34} style={{ objectFit: 'contain' }} preview={{ mask: '' }} fallback={PRODUCT_IMAGE_FALLBACK} />
        ) : (
          <span className="pos-products-image-cell">-</span>
        ),
    },
    {
      title: t('posAdmin.invoiceDetail.barcode', '条码'),
      dataIndex: 'barcode',
      key: 'barcode',
      width: 132,
      sorter: true,
      sortOrder: sortBy === 'barcode' ? sortOrder : undefined,
      ...textFilterProps('barcode', t('posAdmin.invoiceDetail.barcode', '条码')),
      render: (v: string) => (
        <div className="pos-products-barcode-cell">
          <BarcodePreview
            value={v}
            compactCopy
            align="left"
            className="pos-products-barcode-preview"
            gap={2}
            options={{ height: 22, width: 1, margin: 0 }}
            textMaxWidth={104}
            textNoWrap
          />
        </div>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.productName', '商品名称'),
      dataIndex: 'productName',
      key: 'productName',
      width: 180,
      sorter: true,
      sortOrder: sortBy === 'productName' ? sortOrder : undefined,
      ...textFilterProps('productName', t('posAdmin.invoiceDetail.productName', '商品名称')),
      render: (v: string) => (
        <div className="pos-products-name-cell" title={v}>
          {v}
        </div>
      ),
    },
    {
      title: t('posAdmin.products.productCode', '商品编码'),
      dataIndex: 'productCode',
      key: 'productCode',
      width: 86,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'productCode' ? sortOrder : undefined,
      ...textFilterProps('productCode', t('posAdmin.products.productCode', '商品编码')),
      render: (v: string) => (
        <Tooltip title={t('posAdmin.products.copyProductCode', '复制商品编码')}>
          <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => copyTextToClipboard(v)} />
        </Tooltip>
      ),
    },
    {
      title: t('posAdmin.productPrice.supplier', '供应商'),
      dataIndex: 'localSupplierName',
      key: 'localSupplierCode',
      width: 110,
      sorter: true,
      sortOrder: sortBy === 'localSupplierCode' ? sortOrder : undefined,
      ...enumFilterProps('localSupplierCode', supplierColumnFilterOptions),
      render: (v: string, record) => {
        const supplierName = v || supplierNameMap.get(record.localSupplierCode || '') || record.localSupplierCode || '-'

        return (
          <div
            className="pos-products-supplier-cell"
            title={supplierName}
          >
            {supplierName}
          </div>
        )
      },
    },
    {
      title: t('posAdmin.products.categoryGuid', '分类'),
      dataIndex: 'categoryName',
      key: 'categoryGuid',
      width: 90,
      sorter: true,
      sortOrder: sortBy === 'categoryGuid' ? sortOrder : undefined,
      ...enumFilterProps('categoryGuid', categoryColumnFilterOptions),
      render: (v: string) => v || '-',
    },
    {
      title: t('posAdmin.invoiceDetail.purchasePrice', '进货价'),
      dataIndex: 'purchasePrice',
      width: 84,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'purchasePrice' ? sortOrder : undefined,
      ...numberFilterProps('purchasePrice'),
      render: (v: number) => <span className="pos-products-numeric-cell">{v != null ? Number(v).toFixed(2) : '-'}</span>,
    },
    {
      title: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
      dataIndex: 'retailPrice',
      width: 84,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'retailPrice' ? sortOrder : undefined,
      ...numberFilterProps('retailPrice'),
      render: (v: number) => <span className="pos-products-numeric-cell">{v != null ? Number(v).toFixed(2) : '-'}</span>,
    },
    {
      title: t('posAdmin.products.autoPricing', '自动定价'),
      dataIndex: 'isAutoPricing',
      width: 92,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'isAutoPricing' ? sortOrder : undefined,
      ...enumFilterProps('isAutoPricing', booleanColumnFilterOptions),
      // 主表展示商品主档的自动定价状态，便于和价格字段一起核对。
      render: (value: boolean | undefined) => (
        <Tag color={value ? 'green' : 'default'}>
          {value ? t('common.yes', '是') : t('common.no', '否')}
        </Tag>
      ),
    },
    {
      title: t('posAdmin.cashierUsers.status', '状态'),
      dataIndex: 'isActive',
      width: 72,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'isActive' ? sortOrder : undefined,
      ...enumFilterProps('isActive', activeColumnFilterOptions),
      render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? t('posAdmin.products.enable', '启用') : t('posAdmin.products.disable', '禁用')}</Tag>,
    },
    {
      title: t('posAdmin.products.productTypeLabel', '商品类型'),
      dataIndex: 'productType',
      width: 88,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'productType' ? sortOrder : undefined,
      ...enumFilterProps('productType', productTypeColumnFilterOptions),
      render: (v: number | undefined) => (
        <Tag color={getProductTypeColor(v)}>
          {getProductTypeLabel(v)}
        </Tag>
      ),
    },
    {
      title: t('posAdmin.products.barcodeRecordCount', '条码记录'),
      dataIndex: 'setCount',
      width: 74,
      align: 'center',
      render: (v: number | undefined, record) => {
        const count = Number(v ?? 0)
        if (!isBarcodeManagedProduct(record.productType)) {
          return <span>0</span>
        }

        const typeLabel = getProductTypeLabel(record.productType)
        const content = canManagePosProducts ? (
          <Button type="link" size="small" onClick={() => openSetCodeManager(record)}>
            {count}
          </Button>
        ) : (
          <span>{count}</span>
        )

        return (
          <Tooltip title={t('posAdmin.products.barcodeRecordTooltip', '{{type}} ({{count}} 条记录)', { type: typeLabel, count })}>
            {content}
          </Tooltip>
        )
      },
    },
    {
      title: t('posAdmin.products.storeRecords', '分店记录'),
      dataIndex: 'storeRecordCount',
      width: 88,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'storeRecordCount' ? sortOrder : undefined,
      ...numberFilterProps('storeRecordCount'),
      render: (v: number | undefined, record) => {
        const count = Number(v ?? 0)
        return count > 0 && canManageStoreProducts ? (
          <Button type="link" size="small" onClick={() => openStoreRecords(record)}>
            {count}
          </Button>
        ) : (
          <span>{count}</span>
        )
      },
    },
    {
      title: t('column.createTime', '创建时间'),
      dataIndex: 'createdAt',
      width: 108,
      sorter: true,
      sortOrder: sortBy === 'createdAt' ? sortOrder : undefined,
      ...dateFilterProps('createdAt'),
      render: (v: string) => v ? (
        <span className="pos-products-date-cell">
          <span>{dayjs(v).format('YYYY-MM-DD')}</span>
          <span>{dayjs(v).format('HH:mm')}</span>
        </span>
      ) : '-',
    },
    {
      title: t('posAdmin.productPrice.updatedAt', '更新时间'),
      dataIndex: 'updatedAt',
      width: 108,
      sorter: true,
      sortOrder: sortBy === 'updatedAt' ? sortOrder : undefined,
      ...dateFilterProps('updatedAt'),
      render: (v: string) => v ? (
        <span className="pos-products-date-cell">
          <span>{dayjs(v).format('YYYY-MM-DD')}</span>
          <span>{dayjs(v).format('HH:mm')}</span>
        </span>
      ) : '-',
    },
    {
      title: t('column.action'),
      key: 'actions',
      width: 140,
      fixed: 'right',
      render: (_, record) => (
        <Space>
          {canManagePosProducts ? (
            <>
              <Button type="link" size="small" onClick={() => openEdit(record)}>
                {t('posAdmin.products.edit', '编辑')}
              </Button>
              {isBarcodeManagedProduct(record.productType) && (
                <Button type="link" size="small" onClick={() => openSetCodeManager(record)}>
                  {normalizeProductType(record.productType) === 2
                    ? t('posAdmin.products.multiBarcodeManagement', '多码管理')
                    : t('posAdmin.products.setManagement', '套装管理')}
                </Button>
              )}
            </>
          ) : (
            <Button type="link" size="small" disabled>
              {t('posAdmin.products.readOnly', '只读')}
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('posAdmin.products.title')}
      subtitle={t('posAdmin.products.subtitle', { count: total })}
      extra={
        <Space>
          <Button icon={<SafetyCertificateOutlined />} onClick={() => { setIntegrityVisible(true); setIntegrityResult(null) }}>
            {t('posAdmin.products.integrityCheck', '一致性检查')}
          </Button>
          {canManagePosProducts && (
            <Button icon={<SettingOutlined />} onClick={handleOpenCategoryModal}>
              {t('posAdmin.products.categoryManagement', '分类管理')}
            </Button>
          )}
        </Space>
      }
    >
      <div
        ref={wrapRef}
        style={{
          height: 'calc(100vh - 200px)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div ref={toolbarRef} style={{ padding: '12px 0' }}>
          <Space wrap>
            <Input
              allowClear
              placeholder={t('posAdmin.products.searchPlaceholder')}
              style={{ width: 260 }}
              value={keywordInput}
              onChange={(event) => setKeywordInput(event.target.value)}
              onPressEnter={handleSearch}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder={t('posAdmin.products.supplierPlaceholder', '供应商')}
              style={{ width: 200 }}
              value={supplierCodeInput}
              onChange={setSupplierCodeInput}
              options={supplierOptions}
            />
            <Cascader
              allowClear
              placeholder={t('posAdmin.products.categoryPlaceholder', '分类')}
              style={{ width: 200 }}
              value={getCategoryValueFromGuid(categoryGuidInput, categoryTree)}
              onChange={(v) => setCategoryGuidInput(v?.[v.length - 1])}
              options={buildCategoryCascaderOptions(categoryTree)}
              disabled={categoryLoadFailed}
              changeOnSelect
            />
            <Select
              allowClear
              placeholder={t('posAdmin.products.statusPlaceholder', '状态')}
              style={{ width: 100 }}
              value={isActiveFilterInput}
              onChange={setIsActiveFilterInput}
              options={[
                { label: t('posAdmin.products.enable', '启用'), value: true },
                { label: t('posAdmin.products.disable', '禁用'), value: false },
              ]}
            />
            <Select
              allowClear
              placeholder={t('posAdmin.products.setPlaceholder', '套装')}
              style={{ width: 100 }}
              value={isSetFilterInput}
              onChange={setIsSetFilterInput}
              options={[
                { label: t('posAdmin.products.setProduct', '套装'), value: true },
                { label: t('posAdmin.products.normalProduct', '单品'), value: false },
              ]}
            />
            <Select
              placeholder={t('posAdmin.products.storeRecordFilterPlaceholder', '分店记录')}
              style={{ width: 120 }}
              value={storeRecordCountModeInput}
              className={hasAppliedStoreRecordCountFilter ? 'store-record-filter-active' : undefined}
              onChange={(value) => setStoreRecordCountModeInput(value)}
              options={[
                { label: t('posAdmin.products.storeRecordFilterAll', '全部'), value: 'all' },
                { label: t('posAdmin.products.storeRecordFilterHasRecords', '有记录'), value: 'hasRecords' },
                { label: t('posAdmin.products.storeRecordFilterNoRecords', '无记录'), value: 'noRecords' },
                { label: t('posAdmin.products.storeRecordFilterCustom', '自定义范围'), value: 'custom' },
              ]}
            />
            {isStoreRecordCountCustomMode ? (
              <Space.Compact>
                <InputNumber
                  min={0}
                  precision={0}
                  placeholder={t('posAdmin.products.storeRecordFilterMin', '最小数量')}
                  style={{ width: 110 }}
                  value={storeRecordCountMinInput}
                  onChange={(value) => setStoreRecordCountMinInput(value ?? undefined)}
                />
                <InputNumber
                  min={0}
                  precision={0}
                  placeholder={t('posAdmin.products.storeRecordFilterMax', '最大数量')}
                  style={{ width: 110 }}
                  value={storeRecordCountMaxInput}
                  onChange={(value) => setStoreRecordCountMaxInput(value ?? undefined)}
                />
              </Space.Compact>
            ) : null}
            <Button type="primary" icon={<SearchOutlined />} onClick={handleSearch}>
              {t('common.query', '查询')}
            </Button>
            <Button icon={<ReloadOutlined />} onClick={handleReset}>
              {t('posAdmin.products.reset', '重置')}
            </Button>
            <Divider type="vertical" style={{ height: 32 }} />
            {isAdmin && (
              <>
                <Button
                  icon={<CloudDownloadOutlined />}
                  loading={hqSyncSubmitting && hqSyncMode === 'full'}
                  disabled={hqSyncSubmitting}
                  onClick={() => openHqSyncModal('full')}
                >
                  {activeHqSyncJob ? t('posAdmin.products.hqSyncInProgress', '同步中') : t('posAdmin.products.fullSyncFromHQ', '全量同步')}
                </Button>
                <Button
                  icon={<CloudSyncOutlined />}
                  loading={hqSyncSubmitting && hqSyncMode === 'incremental'}
                  disabled={hqSyncSubmitting}
                  onClick={() => openHqSyncModal('incremental')}
                >
                  {activeHqSyncJob ? t('posAdmin.products.hqSyncInProgress', '同步中') : t('posAdmin.products.incrementalSyncFromHQ', '增量同步')}
                </Button>
                <Button
                  icon={<CloudDownloadOutlined />}
                  loading={selectedFromHqLoading}
                  disabled={!selectedRowKeys.length || selectedFromHqLoading}
                  onClick={handleSyncSelectedFromHq}
                >
                  {t('posAdmin.products.syncSelectedFromHq', '从HQ同步选中')}
                </Button>
              </>
            )}
            {canCreateStoreProducts && (
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
                {t('posAdmin.products.createProduct', '创建商品')}
              </Button>
            )}
            {canManagePosProducts && (
              <>
                <Button
                  icon={<CloudUploadOutlined />}
                  loading={pushToHqLoading}
                  disabled={!selectedRowKeys.length || pushToHqLoading}
                  onClick={handlePushToHq}
                >
                  {t('posAdmin.products.pushToHq', '发送到HQ')}
                </Button>
                <Button icon={<CloudUploadOutlined />} onClick={openSyncToStoreModal}>
                  {t('posAdmin.products.syncToStore', '同步到分店')}
                </Button>
                <Button icon={<FileImageOutlined />} onClick={openImageBatch}>
                  {t('posAdmin.products.batchImageUpdate', '图片批量修改')}
                </Button>
                <Button
                  icon={<TranslationOutlined />}
                  loading={translating}
                  disabled={!selectedRowKeys.length || translating}
                  onClick={handleBatchTranslate}
                >
                  {t('posAdmin.products.batchTranslate', '批量翻译')}
                </Button>
                <Button onClick={openBatchEdit} disabled={!selectedRowKeys.length || categoryLoadFailed}>
                  {t('posAdmin.products.batchEdit', '批量编辑')}
                </Button>
                <Popconfirm title={t('posAdmin.products.confirmBatchEnable', '确认启用选中的商品？')} onConfirm={() => handleBatchEnable(true)}>
                  <Button disabled={!selectedRowKeys.length}>{t('posAdmin.products.batchEnable', '批量启用')}</Button>
                </Popconfirm>
                <Popconfirm title={t('posAdmin.products.confirmBatchDisable', '确认禁用选中的商品？')} onConfirm={() => handleBatchEnable(false)}>
                  <Button danger disabled={!selectedRowKeys.length}>
                    {t('posAdmin.products.batchDisable', '批量禁用')}
                  </Button>
                </Popconfirm>
              </>
            )}
          </Space>
        </div>

        <div style={{ flex: 1, minHeight: 0 }}>
          <Table
            virtual
            className="pos-products-compact-table"
            size="small"
            rowKey="key"
            loading={loading}
            dataSource={data}
            columns={columns}
            pagination={false}
            scroll={{ x: 1640, y: tableScrollY }}
            rowSelection={{
              selectedRowKeys,
              onChange: (keys) => setSelectedRowKeys(keys),
              columnWidth: 40,
            }}
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            onChange={(_pagination, filters, sorter) => {
              const nextColumnFilters = normalizeProductTableFilters(filters as Record<string, FilterValue | null>)
              setColumnFilters(nextColumnFilters)
              const s = Array.isArray(sorter) ? sorter[0] : sorter
              const field = String(s?.columnKey || s?.field || s?.column?.dataIndex || 'productCode')
              const order = s?.order as 'ascend' | 'descend' | undefined
              if (order) {
                setSortBy(field)
                setSortOrder(order)
              } else {
                setSortBy('productCode')
                setSortOrder('ascend')
              }
              // 列头筛选和排序都属于重新查询，必须回到第一页并清空跨条件选择。
              setPage(1)
              setSelectedRowKeys([])
            }}
          />
        </div>

        <div
          ref={pagerRef}
          style={{
            padding: '8px 0',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            width: '100%',
            background: '#fff',
            zIndex: 1,
          }}
        >
          <Space>
            <span>
              {t('posAdmin.products.selectedCount', '已选 {{count}} 项', { count: selectedRowKeys.length })}
            </span>
            {selectedRowKeys.length > 0 && (
              <Button type="link" size="small" onClick={() => setSelectedRowKeys([])}>
                {t('posAdmin.products.clearSelection', '清空选择')}
              </Button>
            )}
          </Space>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            onChange={(p, ps) => {
              setPage(p)
              setPageSize(ps)
            }}
            showSizeChanger
            responsive={false}
            pageSizeOptions={[10, 20, 50, 100, 200]}
            showTotal={(total) => t('posAdmin.products.totalCount', '共 {{count}} 条', { count: total })}
          />
        </div>
      </div>

      <Modal
        open={createVisible}
        title={t('posAdmin.products.createProduct', '创建商品')}
        onCancel={() => setCreateVisible(false)}
        onOk={handleCreateSave}
        confirmLoading={createSubmitting}
        width={720}
        destroyOnHidden
      >
        <Form form={createForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="productName" label={t('posAdmin.products.productName', '商品名称')} rules={[{ required: true, message: t('posAdmin.products.inputProductName', '请输入商品名称') }]}>
            <Input />
          </Form.Item>
          <Form.Item name="productImage" label={t('posAdmin.invoiceDetail.image', '图片')}>
            <Input allowClear placeholder={t('posAdmin.products.productImageUrlPlaceholder', '请输入商品图片 URL')} />
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="barcode" label={t('posAdmin.products.barcodeLabel', '条码')} rules={[{ required: true, message: t('posAdmin.products.inputBarcode', '请输入条码') }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="itemNumber" label={t('posAdmin.products.addItemNumber', '货号')}>
                <Input />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="localSupplierCode" label={t('posAdmin.products.supplier', '供应商')}>
                <Select allowClear showSearch optionFilterProp="label" options={supplierOptions} placeholder={t('posAdmin.products.selectSupplier', '请选择供应商')} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="productCategoryGUID" label={t('posAdmin.products.category', '分类')}>
                <Cascader
                  allowClear
                  showSearch
                  changeOnSelect
                  options={buildCategoryCascaderOptions(categoryTree)}
                  disabled={categoryLoadFailed}
                  placeholder={t('posAdmin.products.selectCategory', '选择分类')}
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="purchasePrice" label={t('posAdmin.products.purchasePrice', '采购价')} rules={[{ required: true, message: t('posAdmin.products.inputPurchasePrice', '请输入采购价') }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="retailPrice" label={t('posAdmin.products.retailPrice', '零售价')} rules={[{ required: true, message: t('posAdmin.products.inputRetailPrice', '请输入零售价') }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="isAutoPricing" label={t('posAdmin.products.isAutoPricing', '自动定价')} valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren={t('posAdmin.products.yes', '是')} unCheckedChildren={t('posAdmin.products.no', '否')} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="isSpecialProduct" label={t('posAdmin.products.isSpecialProduct', '特殊商品')} valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren={t('posAdmin.products.yes', '是')} unCheckedChildren={t('posAdmin.products.no', '否')} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>

      <Modal
        open={hqSyncVisible}
        title={hqSyncMode === 'full' ? t('posAdmin.products.fullSyncFromHQ', '全量同步') : t('posAdmin.products.incrementalSyncFromHQ', '增量同步')}
        onCancel={() => setHqSyncVisible(false)}
        onOk={handleSyncFromHq}
        confirmLoading={hqSyncSubmitting}
        okText={t('common.confirm', '确定')}
        cancelText={t('common.cancel', '取消')}
        destroyOnHidden
      >
        <Form form={hqSyncForm} layout="vertical">
          {hqSyncMode === 'full' ? (
            <div style={{ color: '#595959' }}>
              {t('posAdmin.products.fullSyncNotice', '全量同步只覆盖商品主表，不同步关联表。')}
            </div>
          ) : (
            <Form.Item
              name="startDate"
              label={t('posAdmin.products.incrementalStartDate', '起始日期')}
              rules={[{ required: true, message: t('posAdmin.products.incrementalStartDateRequired', '请选择增量同步起始日期') }]}
            >
              <DatePicker style={{ width: '100%' }} allowClear={false} format="YYYY-MM-DD" />
            </Form.Item>
          )}
        </Form>
      </Modal>

      <Modal
        open={editVisible}
        title={editingProduct ? t('posAdmin.products.editProductWithCode', '编辑商品 - {{code}}', { code: editingProduct.productCode }) : t('posAdmin.products.editProduct', '编辑商品')}
        onCancel={() => { setEditVisible(false); setEditingProduct(null); resetEditSetCodeState() }}
        onOk={handleEditSave}
        width={900}
        destroyOnHidden
      >
        <Form form={editForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="productName" label={t('posAdmin.products.productName', '商品名称')} rules={[{ required: true, message: t('posAdmin.products.inputProductName', '请输入商品名称') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('posAdmin.invoiceDetail.image', '图片')}>
            <Space align="start" className="pos-products-edit-image-field">
              <Form.Item shouldUpdate={(prev, cur) => prev.productImage !== cur.productImage} noStyle>
                {({ getFieldValue }) => {
                  const productImage = String(getFieldValue('productImage') ?? '').trim()
                  return productImage ? (
                    <Image
                      className="pos-products-edit-image-preview"
                      src={productImage}
                      width={64}
                      height={64}
                      style={{ objectFit: 'contain' }}
                      preview={{ mask: '' }}
                      fallback={PRODUCT_IMAGE_FALLBACK}
                    />
                  ) : (
                    <span className="pos-products-edit-image-preview">-</span>
                  )
                }}
              </Form.Item>
              <Form.Item name="productImage" noStyle>
                <Input allowClear placeholder={t('posAdmin.products.productImageUrlPlaceholder', '请输入商品图片 URL')} />
              </Form.Item>
            </Space>
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item label={t('posAdmin.products.addItemNumber', '货号')}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="itemNumber" noStyle>
                    <Input style={{ flex: 1 }} />
                  </Form.Item>
                  <Button icon={<CopyOutlined />} onClick={() => { const v = editForm.getFieldValue('itemNumber'); if (v) { copyTextToClipboard(v); message.success(t('message.copySuccess', '复制成功')) } }} />
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.products.barcodeLabel', '条码')}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="barcode" noStyle>
                    <Input style={{ flex: 1 }} />
                  </Form.Item>
                  <Button icon={<CopyOutlined />} onClick={() => { const v = editForm.getFieldValue('barcode'); if (v) { copyTextToClipboard(v); message.success(t('posAdmin.products.copySuccess', '复制成功')) } }} />
                </Space.Compact>
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="localSupplierCode" label={t('posAdmin.products.supplier', '供应商')}>
                <Select allowClear showSearch optionFilterProp="label" options={supplierOptions} placeholder={t('posAdmin.products.selectSupplier', '请选择供应商')} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="productType" label={t('posAdmin.products.productTypeLabel', '商品类型')}>
                <Radio.Group onChange={(e) => handleProductTypeChange(e.target.value)}>
                  <Radio.Button value={0}>{t('posAdmin.products.normalProduct', '普通商品')}</Radio.Button>
                  <Radio.Button value={1}>{t('posAdmin.products.setProduct', '套装商品')}</Radio.Button>
                  <Radio.Button value={2}>{t('posAdmin.products.multiBarcodeProduct', '多条码商品')}</Radio.Button>
                </Radio.Group>
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="purchasePrice" label={t('posAdmin.products.purchasePrice', '采购价')} rules={[{ required: true, message: t('posAdmin.products.inputPurchasePrice', '请输入采购价') }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="retailPrice" label={t('posAdmin.products.retailPrice', '零售价')} rules={[{ required: true, message: t('posAdmin.products.inputRetailPrice', '请输入零售价') }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="middlePackageQuantity" label={t('posAdmin.products.middlePackage', '中包数')}>
                <InputNumber min={1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item shouldUpdate={(prev, cur) => prev.purchasePrice !== cur.purchasePrice || prev.retailPrice !== cur.retailPrice} noStyle>
                {({ getFieldValue }) => {
                  const pp = getFieldValue('purchasePrice')
                  const rp = getFieldValue('retailPrice')
                  const rate = pp > 0 ? (rp / pp) : undefined
                  return rate !== undefined ? (
                    <Form.Item label={t('posAdmin.products.currentRate', '当前倍率')}>
                      <Input value={rate.toFixed(2)} disabled />
                    </Form.Item>
                  ) : null
                }}
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="isAutoPricing" label={t('posAdmin.products.isAutoPricing', '自动定价')} valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren={t('posAdmin.products.yes', '是')} unCheckedChildren={t('posAdmin.products.no', '否')} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="isSpecialProduct" label={t('posAdmin.products.isSpecialProduct', '特殊商品')} valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren={t('posAdmin.products.yes', '是')} unCheckedChildren={t('posAdmin.products.no', '否')} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="isActive" label={t('posAdmin.products.productStatusLabel', '是否启用')} valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren={t('posAdmin.products.enable', '启用')} unCheckedChildren={t('posAdmin.products.disable', '禁用')} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="unitWeight" label={t('posAdmin.products.weight', '重量')}>
                <InputNumber min={0} precision={3} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="categoryGuid" label={t('posAdmin.products.category', '分类')}>
                <Cascader
                  allowClear
                  showSearch
                  changeOnSelect
                  options={buildCategoryCascaderOptions(categoryTree)}
                  disabled={categoryLoadFailed}
                  placeholder={t('posAdmin.products.selectCategory', '选择分类')}
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
          </Row>
        </Form>
        {productTypeWatch === 1 && (
          <div style={{ marginTop: 12 }}>
            <Space style={{ marginBottom: 8 }}>
              <Button type="dashed" onClick={editAddSetCodeRow}>{t('posAdmin.products.addBarcodeBtn', '添加条码')}</Button>
              <span style={{ fontSize: 12, color: '#52c41a' }}>{t('posAdmin.products.setBarcodeTip', '套装条码采购价和零售价和主条码不一致')}</span>
            </Space>
            <Table
              rowKey={(r: any) => r.id || r._rowId}
              loading={editSetCodesLoading}
              dataSource={editSetCodes}
              pagination={false}
              size="small"
              locale={{ emptyText: t('posAdmin.products.noSetBarcode', '暂无套装条码') }}
              columns={[
                { title: t('posAdmin.products.setBarcodeLabel', '套装条码 *'), dataIndex: 'setBarcode', width: 220, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return (<Space.Compact style={{ width: '100%' }}><Input style={{ flex: 1 }} value={edit.setBarcode ?? row.setBarcode} placeholder="请输入条码" onChange={(e) => editHandleBarcodeChange(row, e.target.value)} /><Button type="text" size="small" icon={<CopyOutlined />} onClick={() => { const v = editSetPriceEdits[rowId]?.setBarcode ?? row.setBarcode; if (v) { copyTextToClipboard(v); message.success('复制成功') } }} style={{ padding: '0 4px' }} /></Space.Compact>) } },
                { title: t('posAdmin.productPrice.purchasePrice', '采购价'), dataIndex: 'setPurchasePrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setPurchasePrice !== undefined ? edit.setPurchasePrice : row.setPurchasePrice} placeholder="根据零售价自动计算" onChange={(v) => v !== undefined && editHandlePurchasePriceChange(row, v)} /> } },
                { title: t('posAdmin.invoiceDetail.retailPrice', '零售价'), dataIndex: 'setRetailPrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setRetailPrice !== undefined ? edit.setRetailPrice : row.setRetailPrice} onChange={(v) => v !== undefined && editHandleRetailPriceChange(row, v!)} /> } },
                { title: t('posAdmin.cashierUsers.status', '状态'), dataIndex: 'isActive', width: 80, render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? t('posAdmin.products.enable', '启用') : t('posAdmin.products.disable', '禁用')}</Tag> },
                { title: t('column.action'), width: 80, render: (_: any, row: any) => <Button type="link" danger size="small" onClick={() => editDeleteSetCodeRow(row)}>{t('common.delete')}</Button> },
              ]}
            />
          </div>
        )}
        {productTypeWatch === 2 && (
          <div style={{ marginTop: 12 }}>
            <Space style={{ marginBottom: 8 }}>
              <Button type="dashed" onClick={editAddSetCodeRow}>{t('posAdmin.products.addBarcodeBtn', '添加条码')}</Button>
              <span style={{ fontSize: 12, color: '#52c41a' }}>{t('posAdmin.products.multiBarcodeTip', '多条码零售价和采购价和主条码一致')}</span>
            </Space>
            <Table
              rowKey={(r: any) => r.id || r._rowId}
              loading={editSetCodesLoading}
              dataSource={editSetCodes}
              pagination={false}
              size="small"
              locale={{ emptyText: t('posAdmin.products.noMultiBarcodeBarcode', '暂无多码条码') }}
              columns={[
                { title: t('posAdmin.products.multiCodeBarcodeLabel', '多码条码 *'), dataIndex: 'setBarcode', width: 220, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return (<Space.Compact style={{ width: '100%' }}><Input style={{ flex: 1 }} value={edit.setBarcode ?? row.setBarcode} placeholder="请输入条码" onChange={(e) => editHandleBarcodeChange(row, e.target.value)} /><Button type="text" size="small" icon={<CopyOutlined />} onClick={() => { const v = editSetPriceEdits[rowId]?.setBarcode ?? row.setBarcode; if (v) { copyTextToClipboard(v); message.success('复制成功') } }} style={{ padding: '0 4px' }} /></Space.Compact>) } },
                { title: t('posAdmin.productPrice.purchasePrice', '采购价'), dataIndex: 'setPurchasePrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setPurchasePrice !== undefined ? edit.setPurchasePrice : row.setPurchasePrice} onChange={(v) => v !== undefined && editHandlePurchasePriceChange(row, v)} /> } },
                { title: t('posAdmin.invoiceDetail.retailPrice', '零售价'), dataIndex: 'setRetailPrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setRetailPrice !== undefined ? edit.setRetailPrice : row.setRetailPrice} onChange={(v) => v !== undefined && editHandleRetailPriceChange(row, v!)} /> } },
                { title: t('posAdmin.cashierUsers.status', '状态'), dataIndex: 'isActive', width: 80, render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? t('posAdmin.products.enable', '启用') : t('posAdmin.products.disable', '禁用')}</Tag> },
                { title: t('column.action'), width: 80, render: (_: any, row: any) => <Button type="link" danger size="small" onClick={() => editDeleteSetCodeRow(row)}>{t('common.delete')}</Button> },
              ]}
            />
          </div>
        )}
      </Modal>

      <Modal
        open={imageBatchVisible}
        title={t('posAdmin.products.batchImageUpdateTitle', '供应商图片批量修改')}
        onCancel={() => setImageBatchVisible(false)}
        onOk={handleImageBatchSave}
        confirmLoading={imageBatchLoading}
        width={720}
        destroyOnHidden
      >
        <Form form={imageBatchForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="localSupplierCode" label={t('posAdmin.products.supplier', '供应商')} rules={[{ required: true, message: t('posAdmin.products.selectSupplier', '请选择供应商') }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={supplierOptions}
              placeholder={t('posAdmin.products.selectSupplier', '请选择供应商')}
              onChange={(value) => {
                const mode = (imageBatchForm.getFieldValue('mode') || 'supplier') as SupplierImageMode
                setImageBatchTemplate(value, mode)
              }}
            />
          </Form.Item>
          <Form.Item name="mode" label={t('posAdmin.products.imageTemplateMode', '模式')} initialValue="supplier">
            <Radio.Group
              onChange={(event) => {
                setImageBatchTemplate(imageBatchForm.getFieldValue('localSupplierCode'), event.target.value)
              }}
            >
              <Radio.Button value="supplier">{t('posAdmin.products.supplierImageTemplate', '供应商图片基础 URL')}</Radio.Button>
              <Radio.Button value="cos">{t('posAdmin.products.cosImageTemplate', 'COS 地址')}</Radio.Button>
            </Radio.Group>
          </Form.Item>
          <Form.Item name="scope" label={t('posAdmin.products.imageUpdateScope', '更新范围')} initialValue="supplier">
            <Radio.Group>
              <Radio.Button value="supplier">{t('posAdmin.products.imageUpdateSupplierScope', '供应商全部商品')}</Radio.Button>
              <Radio.Button value="selected" disabled={!selectedRowKeys.length}>
                {t('posAdmin.products.imageUpdateSelectedScope', '选中商品 ({{count}})', { count: selectedRowKeys.length })}
              </Radio.Button>
            </Radio.Group>
          </Form.Item>
          <Form.Item
            name="urlTemplate"
            label={t('posAdmin.suppliers.imageBaseUrl', '图片基础 URL')}
            rules={[
              {
                validator: async (_, value) => {
                  const error = validateSupplierImageTemplate(value || '')
                  if (error) return Promise.reject(new Error(error))
                  return Promise.resolve()
                },
              },
            ]}
          >
            <Input.TextArea
              rows={2}
              placeholder="https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg"
            />
          </Form.Item>
          <Form.Item name="saveSupplierImageBaseUrl" label=" " colon={false} valuePropName="checked">
            <Checkbox>{t('posAdmin.products.saveImageBaseUrlToSupplier', '保存到供应商信息')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateTargets" label={t('posAdmin.products.imageUpdateTargets', '写入目标')}>
            <Checkbox.Group
              options={[
                { label: 'Hbweb', value: 'hbweb' },
                { label: 'HQ', value: 'hq' },
              ]}
            />
          </Form.Item>
          <div style={{ color: '#666', paddingLeft: 150 }}>
            <p style={{ marginBottom: 4 }}>
              {t('posAdmin.products.batchImageScopeTip', '供应商全部商品会为该供应商未删除且图片为空的商品补图；选中商品只更新当前勾选商品。Hbweb/HQ 已有图片分别跳过。')}
            </p>
            {imageBatchSupplier?.imageBaseUrl ? (
              <p style={{ marginBottom: 4 }}>
                {t('posAdmin.products.savedSupplierImageBaseUrl', '已保存供应商图片基础 URL。')}
              </p>
            ) : null}
            {imageBatchPreviewUrl ? (
              <p style={{ marginBottom: 0, wordBreak: 'break-all' }}>
                {t('posAdmin.products.imagePreviewUrl', '预览')}：{imageBatchPreviewUrl}
              </p>
            ) : null}
          </div>
        </Form>
      </Modal>

      <Modal
        open={batchEditVisible}
        title={t('posAdmin.products.batchEditProduct', '批量编辑 ({{count}} 个商品)', { count: selectedRowKeys.length })}
        onCancel={() => setBatchEditVisible(false)}
        onOk={handleBatchEditSave}
        width={600}
        destroyOnHidden
      >
        <Form form={batchEditForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="categoryGuid" label={t('posAdmin.products.productCategoryLabel', '商品分类')}>
            <Cascader
              allowClear
              showSearch
              changeOnSelect
              options={buildCategoryCascaderOptions(categoryTree)}
              disabled={categoryLoadFailed}
              placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')}
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item name="localSupplierCode" label={t('posAdmin.products.supplier', '供应商')}>
            <Select allowClear showSearch optionFilterProp="label" options={supplierOptions} placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} />
          </Form.Item>
          <Form.Item name="purchasePrice" label={t('posAdmin.products.purchasePrice', '采购价')}>
            <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} />
          </Form.Item>
          <Form.Item name="retailPrice" label={t('posAdmin.products.retailPrice', '零售价')}>
            <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} />
          </Form.Item>
          <Form.Item name="middlePackageQuantity" label={t('posAdmin.products.middlePackage', '中包数')}>
            <InputNumber min={1} style={{ width: '100%' }} placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} />
          </Form.Item>
          <Form.Item name="isAutoPricing" label={t('posAdmin.products.isAutoPricing', '自动定价')}>
            <Select placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} allowClear>
              <Select.Option value={true}>{t('posAdmin.products.yes', '是')}</Select.Option>
              <Select.Option value={false}>{t('posAdmin.products.no', '否')}</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="isSpecialProduct" label={t('posAdmin.products.isSpecialProduct', '特殊商品')}>
            <Select placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} allowClear>
              <Select.Option value={true}>{t('posAdmin.products.yes', '是')}</Select.Option>
              <Select.Option value={false}>{t('posAdmin.products.no', '否')}</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="isActive" label={t('posAdmin.products.productStatusLabel', '是否启用')}>
            <Select placeholder={t('posAdmin.products.leaveEmpty', '留空不修改')} allowClear>
              <Select.Option value={true}>{t('posAdmin.products.enable', '启用')}</Select.Option>
              <Select.Option value={false}>{t('posAdmin.products.disable', '禁用')}</Select.Option>
            </Select>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={syncToStoreVisible}
        title={t('posAdmin.products.syncToStoreTitle', '同步到分店 (已选 {{count}} 个商品)', { count: selectedRowKeys.length })}
        onCancel={() => setSyncToStoreVisible(false)}
        onOk={handleSyncToStores}
        confirmLoading={syncToStoreLoading}
        width={600}
        destroyOnHidden
      >
        <Form form={syncToStoreForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item label={t('posAdmin.products.syncFields', '同步字段')} required>
            <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
              <Checkbox>{t('posAdmin.products.purchasePrice', '进货价')}</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
              <Checkbox>{t('posAdmin.products.retailPrice', '零售价')}</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
              <Checkbox>{t('posAdmin.products.isAutoPricing', '是否自动定价')}</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
              <Checkbox>{t('posAdmin.products.isSpecialProduct', '是否特殊商品')}</Checkbox>
            </Form.Item>
          </Form.Item>
          <Form.Item name="storeCodes" label={t('posAdmin.products.targetStore', '目标分店')} rules={[{ required: true, message: t('posAdmin.products.targetStoreRequired', '请选择目标分店') }]}>
            <Select
              mode="multiple"
              showSearch
              allowClear
              optionFilterProp="label"
              placeholder={t('posAdmin.products.selectTargetStore', '选择目标分店')}
              options={storeOptions}
              onChange={(values) => setSyncSelectAll(values.length === storeOptions.length)}
            />
          </Form.Item>
          <Form.Item label=" " colon={false}>
            <Checkbox
              checked={syncSelectAll}
              onChange={(e) => {
                const checked = e.target.checked
                setSyncSelectAll(checked)
                if (checked) {
                  syncToStoreForm.setFieldValue('storeCodes', storeOptions.map((s) => s.value))
                } else {
                  syncToStoreForm.setFieldValue('storeCodes', [])
                }
              }}
            >
              {t('posAdmin.products.selectAllStores', '全选所有分店 ({{count}} 个)', { count: storeOptions.length })}
            </Checkbox>
          </Form.Item>
          <div style={{ marginTop: 16, color: '#888' }}>
            <p>
              {t('posAdmin.products.selectedProducts', '已选商品: ')}<strong>{selectedRowKeys.length}</strong> {t('posAdmin.products.unit', '个')}
            </p>
            <p>{t('posAdmin.products.copyExplanation', '说明：')}</p>
            <ul style={{ margin: 0, paddingLeft: 20 }}>
              <li>{t('posAdmin.products.description1', '如果分店不存在该商品，将创建包含所有字段的新记录')}</li>
              <li>{t('posAdmin.products.description2', '如果分店已存在该商品，将只更新选中的字段')}</li>
            </ul>
          </div>
        </Form>
      </Modal>

      <Modal
        open={storeRecordsVisible}
        title={t('posAdmin.products.storeRecordsTitle', '分店记录 - {{product}}', {
          product: storeRecordsProduct?.itemNumber || storeRecordsProduct?.productName || storeRecordsProduct?.productCode || '',
        })}
        onCancel={closeStoreRecords}
        footer={[
          <Button
            key="batch-edit"
            type="primary"
            onClick={openStoreRecordBatchEdit}
            disabled={!canEditStoreProducts || !storeRecordSelectedRowKeys.length}
          >
            {t('posAdmin.products.batchUpdateStoreRecords', '批量修改')}
          </Button>,
          <Button key="close" onClick={closeStoreRecords}>
            {t('common.close', '关闭')}
          </Button>,
        ]}
        width={1100}
      >
        <Table
          rowKey={(record) => `${record.storeCode || ''}-${record.storeProductCode || ''}`}
          loading={storeRecordsLoading}
          dataSource={storeRecordsData}
          pagination={false}
          locale={{ emptyText: t('posAdmin.products.noStoreRecords', '暂无分店记录') }}
          scroll={{ x: 1000, y: 420 }}
          rowSelection={{
            selectedRowKeys: storeRecordSelectedRowKeys,
            onChange: (keys) => setStoreRecordSelectedRowKeys(keys),
            columnWidth: 40,
          }}
          columns={[
            { title: t('common.storeCode', '分店代码'), dataIndex: 'storeCode', width: 110, sorter: compareProductStoreRecordsByStoreCode },
            {
              title: t('common.storeName', '分店名称'),
              dataIndex: 'storeName',
              width: 160,
              sorter: compareProductStoreRecordsByName,
              render: (value: string) => value || '-',
            },
            { title: t('posAdmin.products.storeProductCode', '分店商品编码'), dataIndex: 'storeProductCode', width: 160, sorter: compareProductStoreRecordsByStoreProductCode, render: (value: string) => value || '-' },
            { title: t('posAdmin.invoiceDetail.purchasePrice', '进货价'), dataIndex: 'purchasePrice', width: 100, align: 'right' as const, sorter: compareProductStoreRecordsByPurchasePrice, render: (value: number) => value != null ? Number(value).toFixed(2) : '-' },
            { title: t('posAdmin.invoiceDetail.retailPrice', '零售价'), dataIndex: 'storeRetailPriceValue', width: 100, align: 'right' as const, sorter: compareProductStoreRecordsByRetailPrice, render: (value: number) => value != null ? Number(value).toFixed(2) : '-' },
            { title: t('posAdmin.productPrice.discountRate', '折扣率'), dataIndex: 'discountRate', width: 100, align: 'right' as const, sorter: compareProductStoreRecordsByDiscountRate, render: (value: number) => value != null ? Number(value).toFixed(4) : '-' },
            { title: t('posAdmin.products.autoPricing', '自动定价'), dataIndex: 'isAutoPricing', width: 100, align: 'center' as const, sorter: compareProductStoreRecordsByAutoPricing, render: (value: boolean) => value ? t('common.yes', '是') : t('common.no', '否') },
            { title: t('posAdmin.products.specialProduct', '特殊商品'), dataIndex: 'isSpecialProduct', width: 100, align: 'center' as const, sorter: compareProductStoreRecordsBySpecialProduct, render: (value: boolean) => value ? t('common.yes', '是') : t('common.no', '否') },
            {
              title: t('posAdmin.cashierUsers.status', '状态'),
              dataIndex: 'isActive',
              width: 90,
              align: 'center' as const,
              sorter: compareProductStoreRecordsByActive,
              render: (value: boolean) => <Tag color={value ? 'green' : 'red'}>{value ? t('posAdmin.products.enable', '启用') : t('posAdmin.products.disable', '禁用')}</Tag>,
            },
            { title: t('posAdmin.productPrice.updatedAt', '更新时间'), dataIndex: 'updatedAt', width: 160, sorter: compareProductStoreRecordsByUpdatedAt, render: (value: string) => value ? dayjs(value).format('YYYY-MM-DD HH:mm') : '-' },
            { title: t('posAdmin.products.updatedBy', '更新人'), dataIndex: 'updatedBy', width: 120, sorter: compareProductStoreRecordsByUpdatedBy, render: (value: string) => value || '-' },
          ]}
        />
      </Modal>

      <Modal
        open={storeRecordBatchEditVisible}
        title={t('posAdmin.products.batchUpdateStoreRecords', '批量修改')}
        onCancel={() => {
          storeRecordBatchEditForm.resetFields()
          setStoreRecordBatchEditVisible(false)
        }}
        onOk={handleStoreRecordBatchEditSave}
        confirmLoading={storeRecordBatchUpdating}
        width={720}
        destroyOnHidden
      >
        <Form form={storeRecordBatchEditForm} layout="vertical">
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item label={t('posAdmin.invoiceDetail.purchasePrice', '进货价')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updatePurchasePrice" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}>
                    {({ getFieldValue }) => (
                      <Form.Item name="purchasePrice" noStyle>
                        <InputNumber
                          min={0}
                          precision={2}
                          disabled={!getFieldValue('updatePurchasePrice')}
                          placeholder={t('posAdmin.invoiceDetail.purchasePrice', '进货价')}
                          style={{ width: '100%' }}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.invoiceDetail.retailPrice', '零售价')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updateStoreRetailPriceValue" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateStoreRetailPriceValue !== cur.updateStoreRetailPriceValue}>
                    {({ getFieldValue }) => (
                      <Form.Item name="storeRetailPriceValue" noStyle>
                        <InputNumber
                          min={0}
                          precision={2}
                          disabled={!getFieldValue('updateStoreRetailPriceValue')}
                          placeholder={t('posAdmin.invoiceDetail.retailPrice', '零售价')}
                          style={{ width: '100%' }}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.productPrice.discountRate', '折扣率')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updateDiscountRate" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}>
                    {({ getFieldValue }) => (
                      <Form.Item name="discountRate" noStyle>
                        <InputNumber
                          min={0}
                          precision={4}
                          disabled={!getFieldValue('updateDiscountRate')}
                          placeholder={t('posAdmin.productPrice.discountRate', '折扣率')}
                          style={{ width: '100%' }}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.products.autoPricing', '自动定价')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updateIsAutoPricing" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateIsAutoPricing !== cur.updateIsAutoPricing}>
                    {({ getFieldValue }) => (
                      <Form.Item name="isAutoPricing" noStyle>
                        <Select
                          disabled={!getFieldValue('updateIsAutoPricing')}
                          placeholder={t('posAdmin.products.autoPricing', '自动定价')}
                          options={[
                            { value: true, label: t('common.yes', '是') },
                            { value: false, label: t('common.no', '否') },
                          ]}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.products.specialProduct', '特殊商品')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updateIsSpecialProduct" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateIsSpecialProduct !== cur.updateIsSpecialProduct}>
                    {({ getFieldValue }) => (
                      <Form.Item name="isSpecialProduct" noStyle>
                        <Select
                          disabled={!getFieldValue('updateIsSpecialProduct')}
                          placeholder={t('posAdmin.products.specialProduct', '特殊商品')}
                          options={[
                            { value: true, label: t('common.yes', '是') },
                            { value: false, label: t('common.no', '否') },
                          ]}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t('posAdmin.cashierUsers.status', '状态')} style={{ marginBottom: 16 }}>
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="updateIsActive" valuePropName="checked" noStyle>
                    <Checkbox>{t('posAdmin.products.toggleFieldUpdate', '修改该字段')}</Checkbox>
                  </Form.Item>
                  <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateIsActive !== cur.updateIsActive}>
                    {({ getFieldValue }) => (
                      <Form.Item name="isActive" noStyle>
                        <Select
                          disabled={!getFieldValue('updateIsActive')}
                          placeholder={t('posAdmin.cashierUsers.status', '状态')}
                          options={[
                            { value: true, label: t('common.yes', '是') },
                            { value: false, label: t('common.no', '否') },
                          ]}
                        />
                      </Form.Item>
                    )}
                  </Form.Item>
                </Space.Compact>
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>

      <Modal
        open={setCodeVisible}
        title={t('posAdmin.products.setBarcodeManagement', '套装条码管理 - {{code}}', { code: setCodeProduct?.productCode || '' })}
        onCancel={() => { setSetCodeVisible(false); setSetCodeProduct(null); setSetCodeData([]) }}
        onOk={handleSaveSetCodes}
        width={900}
        destroyOnHidden
      >
        <Spin spinning={setCodeLoading}>
          <div style={{ marginBottom: 12 }}>
            <Button type="dashed" icon={<PlusOutlined />} onClick={handleAddSetCode}>
              {t('posAdmin.products.addSubCode', '添加子码')}
            </Button>
          </div>
          <Table
            rowKey={(r) => r.id || ''}
            dataSource={setCodeData}
            pagination={false}
            size="small"
            scroll={{ y: 400 }}
            columns={[
              {
                title: t('posAdmin.products.setItemNumber', '套装货号'),
                dataIndex: 'setItemNumber',
                width: 150,
                render: (v: string, record) =>
                  setCodeEditingKey === record.id ? (
                    <Input
                      size="small"
                      value={v}
                      onChange={(e) => handleSetCodeChange(record.id!, 'setItemNumber', e.target.value)}
                    />
                  ) : (
                    v || '-'
                  ),
              },
              {
                title: t('posAdmin.invoiceDetail.barcode', '条码'),
                dataIndex: 'setBarcode',
                width: 200,
                render: (v: string, record) =>
                  setCodeEditingKey === record.id ? (
                    <Input
                      size="small"
                      value={v}
                      onChange={(e) => handleSetCodeChange(record.id!, 'setBarcode', e.target.value)}
                    />
                  ) : (
                    <BarcodePreview value={v} compactCopy />
                  ),
              },
              {
                title: t('posAdmin.invoiceDetail.purchasePrice', '进货价'),
                dataIndex: 'setPurchasePrice',
                width: 120,
                render: (v: number, record) =>
                  setCodeEditingKey === record.id ? (
                    <InputNumber
                      size="small"
                      min={0}
                      precision={2}
                      value={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'setPurchasePrice', val)}
                    />
                  ) : (
                    (v ?? 0).toFixed(2)
                  ),
              },
              {
                title: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
                dataIndex: 'setRetailPrice',
                width: 120,
                render: (v: number, record) =>
                  setCodeEditingKey === record.id ? (
                    <InputNumber
                      size="small"
                      min={0}
                      precision={2}
                      value={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'setRetailPrice', val)}
                    />
                  ) : (
                    (v ?? 0).toFixed(2)
                  ),
              },
              {
                title: t('posAdmin.productPrice.enabled', '启用'),
                dataIndex: 'isActive',
                width: 80,
                render: (v: boolean, record) =>
                  setCodeEditingKey === record.id ? (
                    <Switch
                      size="small"
                      checked={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'isActive', val)}
                    />
                  ) : (
                    <Tag color={v ? 'green' : 'red'}>{v ? t('posAdmin.products.enable', '启用') : t('posAdmin.products.disable', '禁用')}</Tag>
                  ),
              },
              {
                title: t('column.action'),
                width: 120,
                render: (_, record) => (
                  <Space>
                    {setCodeEditingKey === record.id ? (
                      <Button size="small" type="link" onClick={() => setSetCodeEditingKey(null)}>
                        {t('posAdmin.products.complete', '完成')}
                      </Button>
                    ) : (
                      <Button size="small" type="link" onClick={() => setSetCodeEditingKey(record.id ?? null)}>
                        {t('posAdmin.products.edit', '编辑')}
                      </Button>
                    )}
                    <Popconfirm title={t('posAdmin.products.confirmDelete', '确认删除？')} onConfirm={() => handleDeleteSetCode(record)}>
                      <Button size="small" type="link" danger>
                        {t('common.delete')}
                      </Button>
                    </Popconfirm>
                  </Space>
                ),
              },
            ]}
          />
        </Spin>
      </Modal>

      <Modal
        open={categoryModalVisible}
        title={t('posAdmin.products.categoryTitle', '商品分类管理')}
        onCancel={() => setCategoryModalVisible(false)}
        footer={[
          <Button key="close" onClick={() => setCategoryModalVisible(false)}>
            {t('posAdmin.products.close', '关闭')}
          </Button>,
          <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => { setEditingCategory(null); categoryEditForm.resetFields() }}>
            {t('posAdmin.products.newCategory', '新建分类')}
          </Button>,
        ]}
        width={700}
      >
        <div style={{ display: 'flex', gap: 16 }}>
          <div style={{ flex: 1 }}>
            {categoryTree.length > 0 ? (
              <Tree
                defaultExpandAll
                treeData={buildCategoryTreeData(categoryTree)}
              />
            ) : (
              <div style={{ textAlign: 'center', padding: 24, color: '#999' }}>{t('posAdmin.products.noCategoryData', '暂无分类数据')}</div>
            )}
          </div>
          <div style={{ width: 280 }}>
            <Card title={editingCategory ? t('posAdmin.products.editCategory', '编辑分类') : t('posAdmin.products.newCategory', '新建分类')} size="small">
              <Form form={categoryEditForm} layout="vertical">
                <Form.Item name="name" label={t('posAdmin.products.categoryName', '名称')} rules={[{ required: true, message: t('posAdmin.products.categoryNameRequired', '请输入分类名称') }]}>
                  <Input />
                </Form.Item>
                <Form.Item name="parentGuid" label={t('posAdmin.products.parentCategory', '父分类')}>
                  <Cascader
                    allowClear
                    showSearch
                    changeOnSelect
                    options={buildCategoryCascaderOptions(categoryTree, categoryParentDisabledGuids)}
                    disabled={categoryLoadFailed}
                    placeholder={t('posAdmin.products.noParent', '无（顶级分类）')}
                  />
                </Form.Item>
                <Form.Item name="sortOrder" label={t('posAdmin.products.sortOrder', '排序')}>
                  <InputNumber min={0} style={{ width: '100%' }} />
                </Form.Item>
                <Form.Item>
                  <Button type="primary" onClick={handleSaveCategory}>
                    {editingCategory ? t('posAdmin.products.update', '更新') : t('posAdmin.products.create', '创建')}
                  </Button>
                </Form.Item>
              </Form>
            </Card>
          </div>
        </div>
      </Modal>

      <Modal
        open={integrityVisible}
        title={t('posAdmin.products.integrityTitle', '数据一致性检查')}
        onCancel={() => setIntegrityVisible(false)}
        footer={[
          <Button key="close" onClick={() => setIntegrityVisible(false)}>
            {t('posAdmin.products.close', '关闭')}
          </Button>,
          <Button key="check" type="primary" loading={integrityLoading} onClick={handleCheckIntegrity} icon={<SafetyCertificateOutlined />}>
            {t('posAdmin.products.check', '检查')}
          </Button>,
          canManagePosProducts && integrityResult && integritySummary.issueCount > 0 ? (
            <Button key="fix" type="primary" danger loading={fixLoading} onClick={handleFixIntegrity} icon={<CloudSyncOutlined />}>
              {t('posAdmin.products.autoFix', '自动修复')}
            </Button>
          ) : null,
        ]}
        width={800}
        destroyOnHidden
      >
        <Spin spinning={integrityLoading}>
          {integrityResult ? (
            <div>
              <Descriptions bordered size="small" column={3} style={{ marginBottom: 16 }}>
                <Descriptions.Item label={t('posAdmin.products.checkedStores', '检查分店数')}>{integritySummary.storeCount}</Descriptions.Item>
                <Descriptions.Item label={t('posAdmin.products.checkedRecords', '检查记录数')}>
                  {integritySummary.totalChecked}
                </Descriptions.Item>
                <Descriptions.Item label={t('posAdmin.products.issueRecords', '问题记录数')}>
                  <Tag color={integritySummary.issueCount > 0 ? 'red' : 'green'}>{integritySummary.issueCount}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label={t('posAdmin.products.durationSeconds', '耗时')}>
                  {integritySummary.durationSeconds.toFixed(2)}s
                </Descriptions.Item>
              </Descriptions>
              {integritySummary.issueRows.length > 0 ? (
                <Table<ProductIntegrityIssueRow>
                  rowKey="key"
                  dataSource={integritySummary.issueRows}
                  pagination={false}
                  size="small"
                  scroll={{ y: 300 }}
                  columns={[
                    { title: t('posAdmin.products.scope', '范围'), dataIndex: 'scope', width: 160 },
                    { title: t('posAdmin.products.tableName', '数据表'), dataIndex: 'tableName', width: 170 },
                    { title: t('posAdmin.products.issueType', '问题类型'), dataIndex: 'issueType', width: 140 },
                    { title: t('posAdmin.products.issueCount', '数量'), dataIndex: 'count', width: 100 },
                    {
                      title: t('posAdmin.products.sampleProductCodes', '样本商品代码'),
                      dataIndex: 'sampleProductCodes',
                      render: (codes: string[]) => (
                        codes.length ? codes.join(', ') : '-'
                      ),
                    },
                  ]}
                />
              ) : (
                <div style={{ textAlign: 'center', padding: 24, color: '#52c41a' }}>
                  {t('posAdmin.products.integrityAllPass', '所有商品数据一致性检查通过')}
                </div>
              )}
            </div>
          ) : (
            <div style={{ textAlign: 'center', padding: 24, color: '#999' }}>
              {t('posAdmin.products.integrityClickCheck', '点击"检查"按钮开始数据一致性检查')}
            </div>
          )}
        </Spin>
      </Modal>
    </PageContainer>
  )
}
