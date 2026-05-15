import { CopyOutlined, DownloadOutlined, EditOutlined, GiftOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons'
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
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import {
  getDomesticProductSetItems,
  getSupplierOptions,
  updateDomesticProductSetItems,
} from '../../../services/domesticProductService'
import { exportDomesticProductsToExcel, type ExportResult } from '../../../services/exportService'
import {
  batchToggleWarehouseProductsActive,
  getWarehouseProductsTable,
  updateWarehouseProductFull,
  type WarehouseProductListItem,
  type WarehouseProductsTableQuery,
} from '../../../services/warehouseProductService'
import { useAuthStore } from '../../../store/auth'
import type {
  DomesticProductSetItem,
  ProductType,
  SupplierOption,
} from '../../../types/domesticProduct'
import { ProductTypeLabels } from '../../../types/domesticProduct'
import { copyTextToClipboard } from '../../../utils/clipboard'
import CreateProductModal from './CreateProductModal'
import ImportFromDomesticModal from './ImportFromDomesticModal'
import ImportNonHbModal from './ImportNonHbModal'

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
  { value: true, label: t('warehouse.active', '上架') },
  { value: false, label: t('warehouse.inactive', '下架') },
]

const productTypeOptions = Object.entries(ProductTypeLabels).map(([value, label]) => ({
  value: Number(value),
  label,
}))

const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 80

