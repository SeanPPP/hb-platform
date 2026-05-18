import {
  ArrowLeftOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  ReloadOutlined,
  SaveOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  DatePicker,
  Descriptions,
  Dropdown,
  Image,
  Input,
  InputNumber,
  Modal,
  Progress,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { TFunction } from 'i18next'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useEffect, useMemo, useState, type Key } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import {
  batchDeleteDetails,
  batchUpdateDetails,
  getContainerDetail,
  getContainerProducts,
  updateContainer,
} from '../../../services/containerService'
import { exportContainerDetailsToExcel, type ContainerDetailExportItem } from '../../../services/exportService'
import { createProduct, updateProduct } from '../../../services/posProductService'
import { upsertForActiveStores as upsertMultiCodeForActiveStores } from '../../../services/storeMultiCodePriceService'
import { upsertForActiveStores as upsertRetailForActiveStores } from '../../../services/storeRetailPriceService'
import { batchTranslate } from '../../../services/translationService'
import {
  batchCreateProducts,
  batchUpdateWarehouseProducts,
  bulkSetStatus,
  detectProducts,
} from '../../../services/warehouseProductService'
import { useAuthStore } from '../../../store/auth'
import type { ContainerDetail, ContainerMain, UpdateContainerDetailRequest } from '../../../types/container'

type TagFilter = 'all' | 'new' | 'existing' | 'noOemPrice' | 'abnormalImport'

function formatDate(value?: string) {
  return value ? dayjs(value).format('YYYY-MM-DD') : '--'
}

function formatNumber(value?: number, digits = 2) {
  return value == null ? '--' : value.toLocaleString('zh-CN', { maximumFractionDigits: digits, minimumFractionDigits: digits })
}

function rowKey(row: ContainerDetail) {
  return row.hguid || String(row.id)
}

function getStatusTag(status: number | undefined, t: TFunction) {
  if (status == null) return <Tag>{t('containers.status.unknown')}</Tag>
  const map: Record<number, { color: string; labelKey: string }> = {
    0: { color: 'blue', labelKey: 'loaded' },
    1: { color: 'orange', labelKey: 'inTransit' },
    2: { color: 'success', labelKey: 'completed' },
    7: { color: 'error', labelKey: 'cancelled' },
  }
  const item = map[status]
  return item ? <Tag color={item.color}>{t(`containers.status.${item.labelKey}`)}</Tag> : <Tag>{t('containers.status.unknownWithCode', { status })}</Tag>
}

function getProductTypeLabel(value: string | undefined, t: TFunction) {
  const type = value || '普通商品'
  const map: Record<string, string> = {
    全部: 'common.all',
    普通商品: 'containers.productTypes.normal',
    套装商品: 'containers.productTypes.set',
    套装子商品: 'containers.productTypes.setChild',
  }
  return map[type] ? t(map[type]) : type
}

function isSetProduct(row: ContainerDetail) {
  return row.商品类型 === '套装商品' || row.商品类型 === '套装子商品' || row.商品信息?.商品类型 === '套装商品'
}

