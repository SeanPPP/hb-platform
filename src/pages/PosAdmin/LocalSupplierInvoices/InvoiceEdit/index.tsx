import {
  CheckCircleOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
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
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
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
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  ProductStatus,
  UpdateToStorePricesFields,
  UpdateToStorePricesRequest,
} from '../../../../types/localSupplierInvoice'
import { BarcodeStatus } from '../../../../types/localSupplierInvoice'
import { DetailAction as DetailActionEnum } from '../../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../../utils/clipboard'
import { getStableTagColor } from '../../../../utils/tagColors'

/* ------------------------------------------------------------------ */
/*  辅助函数                                                           */
/* ------------------------------------------------------------------ */

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
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
const DETAIL_ACTION_CONFIG: Record<number, { label: string; color: string }> = {
  [DetailActionEnum.None]: { label: '无', color: 'default' },
  [DetailActionEnum.CreateProduct]: { label: '新建商品', color: 'blue' },
  [DetailActionEnum.UpdatePurchasePrice]: { label: '更新进货价', color: 'green' },
  [DetailActionEnum.WaitForOperation]: { label: '等待操作', color: 'orange' },
  [DetailActionEnum.UpdateItemNumber]: { label: '更新货号', color: 'purple' },
  [DetailActionEnum.AddMultiCode]: { label: '添加多码', color: 'cyan' },
}

/** 商品状态标签 */
const PRODUCT_STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: '未知', color: 'default' },
  1: { label: '已存在', color: 'green' },
  2: { label: '不存在', color: 'red' },
}

/** 条码状态标签 */
const BARCODE_STATUS_MAP: Record<number, { label: string; color: string }> = {
  [BarcodeStatus.Unknown]: { label: '未知', color: 'default' },
  [BarcodeStatus.Normal]: { label: '正常', color: 'green' },
  [BarcodeStatus.Abnormal]: { label: '异常', color: 'red' },
}

/** 操作类型下拉菜单项 */
const ACTION_MENU_ITEMS = [
  { key: '0', label: <Tag color="default">无</Tag> },
  { key: '1', label: <Tag color="blue">新建商品</Tag> },
  { key: '2', label: <Tag color="green">更新进货价</Tag> },
  { key: '3', label: <Tag color="orange">等待操作</Tag> },
  { key: '4', label: <Tag color="purple">更新货号</Tag> },
  { key: '5', label: <Tag color="cyan">添加多码</Tag> },
]

/** 粘贴数据解析 - 从文本解析 Tab 分隔的行 */
interface ParsedPasteRow {
  itemNumber?: string
  barcode?: string
  productName?: string
  quantity?: number
  purchasePrice?: number
  newAutoRetailPrice?: number
  retailPrice?: number
}

function parsePasteText(text: string): ParsedPasteRow[] {
  if (!text.trim()) return []
  const lines = text.split('\n').filter((l) => l.trim())
  return lines.map((line) => {
    const cols = line.split('\t')
    const parseNum = (v?: string) => {
      if (!v?.trim()) return undefined
      const n = Number(v.trim().replace(/,/g, ''))
      return Number.isNaN(n) ? undefined : n
    }
    return {
      itemNumber: cols[0]?.trim() || undefined,
      barcode: cols[1]?.trim() || undefined,
      productName: cols[2]?.trim() || undefined,
      quantity: parseNum(cols[3]),
      purchasePrice: parseNum(cols[4]),
      newAutoRetailPrice: parseNum(cols[5]),
      retailPrice: parseNum(cols[6]),
    }
  })
}

/* ------------------------------------------------------------------ */
/*  可编辑本次进货价单元格                                              */
/* ------------------------------------------------------------------ */