const warehouseProductsTableStyle = `
  .warehouse-products-table .ant-table-tbody > tr > td {
    height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    max-height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    vertical-align: middle;
  }

  .warehouse-products-table .ant-table-cell {
    white-space: nowrap;
  }

  .warehouse-products-table .warehouse-products-image-cell,
  .warehouse-products-table .warehouse-products-barcode-cell {
    min-height: 64px;
    max-height: 64px;
    overflow: hidden;
    display: flex;
    align-items: center;
  }

  .warehouse-products-table .warehouse-products-image-cell .ant-image,
  .warehouse-products-table .warehouse-products-image-cell img {
    width: 44px;
    height: 44px;
    display: block;
  }

  .warehouse-products-table .warehouse-products-barcode-cell svg,
  .warehouse-products-table .warehouse-products-barcode-cell canvas,
  .warehouse-products-table .warehouse-products-barcode-cell img {
    max-height: 56px !important;
  }

  .warehouse-products-table .warehouse-products-supplier-cell {
    white-space: normal;
    overflow: hidden;
  }

  .warehouse-products-table .warehouse-products-supplier-cell .ant-tag {
    max-width: 100%;
    margin-inline-end: 0;
    white-space: normal;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    line-height: 20px;
    padding-block: 2px;
  }

  .warehouse-products-table .warehouse-products-text-2line {
    white-space: normal;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    line-height: 20px;
    word-break: break-word;
  }
`

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
    label: `${item.code} - ${item.name}`,
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
  editingItem: WarehouseProductListItem | null
  suppliers: SupplierOption[]
  form: ReturnType<typeof Form.useForm<ProductFormValues>>[0]
  onCancel: () => void
  onSubmit: () => void
}) {
  const { t } = useTranslation()
  return (
    <Modal
      title={editingItem ? t('warehouse.editProductTitle', '编辑商品 - {{name}}', { name: editingItem.itemNumber || editingItem.name }) : t('warehouse.editProduct', '编辑商品')}
      open={open}
      width={920}
      destroyOnClose
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
            label={t('warehouse.productType', '商品类型')}
            style={{ width: 180 }}
            rules={[{ required: true, message: t('warehouse.selectProductType', '请选择商品类型') }]}
          >
            <Select placeholder={t('warehouse.selectProductType', '请选择商品类型')} options={productTypeOptions} />
          </Form.Item>
          <Form.Item name="isActive" label={t('domesticProducts.status', '状态')} valuePropName="checked" style={{ width: 120 }}>
            <Switch checkedChildren={t('warehouse.active', '上架')} unCheckedChildren={t('warehouse.inactive', '下架')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item
            name="productName"
            label={t('domesticProducts.productName', '商品名称')}
            style={{ flex: 1 }}
            rules={[{ required: true, message: t('warehouse.enterProductName', '请输入商品名称') }]}
          >
            <Input placeholder={t('warehouse.enterProductName', '请输入商品名称')} />
          </Form.Item>
          <Form.Item name="englishProductName" label={t('warehouse.englishName', '英文名称')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterEnglishName', '请输入英文名称')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="hbProductNo" label={t('warehouse.hbProductNo', 'HB货号')} style={{ flex: 1 }}>
            <Input disabled={Boolean(editingItem)} placeholder={t('warehouse.autoGenerate', '不填则后端自动生成')} />
          </Form.Item>
          <Form.Item name="barcode" label={t('domesticProducts.barcode', '条码')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.autoGenerate', '不填则后端自动生成')} />
          </Form.Item>
          <Form.Item name="productSpecification" label={t('domesticProducts.specification', '规格')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterSpec', '请输入商品规格')} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="domesticPrice" label={t('domesticProducts.domesticPrice', '国内价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="oemPrice" label={t('productCreation.privateLabelPrice', '贴牌价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="importPrice" label={t('warehouse.importPrice', '进口价')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingQuantity" label={t('warehouse.packingQuantity', '装箱数')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="middlePackQuantity" label={t('warehouse.middlePackQuantity', '中包数量')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="unitVolume" label={t('warehouse.volume', '体积')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={4} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingSize" label={t('warehouse.packingSize', '包装尺寸')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterPackingSize', '请输入包装尺寸')} />
          </Form.Item>
          <Form.Item name="material" label={t('warehouse.material', '材质')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterMaterial', '请输入材质')} />
          </Form.Item>
          <Form.Item name="productImage" label={t('warehouse.imageUrl', '图片地址')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterImageUrl', '请输入图片 URL')} />
          </Form.Item>
        </Space>

        <Form.Item name="remarks" label={t('common.remarks', '备注')}>
          <Input.TextArea rows={3} placeholder={t('common.enterRemarks', '请输入备注')} />
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
  onSubmit,
}: {
  open: boolean
  loading: boolean
  saving: boolean
  product: WarehouseProductListItem | null
  items: DomesticProductSetItem[]
  canEdit: boolean
  onCancel: () => void
  onAddRow: () => void
  onRemoveRow: (rowId: string) => void
  onChangeField: (rowId: string, field: keyof DomesticProductSetItem, value: string | number | undefined) => void
  onSubmit: () => void
}) {
  const { t } = useTranslation()
  const columns: ColumnsType<DomesticProductSetItem> = [
    {
      title: t('domesticProducts.productName', '商品名称'),
      dataIndex: 'productName',
      render: (_, record) => (
        <Input
          value={record.productName}
          disabled={!canEdit}
          onChange={(event) => onChangeField(record.id, 'productName', event.target.value)}
        />
      ),
    },
    {
      title: t('warehouse.setProductNo', '套装货号'),
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
      width: 180,
      render: (_, record) => (
        <Input
          value={record.setBarcode}
          disabled={!canEdit}
          onChange={(event) => onChangeField(record.id, 'setBarcode', event.target.value)}
        />
      ),
    },
    {
      title: t('domesticProducts.domesticPrice', '国内价'),
      dataIndex: 'domesticPrice',
      width: 120,
      render: (_, record) => (
        <InputNumber
          min={0}
          precision={2}
          value={record.domesticPrice}
          disabled={!canEdit}
          style={{ width: '100%' }}
          onChange={(value) => onChangeField(record.id, 'domesticPrice', value ?? undefined)}
        />
      ),
    },
    {
      title: t('warehouse.importPrice', '进口价'),
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
      title: t('productCreation.privateLabelPrice', '贴牌价'),
      dataIndex: 'oemPrice',
      width: 120,
      render: (_, record) => (
        <InputNumber
          min={0}
          precision={2}
          value={record.oemPrice}
          disabled={!canEdit}
          style={{ width: '100%' }}
          onChange={(value) => onChangeField(record.id, 'oemPrice', value ?? undefined)}
        />
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
      title={product ? t('warehouse.setDetailsTitle', '套装子项 - {{name}}', { name: product.itemNumber || product.name }) : t('warehouse.setDetails', '套装子项')}
      open={open}
      width={1100}
      destroyOnClose
      onCancel={onCancel}
      onOk={onSubmit}
      okText={t('common.save', '保存')}
      cancelText={t('common.close', '关闭')}
      confirmLoading={saving}
      okButtonProps={{ disabled: !canEdit }}
    >
      <Space style={{ marginBottom: 16 }}>
        <Typography.Text type="secondary">
          t('warehouse.setEditHint', '仅对套装商品开放编辑，普通商品和多码商品不展示此入口。')
        </Typography.Text>
        {canEdit ? (
          <Button type="dashed" onClick={onAddRow}>
            {t('warehouse.addSubItem', '新增子项')}
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
    </Modal>
  )
}

export default function WarehouseProductsPage() {
  const { t } = useTranslation()
  const [form] = Form.useForm<ProductFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<WarehouseProductListItem | null>(null)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [data, setData] = useState<WarehouseProductListItem[]>([])
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
  const [includeBarcodeImage, setIncludeBarcodeImage] = useState(true)
  const [includeProductImage, setIncludeProductImage] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportMessage, setExportMessage] = useState('')
  const [importFromDomesticOpen, setImportFromDomesticOpen] = useState(false)
  const [importNonHbOpen, setImportNonHbOpen] = useState(false)
  const [setItemsOpen, setSetItemsOpen] = useState(false)
  const [setItemsLoading, setSetItemsLoading] = useState(false)
  const [setItemsSaving, setSetItemsSaving] = useState(false)
  const [currentSetProduct, setCurrentSetProduct] = useState<WarehouseProductListItem | null>(null)
  const [setItemsDraft, setSetItemsDraft] = useState<DomesticProductSetItem[]>([])
  const [batchActionLoading, setBatchActionLoading] = useState(false)
  const [togglingProductCodes, setTogglingProductCodes] = useState<string[]>([])
  const [exportFailDetailOpen, setExportFailDetailOpen] = useState(false)
  const [exportFailDetail, setExportFailDetail] = useState<ExportResult['failedProductImages']>([])
  const { access } = useAuthStore()

  const buildGridQuery = (overrides: Partial<WarehouseProductsTableQuery> = {}): WarehouseProductsTableQuery => ({
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

  const loadData = async (overrides: Partial<WarehouseProductsTableQuery> = {}) => {
    const query = buildGridQuery(overrides)

    setLoading(true)
    try {
      const result = await getWarehouseProductsTable(query)
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setSelectedRowKeys([])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.loadProductsFailed', '加载仓库商品失败'))
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
          message.error(t('productCreation.loadSupplierListFailed', '加载供应商列表失败'))
        }),
    ])
  }, [])

  const handleOpenCreate = () => {
    setCreateModalOpen(true)
  }

  const handleOpenEdit = (record: WarehouseProductListItem) => {
    setEditingItem(record)
    form.setFieldsValue({
      supplierCode: record.domesticSupplierCode,
      productName: record.name,
      englishProductName: record.nameEn,
      hbProductNo: record.itemNumber,
      barcode: record.barcode,
      productType: record.productType,
      domesticPrice: record.domesticPrice,
      oemPrice: record.labelPrice,
      importPrice: record.importPrice,
      packingQuantity: record.packingQty,
      unitVolume: record.volume,
      middlePackQuantity: record.middlePackQty,
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
    if (!editingItem) {
      return
    }

    try {
      const values = await form.validateFields()
      setSaving(true)
      await updateWarehouseProductFull(editingItem.productCode, {
        productName: values.productName,
        englishName: values.englishProductName,
        productSpecification: values.productSpecification,
        productType: values.productType,
        domesticPrice: values.domesticPrice,
        oemPrice: values.oemPrice,
        importPrice: values.importPrice,
        packingQuantity: values.packingQuantity,
        unitVolume: values.unitVolume,
        middlePackQuantity: values.middlePackQuantity,
        packingSize: values.packingSize,
        material: values.material,
        remark: values.remarks,
        productImage: values.productImage,
        isActive: values.isActive,
        supplierCode: values.supplierCode,
      })
      message.success(t('warehouse.updateProductSuccess', '更新商品成功'))

      handleCloseModal()
      void loadData({ page })
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.saveProductFailed', '保存商品失败'))
    } finally {
      setSaving(false)
    }
  }

  const handleBatchToggleActive = async (nextIsActive: boolean) => {
    if (!selectedRowKeys.length) {
      return
    }

    try {
      setBatchActionLoading(true)
      const result = await batchToggleWarehouseProductsActive({
        productCodes: selectedRowKeys.map(String),
        isActive: nextIsActive,
      })

      if (!result.success) {
        message.error(result.message || t('warehouse.batchStatusUpdateFailed', '批量更新状态失败'))
        return
      }

      message.success(result.message || t('warehouse.batchStatusUpdated', '已批量{{status}} {{count}} 个商品', { status: nextIsActive ? t('warehouse.active', '上架') : t('warehouse.inactive', '下架'), count: selectedRowKeys.length }))
      void loadData({ page })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.batchStatusUpdateFailed', '批量更新状态失败'))
    } finally {
      setBatchActionLoading(false)
    }
  }

  const handleToggleSingleActive = async (record: WarehouseProductListItem, nextIsActive: boolean) => {
    try {
      setTogglingProductCodes((current) => [...current, record.productCode])
      const result = await batchToggleWarehouseProductsActive({
        productCodes: [record.productCode],
        isActive: nextIsActive,
      })

      if (!result.success) {
        message.error(result.message || t('warehouse.toggleStatusFailed', '切换状态失败'))
        return
      }

      setData((current) =>
        current.map((item) => (item.productCode === record.productCode ? { ...item, isActive: nextIsActive } : item)),
      )
      message.success(result.message || t('warehouse.statusToggled', '商品已{{status}}', { status: nextIsActive ? t('warehouse.active', '上架') : t('warehouse.inactive', '下架') }))
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.toggleStatusFailed', '切换状态失败'))
    } finally {
      setTogglingProductCodes((current) => current.filter((code) => code !== record.productCode))
    }
  }

  const handleOpenSetItems = async (record: WarehouseProductListItem) => {
    setCurrentSetProduct(record)
    setSetItemsOpen(true)
    setSetItemsLoading(true)
    setSetItemsDraft([])
    try {
      const items = await getDomesticProductSetItems(record.productCode)
      setSetItemsDraft(items)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.loadSetItemsFailed', '加载套装子项失败'))
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
      await updateDomesticProductSetItems(currentSetProduct.productCode, setItemsDraft)
      message.success(t('warehouse.setItemsUpdated', '套装子项已更新'))
      setSetItemsOpen(false)
      setCurrentSetProduct(null)
      setSetItemsDraft([])
      void loadData()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.saveSetItemsFailed', '保存套装子项失败'))
    } finally {
      setSetItemsSaving(false)
    }
  }

  const handleExport = async () => {
    try {
      setExporting(true)
      setExportProgress(0)
      setExportMessage(t('warehouse.preparingExport', '准备导出...'))

      const selectedProducts = selectedRowKeys.length
        ? data.filter((item) => selectedRowKeys.includes(item.id))
        : []

      let productsToExport = selectedProducts
      if (!productsToExport.length) {
        if (!total) {
          message.warning(t('warehouse.noDataToExport', '没有可导出的商品数据'))
          return
        }

        const exportQuery = buildGridQuery({
          page: 1,
          pageSize: Math.max(total, 1),
        })
        const result = await getWarehouseProductsTable(exportQuery)
        productsToExport = result.items
      }

      if (!productsToExport.length) {
        message.warning(t('warehouse.noDataToExport', '没有可导出的商品数据'))
        return
      }

      const exportResult = await exportDomesticProductsToExcel(
        productsToExport.map(
          (item) => ({
            itemNumber: item.itemNumber,
            barcode: item.barcode,
            name: item.name,
            labelPrice: item.labelPrice,
            productImage: item.productImage,
          }),
        ),
        {
          includeLabelPrice,
          includeBarcodeImage,
          includeProductImage,
          fileName: t('warehouse.warehouseProducts', '仓库商品'),
          onProgress: (progress, nextMessage) => {
            setExportProgress(progress)
            setExportMessage(nextMessage)
          },
        },
      )

      if (exportResult.failedProductImages.length > 0) {
        setExportFailDetail(exportResult.failedProductImages)
        setExportFailDetailOpen(true)
      } else {
        message.success(t('warehouse.exportSuccess', '导出成功'))
      }
      setExportConfigOpen(false)
      setExportProgress(0)
      setExportMessage('')
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.exportFailed', '导出失败'))
    } finally {
      setExporting(false)
    }
  }

  const columns = useMemo<ColumnsType<WarehouseProductListItem>>(
    () => [
      { title: '#', dataIndex: 'rowNumber', width: 30, fixed: 'left' },
      {
        title: t('warehouse.hbProductNo', 'HB货号'),
        dataIndex: 'itemNumber',
        width: 120,
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
        title: t('warehouse.productImage', '商品图片'),
        dataIndex: 'productImage',
        width: 80,
        render: (value: string | undefined) => (
          <div className="warehouse-products-image-cell">
            <Image
              src={value}
              alt=""
              width={44}
              height={44}
              style={{ borderRadius: 4, objectFit: 'cover' }}
              fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
            />
          </div>
        ),
      },
      {
        title: t('warehouse.domesticSupplier', '国内供应商'),
        dataIndex: 'domesticSupplierCode',
        width: 150,
        sorter: true,
        render: (_value, record) =>
          record.domesticSupplierCode || record.domesticSupplierName ? (
            <div className="warehouse-products-supplier-cell">
              <Tag color="blue">
                {[record.domesticSupplierCode, record.domesticSupplierName].filter(Boolean).join(' - ')}
              </Tag>
            </div>
          ) : (
            '--'
          ),
      },
      
      {
        title: t('domesticProducts.productName', '商品名称'),
        dataIndex: 'name',
        width: 200,
        sorter: true,
        render: (value: string | undefined) =>
          value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
      },
      {
        title: t('warehouse.englishName', '英文名称'),
        dataIndex: 'nameEn',
        width: 200,
        render: (value: string | undefined) =>
          value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
      },
      {
        title: t('domesticProducts.barcode', '条码'),
        dataIndex: 'barcode',
        width: 180,
        render: (value: string | undefined) =>
          value ? (
            <div className="warehouse-products-barcode-cell">
              <BarcodePreview value={value} textMaxWidth={180} compactCopy />
            </div>
          ) : (
            '--'
          ),
      },
      {
        title: t('domesticProducts.status', '状态'),
        dataIndex: 'isActive',
        width: 110,
        render: (value: boolean, record) => (
          <Switch
            checked={value}
            checkedChildren={t('warehouse.active', '上架')}
            unCheckedChildren={t('warehouse.inactive', '下架')}
            disabled={!access.canWriteProduct || togglingProductCodes.includes(record.productCode)}
            loading={togglingProductCodes.includes(record.productCode)}
            onChange={(nextChecked) => void handleToggleSingleActive(record, nextChecked)}
          />
        ),
      },
      {
        title: t('warehouse.productType', '商品类型'),
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
        title: t('productCreation.privateLabelPrice', '贴牌价'),
        dataIndex: 'labelPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: t('warehouse.importPrice', '进口价'),
        dataIndex: 'importPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: t('warehouse.packingQuantity', '装箱数'),
        dataIndex: 'packingQty',
        width: 140,
        render: (value: number | undefined, record) =>
          value !== undefined && value !== null ? (
            <Space size={4}>
              <span>{value}</span>
              {record.isPackingQtyFallback ? <Tag color="gold">{t('warehouse.domestic', '国内')}</Tag> : <Tag color="green">{t('warehouse.warehouse', '仓库')}</Tag>}
            </Space>
          ) : (
            '--'
          ),
      },
      {
        title: t('warehouse.volume', '体积'),
        dataIndex: 'volume',
        width: 140,
        render: (value: number | undefined, record) =>
          value !== undefined && value !== null ? (
            <Space size={4}>
              <span>{value}</span>
              {record.isVolumeFallback ? <Tag color="gold">{t('warehouse.domestic', '国内')}</Tag> : <Tag color="green">{t('warehouse.warehouse', '仓库')}</Tag>}
            </Space>
          ) : (
            '--'
          ),
      },
      {
        title: t('warehouse.australianSupplier', '澳洲供应商'),
        dataIndex: 'localSupplierCode',
        width: 180,
        sorter: true,
        render: (_value, record) =>
          record.localSupplierName ? (
            <div className="warehouse-products-supplier-cell">
              <Tag color="purple">{record.localSupplierName}</Tag>
            </div>
          ) : (
            '--'
          ),
      },
      
      {
        title: t('warehouse.updatedAt', '更新时间'),
        dataIndex: 'updatedAt',
        width: 180,
        sorter: true,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: t('warehouse.updatedBy', '更新人'),
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
                {t('warehouse.setSubItems', '套装子项')}
              </Button>
            ) : (
              <Tooltip title={t('warehouse.setEditOnlyHint', '仅套装商品可编辑套装子项')}>
                <Button type="link" icon={<GiftOutlined />} disabled>
                  {t('warehouse.setSubItems', '套装子项')}
                </Button>
              </Tooltip>
            )}
          </Space>
        ),
      },
    ],
    [access.canWriteProduct, togglingProductCodes],
  )

  return (
    <>
      <style>{warehouseProductsTableStyle}</style>
      <PageContainer
        title={t('warehouse.productManagement', '仓库商品管理')}
        subtitle={t('warehouse.productManagementSubtitle', '已接仓库主列表、新建编辑、国内/本地双供应商显示、批量上下架与单条状态切换。')}
        extra={
          <Space wrap>
          <Button
            icon={<DownloadOutlined />}
            loading={exporting}
            disabled={exporting}
            onClick={() => setExportConfigOpen(true)}
          >
            {t('warehouse.exportExcel', '导出 Excel')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportFromDomesticOpen(true)}>
            {t('warehouse.importFromDomestic', '从国内导入')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportNonHbOpen(true)}>
            {t('warehouse.importNonHb.title', '导入非国内商品')}
          </Button>
          <Button
            icon={<GiftOutlined />}
            onClick={() => message.info(t('warehouse.batchSetMigrated', '批量建套装商品迁移到第二轮补齐'))}
          >
            {t('warehouse.batchCreateSet', '批量建套装')}
          </Button>
          <Button
            icon={<UploadOutlined />}
            onClick={() => message.info(t('warehouse.batchImageUploadMigrated', '批量图片上传迁移到第二轮补齐'))}
          >
            {t('warehouse.batchImageUpload', '批量图片上传')}
          </Button>
          {access.canWriteProduct ? (
            <Popconfirm
              title={t('warehouse.confirmBatchActivate', '确认批量上架选中的商品吗？')}
              okText={t('warehouse.active', '上架')}
              cancelText={t('common.cancel', '取消')}
              disabled={!selectedRowKeys.length}
              onConfirm={() => void handleBatchToggleActive(true)}
            >
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchActivate', '批量上架')}
              </Button>
            </Popconfirm>
          ) : null}
          {access.canWriteProduct ? (
            <Popconfirm
              title={t('warehouse.confirmBatchDeactivate', '确认批量下架选中的商品吗？')}
              okText={t('warehouse.inactive', '下架')}
              cancelText={t('common.cancel', '取消')}
              disabled={!selectedRowKeys.length}
              onConfirm={() => void handleBatchToggleActive(false)}
            >
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchDeactivate', '批量下架')}
              </Button>
            </Popconfirm>
          ) : null}
          {access.canWriteProduct ? (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleOpenCreate}>
              {t('warehouse.createProduct', '新建商品')}
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
            placeholder={t('warehouse.searchProductFull', '搜索商品名称 / 货号 / 条码 / 英文名 / 国内供应商 / 本地供应商')}
            style={{ width: 300 }}
            allowClear
          />
          <Select
            value={supplierCode}
            onChange={setSupplierCode}
            options={buildSupplierOptions(suppliers)}
            placeholder={t('warehouse.allDomesticSuppliers', '全部国内供应商')}
            style={{ width: 240 }}
            showSearch
            filterOption={filterSupplierOption}
            allowClear
          />
          <Select
            value={productType}
            onChange={setProductType}
            options={productTypeOptions}
            placeholder={t('warehouse.allProductTypes', '全部商品类型')}
            style={{ width: 160 }}
            allowClear
          />
          <Select
            value={isActive}
            onChange={setIsActive}
            options={getStatusOptions(t)}
            placeholder={t('warehouse.allStatus', '全部状态')}
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
        </Space>

          <Table
            className="warehouse-products-table"
            rowKey="productCode"
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
            }}
            onChange={(pagination: TablePaginationConfig, _, sorter: SorterResult<WarehouseProductListItem> | SorterResult<WarehouseProductListItem>[]) => {
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

      <CreateProductModal
        open={createModalOpen}
        suppliers={suppliers}
        onCancel={() => setCreateModalOpen(false)}
        onSuccess={() => {
          setCreateModalOpen(false)
          void loadData({ page: 1 })
        }}
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
        onSubmit={() => void handleSaveSetItems()}
      />

      <ImportFromDomesticModal
        open={importFromDomesticOpen}
        onCancel={() => setImportFromDomesticOpen(false)}
        onSuccess={() => void loadData({ page: 1 })}
      />

      <ImportNonHbModal
        open={importNonHbOpen}
        onCancel={() => setImportNonHbOpen(false)}
        onSuccess={() => void loadData({ page: 1 })}
      />

      <Modal
        title={t('warehouse.exportExcel', '导出 Excel')}
        open={exportConfigOpen}
        okText={t('warehouse.startExport', '开始导出')}
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
              ? t('warehouse.exportSelected', '将导出已选择的 {{count}} 件商品', { count: selectedRowKeys.length })
              : t('warehouse.exportFiltered', '将导出当前筛选结果共 {{count}} 件商品', { count: total })}
          </Typography.Text>
          <Checkbox checked={includeLabelPrice} onChange={(event) => setIncludeLabelPrice(event.target.checked)}>
            {t('warehouse.includeLabelPrice', '包含零售列（贴牌价）')}
          </Checkbox>
          <Checkbox checked={includeBarcodeImage} onChange={(event) => setIncludeBarcodeImage(event.target.checked)}>
            {t('warehouse.includeBarcodeImage', '包含条码图片')}
          </Checkbox>
          <Checkbox checked={includeProductImage} onChange={(event) => setIncludeProductImage(event.target.checked)}>
            {t('warehouse.includeProductImage', '包含商品图片（从图片地址下载）')}
          </Checkbox>
          {exporting ? (
            <Typography.Text type="secondary">
              {exportMessage} ({exportProgress}%)
            </Typography.Text>
          ) : null}
        </Space>
      </Modal>

      <Modal
        title={t('warehouse.exportCompleteFailed', '导出完成 — {{count}} 张图片下载失败', { count: exportFailDetail.length })}
        open={exportFailDetailOpen}
        width={700}
        footer={null}
        onCancel={() => setExportFailDetailOpen(false)}
      >
        <Typography.Paragraph type="secondary" style={{ marginBottom: 12 }}>
          {t('warehouse.imageDownloadFailedMsg', '以下商品图片未能成功下载，Excel 对应行的图片列已标记为「图片下载失败」。')}
        </Typography.Paragraph>
        <Table
          size="small"
          pagination={false}
          scroll={{ y: 360 }}
          dataSource={exportFailDetail}
          rowKey="itemNumber"
          columns={[
            { title: t('productImport.hbProductNoCol', '货号'), dataIndex: 'itemNumber', width: 120 },
            { title: t('warehouse.failureReason', '失败原因'), dataIndex: 'reason', width: 200 },
            {
              title: t('warehouse.imageUrl', '图片地址'),
              dataIndex: 'url',
              ellipsis: true,
              render: (val: string) => (
                <Tooltip title={val}>
                  <span style={{ fontSize: 12, wordBreak: 'break-all' }}>{val}</span>
                </Tooltip>
              ),
            },
          ]}
        />
      </Modal>
      </PageContainer>
    </>
  )
}
