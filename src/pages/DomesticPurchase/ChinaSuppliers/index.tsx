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
import type { SorterResult } from 'antd/es/table/interface'
import { useEffect, useState } from 'react'
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
  SaveChinaSupplierPayload,
} from '../../../types/chinaSupplier'
import { copyTextToClipboard } from '../../../utils/clipboard'

type SupplierFormValues = SaveChinaSupplierPayload

const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '启用', value: 1 },
  { label: '禁用', value: 0 },
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

  const loadData = async (
    nextPage = page,
    nextPageSize = pageSize,
    nextSortField = sortField,
    nextSortDirection = sortDirection,
  ) => {
    setLoading(true)
    try {
      const result = await getChinaSuppliers({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
        status: status === 'all' ? undefined : status,
        sortField: nextSortField,
        sortDirection: nextSortDirection,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setSortField(nextSortField)
      setSortDirection(nextSortDirection)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载国内供应商列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
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
        message.success('更新国内供应商成功')
      } else {
        await createChinaSupplier(values)
        message.success('创建国内供应商成功')
      }

      handleCloseModal()
      setSelectedRowKeys([])
      void loadData(editingItem ? page : 1, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : '保存国内供应商失败')
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (record: ChinaSupplierItem) => {
    try {
      await deleteChinaSupplier(record.guid)
      message.success('删除国内供应商成功')
      setSelectedRowKeys((current) => current.filter((item) => item !== record.guid))
      void loadData(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '删除国内供应商失败')
    }
  }

  const handleToggleStatus = async (record: ChinaSupplierItem) => {
    const nextStatus = record.status === 1 ? 0 : 1
    try {
      await toggleChinaSupplierStatus(record.guid, nextStatus)
      message.success(nextStatus === 1 ? '已启用供应商' : '已禁用供应商')
      void loadData(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '切换供应商状态失败')
    }
  }

  const handleSync = () => {
    if (!selectedRowKeys.length) {
      message.warning('请先选择要同步的供应商')
      return
    }

    Modal.confirm({
      title: '同步到销售库',
      content: `确认同步选中的 ${selectedRowKeys.length} 个供应商到 HBSales？`,
      okText: '确认同步',
      cancelText: '取消',
      onOk: async () => {
        try {
          setSyncing(true)
          const result = await syncChinaSuppliersToHbSales(selectedRowKeys.map(String))
          message.success(
            `同步完成：共 ${result.totalProcessed} 条，新增 ${result.insertedCount} 条，更新 ${result.updatedCount} 条，失败 ${result.failCount} 条`,
          )
          setSelectedRowKeys([])
        } catch (error) {
          console.error(error)
          message.error(error instanceof Error ? error.message : '同步国内供应商失败')
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
  ) => {
    const nextPage = pagination.current ?? 1
    const nextPageSize = pagination.pageSize ?? pageSize

    if (!Array.isArray(sorter) && sorter.field) {
      const nextSortDirection = sorter.order === 'ascend' ? 'asc' : 'desc'
      void loadData(nextPage, nextPageSize, String(sorter.field), nextSortDirection)
      return
    }

    void loadData(nextPage, nextPageSize)
  }

  const columns: ColumnsType<ChinaSupplierItem> = [
    {
      title: '序号',
      key: 'rowNumber',
      width: 80,
      render: (_value, _record, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: '供应商编码',
      dataIndex: 'supplierCode',
      width: 140,
      sorter: true,
      render: (value: string) => (
        <Space size={4}>
          <span>{value}</span>
          <Tooltip title="复制">
            <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => void copyTextToClipboard(value)} />
          </Tooltip>
        </Space>
      ),
    },
    {
      title: '供应商名称',
      dataIndex: 'supplierName',
      width: 220,
      ellipsis: true,
    },
    {
      title: '店铺号',
      dataIndex: 'shopNumber',
      width: 140,
      render: (value?: string) => value || '--',
    },
    {
      title: '联系人',
      dataIndex: 'contactPerson',
      width: 120,
      render: (value?: string) => value || '--',
    },
    {
      title: '联系电话',
      dataIndex: 'phone',
      width: 150,
      render: (value?: string) => value || '--',
    },
    {
      title: '邮箱',
      dataIndex: 'email',
      width: 220,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 100,
      render: (value: number) => (
        <Tag color={value === 1 ? 'success' : 'default'}>
          {value === 1 ? '启用' : '禁用'}
        </Tag>
      ),
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      width: 180,
      sorter: true,
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: '创建时间',
      dataIndex: 'createdAt',
      width: 180,
      sorter: true,
      defaultSortOrder: 'descend',
      render: (value?: string) => formatDateTime(value),
    },
    {
      title: '备注',
      dataIndex: 'remarks',
      width: 220,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: '操作',
      key: 'action',
      width: 170,
      fixed: 'right',
      render: (_, record) => (
        <Space size={0}>
          <Tooltip title="编辑">
            <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>
              编辑
            </Button>
          </Tooltip>
          <Popconfirm
            title={`确认${record.status === 1 ? '禁用' : '启用'}该供应商？`}
            onConfirm={() => void handleToggleStatus(record)}
          >
            <Button
              type="link"
              danger={record.status === 1}
              icon={record.status === 1 ? <StopOutlined /> : <CheckCircleOutlined />}
            >
              {record.status === 1 ? '禁用' : '启用'}
            </Button>
          </Popconfirm>
          <Popconfirm
            title="确认删除该供应商？"
            description="删除后不可恢复。"
            onConfirm={() => void handleDelete(record)}
          >
            <Button type="link" danger icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title="国内供应商"
      subtitle="首批迁移国内采购模块，先落供应商管理，后续页面沿用同一路由与服务适配模式。"
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={() => void handleCreate()}>
          新建供应商
        </Button>
      }
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索供应商编码 / 名称 / 店铺号"
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
          <Button type="primary" onClick={() => void loadData(1, pageSize)}>
            查询
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize)}>
            刷新
          </Button>
          <Button
            icon={<CloudUploadOutlined />}
            loading={syncing}
            disabled={!selectedRowKeys.length}
            onClick={handleSync}
          >
            同步到销售库
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
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>

      <Modal
        title={editingItem ? '编辑国内供应商' : '新建国内供应商'}
        open={modalOpen}
        confirmLoading={saving}
        onCancel={handleCloseModal}
        onOk={() => void handleSave()}
        destroyOnClose
        width={680}
      >
        <Form form={form} layout="vertical">
          <Form.Item
            label="供应商编码"
            name="supplierCode"
            rules={[
              { required: true, message: '请输入供应商编码' },
              { max: 50, message: '供应商编码不能超过 50 个字符' },
              {
                validator: async (_, value) => {
                  if (!value) {
                    return
                  }
                  const exists = await checkSupplierCodeExists(value, editingItem?.guid)
                  if (exists) {
                    throw new Error('供应商编码已存在')
                  }
                },
              },
            ]}
          >
            <Input maxLength={50} disabled={Boolean(editingItem)} />
          </Form.Item>

          <Form.Item
            label="供应商名称"
            name="supplierName"
            rules={[
              { required: true, message: '请输入供应商名称' },
              { max: 200, message: '供应商名称不能超过 200 个字符' },
            ]}
          >
            <Input maxLength={200} />
          </Form.Item>

          <Space style={{ display: 'flex' }} align="start">
            <Form.Item label="店铺号" name="shopNumber" rules={[{ max: 50, message: '店铺号不能超过 50 个字符' }]}>
              <Input maxLength={50} style={{ width: 200 }} />
            </Form.Item>
            <Form.Item label="联系人" name="contactPerson" rules={[{ max: 100, message: '联系人不能超过 100 个字符' }]}>
              <Input maxLength={100} style={{ width: 200 }} />
            </Form.Item>
            <Form.Item label="状态" name="status" initialValue={1}>
              <Select
                style={{ width: 140 }}
                options={[
                  { label: '启用', value: 1 },
                  { label: '禁用', value: 0 },
                ]}
              />
            </Form.Item>
          </Space>

          <Space style={{ display: 'flex' }} align="start">
            <Form.Item
              label="联系电话"
              name="phone"
              rules={[
                { max: 20, message: '联系电话不能超过 20 个字符' },
                { pattern: /^[\d\s\-+()]+$/, message: '请输入有效的电话号码' },
              ]}
            >
              <Input maxLength={20} style={{ width: 260 }} />
            </Form.Item>
            <Form.Item
              label="联系邮箱"
              name="email"
              rules={[
                { type: 'email', message: '请输入有效的邮箱地址' },
                { max: 100, message: '联系邮箱不能超过 100 个字符' },
              ]}
            >
              <Input maxLength={100} style={{ width: 260 }} />
            </Form.Item>
          </Space>

          <Form.Item label="店面照片地址" name="storefrontPhoto">
            <Input />
          </Form.Item>

          <Form.Item label="备注" name="remarks" rules={[{ max: 1000, message: '备注不能超过 1000 个字符' }]}>
            <Input.TextArea rows={4} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
