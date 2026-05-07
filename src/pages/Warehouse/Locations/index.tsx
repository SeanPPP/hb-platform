import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Image,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState } from 'react'
import PageContainer from '../../../components/PageContainer'
import {
  createLocation,
  deleteLocation,
  getLocationList,
  updateLocation,
} from '../../../services/locationService'
import { useAuthStore } from '../../../store/auth'
import BarcodePreview from '../../../components/BarcodePreview'
import type {
  CreateLocationParams,
  LocationItem,
  LocationProduct,
  UpdateLocationParams,
} from '../../../types/location'
import { copyTextToClipboard } from '../../../utils/clipboard'

interface LocationFormValues {
  locationCode: string
  locationBarcode?: string
  locationType?: number
  status?: number
}

const locationTypeOptions = [
  { value: 0, label: '存储货位' },
  { value: 1, label: '拣货货位' },
]

const statusOptions = [
  { value: 1, label: '启用' },
  { value: 0, label: '停用' },
]

const usageOptions = [
  { value: true, label: '已使用' },
  { value: false, label: '未使用' },
]

function formatLocationType(value?: number | null) {
  return locationTypeOptions.find((item) => item.value === value)?.label || '--'
}

function formatStatus(value?: number | null) {
  return value === 1 ? <Tag color="success">启用</Tag> : <Tag>停用</Tag>
}

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

function renderProductsText(products: LocationProduct[], field: 'itemNumber' | 'productName') {
  if (!products?.length) {
    return '--'
  }

  const values = products.map((item) => item[field]).filter((value): value is string => Boolean(value))
  if (!values.length) {
    return '--'
  }

  const display = values.slice(0, 3).join('，')
  const suffix = values.length > 3 ? ` +${values.length - 3}` : ''

  return <Tooltip title={values.join('，')}>{`${display}${suffix}`}</Tooltip>
}

function getProductFieldValues(products: LocationProduct[], field: 'itemNumber' | 'productName') {
  if (!products?.length) {
    return []
  }

  return products.map((item) => item[field]).filter((value): value is string => Boolean(value))
}

function renderCopyableProducts(products: LocationProduct[], field: 'itemNumber' | 'productName') {
  const values = getProductFieldValues(products, field)
  if (!values.length) {
    return '--'
  }

  const display = values.slice(0, 3).join('，')
  const suffix = values.length > 3 ? ` +${values.length - 3}` : ''
  const fullText = values.join('，')

  return (
    <Space size={4} wrap>
      <Tooltip title={fullText}>
        <Typography.Text>{`${display}${suffix}`}</Typography.Text>
      </Tooltip>
      <Tooltip title="复制货号">
        <Button
          size="small"
          type="text"
          icon={<CopyOutlined />}
          onClick={() => void copyTextToClipboard(fullText)}
        />
      </Tooltip>
    </Space>
  )
}

function renderProductImages(products: LocationProduct[]) {
  const images = products?.map((item) => item.productImage).filter((value): value is string => Boolean(value)) ?? []

  if (!images.length) {
    return '--'
  }

  return (
    <Image.PreviewGroup>
      <Space size={4}>
        {images.slice(0, 3).map((image, index) => (
          <Image
            key={`${image}-${index}`}
            src={image}
            width={32}
            height={32}
            style={{ borderRadius: 4, objectFit: 'cover' }}
          />
        ))}
        {images.length > 3 ? <Tag>+{images.length - 3}</Tag> : null}
      </Space>
    </Image.PreviewGroup>
  )
}

