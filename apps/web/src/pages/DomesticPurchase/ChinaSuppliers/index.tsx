import {
  CheckCircleOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  StopOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult, TableCurrentDataSource } from 'antd/es/table/interface'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  checkSupplierCodeExists,
  createChinaSupplier,
  deleteChinaSupplier,
  generateNextSupplierCode,
  getChinaSuppliers,
  syncChinaSuppliersToHbSales,
  toggleChinaSupplierStatus,
  updateChinaSupplier,
} from '../../../services/chinaSupplierService'
import type {
  ChinaSupplierItem,
  ChinaSupplierListParams,
  SaveChinaSupplierPayload,
} from '../../../types/chinaSupplier'
import { copyTextToClipboard } from '../../../utils/clipboard'
import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../../utils/latestRequestGuard'

type SupplierFormValues = SaveChinaSupplierPayload
type DesiredChinaSupplierQuery = ChinaSupplierListParams & {
  page: number
  pageSize: number
  sortField: string
  sortDirection: 'asc' | 'desc'
}

const getStatusOptions = (t: ReturnType<typeof useTranslation>['t']) => [
  { label: t('chinaSuppliers.allStatus', '全部状态'), value: 'all' },
  { label: t('common.enable', '启用'), value: 1 },
  { label: t('chinaSuppliers.disableStatus', '禁用'), value: 0 },
]

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

