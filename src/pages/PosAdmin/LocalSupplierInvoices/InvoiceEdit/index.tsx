import {
  CheckCircleOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,

  PlusOutlined,
  RollbackOutlined,
  SearchOutlined,
  SendOutlined,
  SnippetsOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Dropdown,
  Form,
  Image,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  message,
  notification,
} from 'antd'
import type { ColumnType, ColumnsType, TableProps } from 'antd/es/table'
import dayjs from 'dayjs'
import type { CSSProperties, KeyboardEvent, MouseEvent as ReactMouseEvent, ReactNode } from 'react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useStableRouteContext } from '../../../../hooks/useStableRouteContext'
import BarcodePreview from '../../../../components/BarcodePreview'
import { getProductById, updateProduct } from '../../../../services/posProductService'
import {
  batchExecuteActions,
  batchUpdateDetailAction,
  batchUpdateDetails,
  batchUpsertDetails,
  deleteDetails,
  getCheckProductsJob,
  getPasteDetailsJob,
  getProductsByBarcode,
  getInvoice,
  getInvoiceDetails,
  getUpdateHqProductsJob,
  getUpdateToStorePricesJob,
  startCheckProductsJob,
  startPasteDetailsJob,
  startUpdateHqProductsJob,
  startUpdateToStorePricesJob,
  updateDetailAction,
  updateInvoice,
} from '../../../../services/localSupplierInvoiceService'
import {
  createHqSyncJobPoller,
  HqProductSyncPollingTimeoutError,
} from '../../../../services/productHqSyncPolling'
import { getActiveStores } from '../../../../services/storeService'
import { useAuthStore } from '../../../../store/auth'
import type {
  BatchEditFields,
  BatchExecuteActionsResult,
  BatchResultDto,
  CheckProductsJobResult,
  CheckProductsResponse,
  BarcodeAbnormalMatchedProductDto,
  DetailAction,
  EnsureHqProductError,
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  PasteDetailsJobResult,
  UpdateHqProductsResult,
  UpdateHqProductsJobResult,
  UpdateToStorePricesFields,
  UpdateToStorePricesRequest,
  UpdateToStorePricesResult,
  UpdateToStorePricesJobResult,
} from '../../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../../utils/clipboard'
import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../../utils/detailLoadState'
import { discountRateToDecimal, discountRateToPercent, formatDiscountRate } from '../../../../utils/discountRate'
import { RequestError } from '../../../../utils/request'
import { DetailAction as DetailActionEnum } from '../../../../types/localSupplierInvoice'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
} from '../../../../utils/managedStoreScope'
import {
  filterInvoiceDetails,
  actionTypeFilters,
  getBarcodeStatusFilter,
  getDetailStatusStats,
  getProductStatusFilter,
  toggleStatusFilter,
} from './statusFilters'
import {
  compareNullableNumbers,
  compareNullableText,
  filterBarcodeStatusColumn,
  filterBooleanColumn,
  filterProductStatusColumn,
  matchesTextColumnFilter,
  type TextFilterField,
} from './tableColumnFilters'
import { defaultPasteFieldOrder, parsePasteText, type PasteFieldKey } from './pasteDetails'
import {
  buildBatchExecuteConfirmText,
  buildBatchExecuteSnapshot,
  constrainSelectedRowKeysToVisibleDetails,
  getBatchExecuteErrorFeedback,
} from './batchExecuteConfirm'
import {
  canApplyCheckProductsJobResult,
  canApplyInvoiceJobResult,
} from './backgroundJobGuards'
import {
  applyInvoiceDetailInlineEdit,
  buildInvoiceDetailSaveItems,
  normalizeInvoiceDetailInlineValue,
  type InvoiceDetailInlineEditableField,
} from './inlineEdit'
import {
  buildMatchedProductMasterUpdatePayload,
  getMatchedProductMasterUpdateTarget,
} from './matchedProductMasterUpdate'
import type {
  BarcodeStatusFilter,
  ActionTypeFilterValue,
  PriceFilter,
  ProductStatusFilter,
  StatusFilterValue,
} from './statusFilters'


/* ------------------------------------------------------------------ */
/*  辅助函数                                                           */
/* ------------------------------------------------------------------ */

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

function formatPricingFloatRate(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

function buildInvoiceHeaderFormValues(data: LocalSupplierInvoiceDetailDto) {
  return {
    invoiceNo: data.invoiceNo,
    storeName: data.storeName ? `${data.storeCode} - ${data.storeName}` : data.storeCode,
    supplierName: data.supplierName
      ? `${data.supplierCode} - ${data.supplierName}`
      : data.supplierCode,
    orderDate: data.orderDate ? dayjs(data.orderDate) : undefined,
    inboundDate: data.inboundDate ? dayjs(data.inboundDate) : undefined,
    totalAmount: formatAmount(data.totalAmount),
    remarks: data.remarks,
  }
}

function normalizeInvoiceSnapshot(data: LocalSupplierInvoiceDetailDto | null) {
  if (!data) return null
  return {
    invoiceGUID: data.invoiceGUID,
    appGUID: data.appGUID,
    pcGUID: data.pcGUID,
    storeCode: data.storeCode,
    storeName: data.storeName,
    supplierCode: data.supplierCode,
    supplierName: data.supplierName,
    invoiceNo: data.invoiceNo,
    orderDate: data.orderDate,
    inboundDate: data.inboundDate,
    totalAmount: data.totalAmount,
    remarks: data.remarks,
  }
}

function areLocalSupplierInvoicesEqual(
  current: LocalSupplierInvoiceDetailDto | null,
  next: LocalSupplierInvoiceDetailDto | null,
) {
  return JSON.stringify(normalizeInvoiceSnapshot(current)) === JSON.stringify(normalizeInvoiceSnapshot(next))
}

function normalizeInvoiceDetailSnapshot(item: LocalSupplierInvoiceItemDto) {
  return {
    detailGUID: item.detailGUID,
    invoiceGUID: item.invoiceGUID,
    storeCode: item.storeCode,
    supplierCode: item.supplierCode,
    productTagGUID: item.productTagGUID,
    productCategoryGUID: item.productCategoryGUID,
    storeProductCode: item.storeProductCode,
    productCode: item.productCode,
    itemNumber: item.itemNumber,
    barcode: item.barcode,
    productName: item.productName,
    specification: item.specification,
    unit: item.unit,
    quantity: item.quantity,
    lastPurchasePrice: item.lastPurchasePrice,
    purchasePrice: item.purchasePrice,
    retailPrice: item.retailPrice,
    amount: item.amount,
    existingProductCount: item.existingProductCount,
    barcodeStatus: item.barcodeStatus,
    barcodeMatchCount: item.barcodeMatchCount,
    productImage: item.productImage,
    activityType: item.activityType,
    discountRate: item.discountRate,
    autoPricing: item.autoPricing,
    pricingFloatRate: item.pricingFloatRate,
    newAutoRetailPrice: item.newAutoRetailPrice,
    isSpecialProduct: item.isSpecialProduct,
    oldStoreProductCode: item.oldStoreProductCode,
  }
}

function areLocalSupplierInvoiceDetailsEqual(
  current: LocalSupplierInvoiceItemDto[],
  next: LocalSupplierInvoiceItemDto[],
) {
  if (current.length !== next.length) return false
  return current.every((item, index) => (
    JSON.stringify(normalizeInvoiceDetailSnapshot(item)) === JSON.stringify(normalizeInvoiceDetailSnapshot(next[index]))
  ))
}

function buildInvoiceRowActions(data: LocalSupplierInvoiceItemDto[]) {
  return Object.fromEntries(
    data
      .filter((item) => item.activityType !== undefined && item.activityType !== null)
      .map((item) => [item.detailGUID, item.activityType as number]),
  )
}

const statusStatsTagColors = {
  product: { all: 'blue', notDetected: 'purple', exists: 'green', notExists: 'red' },
  barcode: { all: 'geekblue', notDetected: 'purple', normal: 'cyan', noMatch: 'volcano', multiMatch: 'orange' },
} as const

const pasteFieldOrderStorageKey = 'hbweb_rv.localSupplierInvoice.pasteFieldOrder.v1'
const validPasteFieldKeys = new Set<PasteFieldKey>([
  'itemNumber',
  'barcode',
  'productName',
  'quantity',
  'purchasePrice',
  'newAutoRetailPrice',
  'retailPrice',
  'skip',
])

function normalizePasteFieldOrder(value: unknown): PasteFieldKey[] {
  if (!Array.isArray(value) || !value.length) {
    return [...defaultPasteFieldOrder]
  }

  const fields = value.filter((item): item is PasteFieldKey => typeof item === 'string' && validPasteFieldKeys.has(item as PasteFieldKey))
  return fields.length === value.length && !hasDuplicatePasteFields(fields) ? fields : [...defaultPasteFieldOrder]
}

function hasDuplicatePasteFields(fieldOrder: PasteFieldKey[]) {
  const fields = fieldOrder.filter((field) => field !== 'skip')
  return new Set(fields).size !== fields.length
}

function loadSavedPasteFieldOrder() {
  if (typeof window === 'undefined') return [...defaultPasteFieldOrder]

  try {
    const saved = window.localStorage.getItem(pasteFieldOrderStorageKey)
    return saved ? normalizePasteFieldOrder(JSON.parse(saved)) : [...defaultPasteFieldOrder]
  } catch {
    return [...defaultPasteFieldOrder]
  }
}

function getPasteTextMaxColumnCount(text: string) {
  if (!text.trim()) return 0

  return Math.max(
    ...text
      .split('\n')
      .filter((line) => line.trim())
      .map((line) => line.split('\t').length),
  )
}

function getStatusStatsTagStyle(selected: boolean): CSSProperties {
  return {
    cursor: 'pointer',
    fontWeight: selected ? 600 : 400,
    boxShadow: selected ? '0 0 0 1px rgba(22, 119, 255, 0.35)' : undefined,
  }
}

const productNameCellStyle: CSSProperties = {
  display: '-webkit-box',
  WebkitBoxOrient: 'vertical',
  WebkitLineClamp: 2,
  overflow: 'hidden',
  whiteSpace: 'normal',
  wordBreak: 'break-word',
  lineHeight: '20px',
}

const matchedProductTableScrollX = 900

const matchedProductNameCellStyle: CSSProperties = {
  minWidth: 240,
  maxWidth: 280,
  whiteSpace: 'normal',
  wordBreak: 'break-word',
  lineHeight: '20px',
}

const matchedProductTagStyle: CSSProperties = {
  marginInlineEnd: 0,
  whiteSpace: 'nowrap',
}

const matchedProductActionButtonStyle: CSSProperties = {
  paddingInline: 0,
}

function renderCompactHeader(label: ReactNode) {
  return <span className="invoice-detail-compact-table-header">{label}</span>
}

function renderNowrapText(value: ReactNode) {
  return <span className="invoice-detail-nowrap">{value}</span>
}

function renderNumericCell(value: ReactNode) {
  return <span className="invoice-detail-nowrap invoice-detail-numeric-cell">{value}</span>
}

type ActiveFilterTag = {
  key: string
  label: ReactNode
  color?: string
  onClose: () => void
}

function normalizeEnsureHqErrors(value: unknown): EnsureHqProductError[] {
  if (!Array.isArray(value)) return []

  return value.map((item) => {
    if (typeof item === 'string') {
      return { detailGuid: '', message: item }
    }
    if (item && typeof item === 'object') {
      const raw = item as Partial<EnsureHqProductError>
      return {
        detailGuid: String(raw.detailGuid ?? ''),
        storeCode: raw.storeCode ? String(raw.storeCode) : undefined,
        message: String(raw.message ?? ''),
      }
    }
    return { detailGuid: '', message: String(item) }
  })
}

function getUpdateHqProductsFailure(error: unknown): UpdateHqProductsResult | undefined {
  if (!(error instanceof RequestError)) return undefined

  const payload = error.payload as { data?: unknown; details?: unknown } | undefined
  const candidate = (payload?.details ?? payload?.data) as Partial<UpdateHqProductsResult> | undefined
  if (!candidate || typeof candidate !== 'object') return undefined

  return {
    total: Number(candidate.total ?? 0),
    updated: Number(candidate.updated ?? 0),
    failed: Number(candidate.failed ?? 0),
    skipped: Number(candidate.skipped ?? 0),
    hqExisting: Number(candidate.hqExisting ?? 0),
    hbwebCreated: Number(candidate.hbwebCreated ?? 0),
    hqCreated: Number(candidate.hqCreated ?? 0),
    hqSynced: Number(candidate.hqSynced ?? 0),
    hqPurchasePricesUpdated: Number(candidate.hqPurchasePricesUpdated ?? 0),
    hqRetailPricesUpdated: Number(candidate.hqRetailPricesUpdated ?? 0),
    hqAutoPricingUpdated: Number(candidate.hqAutoPricingUpdated ?? 0),
    hqSpecialProductsUpdated: Number(candidate.hqSpecialProductsUpdated ?? 0),
    hqDiscountRatesUpdated: Number(candidate.hqDiscountRatesUpdated ?? 0),
    errors: normalizeEnsureHqErrors(candidate.errors),
  }
}

function buildUpdatePriceFields(values: Record<string, unknown>): UpdateToStorePricesFields {
  return {
    updatePurchasePrice: values.updatePurchasePrice === true,
    updateRetailPrice: values.updateRetailPrice === true,
    updateIsAutoPricing: values.updateIsAutoPricing === true,
    updateIsSpecialProduct: values.updateIsSpecialProduct === true,
    updateDiscountRate: values.updateDiscountRate === true,
  }
}

function hasAnyUpdatePriceField(updateFields: UpdateToStorePricesFields) {
  return (
    updateFields.updatePurchasePrice ||
    updateFields.updateRetailPrice ||
    updateFields.updateIsAutoPricing ||
    updateFields.updateIsSpecialProduct ||
    updateFields.updateDiscountRate
  )
}

/** 价格变动高亮背景色 */
function getPriceChangeBg(lastPrice?: number, currentPrice?: number): string {
  if (lastPrice === undefined || lastPrice === null || lastPrice === 0) return ''
  if (currentPrice === undefined || currentPrice === null) return ''
  const changeRate = (currentPrice - lastPrice) / lastPrice
  if (changeRate > 0.2) return '#ffccc7' // 涨>20% 红底
  if (changeRate > 0.05) return '#ffe7ba' // 涨>5% 橙底
  if (changeRate > 0) return '#fffbe6' // 涨>0% 黄底
  if (changeRate < 0) return '#d9f7be' // 跌 绿底
  return ''
}

/** 操作类型配置 */
const DETAIL_ACTION_CONFIG = (t: ReturnType<typeof useTranslation>['t']): Record<number, { label: string; color: string }> => ({
  [DetailActionEnum.None]: { label: t('posAdmin.invoiceDetail.none', '无'), color: 'default' },
  [DetailActionEnum.CreateProduct]: { label: t('posAdmin.invoiceDetail.createProduct', '新建商品'), color: 'blue' },
  [DetailActionEnum.UpdatePurchasePrice]: { label: t('posAdmin.invoiceDetail.updatePurchasePriceShort', '更新进货价'), color: 'green' },
  [DetailActionEnum.WaitForOperation]: { label: t('posAdmin.invoiceDetail.waitForOperation', '等待操作'), color: 'orange' },
  [DetailActionEnum.UpdateItemNumber]: { label: t('posAdmin.invoiceDetail.updateItemNumber', '更新货号'), color: 'purple' },
  [DetailActionEnum.AddMultiCode]: { label: t('posAdmin.invoiceDetail.addMultiCode', '添加多码'), color: 'cyan' },
  [99]: { label: t('posAdmin.invoiceDetail.executed', '已执行'), color: 'default' },
})

/** 操作类型下拉菜单项 */
const ACTION_MENU_ITEMS = (t: ReturnType<typeof useTranslation>['t']) => [
  { key: '0', label: <Tag color="default">{t('posAdmin.invoiceDetail.none', '无')}</Tag> },
  { key: '1', label: <Tag color="blue">{t('posAdmin.invoiceDetail.createProduct', '新建商品')}</Tag> },
  { key: '2', label: <Tag color="green">{t('posAdmin.invoiceDetail.updatePurchasePriceShort', '更新进货价')}</Tag> },
  { key: '3', label: <Tag color="orange">{t('posAdmin.invoiceDetail.waitForOperation', '等待操作')}</Tag> },
  { key: '4', label: <Tag color="purple">{t('posAdmin.invoiceDetail.updateItemNumber', '更新货号')}</Tag> },
  { key: '5', label: <Tag color="cyan">{t('posAdmin.invoiceDetail.addMultiCode', '添加多码')}</Tag> },
]

type InlineCellSaveHandler = (
  detailGuid: string,
  field: InvoiceDetailInlineEditableField,
  value: unknown,
) => void

/* ------------------------------------------------------------------ */
/*  双击行内编辑单元格                                                  */
/* ------------------------------------------------------------------ */

function EditableTextCell({
  value,
  detailGuid,
  field,
  onSave,
  display,
  style,
}: {
  value?: string
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  display?: ReactNode
  style?: CSSProperties
}) {
  const [editing, setEditing] = useState(false)
  const [inputValue, setInputValue] = useState(value ?? '')

  useEffect(() => {
    setInputValue(value ?? '')
  }, [value])

  const commit = () => {
    onSave(detailGuid, field, inputValue)
    setEditing(false)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      commit()
    }
    if (event.key === 'Escape') {
      setInputValue(value ?? '')
      setEditing(false)
    }
  }

  if (editing) {
    return (
      <Input
        autoFocus
        size="small"
        value={inputValue}
        onChange={(event) => setInputValue(event.target.value)}
        onBlur={commit}
        onKeyDown={handleKeyDown}
      />
    )
  }

  return (
    <span style={{ ...style, cursor: 'pointer' }} onDoubleClick={() => setEditing(true)}>
      {display ?? value ?? '--'}
    </span>
  )
}

