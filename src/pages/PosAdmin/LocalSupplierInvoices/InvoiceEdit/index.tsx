import {
  CheckCircleOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,

  PlusOutlined,
  RollbackOutlined,
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
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { CSSProperties } from 'react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useStableRouteContext } from '../../../../hooks/useStableRouteContext'
import BarcodePreview from '../../../../components/BarcodePreview'
import {
  batchExecuteActions,
  batchUpdateDetailAction,
  batchUpsertDetails,
  checkProducts,
  deleteDetails,
  getInvoice,
  getInvoiceDetails,
  pasteDetails,
  updateHqProducts,
  updateInvoice,
  updateToStorePrices,
} from '../../../../services/localSupplierInvoiceService'
import { getActiveStores } from '../../../../services/storeService'
import { useAuthStore } from '../../../../store/auth'
import type {
  BatchEditFields,
  BatchExecuteActionsResult,
  CheckProductsResponse,
  DetailAction,
  EnsureHqProductError,
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  UpdateHqProductsResult,
  UpdateToStorePricesFields,
  UpdateToStorePricesRequest,
} from '../../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../../utils/clipboard'
import { discountRateToDecimal, formatDiscountRate } from '../../../../utils/discountRate'
import { RequestError } from '../../../../utils/request'
import { DetailAction as DetailActionEnum } from '../../../../types/localSupplierInvoice'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
} from '../../../../utils/managedStoreScope'
import {
  filterInvoiceDetails,
  getBarcodeStatusFilter,
  getDetailStatusStats,
  getProductStatusFilter,
  toggleStatusFilter,
} from './statusFilters'
import { parsePasteText } from './pasteDetails'
import type {
  BarcodeStatusFilter,
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

const statusStatsTagColors = {
  product: { all: 'blue', notDetected: 'purple', exists: 'green', notExists: 'red' },
  barcode: { all: 'geekblue', notDetected: 'purple', normal: 'cyan', noMatch: 'volcano', multiMatch: 'orange' },
} as const

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

function getBatchExecuteFailure(error: unknown): BatchExecuteActionsResult | undefined {
  if (!(error instanceof RequestError)) return undefined

  const payload = error.payload as { data?: unknown; details?: unknown } | undefined
  const candidate = (payload?.details ?? payload?.data) as Partial<BatchExecuteActionsResult> | undefined
  if (!candidate || typeof candidate !== 'object') return undefined

  return {
    createdProducts: Number(candidate.createdProducts ?? 0),
    updatedPurchasePrices: Number(candidate.updatedPurchasePrices ?? 0),
    updatedItemNumbers: Number(candidate.updatedItemNumbers ?? 0),
    addedMultiCodes: Number(candidate.addedMultiCodes ?? 0),
    skipped: Number(candidate.skipped ?? 0),
    failed: Number(candidate.failed ?? 0),
    errors: Array.isArray(candidate.errors) ? candidate.errors.map(String) : [],
  }
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

/* ------------------------------------------------------------------ */
/*  可编辑本次进货价单元格                                              */
/* ------------------------------------------------------------------ */

function EditablePriceCell({
  value,
  lastPurchasePrice,
  detailGuid,
  invoiceGuid: _invoiceGuid,
  onSave,
}: {
  value?: number
  lastPurchasePrice?: number
  detailGuid: string
  invoiceGuid: string
  onSave: (guid: string, newPrice: number) => void
}) {
  const [editing, setEditing] = useState(false)
  const { t } = useTranslation()
  const [inputValue, setInputValue] = useState<string>(value?.toFixed(2) ?? '')
  const [_saving, setSaving] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    setInputValue(value?.toFixed(2) ?? '')
  }, [value])

  useEffect(() => {
    if (editing && inputRef.current) {
      inputRef.current?.focus()
      inputRef.current?.select()
    }
  }, [editing])

  const handleSave = useCallback(async () => {
    const newPrice = Number(inputValue)
    if (Number.isNaN(newPrice) || newPrice < 0) {
      message.error(t('posAdmin.invoiceDetail.invalidPrice', '请输入有效的价格'))
      return
    }
    if (newPrice === value) {
      setEditing(false)
      return
    }
    setSaving(true)
    try {
      onSave(detailGuid, newPrice)
      setEditing(false)
    } finally {
      setSaving(false)
    }
  }, [inputValue, value, detailGuid, onSave])

  const bg = getPriceChangeBg(lastPurchasePrice, value)
  const bgStyle = bg ? { backgroundColor: bg, padding: '2px 6px', borderRadius: 4 } : undefined

  if (editing) {
    return (
      <InputNumber
        ref={inputRef as any}
        size="small"
        min={0}
        precision={2}
        value={Number(inputValue)}
        onChange={(v) => setInputValue(v?.toFixed(2) ?? '')}
        onPressEnter={() => void handleSave()}
        onBlur={() => void handleSave()}
        style={{ width: 90 }}
      />
    )
  }

  return (
    <span
      style={{ ...bgStyle, cursor: 'pointer' }}
      onClick={() => setEditing(true)}
    >
      {formatAmount(value)}
    </span>
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
  const canWriteLocalPurchaseToHq = access.canEditLocalPurchase && access.canPushLocalPurchaseToHq
  const managedStoreCodes = access.managedStoreCodes()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'

  /* ---- 主表数据 ---- */
  const [_invoice, setInvoice] = useState<LocalSupplierInvoiceDetailDto | null>(null)
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

  /* ---- 表单 ---- */
  const [form] = Form.useForm()

  /* ---- 分店选项 ---- */
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])

  /* ---- 粘贴数据 Modal ---- */
  const [pasteVisible, setPasteVisible] = useState(false)
  const [pasteMode, setPasteMode] = useState<'append' | 'replace'>('append')
  const [pasteText, setPasteText] = useState('')
  const [pasteLoading, setPasteLoading] = useState(false)

  /* ---- 批量编辑 Modal ---- */
  const [batchEditVisible, setBatchEditVisible] = useState(false)
  const [batchEditForm] = Form.useForm()
  const [batchEditLoading, setBatchEditLoading] = useState(false)

  /* ---- 更新到分店价格 Modal ---- */
  const [storePriceVisible, setStorePriceVisible] = useState(false)
  const [storePriceForm] = Form.useForm()
  const [storePriceLoading, setStorePriceLoading] = useState(false)

  /* ---- 更新 HQ 商品 Modal ---- */
  const [hqUpdateVisible, setHqUpdateVisible] = useState(false)
  const [hqUpdateForm] = Form.useForm()
  const [hqUpdateLoading, setHqUpdateLoading] = useState(false)
  const hqUpdateIdempotencyKeyRef = useRef<string | null>(null)

  /* ---- 商品检测 ---- */
  const [checking, setChecking] = useState(false)

  /* ---- 批量执行操作 ---- */
  const [executing, setExecuting] = useState(false)

  /* ---- 动态表格高度 ---- */
  const tableCardRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number>(400)

  /* ================================================================ */
  /*  数据加载                                                         */
  /* ================================================================ */

  const loadInvoice = useCallback(async () => {
    if (!invoiceGuid) return false
    setLoading(true)
    try {
      const data = await getInvoice(invoiceGuid)
      if (!isStoreCodeInManagedScope(data.storeCode, managedStoreCodes)) {
        setCanAccessInvoice(false)
        setInvoice(null)
        setDetails([])
        setSelectedRowKeys([])
        setRowActions({})
        form.resetFields()
        message.error(t('message.noPermission', '无权查看该数据'))
        return false
      }
      setCanAccessInvoice(true)
      setInvoice(data)
      form.setFieldsValue({
        invoiceNo: data.invoiceNo,
        storeName: data.storeName ? `${data.storeCode} - ${data.storeName}` : data.storeCode,
        supplierName: data.supplierName
          ? `${data.supplierCode} - ${data.supplierName}`
          : data.supplierCode,
        orderDate: data.orderDate ? dayjs(data.orderDate) : undefined,
        inboundDate: data.inboundDate ? dayjs(data.inboundDate) : undefined,
        totalAmount: formatAmount(data.totalAmount),
        remarks: data.remarks,
      })
      return true
    } catch {
      message.error(t('posAdmin.invoiceDetail.loadInvoiceFailed', '加载进货单失败'))
      return false
    } finally {
      setLoading(false)
    }
  }, [invoiceGuid, form, managedStoreCodeKey, t])

  const loadDetails = useCallback(async () => {
    if (!invoiceGuid) return
    setDetailLoading(true)
    try {
      const data = await getInvoiceDetails(invoiceGuid)
      setDetails(data)
      setRowActions(
        Object.fromEntries(
          data
            .filter((item) => item.activityType !== undefined && item.activityType !== null)
            .map((item) => [item.detailGUID, item.activityType as number]),
        ),
      )
    } catch {
      message.error(t('posAdmin.invoiceDetail.loadDetailsFailed', '加载明细失败'))
    } finally {
      setDetailLoading(false)
    }
  }, [invoiceGuid, t])

  useEffect(() => {
    void (async () => {
      if (await loadInvoice()) {
        await loadDetails()
      }
    })()
    if (managedStoreCodes === null) {
      getActiveStores()
        .then((stores) => {
          setStoreOptions(filterStoreOptionsByManagedCodes(stores, managedStoreCodes))
        })
        .catch(() => setStoreOptions([]))
    } else {
      setStoreOptions(buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true }))
    }
  }, [currentUser?.stores, loadInvoice, loadDetails, managedStoreCodeKey])

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
  const detailStatusStats = useMemo(() => getDetailStatusStats(details), [details])

  // 过滤后数据
  const filteredDetails = useMemo(
    () =>
      filterInvoiceDetails(details, {
        searchText,
        priceFilter,
        productStatusFilter,
        barcodeStatusFilter,
      }),
    [details, searchText, priceFilter, productStatusFilter, barcodeStatusFilter],
  )

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

  // ---- 行内编辑进货价 ----
  const handlePriceSave = useCallback(
    (detailGuid: string, newPrice: number) => {
      setDetails((prev) =>
        prev.map((d) =>
          d.detailGUID === detailGuid
            ? { ...d, purchasePrice: newPrice, amount: (d.quantity ?? 0) * newPrice }
            : d,
        ),
      )
    },
    [],
  )

  // ---- 批量保存明细（含行内价格编辑） ----
  const handleSaveDetails = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    const items: InvoiceDetailUpsertItemDto[] = details.map((d) => ({
      detailGUID: d.detailGUID,
      itemNumber: d.itemNumber,
      barcode: d.barcode,
      productName: d.productName,
      quantity: d.quantity,
      purchasePrice: d.purchasePrice,
      retailPrice: d.retailPrice,
      amount: d.amount,
      autoPricing: d.autoPricing,
      pricingFloatRate: d.pricingFloatRate,
      newAutoRetailPrice: d.newAutoRetailPrice,
      isSpecialProduct: d.isSpecialProduct,
      discountRate: d.discountRate,
    }))
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
    const parsed = parsePasteText(pasteText)
    if (!parsed.length) {
      message.warning(t('posAdmin.invoiceDetail.noValidData', '未检测到有效数据'))
      return
    }
    setPasteLoading(true)
    try {
      const result = await pasteDetails({
        invoiceGuid,
        mode: pasteMode,
        items: parsed,
      })
      message.success(
        t('posAdmin.invoiceDetail.pasteComplete', '粘贴完成：新增 {{inserted}} 条，更新 {{updated}} 条，失败 {{failed}} 条', { inserted: result?.inserted ?? 0, updated: result?.updated ?? 0, failed: result?.failed ?? 0 }),
      )
      setPasteVisible(false)
      setPasteText('')
      loadDetails()
    } catch {
      message.error(t('posAdmin.invoiceDetail.pasteFailed', '粘贴数据失败'))
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
      await batchUpsertDetails(invoiceGuid, items)
      // 使用 batchUpdateDetails 的替代方案：先 upert 再 reload
      // 这里实际应该用 batchUpdateDetails，但由于 batchUpsertDetails 不支持 editFields
      // 改用本地更新方式
      const updatedDetails = details.map((d) => {
        if (!selectedRowKeys.includes(d.detailGUID)) return d
        const updated = { ...d }
        if (editFields.updatePurchasePrice && editFields.purchasePrice !== undefined) {
          updated.purchasePrice = editFields.purchasePrice
          updated.amount = (updated.quantity ?? 0) * editFields.purchasePrice
        }
        if (editFields.updateRetailPrice && editFields.retailPrice !== undefined) {
          updated.retailPrice = editFields.retailPrice
        }
        if (editFields.updateIsAutoPricing && editFields.isAutoPricing !== undefined) {
          updated.autoPricing = editFields.isAutoPricing
        }
        if (editFields.updateIsSpecialProduct && editFields.isSpecialProduct !== undefined) {
          updated.isSpecialProduct = editFields.isSpecialProduct
        }
        if (editFields.updateDiscountRate && editFields.discountRate !== undefined) {
          updated.discountRate = editFields.discountRate
        }
        return updated
      })
      setDetails(updatedDetails)
      message.success(t('posAdmin.invoiceDetail.batchUpdateSuccess', '批量更新成功'))
      setBatchEditVisible(false)
      batchEditForm.resetFields()
      setSelectedRowKeys([])
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

    setStorePriceLoading(true)
    try {
      const request: UpdateToStorePricesRequest = {
        invoiceGuid,
        detailGuids: selectedRowKeys.map(String),
        targetStoreCodes: values.targetStoreCodes,
        updateFields,
      }
      const result = await updateToStorePrices(request)
      message.success(
        t('posAdmin.invoiceDetail.updateToStoreResult', '更新完成：成功 {{updated}} 条，失败 {{failed}} 条', { updated: result?.updated ?? 0, failed: result?.failed ?? 0 }),
      )
      setStorePriceVisible(false)
      storePriceForm.resetFields()
      setSelectedRowKeys([])
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.updateToStoreFailed', '更新到分店价格失败'))
    } finally {
      setStorePriceLoading(false)
    }
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

    setHqUpdateLoading(true)
    try {
      // 同一轮提交使用稳定幂等键，避免用户重复点击造成 HQ 重复写入。
      hqUpdateIdempotencyKeyRef.current =
        hqUpdateIdempotencyKeyRef.current ??
        (typeof crypto !== 'undefined' && 'randomUUID' in crypto
          ? crypto.randomUUID()
          : `${invoiceGuid}-${detailGuids.join(',')}-${targetStoreCodes.join(',')}`)
      const result = await updateHqProducts(invoiceGuid, {
        detailGuids,
        targetStoreCodes,
        updateFields,
        idempotencyKey: hqUpdateIdempotencyKeyRef.current,
      })
      showUpdateHqProductsResult(result)
      setHqUpdateVisible(false)
      hqUpdateForm.resetFields()
      setSelectedRowKeys([])
      await loadDetails()
    } catch (error) {
      const failure = getUpdateHqProductsFailure(error)
      if (failure) {
        showUpdateHqProductsResult(failure)
      } else {
        message.error(error instanceof Error ? error.message : t('posAdmin.invoiceDetail.updateHqProductsFailed', '更新HQ商品失败'))
      }
    } finally {
      hqUpdateIdempotencyKeyRef.current = null
      setHqUpdateLoading(false)
    }
  }

  // ---- 商品检测 ----
  const handleCheckProducts = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!details.length) {
      message.warning(t('posAdmin.invoiceDetail.noDetailToDetect', '没有明细数据可检测'))
      return
    }
    setChecking(true)
    try {
      const result: CheckProductsResponse = await checkProducts({
        invoiceGuid,
        detailGuids: selectedRowKeys.length > 0 ? selectedRowKeys.map(String) : undefined,
      })
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

      message.success(
        t('posAdmin.invoiceDetail.detectCompleteMsg', '检测完成：共 {{total}}条，商品存在 {{productExists}}条，不存在 {{productNotExists}}条，条码正常 {{barcodeNormal}}条，异常 {{barcodeAbnormal}}条', result.summary),
      )
    } catch {
      message.error(t('posAdmin.invoiceDetail.detectFailed', '商品检测失败'))
    } finally {
      setChecking(false)
    }
  }

  // ---- 行操作类型变更 ----
  const handleRowActionChange = async (detailGuid: string, actionKey: string) => {
    if (!ensureCanAccessInvoice()) return
    const action = Number(actionKey) as DetailAction
    setRowActions((prev) => ({ ...prev, [detailGuid]: action }))
    setDetails((prev) =>
      prev.map((item) =>
        item.detailGUID === detailGuid ? { ...item, activityType: action } : item,
      ),
    )

    // 同步到服务端
    if (invoiceGuid) {
      try {
        const { updateDetailAction } = await import('../../../../services/localSupplierInvoiceService')
        await updateDetailAction(invoiceGuid, detailGuid, action)
      } catch {
        message.error(t('posAdmin.invoiceDetail.updateActionFailed', '更新操作类型失败'))
      }
    }
  }

  // ---- 批量执行操作 ----
  const handleBatchExecute = async () => {
    if (!invoiceGuid || !ensureCanAccessInvoice()) return
    if (!selectedRowKeys.length) {
      message.warning(t('posAdmin.invoiceDetail.selectDetailsFirst', '请先选择明细行'))
      return
    }
    setExecuting(true)
    try {
      const result: BatchExecuteActionsResult = await batchExecuteActions({
        invoiceGuid,
        detailGuids: selectedRowKeys.map(String),
      })
      const parts: string[] = []
      if (result.createdProducts > 0) parts.push(t('posAdmin.invoiceDetail.createdProducts', '新建商品{{count}}条', { count: result.createdProducts }))
      if (result.updatedPurchasePrices > 0) parts.push(t('posAdmin.invoiceDetail.updatedPurchasePrices', '更新进货价{{count}}条', { count: result.updatedPurchasePrices }))
      if (result.updatedItemNumbers > 0) parts.push(t('posAdmin.invoiceDetail.updatedItemNumbers', '更新货号{{count}}条', { count: result.updatedItemNumbers }))
      if (result.addedMultiCodes > 0) parts.push(t('posAdmin.invoiceDetail.addedMultiCodes', '添加多码{{count}}条', { count: result.addedMultiCodes }))
      if (result.skipped > 0) parts.push(t('posAdmin.invoiceDetail.skipped', '跳过{{count}}条', { count: result.skipped }))
      if (result.failed > 0) parts.push(t('posAdmin.invoiceDetail.failed', '失败{{count}}条', { count: result.failed }))
      if (result.errors?.length) {
        Modal.error({
          title: t('posAdmin.invoiceDetail.partialFailed', '部分操作失败'),
          content: (
            <div>
              <p>{parts.join('，')}</p>
              <div style={{ maxHeight: 200, overflow: 'auto', marginTop: 8 }}>
                {result.errors.map((err, i) => (
                  <div key={i} style={{ color: '#ff4d4f', fontSize: 12 }}>{err}</div>
                ))}
              </div>
            </div>
          ),
        })
      } else {
        message.success(t('posAdmin.invoiceDetail.executeResultMsg', '执行完成：{{parts}}', { parts: parts.join('，') || t('posAdmin.invoiceDetail.noOperation', '无操作') }))
      }
      setSelectedRowKeys([])
      loadDetails()
    } catch (error) {
      const failure = getBatchExecuteFailure(error)
      if (failure?.errors.length) {
        Modal.error({
          title: t('posAdmin.invoiceDetail.executeFailed', '批量执行操作失败'),
          content: (
            <div style={{ maxHeight: 240, overflow: 'auto' }}>
              {failure.errors.map((err, i) => (
                <div key={i} style={{ color: '#ff4d4f', fontSize: 12 }}>{err}</div>
              ))}
            </div>
          ),
        })
      } else {
        message.error(t('posAdmin.invoiceDetail.executeFailed', '批量执行操作失败'))
      }
    } finally {
      setExecuting(false)
    }
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
    } catch {
      message.error(t('posAdmin.invoiceDetail.batchSetActionFailed', '批量设置操作类型失败'))
    }
  }

  /* ================================================================ */
  /*  表格列定义                                                       */
  /* ================================================================ */

  const columns: ColumnsType<LocalSupplierInvoiceItemDto> = [
    {
      title: t('posAdmin.invoiceDetail.seqNo', '序号'),
      width: 50,
      align: 'right',
      fixed: 'left',
      render: (_, __, index) => index + 1,
    },
    {
      title: t('posAdmin.invoiceDetail.image', '图片'),
      dataIndex: 'productImage',
      width: 70,
      render: (v: string) =>
        v ? (
          <Image src={v} width={44} height={44} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <div
            style={{
              width: 44,
              height: 44,
              background: '#f5f5f5',
              borderRadius: 4,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#ccc',
              fontSize: 12,
            }}
          >
            {t('posAdmin.invoiceDetail.noImage', '无图')}
          </div>
        ),
    },
    {
      title: t('posAdmin.invoiceDetail.itemNumber', '货号'),
      dataIndex: 'itemNumber',
      width: 130,
      render: (v: string) => (
        <Space size={4}>
          <span>{v || '--'}</span>
          {v && (
            <Tooltip title={t('posAdmin.invoiceDetail.copyItemNumber', '复制货号')}>
              <Button
                type="text"
                size="small"
                icon={<CopyOutlined />}
                onClick={() => void copyTextToClipboard(v)}
              />
            </Tooltip>
          )}
        </Space>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.barcode', '条码'),
      dataIndex: 'barcode',
      width: 170,
      render: (v: string) => <BarcodePreview value={v} compactCopy textMaxWidth={120} />,
    },
    {
      title: t('posAdmin.invoiceDetail.productName', '商品名称'),
      dataIndex: 'productName',
      width: 180,
      render: (v: string) => <div style={productNameCellStyle}>{v || '--'}</div>,
    },
    {
      title: t('posAdmin.invoiceDetail.quantity', '数量'),
      dataIndex: 'quantity',
      width: 70,
      align: 'right',
      render: (v: number) => v ?? '--',
    },
    {
      title: t('posAdmin.invoiceDetail.lastPurchasePrice', '上次进货价'),
      dataIndex: 'lastPurchasePrice',
      width: 100,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.currentPurchasePrice', '本次进货价'),
      dataIndex: 'purchasePrice',
      width: 110,
      align: 'right',
      render: (v: number, record) => (
        <EditablePriceCell
          value={v}
          lastPurchasePrice={record.lastPurchasePrice}
          detailGuid={record.detailGUID}
          invoiceGuid={invoiceGuid!}
          onSave={handlePriceSave}
        />
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
      dataIndex: 'retailPrice',
      width: 90,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.pricingRate', '定价浮率'),
      dataIndex: 'pricingFloatRate',
      width: 90,
      align: 'right',
      render: (v: number) =>
        v !== undefined && v !== null ? `${(v * 100).toFixed(1)}%` : '--',
    },
    {
      title: t('posAdmin.invoiceDetail.newAutoRetailPrice', '新自动零售价'),
      dataIndex: 'newAutoRetailPrice',
      width: 110,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.autoPricingLabel', '自动定价'),
      dataIndex: 'autoPricing',
      width: 80,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? t('posAdmin.invoiceDetail.auto', '自动') : t('posAdmin.invoiceDetail.manual', '手动')}</Tag>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.specialProductLabel', '特殊商品'),
      dataIndex: 'isSpecialProduct',
      width: 80,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'orange' : 'default'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.discountRate', '折扣率'),
      dataIndex: 'discountRate',
      width: 80,
      align: 'right',
      render: (v: number) => formatDiscountRate(v),
    },
    {
      title: t('posAdmin.invoiceDetail.amount', '金额'),
      dataIndex: 'amount',
      width: 100,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.productStatus', '商品状态'),
      dataIndex: 'existingProductCount',
      width: 90,
      align: 'center',
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
      title: t('posAdmin.invoiceDetail.barcodeStatus', '条码状态'),
      dataIndex: 'barcodeMatchCount',
      width: 90,
      align: 'center',
      render: (_: number, record) => {
        const count = record.barcodeMatchCount ?? 0
        const status = getBarcodeStatusFilter(record)
        if (status === 'notDetected') {
          return <Tag color="default">{t('posAdmin.invoiceDetail.notDetected', '未检测')}</Tag>
        }
        if (status === 'normal') {
          return <Tag color="green">{t('posAdmin.invoiceDetail.normal', '正常')}</Tag>
        }
        if (status === 'noMatch') {
          return <Tag color="red">{t('posAdmin.invoiceDetail.noMatch', '无匹配')}</Tag>
        }
        return <Tag color="orange">{t('posAdmin.invoiceDetail.multiMatchShort', '多匹配({{count}})', { count })}</Tag>
      },
    },
    {
      title: t('posAdmin.invoiceDetail.action', '操作'),
      key: 'action',
      width: 110,
      fixed: 'right',
      render: (_, record) => {
        const currentAction = rowActions[record.detailGUID] ?? record.activityType ?? 0
        const actionConfig = DETAIL_ACTION_CONFIG(t)
        const config = actionConfig[currentAction] || actionConfig[0]
        return (
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
      <Card title={t('posAdmin.invoiceDetail.orderHeaderInfo', '订单头信息')} loading={loading} size="small">
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col span={4}>
              <Form.Item name="invoiceNo" label={t('posAdmin.invoiceDetail.orderNoLabel', '订单号')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={4}>
              <Form.Item name="storeName" label={t('posAdmin.invoiceDetail.storeLabel', '分店')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={4}>
              <Form.Item name="supplierName" label={t('posAdmin.invoiceDetail.supplierLabel', '供应商')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="orderDate" label={t('posAdmin.invoiceDetail.orderDate', '订单日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="inboundDate" label={t('posAdmin.invoiceDetail.inboundDate', '入库日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="totalAmount" label={t('posAdmin.invoiceDetail.totalAmountLabel', '总金额')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="remarks" label={t('posAdmin.invoiceDetail.remarksLabel', '备注')}>
                <Input placeholder={t('posAdmin.invoiceDetail.remarksPlaceholder', '备注')} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col>
              <Button type="primary" loading={saving} onClick={() => void handleSave()}>
                {t('posAdmin.invoiceDetail.saveBtn', '保存')}
              </Button>
            </Col>
            <Col>
              <Button
                icon={<RollbackOutlined />}
                onClick={() => navigate('/pos-admin/local-supplier-invoices')}
              >
                {t('posAdmin.invoiceDetail.returnToList', '返回列表')}
              </Button>
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
            {isAdmin && (
              <Button
                icon={<ThunderboltOutlined />}
                loading={executing}
                disabled={!selectedRowKeys.length}
                onClick={() => void handleBatchExecute()}
              >
                {t('posAdmin.invoiceDetail.batchExecuteBtn', '批量执行操作')}
              </Button>
            )}
            {isAdmin && (
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
          scroll={{ x: 2200, y: tableScrollY }}
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys),
          }}
          rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
          size="small"
          bordered
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
          {t('posAdmin.invoiceDetail.pasteHint', '请从 Excel 复制数据后粘贴到下方文本框。每行一条记录，列顺序为：货号 / 条码 / 商品名称 / 数量 / 进货价 / 新自动零售价 / 零售价（Tab 分隔）')}
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
            {t('posAdmin.invoiceDetail.parsedRows', '已识别 {{count}} 行数据', { count: parsePasteText(pasteText).length })}
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
              optionFilterProp="label"
              placeholder={t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店')}
              options={storeOptions}
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
              optionFilterProp="label"
              placeholder={t('posAdmin.invoiceDetail.selectTargetStore', '请选择目标分店')}
              options={storeOptions}
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