export default function WarehouseLocationsPage() {
  const [form] = Form.useForm<LocationFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<LocationItem | null>(null)
  const [data, setData] = useState<LocationItem[]>([])
  const [locationCodeKeyword, setLocationCodeKeyword] = useState('')
  const [locationBarcodeKeyword, setLocationBarcodeKeyword] = useState('')
  const [updatedByKeyword, setUpdatedByKeyword] = useState('')
  const [locationTypeFilter, setLocationTypeFilter] = useState<number | null | undefined>(undefined)
  const [usageFilter, setUsageFilter] = useState<boolean | null | undefined>(undefined)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const { access } = useAuthStore()

  const loadData = async (
    nextPage = page,
    nextPageSize = pageSize,
    nextLocationType = locationTypeFilter,
    nextUsage = usageFilter,
    nextLocationCode = locationCodeKeyword,
    nextLocationBarcode = locationBarcodeKeyword,
    nextUpdatedBy = updatedByKeyword,
  ) => {
    setLoading(true)
    try {
      const result = await getLocationList({
        locationType: nextLocationType,
        isUsed: nextUsage,
        locationCode: nextLocationCode || undefined,
        locationBarcode: nextLocationBarcode || undefined,
        updatedBy: nextUpdatedBy || undefined,
        pageNumber: nextPage,
        pageSize: nextPageSize,
        sortBy: 'LocationCode',
      })

      setData(result.items)
      setTotal(result.total)
      setPage(result.pageNumber)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载仓库标签列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const handleCreate = () => {
    setEditingItem(null)
    form.resetFields()
    form.setFieldsValue({
      locationType: 0,
      status: 1,
    })
    setModalOpen(true)
  }

  const handleEdit = (record: LocationItem) => {
    setEditingItem(record)
    form.setFieldsValue({
      locationCode: record.locationCode,
      locationBarcode: record.locationBarcode,
      locationType: record.locationType ?? 0,
      status: record.status ?? 1,
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
        await updateLocation(editingItem.locationGuid, values as UpdateLocationParams)
        message.success('更新仓库标签成功')
      } else {
        await createLocation(values as CreateLocationParams)
        message.success('创建仓库标签成功')
      }

      handleCloseModal()
      void loadData(editingItem ? page : 1, pageSize)
    } catch (error) {
      if (error instanceof Error) {
        message.error(error.message || '保存仓库标签失败')
      }
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (record: LocationItem) => {
    try {
      await deleteLocation(record.locationGuid)
      message.success('删除仓库标签成功')
      void loadData(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '删除仓库标签失败')
    }
  }

  const columns: ColumnsType<LocationItem> = [
    {
      title: '序号',
      key: 'index',
      width: 80,
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: '货位代码',
      dataIndex: 'locationCode',
      width: 220,
      render: (value: string | undefined) => (
        <BarcodePreview value={value} align="left" textMaxWidth={140} compactCopy />
      ),
    },
    {
      title: '货位条码',
      dataIndex: 'locationBarcode',
      width: 220,
      render: (value: string | undefined) => (
        <BarcodePreview value={value} align="left" textMaxWidth={140} compactCopy />
      ),
    },
    {
      title: '标签状态',
      dataIndex: 'status',
      width: 100,
      render: (value: number | null | undefined) => formatStatus(value),
    },
    {
      title: '货位类型',
      dataIndex: 'locationType',
      width: 120,
      render: (value: number | null | undefined) => formatLocationType(value),
    },
    {
      title: '使用状态',
      key: 'usage',
      width: 100,
      render: (_, record) =>
        record.products?.length ? <Tag color="processing">已使用</Tag> : <Tag>未使用</Tag>,
    },
    {
      title: '商品货号',
      key: 'itemNumbers',
      width: 220,
      render: (_, record) => renderCopyableProducts(record.products, 'itemNumber'),
    },
    {
      title: '商品名称',
      key: 'productNames',
      width: 220,
      render: (_, record) => renderProductsText(record.products, 'productName'),
    },
    {
      title: '商品图片',
      key: 'productImages',
      width: 140,
      render: (_, record) => renderProductImages(record.products),
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      width: 180,
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
      width: 180,
      fixed: 'right',
      render: (_, record) => (
        <Space size={4}>
          <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确认删除该仓库标签吗？"
            description="删除后不可恢复。"
            okText="删除"
            cancelText="取消"
            onConfirm={() => void handleDelete(record)}
          >
            <Button danger type="link" icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title="仓库标签管理"
      subtitle="首个仓库业务页面已接入新框架，当前提供列表、筛选、新增、编辑和删除能力。"
      extra={
        access.canManageWarehouse ? (
          <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
            新建标签
          </Button>
        ) : null
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索货位代码"
            value={locationCodeKeyword}
            onChange={(event) => setLocationCodeKeyword(event.target.value)}
            style={{ width: 180 }}
            allowClear
          />
          <Input
            placeholder="搜索货位条码"
            value={locationBarcodeKeyword}
            onChange={(event) => setLocationBarcodeKeyword(event.target.value)}
            style={{ width: 180 }}
            allowClear
          />
          <Input
            placeholder="搜索更新人"
            value={updatedByKeyword}
            onChange={(event) => setUpdatedByKeyword(event.target.value)}
            style={{ width: 160 }}
            allowClear
          />
          <Space>
            <Typography.Text>标签类型</Typography.Text>
            <Select
              value={locationTypeFilter}
              onChange={setLocationTypeFilter}
              options={locationTypeOptions}
              placeholder="全部类型"
              allowClear
              style={{ width: 160 }}
            />
          </Space>
          <Space>
            <Typography.Text>使用状态</Typography.Text>
            <Select
              value={usageFilter}
              onChange={setUsageFilter}
              options={usageOptions}
              placeholder="全部状态"
              allowClear
              style={{ width: 160 }}
            />
          </Space>
          <Button type="primary" icon={<SearchOutlined />} onClick={() => void loadData(1, pageSize)}>
            查询
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setLocationCodeKeyword('')
              setLocationBarcodeKeyword('')
              setUpdatedByKeyword('')
              setLocationTypeFilter(undefined)
              setUsageFilter(undefined)
              void loadData(1, pageSize, undefined, undefined, '', '', '')
            }}
          >
            重置
          </Button>
        </Space>

        <Table
          rowKey="locationGuid"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 1280 }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>

      <Modal
        title={editingItem ? '编辑仓库标签' : '新建仓库标签'}
        open={modalOpen}
        onOk={() => void handleSave()}
        onCancel={handleCloseModal}
        confirmLoading={saving}
        destroyOnClose
        okText="保存"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="locationCode"
            label="货位代码"
            rules={[{ required: true, message: '请输入货位代码' }]}
          >
            <Input placeholder="请输入货位代码" />
          </Form.Item>
          <Form.Item name="locationBarcode" label="货位条码">
            <Input placeholder="请输入货位条码" />
          </Form.Item>
          <Form.Item name="locationType" label="货位类型">
            <Select placeholder="请选择货位类型" options={locationTypeOptions} />
          </Form.Item>
          <Form.Item name="status" label="标签状态">
            <Select placeholder="请选择标签状态" options={statusOptions} />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