function EditableNumberCell({
  value,
  detailGuid,
  field,
  onSave,
  displayValue,
  style,
  min = 0,
  max,
  precision = 2,
  addonAfter,
}: {
  value?: number | null
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  displayValue?: ReactNode
  style?: CSSProperties
  min?: number
  max?: number
  precision?: number
  addonAfter?: ReactNode
}) {
  const [editing, setEditing] = useState(false)
  const [inputValue, setInputValue] = useState<number | null>(value ?? null)

  useEffect(() => {
    setInputValue(value ?? null)
  }, [value])

  const commit = () => {
    if (inputValue == null) {
      setEditing(false)
      return
    }
    onSave(detailGuid, field, inputValue)
    setEditing(false)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      commit()
    }
    if (event.key === 'Escape') {
      setInputValue(value ?? null)
      setEditing(false)
    }
  }

  if (editing) {
    return (
      <InputNumber
        autoFocus
        size="small"
        min={min}
        max={max}
        precision={precision}
        addonAfter={addonAfter}
        value={inputValue}
        onChange={(nextValue) => setInputValue(nextValue)}
        onBlur={commit}
        onKeyDown={handleKeyDown}
        style={{ width: addonAfter ? 110 : 90 }}
      />
    )
  }

  return (
    <span style={{ ...style, cursor: 'pointer' }} onDoubleClick={() => setEditing(true)}>
      {displayValue ?? formatAmount(value ?? undefined)}
    </span>
  )
}

function EditableBooleanCell({
  value,
  detailGuid,
  field,
  onSave,
  trueLabel,
  falseLabel,
  trueColor,
}: {
  value?: boolean | null
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  trueLabel: string
  falseLabel: string
  trueColor: string
}) {
  const actualValue = Boolean(value)

  return (
    <Tooltip title="双击切换">
      <Tag
        color={actualValue ? trueColor : 'default'}
        style={{ cursor: 'pointer' }}
        onDoubleClick={() => onSave(detailGuid, field, !actualValue)}
      >
        {actualValue ? trueLabel : falseLabel}
      </Tag>
    </Tooltip>
  )
}

/* ------------------------------------------------------------------ */
/*  主页面组件                                                          */
/* ------------------------------------------------------------------ */