export default function DomesticChinaSuppliersPage() {
  const { t } = useTranslation()
  const statusOptions = getStatusOptions(t)
  const [form] = Form.useForm<SupplierFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<ChinaSupplierItem | null>(null)
  const [data, setData] = useState<ChinaSupplierItem[]>([])
  const [keyword, setKeyword] = useState('')
  const [status, setStatus] = useState<number | 'all'>('all')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [sortField, setSortField] = useState('fgC_CreateDate')
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc')
  const mainListRequestGuardRef = useRef(createLatestRequestGuard())
  const mountedRef = useRef(false)
  const desiredListQueryRef = useRef<DesiredChinaSupplierQuery>({
    page,
    pageSize,
    search: keyword || undefined,
    status: status === 'all' ? undefined : status,
    sortField,
    sortDirection,
  })

  const loadData = async (overrides: Partial<DesiredChinaSupplierQuery> = {}) => {
    if (!mountedRef.current) {
      return
    }

    const query: DesiredChinaSupplierQuery = {
      page,
      pageSize,
      search: keyword || undefined,
      status: status === 'all' ? undefined : status,
      sortField,
      sortDirection,
      ...overrides,
    }
    // 请求 begin 前同步目标分页和排序，避免 mutation 用最后成功页覆盖在途查询。
    desiredListQueryRef.current = query

    await runLatestGuardedRequest(mainListRequestGuardRef.current, () => getChinaSuppliers(query), {
      onStart: () => setLoading(true),
      onSuccess: (result) => {
        setData(result.items)
        setTotal(result.total)
        setPage(result.page)
        setPageSize(result.pageSize)
        setSortField(query.sortField)
        setSortDirection(query.sortDirection)
      },
      onError: (error) => {
        console.error(error)
        message.error(error instanceof Error ? error.message : t('chinaSuppliers.loadFailed', '加载国内供应商列表失败'))
      },
      // 旧请求结束时不能关闭较新请求的 loading。
      onSettled: () => setLoading(false),
    })
  }

  const latestLoadDataRef = useRef(loadData)

  useLayoutEffect(() => {
    latestLoadDataRef.current = loadData
  })

  const refreshDesiredList = (overrides: Partial<DesiredChinaSupplierQuery> = {}) =>
    latestLoadDataRef.current({ ...desiredListQueryRef.current, ...overrides })

  useLayoutEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
      mainListRequestGuardRef.current.invalidate()
    }
  }, [])

  useEffect(() => {
    void loadData({ page: 1, pageSize })
  }, [])

  const handleCreate = async () => {
    setEditingItem(null)
    form.resetFields()
    form.setFieldsValue({ status: 1 })
    setModalOpen(true)

    try {
      const supplierCode = await generateNextSupplierCode()
      form.setFieldValue('supplierCode', supplierCode)
    } catch (error) {
      console.error(error)
    }
  }

  const handleEdit = (record: ChinaSupplierItem) => {
    setEditingItem(record)
    form.setFieldsValue({
      supplierCode: record.supplierCode,
      supplierName: record.supplierName,
      shopNumber: record.shopNumber,
      contactPerson: record.contactPerson,
      phone: record.phone,
      email: record.email,
      storefrontPhoto: record.storefrontPhoto,
      status: record.status,
      remarks: record.remarks,
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
        await updateChinaSupplier(editingItem.guid, values)
        message.success(t('chinaSuppliers.updateSuccess', '更新国内供应商成功'))
      } else {
        await createChinaSupplier(values)
        message.success(t('chinaSuppliers.createSuccess', '创建国内供应商成功'))
      }

      handleCloseModal()
      setSelectedRowKeys([])
      if (editingItem) {
        void refreshDesiredList()
      } else {
        void refreshDesiredList({ page: 1 })
      }
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('chinaSuppliers.saveFailed', '保存国内供应商失败'))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (record: ChinaSupplierItem) => {
    try {
      await deleteChinaSupplier(record.guid)
      message.success(t('chinaSuppliers.deleteSuccess', '删除国内供应商成功'))
      setSelectedRowKeys((current) => current.filter((item) => item !== record.guid))
      void refreshDesiredList()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('chinaSuppliers.deleteFailed', '删除国内供应商失败'))
    }
  }

  const handleToggleStatus = async (record: ChinaSupplierItem) => {
    const nextStatus = record.status === 1 ? 0 : 1
    try {
      await toggleChinaSupplierStatus(record.guid, nextStatus)
      message.success(nextStatus === 1 ? t('chinaSuppliers.enabled', '已启用供应商') : t('chinaSuppliers.disabled', '已禁用供应商'))
      void refreshDesiredList()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('chinaSuppliers.toggleStatusFailed', '切换供应商状态失败'))
    }
  }

  const handleSync = () => {
    if (!selectedRowKeys.length) {
      message.warning(t('chinaSuppliers.selectSyncFirst', '请先选择要同步的供应商'))
      return
    }

    Modal.confirm({
      title: t('chinaSuppliers.syncToSales', '同步到销售库'),
      content: t('chinaSuppliers.syncConfirm', '确认同步选中的 {{count}} 个供应商到 HBSales？', { count: selectedRowKeys.length }),
      okText: t('chinaSuppliers.confirmSync', '确认同步'),
      cancelText: t('common.cancel', '取消'),
      onOk: async () => {
        try {
          setSyncing(true)
          const result = await syncChinaSuppliersToHbSales(selectedRowKeys.map(String))
          message.success(
            t('chinaSuppliers.syncResult', '同步完成：共 {{total}} 条，新增 {{inserted}} 条，更新 {{updated}} 条，失败 {{failed}} 条', { total: result.totalProcessed, inserted: result.insertedCount, updated: result.updatedCount, failed: result.failCount }),
          )
          setSelectedRowKeys([])
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : t('chinaSuppliers.syncFailed', '同步国内供应商失败'))
        } finally {
          setSyncing(false)
        }
      },
    })
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    _filters: Record<string, unknown>,
    sorter: SorterResult<ChinaSupplierItem> | SorterResult<ChinaSupplierItem>[],
    extra: TableCurrentDataSource<ChinaSupplierItem>,
  ) => {
    const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1
    const nextPageSize = pagination.pageSize ?? pageSize

    if (!Array.isArray(sorter) && sorter.field) {
      const nextSortDirection = sorter.order === 'ascend' ? 'asc' : 'desc'
      void loadData({
        page: nextPage,
        pageSize: nextPageSize,
        sortField: String(sorter.field),
        sortDirection: nextSortDirection,
      })
      return
    }

    void loadData({ page: nextPage, pageSize: nextPageSize })
  }

  const columns: ColumnsType<ChinaSupplierItem> = [
    {
      title: t('column.index', '序号'),
      key: 'rowNumber',
      width: 80,
      render: (_value, _record, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('chinaSuppliers.supplierCode', '供应商编码'),
      dataIndex: 'supplierCode',
      width: 140,
      sorter: true,
      render: (value: string) => (
        <Space size={4}>
          <span>{value}</span>
          <Tooltip title={t('common.copy', '复制')}>
            <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => void copyTextToClipboard(value)} />
          </Tooltip>
        </Space>
      ),
    },
    {
      title: t('chinaSuppliers.supplierName', '供应商名称'),
      dataIndex: 'supplierName',
      width: 220,
      ellipsis: true,
    },
    {
      title: t('chinaSuppliers.shopNumber', '店铺号'),
      dataIndex: 'shopNumber',
      width: 140,
      render: (value?: string) => value || '--',
    },
    {
      title: t('chinaSuppliers.contactPerson', '联系人'),
      dataIndex: 'contactPerson',
      width: 120,
      render: (value?: string) => value || '--',
    },
    {
      title: t('chinaSuppliers.contactPhone', '联系电话'),
      dataIndex: 'phone',
      width: 150,
      render: (value?: string) => value || '--',
    },
    {
      title: t('chinaSuppliers.email', '邮箱'),
      dataIndex: 'email',
      width: 220,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: t('domesticProducts.status', '状态'),
      dataIndex: 'status',
      width: 100,
      render: (value: number) => (
        <Tag color={value === 1 ? 'success' : 'default'}>
          {value === 1 ? t('common.enable', '启用') : t('chinaSuppliers.disableStatus', '禁用')}
        </Tag>
      ),
    },
    {
      title: t('domesticProducts.updatedAt', '更新时间'),
      dataIndex: 'updatedAt',
      width: 180,
      sorter: true,
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: t('chinaSuppliers.createdAt', '创建时间'),
      dataIndex: 'createdAt',
      width: 180,
      sorter: true,
      defaultSortOrder: 'descend',
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: t('domesticProducts.remarks', '备注'),
      dataIndex: 'remarks',
      width: 220,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: t('common.action', '操作'),
      key: 'action',
      width: 170,
      fixed: 'right',
      render: (_, record) => (
        <Space size={0}>
          <Tooltip title={t('common.edit', '编辑')}>
            <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>
              {t('common.edit', '编辑')}
            </Button>
          </Tooltip>
          <Popconfirm
            title={t('chinaSuppliers.confirmToggle', '确认{{action}}该供应商？', { action: record.status === 1 ? t('chinaSuppliers.disableStatus', '禁用') : t('common.enable', '启用') })}
            onConfirm={() => void handleToggleStatus(record)}
          >
            <Button
              type="link"
              danger={record.status === 1}
              icon={record.status === 1 ? <StopOutlined /> : <CheckCircleOutlined />}
            >
              {record.status === 1 ? t('chinaSuppliers.disableStatus', '禁用') : t('common.enable', '启用')}
            </Button>
          </Popconfirm>
          <Popconfirm
            title={t('chinaSuppliers.confirmDelete', '确认删除该供应商？')}
            description={t('chinaSuppliers.deleteWarning', '删除后不可恢复。')}
            onConfirm={() => void handleDelete(record)}
          >
            <Button type="link" danger icon={<DeleteOutlined />}>
              {t('common.delete', '删除')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('chinaSuppliers.pageTitle', '国内供应商')}
      subtitle={t('chinaSuppliers.pageSubtitle', '首批迁移国内采购模块，先落供应商管理，后续页面沿用同一路由与服务适配模式。')}
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={() => void handleCreate()}>
          {t('chinaSuppliers.newSupplier', '新建供应商')}
        </Button>
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('chinaSuppliers.searchPlaceholder', '搜索供应商编码 / 名称 / 店铺号')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            allowClear
            style={{ width: 280 }}
          />
          <Select
            value={status}
            options={statusOptions}
            onChange={setStatus}
            style={{ width: 140 }}
          />
          <Button type="primary" onClick={() => void loadData({ page: 1, pageSize })}>
            {t('common.query', '查询')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void refreshDesiredList()}>
            {t('common.refresh', '刷新')}
          </Button>
          <Button
            icon={<CloudUploadOutlined />}
            loading={syncing}
            disabled={!selectedRowKeys.length}
            onClick={handleSync}
          >
            {t('chinaSuppliers.syncToSales', '同步到销售库')}
          </Button>
        </Space>

        <Table
          rowKey="guid"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 1700 }}
          rowSelection={{
            selectedRowKeys,
            onChange: setSelectedRowKeys,
            preserveSelectedRowKeys: true,
          }}
          onChange={handleTableChange}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
          }}
        />
      </Card>

      <Modal
        title={editingItem ? t('chinaSuppliers.editTitle', '编辑国内供应商') : t('chinaSuppliers.createTitle', '新建国内供应商')}
        open={modalOpen}
        confirmLoading={saving}
        onCancel={handleCloseModal}
        onOk={() => void handleSave()}
        destroyOnHidden
        width={680}
      >
        <Form form={form} layout="vertical">
          <Form.Item
            label={t('chinaSuppliers.supplierCode', '供应商编码')}
            name="supplierCode"
            rules={[
              { required: true, message: t('chinaSuppliers.enterSupplierCode', '请输入供应商编码') },
              { max: 50, message: t('chinaSuppliers.supplierCodeMaxLength', '供应商编码不能超过 50 个字符') },
              {
                validator: async (_, value) => {
                  if (!value) {
                    return
                  }
                  const exists = await checkSupplierCodeExists(value, editingItem?.guid)
                  if (exists) {
                    throw new Error(t('chinaSuppliers.supplierCodeExists', '供应商编码已存在'))
                  }
                },
              },
            ]}
          >
            <Input maxLength={50} disabled={Boolean(editingItem)} />
          </Form.Item>

          <Form.Item
            label={t('chinaSuppliers.supplierName', '供应商名称')}
            name="supplierName"
            rules={[
              { required: true, message: t('chinaSuppliers.enterSupplierName', '请输入供应商名称') },
              { max: 200, message: t('chinaSuppliers.supplierNameMaxLength', '供应商名称不能超过 200 个字符') },
            ]}
          >
            <Input maxLength={200} />
          </Form.Item>

          <Space style={{ display: 'flex' }} align="start">
            <Form.Item label={t('chinaSuppliers.shopNumber', '店铺号')} name="shopNumber" rules={[{ max: 50, message: t('chinaSuppliers.shopNumberMaxLength', '店铺号不能超过 50 个字符') }]}>
              <Input maxLength={50} style={{ width: 200 }} />
            </Form.Item>
            <Form.Item label={t('chinaSuppliers.contactPerson', '联系人')} name="contactPerson" rules={[{ max: 100, message: t('chinaSuppliers.contactPersonMaxLength', '联系人不能超过 100 个字符') }]}>
              <Input maxLength={100} style={{ width: 200 }} />
            </Form.Item>
            <Form.Item label={t('domesticProducts.status', '状态')} name="status" initialValue={1}>
              <Select
                style={{ width: 140 }}
                options={[
                  { label: t('common.enable', '启用'), value: 1 },
                  { label: t('chinaSuppliers.disableStatus', '禁用'), value: 0 },
                ]}
              />
            </Form.Item>
          </Space>

          <Space style={{ display: 'flex' }} align="start">
            <Form.Item
              label={t('chinaSuppliers.contactPhone', '联系电话')}
              name="phone"
              rules={[
                { max: 20, message: t('chinaSuppliers.phoneMaxLength', '联系电话不能超过 20 个字符') },
                { pattern: /^[\d\s\-+()]+$/, message: t('chinaSuppliers.invalidPhone', '请输入有效的电话号码') },
              ]}
            >
              <Input maxLength={20} style={{ width: 260 }} />
            </Form.Item>
            <Form.Item
              label={t('chinaSuppliers.contactEmail', '联系邮箱')}
              name="email"
              rules={[
                { type: 'email', message: t('chinaSuppliers.invalidEmail', '请输入有效的邮箱地址') },
                { max: 100, message: t('chinaSuppliers.emailMaxLength', '联系邮箱不能超过 100 个字符') },
              ]}
            >
              <Input maxLength={100} style={{ width: 260 }} />
            </Form.Item>
          </Space>

          <Form.Item label={t('chinaSuppliers.storefrontPhoto', '店面照片地址')} name="storefrontPhoto">
            <Input />
          </Form.Item>

          <Form.Item label={t('domesticProducts.remarks', '备注')} name="remarks" rules={[{ max: 1000, message: t('chinaSuppliers.remarksMaxLength', '备注不能超过 1000 个字符') }]}>
            <Input.TextArea rows={4} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
