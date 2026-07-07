import {
  CopyOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  GiftOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Form,
  Image,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { DefaultOptionType } from 'antd/es/select'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
import type { ClipboardEvent } from 'react'
import { useEffect, useMemo, useState } from 'react'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import {
  batchDeleteDomesticProducts,
  createDomesticProduct,
  getDomesticProductsGrid,
  getDomesticProductSetItems,
  getSupplierOptions,
  updateDomesticProduct,
  updateDomesticProductSetItems,
} from '../../../services/domesticProductService'
import { exportDomesticProductsToExcel } from '../../../services/exportService'
import { useAuthStore } from '../../../store/auth'
import type {
  CreateDomesticProductPayload,
  DomesticProductGridQuery,
  DomesticProductItem,
  DomesticProductSetItem,
  ProductType,
  SupplierOption,
  UpdateDomesticProductPayload,
} from '../../../types/domesticProduct'
import { ProductTypeLabels } from '../../../types/domesticProduct'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { useTranslation } from 'react-i18next'
import {
  applySetItemColumnPaste,
  buildSetProductPriceSyncPayload,
  calculateSetItemPriceTotals,
  type PasteableSetItemField,
} from './setItemsBulkPaste'

interface ProductFormValues {
  supplierCode?: string
  productName: string
  englishProductName?: string
  hbProductNo?: string
  barcode?: string
  productSpecification?: string
  productType: ProductType
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  packingQuantity?: number
  unitVolume?: number
  middlePackQuantity?: number
  packingSize?: string
  material?: string
  remarks?: string
  productImage?: string
  isActive: boolean
}

const getStatusOptions = (t: ReturnType<typeof useTranslation>['t']) => [
  { value: true, label: t('common.enable', '启用') },
  { value: false, label: t('common.disable', '停用') },
]

const productTypeOptions = Object.entries(ProductTypeLabels).map(([value, label]) => ({
  value: Number(value),
  label,
}))

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

function formatPrice(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }

  return value.toFixed(2)
}

type SupplierSelectOption = DefaultOptionType & {
  searchText?: string
}

function buildSupplierOptions(suppliers: SupplierOption[]): SupplierSelectOption[] {
  return suppliers.map((item) => ({
    value: item.code,
    label: `${item.code} - ${item.name}${item.shopNumber ? ` - ${item.shopNumber}` : ''}`,
    searchText: `${item.code} ${item.name} ${item.shopNumber ?? ''}`.toLowerCase(),
  }))
}

function filterSupplierOption(input: string, option?: DefaultOptionType) {
  return String((option as SupplierSelectOption | undefined)?.searchText ?? '')
    .includes(input.trim().toLowerCase())
}

