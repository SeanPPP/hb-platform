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
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation()
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
      message.error(t('message.loadFailed'))
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
      message.success(t('message.createSuccess'))
      setCreateVisible(false)
      createForm.resetFields()
      await loadData()
    } catch { message.error(t('message.createFailed')) }
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
      message.success(t('message.updateSuccess'))
      setEditVisible(false)
      editForm.resetFields()
      setEditingRecord(null)
      await loadData()
    } catch { message.error(t('message.updateFailed')) }
  }

  const handleDelete = async (record: CashRegisterUserListDto) => {
    Modal.confirm({
      title: t('message.confirmDelete'),
      content: t('posAdmin.cashierUsers.confirmDeleteUser', { name: record.operatorUser || record.userBarcode }),
      okText: t('common.delete'), cancelText: t('common.cancel'), okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await deleteCashRegisterUser(record.hGuid)
          message.success(t('message.deleteSuccess'))
          await loadData()
        } catch { message.error(t('message.deleteFailed')) }
      },
    })
  }

  const handleBatchDelete = async () => {
    if (!selectedRows.length) { message.warning(t('message.pleaseSelect')); return }
    Modal.confirm({
      title: t('posAdmin.cashierUsers.confirmBatchDelete', '确认批量删除'),
      content: t('message.batchDeleteConfirm', { count: selectedRows.length }),
      okText: t('common.delete'), cancelText: t('common.cancel'), okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await batchDeleteCashRegisterUsers(selectedRows.map((r) => r.hGuid))
          message.success(t('message.deleteSuccess'))
          setSelectedRowKeys([])
          setSelectedRows([])
          await loadData()
        } catch { message.error(t('message.deleteFailed')) }
      },
    })
  }

  const columns: ColumnsType<CashRegisterUserListDto> = [
    {
      title: '#', key: 'rowNum', width: 60, align: 'center',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    { title: t('posAdmin.cashierUsers.store'), dataIndex: 'storeName', sorter: (a, b) => (a.storeName || '').localeCompare(b.storeName || ''), render: (v: string) => <span style={{ color: getColorFromString(v) }}>{v}</span> },
    { title: t('posAdmin.cashierUsers.operator'), dataIndex: 'operatorUser', sorter: (a, b) => (a.operatorUser || '').localeCompare(b.operatorUser || ''), render: (v: string) => <span style={{ color: getColorFromString(v) }}>{v}</span> },
    { title: t('posAdmin.cashierUsers.barcode'), dataIndex: 'userBarcode', width: 250, render: (text: string) => text ? <BarcodePreview value={text} showCopy /> : null },
    {
      title: t('posAdmin.cashierUsers.loginRole'), dataIndex: 'loginRole',
      render: (value: string) => {
        const roleText = value === '1' ? t('posAdmin.cashierUsers.admin') : value === '2' ? t('posAdmin.cashierUsers.cashier') : value
        const color = value === '1' ? '#1890ff' : value === '2' ? '#52c41a' : '#8c8c8c'
        return <span style={{ color }}>{roleText}</span>
      },
    },
    { title: t('posAdmin.cashierUsers.printCount', '打印次数'), dataIndex: 'printCount', align: 'right' },
    { title: t('column.status'), dataIndex: 'status', render: (v: boolean) => v ? <Tag color="green">{t('common.active')}</Tag> : <Tag color="red">{t('common.inactive')}</Tag> },
    { title: t('column.remarks'), dataIndex: 'remark', ellipsis: true },
    { title: t('column.createTime'), dataIndex: 'createDate', render: (v: string) => dayjs(v).format('YYYY-MM-DD HH:mm:ss') },
    {
      title: t('column.action'), key: 'action', width: 150,
      render: (_, record) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleEdit(record)}>{t('common.edit')}</Button>
          <Button type="link" size="small" danger onClick={() => handleDelete(record)}>{t('common.delete')}</Button>
        </Space>
      ),
    },
  ]

  const renderFormFields = (formInstance: typeof createForm, barcodeVal?: string, isEdit?: boolean) => (
    <Form form={formInstance} layout="vertical">
      <Form.Item name="storeCode" label={t('posAdmin.cashierUsers.store')} rules={[{ required: true, message: t('form.pleaseSelectStore') }]}>
        <Select placeholder={t('form.pleaseSelectStore')} options={storeOptions} showSearch optionFilterProp="label" />
      </Form.Item>
      <Form.Item name="operatorUser" label={t('posAdmin.cashierUsers.operator')} rules={[{ max: 100 }]}><Input placeholder={t('posAdmin.cashierUsers.operatorName')} /></Form.Item>
      <Form.Item label={t('posAdmin.cashierUsers.barcode')} required>
        <Space.Compact style={{ width: '100%' }}>
          <Form.Item name="userBarcode" rules={[{ required: true, message: t('posAdmin.cashierUsers.barcodeRequired', '请输入条码'), len: 13 }]} style={{ marginBottom: 0, flex: 1 }}><Input placeholder={t('posAdmin.cashierUsers.barcode13')} /></Form.Item>
          <Button icon={<CopyOutlined />} onClick={() => { const bc = formInstance.getFieldValue('userBarcode'); if (bc) copyTextToClipboard(bc) }} />
          {isEdit && <Button onClick={() => formInstance.setFieldsValue({ userBarcode: generateBarcode13() })}>{t('posAdmin.cashierUsers.changeCode')}</Button>}
        </Space.Compact>
      </Form.Item>
      {barcodeVal && <div style={{ marginBottom: 16 }}><BarcodePreview value={barcodeVal} /></div>}
      <Form.Item name="loginRole" label={t('posAdmin.cashierUsers.loginRole')} rules={[{ required: true, message: t('posAdmin.cashierUsers.roleRequired', '请选择角色') }]}>
        <Radio.Group options={[{ label: t('posAdmin.cashierUsers.admin'), value: '1' }, { label: t('posAdmin.cashierUsers.cashier'), value: '2' }]} />
      </Form.Item>
      <Form.Item name="remark" label={t('column.remarks')} rules={[{ max: 500 }]}><Input.TextArea placeholder={t('column.remarks')} rows={3} /></Form.Item>
      <Form.Item name="status" label={t('common.status')} valuePropName="checked" initialValue={true}><Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} /></Form.Item>
    </Form>
  )

  const createBarcodeVal = Form.useWatch('userBarcode', createForm)
  const editBarcodeVal = Form.useWatch('userBarcode', editForm)

  return (
    <Card
      title={t('posAdmin.cashierUsers.title')}
      extra={
        <Space>
          <Input.Search allowClear placeholder={t('common.search')} style={{ width: 240 }} onSearch={(v) => { setKeyword(v); setPage(1) }} />
          <Select allowClear placeholder={t('posAdmin.cashierUsers.store')} style={{ width: 150 }} value={storeCode} onChange={(v) => { setStoreCode(v); setPage(1) }} options={storeOptions} showSearch optionFilterProp="label" />
          <Select allowClear placeholder={t('common.status')} style={{ width: 100 }} value={status === undefined ? undefined : String(status)} onChange={(v) => { setStatus(v === undefined ? undefined : v === 'true'); setPage(1) }} options={[{ label: t('common.active'), value: 'true' }, { label: t('common.inactive'), value: 'false' }]} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { createForm.resetFields(); createForm.setFieldsValue({ userBarcode: generateBarcode13(), loginRole: '2', status: true }); setCreateVisible(true) }}>{t('common.create')}</Button>
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
            showTotal: (total) => t('common.total', { count: total }),
            onChange: (p, ps) => { setPage(p); setPageSize(ps) },
          }}
        />
        {selectedRows.length > 0 && <Button danger icon={<DeleteOutlined />} onClick={handleBatchDelete} style={{ marginTop: 8 }}>{t('common.batchDelete')} ({selectedRows.length})</Button>}
      </div>

      <Modal open={createVisible} title={t('posAdmin.cashierUsers.createUser')} onCancel={() => { setCreateVisible(false); createForm.resetFields() }} onOk={handleCreate} width={600}>
        {renderFormFields(createForm, createBarcodeVal)}
      </Modal>

      <Modal open={editVisible} title={t('posAdmin.cashierUsers.editUser')} onCancel={() => { setEditVisible(false); editForm.resetFields(); setEditingRecord(null) }} onOk={handleUpdate} width={600}>
        {renderFormFields(editForm, editBarcodeVal, true)}
      </Modal>
    </Card>
  )
}
