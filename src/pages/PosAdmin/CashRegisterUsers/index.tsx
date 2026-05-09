import { CopyOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Input,
  message,
  Modal,
  Radio,
  Select,
  Space,
  Switch,
  Table,
  Tag,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useEffect, useRef, useState } from 'react'
import BarcodePreview from '../../../components/BarcodePreview'
import {
  batchDeleteCashRegisterUsers,
  createCashRegisterUser,
  deleteCashRegisterUser,
  getCashRegisterUserGrid,
  updateCashRegisterUser,
} from '../../../services/cashRegisterUserService'
import { getActiveStores } from '../../../services/storeService'
import type { CashRegisterUserListDto } from '../../../types/cashRegisterUser'
import { copyTextToClipboard } from '../../../utils/clipboard'

function getColorFromString(str: string): string {
  if (!str) return '#8c8c8c'
  let hash = 0
  for (let i = 0; i < str.length; i++) { hash = str.charCodeAt(i) + ((hash << 5) - hash) }
  return `hsl(${Math.abs(hash % 360)}, 70%, 55%)`
}

function generateBarcode13(): string {
  const rand12 = Array.from({ length: 12 }, () => Math.floor(Math.random() * 10)).join('')
  const digits = rand12.split('').map((d) => parseInt(d, 10))
  const sum = digits.reduce((acc, d, idx) => acc + d * (idx % 2 === 0 ? 1 : 3), 0)
  return rand12 + String((10 - (sum % 10)) % 10)
}