export default function InvoiceEditPage() {
  const { t } = useTranslation()
  const route = useStableRouteContext()
  const invoiceGuid = route?.params.id
  const navigate = useNavigate()
  const { access, currentUser } = useAuthStore()
  const isAdmin = access.isAdmin
  const canManagePosProducts = access.canManagePosProducts
  const canWriteLocalPurchaseToHq = access.canEditLocalPurchase && access.canPushLocalPurchaseToHq
  const canRunGlobalLocalPurchaseBatchActions = access.canEditLocalPurchase && (access.isAdmin || access.isWarehouseManager)
  const managedStoreCodes = access.managedStoreCodes()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'
  // 记录当前发票已完成首次加载，保活 Tab 恢复时保留订单头和明细表。
  const loadedInvoiceGuidRef = useRef<string | null>(null)
  const visibleInvoiceGuidRef = useRef<string | null>(null)
  const currentInvoiceGuidRef = useRef<string | undefined>(invoiceGuid)
  currentInvoiceGuidRef.current = invoiceGuid
  const lastLoadedManagedStoreCodeKeyRef = useRef<string | null>(null)
  const invoiceSnapshotRef = useRef<LocalSupplierInvoiceDetailDto | null>(null)
  const detailsSnapshotRef = useRef<LocalSupplierInvoiceItemDto[]>([])

  /* ---- 主表数据 ---- */
  const [invoice, setInvoice] = useState<LocalSupplierInvoiceDetailDto | null>(null)
  const [canAccessInvoice, setCanAccessInvoice] = useState(true)
  const [details, setDetails] = useState<LocalSupplierInvoiceItemDto[]>([])
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  /* ---- 行选择 ---- */
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  /* ---- 行内操作类型 (本地临时存储) ---- */
  const [rowActions, setRowActions] = useState<Record<string, number>>({})

  /* ---- 搜索 ---- */
  const [searchText, setSearchText] = useState('')

  /* ---- 涨跌过滤 ---- */
  const [priceFilter, setPriceFilter] = useState<PriceFilter>('all')
  const [productStatusFilter, setProductStatusFilter] = useState<StatusFilterValue<ProductStatusFilter>>('all')
  const [barcodeStatusFilter, setBarcodeStatusFilter] = useState<StatusFilterValue<BarcodeStatusFilter>>('all')
  const [actionTypeFilter, setActionTypeFilter] = useState<ActionTypeFilterValue>('all')

  /* ---- 表单 ---- */
  const [form] = Form.useForm()

  /* ---- 分店选项 ---- */
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const allStoreCodes = useMemo(() => storeOptions.map((item) => item.value), [storeOptions])

  /* ---- 粘贴数据 Modal ---- */
  const [pasteVisible, setPasteVisible] = useState(false)
  const [pasteMode, setPasteMode] = useState<'append' | 'replace'>('append')
  const [pasteText, setPasteText] = useState('')
  const [pasteLoading, setPasteLoading] = useState(false)
  const activePasteJobIdRef = useRef<string | null>(null)
  const [pasteFieldOrder, setPasteFieldOrder] = useState<PasteFieldKey[]>(loadSavedPasteFieldOrder)
  const [normalizeRetailPriceOnPaste, setNormalizeRetailPriceOnPaste] = useState(true)

  /* ---- 批量编辑 Modal ---- */
  const [batchEditVisible, setBatchEditVisible] = useState(false)
  const [batchEditForm] = Form.useForm()
  const [batchEditLoading, setBatchEditLoading] = useState(false)

  /* ---- 更新到分店价格 Modal ---- */
  const [storePriceVisible, setStorePriceVisible] = useState(false)
  const [storePriceForm] = Form.useForm()
  const selectedStorePriceTargetCodes = (Form.useWatch('targetStoreCodes', storePriceForm) ?? []) as string[]
  const selectedStorePriceTargetCodeSet = useMemo(
    () => new Set<string>(selectedStorePriceTargetCodes),
    [selectedStorePriceTargetCodes],
  )
  const allStorePriceStoresSelected = allStoreCodes.length > 0 && allStoreCodes.every((storeCode) => selectedStorePriceTargetCodeSet.has(storeCode))
  const hasPartialStorePriceStoreSelection = selectedStorePriceTargetCodes.length > 0 && !allStorePriceStoresSelected
  const [storePriceLoading, setStorePriceLoading] = useState(false)

  /* ---- 更新 HQ 商品 Modal ---- */
  const [hqUpdateVisible, setHqUpdateVisible] = useState(false)
  const [hqUpdateForm] = Form.useForm()
  const selectedHqUpdateTargetCodes = (Form.useWatch('targetStoreCodes', hqUpdateForm) ?? []) as string[]
  const selectedHqUpdateTargetCodeSet = useMemo(
    () => new Set<string>(selectedHqUpdateTargetCodes),
    [selectedHqUpdateTargetCodes],
  )
  const allHqUpdateStoresSelected = allStoreCodes.length > 0 && allStoreCodes.every((storeCode) => selectedHqUpdateTargetCodeSet.has(storeCode))
  const hasPartialHqUpdateStoreSelection = selectedHqUpdateTargetCodes.length > 0 && !allHqUpdateStoresSelected
  const [hqUpdateLoading, setHqUpdateLoading] = useState(false)
  const hqUpdateIdempotencyKeyRef = useRef<string | null>(null)

  /* ---- 商品检测 ---- */
  const [checking, setChecking] = useState(false)
  const activeCheckProductsJobIdRef = useRef<string | null>(null)

  /* ---- 批量执行操作 ---- */
  const [executing, setExecuting] = useState(false)

  /* ---- 动态表格高度 ---- */
  const tableCardRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number>(400)

  /* ================================================================ */
  /*  数据加载                                                         */
  /* ================================================================ */

  useEffect(() => {
    activePasteJobIdRef.current = null
    activeCheckProductsJobIdRef.current = null
    setChecking(false)
  }, [invoiceGuid])

  const loadInvoice = useCallback(async (showLoading = true) => {
    if (!invoiceGuid) return false
    if (showLoading) {
      setLoading(true)
    }
    try {
      const data = await getInvoice(invoiceGuid)
      if (!isStoreCodeInManagedScope(data.storeCode, managedStoreCodes)) {
        loadedInvoiceGuidRef.current = null
        visibleInvoiceGuidRef.current = null
        lastLoadedManagedStoreCodeKeyRef.current = null
        invoiceSnapshotRef.current = null
        detailsSnapshotRef.current = []
        setCanAccessInvoice(false)
        setInvoice(null)
        setDetails([])
        setSelectedRowKeys([])
        setRowActions({})
        form.resetFields()
        message.error(t('message.noPermission', '无权查看该数据'))
        return false
      }
      loadedInvoiceGuidRef.current = invoiceGuid
      visibleInvoiceGuidRef.current = invoiceGuid
      lastLoadedManagedStoreCodeKeyRef.current = managedStoreCodeKey
      setCanAccessInvoice(true)
      if (!areLocalSupplierInvoicesEqual(invoiceSnapshotRef.current, data)) {
        invoiceSnapshotRef.current = data
        setInvoice(data)
        form.setFieldsValue(buildInvoiceHeaderFormValues(data))
      }
      return true
    } catch {
      if (showLoading) {
        visibleInvoiceGuidRef.current = null
      }
      message.error(t('posAdmin.invoiceDetail.loadInvoiceFailed', '加载进货单失败'))
      return false
    } finally {
      if (showLoading) {
        setLoading(false)
      }
    }
  }, [invoiceGuid, form, managedStoreCodeKey, t])

  const loadDetails = useCallback(async (showLoading = true) => {
    if (!invoiceGuid) return
    if (showLoading) {
      setDetailLoading(true)
    }
    try {
      const data = await getInvoiceDetails(invoiceGuid)
      if (!areLocalSupplierInvoiceDetailsEqual(detailsSnapshotRef.current, data)) {
        detailsSnapshotRef.current = data
        setDetails(data)
        setRowActions(buildInvoiceRowActions(data))
      }
    } catch {
      message.error(t('posAdmin.invoiceDetail.loadDetailsFailed', '加载明细失败'))
    } finally {
      if (showLoading) {
        setDetailLoading(false)
      }
    }
  }, [invoiceGuid, t])

  const loadInvoiceAndDetails = useCallback(async (showLoading = true) => {
    if (await loadInvoice(showLoading)) {
      await loadDetails(showLoading)
    }
  }, [loadInvoice, loadDetails])

  useEffect(() => {
    if (!shouldSkipDetailAutoReload({
      requestedDetailId: invoiceGuid || '',
      loadedDetailId: loadedInvoiceGuidRef.current,
      visibleDetailId: visibleInvoiceGuidRef.current,
      requestedDetailQueryKey: managedStoreCodeKey,
      loadedDetailQueryKey: lastLoadedManagedStoreCodeKeyRef.current,
    })) {
      // 未命中保活缓存或权限范围变化时才自动加载；同一编辑进货单 Tab 切回直接复用表格状态。
      const shouldShowInitialLoading = shouldShowDetailInitialLoading({
        requestedDetailId: invoiceGuid || '',
        loadedDetailId: loadedInvoiceGuidRef.current,
        visibleDetailId: visibleInvoiceGuidRef.current,
      })
      void loadInvoiceAndDetails(shouldShowInitialLoading)
    }
    if (managedStoreCodes === null) {
      getActiveStores()
        .then((stores) => {
          setStoreOptions(filterStoreOptionsByManagedCodes(stores, managedStoreCodes))
        })
        .catch(() => setStoreOptions([]))
    } else {
      setStoreOptions(buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true }))
    }
  }, [currentUser?.stores, invoiceGuid, loadInvoiceAndDetails, managedStoreCodeKey])

  useEffect(() => {
    if (typeof window === 'undefined') return

    try {
      // 只在前端记住 Excel 列和业务字段的对应关系，提交给后端的契约仍然是字段名 payload。
      window.localStorage.setItem(pasteFieldOrderStorageKey, JSON.stringify(pasteFieldOrder))
    } catch {
      // localStorage 不可用时不影响粘贴提交，继续使用当前页面内的列配置。
    }
  }, [pasteFieldOrder])

  const ensureCanAccessInvoice = useCallback(() => {
    if (canAccessInvoice) {
      return true
    }
    message.error(t('message.noPermission', '无权操作该数据'))
    return false
  }, [canAccessInvoice, t])

  /* ---- 动态高度 ---- */
  useLayoutEffect(() => {
    const calc = () => {
      const available = window.innerHeight - (tableCardRef.current?.getBoundingClientRect().top ?? 200) - 80
      setTableScrollY(available > 200 ? available : 200)
    }
    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [details.length])

  /* ================================================================ */
  /*  计算属性                                                         */
  /* ================================================================ */

  // 涨跌统计
  const priceStats = useMemo(() => {
    let upCount = 0
    let downCount = 0
    details.forEach((item) => {
      if (
        item.lastPurchasePrice !== undefined &&
        item.lastPurchasePrice !== null &&
        item.lastPurchasePrice > 0 &&
        item.purchasePrice !== undefined &&
        item.purchasePrice !== null
      ) {
        if (item.purchasePrice > item.lastPurchasePrice) upCount++
        else if (item.purchasePrice < item.lastPurchasePrice) downCount++
      }
    })
    return { upCount, downCount }
  }, [details])

  // 状态统计始终基于全部明细计算，不受当前搜索和过滤条件影响。
  const detailStatusStats = useMemo(() => getDetailStatusStats(details, rowActions), [details, rowActions])

  // 过滤后数据
  const filteredDetails = useMemo(
    () =>
      filterInvoiceDetails(details, {
        searchText,
        priceFilter,
        productStatusFilter,
        barcodeStatusFilter,
        actionTypeFilter,
        rowActions,
      }),
    [details, searchText, priceFilter, productStatusFilter, barcodeStatusFilter, actionTypeFilter, rowActions],
  )

  const detailActionConfig = useMemo(() => DETAIL_ACTION_CONFIG(t), [t])

  const productStatusFilterLabels: Record<ProductStatusFilter, string> = useMemo(
    () => ({
      notDetected: t('posAdmin.invoiceDetail.notDetected', '未检测'),
      exists: t('posAdmin.invoiceDetail.exists', '已存在'),
      notExists: t('posAdmin.invoiceDetail.notExistsShort', '不存在'),
    }),
    [t],
  )

  const barcodeStatusFilterLabels: Record<BarcodeStatusFilter, string> = useMemo(
    () => ({
      notDetected: t('posAdmin.invoiceDetail.notDetected', '未检测'),
      normal: t('posAdmin.invoiceDetail.normal', '正常'),
      noMatch: t('posAdmin.invoiceDetail.noMatch', '无匹配'),
      multiMatch: t('posAdmin.invoiceDetail.multiMatchShort', '多匹配({{count}})', { count: detailStatusStats.barcode.multiMatch }),
    }),
    [detailStatusStats.barcode.multiMatch, t],
  )

  const pasteFieldLabels: Record<PasteFieldKey, string> = useMemo(
    () => ({
      itemNumber: t('posAdmin.invoiceDetail.itemNumber', '货号'),
      barcode: t('posAdmin.invoiceDetail.barcode', '条码'),
      productName: t('posAdmin.invoiceDetail.productName', '商品名称'),
      quantity: t('posAdmin.invoiceDetail.quantity', '数量'),
      purchasePrice: t('posAdmin.invoiceDetail.currentPurchasePrice', '本次进货价'),
      newAutoRetailPrice: t('posAdmin.invoiceDetail.newAutoRetailPrice', '新自动零售价'),
      retailPrice: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
      skip: t('posAdmin.invoiceDetail.pasteFieldSkip', '跳过此列'),
    }),
    [t],
  )
  const pasteFieldOptions = useMemo(
    () => Array.from(validPasteFieldKeys).map((field) => ({ label: pasteFieldLabels[field], value: field })),
    [pasteFieldLabels],
  )
  const pasteColumnCount = useMemo(() => getPasteTextMaxColumnCount(pasteText), [pasteText])
  const hasDuplicatePasteField = useMemo(() => hasDuplicatePasteFields(pasteFieldOrder), [pasteFieldOrder])
  const pasteParseOptions = useMemo(
    () => ({ normalizeRetailPrice: normalizeRetailPriceOnPaste }),
    [normalizeRetailPriceOnPaste],
  )

  useEffect(() => {
    if (pasteColumnCount <= pasteFieldOrder.length) return

    setPasteFieldOrder((prev) => [
      ...prev,
      ...Array<PasteFieldKey>(pasteColumnCount - prev.length).fill('skip'),
    ])
  }, [pasteColumnCount, pasteFieldOrder.length])

  const handleClearAllOuterFilters = useCallback(() => {
    setSearchText('')
    setPriceFilter('all')
    setProductStatusFilter('all')
    setBarcodeStatusFilter('all')
    setActionTypeFilter('all')
  }, [])

  // 当前过滤栏：只展示页面外层过滤，不包含表格列头自带过滤。
  const activeFilterTags = useMemo<ActiveFilterTag[]>(() => {
    const tags: ActiveFilterTag[] = []
    const keyword = searchText.trim()

    if (keyword) {
      tags.push({
        key: 'search',
        color: 'blue',
        label: t('posAdmin.invoiceDetail.activeSearchFilter', '搜索：{{value}}', { value: keyword }),
        onClose: () => setSearchText(''),
      })
    }

    if (priceFilter === 'up') {
      tags.push({
        key: 'price-up',
        color: 'red',
        label: t('posAdmin.invoiceDetail.activePriceUpFilter', '涨价'),
        onClose: () => setPriceFilter('all'),
      })
    } else if (priceFilter === 'down') {
      tags.push({
        key: 'price-down',
        color: 'green',
        label: t('posAdmin.invoiceDetail.activePriceDownFilter', '降价'),
        onClose: () => setPriceFilter('all'),
      })
    }

    if (productStatusFilter !== 'all') {
      tags.push({
        key: 'product-status',
        color: statusStatsTagColors.product[productStatusFilter],
        label: t('posAdmin.invoiceDetail.activeProductStatusFilter', '商品状态：{{value}}', {
          value: productStatusFilterLabels[productStatusFilter],
        }),
        onClose: () => setProductStatusFilter('all'),
      })
    }

    if (barcodeStatusFilter !== 'all') {
      tags.push({
        key: 'barcode-status',
        color: statusStatsTagColors.barcode[barcodeStatusFilter],
        label: t('posAdmin.invoiceDetail.activeBarcodeStatusFilter', '条码状态：{{value}}', {
          value: barcodeStatusFilterLabels[barcodeStatusFilter],
        }),
        onClose: () => setBarcodeStatusFilter('all'),
      })
    }

    if (actionTypeFilter !== 'all') {
      tags.push({
        key: 'action-type',
        color: detailActionConfig[actionTypeFilter]?.color,
        label: t('posAdmin.invoiceDetail.activeActionTypeFilter', '操作类型：{{value}}', {
          value: detailActionConfig[actionTypeFilter]?.label ?? detailActionConfig[DetailActionEnum.None].label,
        }),
        onClose: () => setActionTypeFilter('all'),
      })
    }

    return tags
  }, [
    actionTypeFilter,
    barcodeStatusFilter,
    barcodeStatusFilterLabels,
    detailActionConfig,
    priceFilter,
    productStatusFilter,
    productStatusFilterLabels,
    searchText,
    t,
  ])

  useEffect(() => {
    setSelectedRowKeys((prev) => constrainSelectedRowKeysToVisibleDetails(prev, filteredDetails))
  }, [filteredDetails])

  /* ================================================================ */
  /*  操作处理函数                                                      */
  /* ================================================================ */

  // ---- 保存主表 ----
  const handleSave = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    const values = await form.validateFields()
    setSaving(true)
    try {
      await updateInvoice(invoiceGuid, {
        orderDate: values.orderDate?.format?.('YYYY-MM-DD') || undefined,
        inboundDate: values.inboundDate?.format?.('YYYY-MM-DD') || undefined,
        remarks: values.remarks?.trim() || undefined,
      })
      message.success(t('posAdmin.invoiceDetail.saveSuccess', '保存成功'))
      loadInvoice()
    } catch {
      message.error(t('posAdmin.invoiceDetail.saveFailed', '保存失败'))
    } finally {
      setSaving(false)
    }
  }

  // ---- 行内双击编辑，先更新本地明细，统一由“保存明细”落库 ----
  const handleInlineDetailSave = useCallback(
    (detailGuid: string, field: InvoiceDetailInlineEditableField, value: unknown) => {
      try {
        const normalizedValue = normalizeInvoiceDetailInlineValue(field, value)
        setDetails((prev) => applyInvoiceDetailInlineEdit(prev, detailGuid, field, normalizedValue))
      } catch {
        message.error(t('posAdmin.invoiceDetail.invalidInlineValue', '请输入有效的明细内容'))
      }
    },
    [t],
  )

  // ---- 批量保存明细（含行内价格编辑） ----
  const handleSaveDetails = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    const items: InvoiceDetailUpsertItemDto[] = buildInvoiceDetailSaveItems(details)
    setDetailLoading(true)
    try {
      await batchUpsertDetails(invoiceGuid, items)
      message.success(t('posAdmin.invoiceDetail.detailSaveSuccess', '明细保存成功'))
      loadDetails()
    } catch {
      message.error(t('posAdmin.invoiceDetail.detailSaveFailed', '明细保存失败'))
    } finally {
      setDetailLoading(false)
    }
  }

  // ---- 粘贴数据 ----
  const handlePaste = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (hasDuplicatePasteField) {
      message.warning(t('posAdmin.invoiceDetail.pasteFieldDuplicateWarning', '同一个字段不能选择多次，请把多余列设置为“跳过此列”'))
      return
    }

    const parsed = parsePasteText(pasteText, pasteFieldOrder, pasteParseOptions)
    if (!parsed.length) {
      message.warning(t('posAdmin.invoiceDetail.noValidData', '未检测到有效数据'))
      return
    }
    setPasteLoading(true)
    try {
      const submittedInvoiceGuid = invoiceGuid
      const job = await startPasteDetailsJob({
        invoiceGuid: submittedInvoiceGuid,
        mode: pasteMode,
        items: parsed,
      })
      activePasteJobIdRef.current = job.jobId
      setPasteVisible(false)
      setPasteText('')
      notifyBackgroundTaskSubmitted(t('posAdmin.invoiceDetail.pasteJobSubmitted', '粘贴数据任务已提交'))

      void (async () => {
        try {
          const completedJob = await pollPasteDetailsJob(submittedInvoiceGuid, job.jobId)
          if (activePasteJobIdRef.current !== job.jobId) {
            return
          }
          const result = completedJob.result
          if (!result) {
            throw new Error(completedJob.message || t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'))
          }

          const description = formatPasteDetailsResult(result)
          if (completedJob.status === 'Failed') {
            notification.error({
              message: t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'),
              description: completedJob.message || description,
              duration: 0,
            })
          } else {
            notification[result.failed > 0 ? 'warning' : 'success']({
              message: t('posAdmin.invoiceDetail.pasteCompletedTitle', '粘贴数据完成'),
              description,
              duration: result.failed > 0 ? 0 : 4,
            })
          }
          if (canApplyInvoiceJobResult(currentInvoiceGuidRef.current, submittedInvoiceGuid)) {
            await loadDetails()
          }
        } catch (error) {
          if (error instanceof HqProductSyncPollingTimeoutError) {
            notifyBatchJobTimeout()
            return
          }
          notification.error({
            message: t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'),
            description: error instanceof Error ? error.message : t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'),
            duration: 0,
          })
        } finally {
          if (activePasteJobIdRef.current === job.jobId) {
            activePasteJobIdRef.current = null
          }
        }
      })()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'))
    } finally {
      setPasteLoading(false)
    }
  }

  // ---- 批量编辑 ----
  const handleBatchEdit = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailRows', '请先选择明细行'))
      return
    }
    const values = await batchEditForm.validateFields()
    const editFields: BatchEditFields = {
      updatePurchasePrice: values.updatePurchasePrice ?? false,
      purchasePrice: values.updatePurchasePrice ? values.purchasePrice : undefined,
      updateRetailPrice: values.updateRetailPrice ?? false,
      retailPrice: values.updateRetailPrice ? values.retailPrice : undefined,
      updateIsAutoPricing: values.updateIsAutoPricing ?? false,
      isAutoPricing: values.updateIsAutoPricing ? values.isAutoPricing : undefined,
      updateIsSpecialProduct: values.updateIsSpecialProduct ?? false,
      isSpecialProduct: values.updateIsSpecialProduct ? values.isSpecialProduct : undefined,
      updateDiscountRate: values.updateDiscountRate ?? false,
      discountRate: values.updateDiscountRate ? discountRateToDecimal(values.discountRate) : undefined,
      updateAction: false,
    }

    const hasAnyField =
      editFields.updatePurchasePrice ||
      editFields.updateRetailPrice ||
      editFields.updateIsAutoPricing ||
      editFields.updateIsSpecialProduct ||
      editFields.updateDiscountRate
    if (!hasAnyField) {
      message.warning(t('posAdmin.invoiceDetail.selectUpdateField', '请至少选择一个要更新的字段'))
      return
    }

    setBatchEditLoading(true)
    try {
      const items = selectedRowKeys.map((key) => ({
        detailGUID: String(key),
      }))
      await batchUpdateDetails(invoiceGuid, items, editFields)
      message.success(t('posAdmin.invoiceDetail.batchUpdateSuccess', '批量更新成功'))
      setBatchEditVisible(false)
      batchEditForm.resetFields()
      setSelectedRowKeys([])
      await loadDetails()
    } catch {
      message.error(t('posAdmin.invoiceDetail.batchUpdateFailed', '批量更新失败'))
    } finally {
      setBatchEditLoading(false)
    }
  }

  const openStorePriceModal = () => {
    setStorePriceVisible(true)
  }

  const openHqUpdateModal = () => {
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailRows', '请先选择明细行'))
      return
    }
    hqUpdateForm.resetFields()
    setHqUpdateVisible(true)
  }

  const showUpdateHqProductsResult = (result: UpdateHqProductsResult) => {
    const hqErrors = normalizeEnsureHqErrors(result.errors)
    Modal.info({
      title: t('posAdmin.invoiceDetail.updateHqProductsResultTitle', '更新HQ商品结果'),
      width: 640,
      content: (
        <Space direction="vertical" size={4} style={{ width: '100%' }}>
          <div>{t('posAdmin.invoiceDetail.updateHqProductsTotal', '总处理：{{count}} 条', { count: result.total ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqProductsUpdated', '成功更新：{{count}} 条', { count: result.updated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqProductsSkipped', '跳过：{{count}} 条', { count: result.skipped ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqProductsFailedCount', '失败：{{count}} 条', { count: result.failed ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.ensureHqExisting', 'HQ已存在：{{count}} 条', { count: result.hqExisting ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.ensureHqHbwebCreated', 'HBweb新建：{{count}} 条', { count: result.hbwebCreated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.ensureHqCreated', 'HQ新建：{{count}} 条', { count: result.hqCreated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.ensureHqSynced', 'HQ同步：{{count}} 条', { count: result.hqSynced ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqPurchasePricesUpdated', 'HQ进货价更新：{{count}} 条', { count: result.hqPurchasePricesUpdated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqRetailPricesUpdated', 'HQ零售价更新：{{count}} 条', { count: result.hqRetailPricesUpdated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqAutoPricingUpdated', 'HQ自动定价更新：{{count}} 条', { count: result.hqAutoPricingUpdated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqSpecialProductsUpdated', 'HQ特殊商品更新：{{count}} 条', { count: result.hqSpecialProductsUpdated ?? 0 })}</div>
          <div>{t('posAdmin.invoiceDetail.updateHqDiscountRatesUpdated', 'HQ折扣率更新：{{count}} 条', { count: result.hqDiscountRatesUpdated ?? 0 })}</div>
          {hqErrors.length > 0 && (
            <div style={{ maxHeight: 220, overflow: 'auto', marginTop: 8 }}>
              {hqErrors.map((item, index) => (
                <div key={`${item.detailGuid || 'detail'}-${item.storeCode ?? 'store'}-${index}`} style={{ color: '#ff4d4f', fontSize: 12 }}>
                  {item.detailGuid ? `${item.detailGuid}：` : ''}{item.storeCode ? `${item.storeCode}：` : ''}{item.message}
                </div>
              ))}
            </div>
          )}
        </Space>
      ),
    })
  }

  const renderBackgroundTaskDetailsButton = (onClick: () => void) => (
    <Button type="link" size="small" style={{ padding: 0 }} onClick={onClick}>
      {t('posAdmin.invoiceDetail.backgroundTaskViewDetails', '查看详情')}
    </Button>
  )

  const notifyBackgroundTaskSubmitted = (messageText: string) => {
    notification.info({
      message: messageText,
      description: t('posAdmin.invoiceDetail.backgroundTaskSubmitted', '已提交到后台执行，完成后会在右上角通知结果。'),
      duration: 3,
    })
  }

  const notifyBatchJobTimeout = () => {
    notification.warning({
      message: t('posAdmin.invoiceDetail.localSupplierInvoiceBatchJobTimeoutTitle', '本地进货单批量任务仍在后台执行'),
      description: t('posAdmin.invoiceDetail.localSupplierInvoiceBatchJobTimeout', '前端已停止轮询该任务。你可以稍后刷新页面查看结果，或使用相同条件重新提交以接管后台任务。'),
      duration: 0,
    })
  }

  const formatPasteDetailsResult = (result: BatchResultDto) => {
    return t('posAdmin.invoiceDetail.pasteComplete', '粘贴完成：新增 {{inserted}} 条，更新 {{updated}} 条，失败 {{failed}} 条', {
      inserted: result.inserted ?? 0,
      updated: result.updated ?? 0,
      failed: result.failed ?? 0,
    })
  }

  const pollPasteDetailsJob = async (submittedInvoiceGuid: string, jobId: string) => {
    // 关键位置：粘贴数据可能触发大量写入，只保留 job 查询，避免弹窗确认一直等待长请求。
    const poller = createHqSyncJobPoller<PasteDetailsJobResult>({
      jobId,
      getJob: () => getPasteDetailsJob(submittedInvoiceGuid, jobId),
    })
    return poller.promise
  }

  const pollCheckProductsJob = async (submittedInvoiceGuid: string, jobId: string) => {
    // 关键位置：商品检测改为后台执行，前台只轮询终态并合并结果。
    const poller = createHqSyncJobPoller<CheckProductsJobResult>({
      jobId,
      getJob: () => getCheckProductsJob(submittedInvoiceGuid, jobId),
    })
    return poller.promise
  }

  const pollUpdateToStorePricesJob = async (jobId: string) => {
    // 关键位置：长任务只轮询后台 job，避免把浏览器请求保持到网关超时。
    const poller = createHqSyncJobPoller<UpdateToStorePricesJobResult>({
      jobId,
      getJob: () => getUpdateToStorePricesJob(jobId),
    })
    return poller.promise
  }

  const pollUpdateHqProductsJob = async (jobId: string) => {
    // 关键位置：更新 HQ 商品可能跨库写入，必须通过后台 job 查询最终结果。
    const poller = createHqSyncJobPoller<UpdateHqProductsJobResult>({
      jobId,
      getJob: () => getUpdateHqProductsJob(invoiceGuid!, jobId),
    })
    return poller.promise
  }

  const formatUpdateToStoreResult = (result: UpdateToStorePricesResult) => {
    return t(
      'posAdmin.invoiceDetail.updateToStoreResultWithSkipped',
      '更新完成：成功 {{updated}} 条，跳过 {{skipped}} 条，失败 {{failed}} 条',
      { updated: result.updated ?? 0, skipped: result.skipped ?? 0, failed: result.failed ?? 0 },
    )
  }

  const showUpdateToStoreErrors = (result: UpdateToStorePricesResult) => {
    Modal.error({
      title: t('posAdmin.invoiceDetail.updateToStoreResultTitle', '更新到分店价格结果'),
      content: (
        <Space direction="vertical" size={8} style={{ width: '100%' }}>
          <div>{formatUpdateToStoreResult(result)}</div>
          {!!result.errors?.length && (
            <div style={{ maxHeight: 240, overflow: 'auto' }}>
              {result.errors.map((err, i) => (
                <div key={i} style={{ color: '#ff4d4f', fontSize: 12 }}>{err}</div>
              ))}
            </div>
          )}
        </Space>
      ),
    })
  }

  const formatBatchExecuteResultParts = (result: BatchExecuteActionsResult) => {
    const parts: string[] = []
    if (result.createdProducts > 0) parts.push(t('posAdmin.invoiceDetail.createdProducts', '新建商品{{count}}条', { count: result.createdProducts }))
    if (result.updatedPurchasePrices > 0) parts.push(t('posAdmin.invoiceDetail.updatedPurchasePrices', '更新进货价{{count}}条', { count: result.updatedPurchasePrices }))
    if (result.updatedItemNumbers > 0) parts.push(t('posAdmin.invoiceDetail.updatedItemNumbers', '更新货号{{count}}条', { count: result.updatedItemNumbers }))
    if (result.addedMultiCodes > 0) parts.push(t('posAdmin.invoiceDetail.addedMultiCodes', '添加多码{{count}}条', { count: result.addedMultiCodes }))
    if (result.skipped > 0) parts.push(t('posAdmin.invoiceDetail.skipped', '跳过{{count}}条', { count: result.skipped }))
    if (result.failed > 0) parts.push(t('posAdmin.invoiceDetail.failed', '失败{{count}}条', { count: result.failed }))
    return parts.join('，') || t('posAdmin.invoiceDetail.noOperation', '无操作')
  }

  const showBatchExecuteResultDetails = (result: BatchExecuteActionsResult) => {
    Modal.error({
      title: t('posAdmin.invoiceDetail.partialFailed', '部分操作失败'),
      content: (
        <div>
          <p>{formatBatchExecuteResultParts(result)}</p>
          <div style={{ maxHeight: 200, overflow: 'auto', marginTop: 8 }}>
            {result.errors.map((err, i) => (
              <div key={i} style={{ color: '#ff4d4f', fontSize: 12 }}>{err}</div>
            ))}
          </div>
        </div>
      ),
    })
  }

  // ---- 更新到分店价格 ----
  const handleUpdateToStorePrices = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailRows', '请先选择明细行'))
      return
    }
    const values = await storePriceForm.validateFields()
    if (!values.targetStoreCodes?.length) {
      message.warning(t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店'))
      return
    }
    if (!values.targetStoreCodes.every((storeCode: string) => isStoreCodeInManagedScope(storeCode, managedStoreCodes))) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    const updateFields = buildUpdatePriceFields(values)
    if (!hasAnyUpdatePriceField(updateFields)) {
      message.warning(t('posAdmin.invoiceDetail.selectUpdateField', '请至少选择一个要更新的字段'))
      return
    }

    const request: UpdateToStorePricesRequest = {
      invoiceGuid,
      detailGuids: selectedRowKeys.map(String),
      targetStoreCodes: values.targetStoreCodes,
      updateFields,
    }

    setStorePriceVisible(false)
    storePriceForm.resetFields()
    setStorePriceLoading(true)
    notifyBackgroundTaskSubmitted(t('posAdmin.invoiceDetail.updateToStoreSubmitted', '更新到分店价格已提交'))

    void (async () => {
      try {
        const job = await startUpdateToStorePricesJob(request)
        const completedJob = await pollUpdateToStorePricesJob(job.jobId)
        const result = completedJob.result
        if (!result) {
          throw new Error(completedJob.message || t('posAdmin.invoiceDetail.updateToStoreFailed', '更新到分店价格失败'))
        }
        const description = formatUpdateToStoreResult(result)
        const hasDetails = !!result.errors?.length || (result.skipped ?? 0) > 0 || (result.failed ?? 0) > 0
        if (completedJob.status === 'Failed') {
          notification.error({
            message: t('posAdmin.invoiceDetail.updateToStoreFailed', '更新到分店价格失败'),
            description: hasDetails ? (
              <Space direction="vertical" size={4}>
                <span>{completedJob.message || description}</span>
                {renderBackgroundTaskDetailsButton(() => showUpdateToStoreErrors(result))}
              </Space>
            ) : (completedJob.message || description),
            duration: 0,
          })
          return
        }
        notification[result.failed > 0 || result.errors?.length ? 'warning' : 'success']({
          message: t('posAdmin.invoiceDetail.updateToStoreCompleted', '更新到分店价格完成'),
          description: hasDetails ? (
            <Space direction="vertical" size={4}>
              <span>{description}</span>
              {renderBackgroundTaskDetailsButton(() => showUpdateToStoreErrors(result))}
            </Space>
          ) : description,
          duration: hasDetails ? 0 : 4,
        })
      } catch (error) {
        if (error instanceof HqProductSyncPollingTimeoutError) {
          notifyBatchJobTimeout()
          return
        }
        notification.error({
          message: t('posAdmin.invoiceDetail.updateToStoreFailed', '更新到分店价格失败'),
          description: error instanceof Error ? error.message : t('posAdmin.invoiceDetail.updateToStoreFailed', '更新到分店价格失败'),
          duration: 0,
        })
      } finally {
        setStorePriceLoading(false)
      }
    })()
  }

  // ---- 更新 HQ 商品 ----
  const handleUpdateHqProducts = async () => {
    if (hqUpdateLoading) return
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!canWriteLocalPurchaseToHq) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailRows', '请先选择明细行'))
      return
    }
    const values = await hqUpdateForm.validateFields()
    if (!values.targetStoreCodes?.length) {
      message.warning(t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店'))
      return
    }
    if (!values.targetStoreCodes.every((storeCode: string) => isStoreCodeInManagedScope(storeCode, managedStoreCodes))) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    const updateFields = buildUpdatePriceFields(values)
    if (!hasAnyUpdatePriceField(updateFields)) {
      message.warning(t('posAdmin.invoiceDetail.selectUpdateField', '请至少选择一个要更新的字段'))
      return
    }

    const detailGuids = selectedRowKeys.map(String)
    const targetStoreCodes = values.targetStoreCodes

    // 同一轮提交使用稳定幂等键，避免用户重复点击造成 HQ 重复写入。
    hqUpdateIdempotencyKeyRef.current =
      hqUpdateIdempotencyKeyRef.current ??
      (typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID()
        : `${invoiceGuid}-${detailGuids.join(',')}-${targetStoreCodes.join(',')}`)
    const idempotencyKey = hqUpdateIdempotencyKeyRef.current

    setHqUpdateVisible(false)
    hqUpdateForm.resetFields()
    setHqUpdateLoading(true)
    notifyBackgroundTaskSubmitted(t('posAdmin.invoiceDetail.updateHqProductsSubmitted', '更新HQ商品已提交'))

    void (async () => {
      let shouldClearIdempotencyKey = true
      try {
        const job = await startUpdateHqProductsJob(invoiceGuid, {
          detailGuids,
          targetStoreCodes,
          updateFields,
          idempotencyKey,
        })
        const completedJob = await pollUpdateHqProductsJob(job.jobId)
        const result = completedJob.result
        if (!result) {
          throw new Error(completedJob.message || t('posAdmin.invoiceDetail.updateHqProductsFailed', '更新HQ商品失败'))
        }
        const hasDetails = result.failed > 0 || (result.skipped ?? 0) > 0 || !!result.errors?.length
        if (completedJob.status === 'Failed') {
          notification.error({
            message: t('posAdmin.invoiceDetail.updateHqProductsFailed', '更新HQ商品失败'),
            description: (
              <Space direction="vertical" size={4}>
                <span>{completedJob.message || t('posAdmin.invoiceDetail.updateHqProductsFailedCount', '失败：{{count}} 条', { count: result.failed ?? 0 })}</span>
                {hasDetails && renderBackgroundTaskDetailsButton(() => showUpdateHqProductsResult(result))}
              </Space>
            ),
            duration: 0,
          })
          return
        }
        notification[result.failed > 0 || result.errors?.length ? 'warning' : 'success']({
          message: t('posAdmin.invoiceDetail.updateHqProductsCompleted', '更新HQ商品完成'),
          description: (
            <Space direction="vertical" size={4}>
              <span>{t('posAdmin.invoiceDetail.updateHqProductsUpdated', '成功更新：{{count}} 条', { count: result.updated ?? 0 })}</span>
              {hasDetails && renderBackgroundTaskDetailsButton(() => showUpdateHqProductsResult(result))}
            </Space>
          ),
          duration: hasDetails ? 0 : 4,
        })
        await loadDetails()
      } catch (error) {
        if (error instanceof HqProductSyncPollingTimeoutError) {
          shouldClearIdempotencyKey = false
          notifyBatchJobTimeout()
          return
        }
        const failure = getUpdateHqProductsFailure(error)
        notification.error({
          message: t('posAdmin.invoiceDetail.updateHqProductsFailed', '更新HQ商品失败'),
          description: failure ? (
            <Space direction="vertical" size={4}>
              <span>{t('posAdmin.invoiceDetail.updateHqProductsFailedCount', '失败：{{count}} 条', { count: failure.failed ?? 0 })}</span>
              {renderBackgroundTaskDetailsButton(() => showUpdateHqProductsResult(failure))}
            </Space>
          ) : (error instanceof Error ? error.message : t('posAdmin.invoiceDetail.updateHqProductsFailed', '更新HQ商品失败')),
          duration: 0,
        })
      } finally {
        if (shouldClearIdempotencyKey) {
          hqUpdateIdempotencyKeyRef.current = null
        }
        setHqUpdateLoading(false)
      }
    })()
  }

  const applyCheckProductsResponse = (result: CheckProductsResponse) => {
    // 更新每行的商品状态和条码状态
    const statusMap = new Map(result.results.map((r) => [r.detailGuid, r]))
    setDetails((prev) =>
      prev.map((d) => {
        const checkResult = statusMap.get(d.detailGUID)
        if (!checkResult) return d
        return {
          ...d,
          productCode: checkResult.productInfo?.productCode ?? undefined,
          storeProductCode: checkResult.productInfo?.storeProductCode ?? checkResult.storeProductCode ?? undefined,
          existingProductCount: checkResult.existingProductCount,
          barcodeStatus: checkResult.barcodeStatus,
          barcodeMatchCount: checkResult.barcodeMatchCount,
          autoPricing: checkResult.autoPricing ?? undefined,
          isSpecialProduct: checkResult.isSpecialProduct ?? undefined,
          discountRate: checkResult.discountRate ?? undefined,
          pricingFloatRate: checkResult.pricingFloatRate ?? undefined,
          newAutoRetailPrice: checkResult.newAutoRetailPrice ?? undefined,
          lastPurchasePrice: checkResult.lastPurchasePrice ?? undefined,
          activityType: checkResult.defaultAction ?? d.activityType,
        } as LocalSupplierInvoiceItemDto
      }),
    )
    // 更新行内操作类型
    const newActions: Record<string, number> = {}
    result.results.forEach((r) => {
      if (r.defaultAction !== undefined) {
        newActions[r.detailGuid] = r.defaultAction
      }
    })
    setRowActions((prev) => ({ ...prev, ...newActions }))
  }

  // ---- 商品检测 ----
  const handleCheckProducts = async () => {
    if (checking) return
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!details.length) {
      message.warning(t('posAdmin.invoiceDetail.noDetailToDetect', '没有明细数据可检测'))
      return
    }
    setChecking(true)
    try {
      const submittedInvoiceGuid = invoiceGuid
      const job = await startCheckProductsJob({
        invoiceGuid: submittedInvoiceGuid,
        detailGuids: selectedRowKeys.length > 0 ? selectedRowKeys.map(String) : undefined,
      })
      activeCheckProductsJobIdRef.current = job.jobId
      notifyBackgroundTaskSubmitted(t('posAdmin.invoiceDetail.checkProductsJobSubmitted', '商品检测任务已提交'))

      void (async () => {
        try {
          const completedJob = await pollCheckProductsJob(submittedInvoiceGuid, job.jobId)
          if (activeCheckProductsJobIdRef.current !== job.jobId) {
            return
          }
          const result = completedJob.result
          if (completedJob.status === 'Failed') {
            notification.error({
              message: t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'),
              description: completedJob.message || t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'),
              duration: 0,
            })
            return
          }
          if (!result) {
            throw new Error(completedJob.message || t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'))
          }

          const description = t('posAdmin.invoiceDetail.detectCompleteMsg', '检测完成：共 {{total}}条，商品存在 {{productExists}}条，不存在 {{productNotExists}}条，条码正常 {{barcodeNormal}}条，异常 {{barcodeAbnormal}}条', result.summary)
          notification.success({
            message: t('posAdmin.invoiceDetail.detectCompletedTitle', '商品检测完成'),
            description,
            duration: 4,
          })
          if (canApplyCheckProductsJobResult({
            currentInvoiceGuid: currentInvoiceGuidRef.current,
            submittedInvoiceGuid,
            status: completedJob.status,
            hasResult: true,
          })) {
            applyCheckProductsResponse(result)
            await loadDetails()
          }
        } catch (error) {
          if (error instanceof HqProductSyncPollingTimeoutError) {
            notifyBatchJobTimeout()
            return
          }
          notification.error({
            message: t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'),
            description: error instanceof Error ? error.message : t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'),
            duration: 0,
          })
        } finally {
          if (activeCheckProductsJobIdRef.current === job.jobId) {
            activeCheckProductsJobIdRef.current = null
            setChecking(false)
          }
        }
      })()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'))
      setChecking(false)
    }
  }

  // ---- 行操作类型变更 ----
  const handleRowActionChange = async (detailGuid: string, actionKey: string) => {
    if (!ensureCanAccessInvoice()) return
    if (!isAdmin) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }
    const action = Number(actionKey) as DetailAction

    if (invoiceGuid) {
      try {
        await updateDetailAction(invoiceGuid, detailGuid, action)
        setRowActions((prev) => ({ ...prev, [detailGuid]: action }))
        setDetails((prev) =>
          prev.map((item) =>
            item.detailGUID === detailGuid ? { ...item, activityType: action } : item,
          ),
        )
      } catch (error) {
        message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.updateActionFailed', '更新操作类型失败'))
      }
    }
  }

  const executeSelectedBatchActions = async (snapshot: ReturnType<typeof buildBatchExecuteSnapshot>) => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    setExecuting(true)
    notifyBackgroundTaskSubmitted(t('posAdmin.invoiceDetail.batchExecuteSubmitted', '批量执行操作已提交'))
    try {
      const result: BatchExecuteActionsResult = await batchExecuteActions({
        invoiceGuid,
        detailGuids: snapshot.detailGuids,
        expectedActions: snapshot.expectedActions,
        confirmedCreateProductCount: snapshot.confirmedCreateProductCount,
        confirmedAt: snapshot.confirmedAt ?? new Date().toISOString(),
      })
      const parts = formatBatchExecuteResultParts(result)
      const hasDetails = !!result.errors?.length || result.failed > 0 || result.skipped > 0
      notification[result.failed > 0 || result.errors?.length ? 'warning' : 'success']({
        message: t('posAdmin.invoiceDetail.batchExecuteCompleted', '批量执行操作完成'),
        description: hasDetails ? (
          <Space direction="vertical" size={4}>
            <span>{t('posAdmin.invoiceDetail.executeResultMsg', '执行完成：{{parts}}', { parts })}</span>
            {renderBackgroundTaskDetailsButton(() => showBatchExecuteResultDetails(result))}
          </Space>
        ) : t('posAdmin.invoiceDetail.executeResultMsg', '执行完成：{{parts}}', { parts }),
        duration: hasDetails ? 0 : 4,
      })
      void loadDetails()
    } catch (error) {
      const feedback = getBatchExecuteErrorFeedback(error, t('posAdmin.invoiceDetail.executeFailed', '批量执行操作失败'))
      notification.error({
        message: feedback.message,
        description: feedback.details.length ? (
          <Space direction="vertical" size={4}>
            {feedback.failure && <span>{formatBatchExecuteResultParts(feedback.failure)}</span>}
            {feedback.failure && renderBackgroundTaskDetailsButton(() => showBatchExecuteResultDetails(feedback.failure!))}
          </Space>
        ) : feedback.message,
        duration: 0,
      })
    } finally {
      setExecuting(false)
    }
  }

  // ---- 批量执行操作 ----
  const handleBatchExecute = () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    const visibleSelectedRowKeys = constrainSelectedRowKeysToVisibleDetails(selectedRowKeys, filteredDetails)
    if (!visibleSelectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailsFirst', '请先选择明细行'))
      return
    }
    if (visibleSelectedRowKeys.length !== selectedRowKeys.length) {
      setSelectedRowKeys(visibleSelectedRowKeys)
    }

    const previewSnapshot = buildBatchExecuteSnapshot({
      selectedRowKeys: visibleSelectedRowKeys,
      details,
      rowActions,
    })
    const confirmText = buildBatchExecuteConfirmText({
      selectedCount: previewSnapshot.selectedCount,
      createProductCount: previewSnapshot.confirmedCreateProductCount,
      labels: {
        title: t('posAdmin.invoiceDetail.batchExecuteConfirmTitle', '确认执行批量操作？'),
        content: t('posAdmin.invoiceDetail.batchExecuteConfirmContent', '将对 {{count}} 条明细执行已设置的操作。'),
        createProductNotice: t('posAdmin.invoiceDetail.batchExecuteCreateProductNotice', '其中 {{count}} 条会新建商品，请确认货号、条码和名称无误。'),
        okText: t('posAdmin.invoiceDetail.batchExecuteConfirmOk', '确认执行'),
        cancelText: t('common.cancel', '取消'),
      },
    })

    Modal.confirm({
      title: confirmText.title,
      content: (
        <Space direction="vertical" size={4}>
          {confirmText.content.split('\n').map((line) => (
            <div key={line}>{line}</div>
          ))}
        </Space>
      ),
      okText: confirmText.okText,
      cancelText: confirmText.cancelText,
      okButtonProps: { danger: previewSnapshot.confirmedCreateProductCount > 0 },
      onOk: () => {
        void executeSelectedBatchActions(buildBatchExecuteSnapshot({
          selectedRowKeys: previewSnapshot.detailGuids,
          details,
          rowActions,
          // 真正提交的确认时间在用户点击确认时生成。
          confirmedAt: new Date().toISOString(),
        }))
      },
    })
  }

  // ---- 删除选中 ----
  const handleDeleteSelected = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDeleteRows', '请先选择要删除的明细行'))
      return
    }
    setDetailLoading(true)
    try {
      await deleteDetails(invoiceGuid, selectedRowKeys.map(String))
      message.success(t('posAdmin.invoiceDetail.deleteSuccess', '删除成功'))
      setSelectedRowKeys([])
      loadDetails()
      loadInvoice()
    } catch {
      message.error(t('posAdmin.invoiceDetail.deleteFailed', '删除失败'))
    } finally {
      setDetailLoading(false)
    }
  }

  // ---- 批量设置操作类型 ----
  const handleBatchSetAction = async (actionKey: string) => {
    if (!invoiceGuid || !selectedRowKeys.length || !ensureCanAccessInvoice()) return
    const action = Number(actionKey)
    try {
      await batchUpdateDetailAction(invoiceGuid, selectedRowKeys.map(String), action)
      const newActions: Record<string, number> = {}
      selectedRowKeys.forEach((key) => {
        newActions[String(key)] = action
      })
      setRowActions((prev) => ({ ...prev, ...newActions }))
      setDetails((prev) =>
        prev.map((item) =>
          selectedRowKeys.map(String).includes(item.detailGUID)
            ? { ...item, activityType: action }
            : item,
        ),
      )
      message.success(t('posAdmin.invoiceDetail.batchSetActionSuccess', '批量设置操作类型成功'))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.batchSetActionFailed', '批量设置操作类型失败'))
    }
  }

  /* ================================================================ */
  /*  表格列定义                                                       */
  /* ================================================================ */

  const getTextColumnSearchProps = (
    field: TextFilterField,
    label: string,
  ): ColumnType<LocalSupplierInvoiceItemDto> => ({
    filterDropdown: ({ setSelectedKeys, selectedKeys, confirm, clearFilters }) => (
      <div style={{ padding: 8 }} onKeyDown={(event) => event.stopPropagation()}>
        <Input
          autoFocus
          allowClear
          size="small"
          placeholder={t('posAdmin.invoiceDetail.columnSearchPlaceholder', '搜索{{label}}', { label })}
          value={String(selectedKeys[0] ?? '')}
          onChange={(event) => {
            const value = event.target.value
            setSelectedKeys(value ? [value] : [])
          }}
          onPressEnter={() => confirm()}
          style={{ width: 180, marginBottom: 8, display: 'block' }}
        />
        <Space size={8}>
          <Button
            type="primary"
            size="small"
            icon={<SearchOutlined />}
            onClick={() => confirm()}
          >
            {t('common.search', '搜索')}
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
      </div>
    ),
    filterIcon: (filtered) => (
      <SearchOutlined style={{ color: filtered ? '#1677ff' : undefined }} />
    ),
    onFilter: (value, record) => matchesTextColumnFilter(record, field, value),
  })

  const handleTableChange: TableProps<LocalSupplierInvoiceItemDto>['onChange'] = (
    _pagination,
    _filters,
    _sorter,
    extra,
  ) => {
    setSelectedRowKeys((prev) => constrainSelectedRowKeysToVisibleDetails(prev, extra.currentDataSource))
  }

  const showBarcodeMatchedProducts = async (record: LocalSupplierInvoiceItemDto) => {
    if (!invoiceGuid || !record.barcode) {
      message.warning(t('posAdmin.invoiceDetail.noBarcodeToQuery', '当前行没有条码'))
      return
    }

    const barcode = record.barcode
    const modal = Modal.info({
      title: t('posAdmin.invoiceDetail.barcodeMatchedProductsTitle', '条码匹配商品：{{barcode}}', { barcode }),
      width: 920,
      okText: t('common.close', '关闭'),
      content: <div>{t('common.loading', '加载中...')}</div>,
    })

    const renderMatchedProductsContent = (
      matchedProducts: BarcodeAbnormalMatchedProductDto[],
      matchedProductColumns: ColumnsType<BarcodeAbnormalMatchedProductDto>,
    ) => matchedProducts.length ? (
      <Table<BarcodeAbnormalMatchedProductDto>
        size="small"
        rowKey={(item, index) => `${item.productCode || 'product'}-${item.barcode || barcode}-${index ?? 0}`}
        columns={matchedProductColumns}
        dataSource={matchedProducts}
        pagination={false}
        tableLayout="fixed"
        scroll={{ x: matchedProductTableScrollX, y: 320 }}
      />
    ) : (
      <div>{t('posAdmin.invoiceDetail.noBarcodeMatchedProducts', '没有匹配到商品')}</div>
    )

    try {
      const refreshMatchedProducts = async (
        matchedProductColumns: ColumnsType<BarcodeAbnormalMatchedProductDto>,
      ) => {
        const refreshed = await getProductsByBarcode(invoiceGuid, barcode)
        modal.update({
          content: renderMatchedProductsContent(refreshed?.matchedProducts ?? [], matchedProductColumns),
        })
      }

      const handleReplaceMatchedProductMaster = (
        matchedProduct: BarcodeAbnormalMatchedProductDto,
        matchedProductColumns: ColumnsType<BarcodeAbnormalMatchedProductDto>,
      ) => {
        const target = getMatchedProductMasterUpdateTarget(record, invoice)
        if (!target.itemNumber) {
          message.warning(t('posAdmin.invoiceDetail.replaceProductMasterMissingItemNumber', '当前明细缺少货号，无法更换'))
          return
        }
        if (!target.supplierCode) {
          message.warning(t('posAdmin.invoiceDetail.replaceProductMasterMissingSupplier', '当前明细缺少供应商，无法更换'))
          return
        }
        if (!matchedProduct.productCode) {
          message.warning(t('posAdmin.invoiceDetail.replaceProductMasterMissingProductCode', '匹配商品缺少商品编码，无法更换'))
          return
        }

        Modal.confirm({
          title: t('posAdmin.invoiceDetail.replaceProductMasterConfirmTitle', '确认更换匹配商品主档？'),
          content: (
            <Space direction="vertical" size={4}>
              <span>
                {t('posAdmin.invoiceDetail.replaceProductMasterSourceLine', '所选商品当前：货号 {{itemNumber}}，供应商 {{supplier}}', {
                  itemNumber: matchedProduct.itemNumber || '--',
                  supplier: matchedProduct.supplierName
                    ? `${matchedProduct.supplierCode || '--'} - ${matchedProduct.supplierName}`
                    : matchedProduct.supplierCode || '--',
                })}
              </span>
              <span>
                {t('posAdmin.invoiceDetail.replaceProductMasterTargetLine', '将写入当前明细：货号 {{itemNumber}}，供应商 {{supplier}}', {
                  itemNumber: target.itemNumber,
                  supplier: target.supplierCode,
                })}
              </span>
            </Space>
          ),
          okText: t('posAdmin.invoiceDetail.replaceProductMaster', '更换货号和供应商'),
          cancelText: t('common.cancel', '取消'),
          onOk: async () => {
            try {
              const fullProduct = await getProductById(matchedProduct.productCode)
              // 商品更新接口是完整 DTO 语义，这里先读取详情再覆盖目标字段，避免清空其它主档字段。
              const payload = buildMatchedProductMasterUpdatePayload(fullProduct, record, invoice)
              await updateProduct(matchedProduct.productCode, payload)
              message.success(t('posAdmin.invoiceDetail.replaceProductMasterSuccess', '商品主档已更新'))
              await refreshMatchedProducts(matchedProductColumns)
            } catch (error) {
              message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.replaceProductMasterFailed', '更换商品主档失败'))
              throw error
            }
          },
        })
      }

      const result = await getProductsByBarcode(invoiceGuid, barcode)
      const matchedProducts = result?.matchedProducts ?? []
      const matchedProductColumns: ColumnsType<BarcodeAbnormalMatchedProductDto> = [
        {
          title: t('posAdmin.invoiceDetail.itemNumber', '货号'),
          dataIndex: 'itemNumber',
          width: 120,
          render: (value?: string) => value || '--',
        },
        {
          title: t('posAdmin.invoiceDetail.barcode', '条码'),
          dataIndex: 'barcode',
          width: 150,
          render: (value?: string) => value || '--',
        },
        {
          title: t('posAdmin.invoiceDetail.productName', '商品名称'),
          dataIndex: 'productName',
          width: 280,
          render: (value?: string) => (
            <div style={matchedProductNameCellStyle} title={value || undefined}>
              {value || '--'}
            </div>
          ),
        },
        {
          title: t('posAdmin.invoiceDetail.supplierName', '供应商名称'),
          dataIndex: 'supplierName',
          width: 150,
          render: (value?: string) => value || '--',
        },
        {
          title: t('posAdmin.invoiceDetail.matchSource', '来源'),
          dataIndex: 'isMultiCode',
          width: 100,
          render: (isMultiCode?: boolean) => (
            <Tag color={isMultiCode ? 'orange' : 'blue'} style={matchedProductTagStyle}>
              {isMultiCode
                ? t('posAdmin.invoiceDetail.multiBarcode', '分店多条码')
                : t('posAdmin.invoiceDetail.mainBarcode', '商品主条码')}
            </Tag>
          ),
        },
        ...(canManagePosProducts ? [{
          title: t('posAdmin.invoiceDetail.action', '操作'),
          key: 'replaceProductMaster',
          width: 90,
          render: (_: unknown, matchedProduct: BarcodeAbnormalMatchedProductDto) => (
            <Tooltip title={t('posAdmin.invoiceDetail.replaceProductMaster', '更换货号和供应商')}>
              <Button
                size="small"
                type="link"
                style={matchedProductActionButtonStyle}
                onClick={() => handleReplaceMatchedProductMaster(matchedProduct, matchedProductColumns)}
              >
                {t('posAdmin.invoiceDetail.replaceProductMasterShort', '更换')}
              </Button>
            </Tooltip>
          ),
        } satisfies ColumnType<BarcodeAbnormalMatchedProductDto>] : []),
      ]

      modal.update({
        content: renderMatchedProductsContent(matchedProducts, matchedProductColumns),
      })
    } catch {
      modal.destroy()
      message.error(t('posAdmin.invoiceDetail.queryBarcodeMatchedProductsFailed', '查询条码匹配商品失败'))
    }
  }

  const columns: ColumnsType<LocalSupplierInvoiceItemDto> = [
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.seqNo', '序号')),
      width: 44,
      align: 'right',
      fixed: 'left',
      render: (_, __, index) => renderNumericCell(index + 1),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.image', '图片')),
      dataIndex: 'productImage',
      width: 48,
      fixed: 'left',
      render: (v: string) =>
        v ? (
          <Image src={v} width={36} height={36} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <div
            style={{
              width: 36,
              height: 36,
              background: '#f5f5f5',
              borderRadius: 4,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#ccc',
              fontSize: 10,
            }}
          >
            {t('posAdmin.invoiceDetail.noImage', '无图')}
          </div>
        ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.itemNumber', '货号')),
      dataIndex: 'itemNumber',
      width: 108,
      fixed: 'left',
      sorter: (a, b) => compareNullableText(a.itemNumber, b.itemNumber),
      ...getTextColumnSearchProps('itemNumber', t('posAdmin.invoiceDetail.itemNumber', '货号')),
      render: (v: string, record) => (
        <Space size={2} className="invoice-detail-nowrap">
          <EditableTextCell
            value={v}
            detailGuid={record.detailGUID}
            field="itemNumber"
            onSave={handleInlineDetailSave}
            display={renderNowrapText(v || '--')}
          />
          {v && (
            <Tooltip title={t('posAdmin.invoiceDetail.copyItemNumber', '复制货号')}>
              <Button
                type="text"
                size="small"
                icon={<CopyOutlined />}
                onDoubleClick={(event) => event.stopPropagation()}
                onClick={(event) => {
                  event.stopPropagation()
                  void copyTextToClipboard(v)
                }}
              />
            </Tooltip>
          )}
        </Space>
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.barcode', '条码')),
      dataIndex: 'barcode',
      width: 138,
      sorter: (a, b) => compareNullableText(a.barcode, b.barcode),
      ...getTextColumnSearchProps('barcode', t('posAdmin.invoiceDetail.barcode', '条码')),
      render: (v: string, record) => (
        <div className="invoice-detail-nowrap">
          <EditableTextCell
            value={v}
            detailGuid={record.detailGUID}
            field="barcode"
            onSave={handleInlineDetailSave}
            display={<BarcodePreview value={v} compactCopy />}
          />
        </div>
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.productName', '商品名称')),
      dataIndex: 'productName',
      width: 190,
      sorter: (a, b) => compareNullableText(a.productName, b.productName),
      ...getTextColumnSearchProps('productName', t('posAdmin.invoiceDetail.productName', '商品名称')),
      render: (v: string, record) => (
        <EditableTextCell
          value={v}
          detailGuid={record.detailGUID}
          field="productName"
          onSave={handleInlineDetailSave}
          display={<div style={productNameCellStyle}>{v || '--'}</div>}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.quantity', '数量')),
      dataIndex: 'quantity',
      width: 58,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.quantity, b.quantity),
      render: (v: number, record) => (
        <EditableNumberCell
          value={v}
          detailGuid={record.detailGUID}
          field="quantity"
          onSave={handleInlineDetailSave}
          precision={0}
          displayValue={renderNumericCell(v ?? '--')}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.lastPurchasePrice', '上次进货价')),
      dataIndex: 'lastPurchasePrice',
      width: 82,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.lastPurchasePrice, b.lastPurchasePrice),
      render: (v: number) => renderNumericCell(formatAmount(v)),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.currentPurchasePrice', '本次进货价')),
      dataIndex: 'purchasePrice',
      width: 86,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.purchasePrice, b.purchasePrice),
      render: (v: number, record) => {
        const bg = getPriceChangeBg(record.lastPurchasePrice, v)
        const bgStyle = bg ? { backgroundColor: bg, padding: '2px 6px', borderRadius: 4 } : undefined
        return (
          <span className="invoice-detail-nowrap invoice-detail-numeric-cell">
            <EditableNumberCell
              value={v}
              detailGuid={record.detailGUID}
              field="purchasePrice"
              onSave={handleInlineDetailSave}
              style={bgStyle}
            />
          </span>
        )
      },
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.retailPrice', '零售价')),
      dataIndex: 'retailPrice',
      width: 72,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.retailPrice, b.retailPrice),
      render: (v: number, record) => (
        <EditableNumberCell
          value={v}
          detailGuid={record.detailGUID}
          field="retailPrice"
          onSave={handleInlineDetailSave}
          displayValue={renderNumericCell(formatAmount(v))}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.pricingRate', '定价浮率')),
      dataIndex: 'pricingFloatRate',
      width: 76,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.pricingFloatRate, b.pricingFloatRate),
      render: (v: number, record) => (
        <EditableNumberCell
          value={v}
          detailGuid={record.detailGUID}
          field="pricingFloatRate"
          onSave={handleInlineDetailSave}
          displayValue={renderNumericCell(formatPricingFloatRate(v))}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.newAutoRetailPrice', '新自动零售价')),
      dataIndex: 'newAutoRetailPrice',
      width: 92,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.newAutoRetailPrice, b.newAutoRetailPrice),
      render: (v: number, record) => (
        <EditableNumberCell
          value={v}
          detailGuid={record.detailGUID}
          field="newAutoRetailPrice"
          onSave={handleInlineDetailSave}
          displayValue={renderNumericCell(formatAmount(v))}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.autoPricingLabel', '自动定价')),
      dataIndex: 'autoPricing',
      width: 68,
      align: 'center',
      filters: [
        { text: t('posAdmin.invoiceDetail.auto', '自动'), value: true },
        { text: t('posAdmin.invoiceDetail.manual', '手动'), value: false },
      ],
      onFilter: (value, record) => filterBooleanColumn(record.autoPricing, value),
      render: (v: boolean, record) => (
        <EditableBooleanCell
          value={v}
          detailGuid={record.detailGUID}
          field="autoPricing"
          onSave={handleInlineDetailSave}
          trueLabel={t('posAdmin.invoiceDetail.auto', '自动')}
          falseLabel={t('posAdmin.invoiceDetail.manual', '手动')}
          trueColor="green"
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.specialProductLabel', '特殊商品')),
      dataIndex: 'isSpecialProduct',
      width: 68,
      align: 'center',
      filters: [
        { text: t('posAdmin.invoiceDetail.yes', '是'), value: true },
        { text: t('posAdmin.invoiceDetail.no', '否'), value: false },
      ],
      onFilter: (value, record) => filterBooleanColumn(record.isSpecialProduct, value),
      render: (v: boolean, record) => (
        <EditableBooleanCell
          value={v}
          detailGuid={record.detailGUID}
          field="isSpecialProduct"
          onSave={handleInlineDetailSave}
          trueLabel={t('posAdmin.invoiceDetail.yes', '是')}
          falseLabel={t('posAdmin.invoiceDetail.no', '否')}
          trueColor="orange"
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.discountRate', '折扣率')),
      dataIndex: 'discountRate',
      width: 68,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.discountRate, b.discountRate),
      render: (v: number, record) => (
        <EditableNumberCell
          value={discountRateToPercent(v)}
          detailGuid={record.detailGUID}
          field="discountRate"
          onSave={handleInlineDetailSave}
          max={100}
          precision={1}
          addonAfter="%"
          displayValue={renderNumericCell(formatDiscountRate(v))}
        />
      ),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.amount', '金额')),
      dataIndex: 'amount',
      width: 82,
      align: 'right',
      sorter: (a, b) => compareNullableNumbers(a.amount, b.amount),
      render: (v: number) => renderNumericCell(formatAmount(v)),
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.productStatus', '商品状态')),
      dataIndex: 'existingProductCount',
      width: 78,
      align: 'center',
      filters: [
        { text: t('posAdmin.invoiceDetail.notDetected', '未检测'), value: 'notDetected' },
        { text: t('posAdmin.invoiceDetail.exists', '已存在'), value: 'exists' },
        { text: t('posAdmin.invoiceDetail.notExistsShort', '不存在'), value: 'notExists' },
      ],
      onFilter: (value, record) => filterProductStatusColumn(record, value),
      render: (_: number, record) => {
        const count = record.existingProductCount
        const status = getProductStatusFilter(record)
        if (status === 'notDetected') {
          return <Tag color="default">{t('posAdmin.invoiceDetail.notDetected', '未检测')}</Tag>
        }
        if (status === 'exists') {
          return <Tag color="green">{t('posAdmin.invoiceDetail.existsWithCount', '已存在({{count}})', { count })}</Tag>
        }
        return <Tag color="red">{t('posAdmin.invoiceDetail.notExistsShort', '不存在')}</Tag>
      },
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.barcodeStatus', '条码状态')),
      dataIndex: 'barcodeMatchCount',
      width: 84,
      align: 'center',
      filters: [
        { text: t('posAdmin.invoiceDetail.notDetected', '未检测'), value: 'notDetected' },
        { text: t('posAdmin.invoiceDetail.normal', '正常'), value: 'normal' },
        { text: t('posAdmin.invoiceDetail.noMatch', '无匹配'), value: 'noMatch' },
        { text: t('posAdmin.invoiceDetail.multiMatchShort', '多匹配'), value: 'multiMatch' },
      ],
      onFilter: (value, record) => filterBarcodeStatusColumn(record, value),
      render: (_: number, record) => {
        const count = record.barcodeMatchCount ?? 0
        const status = getBarcodeStatusFilter(record)
        const openMatchedProducts = (event: ReactMouseEvent) => {
          event.stopPropagation()
          void showBarcodeMatchedProducts(record)
        }
        const clickableStyle: CSSProperties | undefined = status === 'notDetected' ? undefined : { cursor: 'pointer' }
        if (status === 'notDetected') {
          return <Tag color="default">{t('posAdmin.invoiceDetail.notDetected', '未检测')}</Tag>
        }
        if (status === 'normal') {
          return <Tag color="green" style={clickableStyle} onClick={openMatchedProducts}>{t('posAdmin.invoiceDetail.normal', '正常')}</Tag>
        }
        if (status === 'noMatch') {
          return <Tag color="red" style={clickableStyle} onClick={openMatchedProducts}>{t('posAdmin.invoiceDetail.noMatch', '无匹配')}</Tag>
        }
        return <Tag color="orange" style={clickableStyle} onClick={openMatchedProducts}>{t('posAdmin.invoiceDetail.multiMatchShort', '多匹配({{count}})', { count })}</Tag>
      },
    },
    {
      title: renderCompactHeader(t('posAdmin.invoiceDetail.action', '操作')),
      key: 'action',
      width: 98,
      fixed: 'right',
      render: (_, record) => {
        const currentAction = rowActions[record.detailGUID] ?? record.activityType ?? 0
        const actionConfig = DETAIL_ACTION_CONFIG(t)
        const config = actionConfig[currentAction] || actionConfig[0]
        return isAdmin ? (
          <Dropdown
            menu={{
              items: ACTION_MENU_ITEMS(t),
              onClick: ({ key }) => void handleRowActionChange(record.detailGUID, key),
              selectedKeys: [String(currentAction)],
            }}
            trigger={['click']}
          >
            <Button size="small" type="text">
              <Tag color={config.color} style={{ cursor: 'pointer' }}>
                {config.label}
              </Tag>
            </Button>
          </Dropdown>
        ) : (
          <Tag color={config.color}>{config.label}</Tag>
        )
      },
    },
  ]

  /* ================================================================ */
  /*  渲染                                                             */
  /* ================================================================ */

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      {/* ============================================================ */}
      {/* 顶部 Card - 订单头信息                                         */}
      {/* ============================================================ */}
      <Card
        title={t('posAdmin.invoiceDetail.orderHeaderInfo', '订单头信息')}
        loading={loading}
        size="small"
        className="invoice-header-compact-card"
      >
        <Form form={form} layout="vertical">
          <Row gutter={[12, 8]} align="bottom">
            <Col flex="150px">
              <Form.Item name="invoiceNo" label={t('posAdmin.invoiceDetail.orderNoLabel', '订单号')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col flex="150px">
              <Form.Item name="storeName" label={t('posAdmin.invoiceDetail.storeLabel', '分店')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col flex="170px">
              <Form.Item name="supplierName" label={t('posAdmin.invoiceDetail.supplierLabel', '供应商')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col flex="130px">
              <Form.Item name="orderDate" label={t('posAdmin.invoiceDetail.orderDate', '订单日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col flex="130px">
              <Form.Item name="inboundDate" label={t('posAdmin.invoiceDetail.inboundDate', '入库日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col flex="110px">
              <Form.Item name="totalAmount" label={t('posAdmin.invoiceDetail.totalAmountLabel', '总金额')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col flex="160px">
              <Form.Item name="remarks" label={t('posAdmin.invoiceDetail.remarksLabel', '备注')}>
                <Input placeholder={t('posAdmin.invoiceDetail.remarksPlaceholder', '备注')} />
              </Form.Item>
            </Col>
            <Col flex="none" className="invoice-header-compact-card__actions">
              <Form.Item label=" " className="invoice-header-compact-card__actions-item">
                <Space size={8} wrap className="invoice-header-compact-card__actions-space">
                  <Button type="primary" loading={saving} onClick={() => void handleSave()}>
                    {t('posAdmin.invoiceDetail.saveBtn', '保存')}
                  </Button>
                  <Button
                    icon={<RollbackOutlined />}
                    onClick={() => navigate('/pos-admin/local-supplier-invoices')}
                  >
                    {t('posAdmin.invoiceDetail.returnToList', '返回列表')}
                  </Button>
                </Space>
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>

      {/* ============================================================ */}
      {/* 底部 Card - 明细表格                                           */}
      {/* ============================================================ */}
      <Card
        ref={tableCardRef}
        title={t('posAdmin.invoiceDetail.detailCount', '明细 ({{count}} 条)', { count: details.length })}
        size="small"
        extra={
          <div ref={toolbarRef}>
            <Space wrap size={8}>
              {/* 搜索 */}
              <Input
                allowClear
                placeholder={t('posAdmin.invoiceDetail.searchKeyword', '搜索货号/条码/名称')}
                style={{ width: 180 }}
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                prefix={<CopyOutlined />}
              />
              {/* 涨跌过滤 */}
              <Button
                type={priceFilter === 'up' ? 'primary' : 'default'}
                danger={priceFilter === 'up'}
                size="small"
                onClick={() => setPriceFilter(priceFilter === 'up' ? 'all' : 'up')}
              >
                {t('posAdmin.invoiceDetail.priceUpCount', '涨价 ({{count}})', { count: priceStats.upCount })}
              </Button>
              <Button
                type={priceFilter === 'down' ? 'primary' : 'default'}
                size="small"
                style={priceFilter === 'down' ? { background: '#52c41a', borderColor: '#52c41a' } : {}}
                onClick={() => setPriceFilter(priceFilter === 'down' ? 'all' : 'down')}
              >
                {t('posAdmin.invoiceDetail.priceDownCount', '降价 ({{count}})', { count: priceStats.downCount })}
              </Button>
            </Space>
          </div>
        }
      >
        {/* 状态统计栏：数量按全部明细计算，点击后与搜索和涨跌筛选叠加。 */}
        <div style={{ marginBottom: 12 }}>
          <Space wrap size={8}>
            <span style={{ fontWeight: 500 }}>{t('posAdmin.invoiceDetail.statusStatsTitle', '状态统计')}</span>
            <span style={{ color: '#595959' }}>{t('posAdmin.invoiceDetail.productStatusLabel', '商品状态')}</span>
            <Tag
              color={statusStatsTagColors.product.all}
              style={getStatusStatsTagStyle(productStatusFilter === 'all')}
              onClick={() => setProductStatusFilter('all')}
            >
              {t('posAdmin.invoiceDetail.statusStatsAll', '全部 {{count}}', { count: details.length })}
            </Tag>
            <Tag
              color={statusStatsTagColors.product.notDetected}
              style={getStatusStatsTagStyle(productStatusFilter === 'notDetected')}
              onClick={() => setProductStatusFilter(toggleStatusFilter(productStatusFilter, 'notDetected'))}
            >
              {t('posAdmin.invoiceDetail.notDetected', '未检测')} {detailStatusStats.product.notDetected}
            </Tag>
            <Tag
              color={statusStatsTagColors.product.exists}
              style={getStatusStatsTagStyle(productStatusFilter === 'exists')}
              onClick={() => setProductStatusFilter(toggleStatusFilter(productStatusFilter, 'exists'))}
            >
              {t('posAdmin.invoiceDetail.exists', '已存在')} {detailStatusStats.product.exists}
            </Tag>
            <Tag
              color={statusStatsTagColors.product.notExists}
              style={getStatusStatsTagStyle(productStatusFilter === 'notExists')}
              onClick={() => setProductStatusFilter(toggleStatusFilter(productStatusFilter, 'notExists'))}
            >
              {t('posAdmin.invoiceDetail.notExistsShort', '不存在')} {detailStatusStats.product.notExists}
            </Tag>
            <span style={{ color: '#595959' }}>{t('posAdmin.invoiceDetail.barcodeStatusLabel', '条码状态')}</span>
            <Tag
              color={statusStatsTagColors.barcode.all}
              style={getStatusStatsTagStyle(barcodeStatusFilter === 'all')}
              onClick={() => setBarcodeStatusFilter('all')}
            >
              {t('posAdmin.invoiceDetail.statusStatsAll', '全部 {{count}}', { count: details.length })}
            </Tag>
            <Tag
              color={statusStatsTagColors.barcode.notDetected}
              style={getStatusStatsTagStyle(barcodeStatusFilter === 'notDetected')}
              onClick={() => setBarcodeStatusFilter(toggleStatusFilter(barcodeStatusFilter, 'notDetected'))}
            >
              {t('posAdmin.invoiceDetail.notDetected', '未检测')} {detailStatusStats.barcode.notDetected}
            </Tag>
            <Tag
              color={statusStatsTagColors.barcode.normal}
              style={getStatusStatsTagStyle(barcodeStatusFilter === 'normal')}
              onClick={() => setBarcodeStatusFilter(toggleStatusFilter(barcodeStatusFilter, 'normal'))}
            >
              {t('posAdmin.invoiceDetail.normal', '正常')} {detailStatusStats.barcode.normal}
            </Tag>
            <Tag
              color={statusStatsTagColors.barcode.noMatch}
              style={getStatusStatsTagStyle(barcodeStatusFilter === 'noMatch')}
              onClick={() => setBarcodeStatusFilter(toggleStatusFilter(barcodeStatusFilter, 'noMatch'))}
            >
              {t('posAdmin.invoiceDetail.noMatch', '无匹配')} {detailStatusStats.barcode.noMatch}
            </Tag>
            <Tag
              color={statusStatsTagColors.barcode.multiMatch}
              style={getStatusStatsTagStyle(barcodeStatusFilter === 'multiMatch')}
              onClick={() => setBarcodeStatusFilter(toggleStatusFilter(barcodeStatusFilter, 'multiMatch'))}
            >
              {t('posAdmin.invoiceDetail.multiMatch', '多匹配({{count}})', { count: detailStatusStats.barcode.multiMatch })}
            </Tag>
            <span style={{ color: '#595959' }}>{t('posAdmin.invoiceDetail.actionTypeLabel', '操作类型')}</span>
            <Tag
              color="blue"
              style={getStatusStatsTagStyle(actionTypeFilter === 'all')}
              onClick={() => setActionTypeFilter('all')}
            >
              {t('posAdmin.invoiceDetail.statusStatsAll', '全部 {{count}}', { count: details.length })}
            </Tag>
            {actionTypeFilters.map((actionType) => {
              const config = detailActionConfig[actionType] ?? detailActionConfig[DetailActionEnum.None]
              return (
                <Tag
                  key={actionType}
                  color={config.color}
                  style={getStatusStatsTagStyle(actionTypeFilter === actionType)}
                  onClick={() => setActionTypeFilter(toggleStatusFilter(actionTypeFilter, actionType))}
                >
                  {config.label} {detailStatusStats.action[actionType]}
                </Tag>
              )
            })}
          </Space>
        </div>

        <div style={{ marginBottom: 12 }}>
          <Space wrap size={8}>
            <span style={{ fontWeight: 500 }}>{t('posAdmin.invoiceDetail.activeFiltersTitle', '当前过滤')}</span>
            {activeFilterTags.length ? (
              <>
                {activeFilterTags.map((filterTag) => (
                  <Tag
                    key={filterTag.key}
                    color={filterTag.color}
                    closable
                    onClose={filterTag.onClose}
                  >
                    {filterTag.label}
                  </Tag>
                ))}
                <Button size="small" type="link" onClick={handleClearAllOuterFilters}>
                  {t('posAdmin.invoiceDetail.clearActiveFilters', '清空过滤')}
                </Button>
              </>
            ) : (
              <Tag>{t('posAdmin.invoiceDetail.noActiveFilters', '无')}</Tag>
            )}
          </Space>
        </div>

        {/* 工具栏按钮 */}
        <div style={{ marginBottom: 12 }}>
          <Space wrap>
            {isAdmin && (
              <Button
                icon={<SnippetsOutlined />}
                onClick={() => setPasteVisible(true)}
              >
                {t('posAdmin.invoiceDetail.pasteDataBtn', '粘贴数据')}
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<EditOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => setBatchEditVisible(true)}
              >
                {t('posAdmin.invoiceDetail.batchEditCount', '批量编辑 ({{count}})', { count: selectedRowKeys.length })}
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<SendOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => openStorePriceModal()}
              >
                {t('posAdmin.invoiceDetail.updateToStoreBtn', '更新到分店')}
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<CheckCircleOutlined />}
                loading={checking}
                disabled={checking}
                onClick={() => void handleCheckProducts()}
              >
                {t('posAdmin.invoiceDetail.productDetectBtn', '商品检测')}
              </Button>
            )}
            {canWriteLocalPurchaseToHq && (
              <Button
                icon={<CloudUploadOutlined />}
                loading={hqUpdateLoading}
                disabled={hqUpdateLoading || !selectedRowKeys.length}
                onClick={() => openHqUpdateModal()}
              >
                {t('posAdmin.invoiceDetail.updateHqProductsBtn', '更新HQ商品')}
              </Button>
            )}
            {canRunGlobalLocalPurchaseBatchActions && (
              <Button
                icon={<ThunderboltOutlined />}
                loading={executing}
                disabled={executing || !selectedRowKeys.length}
                onClick={() => void handleBatchExecute()}
              >
                {t('posAdmin.invoiceDetail.batchExecuteBtn', '批量执行操作')}
              </Button>
            )}
            {canRunGlobalLocalPurchaseBatchActions && (
              <Dropdown
                menu={{
                  items: ACTION_MENU_ITEMS(t),
                  onClick: ({ key }) => void handleBatchSetAction(key),
                }}
                disabled={!selectedRowKeys.length}
              >
                <Button icon={<PlusOutlined />} disabled={!selectedRowKeys.length}>
                  {t('posAdmin.invoiceDetail.batchSetActionBtn', '批量设置操作类型')}
                </Button>
              </Dropdown>
            )}
            {isAdmin && (
              <Button
                type="primary"
                loading={detailLoading}
                onClick={() => void handleSaveDetails()}
              >
                {t('posAdmin.invoiceDetail.saveDetailBtn2', '保存明细')}
              </Button>
            )}
            {isAdmin && (
              <Popconfirm
                title={t('posAdmin.invoiceDetail.confirmDeleteTitle', '确认删除选中的明细行吗？')}
                description={t('posAdmin.invoiceDetail.willDeleteCount', '将删除 {{count}} 条记录', { count: selectedRowKeys.length })}
                okText={t('posAdmin.invoiceDetail.delete', '删除')}
                cancelText={t('common.cancel', '取消')}
                okButtonProps={{ danger: true }}
                onConfirm={() => void handleDeleteSelected()}
              >
                <Button
                  icon={<DeleteOutlined />}
                  danger
                  disabled={!selectedRowKeys.length}
                >
                  {t('posAdmin.invoiceDetail.deleteSelectedCount', '删除选中 ({{count}})', { count: selectedRowKeys.length })}
                </Button>
              </Popconfirm>
            )}
          </Space>
        </div>

        {/* 明细表格 */}
        <Table
          rowKey="detailGUID"
          loading={detailLoading}
          dataSource={filteredDetails}
          columns={columns}
          pagination={false}
          onChange={handleTableChange}
          scroll={{ x: 1600, y: tableScrollY }}
          className="invoice-detail-compact-table"
          rowSelection={{
            fixed: true,
            columnWidth: 36,
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys),
          }}
          rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
          size="small"
        />
      </Card>

      {/* ============================================================ */}
      {/* 粘贴数据 Modal                                                 */}
      {/* ============================================================ */}
      <Modal
        open={pasteVisible}
        title={t('posAdmin.invoiceDetail.pasteTitle', '粘贴数据')}
        confirmLoading={pasteLoading}
        onCancel={() => {
          setPasteVisible(false)
          setPasteText('')
        }}
        onOk={() => void handlePaste()}
        width={700}
      >
        <div style={{ marginBottom: 16 }}>
          <Radio.Group
            value={pasteMode}
            onChange={(e) => setPasteMode(e.target.value)}
            optionType="button"
            buttonStyle="solid"
          >
            <Radio.Button value="append">{t('posAdmin.invoiceDetail.pasteModeAppend', '追加 (Append)')}</Radio.Button>
            <Radio.Button value="replace">{t('posAdmin.invoiceDetail.pasteModeReplace', '替换 (Replace)')}</Radio.Button>
          </Radio.Group>
          <span style={{ marginLeft: 12, color: '#999', fontSize: 12 }}>
            {pasteMode === 'append' ? t('posAdmin.invoiceDetail.appendDesc', '保留现有数据，追加新数据') : t('posAdmin.invoiceDetail.replaceDesc', '清除现有数据，替换为新数据')}
          </span>
        </div>
        <div style={{ marginBottom: 8, color: '#666', fontSize: 12 }}>
          {t('posAdmin.invoiceDetail.pasteHint', '请从 Excel 复制数据后粘贴到下方文本框。每行一条记录，可在下方调整列对应字段（Tab 分隔）')}
        </div>
        <div style={{ marginBottom: 12 }}>
          <Space size={8} align="center" wrap>
            {/* 只影响粘贴映射为“零售价”的列，进货价和新自动零售价保持原始粘贴值。 */}
            <Switch
              size="small"
              checked={normalizeRetailPriceOnPaste}
              onChange={setNormalizeRetailPriceOnPaste}
            />
            <span style={{ color: '#666', fontSize: 12 }}>
              {t('posAdmin.invoiceDetail.normalizeRetailPriceOnPaste', '零售价小数规范化')}
            </span>
            <span style={{ color: '#999', fontSize: 12 }}>
              {t('posAdmin.invoiceDetail.normalizeRetailPriceOnPasteHint', '5→4.99，4.1→4.50，4.6→4.99；1和2不变')}
            </span>
          </Space>
        </div>
        <div style={{ marginBottom: 12 }}>
          <Space style={{ marginBottom: 8, width: '100%', justifyContent: 'space-between' }}>
            <span style={{ color: '#666', fontSize: 12 }}>
              {t('posAdmin.invoiceDetail.pasteFieldOrderTitle', '列对应字段')}
            </span>
            <Button size="small" onClick={() => setPasteFieldOrder([...defaultPasteFieldOrder])}>
              {t('posAdmin.invoiceDetail.pasteRestoreDefaultOrder', '恢复默认')}
            </Button>
          </Space>
          <Row gutter={[8, 8]}>
            {pasteFieldOrder.map((field, index) => (
              <Col span={8} key={`paste-field-${index}`}>
                <div style={{ color: '#999', fontSize: 12, marginBottom: 4 }}>
                  {t('posAdmin.invoiceDetail.pasteColumnLabel', '第 {{index}} 列', { index: index + 1 })}
                </div>
                <Select<PasteFieldKey>
                  size="small"
                  value={field}
                  options={pasteFieldOptions}
                  style={{ width: '100%' }}
                  onChange={(nextField) => {
                    setPasteFieldOrder((prev) => prev.map((item, itemIndex) => (itemIndex === index ? nextField : item)))
                  }}
                />
              </Col>
            ))}
          </Row>
          {hasDuplicatePasteField && (
            <div style={{ color: '#cf1322', fontSize: 12, marginTop: 8 }}>
              {t('posAdmin.invoiceDetail.pasteFieldDuplicateWarning', '同一个字段不能选择多次，请把多余列设置为“跳过此列”')}
            </div>
          )}
        </div>
        <Input.TextArea
          rows={12}
          value={pasteText}
          onChange={(e) => setPasteText(e.target.value)}
          placeholder={t('posAdmin.invoiceDetail.pastePlaceholder', '从 Excel 复制数据后粘贴到此处...')}
          style={{ fontFamily: 'monospace' }}
        />
        {pasteText.trim() && (
          <div style={{ marginTop: 8, color: '#999', fontSize: 12 }}>
            {t('posAdmin.invoiceDetail.parsedRows', '已识别 {{count}} 行数据', { count: parsePasteText(pasteText, pasteFieldOrder, pasteParseOptions).length })}
          </div>
        )}
      </Modal>

      {/* ============================================================ */}
      {/* 批量编辑 Modal                                                 */}
      {/* ============================================================ */}
      <Modal
        open={batchEditVisible}
        title={t('posAdmin.invoiceDetail.editCountTitle', '批量编辑 ({{count}} 条)', { count: selectedRowKeys.length })}
        confirmLoading={batchEditLoading}
        onCancel={() => {
          setBatchEditVisible(false)
          batchEditForm.resetFields()
        }}
        onOk={() => void handleBatchEdit()}
        width={600}
      >
        <Form form={batchEditForm} layout="vertical">
          <Form.Item name="updatePurchasePrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.purchasePriceCheckbox', '进货价')}</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}
          >
            {({ getFieldValue }) =>
              getFieldValue('updatePurchasePrice') ? (
                <Form.Item name="purchasePrice" label={t('posAdmin.invoiceDetail.purchasePriceLabel', '进货价')} style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.retailPriceCheckbox', '零售价')}</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateRetailPrice !== cur.updateRetailPrice}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateRetailPrice') ? (
                <Form.Item name="retailPrice" label={t('posAdmin.invoiceDetail.retailPriceLabel', '零售价')} style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.autoPricingCheckbox', '自动定价')}</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateIsAutoPricing !== cur.updateIsAutoPricing}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateIsAutoPricing') ? (
                <Form.Item name="isAutoPricing" label={t('posAdmin.invoiceDetail.autoPricingLabel', '自动定价')} style={{ marginLeft: 24 }}>
                  <Select
                    options={[
                      { label: t('posAdmin.invoiceDetail.yes', '是'), value: true },
                      { label: t('posAdmin.invoiceDetail.no', '否'), value: false },
                    ]}
                  />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateIsSpecialProduct" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.specialProductLabel', '特殊商品')}</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateIsSpecialProduct !== cur.updateIsSpecialProduct}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateIsSpecialProduct') ? (
                <Form.Item name="isSpecialProduct" label={t('posAdmin.invoiceDetail.specialProductLabel', '特殊商品')} style={{ marginLeft: 24 }}>
                  <Select
                    options={[
                      { label: t('posAdmin.invoiceDetail.yes', '是'), value: true },
                      { label: t('posAdmin.invoiceDetail.no', '否'), value: false },
                    ]}
                  />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateDiscountRate" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.discountRate', '折扣率')}</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateDiscountRate') ? (
                <Form.Item name="discountRate" label={t('posAdmin.invoiceDetail.discountRate', '折扣率')} style={{ marginLeft: 24 }}>
                  <InputNumber
                    min={0}
                    max={100}
                    step={1}
                    precision={1}
                    addonAfter="%"
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              ) : null
            }
          </Form.Item>
        </Form>
      </Modal>

      {/* ============================================================ */}
      {/* 更新到分店价格 Modal                                            */}
      {/* ============================================================ */}
      <Modal
        open={storePriceVisible}
        title={t('posAdmin.invoiceDetail.updateToStorePriceTitle2', '更新到分店价格 ({{count}} 条)', { count: selectedRowKeys.length })}
        confirmLoading={storePriceLoading}
        onCancel={() => {
          setStorePriceVisible(false)
          storePriceForm.resetFields()
        }}
        onOk={() => void handleUpdateToStorePrices()}
        width={600}
      >
        <Form form={storePriceForm} layout="vertical">
          <Form.Item
            name="targetStoreCodes"
            label={t('posAdmin.invoiceDetail.targetStoreLabel', '目标分店')}
            rules={[{ required: true, message: t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店') }]}
          >
            <Select
              mode="multiple"
              showSearch
              allowClear
              optionFilterProp="label"
              placeholder={t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店')}
              options={storeOptions}
              popupRender={(menu) => (
                <>
                  <div style={{ padding: '4px 8px 8px', borderBottom: '1px solid #f0f0f0' }}>
                    {/* 全选只写入当前可选分店编码，提交和权限校验仍走原来的 targetStoreCodes 数组。 */}
                    <Checkbox
                      checked={allStorePriceStoresSelected}
                      indeterminate={hasPartialStorePriceStoreSelection}
                      disabled={!allStoreCodes.length}
                      onChange={(event) => {
                        storePriceForm.setFieldValue('targetStoreCodes', event.target.checked ? allStoreCodes : [])
                      }}
                    >
                      {t('posAdmin.invoiceDetail.selectAllStores', '全选所有分店 ({{count}} 个)', { count: allStoreCodes.length })}
                    </Checkbox>
                  </div>
                  {menu}
                </>
              )}
            />
          </Form.Item>

          <Form.Item name="updatePurchasePrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updatePurchasePriceLabel', '更新进货价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateRetailPrice', '更新零售价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateAutoPricing', '更新自动定价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsSpecialProduct" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateSpecialProduct', '更新特殊商品')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateDiscountRate" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateDiscountRate', '更新折扣率')}</Checkbox>
          </Form.Item>
        </Form>
      </Modal>

      {/* ============================================================ */}
      {/* 更新 HQ 商品 Modal                                             */}
      {/* ============================================================ */}
      <Modal
        open={hqUpdateVisible}
        title={t('posAdmin.invoiceDetail.updateHqProductsTitle', '更新HQ商品 ({{count}} 条)', { count: selectedRowKeys.length })}
        confirmLoading={hqUpdateLoading}
        onCancel={() => {
          setHqUpdateVisible(false)
          hqUpdateForm.resetFields()
        }}
        onOk={() => void handleUpdateHqProducts()}
        width={600}
      >
        <Form form={hqUpdateForm} layout="vertical">
          <Form.Item
            name="targetStoreCodes"
            label={t('posAdmin.invoiceDetail.targetStoreLabel', '目标分店')}
            rules={[{ required: true, message: t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店') }]}
          >
            <Select
              mode="multiple"
              showSearch
              allowClear
              optionFilterProp="label"
              placeholder={t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店')}
              options={storeOptions}
              popupRender={(menu) => (
                <>
                  <div style={{ padding: '4px 8px 8px', borderBottom: '1px solid #f0f0f0' }}>
                    {/* 全选只写入当前可选分店编码，提交和权限校验仍走原来的 targetStoreCodes 数组。 */}
                    <Checkbox
                      checked={allHqUpdateStoresSelected}
                      indeterminate={hasPartialHqUpdateStoreSelection}
                      disabled={!allStoreCodes.length}
                      onChange={(event) => {
                        hqUpdateForm.setFieldValue('targetStoreCodes', event.target.checked ? allStoreCodes : [])
                      }}
                    >
                      {t('posAdmin.invoiceDetail.selectAllStores', '全选所有分店 ({{count}} 个)', { count: allStoreCodes.length })}
                    </Checkbox>
                  </div>
                  {menu}
                </>
              )}
            />
          </Form.Item>

          {/* 只把勾选字段提交给 HQ，后端据此避免改动未指定字段。 */}
          <Form.Item name="updatePurchasePrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updatePurchasePriceLabel', '更新进货价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateRetailPrice', '更新零售价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateAutoPricing', '更新自动定价')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsSpecialProduct" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateSpecialProduct', '更新特殊商品')}</Checkbox>
          </Form.Item>
          <Form.Item name="updateDiscountRate" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.updateDiscountRate', '更新折扣率')}</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