export default function ContainerDetailPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const route = useStableRouteContext()
  const [containerGuid] = useState(() => route?.params.containerGuid || '')
  const access = useAuthStore((state) => state.access)
  const [loading, setLoading] = useState(false)
  const [savingHeader, setSavingHeader] = useState(false)
  const [container, setContainer] = useState<ContainerMain | null>(null)
  const [rows, setRows] = useState<ContainerDetail[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])
  const [itemNumberFilter, setItemNumberFilter] = useState('')
  const [productTypeFilter, setProductTypeFilter] = useState('全部')
  const [tagFilter, setTagFilter] = useState<TagFilter>('all')
  const [batchFloatRate, setBatchFloatRate] = useState<number | null>(null)
  const [batchImportPrice, setBatchImportPrice] = useState<number | null>(null)
  const [batchOemPrice, setBatchOemPrice] = useState<number | null>(null)
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [headerEditing, setHeaderEditing] = useState(false)
  const [headerForm, setHeaderForm] = useState<{
    实际到货日期?: Dayjs | null
    汇率?: number
    运费?: number
    备注?: string
  }>({})

  useDynamicTabTitle(container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : undefined)

  const loadData = async () => {
    if (!containerGuid) {
      return
    }
    setLoading(true)
    try {
      const [info, products] = await Promise.all([
        getContainerDetail(containerGuid),
        getContainerProducts(containerGuid),
      ])
      setContainer(info)
      setHeaderForm({
        实际到货日期: info.实际到货日期 ? dayjs(info.实际到货日期) : null,
        汇率: info.汇率,
        运费: info.运费,
        备注: info.备注,
      })
      setRows(products)
      setSelectedRowKeys([])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('containers.messages.loadDetailFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [containerGuid])

  const filteredRows = useMemo(() => {
    return rows.filter((row) => {
      const itemNumber = row.商品信息?.货号 || ''
      const productType = getProductTypeLabel(row.商品类型 || row.商品信息?.商品类型, t)
      if (itemNumberFilter && !itemNumber.toLowerCase().includes(itemNumberFilter.toLowerCase())) return false
      if (productTypeFilter !== '全部' && productType !== productTypeFilter) return false
      if (tagFilter === 'new') return Boolean(row.是否新商品)
      if (tagFilter === 'existing') return !row.是否新商品
      if (tagFilter === 'noOemPrice') return Boolean(row.是否新商品) && (!row.贴牌价格 || row.贴牌价格 <= 0)
      if (tagFilter === 'abnormalImport') return !row.进口价格 || row.进口价格 <= 0
      return true
    })
  }, [itemNumberFilter, productTypeFilter, rows, tagFilter])

  const selectedRows = useMemo(
    () => filteredRows.filter((row) => selectedRowKeys.includes(rowKey(row))),
    [filteredRows, selectedRowKeys],
  )

  const targetRows = selectedRows.length ? selectedRows : filteredRows

  const patchRow = (key: string, patch: Partial<ContainerDetail>) => {
    setRows((items) => items.map((item) => (rowKey(item) === key ? { ...item, ...patch } : item)))
  }

  const saveRowPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {
    if (!access.canEditContainer || !row.hguid) return
    patchRow(rowKey(row), patch)
    await batchUpdateDetails([{ hguid: row.hguid, ...patch } as UpdateContainerDetailRequest])
  }

  const saveHeader = async () => {
    if (!containerGuid || !access.canEditContainer) return
    setSavingHeader(true)
    try {
      await updateContainer(containerGuid, {
        实际到货日期: headerForm.实际到货日期 ? headerForm.实际到货日期.format('YYYY-MM-DD') : undefined,
        汇率: headerForm.汇率,
        运费: headerForm.运费,
        备注: headerForm.备注,
      })
      message.success(t('containers.messages.headerSaveSuccess'))
      setHeaderEditing(false)
      await loadData()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('containers.messages.headerSaveFailed'))
    } finally {
      setSavingHeader(false)
    }
  }

  const calculateImportPrice = (row: ContainerDetail, floatRate: number) => {
    const rate = container?.汇率
    if (!rate || rate <= 0 || !row.国内价格) return row.进口价格
    const transportCost = row.运输成本 ?? 0
    return ((row.国内价格 / rate + transportCost) * floatRate * 10) / 11
  }

  const applyFloatRate = async () => {
    if (batchFloatRate == null) {
      message.warning(t('containers.messages.enterFloatRate'))
      return
    }
    const updates = targetRows
      .filter((row) => row.hguid)
      .map((row) => ({
        hguid: row.hguid,
        调整浮率: batchFloatRate,
        进口价格: calculateImportPrice(row, batchFloatRate),
      }))
    if (!updates.length) return
    await batchUpdateDetails(updates)
    setRows((items) =>
      items.map((item) => {
        const match = updates.find((update) => update.hguid === item.hguid)
        return match ? { ...item, 调整浮率: match.调整浮率, 进口价格: match.进口价格 } : item
      }),
    )
    setBatchFloatRate(null)
    message.success(t('containers.messages.detailsUpdated', { count: updates.length }))
  }

  const applyPrices = async () => {
    if (batchImportPrice == null && batchOemPrice == null) {
      message.warning(t('containers.messages.enterImportOrOemPrice'))
      return
    }
    if (!selectedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    const updates = selectedRows
      .filter((row) => row.hguid)
      .map((row) => ({ hguid: row.hguid, 进口价格: batchImportPrice ?? row.进口价格, 贴牌价格: batchOemPrice ?? row.贴牌价格 }))
    await batchUpdateDetails(updates)
    setRows((items) =>
      items.map((item) => {
        const match = updates.find((update) => update.hguid === item.hguid)
        return match ? { ...item, 进口价格: match.进口价格, 贴牌价格: match.贴牌价格 } : item
      }),
    )
    setBatchImportPrice(null)
    setBatchOemPrice(null)
    setSelectedRowKeys([])
    message.success(t('containers.messages.detailPricesUpdated', { count: updates.length }))
  }

  const applyActive = async (isActive: boolean) => {
    if (!selectedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    const productCodes = selectedRows
      .map((row) => row.商品编码 || row.商品信息?.商品编码)
      .filter((value): value is string => Boolean(value))
    if (!productCodes.length) {
      message.warning(t('containers.messages.selectedProductsMissingCode'))
      return
    }
    const result = await bulkSetStatus(productCodes, isActive)
    if (result.success === false) {
      message.error(result.message || t('containers.messages.batchActiveFailed'))
      return
    }
    setRows((items) =>
      items.map((item) => (productCodes.includes(item.商品编码 || item.商品信息?.商品编码 || '') ? { ...item, warehouseIsActive: isActive } : item)),
    )
    setSelectedRowKeys([])
    message.success(t(isActive ? 'containers.messages.productsActivated' : 'containers.messages.productsDeactivated', { count: productCodes.length }))
  }

  const translateNames = async () => {
    const names = Array.from(new Set(targetRows.map((row) => row.商品信息?.商品名称).filter((value): value is string => Boolean(value))))
    if (!names.length) {
      message.warning(t('containers.messages.noNamesToTranslate'))
      return
    }
    const translations = await batchTranslate(names)
    const updates = targetRows
      .filter((row) => row.hguid && row.商品信息?.商品名称 && translations[row.商品信息.商品名称])
      .map((row) => ({
        hguid: row.hguid,
        英文名称: translations[row.商品信息!.商品名称!],
      }))
    if (updates.length) {
      await batchUpdateDetails(updates)
      setRows((items) =>
        items.map((item) => {
          const match = updates.find((update) => update.hguid === item.hguid)
          return match ? { ...item, 商品信息: { ...item.商品信息, 英文名称: match.英文名称 } } : item
        }),
      )
    }
    message.success(t('containers.messages.namesTranslated', { count: updates.length }))
  }

  const createNewProducts = async () => {
    const candidates = targetRows.filter((row) => row.是否新商品)
    const eligible = candidates.filter((row) => {
      const name = row.商品信息?.商品名称
      const englishName = row.商品信息?.英文名称
      return name && englishName && (row.进口价格 ?? 0) > 0 && (row.贴牌价格 ?? 0) > 0
    })
    if (!eligible.length) {
      message.warning(t('containers.messages.noEligibleNewProducts'))
      return
    }
    const created: Array<{ row: ContainerDetail; productCode: string }> = []
    for (const row of eligible) {
      const productCode = row.商品编码 || row.商品信息?.商品编码
      if (!productCode) continue
      await createProduct({
        productCode,
        productName: row.商品信息?.商品名称,
        itemNumber: row.商品信息?.货号,
        barcode: row.商品信息?.条形码,
        purchasePrice: row.进口价格,
        retailPrice: row.贴牌价格,
        localSupplierCode: '200',
        isAutoPricing: false,
        isSpecialProduct: false,
      })
      created.push({ row, productCode })
    }
    if (created.length) {
      await batchCreateProducts(created.map(({ row, productCode }) => ({
        ProductCode: productCode,
        ItemNumber: row.商品信息?.货号,
        Barcode: row.商品信息?.条形码,
        ChineseName: row.商品信息?.商品名称,
        EnglishName: row.商品信息?.英文名称,
        DomesticPrice: row.国内价格,
        OEMPrice: row.贴牌价格,
        ImportPrice: row.进口价格,
        Volume: row.单件体积,
        IsSetProduct: isSetProduct(row),
      })))
      await upsertRetailForActiveStores(created.map(({ row, productCode }) => ({
        ProductCode: productCode,
        PurchasePrice: row.进口价格,
        StoreRetailPriceValue: row.贴牌价格,
        IsActive: true,
        IsAutoPricing: false,
      })))
      await upsertMultiCodeForActiveStores(created.filter(({ row }) => isSetProduct(row)).map(({ row, productCode }) => ({
        ProductCode: productCode,
        PurchasePrice: row.进口价格,
        MultiCodeRetailPrice: row.贴牌价格,
        IsActive: true,
        IsAutoPricing: false,
      })))
    }
    message.success(t('containers.messages.newProductsCreated', { count: created.length }))
    await loadData()
  }

  const updateExistingPurchase = async () => {
    const candidates = targetRows.filter((row) => !row.是否新商品)
    if (!candidates.length) {
      message.info(t('containers.messages.noExistingProductsToUpdate'))
      return
    }
    const detection = await detectProducts(candidates.map((row) => ({
      ProductCode: row.商品编码 || row.商品信息?.商品编码,
      ItemNumber: row.商品信息?.货号,
      Barcode: row.商品信息?.条形码,
    })))
    const importMap = new Map<string, number | undefined>()
    detection.forEach((item) => {
      const code = item.ProductCode || item.productCode
      if (code) importMap.set(code, item.WarehouseImportPrice ?? item.warehouseImportPrice ?? item.importPrice)
    })
    const updates = candidates.filter((row) => {
      const code = row.商品编码 || row.商品信息?.商品编码 || ''
      const current = importMap.get(code)
      return code && (row.进口价格 ?? 0) > 0 && (current == null || Math.abs(current - (row.进口价格 ?? 0)) > 0.000001)
    })
    if (!updates.length) {
      message.info(t('containers.messages.noPurchasePriceDiff'))
      return
    }
    await batchUpdateWarehouseProducts(updates.map((row) => ({
      ProductCode: row.商品编码 || row.商品信息?.商品编码,
      ImportPrice: row.进口价格,
      IsActive: true,
    })))
    await Promise.all(
      updates.map((row) => {
        const code = row.商品编码 || row.商品信息?.商品编码
        return code ? updateProduct(code, { purchasePrice: row.进口价格 ?? 0 }).catch(() => null) : Promise.resolve(null)
      }),
    )
    await upsertRetailForActiveStores(updates.map((row) => ({
      ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
      PurchasePrice: row.进口价格,
      IsActive: true,
    })))
    await upsertMultiCodeForActiveStores(updates.map((row) => ({
      ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
      PurchasePrice: row.进口价格,
      IsActive: true,
    })))
    message.success(t('containers.messages.purchasePricesUpdated', { count: updates.length }))
  }

  const deleteSelected = () => {
    if (!selectedRowKeys.length) {
      message.warning(t('containers.messages.selectDetails'))
      return
    }
    Modal.confirm({
      title: t('containers.modals.deleteDetailsTitle'),
      content: t('containers.modals.deleteDetailsContent', { count: selectedRowKeys.length }),
      okText: t('containers.actions.confirmDelete'),
      okButtonProps: { danger: true },
      onOk: async () => {
        const hguids = selectedRows.map((row) => row.hguid).filter((value): value is string => Boolean(value))
        await batchDeleteDetails(hguids)
        setRows((items) => items.filter((item) => !hguids.includes(item.hguid)))
        setSelectedRowKeys([])
        message.success(t('containers.messages.detailsDeleted', { count: hguids.length }))
      },
    })
  }

  const exportExcel = async () => {
    const exportRows = targetRows
    if (!exportRows.length) {
      message.warning(t('containers.messages.noDataToExport'))
      return
    }
    setExporting(true)
    try {
      const items: ContainerDetailExportItem[] = exportRows.map((row) => ({
        imageUrl: row.商品信息?.商品图片,
        itemNumber: row.商品信息?.货号 || '',
        barcode: row.商品信息?.条形码 || '',
        productName: row.商品信息?.商品名称 || '',
        isNewProduct: Boolean(row.是否新商品),
        containerQuantity: row.装柜数量 || 0,
        domesticPrice: row.国内价格 || 0,
        transportCost: row.运输成本 || 0,
        importPrice: row.进口价格 || 0,
        oemPrice: row.贴牌价格 || 0,
      }))
      await exportContainerDetailsToExcel(items, {
        fileName: `${container?.货柜编号 || t('containers.detailTitle')}_${t('containers.export.detailSuffix')}`,
        onProgress: (progress) => setExportProgress(progress),
      })
      message.success(t('containers.messages.detailsExported', { count: items.length }))
    } finally {
      setExporting(false)
      setExportProgress(0)
    }
  }

  const columns: ColumnsType<ContainerDetail> = [
    { title: t('containers.columns.index'), width: 70, fixed: 'left', render: (_v, _r, index) => index + 1 },
    {
      title: t('containers.columns.image'),
      width: 80,
      fixed: 'left',
      render: (_, row) =>
        row.商品信息?.商品图片 ? (
          <Image width={48} height={48} src={row.商品信息.商品图片} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <span style={{ color: '#999' }}>{t('containers.empty.noImage')}</span>
        ),
    },
    { title: t('containers.fields.itemNumber'), width: 150, sorter: (a, b) => (a.商品信息?.货号 || '').localeCompare(b.商品信息?.货号 || ''), render: (_, row) => row.商品信息?.货号 || '--' },
    { title: t('containers.fields.barcode'), width: 180, render: (_, row) => <BarcodePreview value={row.商品信息?.条形码} showText={false} showCopy={false} /> },
    { title: t('containers.fields.productName'), width: 240, render: (_, row) => row.商品信息?.商品名称 || '--' },
    {
      title: t('containers.fields.englishName'),
      width: 220,
      render: (_, row) => access.canEditContainer ? (
        <Input
          defaultValue={row.商品信息?.英文名称}
          onBlur={(event) => void saveRowPatch(row, { 商品信息: { ...row.商品信息, 英文名称: event.target.value } } as Partial<ContainerDetail>)}
        />
      ) : row.商品信息?.英文名称 || '--',
    },
    { title: t('containers.fields.productType'), width: 110, render: (_, row) => getProductTypeLabel(row.商品类型 || row.商品信息?.商品类型, t) },
    { title: t('containers.fields.newProduct'), width: 90, render: (_, row) => (row.是否新商品 ? <Tag color="blue">{t('containers.tags.new')}</Tag> : <Tag>{t('containers.tags.existing')}</Tag>) },
    { title: t('containers.fields.containerPieces'), dataIndex: '装柜件数', width: 100, align: 'right' },
    { title: t('containers.fields.containerQuantity'), dataIndex: '装柜数量', width: 100, align: 'right' },
    { title: t('containers.fields.domesticPrice'), dataIndex: '国内价格', width: 110, align: 'right', render: (v) => formatNumber(v) },
    {
      title: t('containers.fields.floatRate'),
      dataIndex: '调整浮率',
      width: 120,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber defaultValue={row.调整浮率} precision={4} style={{ width: 96 }} onBlur={(event) => {
            const value = event.target.value ? Number(event.target.value) : undefined
            void saveRowPatch(row, { 调整浮率: value, 进口价格: value == null ? row.进口价格 : calculateImportPrice(row, value) })
          }} />
        ) : formatNumber(row.调整浮率, 4),
    },
    { title: t('containers.fields.transportCost'), dataIndex: '运输成本', width: 110, align: 'right', render: (v) => formatNumber(v) },
    {
      title: t('containers.fields.importPrice'),
      dataIndex: '进口价格',
      width: 120,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? <InputNumber defaultValue={row.进口价格} min={0} precision={2} style={{ width: 100 }} onBlur={(event) => void saveRowPatch(row, { 进口价格: event.target.value ? Number(event.target.value) : undefined })} /> : formatNumber(row.进口价格),
    },
    {
      title: t('containers.fields.oemPrice'),
      dataIndex: '贴牌价格',
      width: 120,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? <InputNumber defaultValue={row.贴牌价格} min={0} precision={2} style={{ width: 100 }} onBlur={(event) => void saveRowPatch(row, { 贴牌价格: event.target.value ? Number(event.target.value) : undefined })} /> : formatNumber(row.贴牌价格),
    },
    { title: t('containers.fields.warehouseStatus'), width: 100, render: (_, row) => <Tag color={row.warehouseIsActive ? 'success' : 'default'}>{row.warehouseIsActive ? t('common.activeUpper') : t('common.inactiveUpper')}</Tag> },
    {
      title: t('containers.fields.remark'),
      width: 220,
      render: (_, row) =>
        access.canEditContainer ? <Input defaultValue={row.备注} onBlur={(event) => void saveRowPatch(row, { 备注: event.target.value })} /> : row.备注 || '--',
    },
  ]

  return (
    <PageContainer title={container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : t('menu.containerDetail')}>
      <Spin spinning={loading}>
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {!containerGuid ? <Alert type="warning" showIcon message={t('containers.messages.missingContainerGuid')} /> : null}
          <Card>
            <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
              <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/warehouse/containers')}>{t('containers.actions.backToList')}</Button>
              <Space wrap>
                <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>{t('common.refresh')}</Button>
                {access.canEditContainer ? (
                  headerEditing ? (
                    <Button type="primary" icon={<SaveOutlined />} loading={savingHeader} onClick={() => void saveHeader()}>{t('containers.actions.saveContainer')}</Button>
                  ) : (
                    <Button icon={<EditOutlined />} onClick={() => setHeaderEditing(true)}>{t('containers.actions.editContainer')}</Button>
                  )
                ) : null}
              </Space>
            </Space>
            <Descriptions bordered size="small" column={4} style={{ marginTop: 16 }}>
              <Descriptions.Item label={t('containers.fields.containerNumber')}>{container?.货柜编号 || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.loadingDate')}>{formatDate(container?.装柜日期)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.estimatedArrival')}>{formatDate(container?.预计到岸日期)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.status')}>{getStatusTag(container?.状态, t)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.actualArrival')}>
                {headerEditing ? <DatePicker value={headerForm.实际到货日期} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 实际到货日期: value }))} /> : formatDate(container?.实际到货日期)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.exchangeRate')}>
                {headerEditing ? <InputNumber value={headerForm.汇率} precision={4} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 汇率: value ?? undefined }))} /> : formatNumber(container?.汇率, 4)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.freight')}>
                {headerEditing ? <InputNumber value={headerForm.运费} precision={2} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 运费: value ?? undefined }))} /> : formatNumber(container?.运费)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.totalVolume')}>{formatNumber(container?.总体积, 4)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.remark')} span={4}>
                {headerEditing ? <Input.TextArea value={headerForm.备注} rows={2} onChange={(event) => setHeaderForm((prev) => ({ ...prev, 备注: event.target.value }))} /> : container?.备注 || '--'}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card>
            <Space direction="vertical" size={12} style={{ width: '100%' }}>
              <Space wrap style={{ justifyContent: 'space-between', width: '100%' }}>
                <Space wrap>
                  <Input value={itemNumberFilter} allowClear prefix={<SearchOutlined />} placeholder={t('containers.placeholders.filterItemNumber')} style={{ width: 180 }} onChange={(event) => setItemNumberFilter(event.target.value)} />
                  <Select value={productTypeFilter} style={{ width: 140 }} onChange={setProductTypeFilter} options={['全部', '普通商品', '套装商品', '套装子商品'].map((value) => ({ value, label: getProductTypeLabel(value, t) }))} />
                  <Select
                    value={tagFilter}
                    style={{ width: 150 }}
                    onChange={setTagFilter}
                    options={[
                      { value: 'all', label: t('containers.filters.allTags') },
                      { value: 'new', label: t('containers.tags.newProduct') },
                      { value: 'existing', label: t('containers.tags.existingProduct') },
                      { value: 'noOemPrice', label: t('containers.filters.missingOemPrice') },
                      { value: 'abnormalImport', label: t('containers.filters.abnormalImportPrice') },
                    ]}
                  />
                  <Typography.Text type="secondary">{t('containers.text.showingRows', { filtered: filteredRows.length, total: rows.length })}</Typography.Text>
                </Space>
                <Space wrap>
                  <Button icon={<DownloadOutlined />} loading={exporting} onClick={() => void exportExcel()}>{t('common.export')}</Button>
                  {access.canEditContainer ? (
                    <Dropdown
                      menu={{
                        items: [
                          { key: 'translate', label: t('containers.actions.batchTranslate') },
                          { key: 'createNew', label: t('containers.actions.createNewProducts') },
                          { key: 'updatePurchase', label: t('containers.actions.updateExistingPurchase') },
                          { key: 'active', label: t('containers.actions.batchActivate') },
                          { key: 'inactive', label: t('containers.actions.batchDeactivate') },
                        ],
                        onClick: ({ key }) => {
                          if (key === 'translate') void translateNames()
                          if (key === 'createNew') void createNewProducts()
                          if (key === 'updatePurchase') void updateExistingPurchase()
                          if (key === 'active') void applyActive(true)
                          if (key === 'inactive') void applyActive(false)
                        },
                      }}
                    >
                      <Button>{t('containers.actions.batchActions')}</Button>
                    </Dropdown>
                  ) : null}
                  {access.canDeleteContainer ? <Button danger icon={<DeleteOutlined />} onClick={deleteSelected}>{t('containers.actions.deleteDetails')}</Button> : null}
                </Space>
              </Space>

              {access.canEditContainer ? (
                <Space wrap>
                  <InputNumber value={batchFloatRate} placeholder={t('containers.fields.floatRate')} precision={4} onChange={setBatchFloatRate} />
                  <Button onClick={() => void applyFloatRate()}>{t('containers.actions.applyFloatRate')}</Button>
                  <InputNumber value={batchImportPrice} placeholder={t('containers.fields.importPrice')} min={0} precision={2} onChange={setBatchImportPrice} />
                  <InputNumber value={batchOemPrice} placeholder={t('containers.fields.oemPrice')} min={0} precision={2} onChange={setBatchOemPrice} />
                  <Button onClick={() => void applyPrices()}>{t('containers.actions.applyPrices')}</Button>
                  <Switch checkedChildren={t('containers.text.selectedFirst')} unCheckedChildren={t('containers.text.allDisplayed')} checked={selectedRowKeys.length > 0} disabled />
                </Space>
              ) : null}

              {exporting ? <Progress percent={exportProgress} size="small" /> : null}

              <Table
                rowKey={rowKey}
                size="small"
                columns={columns}
                dataSource={filteredRows}
                rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys }}
                pagination={{ pageSize: 50, showSizeChanger: true, showTotal: (total) => t('common.total', { count: total }) }}
                scroll={{ x: 2100, y: 620 }}
              />
            </Space>
          </Card>
        </Space>
      </Spin>
    </PageContainer>
  )
}
