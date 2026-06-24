import {
  BarChartOutlined,
  CopyOutlined,
  EditOutlined,
  SendOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Form,
  Image,
  Input,
  InputNumber,
  Modal,
  Row,
  Select,
  Space,
  Table,
  Tag,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import BarcodePreview from '../../../components/BarcodePreview'
import {
  batchUpdateDetails,
  getInvoice,
  getInvoiceDetails,
  updateInvoice,
  updateToStorePrices,
} from '../../../services/localSupplierInvoiceService'
import { getActiveStores } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type {
  BatchEditFields,
  LocalSupplierInvoiceDetailDto,
  LocalSupplierInvoiceItemDto,
  UpdateToStorePricesFields,
  UpdateToStorePricesRequest,
} from '../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'
import { discountRateToDecimal, formatDiscountRate } from '../../../utils/discountRate'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
} from '../../../utils/managedStoreScope'

function formatAmount(value?: number) {
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

export default function LocalSupplierInvoiceDetailPage() {
  const { t } = useTranslation()
  const route = useStableRouteContext()
  const navigate = useNavigate()
  const { active } = useKeepAliveContext()
  const invoiceGuid = route?.params.id
  const { access, currentUser } = useAuthStore()
  const isAdmin = access.isAdmin
  const managedStoreCodes = access.managedStoreCodes()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'
  // 记录当前发票已完成首次加载，保活 Tab 恢复时保留订单头和明细表。
  const loadedInvoiceGuidRef = useRef<string | null>(null)
  const visibleInvoiceGuidRef = useRef<string | null>(null)
  const lastLoadedManagedStoreCodeKeyRef = useRef<string | null>(null)
  const invoiceSnapshotRef = useRef<LocalSupplierInvoiceDetailDto | null>(null)
  const detailsSnapshotRef = useRef<LocalSupplierInvoiceItemDto[]>([])

  // 主表数据
  const [_invoice, setInvoice] = useState<LocalSupplierInvoiceDetailDto | null>(null)
  const [canAccessInvoice, setCanAccessInvoice] = useState(true)
  const [details, setDetails] = useState<LocalSupplierInvoiceItemDto[]>([])
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  // 搜索
  const [searchText, setSearchText] = useState('')

  // 涨跌过滤
  const [priceFilter, setPriceFilter] = useState<'all' | 'up' | 'down'>('all')

  // 行选择
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  // 批量编辑 Modal
  const [batchEditVisible, setBatchEditVisible] = useState(false)
  const [batchEditForm] = Form.useForm()
  const [batchEditLoading, setBatchEditLoading] = useState(false)

  // 更新到分店价格 Modal
  const [storePriceVisible, setStorePriceVisible] = useState(false)
  const [storePriceForm] = Form.useForm()
  const [storePriceLoading, setStorePriceLoading] = useState(false)

  // 表单
  const [form] = Form.useForm()

  // 分店选项
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])

  // 加载数据
  const loadInvoice = async (showLoading = true) => {
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
  }

  const loadDetails = async (showLoading = true) => {
    if (!invoiceGuid) return
    if (showLoading) {
      setDetailLoading(true)
    }
    try {
      const data = await getInvoiceDetails(invoiceGuid)
      if (!areLocalSupplierInvoiceDetailsEqual(detailsSnapshotRef.current, data)) {
        detailsSnapshotRef.current = data
        setDetails(data)
      }
    } catch {
      message.error(t('posAdmin.invoiceDetail.loadDetailsFailed', '加载明细失败'))
    } finally {
      if (showLoading) {
        setDetailLoading(false)
      }
    }
  }

  const loadInvoiceAndDetails = async (showLoading = true) => {
    if (await loadInvoice(showLoading)) {
      await loadDetails(showLoading)
    }
  }

  useEffect(() => {
    if (!active) return

    if (!shouldSkipDetailAutoReload({
      requestedDetailId: invoiceGuid || '',
      loadedDetailId: loadedInvoiceGuidRef.current,
      visibleDetailId: visibleInvoiceGuidRef.current,
      requestedDetailQueryKey: managedStoreCodeKey,
      loadedDetailQueryKey: lastLoadedManagedStoreCodeKeyRef.current,
    })) {
      // 未命中保活缓存或权限范围变化时才自动加载；同一进货单 Tab 切回直接复用现有内容。
      // 隐藏的 KeepAlive 节点也会收到全局路由变化，必须只让当前激活节点发起请求。
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
  }, [active, currentUser?.stores, invoiceGuid, managedStoreCodeKey])

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

  // 保存主表
  const handleSave = async () => {
    if (!invoiceGuid) return
    if (!canAccessInvoice) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

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

  // Batch edit
  const handleBatchEdit = async () => {
    if (!invoiceGuid) return
    if (!canAccessInvoice) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }
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

    // 检查是否至少选了一个字段
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
      loadDetails()
    } catch {
      message.error(t('posAdmin.invoiceDetail.batchUpdateFailed', '批量更新失败'))
    } finally {
      setBatchEditLoading(false)
    }
  }

  // 更新到分店价格
  const handleUpdateToStorePrices = async () => {
    if (!invoiceGuid) return
    if (!canAccessInvoice) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }
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
        t('posAdmin.invoiceDetail.storePriceUpdateResult', '更新完成：成功{{success}} 条，失败{{failed}} 条', { success: result?.updated ?? 0, failed: result?.failed ?? 0 }),
      )
      setStorePriceVisible(false)
      storePriceForm.resetFields()
      setSelectedRowKeys([])
    } catch {
      message.error(t('posAdmin.invoiceDetail.storePriceUpdateFailed', '更新到分店价格失败'))
    } finally {
      setStorePriceLoading(false)
    }
  }

  const columns: ColumnsType<LocalSupplierInvoiceItemDto> = [
    {
      title: t('posAdmin.invoiceDetail.seqNo', '序号'),
      width: 60,
      align: 'right',
      render: (_, __, index) => index + 1,
    },
    {
      title: t('posAdmin.invoiceDetail.image', '图片'),
      dataIndex: 'productImage',
      width: 80,
      render: (v: string) =>
        v ? (
          <Image src={v} width={48} height={48} style={{ objectFit: 'cover' }} />
        ) : (
          '--'
        ),
    },
    {
      title: t('posAdmin.invoiceDetail.itemNumber', '货号'),
      dataIndex: 'itemNumber',
      width: 140,
      render: (v: string) => (
        <Space size={4}>
          <span>{v || '--'}</span>
          {v && (
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              onClick={() => void copyTextToClipboard(v)}
            />
          )}
        </Space>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.barcode', '条码'),
      dataIndex: 'barcode',
      width: 180,
      render: (v: string) => <BarcodePreview value={v} compactCopy />,
    },
    {
      title: t('posAdmin.invoiceDetail.productName', '商品名称'),
      dataIndex: 'productName',
      width: 200,
      ellipsis: true,
      render: (v: string) => v || '--',
    },
    {
      title: t('posAdmin.invoiceDetail.quantity', '数量'),
      dataIndex: 'quantity',
      width: 80,
      align: 'right',
      render: (v: number) => v ?? '--',
    },
    {
      title: t('posAdmin.invoiceDetail.lastPurchasePrice', '上次进货价'),
      dataIndex: 'lastPurchasePrice',
      width: 110,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.currentPurchasePrice', '本次进货价'),
      dataIndex: 'purchasePrice',
      width: 110,
      align: 'right',
      render: (v: number, record) => {
        const bg = getPriceChangeBg(record.lastPurchasePrice, v)
        return (
          <span style={bg ? { backgroundColor: bg, padding: '2px 6px', borderRadius: 4 } : undefined}>
            {formatAmount(v)}
          </span>
        )
      },
    },
    {
      title: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
      dataIndex: 'retailPrice',
      width: 100,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoiceDetail.autoPricing', '自动定价'),
      dataIndex: 'autoPricing',
      width: 90,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.specialProduct', '特殊商品'),
      dataIndex: 'isSpecialProduct',
      width: 90,
      align: 'center',
      render: (v: boolean) => (
        <Tag color={v ? 'orange' : 'default'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>
      ),
    },
    {
      title: t('posAdmin.invoiceDetail.discountRate', '折扣率'),
      dataIndex: 'discountRate',
      width: 90,
      align: 'right',
      render: (v: number) => formatDiscountRate(v),
    },
    {
      title: t('posAdmin.invoiceDetail.amount', '金额'),
      dataIndex: 'amount',
      width: 110,
      align: 'right',
      render: (v: number) => formatAmount(v),
    },
  ]

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {/* 顶部：订单基本信息 */}
      <Card
        title={t('posAdmin.invoiceDetail.orderBasicInfo', '订单基本信息')}
        loading={loading}
        extra={
          invoiceGuid ? (
            <Button
              icon={<BarChartOutlined />}
              onClick={() => navigate(`/pos-admin/local-supplier-invoices/${invoiceGuid}/sales-analysis`)}
            >
              {t('posAdmin.invoiceDetail.salesAnalysis', '销量分析')}
            </Button>
          ) : null
        }
      >
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col span={6}>
              <Form.Item name="invoiceNo" label={t('posAdmin.invoiceDetail.invoiceNo', '订单号')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="storeName" label={t('posAdmin.invoiceDetail.store', '分店')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="supplierName" label={t('posAdmin.invoiceDetail.supplier', '供应商')}>
                <Input disabled />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="totalAmount" label={t('posAdmin.invoiceDetail.totalAmount', '总金额')}>
                <Input disabled />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={6}>
              <Form.Item name="orderDate" label={t('posAdmin.invoiceDetail.orderDate', '订单日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="inboundDate" label={t('posAdmin.invoiceDetail.inboundDate', '入库日期')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="remarks" label={t('posAdmin.invoiceDetail.remarks', '备注')}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={6} style={{ display: 'flex', alignItems: 'flex-end' }}>
              <Form.Item>
                <Button type="primary" loading={saving} onClick={() => void handleSave()}>
                  {t('posAdmin.invoiceDetail.save', '保存')}
                </Button>
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>

      {/* 底部：明细表格 */}
      <Card
        title={t('posAdmin.invoiceDetail.details', '明细 ({{count}} 条)', { count: details.length })}
        extra={
          <Space>
            <Input
              allowClear
              placeholder={t('posAdmin.invoiceDetail.searchPlaceholder', '搜索货号/条码/名称')}
              style={{ width: 200 }}
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              prefix={<CopyOutlined />}
            />
            <Button
              type={priceFilter === 'up' ? 'primary' : 'default'}
              danger={priceFilter === 'up'}
              size="small"
              onClick={() => setPriceFilter(priceFilter === 'up' ? 'all' : 'up')}
            >
              {t('posAdmin.invoiceDetail.priceUp', '涨价 ({{count}})', { count: priceStats.upCount })}
            </Button>
            <Button
              type={priceFilter === 'down' ? 'primary' : 'default'}
              size="small"
              style={priceFilter === 'down' ? { background: '#52c41a', borderColor: '#52c41a' } : {}}
              onClick={() => setPriceFilter(priceFilter === 'down' ? 'all' : 'down')}
            >
              {t('posAdmin.invoiceDetail.priceDown', '降价 ({{count}})', { count: priceStats.downCount })}
            </Button>
            {isAdmin && (
              <Button
                icon={<EditOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => setBatchEditVisible(true)}
              >
                {t('posAdmin.invoiceDetail.batchEdit', '批量编辑 ({{count}})', { count: selectedRowKeys.length })}
              </Button>
            )}
            {isAdmin && (
              <Button
                icon={<SendOutlined />}
                disabled={!selectedRowKeys.length}
                onClick={() => setStorePriceVisible(true)}
              >
                {t('posAdmin.invoiceDetail.updateToStorePrice', '更新到分店价格')}
              </Button>
            )}
          </Space>
        }
      >
        <Table
          rowKey="detailGUID"
          loading={detailLoading}
          dataSource={filteredDetails}
          columns={columns}
          pagination={false}
          scroll={{ x: 1800, y: 480 }}
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys),
          }}
          rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
          size="small"
        />
      </Card>

      {/* 批量编辑 Modal */}
      <Modal
        open={batchEditVisible}
        title={t('posAdmin.invoiceDetail.batchEditTitle', '批量编辑')}
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
            <Checkbox>{t('posAdmin.invoiceDetail.purchasePrice', '进货价')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}>
            {({ getFieldValue }) =>
              getFieldValue('updatePurchasePrice') ? (
                <Form.Item name="purchasePrice" label={t('posAdmin.invoiceDetail.purchasePrice', '进货价')} style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateRetailPrice" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.retailPrice', '零售价')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateRetailPrice !== cur.updateRetailPrice}>
            {({ getFieldValue }) =>
              getFieldValue('updateRetailPrice') ? (
                <Form.Item name="retailPrice" label={t('posAdmin.invoiceDetail.retailPrice', '零售价')} style={{ marginLeft: 24 }}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateIsAutoPricing" valuePropName="checked">
            <Checkbox>{t('posAdmin.invoiceDetail.autoPricing', '自动定价')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateIsAutoPricing !== cur.updateIsAutoPricing}>
            {({ getFieldValue }) =>
              getFieldValue('updateIsAutoPricing') ? (
                <Form.Item name="isAutoPricing" label={t('posAdmin.invoiceDetail.autoPricing', '自动定价')} style={{ marginLeft: 24 }}>
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
            <Checkbox>{t('posAdmin.invoiceDetail.specialProduct', '特殊商品')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateIsSpecialProduct !== cur.updateIsSpecialProduct}>
            {({ getFieldValue }) =>
              getFieldValue('updateIsSpecialProduct') ? (
                <Form.Item name="isSpecialProduct" label={t('posAdmin.invoiceDetail.specialProduct', '特殊商品')} style={{ marginLeft: 24 }}>
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
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}>
            {({ getFieldValue }) =>
              getFieldValue('updateDiscountRate') ? (
                <Form.Item name="discountRate" label={t('posAdmin.invoiceDetail.discountRate', '折扣率')} style={{ marginLeft: 24 }}>
                  <InputNumber min={0} max={100} step={1} precision={1} addonAfter="%" style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>
        </Form>
      </Modal>

      {/* 更新到分店价格 Modal */}
      <Modal
        open={storePriceVisible}
        title={t('posAdmin.invoiceDetail.updateToStorePriceTitle', '更新到分店价格')}
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
            label={t('posAdmin.invoiceDetail.targetStore', '目标分店')}
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
            <Checkbox>{t('posAdmin.invoiceDetail.updatePurchasePrice', '更新进货价')}</Checkbox>
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
