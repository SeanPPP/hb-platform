import {
  CloudSyncOutlined,
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
import type { TableProps } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { SorterResult, SortOrder } from 'antd/es/table/interface'
import type { TFunction } from 'i18next'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import './compact.css'
import {
  createLocation,
  deleteLocation,
  getLocationList,
  syncLocationsFromHq,
  updateLocation,
} from '../../../services/locationService'
import { useAuthStore } from '../../../store/auth'
import BarcodePreview from '../../../components/BarcodePreview'
import type {
  CreateLocationParams,
  LocationHqSyncResult,
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

function getLocationTypeOptions(t: (key: string, options?: Record<string, unknown>) => string) {
  return [
    { value: 0, label: t('warehouseLocations.storageLocation') },
    { value: 1, label: t('warehouseLocations.pickingLocation') },
  ]
}

function getStatusOptions(t: (key: string, options?: Record<string, unknown>) => string) {
  return [
    { value: 1, label: t('common.enable') },
    { value: 0, label: t('common.disable') },
  ]
}

function getUsageOptions(t: (key: string, options?: Record<string, unknown>) => string) {
  return [
    { value: true, label: t('common.used') },
    { value: false, label: t('common.unused') },
  ]
}

function formatLocationType(
  value: number | null | undefined,
  options: Array<{ value: number; label: string }>,
) {
  return options.find((item) => item.value === value)?.label || '--'
}

function formatStatus(value: number | null | undefined, t: (key: string) => string) {
  return value === 1 ? <Tag color="success">{t('common.enable')}</Tag> : <Tag>{t('common.disable')}</Tag>
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

function formatSyncResult(result: LocationHqSyncResult, t: TFunction) {
  const added = result.addedCount ?? result.AddedCount ?? 0
  const updated = result.updatedCount ?? result.UpdatedCount ?? 0
  const errors = result.errorCount ?? result.ErrorCount ?? 0
  return t('warehouseLocations.syncResultStats', { added, updated, errors })
}

type ProductTextField = 'itemNumber' | 'productName'
type LocationSortBy =
  | 'LocationCode'
  | 'LocationType'
  | 'LocationBarcode'
  | 'Status'
  | 'Usage'
  | 'UpdatedAt'
  | 'UpdatedBy'

const DEFAULT_LOCATION_SORT_BY: LocationSortBy = 'LocationCode'
const DEFAULT_LOCATION_SORT_ORDER: SortOrder = 'ascend'

const LOCATION_SORT_FIELD_MAP: Record<string, LocationSortBy> = {
  locationCode: 'LocationCode',
  locationType: 'LocationType',
  locationBarcode: 'LocationBarcode',
  status: 'Status',
  usage: 'Usage',
  updatedAt: 'UpdatedAt',
  updatedBy: 'UpdatedBy',
}

function toApiSortDirection(order: SortOrder): 'asc' | 'desc' {
  return order === 'descend' ? 'desc' : 'asc'
}

function getSortField(sorter: SorterResult<LocationItem>) {
  const field = Array.isArray(sorter.field) ? sorter.field.join('.') : sorter.field
  const key = sorter.columnKey
  const candidate = field || key
  return candidate ? String(candidate) : undefined
}

function getProductFieldValues(products: LocationProduct[], field: ProductTextField | 'productBarcode') {
  if (!products?.length) {
    return []
  }

  return products.map((item) => item[field]).filter((value): value is string => Boolean(value))
}

function renderCopyableProducts(
  products: LocationProduct[],
  field: 'itemNumber' | 'productName',
  t: (key: string) => string,
) {
  const values = getProductFieldValues(products, field)
  if (!values.length) {
    return '--'
  }

  const display = values.slice(0, 3).join('，')
  const suffix = values.length > 3 ? ` +${values.length - 3}` : ''
  const fullText = values.join('，')

  return (
    <Space size={4} className="warehouse-locations-copyable-cell warehouse-locations-nowrap">
      <span className="warehouse-locations-copyable-content">
        <Tooltip title={fullText}>
          <Typography.Text className="warehouse-locations-nowrap">{`${display}${suffix}`}</Typography.Text>
        </Tooltip>
      </span>
      <Tooltip title={t('common.copy')}>
        <Button
          size="small"
          type="text"
          className="warehouse-locations-copy-button"
          icon={<CopyOutlined />}
          onClick={() => void copyTextToClipboard(fullText)}
        />
      </Tooltip>
    </Space>
  )
}

function renderCopyableProductBarcodes(
  products: LocationProduct[],
  t: (key: string) => string,
) {
  const barcodeProducts = products.filter((product) => Boolean(product.productBarcode))
  const values = barcodeProducts.map((product) => product.productBarcode).filter((value): value is string => Boolean(value))
  if (!values.length) {
    return '--'
  }

  const suffix = values.length > 3 ? ` +${values.length - 3}` : ''
  const fullText = values.join('，')

  return (
    <Space size={4} className="warehouse-locations-barcode-cell warehouse-locations-nowrap">
      <span className="warehouse-locations-barcode-content">
        <Tooltip title={fullText}>
          <Space size={3} className="warehouse-locations-product-barcode-list">
            {barcodeProducts.slice(0, 3).map((product, index) => (
              <span
                key={`${product.productBarcode}-${index}`}
                className="warehouse-locations-product-barcode-item"
              >
                <BarcodePreview
                  value={product.productBarcode}
                  align="left"
                  className="warehouse-locations-product-barcode-preview"
                  compactCopy={false}
                  gap={2}
                  options={{ height: 18, width: 1, margin: 0 }}
                  showCopy={false}
                  textNoWrap
                />
              </span>
            ))}
            {suffix ? <Typography.Text className="warehouse-locations-nowrap">{suffix}</Typography.Text> : null}
          </Space>
        </Tooltip>
      </span>
      <Tooltip title={t('common.copy')}>
        <Button
          size="small"
          type="text"
          className="warehouse-locations-copy-button"
          icon={<CopyOutlined />}
          onClick={() => void copyTextToClipboard(fullText)}
        />
      </Tooltip>
    </Space>
  )
}

function renderLocationProductName(products: LocationProduct[]) {
  const values = getProductFieldValues(products, 'productName')
  if (!values.length) {
    return '--'
  }

  const fullText = values.join('，')

  return (
    <Tooltip title={fullText}>
      <span className="warehouse-locations-two-line-text">{fullText}</span>
    </Tooltip>
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
  const { t } = useTranslation()
  const [form] = Form.useForm<LocationFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<LocationItem | null>(null)
  const [syncingFromHq, setSyncingFromHq] = useState(false)
  const [data, setData] = useState<LocationItem[]>([])
  const [locationCodeKeyword, setLocationCodeKeyword] = useState('')
  const [locationBarcodeKeyword, setLocationBarcodeKeyword] = useState('')
  const [updatedByKeyword, setUpdatedByKeyword] = useState('')
  const [locationTypeFilter, setLocationTypeFilter] = useState<number | null | undefined>(undefined)
  const [usageFilter, setUsageFilter] = useState<boolean | null | undefined>(undefined)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [sortBy, setSortBy] = useState<LocationSortBy>(DEFAULT_LOCATION_SORT_BY)
  const [sortOrder, setSortOrder] = useState<SortOrder>(DEFAULT_LOCATION_SORT_ORDER)
  const { access } = useAuthStore()
  const locationTypeOptions = getLocationTypeOptions(t)
  const statusOptions = getStatusOptions(t)
  const usageOptions = getUsageOptions(t)
  const canSyncLocationsFromHq = access.isAdmin || access.isWarehouseManager

  const loadData = async (
    nextPage = page,
    nextPageSize = pageSize,
    nextLocationType = locationTypeFilter,
    nextUsage = usageFilter,
    nextLocationCode = locationCodeKeyword,
    nextLocationBarcode = locationBarcodeKeyword,
    nextUpdatedBy = updatedByKeyword,
    nextSortBy = sortBy,
    nextSortOrder = sortOrder,
  ) => {
    setLoading(true)
    const effectiveSortBy = nextSortBy || DEFAULT_LOCATION_SORT_BY
    const effectiveSortOrder = nextSortOrder || DEFAULT_LOCATION_SORT_ORDER
    try {
      const result = await getLocationList({
        locationType: nextLocationType,
        isUsed: nextUsage,
        locationCode: nextLocationCode || undefined,
        locationBarcode: nextLocationBarcode || undefined,
        updatedBy: nextUpdatedBy || undefined,
        pageNumber: nextPage,
        pageSize: nextPageSize,
        sortBy: effectiveSortBy,
        sortDirection: toApiSortDirection(effectiveSortOrder),
      })

      setData(result.items)
      setTotal(result.total)
      setPage(result.pageNumber)
      setPageSize(result.pageSize)
      setSortBy(effectiveSortBy)
      setSortOrder(effectiveSortOrder)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouseLocations.loadFailed'))
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
        message.success(t('warehouseLocations.updateSuccess'))
      } else {
        await createLocation(values as CreateLocationParams)
        message.success(t('warehouseLocations.createSuccess'))
      }

      handleCloseModal()
      void loadData(editingItem ? page : 1, pageSize)
    } catch (error) {
      if (error instanceof Error) {
        message.error(error.message || t('warehouseLocations.saveFailed'))
      }
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (record: LocationItem) => {
    try {
      await deleteLocation(record.locationGuid)
      message.success(t('warehouseLocations.deleteSuccess'))
      void loadData(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouseLocations.deleteFailed'))
    }
  }

  const handleSyncFromHq = () => {
    Modal.confirm({
      title: t('warehouseLocations.syncFromHqTitle'),
      content: t('warehouseLocations.syncFromHqContent'),
      okText: t('warehouseLocations.syncFromHqConfirm'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        setSyncingFromHq(true)
        try {
          const result = await syncLocationsFromHq()
          Modal.success({
            title: t('warehouseLocations.syncFromHqSuccessTitle'),
            content: (
              <Space direction="vertical">
                <Typography.Text>{t('warehouseLocations.locationSyncResult')}: {formatSyncResult(result.locationResult, t)}</Typography.Text>
                <Typography.Text>{t('warehouseLocations.productLocationSyncResult')}: {formatSyncResult(result.productLocationResult, t)}</Typography.Text>
              </Space>
            ),
          })
          await loadData(1, pageSize)
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('warehouseLocations.syncFromHqFailed'))
        } finally {
          setSyncingFromHq(false)
        }
      },
    })
  }

  const handleTableChange: TableProps<LocationItem>['onChange'] = (pagination, _filters, sorter, extra) => {
    if (extra.action === 'sort') {
      const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
      const field = getSortField(currentSorter)
      const nextSortBy = field ? LOCATION_SORT_FIELD_MAP[field] : DEFAULT_LOCATION_SORT_BY
      const nextSortOrder = currentSorter?.order || DEFAULT_LOCATION_SORT_ORDER
      void loadData(
        1,
        pagination.pageSize || pageSize,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        nextSortBy,
        nextSortOrder,
      )
      return
    }

    if (extra.action === 'paginate') {
      void loadData(pagination.current || page, pagination.pageSize || pageSize)
    }
  }

  const columns: ColumnsType<LocationItem> = [
    {
      title: t('column.index'),
      key: 'index',
      width: 56,
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('column.locationCode'),
      dataIndex: 'locationCode',
      sorter: true,
      sortOrder: sortBy === 'LocationCode' ? sortOrder : null,
      width: 150,
      render: (value: string | undefined) => (
        <BarcodePreview value={value} align="left" compactCopy textNoWrap />
      ),
    },
    {
      title: t('column.locationType'),
      dataIndex: 'locationType',
      sorter: true,
      sortOrder: sortBy === 'LocationType' ? sortOrder : null,
      width: 86,
      render: (value: number | null | undefined) => formatLocationType(value, locationTypeOptions),
    },
    {
      title: t('column.locationBarcode'),
      dataIndex: 'locationBarcode',
      sorter: true,
      sortOrder: sortBy === 'LocationBarcode' ? sortOrder : null,
      width: 150,
      render: (value: string | undefined) => (
        <BarcodePreview value={value} align="left" compactCopy textNoWrap />
      ),
    },
    {
      title: t('column.locationStatus'),
      dataIndex: 'status',
      sorter: true,
      sortOrder: sortBy === 'Status' ? sortOrder : null,
      width: 76,
      render: (value: number | null | undefined) => formatStatus(value, t),
    },
    {
      title: t('column.usageStatus'),
      key: 'usage',
      sorter: true,
      sortOrder: sortBy === 'Usage' ? sortOrder : null,
      width: 86,
      render: (_, record) =>
        record.products?.length ? <Tag color="processing">{t('common.used')}</Tag> : <Tag>{t('common.unused')}</Tag>,
    },
    {
      title: t('column.itemNumber'),
      key: 'itemNumbers',
      width: 118,
      render: (_, record) => renderCopyableProducts(record.products, 'itemNumber', t),
    },
    {
      title: t('column.productBarcode'),
      key: 'productBarcodes',
      width: 150,
      render: (_, record) => renderCopyableProductBarcodes(record.products, t),
    },
    {
      title: t('column.productName'),
      key: 'productNames',
      width: 190,
      render: (_, record) => renderLocationProductName(record.products),
    },
    {
      title: t('column.image'),
      key: 'productImages',
      width: 112,
      render: (_, record) => renderProductImages(record.products),
    },
    {
      title: t('column.updateTime'),
      dataIndex: 'updatedAt',
      sorter: true,
      sortOrder: sortBy === 'UpdatedAt' ? sortOrder : null,
      width: 150,
      render: (value: string | undefined) => (
        <span className="warehouse-locations-nowrap">{formatDateTime(value)}</span>
      ),
    },
    {
      title: t('column.updater'),
      dataIndex: 'updatedBy',
      sorter: true,
      sortOrder: sortBy === 'UpdatedBy' ? sortOrder : null,
      width: 100,
      render: (value: string | undefined) => (
        <span className="warehouse-locations-nowrap">{value || '--'}</span>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 132,
      fixed: 'right',
      render: (_, record) => (
        <Space size={4}>
          <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>
            {t('common.edit')}
          </Button>
          <Popconfirm
            title={t('warehouseLocations.confirmDelete')}
            description={t('warehouseLocations.deleteIrreversible')}
            okText={t('common.delete')}
            cancelText={t('common.cancel')}
            onConfirm={() => void handleDelete(record)}
          >
            <Button danger type="link" icon={<DeleteOutlined />}>
              {t('common.delete')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('warehouseLocations.title')}
      subtitle={t('warehouseLocations.subtitle')}
      extra={
        access.canManageWarehouse || canSyncLocationsFromHq ? (
          <Space>
            {canSyncLocationsFromHq ? (
              <Button
                icon={<CloudSyncOutlined />}
                loading={syncingFromHq}
                disabled={syncingFromHq || loading}
                onClick={handleSyncFromHq}
              >
                {t('warehouseLocations.syncFromHq')}
              </Button>
            ) : null}
            {access.canManageWarehouse ? (
              <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
                {t('warehouseLocations.newLocation')}
              </Button>
            ) : null}
          </Space>
        ) : null
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('warehouseLocations.searchLocationCode')}
            value={locationCodeKeyword}
            onChange={(event) => setLocationCodeKeyword(event.target.value)}
            style={{ width: 180 }}
            allowClear
          />
          <Input
            placeholder={t('warehouseLocations.searchLocationBarcode')}
            value={locationBarcodeKeyword}
            onChange={(event) => setLocationBarcodeKeyword(event.target.value)}
            style={{ width: 180 }}
            allowClear
          />
          <Input
            placeholder={t('warehouseLocations.searchUpdatedBy')}
            value={updatedByKeyword}
            onChange={(event) => setUpdatedByKeyword(event.target.value)}
            style={{ width: 160 }}
            allowClear
          />
          <Space>
            <Typography.Text>{t('warehouseLocations.locationTypeFilter')}</Typography.Text>
            <Select
              value={locationTypeFilter}
              onChange={setLocationTypeFilter}
              options={locationTypeOptions}
              placeholder={t('warehouseLocations.allTypes')}
              allowClear
              style={{ width: 160 }}
            />
          </Space>
          <Space>
            <Typography.Text>{t('warehouseLocations.usageFilter')}</Typography.Text>
            <Select
              value={usageFilter}
              onChange={setUsageFilter}
              options={usageOptions}
              placeholder={t('warehouseLocations.allUsageStatus')}
              allowClear
              style={{ width: 160 }}
            />
          </Space>
          <Button type="primary" icon={<SearchOutlined />} onClick={() => void loadData(1, pageSize)}>
            {t('common.query')}
          </Button>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => {
              setLocationCodeKeyword('')
              setLocationBarcodeKeyword('')
              setUpdatedByKeyword('')
              setLocationTypeFilter(undefined)
              setUsageFilter(undefined)
              setSortBy(DEFAULT_LOCATION_SORT_BY)
              setSortOrder(DEFAULT_LOCATION_SORT_ORDER)
              void loadData(
                1,
                pageSize,
                undefined,
                undefined,
                '',
                '',
                '',
                DEFAULT_LOCATION_SORT_BY,
                DEFAULT_LOCATION_SORT_ORDER,
              )
            }}
          >
            {t('common.reset')}
          </Button>
        </Space>

        <Table
          rowKey="locationGuid"
          className="warehouse-locations-compact-table"
          virtual
          loading={loading}
          columns={columns}
          dataSource={data}
          size="small"
          scroll={{ x: 1480, y: 600 }}
          onChange={handleTableChange}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
          }}
        />
      </Card>

      <Modal
        title={editingItem ? t('warehouseLocations.editTitle') : t('warehouseLocations.createTitle')}
        open={modalOpen}
        onOk={() => void handleSave()}
        onCancel={handleCloseModal}
        confirmLoading={saving}
        destroyOnClose
        okText={t('common.save')}
        cancelText={t('common.cancel')}
      >
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="locationCode"
            label={t('column.locationCode')}
            rules={[{ required: true, message: t('warehouseLocations.enterLocationCode') }]}
          >
            <Input placeholder={t('warehouseLocations.enterLocationCode')} />
          </Form.Item>
          <Form.Item name="locationBarcode" label={t('column.locationBarcode')}>
            <Input placeholder={t('warehouseLocations.enterLocationBarcode')} />
          </Form.Item>
          <Form.Item name="locationType" label={t('column.locationType')}>
            <Select placeholder={t('warehouseLocations.selectLocationType')} options={locationTypeOptions} />
          </Form.Item>
          <Form.Item name="status" label={t('column.locationStatus')}>
            <Select placeholder={t('warehouseLocations.selectLocationStatus')} options={statusOptions} />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