function EditablePriceCell({
  value,
  lastPurchasePrice,
  detailGuid,
  invoiceGuid,
  onSave,
}: {
  value?: number
  lastPurchasePrice?: number
  detailGuid: string
  invoiceGuid: string
  onSave: (guid: string, newPrice: number) => void
}) {
  const [editing, setEditing] = useState(false)
  const [inputValue, setInputValue] = useState<string>(value?.toFixed(2) ?? '')
  const [saving, setSaving] = useState(false)
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
      message.error('请输入有效的价格')
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
        loading={saving}
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
  const route = useStableRouteContext()
  const invoiceGuid = route?.params.id
  const navigate = useNavigate()
  const { access } = useAuthStore()
  const isAdmin = access.isAdmin

  /* ---- 主表数据 ---- */
  const [invoice, setInvoice] = useState<LocalSupplierInvoiceDetailDto | null>(null)
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
  const [priceFilter, setPriceFilter] = useState<'all' | 'up' | 'down'>('all')

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
    if (!invoiceGuid) return
    setLoading(true)
    try {
      const data = await getInvoice(invoiceGuid)
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
    } catch {
      message.error('加载进货单失败')
    } finally {
      setLoading(false)
    }
  }, [invoiceGuid, form])

  const loadDetails = useCallback(async () => {
    if (!invoiceGuid) return
    setDetailLoading(true)
    try {
      const data = await getInvoiceDetails(invoiceGuid)
      setDetails(data)
    } catch {
      message.error('加载明细失败')
    } finally {
      setDetailLoading(false)
    }
  }, [invoiceGuid])

  useEffect(() => {
    loadInvoice()
    loadDetails()
    getActiveStores().then(setStoreOptions).catch(() => {})
  }, [invoiceGuid]) // eslint-disable-line react-hooks/exhaustive-deps

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

  // 过滤后数据
  const filteredDetails = useMemo(() => {
    let result = details

    // 搜索
    if (searchText.trim()) {
      const kw = searchText.trim().toLowerCase()
      result = result.filter(
        (item) =>
          item.productCode?.toLowerCase().includes(kw) ||
          item.itemNumber?.toLowerCase().includes(kw) ||
          item.barcode?.toLowerCase().includes(kw) ||
          item.productName?.toLowerCase().includes(kw) ||
          item.storeProductCode?.toLowerCase().includes(kw),
      )
    }

    // 涨跌过滤
    if (priceFilter === 'up') {
      result = result.filter(
        (item) =>
          item.lastPurchasePrice !== undefined &&
          item.lastPurchasePrice !== null &&
          item.lastPurchasePrice > 0 &&
          item.purchasePrice !== undefined &&
          item.purchasePrice !== null &&
          item.purchasePrice > item.lastPurchasePrice,
      )
    } else if (priceFilter === 'down') {
      result = result.filter(
        (item) =>
          item.lastPurchasePrice !== undefined &&
          item.lastPurchasePrice !== null &&
          item.lastPurchasePrice > 0 &&
          item.purchasePrice !== undefined &&
          item.purchasePrice !== null &&
          item.purchasePrice < item.lastPurchasePrice,
      )
    }

    return result
  }, [details, searchText, priceFilter])

  /* ================================================================ */
  /*  操作处理函数                                                      */
  /* ================================================================ */

  // ---- 保存主表 ----
  const handleSave = async () => {
    if (!invoiceGuid) return
    const values = await form.validateFields()
    setSaving(true)
    try {
      await updateInvoice(invoiceGuid, {
        orderDate: values.orderDate?.format?.('YYYY-MM-DD') || undefined,
        inboundDate: values.inboundDate?.format?.('YYYY-MM-DD') || undefined,
        remarks: values.remarks?.trim() || undefined,
      })
      message.success('保存成功')
      loadInvoice()
    } catch {
      message.error('保存失败')
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
    if (!invoiceGuid) return
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
      message.success('明细保存成功')
      loadDetails()
    } catch {
      message.error('明细保存失败')
    } finally {
      setDetailLoading(false)
    }
  }

  // ---- 粘贴数据 ----
  const handlePaste = async () => {
    if (!invoiceGuid) return
    const parsed = parsePasteText(pasteText)
    if (!parsed.length) {
      message.warning('未检测到有效数据')
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
        `粘贴完成：新增${result?.inserted ?? 0} 条，更新${result?.updated ?? 0} 条，失败${result?.failed ?? 0} 条`,
      )
      setPasteVisible(false)
      setPasteText('')
      loadDetails()
    } catch {
      message.error('粘贴数据失败')
    } finally {
      setPasteLoading(false)
    }
  }

  // ---- 批量编辑 ----
  const handleBatchEdit = async () => {
    if (!invoiceGuid) return
    if (!selectedRowKeys.length) {
      message.warning('请先选择明细行')
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
      discountRate: values.updateDiscountRate ? values.discountRate : undefined,
      updateAction: false,
    }

    const hasAnyField =
      editFields.updatePurchasePrice ||
      editFields.updateRetailPrice ||
      editFields.updateIsAutoPricing ||
      editFields.updateIsSpecialProduct ||
      editFields.updateDiscountRate
    if (!hasAnyField) {
      message.warning('请至少选择一个要更新的字段')
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
      message.success('批量更新成功')
      setBatchEditVisible(false)
      batchEditForm.resetFields()
      setSelectedRowKeys([])
    } catch {
      message.error('批量更新失败')
    } finally {
      setBatchEditLoading(false)
    }
  }

  // ---- 更新到分店价格 ----
  const handleUpdateToStorePrices = async () => {
    if (!invoiceGuid) return
    if (!selectedRowKeys.length) {
      message.warning('请先选择明细行')
      return
    }
    const values = await storePriceForm.validateFields()
    if (!values.targetStoreCodes?.length) {
      message.warning('请选择目标分店')
      return
    }

    const updateFields: UpdateToStorePricesFields = {
      updatePurchasePrice: values.updatePurchasePrice ?? false,
      updateRetailPrice: values.updateRetailPrice ?? false,
      updateIsAutoPricing: values.updateIsAutoPricing ?? false,
      updateIsSpecialProduct: values.updateIsSpecialProduct ?? false,
      updateDiscountRate: values.updateDiscountRate ?? false,
    }

    const hasAnyField =
      updateFields.updatePurchasePrice ||
      updateFields.updateRetailPrice ||
      updateFields.updateIsAutoPricing ||
      updateFields.updateIsSpecialProduct ||
      updateFields.updateDiscountRate
    if (!hasAnyField) {
      message.warning('请至少选择一个要更新的字段')
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
        `更新完成：成功${result?.updated ?? 0} 条，失败${result?.failed ?? 0} 条`,
      )
      setStorePriceVisible(false)
      storePriceForm.resetFields()
      setSelectedRowKeys([])
    } catch {
      message.error('更新到分店价格失败')
    } finally {
      setStorePriceLoading(false)
    }
  }

  // ---- 商品检测 ----
  const handleCheckProducts = async () => {
    if (!invoiceGuid) return
    if (!details.length) {
      message.warning('没有明细数据可检测')
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
            existingProductCount: checkResult.existingProductCount,
            barcodeMatchCount: checkResult.barcodeMatchCount,
            autoPricing: checkResult.autoPricing ?? d.autoPricing,
            isSpecialProduct: checkResult.isSpecialProduct ?? d.isSpecialProduct,
            discountRate: checkResult.discountRate ?? d.discountRate,
            pricingFloatRate: checkResult.pricingFloatRate ?? d.pricingFloatRate,
            newAutoRetailPrice: checkResult.newAutoRetailPrice ?? d.newAutoRetailPrice,
            lastPurchasePrice: checkResult.lastPurchasePrice ?? d.lastPurchasePrice,
            storeProductCode: checkResult.storeProductCode ?? d.storeProductCode,
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
        `检测完成：共${result.summary.total}条，商品存在${result.summary.productExists}条，不存在${result.summary.productNotExists}条，条码正常${result.summary.barcodeNormal}条，异常${result.summary.barcodeAbnormal}条`,
      )
    } catch {
      message.error('商品检测失败')
    } finally {
      setChecking(false)
    }
  }

  // ---- 行操作类型变更 ----
  const handleRowActionChange = async (detailGuid: string, actionKey: string) => {
    const action = Number(actionKey) as DetailAction
    setRowActions((prev) => ({ ...prev, [detailGuid]: action }))

    // 同步到服务端
    if (invoiceGuid) {
      try {
        const { updateDetailAction } = await import('../../../../services/localSupplierInvoiceService')
        await updateDetailAction(invoiceGuid, detailGuid, action)
      } catch {
        message.error('更新操作类型失败')
      }
    }
  }

  // ---- 批量执行操作 ----
  const handleBatchExecute = async () => {
    if (!invoiceGuid) return
    if (!selectedRowKeys.length) {
      message.warning('请先选择明细行')
      return
    }
    setExecuting(true)
    try {
      const result: BatchExecuteActionsResult = await batchExecuteActions({
        invoiceGuid,
        detailGuids: selectedRowKeys.map(String),
      })
      const parts: string[] = []
      if (result.createdProducts > 0) parts.push(`新建商品${result.createdProducts}条`)
      if (result.updatedPurchasePrices > 0) parts.push(`更新进货价${result.updatedPurchasePrices}条`)
      if (result.updatedItemNumbers > 0) parts.push(`更新货号${result.updatedItemNumbers}条`)
      if (result.addedMultiCodes > 0) parts.push(`添加多码${result.addedMultiCodes}条`)
      if (result.skipped > 0) parts.push(`跳过${result.skipped}条`)
      if (result.failed > 0) parts.push(`失败${result.failed}条`)
      if (result.errors?.length) {
        Modal.error({
          title: '部分操作失败',
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
        message.success(`执行完成：${parts.join('，') || '无操作'}`)
      }
      setSelectedRowKeys([])
      loadDetails()
    } catch {
      message.error('批量执行操作失败')
    } finally {
      setExecuting(false)
    }
  }

  // ---- 删除选中 ----
  const handleDeleteSelected = async () => {
    if (!invoiceGuid) return
    if (!selectedRowKeys.length) {
      message.warning('请先选择要删除的明细行')
      return
    }
    setDetailLoading(true)
    try {
      await deleteDetails(invoiceGuid, selectedRowKeys.map(String))
      message.success('删除成功')
      setSelectedRowKeys([])
      loadDetails()
      loadInvoice()
    } catch {
      message.error('删除失败')
    } finally {
      setDetailLoading(false)
    }
  }

  // ---- 批量设置操作类型 ----
  const handleBatchSetAction = async (actionKey: string) => {
    if (!invoiceGuid || !selectedRowKeys.length) return
    const action = Number(actionKey)
    try {
      await batchUpdateDetailAction(invoiceGuid, selectedRowKeys.map(String), action)
      const newActions: Record<string, number> = {}
      selectedRowKeys.forEach((key) => {
        newActions[String(key)] = action
      })
      setRowActions((prev) => ({ ...prev, ...newActions }))
      message.success('批量设置操作类型成功')
    } catch {
      message.error('批量设置操作类型失败')
    }
  }

  /* ================================================================ */
  /*  表格列定义                                                       */
  /* ================================================================ */

  const columns: ColumnsType<LocalSupplierInvoiceItemDto> = [
    {
      title: '序号',
      width: 50,
      align: 'right',
      fixed: 'left',
      render: (_, __, index) => index + 1,
    },
    {
      title: '图片',
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
            无图
          </div>
        ),
    },
    {
      title: '货号',
      dataIndex: 'itemNumber',
      width: 130,
      render: (v: string) => (
        <Space size={4}>
          <span>{v || '--'}</span>
          {v && (
            <Tooltip title="复制货号">
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
      title: '条码',
      dataIndex: 'barcode',
      width: 170,
      render: (v: string) => <BarcodePreview value={v} compactCopy textMaxWidth={120} />,
    },
    {
      title: '商品名称',
      dataIndex: 'productName',
      width: 180,
      ellipsis: { showTitle: true },
      render: (v: string) => v || '--',
    },
    {
      title: '数量',
      dataIndex: 'quantity',
      width: 70,
      align: 'right',
      render: (v: number) => v ?? '--',
    },
    {
      title: '上次进货价',
      dataIndex: 'lastPurchasePrice',
      width: 100,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: '本次进货价',
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
      title: '零售价',
      dataIndex: 'retailPrice',
      width: 90,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: '定价浮率',
      dataIndex: 'pricingFloatRate',
      width: 90,
      align: 'right',
      render: (v: number) =>
        v !== undefined && v !== null ? `${(v * 100).toFixed(1)}%` : '--',
    },
    {
      title: '新自动零售价',
      dataIndex: 'newAutoRetailPrice',
      width: 110,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: '自动定价',
      dataIndex: 'autoPricing',
      width: 80,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? '自动' : '手动'}</Tag>
      ),
    },
    {
      title: '特殊商品',
      dataIndex: 'isSpecialProduct',
      width: 80,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'orange' : 'default'}>{v ? '是' : '否'}</Tag>
      ),
    },
    {
      title: '折扣率',
      dataIndex: 'discountRate',
      width: 80,
      align: 'right',
      render: (v: number) =>
        v !== undefined && v !== null ? `${(v * 100).toFixed(1)}%` : '--',
    },
    {
      title: '金额',
      dataIndex: 'amount',
      width: 100,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: '商品状态',
      dataIndex: 'existingProductCount',
      width: 90,
      align: 'center',
      render: (_: number, record) => {
        const count = record.existingProductCount
        if (count === undefined || count === null) {
          return <Tag color="default">未检测</Tag>
        }
        if (count > 0) {
          return <Tag color="green">已存在({count})</Tag>
        }
        return <Tag color="red">不存在</Tag>
      },
    },
    {
      title: '条码状态',
      dataIndex: 'barcodeMatchCount',
      width: 90,
      align: 'center',
      render: (_: number, record) => {
        const count = record.barcodeMatchCount
        if (count === undefined || count === null) {
          return <Tag color="default">未检测</Tag>
        }
        if (count === 1) {
          return <Tag color="green">正常</Tag>
        }
        if (count === 0) {
          return <Tag color="red">无匹配</Tag>
        }
        return <Tag color="orange">多匹配({count})</Tag>
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 110,
      fixed: 'right',
      render: (_, record) => {
        const currentAction = rowActions[record.detailGUID] ?? 0
        const config = DETAIL_ACTION_CONFIG[currentAction] || DETAIL_ACTION_CONFIG[0]
        return (
          <Dropdown
            menu={{
              items: ACTION_MENU_ITEMS,
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
      <Card title="订单头信息" loading={loading} size="small">
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col span={4}>
              <Form.Item name="invoiceNo" label="订单号">
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={4}>
              <Form.Item name="storeName" label="分店">
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={4}>
              <Form.Item name="supplierName" label="供应商">
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="orderDate" label="订单日期">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="inboundDate" label="入库日期">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="totalAmount" label="总金额">
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={3}>
              <Form.Item name="remarks" label="备注">
                <Input placeholder="备注" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col>
              <Button type="primary" loading={saving} onClick={() => void handleSave()}>
                保存
              </Button>
            </Col>
            <Col>
              <Button
                icon={<RollbackOutlined />}
                onClick={() => navigate('/pos-admin/local-supplier-invoices')}
              >
                返回列表
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
        title={`明细 (${details.length} 条)`}
        size="small"
        extra={
          <div ref={toolbarRef}>
            <Space wrap size={8}>
              {/* 搜索 */}
              <Input
                allowClear
                placeholder="搜索货号/条码/名称"
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
                涨价 ({priceStats.upCount})
              </Button>
              <Button
                type={priceFilter === 'down' ? 'primary' : 'default'}
                size="small"
                style={priceFilter === 'down' ? { background: '#52c41a', borderColor: '#52c41a' } : {}}
                onClick={() => setPriceFilter(priceFilter === 'down' ? 'all' : 'down')}
              >
                降价 ({priceStats.downCount})
              </Button>
            </Space>
          </div>
        }
      >
        {/* 工具栏按钮 */}
        <div style={{ marginBottom: 12 }}>
          <Space wrap>
            {isAdmin && (
              <Button
                icon={<SnippetsOutlined />}
                onClick={() => setPasteVisible(true)}
              >
                粘贴数据
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<EditOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => setBatchEditVisible(true)}
              >
                批量编辑 ({selectedRowKeys.length})
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<SendOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => setStorePriceVisible(true)}
              >
                更新到分店
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<CheckCircleOutlined />}
                loading={checking}
                onClick={() => void handleCheckProducts()}
              >
                商品检测
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<ThunderboltOutlined />}
                loading={executing}
                disabled={!selectedRowKeys.length}
                onClick={() => void handleBatchExecute()}
              >
                批量执行操作
              </Button>
            )}
            {isAdmin && (
              <Dropdown
                menu={{
                  items: ACTION_MENU_ITEMS,
                  onClick: ({ key }) => void handleBatchSetAction(key),
                }}
                disabled={!selectedRowKeys.length}
              >
                <Button icon={<PlusOutlined />} disabled={!selectedRowKeys.length}>
                  批量设置操作类型
                </Button>
              </Dropdown>
            )}
            {isAdmin && (
              <Button
                type="primary"
                loading={detailLoading}
                onClick={() => void handleSaveDetails()}
              >
                保存明细
              </Button>
            )}
            {isAdmin && (
              <Popconfirm
                title="确认删除选中的明细行吗？"
                description={`将删除 ${selectedRowKeys.length} 条记录`}
                okText="删除"
                cancelText="取消"
                okButtonProps={{ danger: true }}
                onConfirm={() => void handleDeleteSelected()}
              >
                <Button
                  icon={<DeleteOutlined />}
                  danger
                  disabled={!selectedRowKeys.length}
                >
                  删除选中 ({selectedRowKeys.length})
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
        title="粘贴数据"
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
            <Radio.Button value="append">追加 (Append)</Radio.Button>
            <Radio.Button value="replace">替换 (Replace)</Radio.Button>
          </Radio.Group>
          <span style={{ marginLeft: 12, color: '#999', fontSize: 12 }}>
            {pasteMode === 'append' ? '保留现有数据，追加新数据' : '清除现有数据，替换为新数据'}
          </span>
        </div>
        <div style={{ marginBottom: 8, color: '#666', fontSize: 12 }}>
          请从 Excel 复制数据后粘贴到下方文本框。每行一条记录，列顺序为：货号 / 条码 / 商品名称 / 数量 / 进货价 / 新自动零售价 / 零售价（Tab 分隔）
        </div>
        <Input.TextArea
          rows={12}
          value={pasteText}
          onChange={(e) => setPasteText(e.target.value)}
          placeholder="从 Excel 复制数据后粘贴到此处..."
          style={{ fontFamily: 'monospace' }}
        />
        {pasteText.trim() && (
          <div style={{ marginTop: 8, color: '#999', fontSize: 12 }}>
            已识别 {parsePasteText(pasteText).length} 行数据
          </div>
        )}
      </Modal>

      {/* ============================================================ */}
      {/* 批量编辑 Modal                                                 */}
      {/* ============================================================ */}
      <Modal
        open={batchEditVisible}
        title={`批量编辑 (${selectedRowKeys.length} 条)`}
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
            <Checkbox>进货价</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}
          >
            {({ getFieldValue }) =>
              getFieldValue('updatePurchasePrice') ? (
                <Form.Item name="purchasePrice" label="进货价" style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>零售价</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateRetailPrice !== cur.updateRetailPrice}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateRetailPrice') ? (
                <Form.Item name="retailPrice" label="零售价" style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>自动定价</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateIsAutoPricing !== cur.updateIsAutoPricing}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateIsAutoPricing') ? (
                <Form.Item name="isAutoPricing" label="自动定价" style={{ marginLeft: 24 }}>
                  <Select
                    options={[
                      { label: '是', value: true },
                      { label: '否', value: false },
                    ]}
                  />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateIsSpecialProduct" valuePropName="checked">
            <Checkbox>特殊商品</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateIsSpecialProduct !== cur.updateIsSpecialProduct}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateIsSpecialProduct') ? (
                <Form.Item name="isSpecialProduct" label="特殊商品" style={{ marginLeft: 24 }}>
                  <Select
                    options={[
                      { label: '是', value: true },
                      { label: '否', value: false },
                    ]}
                  />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateDiscountRate" valuePropName="checked">
            <Checkbox>折扣率</Checkbox>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}
          >
            {({ getFieldValue }) =>
              getFieldValue('updateDiscountRate') ? (
                <Form.Item name="discountRate" label="折扣率" style={{ marginLeft: 24 }}>
                  <InputNumber
                    min={0}
                    max={1}
                    step={0.01}
                    precision={2}
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
        title={`更新到分店价格 (${selectedRowKeys.length} 条)`}
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
            label="目标分店"
            rules={[{ required: true, message: '请选择目标分店' }]}
          >
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              placeholder="请选择目标分店"
              options={storeOptions}
            />
          </Form.Item>

          <Form.Item name="updatePurchasePrice" valuePropName="checked">
            <Checkbox>更新进货价</Checkbox>
          </Form.Item>
          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>更新零售价</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>更新自动定价</Checkbox>
          </Form.Item>
          <Form.Item name="updateIsSpecialProduct" valuePropName="checked">
            <Checkbox>更新特殊商品</Checkbox>
          </Form.Item>
          <Form.Item name="updateDiscountRate" valuePropName="checked">
            <Checkbox>更新折扣率</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