export default function CashRegisterUsersPage() {
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<CashRegisterUserListDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [keyword, setKeyword] = useState('')
  const [storeCode, setStoreCode] = useState<string | undefined>(undefined)
  const [status, setStatus] = useState<boolean | undefined>(undefined)
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [createVisible, setCreateVisible] = useState(false)
  const [editVisible, setEditVisible] = useState(false)
  const [editForm] = Form.useForm()
  const [createForm] = Form.useForm()
  const [editingRecord, setEditingRecord] = useState<CashRegisterUserListDto | null>(null)
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([])
  const [selectedRows, setSelectedRows] = useState<CashRegisterUserListDto[]>([])
  const inFlightRef = useRef(false)

  const loadData = async () => {
    if (inFlightRef.current) return
    inFlightRef.current = true
    setLoading(true)
    try {
      const filterModel: Record<string, any> = {}
      if (storeCode) filterModel.storeCode = { filterType: 'text', type: 'equals', filter: storeCode }
      if (status !== undefined) filterModel.status = { filterType: 'text', type: 'equals', filter: String(status) }
      const result = await getCashRegisterUserGrid({
        startRow: (page - 1) * pageSize,
        endRow: page * pageSize - 1,
        pageSize,
        globalSearch: keyword || '',
        filterModel: Object.keys(filterModel).length ? filterModel : {},
      })
      setData(result?.items ?? [])
      setTotal(result?.total ?? 0)
    } catch {
      message.error('加载失败')
    } finally {
      setLoading(false)
      inFlightRef.current = false
    }
  }

  useEffect(() => {
    ;(async () => {
      try { setStoreOptions(await getActiveStores()) } catch { /* ignore */ }
    })()
  }, [])

  useEffect(() => { loadData() }, [keyword, storeCode, status, page, pageSize])

  const handleCreate = async () => {
    try {
      const values = await createForm.validateFields()
      await createCashRegisterUser({
        storeCode: values.storeCode, operatorUser: values.operatorUser,
        userBarcode: values.userBarcode, loginRole: values.loginRole,
        remark: values.remark ?? '', status: values.status ?? true,
      })
      message.success('创建成功')
      setCreateVisible(false)
      createForm.resetFields()
      await loadData()
    } catch { message.error('创建失败') }
  }

  const handleEdit = (record: CashRegisterUserListDto) => {
    setEditingRecord(record)
    editForm.setFieldsValue({
      storeCode: record.storeCode, operatorUser: record.operatorUser,
      userBarcode: record.userBarcode, loginRole: record.loginRole,
      remark: record.remark, status: record.status,
    })
    setEditVisible(true)
  }

  const handleUpdate = async () => {
    if (!editingRecord) return
    try {
      const values = await editForm.validateFields()
      await updateCashRegisterUser(editingRecord.hGuid, {
        storeCode: values.storeCode, operatorUser: values.operatorUser,
        userBarcode: values.userBarcode, loginRole: values.loginRole,
        remark: values.remark ?? '', status: values.status,
      })
      message.success('更新成功')
      setEditVisible(false)
      editForm.resetFields()
      setEditingRecord(null)
      await loadData()
    } catch { message.error('更新失败') }
  }

  const handleDelete = async (record: CashRegisterUserListDto) => {
    Modal.confirm({
      title: '确认删除',
      content: `确定要删除 ${record.operatorUser || record.userBarcode} 吗？`,
      okText: '删除', cancelText: '取消', okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await deleteCashRegisterUser(record.hGuid)
          message.success('删除成功')
          await loadData()
        } catch { message.error('删除失败') }
      },
    })
  }

  const handleBatchDelete = async () => {
    if (!selectedRows.length) { message.warning('请选择要删除的记录'); return }
    Modal.confirm({
      title: '确认批量删除',
      content: `确定要删除选中的 ${selectedRows.length} 条记录吗？`,
      okText: '删除', cancelText: '取消', okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await batchDeleteCashRegisterUsers(selectedRows.map((r) => r.hGuid))
          message.success('删除成功')
          setSelectedRowKeys([])
          setSelectedRows([])
          await loadData()
        } catch { message.error('删除失败') }
      },
    })
  }

  const columns: ColumnsType<CashRegisterUserListDto> = [
    {
      title: '#', key: 'rowNum', width: 60, align: 'center',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    { title: '分店', dataIndex: 'storeName', sorter: (a, b) => (a.storeName || '').localeCompare(b.storeName || ''), render: (t: string) => <span style={{ color: getColorFromString(t) }}>{t}</span> },
    { title: '操作员', dataIndex: 'operatorUser', sorter: (a, b) => (a.operatorUser || '').localeCompare(b.operatorUser || ''), render: (t: string) => <span style={{ color: getColorFromString(t) }}>{t}</span> },
    { title: '条码', dataIndex: 'userBarcode', width: 250, render: (text: string) => text ? <BarcodePreview value={text} showCopy /> : null },
    {
      title: '登录角色', dataIndex: 'loginRole',
      render: (value: string) => {
        const roleText = value === '1' ? '管理员' : value === '2' ? '收银员' : value
        const color = value === '1' ? '#1890ff' : value === '2' ? '#52c41a' : '#8c8c8c'
        return <span style={{ color }}>{roleText}</span>
      },
    },
    { title: '打印次数', dataIndex: 'printCount', align: 'right' },
    { title: '状态', dataIndex: 'status', render: (v: boolean) => v ? <Tag color="green">启用</Tag> : <Tag color="red">禁用</Tag> },
    { title: '备注', dataIndex: 'remark', ellipsis: true },
    { title: '创建时间', dataIndex: 'createDate', render: (v: string) => dayjs(v).format('YYYY-MM-DD HH:mm:ss') },
    {
      title: '操作', key: 'action', width: 150,
      render: (_, record) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleEdit(record)}>编辑</Button>
          <Button type="link" size="small" danger onClick={() => handleDelete(record)}>删除</Button>
        </Space>
      ),
    },
  ]

  const renderFormFields = (formInstance: typeof createForm, barcodeVal?: string, isEdit?: boolean) => (
    <Form form={formInstance} layout="vertical">
      <Form.Item name="storeCode" label="分店" rules={[{ required: true, message: '请选择分店' }]}>
        <Select placeholder="选择分店" options={storeOptions} showSearch optionFilterProp="label" />
      </Form.Item>
      <Form.Item name="operatorUser" label="操作员" rules={[{ max: 100 }]}><Input placeholder="操作员名称" /></Form.Item>
      <Form.Item label="条码" required>
        <Space.Compact style={{ width: '100%' }}>
          <Form.Item name="userBarcode" rules={[{ required: true, message: '请输入条码', len: 13 }]} style={{ marginBottom: 0, flex: 1 }}><Input placeholder="13位条码" /></Form.Item>
          <Button icon={<CopyOutlined />} onClick={() => { const bc = formInstance.getFieldValue('userBarcode'); if (bc) copyTextToClipboard(bc) }} />
          {isEdit && <Button onClick={() => formInstance.setFieldsValue({ userBarcode: generateBarcode13() })}>换码</Button>}
        </Space.Compact>
      </Form.Item>
      {barcodeVal && <div style={{ marginBottom: 16 }}><BarcodePreview value={barcodeVal} /></div>}
      <Form.Item name="loginRole" label="登录角色" rules={[{ required: true, message: '请选择角色' }]}>
        <Radio.Group options={[{ label: '管理员', value: '1' }, { label: '收银员', value: '2' }]} />
      </Form.Item>
      <Form.Item name="remark" label="备注" rules={[{ max: 500 }]}><Input.TextArea placeholder="备注" rows={3} /></Form.Item>
      <Form.Item name="status" label="状态" valuePropName="checked" initialValue={true}><Switch checkedChildren="启用" unCheckedChildren="禁用" /></Form.Item>
    </Form>
  )

  const createBarcodeVal = Form.useWatch('userBarcode', createForm)
  const editBarcodeVal = Form.useWatch('userBarcode', editForm)

  return (
    <Card
      title="收银用户条码管理"
      extra={
        <Space>
          <Input.Search allowClear placeholder="搜索" style={{ width: 240 }} onSearch={(v) => { setKeyword(v); setPage(1) }} />
          <Select allowClear placeholder="分店" style={{ width: 150 }} value={storeCode} onChange={(v) => { setStoreCode(v); setPage(1) }} options={storeOptions} showSearch optionFilterProp="label" />
          <Select allowClear placeholder="状态" style={{ width: 100 }} value={status === undefined ? undefined : String(status)} onChange={(v) => { setStatus(v === undefined ? undefined : v === 'true'); setPage(1) }} options={[{ label: '启用', value: 'true' }, { label: '禁用', value: 'false' }]} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { createForm.resetFields(); createForm.setFieldsValue({ userBarcode: generateBarcode13(), loginRole: '2', status: true }); setCreateVisible(true) }}>新建</Button>
        </Space>
      }
      styles={{ body: { padding: 0 } }}
    >
      <div style={{ padding: 16 }}>
        <Table
          rowKey="hGuid"
          loading={loading}
          dataSource={data}
          rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
          rowSelection={{ selectedRowKeys, onChange: (keys, rows) => { setSelectedRowKeys(keys as string[]); setSelectedRows(rows as CashRegisterUserListDto[]) } }}
          columns={columns}
          pagination={{
            total,
            current: page,
            pageSize,
            showSizeChanger: true,
            pageSizeOptions: ['10','20', '50', '100'],
            showTotal: (t) => `共 ${t} 条`,
            onChange: (p, ps) => { setPage(p); setPageSize(ps) },
          }}
        />
        {selectedRows.length > 0 && <Button danger icon={<DeleteOutlined />} onClick={handleBatchDelete} style={{ marginTop: 8 }}>批量删除 ({selectedRows.length})</Button>}
      </div>

      <Modal open={createVisible} title="新建收银用户" onCancel={() => { setCreateVisible(false); createForm.resetFields() }} onOk={handleCreate} width={600}>
        {renderFormFields(createForm, createBarcodeVal)}
      </Modal>

      <Modal open={editVisible} title="编辑收银用户" onCancel={() => { setEditVisible(false); editForm.resetFields(); setEditingRecord(null) }} onOk={handleUpdate} width={600}>
        {renderFormFields(editForm, editBarcodeVal, true)}
      </Modal>
    </Card>
  )
}