function ProductFormModal({
  open,
  saving,
  editingItem,
  suppliers,
  form,
  onCancel,
  onSubmit,
}: {
  open: boolean
  saving: boolean
  editingItem: DomesticProductItem | null
  suppliers: SupplierOption[]
  form: ReturnType<typeof Form.useForm<ProductFormValues>>[0]
  onCancel: () => void
  onSubmit: () => void
}) {
  const { t } = useTranslation()
  return (
    <Modal
      title={editingItem ? t('domesticProducts.editTitle', '编辑国内商品 - {{name}}', { name: editingItem.itemNumber || editingItem.name }) : t('domesticProducts.createTitle', '新建国内商品')}
      open={open}
      width={920}
      destroyOnHidden
      okText={t('common.save', '保存')}
      cancelText={t('common.cancel', '取消')}
      confirmLoading={saving}
      onCancel={onCancel}
      onOk={onSubmit}
    >
      <Form form={form} layout="vertical" preserve={false}>
        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item
            name="supplierCode"
            label={t('domesticProducts.supplier', '供应商')}
            style={{ flex: 1 }}
            rules={editingItem ? [] : [{ required: true, message: t('domesticProducts.selectSupplier', '请选择供应商') }]}
          >
            <Select
              disabled={Boolean(editingItem)}
              placeholder={t('domesticProducts.selectSupplier', '请选择供应商')}
              showSearch
              filterOption={filterSupplierOption}
              options={buildSupplierOptions(suppliers)}
            />
          </Form.Item>
          <Form.Item
            name="productType"
            label={t('domesticProducts.productType', '商品类型')}
            style={{ width: 180 }}
            rules={[{ required: true, message: t('domesticProducts.selectProductType', '请选择商品类型') }]}
          >
            <Select placeholder={t('domesticProducts.selectProductType', '请选择商品类型')} options={productTypeOptions} />
          </Form.Item>
          <Form.Item name="isActive" label={t('domesticProducts.status', '状态')} valuePropName="checked" style={{ width: 120 }}>
            <Switch checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item
            name="productName"
            label={t('domesticProducts.productName', '商品名称')}
            style={{ flex: 1 }}
            rules={[{ required: true, message: t('domesticProducts.enterProductName', '请输入商品名称') }]}
          >
            <Input placeholder={t('domesticProducts.enterProductName', '请输入商品名称')} />
          </Form.Item>
          <Form.Item name="englishProductName" label={t('domesticProducts.englishName', '英文名称')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.enterEnglishName', '请输入英文名称')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="hbProductNo" label={t('domesticProducts.hbProductNo', 'HB货号')} style={{ flex: 1 }}>
            <Input disabled={Boolean(editingItem)} placeholder={t('domesticProducts.autoGenerate', '不填则后端自动生成')} />
          </Form.Item>
          <Form.Item name="barcode" label={t('domesticProducts.barcode', '条码')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.autoGenerate', '不填则后端自动生成')} />
          </Form.Item>
          <Form.Item name="productSpecification" label={t('domesticProducts.specification', '规格')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.enterSpec', '请输入商品规格')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="domesticPrice" label={t('domesticProducts.domesticPrice', '国内价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="oemPrice" label={t('domesticProducts.oemPrice', '零售价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="importPrice" label={t('domesticProducts.importPrice', '进口价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingQuantity" label={t('domesticProducts.packingQuantity', '装箱数')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="middlePackQuantity" label={t('domesticProducts.middlePackQuantity', '中包数量')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="unitVolume" label={t('domesticProducts.unitVolume', '体积')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={4} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingSize" label={t('domesticProducts.packingSize', '包装尺寸')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.enterPackingSize', '请输入包装尺寸')} />
          </Form.Item>
          <Form.Item name="material" label={t('domesticProducts.material', '材质')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.enterMaterial', '请输入材质')} />
          </Form.Item>
          <Form.Item name="productImage" label={t('domesticProducts.imageUrl', '图片地址')} style={{ flex: 1 }}>
            <Input placeholder={t('domesticProducts.enterImageUrl', '请输入图片 URL')} />
          </Form.Item>
        </Space>

        <Form.Item name="remarks" label={t('domesticProducts.remarks', '备注')}>
          <Input.TextArea rows={3} placeholder={t('domesticProducts.enterRemarks', '请输入备注')} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function SetItemsModal({
  open,
  loading,
  saving,
  product,
  items,
  canEdit,
  onCancel,
  onAddRow,
  onRemoveRow,
  onChangeField,
  onPasteColumn,
  onSubmit,
}: {
  open: boolean
  loading: boolean
  saving: boolean
  product: DomesticProductItem | null
  items: DomesticProductSetItem[]
  canEdit: boolean
  onCancel: () => void
  onAddRow: () => void
  onRemoveRow: (rowId: string) => void
  onChangeField: (rowId: string, field: keyof DomesticProductSetItem, value: string | number | undefined) => void
  onPasteColumn: (rowId: string | undefined, field: PasteableSetItemField, clipboardText: string) => void
  onSubmit: () => void
}) {
  const { t } = useTranslation()
  const totals = useMemo(() => calculateSetItemPriceTotals(items), [items])
  const handlePaste = (
    event: ClipboardEvent<HTMLElement>,
    rowId: string | undefined,
    field: PasteableSetItemField,
  ) => {
    if (!canEdit) {
      return
    }

    const clipboardText = event.clipboardData.getData('text')
    if (!clipboardText) {
      return
    }

    event.preventDefault()
    onPasteColumn(rowId, field, clipboardText)
  }

  const createPasteTitle = (label: string, field: PasteableSetItemField) => (
    <span
      tabIndex={canEdit ? 0 : -1}
      onClick={(event) => event.currentTarget.focus()}
      onPaste={(event) => {
        handlePaste(event, items[0]?.id, field)
      }}
    >
      {label}
    </span>
  )

  const columns: ColumnsType<DomesticProductSetItem> = [
    {
      title: createPasteTitle(t('domesticProducts.productName', '商品名称'), 'productName'),
      dataIndex: 'productName',
      render: (_, record) => (
        <Input
          value={record.productName}
          disabled={!canEdit}
          onPaste={(event) => handlePaste(event, record.id, 'productName')}
          onChange={(event) => onChangeField(record.id, 'productName', event.target.value)}
        />
      ),
    },
    {
      title: t('domesticProducts.setProductNo', '套装货号'),
      dataIndex: 'setProductNo',
      width: 180,
      render: (_, record) => (
        <Input
          value={record.setProductNo}
          disabled={!canEdit}
          onChange={(event) => onChangeField(record.id, 'setProductNo', event.target.value)}
        />
      ),
    },
    {
      title: t('domesticProducts.barcode', '条码'),
      dataIndex: 'setBarcode',
      render: (_, record) => (
        <Input
          value={record.setBarcode}
          disabled={!canEdit}
          onChange={(event) => onChangeField(record.id, 'setBarcode', event.target.value)}
        />
      ),
    },
    {
      title: createPasteTitle(t('domesticProducts.domesticPrice', '国内价'), 'domesticPrice'),
      dataIndex: 'domesticPrice',
      width: 120,
      render: (_, record) => (
        <div onPaste={(event) => handlePaste(event, record.id, 'domesticPrice')}>
          <InputNumber
            min={0}
            precision={2}
            value={record.domesticPrice}
            disabled={!canEdit}
            style={{ width: '100%' }}
            onChange={(value) => onChangeField(record.id, 'domesticPrice', value ?? undefined)}
          />
        </div>
      ),
    },
    {
      title: t('domesticProducts.importPrice', '进口价'),
      dataIndex: 'importPrice',
      width: 120,
      render: (_, record) => (
        <InputNumber
          min={0}
          precision={2}
          value={record.importPrice}
          disabled={!canEdit}
          style={{ width: '100%' }}
          onChange={(value) => onChangeField(record.id, 'importPrice', value ?? undefined)}
        />
      ),
    },
    {
      title: createPasteTitle(t('domesticProducts.oemPrice', '零售价'), 'oemPrice'),
      dataIndex: 'oemPrice',
      width: 120,
      render: (_, record) => (
        <div onPaste={(event) => handlePaste(event, record.id, 'oemPrice')}>
          <InputNumber
            min={0}
            precision={2}
            value={record.oemPrice}
            disabled={!canEdit}
            style={{ width: '100%' }}
            onChange={(value) => onChangeField(record.id, 'oemPrice', value ?? undefined)}
          />
        </div>
      ),
    },
    {
      title: t('common.action', '操作'),
      key: 'action',
      width: 90,
      render: (_, record) =>
        canEdit ? (
          <Button danger type="link" onClick={() => onRemoveRow(record.id)}>
            {t('common.delete', '删除')}
          </Button>
        ) : null,
    },
  ]

  return (
    <Modal
      title={product ? t('domesticProducts.setItemsTitle', '套装子项 - {{name}}', { name: product.itemNumber || product.name }) : t('domesticProducts.setItemsTitleShort', '套装子项')}
      open={open}
      width={1100}
      destroyOnHidden
      onCancel={onCancel}
      onOk={onSubmit}
      okText={t('common.save', '保存')}
      cancelText={t('common.close', '关闭')}
      confirmLoading={saving}
      okButtonProps={{ disabled: !canEdit }}
    >
      <Space direction="vertical" size={4} style={{ marginBottom: 16 }}>
          <Typography.Text type="secondary">
            {t('domesticProducts.setItemsHint', '仅对套装商品开放编辑，普通商品和多码商品不展示此入口。')}
        </Typography.Text>
        {canEdit ? (
          <Typography.Text type="secondary">
            {t('domesticProducts.setItemsPasteHint', '点击商品名称、国内价或零售价列后，可粘贴 Excel 单列数据。')}
          </Typography.Text>
        ) : null}
        {canEdit ? (
          <Button type="dashed" onClick={onAddRow}>
            {t('domesticProducts.addSubItem', '新增子项')}
          </Button>
        ) : null}
      </Space>
      <Table
        rowKey="id"
        loading={loading}
        columns={columns}
        dataSource={items}
        pagination={false}
        scroll={{ x: 980, y: 420 }}
      />
      <Space style={{ marginTop: 16, width: '100%', justifyContent: 'flex-end' }}>
        <Typography.Text strong>
          {t('domesticProducts.setItemsDomesticTotal', '国内价合计')}: {totals.domesticPriceTotal?.toFixed(2) ?? '--'}
        </Typography.Text>
        <Typography.Text strong>
          {t('domesticProducts.setItemsOemTotal', '零售价合计')}: {totals.oemPriceTotal?.toFixed(2) ?? '--'}
        </Typography.Text>
      </Space>
    </Modal>
  )
}

export default function DomesticProductsPage() {
  const { t, i18n } = useTranslation()
  const statusOptions = getStatusOptions(t)
  const [form] = Form.useForm<ProductFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<DomesticProductItem | null>(null)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [data, setData] = useState<DomesticProductItem[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [searchText, setSearchText] = useState('')
  const [supplierCode, setSupplierCode] = useState<string>()
  const [productType, setProductType] = useState<ProductType>()
  const [isActive, setIsActive] = useState<boolean>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [sortField, setSortField] = useState('createdAt')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend')
  const [exportConfigOpen, setExportConfigOpen] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [includeLabelPrice, setIncludeLabelPrice] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportMessage, setExportMessage] = useState('')
  const [setItemsOpen, setSetItemsOpen] = useState(false)
  const [setItemsLoading, setSetItemsLoading] = useState(false)
  const [setItemsSaving, setSetItemsSaving] = useState(false)
  const [currentSetProduct, setCurrentSetProduct] = useState<DomesticProductItem | null>(null)
  const [setItemsDraft, setSetItemsDraft] = useState<DomesticProductSetItem[]>([])
  const { access } = useAuthStore()

  const buildGridQuery = (overrides: Partial<DomesticProductGridQuery> = {}): DomesticProductGridQuery => ({
    page,
    pageSize,
    searchText,
    supplierCode,
    productType,
    isActive,
    sortField,
    sortOrder,
    ...overrides,
  })

  const loadData = async (overrides: Partial<DomesticProductGridQuery> = {}) => {
    const query = buildGridQuery(overrides)

    setLoading(true)
    try {
      const result = await getDomesticProductsGrid(query)
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setSelectedRowKeys([])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.loadFailed', '加载国内商品失败'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void Promise.all([
      loadData({ page: 1 }),
      getSupplierOptions()
        .then(setSuppliers)
        .catch((error) => {
          console.error(error)
          message.error(t('domesticProducts.loadSuppliersFailed', '加载供应商列表失败'))
        }),
    ])
  }, [])

  const handleOpenCreate = () => {
    setEditingItem(null)
    form.resetFields()
    form.setFieldsValue({
      productType: 0,
      isActive: true,
    })
    setModalOpen(true)
  }

  const handleOpenEdit = (record: DomesticProductItem) => {
    setEditingItem(record)
    form.setFieldsValue({
      supplierCode: record.supplierCode,
      productName: record.name,
      englishProductName: record.nameEn,
      hbProductNo: record.itemNumber,
      barcode: record.barcode,
      productSpecification: record.specs,
      productType: record.productType,
      domesticPrice: record.domesticPrice,
      oemPrice: record.labelPrice,
      importPrice: record.importPrice,
      packingQuantity: record.packingQty,
      unitVolume: record.volume,
      middlePackQuantity: record.middlePackQty,
      packingSize: record.packingSize,
      material: record.material,
      remarks: record.remark,
      productImage: record.productImage,
      isActive: record.isActive,
    })
    setModalOpen(true)
  }

  const handleCloseModal = () => {
    setModalOpen(false)
    setEditingItem(null)
    form.resetFields()
  }

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      setSaving(true)

      if (editingItem) {
        await updateDomesticProduct(editingItem.id, values as UpdateDomesticProductPayload)
        message.success(t('domesticProducts.updateSuccess', '更新国内商品成功'))
      } else {
        await createDomesticProduct(values as CreateDomesticProductPayload)
        message.success(t('domesticProducts.createSuccess', '新建国内商品成功'))
      }

      handleCloseModal()
      void loadData({ page: editingItem ? page : 1 })
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.saveFailed', '保存国内商品失败'))
    } finally {
      setSaving(false)
    }
  }

  const handleBatchDelete = async () => {
    try {
      await batchDeleteDomesticProducts(selectedRowKeys.map(String))
      message.success(t('domesticProducts.batchDeleteSuccess', '已删除 {{count}} 个国内商品', { count: selectedRowKeys.length }))
      void loadData({ page: 1 })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.batchDeleteFailed', '批量删除国内商品失败'))
    }
  }

  const handleOpenSetItems = async (record: DomesticProductItem) => {
    setCurrentSetProduct(record)
    setSetItemsOpen(true)
    setSetItemsLoading(true)
    setSetItemsDraft([])
    try {
      const items = await getDomesticProductSetItems(record.id)
      setSetItemsDraft(items)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.loadSetItemsFailed', '加载套装子项失败'))
      setSetItemsOpen(false)
      setCurrentSetProduct(null)
    } finally {
      setSetItemsLoading(false)
    }
  }

  const handleSaveSetItems = async () => {
    if (!currentSetProduct) {
      return
    }

    try {
      setSetItemsSaving(true)
      await updateDomesticProductSetItems(currentSetProduct.id, setItemsDraft)
      const totals = calculateSetItemPriceTotals(setItemsDraft)

      const shouldSyncDomesticPrice = totals.hasDomesticPrice
      const shouldSyncOemPrice = totals.hasOemPrice

      const priceSyncPayload = buildSetProductPriceSyncPayload(currentSetProduct, totals)
      if (priceSyncPayload) {
        await updateDomesticProduct(currentSetProduct.id, priceSyncPayload)
      }

      const syncLabels = [
        shouldSyncDomesticPrice ? t('domesticProducts.domesticPrice', '国内价') : '',
        shouldSyncOemPrice ? t('domesticProducts.oemPrice', '零售价') : '',
      ].filter(Boolean)
      const syncFields = (() => {
        if (syncLabels.length <= 1) {
          return syncLabels[0] || ''
        }

        if (i18n.language.startsWith('zh')) {
          return syncLabels.join('、')
        }

        if (syncLabels.length === 2) {
          return `${syncLabels[0]} and ${syncLabels[1]}`
        }

        return `${syncLabels.slice(0, -1).join(', ')}, and ${syncLabels[syncLabels.length - 1]}`
      })()

      message.success(
        syncLabels.length
          ? t('domesticProducts.setItemsUpdatedWithPriceSync', '套装子项已更新，已同步主码{{fields}}合计', { fields: syncFields })
          : t('domesticProducts.setItemsUpdated', '套装子项已更新'),
      )
      setSetItemsOpen(false)
      setCurrentSetProduct(null)
      setSetItemsDraft([])
      void loadData({ page })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.saveSetItemsFailed', '保存套装子项失败'))
    } finally {
      setSetItemsSaving(false)
    }
  }

  const handleExport = async () => {
    try {
      setExporting(true)
      setExportProgress(0)
      setExportMessage(t('domesticProducts.preparingExport', '准备导出...'))

      const selectedProducts = selectedRowKeys.length
        ? data.filter((item) => selectedRowKeys.includes(item.id))
        : []

      let productsToExport = selectedProducts
      if (!productsToExport.length) {
        if (!total) {
          message.warning(t('domesticProducts.noDataToExport', '没有可导出的商品数据'))
          return
        }

        const exportQuery = buildGridQuery({
          page: 1,
          pageSize: Math.max(total, 1),
        })
        const result = await getDomesticProductsGrid(exportQuery)
        productsToExport = result.items
      }

      if (!productsToExport.length) {
        message.warning(t('domesticProducts.noDataToExport', '没有可导出的商品数据'))
        return
      }

      await exportDomesticProductsToExcel(productsToExport, {
        includeLabelPrice,
        fileName: t('domesticProducts.exportFileName', '国内商品'),
        onProgress: (progress, nextMessage) => {
          setExportProgress(progress)
          setExportMessage(nextMessage)
        },
      })

      message.success(t('domesticProducts.exportSuccess', '导出成功'))
      setExportConfigOpen(false)
      setExportProgress(0)
      setExportMessage('')
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('domesticProducts.exportFailed', '导出失败'))
    } finally {
      setExporting(false)
    }
  }

  const columns = useMemo<ColumnsType<DomesticProductItem>>(
    () => [
      { title: '#', dataIndex: 'rowNumber', width: 70, fixed: 'left' },
      {
        title: t('domesticProducts.hbProductNo', 'HB货号'),
        dataIndex: 'itemNumber',
        width: 150,
        fixed: 'left',
        sorter: true,
        render: (value: string) =>
          value ? (
            <Space size={4}>
              <span>{value}</span>
              <Tooltip title={t('common.copy', '复制')}>
                <Button
                  size="small"
                  type="text"
                  icon={<CopyOutlined />}
                  onClick={() => void copyTextToClipboard(value)}
                />
              </Tooltip>
            </Space>
          ) : (
            '--'
          ),
      },
      {
        title: t('domesticProducts.productImage', '商品图片'),
        dataIndex: 'productImage',
        width: 90,
        render: (value: string | undefined, record) => (
          <Image
            src={value}
            alt={record.name}
            width={44}
            height={44}
            style={{ borderRadius: 4, objectFit: 'cover' }}
            fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
          />
        ),
      },
      {
        title: t('domesticProducts.supplierCode', '供应商编码'),
        dataIndex: 'supplierCode',
        width: 100,
        sorter: true,
      },
      {
        title: t('domesticProducts.supplierName', '供应商名称'),
        dataIndex: 'supplierName',
        width: 150,
        sorter: true,
      },
      {
      title: t('domesticProducts.barcode', '条码'),
      dataIndex: 'barcode',
      width: 180,
        render: (value: string | undefined) =>
          value ? <BarcodePreview value={value} textMaxWidth={180} compactCopy /> : '--',
      },
      {
        title: t('domesticProducts.productName', '商品名称'),
        dataIndex: 'name',
        width: 180,
        sorter: true,
        ellipsis: true,
      },
      {
        title: t('domesticProducts.englishName', '英文名称'),
        dataIndex: 'nameEn',
        width: 180,
        ellipsis: true,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: t('domesticProducts.productType', '商品类型'),
        dataIndex: 'productType',
        width: 120,
        render: (value: ProductType) => ProductTypeLabels[value] || '--',
      },
      {
        title: t('domesticProducts.domesticPrice', '国内价'),
        dataIndex: 'domesticPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: t('domesticProducts.oemPrice', '零售价'),
        dataIndex: 'labelPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: t('domesticProducts.importPrice', '进口价'),
        dataIndex: 'importPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: t('domesticProducts.packingQuantity', '装箱数'),
        dataIndex: 'packingQty',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: t('domesticProducts.unitVolume', '体积'),
        dataIndex: 'volume',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: t('domesticProducts.middlePack', '中包'),
        dataIndex: 'middlePackQty',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: t('domesticProducts.status', '状态'),
        dataIndex: 'isActive',
        width: 90,
        render: (value: boolean) => <Tag color={value ? 'success' : 'default'}>{value ? t('common.enable', '启用') : t('common.disable', '停用')}</Tag>,
      },
      {
        title: t('domesticProducts.updatedAt', '更新时间'),
        dataIndex: 'updatedAt',
        width: 180,
        sorter: true,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: t('domesticProducts.updatedBy', '更新人'),
        dataIndex: 'updatedBy',
        width: 140,
        render: (value: string | undefined) => value || '--',
      },
      {
      title: t('common.action', '操作'),
        key: 'action',
        width: 220,
        fixed: 'right',
        render: (_, record) => (
          <Space size={0}>
            {access.canWriteProduct ? (
              <Button type="link" icon={<EditOutlined />} onClick={() => handleOpenEdit(record)}>
                {t('common.edit', '编辑')}
              </Button>
            ) : null}
            {record.productType === 1 ? (
              <Button type="link" icon={<GiftOutlined />} onClick={() => void handleOpenSetItems(record)}>
                {t('domesticProducts.setItems', '套装子项')}
              </Button>
            ) : (
              <Tooltip title={t('domesticProducts.setItemsOnlyHint', '仅套装商品可编辑套装子项')}>
                <Button type="link" icon={<GiftOutlined />} disabled>
                  {t('domesticProducts.setItems', '套装子项')}
                </Button>
              </Tooltip>
            )}
          </Space>
        ),
      },
    ],
    [access.canWriteProduct],
  )

  return (
    <PageContainer
      title={t('domesticProducts.pageTitle', '国内商品')}
      subtitle={t('domesticProducts.pageSubtitle', '独立于仓库商品的国内商品主数据页，首轮保留列表、筛选、编辑、套装子项、批量删除和导出能力。')}
      extra={
        <Space wrap>
          <Button
            icon={<DownloadOutlined />}
            loading={exporting}
            disabled={exporting}
            onClick={() => setExportConfigOpen(true)}
          >
            {t('domesticProducts.exportExcel', '导出 Excel')}
          </Button>
          {access.canWriteProduct ? (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleOpenCreate}>
              {t('domesticProducts.createProduct', '新建商品')}
            </Button>
          ) : null}
          {exporting ? (
            <Typography.Text type="secondary">
              {exportMessage} ({exportProgress}%)
            </Typography.Text>
          ) : null}
        </Space>
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            prefix={<SearchOutlined />}
            placeholder={t('domesticProducts.searchPlaceholder', '搜索商品名称 / 货号 / 条码 / 英文名 / 供应商')}
            style={{ width: 300 }}
            allowClear
          />
          <Select
            value={supplierCode}
            onChange={setSupplierCode}
            options={buildSupplierOptions(suppliers)}
            placeholder={t('domesticProducts.allSuppliers', '全部供应商')}
            style={{ width: 240 }}
            showSearch
            filterOption={filterSupplierOption}
            allowClear
          />
          <Select
            value={productType}
            onChange={setProductType}
            options={productTypeOptions}
            placeholder={t('domesticProducts.allProductTypes', '全部商品类型')}
            style={{ width: 160 }}
            allowClear
          />
          <Select
            value={isActive}
            onChange={setIsActive}
            options={statusOptions}
            placeholder={t('domesticProducts.allStatus', '全部状态')}
            style={{ width: 140 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData({ page: 1 })}>
            {t('common.query', '查询')}
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setSearchText('')
              setSupplierCode(undefined)
              setProductType(undefined)
              setIsActive(undefined)
              setSortField('createdAt')
              setSortOrder('descend')
              void loadData({
                page: 1,
                searchText: '',
                supplierCode: undefined,
                productType: undefined,
                isActive: undefined,
                sortField: 'createdAt',
                sortOrder: 'descend',
              })
            }}
          >
            {t('common.reset', '重置')}
          </Button>
          {access.canDeleteProduct ? (
            <Popconfirm
          title={t('domesticProducts.confirmBatchDelete', '确认批量删除选中的商品吗？')}
          description={t('domesticProducts.selectedRecordsWarning', '已选择 {{count}} 条记录，删除后不可恢复。', { count: selectedRowKeys.length })}
          okText={t('common.delete', '删除')}
          cancelText={t('common.cancel', '取消')}
              disabled={!selectedRowKeys.length}
              onConfirm={() => void handleBatchDelete()}
            >
              <Button danger icon={<DeleteOutlined />} disabled={!selectedRowKeys.length}>
                {t('domesticProducts.batchDelete', '批量删除')}
              </Button>
            </Popconfirm>
          ) : null}
        </Space>

        <Table
          rowKey="id"
          virtual
          loading={loading}
          columns={columns}
          dataSource={data}
          rowSelection={{
            fixed: true,
            columnWidth: 56,
            selectedRowKeys,
            onChange: setSelectedRowKeys,
          }}
          scroll={{ x: 2200, y: 620 }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
          }}
          onChange={(pagination: TablePaginationConfig, _, sorter: SorterResult<DomesticProductItem> | SorterResult<DomesticProductItem>[]) => {
            const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
            const nextSortField =
              typeof nextSorter?.field === 'string' ? nextSorter.field : sortField
            const nextSortOrder =
              nextSorter?.order === 'ascend' || nextSorter?.order === 'descend'
                ? nextSorter.order
                : sortOrder

            setSortField(nextSortField)
            setSortOrder(nextSortOrder)
            void loadData({
              page: pagination.current || 1,
              pageSize: pagination.pageSize || pageSize,
              sortField: nextSortField,
              sortOrder: nextSortOrder,
            })
          }}
        />
      </Card>

      <ProductFormModal
        open={modalOpen}
        saving={saving}
        editingItem={editingItem}
        suppliers={suppliers}
        form={form}
        onCancel={handleCloseModal}
        onSubmit={() => void handleSave()}
      />

      <SetItemsModal
        open={setItemsOpen}
        loading={setItemsLoading}
        saving={setItemsSaving}
        product={currentSetProduct}
        items={setItemsDraft}
        canEdit={access.canWriteProduct}
        onCancel={() => {
          setSetItemsOpen(false)
          setCurrentSetProduct(null)
          setSetItemsDraft([])
        }}
        onAddRow={() => {
          setSetItemsDraft((current) => [
            ...current,
            {
              id: `temp_${Date.now()}_${Math.random()}`,
            },
          ])
        }}
        onRemoveRow={(rowId) => {
          setSetItemsDraft((current) => current.filter((item) => item.id !== rowId))
        }}
        onChangeField={(rowId, field, value) => {
          setSetItemsDraft((current) =>
            current.map((item) => (item.id === rowId ? { ...item, [field]: value } : item)),
          )
        }}
        onPasteColumn={(rowId, field, clipboardText) => {
          setSetItemsDraft((current) => {
            const result = applySetItemColumnPaste({
              items: current,
              startRowId: rowId,
              field,
              clipboardText,
              createId: (rowIndex) => `temp_${Date.now()}_${rowIndex}_${Math.random()}`,
            })

            if (result.skippedCount) {
              message.warning(t('domesticProducts.setItemsPasteSkipped', '已跳过 {{count}} 个无效价格', { count: result.skippedCount }))
            }

            return result.items
          })
        }}
        onSubmit={() => void handleSaveSetItems()}
      />

      <Modal
        title={t('domesticProducts.exportExcel', '导出 Excel')}
        open={exportConfigOpen}
        okText={t('domesticProducts.startExport', '开始导出')}
        cancelText={t('common.cancel', '取消')}
        confirmLoading={exporting}
        onCancel={() => {
          if (!exporting) {
            setExportConfigOpen(false)
          }
        }}
        onOk={() => void handleExport()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text>
            {selectedRowKeys.length
              ? t('domesticProducts.exportSelectedCount', '将导出已选择的 {{count}} 件商品', { count: selectedRowKeys.length })
              : t('domesticProducts.exportFilteredCount', '将导出当前筛选结果共 {{count}} 件商品', { count: total })}
          </Typography.Text>
          <Checkbox checked={includeLabelPrice} onChange={(event) => setIncludeLabelPrice(event.target.checked)}>
            {t('domesticProducts.includeLabelPrice', '包含零售列（零售价）')}
          </Checkbox>
          {exporting ? (
            <Typography.Text type="secondary">
              {exportMessage} ({exportProgress}%)
            </Typography.Text>
          ) : null}
        </Space>
      </Modal>
    </PageContainer>
  )
}
