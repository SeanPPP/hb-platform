import { CopyOutlined, DeleteOutlined, DownloadOutlined, EditOutlined, GiftOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons'
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
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
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

const statusOptions = [
  { value: true, label: '启用' },
  { value: false, label: '停用' },
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
  return (
    <Modal
      title={editingItem ? `编辑商品 - ${editingItem.itemNumber || editingItem.name}` : '新建商品'}
      open={open}
      width={920}
      destroyOnClose
      okText="保存"
      cancelText="取消"
      confirmLoading={saving}
      onCancel={onCancel}
      onOk={onSubmit}
    >
      <Form form={form} layout="vertical" preserve={false}>
        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item
            name="supplierCode"
            label="供应商"
            style={{ flex: 1 }}
            rules={editingItem ? [] : [{ required: true, message: '请选择供应商' }]}
          >
            <Select
              disabled={Boolean(editingItem)}
              placeholder="请选择供应商"
              showSearch
              optionFilterProp="label"
              options={suppliers.map((item) => ({
                value: item.code,
                label: `${item.code} - ${item.name}`,
              }))}
            />
          </Form.Item>
          <Form.Item
            name="productType"
            label="商品类型"
            style={{ width: 180 }}
            rules={[{ required: true, message: '请选择商品类型' }]}
          >
            <Select placeholder="请选择商品类型" options={productTypeOptions} />
          </Form.Item>
          <Form.Item name="isActive" label="状态" valuePropName="checked" style={{ width: 120 }}>
            <Switch checkedChildren="启用" unCheckedChildren="停用" />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item
            name="productName"
            label="商品名称"
            style={{ flex: 1 }}
            rules={[{ required: true, message: '请输入商品名称' }]}
          >
            <Input placeholder="请输入商品名称" />
          </Form.Item>
          <Form.Item name="englishProductName" label="英文名称" style={{ flex: 1 }}>
            <Input placeholder="请输入英文名称" />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="hbProductNo" label="HB货号" style={{ flex: 1 }}>
            <Input disabled={Boolean(editingItem)} placeholder="不填则后端自动生成" />
          </Form.Item>
          <Form.Item name="barcode" label="条码" style={{ flex: 1 }}>
            <Input placeholder="不填则后端自动生成" />
          </Form.Item>
          <Form.Item name="productSpecification" label="规格" style={{ flex: 1 }}>
            <Input placeholder="请输入商品规格" />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="domesticPrice" label="国内价" style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="oemPrice" label="贴牌价" style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="importPrice" label="进口价" style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingQuantity" label="装箱数" style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="middlePackQuantity" label="中包数量" style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="unitVolume" label="体积" style={{ flex: 1 }}>
            <InputNumber min={0} precision={4} style={{ width: '100%' }} />
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingSize" label="包装尺寸" style={{ flex: 1 }}>
            <Input placeholder="请输入包装尺寸" />
          </Form.Item>
          <Form.Item name="material" label="材质" style={{ flex: 1 }}>
            <Input placeholder="请输入材质" />
          </Form.Item>
          <Form.Item name="productImage" label="图片地址" style={{ flex: 1 }}>
            <Input placeholder="请输入图片 URL" />
          </Form.Item>
        </Space>

        <Form.Item name="remarks" label="备注">
          <Input.TextArea rows={3} placeholder="请输入备注" />
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
  product: DomesticProductItem | null
  items: DomesticProductSetItem[]
  canEdit: boolean
  onCancel: () => void
  onAddRow: () => void
  onRemoveRow: (rowId: string) => void
  onChangeField: (rowId: string, field: keyof DomesticProductSetItem, value: string | number | undefined) => void
  onSubmit: () => void
}) {
  const columns: ColumnsType<DomesticProductSetItem> = [
    {
      title: '商品名称',
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
      title: '套装货号',
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
      title: '条码',
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
      title: '国内价',
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
      title: '进口价',
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
      title: '贴牌价',
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
      title: '操作',
      key: 'action',
      width: 90,
      render: (_, record) =>
        canEdit ? (
          <Button danger type="link" onClick={() => onRemoveRow(record.id)}>
            删除
          </Button>
        ) : null,
    },
  ]

  return (
    <Modal
      title={product ? `套装子项 - ${product.itemNumber || product.name}` : '套装子项'}
      open={open}
      width={1100}
      destroyOnClose
      onCancel={onCancel}
      onOk={onSubmit}
      okText="保存"
      cancelText="关闭"
      confirmLoading={saving}
      okButtonProps={{ disabled: !canEdit }}
    >
      <Space style={{ marginBottom: 16 }}>
        <Typography.Text type="secondary">
          仅对套装商品开放编辑，普通商品和多码商品不展示此入口。
        </Typography.Text>
        {canEdit ? (
          <Button type="dashed" onClick={onAddRow}>
            新增子项
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
  const [importFromDomesticOpen, setImportFromDomesticOpen] = useState(false)
  const [importNonHbOpen, setImportNonHbOpen] = useState(false)
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
      message.error(error instanceof Error ? error.message : '加载仓库商品失败')
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
          message.error('加载供应商列表失败')
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
        message.success('更新商品成功')
      } else {
        await createDomesticProduct(values as CreateDomesticProductPayload)
        message.success('创建商品成功')
      }

      handleCloseModal()
      void loadData({ page: editingItem ? page : 1 })
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : '保存商品失败')
    } finally {
      setSaving(false)
    }
  }

  const handleBatchDelete = async () => {
    try {
      await batchDeleteDomesticProducts(selectedRowKeys.map(String))
      message.success(`已删除 ${selectedRowKeys.length} 个商品`)
      void loadData({ page: 1 })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '批量删除商品失败')
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
      message.error(error instanceof Error ? error.message : '加载套装子项失败')
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
      message.success('套装子项已更新')
      setSetItemsOpen(false)
      setCurrentSetProduct(null)
      setSetItemsDraft([])
      void loadData()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '保存套装子项失败')
    } finally {
      setSetItemsSaving(false)
    }
  }

  const handleExport = async () => {
    try {
      setExporting(true)
      setExportProgress(0)
      setExportMessage('准备导出...')

      const selectedProducts = selectedRowKeys.length
        ? data.filter((item) => selectedRowKeys.includes(item.id))
        : []

      let productsToExport = selectedProducts
      if (!productsToExport.length) {
        if (!total) {
          message.warning('没有可导出的商品数据')
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
        message.warning('没有可导出的商品数据')
        return
      }

      await exportDomesticProductsToExcel(productsToExport, {
        includeLabelPrice,
        fileName: '仓库商品',
        onProgress: (progress, nextMessage) => {
          setExportProgress(progress)
          setExportMessage(nextMessage)
        },
      })

      message.success('导出成功')
      setExportConfigOpen(false)
      setExportProgress(0)
      setExportMessage('')
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '导出失败')
    } finally {
      setExporting(false)
    }
  }

  const columns = useMemo<ColumnsType<DomesticProductItem>>(
    () => [
      { title: '#', dataIndex: 'rowNumber', width: 70, fixed: 'left' },
      {
        title: 'HB货号',
        dataIndex: 'itemNumber',
        width: 150,
        sorter: true,
        render: (value: string) =>
          value ? (
            <Space size={4}>
              <span>{value}</span>
              <Tooltip title="复制">
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
        title: '商品图片',
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
        title: '供应商编码',
        dataIndex: 'supplierCode',
        width: 140,
        sorter: true,
      },
      {
        title: '供应商名称',
        dataIndex: 'supplierName',
        width: 180,
        sorter: true,
      },
      {
        title: '商品名称',
        dataIndex: 'name',
        width: 220,
        sorter: true,
        ellipsis: true,
      },
      {
        title: '英文名称',
        dataIndex: 'nameEn',
        width: 220,
        ellipsis: true,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: '条码',
        dataIndex: 'barcode',
        width: 240,
        render: (value: string | undefined) =>
          value ? (
            <BarcodePreview value={value} textMaxWidth={180} compactCopy />
          ) : (
            '--'
          ),
      },
      {
        title: '商品类型',
        dataIndex: 'productType',
        width: 120,
        render: (value: ProductType) => ProductTypeLabels[value] || '--',
      },
      {
        title: '国内价',
        dataIndex: 'domesticPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: '贴牌价',
        dataIndex: 'labelPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: '进口价',
        dataIndex: 'importPrice',
        width: 100,
        render: (value: number | undefined) => formatPrice(value),
      },
      {
        title: '装箱数',
        dataIndex: 'packingQty',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: '体积',
        dataIndex: 'volume',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: '中包',
        dataIndex: 'middlePackQty',
        width: 100,
        render: (value: number | undefined) => value ?? '--',
      },
      {
        title: '状态',
        dataIndex: 'isActive',
        width: 90,
        render: (value: boolean) => <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>,
      },
      {
        title: '更新时间',
        dataIndex: 'updatedAt',
        width: 180,
        sorter: true,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: '更新人',
        dataIndex: 'updatedBy',
        width: 140,
        render: (value: string | undefined) => value || '--',
      },
      {
        title: '操作',
        key: 'action',
        width: 220,
        fixed: 'right',
        render: (_, record) => (
          <Space size={0}>
            {access.canWriteProduct ? (
              <Button type="link" icon={<EditOutlined />} onClick={() => handleOpenEdit(record)}>
                编辑
              </Button>
            ) : null}
            {record.productType === 1 ? (
              <Button type="link" icon={<GiftOutlined />} onClick={() => void handleOpenSetItems(record)}>
                套装子项
              </Button>
            ) : (
              <Tooltip title="仅套装商品可编辑套装子项">
                <Button type="link" icon={<GiftOutlined />} disabled>
                  套装子项
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
      title="仓库商品管理"
      subtitle="已接主列表、新建编辑、国内导入、非国内商品导入、导出 Excel 与套装子项维护。"
      extra={
        <Space wrap>
          <Button
            icon={<DownloadOutlined />}
            loading={exporting}
            disabled={exporting}
            onClick={() => setExportConfigOpen(true)}
          >
            导出 Excel
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportFromDomesticOpen(true)}>
            从国内导入
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportNonHbOpen(true)}>
            导入非国内商品
          </Button>
          <Button
            icon={<GiftOutlined />}
            onClick={() => message.info('批量建套装商品迁移到第二轮补齐')}
          >
            批量建套装
          </Button>
          <Button
            icon={<UploadOutlined />}
            onClick={() => message.info('批量图片上传迁移到第二轮补齐')}
          >
            批量图片上传
          </Button>
          {access.canWriteProduct ? (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleOpenCreate}>
              新建商品
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
            placeholder="搜索商品名称 / 货号 / 条码 / 英文名 / 供应商"
            style={{ width: 300 }}
            allowClear
          />
          <Select
            value={supplierCode}
            onChange={setSupplierCode}
            options={suppliers.map((item) => ({ value: item.code, label: `${item.code} - ${item.name}` }))}
            placeholder="全部供应商"
            style={{ width: 240 }}
            showSearch
            optionFilterProp="label"
            allowClear
          />
          <Select
            value={productType}
            onChange={setProductType}
            options={productTypeOptions}
            placeholder="全部商品类型"
            style={{ width: 160 }}
            allowClear
          />
          <Select
            value={isActive}
            onChange={setIsActive}
            options={statusOptions}
            placeholder="全部状态"
            style={{ width: 140 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData({ page: 1 })}>
            查询
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
            重置
          </Button>
          {access.canDeleteProduct ? (
            <Popconfirm
              title="确认批量删除选中的商品吗？"
              description={`已选择 ${selectedRowKeys.length} 条记录，删除后不可恢复。`}
              okText="删除"
              cancelText="取消"
              disabled={!selectedRowKeys.length}
              onConfirm={() => void handleBatchDelete()}
            >
              <Button
                danger
                icon={<DeleteOutlined />}
                disabled={!selectedRowKeys.length}
              >
                批量删除
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
        title="导出 Excel"
        open={exportConfigOpen}
        okText="开始导出"
        cancelText="取消"
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
              ? `将导出已选择的 ${selectedRowKeys.length} 件商品`
              : `将导出当前筛选结果共 ${total} 件商品`}
          </Typography.Text>
          <Checkbox checked={includeLabelPrice} onChange={(event) => setIncludeLabelPrice(event.target.checked)}>
            包含零售列（贴牌价）
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
