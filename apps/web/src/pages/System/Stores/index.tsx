import { EditOutlined, EyeOutlined, PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  Modal,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { FilterValue, SorterResult } from 'antd/es/table/interface'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { createStore, getNextStoreCode, getStoreByGuid, getStores, updateStore } from '../../../services/storeService'
import type { CreateStoreDto, StoreDetailDto, StoreDto, UpdateStoreDto } from '../../../types/store'
import { RequestError } from '../../../utils/request'
import StoreUserManagement from './StoreUserManagement'

const brandTagPalette = [
  { background: '#e6f4ff', borderColor: '#91caff', color: '#0958d9' },
  { background: '#f6ffed', borderColor: '#b7eb8f', color: '#389e0d' },
  { background: '#fff7e6', borderColor: '#ffd591', color: '#d46b08' },
  { background: '#f9f0ff', borderColor: '#d3adf7', color: '#722ed1' },
  { background: '#e6fffb', borderColor: '#87e8de', color: '#08979c' },
  { background: '#fff1f0', borderColor: '#ffa39e', color: '#cf1322' },
]

const brandTagStyleByName: Record<string, (typeof brandTagPalette)[number]> = {
  'hot bargain': brandTagPalette[0],
  'discount general': brandTagPalette[1],
  'dollar king': brandTagPalette[5],
}

function getBrandTagStyle(brandName: string) {
  // 常见品牌固定配色，避免列表里不同品牌因为 hash 碰撞显示成同色。
  const normalizedName = brandName.trim().toLowerCase()
  const knownStyle = brandTagStyleByName[normalizedName]
  if (knownStyle) {
    return knownStyle
  }

  // 未知品牌仍按名称稳定取色，分页和刷新后不会跳色。
  const hash = Array.from(normalizedName).reduce((sum, char) => (sum * 31 + char.charCodeAt(0)) >>> 0, 0)
  return brandTagPalette[hash % brandTagPalette.length]
}

function renderBrandName(value?: string) {
  const brandName = value?.trim()
  if (!brandName) {
    return <Typography.Text type="secondary">--</Typography.Text>
  }

  return (
    <Tag bordered={false} style={getBrandTagStyle(brandName)}>
      {brandName}
    </Tag>
  )
}

type StoreSortOrder = 'ascend' | 'descend' | null

function getApiErrorCode(error: unknown) {
  if (!(error instanceof RequestError)) {
    return undefined
  }

  const payload = error.payload
  return typeof payload === 'object' && payload !== null && 'errorCode' in payload
    ? String(payload.errorCode)
    : undefined
}

export default function SystemStoresPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<StoreDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [brandFilter, setBrandFilter] = useState<string | undefined>()
  const [isActiveFilter, setIsActiveFilter] = useState<boolean | undefined>()
  const [sortBy, setSortBy] = useState<string | undefined>()
  const [sortOrder, setSortOrder] = useState<StoreSortOrder>(null)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailStore, setDetailStore] = useState<StoreDetailDto | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingStore, setEditingStore] = useState<StoreDetailDto | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [createSaving, setCreateSaving] = useState(false)
  const [storeCodeLoading, setStoreCodeLoading] = useState(false)
  const [storeUserOpen, setStoreUserOpen] = useState(false)
  const [storeUserTarget, setStoreUserTarget] = useState<StoreDto | null>(null)
  const [form] = Form.useForm<UpdateStoreDto>()
  const [createForm] = Form.useForm<CreateStoreDto>()

  const loadData = async (
    nextPage = page,
    nextPageSize = pageSize,
    nextBrandFilter = brandFilter,
    nextIsActiveFilter = isActiveFilter,
    nextSortBy = sortBy,
    nextSortOrder = sortOrder,
  ) => {
    setLoading(true)
    try {
      const result = await getStores({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
        brandName: nextBrandFilter || undefined,
        isActive: nextIsActiveFilter,
        sortField: nextSortBy,
        sortOrder: nextSortOrder === 'ascend' ? 'asc' : nextSortOrder === 'descend' ? 'desc' : undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setBrandFilter(nextBrandFilter)
      setIsActiveFilter(nextIsActiveFilter)
      setSortBy(nextSortBy)
      setSortOrder(nextSortOrder ?? null)
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const reloadStoreDetail = async (storeGuid: string) => {
    const detail = await getStoreByGuid(storeGuid)
    setDetailStore(detail)
    return detail
  }

  const loadNextStoreCode = async () => {
    setStoreCodeLoading(true)
    try {
      const nextCode = await getNextStoreCode()
      createForm.setFieldValue('storeCode', nextCode)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('system.stores.loadNextStoreCodeFailed'))
    } finally {
      setStoreCodeLoading(false)
    }
  }

  const handleOpenCreate = () => {
    createForm.resetFields()
    // 新建分店默认不启用收银系统，避免刚录入资料就进入 POS 可用范围。
    createForm.setFieldsValue({ isActive: false })
    setCreateOpen(true)
    void loadNextStoreCode()
  }

  const handleCreateSubmit = async () => {
    try {
      const values = await createForm.validateFields()
      setCreateSaving(true)
      const created = await createStore({ ...values, isActive: values.isActive ?? false })
      message.success(t('system.stores.createSuccess'))
      setCreateOpen(false)
      createForm.resetFields()
      setDetailStore(created)
      setDetailOpen(true)
      void loadData(1, pageSize, brandFilter, isActiveFilter, sortBy, sortOrder)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }

      console.error(error)
      message.error(
        getApiErrorCode(error) === 'DUPLICATE_STORE_CODE'
          ? t('system.stores.duplicateStoreCode')
          : error instanceof Error ? error.message : t('system.stores.createFailed'),
      )
    } finally {
      setCreateSaving(false)
    }
  }

  const handleViewDetail = async (record: StoreDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailStore(null)
    try {
      const detail = await getStoreByGuid(record.storeGUID)
      setDetailStore(detail)
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadDetailFailed'))
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const handleEdit = async (record: StoreDto) => {
    setEditOpen(true)
    setEditLoading(true)
    setEditingStore(null)
    form.resetFields()
    try {
      const detail = await getStoreByGuid(record.storeGUID)
      setEditingStore(detail)
      form.setFieldsValue({
        storeName: detail.storeName,
        storeCode: detail.storeCode,
        description: detail.description,
        address: detail.address,
        contactPhone: detail.contactPhone,
        contactEmail: detail.contactEmail,
        abn: detail.abn,
        brandName: detail.brandName,
        isActive: detail.isActive,
      })
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadEditFailed'))
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingStore) {
      return
    }

    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateStore(editingStore.storeGUID, values)
      message.success(t('system.stores.updateSuccess'))
      setEditOpen(false)
      setEditingStore(updated)
      form.resetFields()
      if (detailStore?.storeGUID === updated.storeGUID) {
        setDetailStore((current) => (current ? { ...current, ...updated } : updated))
      }
      void loadData(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(t('system.stores.updateFailed'))
    } finally {
      setEditLoading(false)
    }
  }

  const handleOpenStoreUsers = (store: StoreDto) => {
    setStoreUserTarget(store)
    setStoreUserOpen(true)
  }

  const brandFilterOptions = useMemo(() => {
    const brands = new Set<string>()
    data.forEach((store) => {
      const brandName = store.brandName?.trim()
      if (brandName) {
        brands.add(brandName)
      }
    })
    if (brandFilter) {
      brands.add(brandFilter)
    }

    return Array.from(brands)
      .sort((a, b) => a.localeCompare(b))
      .map((brandName) => ({ text: brandName, value: brandName }))
  }, [brandFilter, data])

  const handleTableChange = (
    pagination: TablePaginationConfig,
    filters: Record<string, FilterValue | null>,
    sorter: SorterResult<StoreDto> | SorterResult<StoreDto>[],
    extra: { action: 'paginate' | 'sort' | 'filter' },
  ) => {
    const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const rawField = currentSorter?.field || currentSorter?.column?.dataIndex
    const field = Array.isArray(rawField) ? rawField.join('.') : rawField ? String(rawField) : undefined
    const order = currentSorter?.order as StoreSortOrder | undefined
    const nextSortBy = field && order ? field : undefined
    const nextSortOrder = field && order ? order : null

    const nextBrandValue = filters.brandName?.[0]
    const nextBrandFilter = typeof nextBrandValue === 'string' ? nextBrandValue : undefined
    const nextIsActiveValue = filters.isActive?.[0]
    const nextIsActiveFilter =
      nextIsActiveValue === 'true' ? true : nextIsActiveValue === 'false' ? false : undefined

    // 表格筛选和排序都走服务端查询，避免分页后只在当前页内处理数据。
    const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1
    void loadData(
      nextPage,
      pagination.pageSize ?? pageSize,
      nextBrandFilter,
      nextIsActiveFilter,
      nextSortBy,
      nextSortOrder,
    )
  }

  const columns: ColumnsType<StoreDto> = [
    {
      title: t('common.index'),
      key: 'rowIndex',
      width: 64,
      render: (_value, _record, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('system.stores.storeName'),
      dataIndex: 'storeName',
      width: 190,
      sorter: true,
      sortOrder: sortBy === 'storeName' ? sortOrder : null,
    },
    {
      title: t('system.stores.storeCode'),
      dataIndex: 'storeCode',
      width: 96,
      sorter: true,
      sortOrder: sortBy === 'storeCode' ? sortOrder : null,
    },
    {
      title: t('system.stores.brandName'),
      dataIndex: 'brandName',
      width: 140,
      filters: brandFilterOptions,
      filteredValue: brandFilter ? [brandFilter] : null,
      sorter: true,
      sortOrder: sortBy === 'brandName' ? sortOrder : null,
      render: renderBrandName,
    },
    {
      title: t('system.stores.contactPhone'),
      dataIndex: 'contactPhone',
      width: 130,
      sorter: true,
      sortOrder: sortBy === 'contactPhone' ? sortOrder : null,
      render: (value) => value || '--',
    },
    {
      title: t('system.stores.address'),
      dataIndex: 'address',
      width: 300,
      sorter: true,
      sortOrder: sortBy === 'address' ? sortOrder : null,
      // 地址列按业务要求完整展示；控制列宽并允许换行，避免撑开整张表。
      render: (value?: string) => value ? (
        <Typography.Text style={{ whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {value}
        </Typography.Text>
      ) : '--',
    },
    {
      title: t('system.stores.linkedUserCount'),
      dataIndex: 'totalUsers',
      width: 92,
      sorter: true,
      sortOrder: sortBy === 'totalUsers' ? sortOrder : null,
      render: (value: number | undefined, record) => (
        <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(record)}>
          {value ?? 0}
        </Button>
      ),
    },
    {
      title: t('system.stores.cashRegisterEnabled'),
      dataIndex: 'isActive',
      width: 112,
      filters: [
        { text: t('common.active'), value: 'true' },
        { text: t('common.inactive'), value: 'false' },
      ],
      filteredValue: isActiveFilter === undefined ? null : [String(isActiveFilter)],
      sorter: true,
      sortOrder: sortBy === 'isActive' ? sortOrder : null,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active') : t('common.inactive')}</Tag>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 132,
      render: (_, record) => (
        <Space size={4}>
          <Button size="small" type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view')}
          </Button>
          <HasPermission code={P.Stores.Edit}>
            <Button size="small" type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              {t('common.edit')}
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('system.stores.pageTitle')}
      subtitle={t('system.stores.pageSubtitle')}
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('system.stores.searchPlaceholder')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 260 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize, brandFilter, isActiveFilter, sortBy, sortOrder)}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize, brandFilter, isActiveFilter, sortBy, sortOrder)}>
            {t('common.refresh')}
          </Button>
          <HasPermission code={P.Stores.Create}>
            <Button type="primary" icon={<PlusOutlined />} onClick={handleOpenCreate}>
              {t('system.stores.createStore')}
            </Button>
          </HasPermission>
        </Space>

        <Table
          rowKey="storeGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          size="small"
          tableLayout="fixed"
          scroll={{ x: 1260 }}
          onChange={handleTableChange}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
          }}
        />
      </Card>

      <Drawer
        title={detailStore ? t('system.stores.detailTitle', { name: detailStore.storeCode }) : t('system.stores.detailTitleShort')}
        width={860}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailStore(null)
        }}
        destroyOnHidden
        extra={
          detailStore ? (
            <HasPermission code={P.Stores.Edit}>
              <Button type="primary" onClick={() => handleOpenStoreUsers(detailStore)}>
                {t('system.stores.manageUsers')}
              </Button>
            </HasPermission>
          ) : null
        }
      >
        {detailLoading ? (
          <Typography.Text type="secondary">{t('system.stores.loadingDetail')}</Typography.Text>
        ) : !detailStore ? (
          <Typography.Text type="danger">{t('system.stores.notFound')}</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions
              bordered
              column={2}
              size="small"
              labelStyle={{ width: 96, whiteSpace: 'nowrap' }}
            >
              <Descriptions.Item label={t('system.stores.storeName')}>{detailStore.storeName}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.storeCode')}>{detailStore.storeCode}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.brandName')}>{detailStore.brandName || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.contactPhone')}>{detailStore.contactPhone || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.contactEmail')}>{detailStore.contactEmail || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.cashRegisterEnabled')}>
                <Tag color={detailStore.isActive ? 'success' : 'default'}>
                  {detailStore.isActive ? t('common.active') : t('common.inactive')}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.stores.userCount')}>
                <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(detailStore)}>
                  {detailStore.activeUsers ?? 0} / {detailStore.totalUsers ?? 0}
                </Button>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.stores.address')} span={2}>
                {detailStore.address || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.description')} span={2}>
                {detailStore.description || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.createTime')}>{detailStore.createdAt}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.updatedAt')}>{detailStore.updatedAt}</Descriptions.Item>
            </Descriptions>
          </Space>
        )}
      </Drawer>

      <Modal
        title={t('system.stores.createTitle')}
        open={createOpen}
        onCancel={() => {
          setCreateOpen(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreateSubmit()}
        confirmLoading={createSaving}
        width={720}
        destroyOnHidden
      >
        <Form form={createForm} layout="vertical" initialValues={{ isActive: false }} autoComplete="off">
          {/* 新建与编辑使用同一套前端校验，尽量在提交前拦住后端必填和长度错误。 */}
          <Form.Item
            label={t('system.stores.storeName')}
            name="storeName"
            rules={[
              { required: true, message: t('system.stores.storeNameRequired') },
              { max: 100, message: t('system.stores.storeNameMaxLength') },
            ]}
          >
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item
            label={t('system.stores.storeCode')}
            name="storeCode"
            rules={[
              { required: true, message: t('system.stores.storeCodeRequired') },
              { max: 20, message: t('system.stores.storeCodeMaxLength') },
            ]}
          >
            <Input
              autoComplete="off"
              addonAfter={(
                <Button
                  type="link"
                  size="small"
                  icon={<ReloadOutlined />}
                  loading={storeCodeLoading}
                  onClick={() => void loadNextStoreCode()}
                >
                  {t('system.stores.regenerateStoreCode')}
                </Button>
              )}
            />
          </Form.Item>
          <Form.Item label={t('system.stores.brandName')} name="brandName" rules={[{ max: 100, message: t('system.stores.brandNameMaxLength') }]}>
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item label={t('system.stores.contactPhone')} name="contactPhone" rules={[{ max: 20, message: t('system.stores.contactPhoneMaxLength') }]}>
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item label={t('system.stores.contactEmail')} name="contactEmail" rules={[{ type: 'email', message: t('system.users.emailInvalid') }]}>
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item label={t('system.stores.address')} name="address" rules={[{ max: 200, message: t('system.stores.addressMaxLength') }]}>
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item label={t('column.description')} name="description" rules={[{ max: 500, message: t('system.stores.descriptionMaxLength') }]}>
            <Input.TextArea rows={4} autoComplete="off" />
          </Form.Item>
          <Form.Item label={t('system.stores.cashRegisterEnabled')} name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingStore ? t('system.stores.editTitle', { name: editingStore.storeCode }) : t('system.stores.editTitleShort')}
        open={editOpen}
        onCancel={() => {
          setEditOpen(false)
          setEditingStore(null)
          form.resetFields()
        }}
        onOk={() => void handleEditSubmit()}
        confirmLoading={editLoading}
        width={720}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          {/* 前端长度限制与后端 UpdateStoreDto 保持一致，避免提交后才收到 400。 */}
          <Form.Item
            label={t('system.stores.storeName')}
            name="storeName"
            rules={[
              { required: true, message: t('system.stores.storeNameRequired') },
              { max: 100, message: t('system.stores.storeNameMaxLength') },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            label={t('system.stores.storeCode')}
            name="storeCode"
            rules={[
              { required: true, message: t('system.stores.storeCodeRequired') },
              { max: 20, message: t('system.stores.storeCodeMaxLength') },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.brandName')} name="brandName" rules={[{ max: 100, message: t('system.stores.brandNameMaxLength') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.contactPhone')} name="contactPhone" rules={[{ max: 20, message: t('system.stores.contactPhoneMaxLength') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.contactEmail')} name="contactEmail" rules={[{ type: 'email', message: t('system.users.emailInvalid') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.address')} name="address" rules={[{ max: 200, message: t('system.stores.addressMaxLength') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('column.description')} name="description" rules={[{ max: 500, message: t('system.stores.descriptionMaxLength') }]}>
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item label={t('system.stores.cashRegisterEnabled')} name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
          </Form.Item>
        </Form>
      </Modal>

      <StoreUserManagement
        open={storeUserOpen}
        store={storeUserTarget}
        onClose={() => {
          setStoreUserOpen(false)
          setStoreUserTarget(null)
        }}
        onChanged={() => {
          if (storeUserTarget) {
            if (detailStore?.storeGUID === storeUserTarget.storeGUID) {
              void reloadStoreDetail(storeUserTarget.storeGUID)
            }
            void loadData(page, pageSize)
          }
        }}
      />
    </PageContainer>
  )
}
