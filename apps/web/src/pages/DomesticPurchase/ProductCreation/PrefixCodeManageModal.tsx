import { DeleteOutlined, EditOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import { Button, Col, Form, Input, InputNumber, message, Modal, Popconfirm, Row, Space, Spin, Switch, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import React, { useCallback, useEffect, useState } from 'react'
import {
  createPrefixCode,
  deletePrefixCode,
  getPrefixCodeList,
  togglePrefixCodeStatus,
  updatePrefixCode,
} from '../../../services/domesticProductCreationService'

const handlePrefixNameChange = (e: React.ChangeEvent<HTMLInputElement>, form: ReturnType<typeof Form.useForm>[0]) => {
  const upperValue = e.target.value.toUpperCase()
  form.setFieldValue('prefixName', upperValue)
}

interface PrefixCodeManageModalProps {
  visible: boolean
  supplierCode: string
  supplierName: string
  onClose: () => void
  onSuccess?: () => void
}

interface PrefixCodeItem {
  prefixCode: string
  supplierCode: string
  supplierName?: string
  prefixName: string
  prefixDescription?: string
  isActive: boolean
  sortOrder?: number
  createdAt: string
}

export default function PrefixCodeManageModal({ visible, supplierCode, supplierName, onClose, onSuccess }: PrefixCodeManageModalProps) {
  const { t } = useTranslation()
  const [form] = Form.useForm()
  const [editForm] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [list, setList] = useState<PrefixCodeItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [search, setSearch] = useState('')
  const [editingKey, setEditingKey] = useState('')

  const loadList = useCallback(async () => {
    if (!supplierCode) return
    setLoading(true)
    try {
      const res = await getPrefixCodeList({ page, pageSize, search: search || undefined, supplierCode } as any)
      setLoading(false)
      if (res.success) {
        setList(res.data?.items || [])
        setTotal(res.data?.total || 0)
      } else {
        message.error(res.message || t('productCreation.loadPrefixFailed', '加载前缀码失败'))
      }
    } catch {
      setLoading(false)
      message.error(t('productCreation.loadPrefixFailed', '加载前缀码失败'))
    }
  }, [supplierCode, page, pageSize, search])

  useEffect(() => {
    if (visible && supplierCode) {
      setPage(1)
      setSearch('')
      form.resetFields()
      editForm.resetFields()
      setEditingKey('')
      loadList()
    }
  }, [visible, supplierCode])

  const handleAdd = useCallback(async () => {
    try {
      const values = await form.validateFields()
      setSubmitting(true)
      const res = await createPrefixCode({
        supplierCode,
        prefixName: values.prefixName?.toUpperCase(),
        prefixDescription: values.prefixDescription,
        isActive: values.isActive ?? true,
        sortOrder: values.sortOrder,
      })
      setSubmitting(false)
      if (res.success) {
        message.success(t('productCreation.addSuccess', '添加成功'))
        form.resetFields()
        loadList()
        onSuccess?.()
      } else {
        message.error(res.message || t('productCreation.addFailed', '添加失败'))
      }
    } catch {
      setSubmitting(false)
    }
  }, [supplierCode, form, loadList, onSuccess])

  const handleEdit = useCallback(async () => {
    if (!editingKey) return
    try {
      const values = await editForm.validateFields()
      setSubmitting(true)
      const res = await updatePrefixCode(editingKey, {
        prefixName: values.prefixName?.toUpperCase(),
        prefixDescription: values.prefixDescription,
        isActive: values.isActive ?? true,
        sortOrder: values.sortOrder,
      })
      setSubmitting(false)
      if (res.success) {
        message.success(t('productCreation.updateSuccess', '更新成功'))
        setEditingKey('')
        editForm.resetFields()
        loadList()
        onSuccess?.()
      } else {
        message.error(res.message || t('productCreation.updateFailed', '更新失败'))
      }
    } catch {
      setSubmitting(false)
    }
  }, [editingKey, editForm, loadList, onSuccess])

  const handleDelete = useCallback(
    async (prefixCode: string) => {
      const res = await deletePrefixCode(prefixCode)
      if (res.success) {
        message.success(t('productCreation.deleteSuccess', '删除成功'))
        loadList()
        onSuccess?.()
      } else {
        message.error(res.message || t('productCreation.deleteFailed', '删除失败'))
      }
    },
    [loadList, onSuccess],
  )

  const handleToggleStatus = useCallback(
    async (prefixCode: string, isActive: boolean) => {
      const res = await togglePrefixCodeStatus(prefixCode, isActive)
      if (res.success) {
        message.success(t('productCreation.statusUpdateSuccess', '状态更新成功'))
        loadList()
        onSuccess?.()
      } else {
        message.error(res.message || t('productCreation.statusUpdateFailed', '状态更新失败'))
      }
    },
    [loadList, onSuccess],
  )

  const startEdit = useCallback((record: PrefixCodeItem) => {
    setEditingKey(record.prefixCode)
    editForm.setFieldsValue({ prefixName: record.prefixName, prefixDescription: record.prefixDescription, isActive: record.isActive, sortOrder: record.sortOrder })
  }, [editForm])

  const cancelEdit = useCallback(() => {
    setEditingKey('')
    editForm.resetFields()
  }, [editForm])

  const columns: ColumnsType<PrefixCodeItem> = [
    {
      title: t('productCreation.prefixCode', '前缀码'),
      dataIndex: 'prefixName',
      key: 'prefixName',
      width: 150,
      render: (text, record) =>
        editingKey === record.prefixCode ? (
          <Form.Item name="prefixName" noStyle><Input style={{ width: 120, textTransform: 'uppercase' }} onChange={(e) => handlePrefixNameChange(e, editForm)} /></Form.Item>
        ) : text,
    },
    {
      title: t('productCreation.description', '描述'),
      dataIndex: 'prefixDescription',
      key: 'prefixDescription',
      width: 200,
      render: (text, record) =>
        editingKey === record.prefixCode ? <Form.Item name="prefixDescription" noStyle><Input style={{ width: 160 }} /></Form.Item> : text || '-',
    },
    {
      title: t('productCreation.sort', '排序'),
      dataIndex: 'sortOrder',
      key: 'sortOrder',
      width: 100,
      render: (text, record) =>
        editingKey === record.prefixCode ? <Form.Item name="sortOrder" noStyle><InputNumber min={0} style={{ width: 70 }} /></Form.Item> : text ?? '-',
    },
    {
      title: t('domesticProducts.status', '状态'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (isActive, record) =>
        editingKey === record.prefixCode ? (
          <Form.Item name="isActive" valuePropName="checked" noStyle><Switch /></Form.Item>
        ) : (
          <Switch checked={isActive} onChange={(checked) => handleToggleStatus(record.prefixCode, checked)} checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} />
        ),
    },
    {
      title: t('chinaSuppliers.createdAt', '创建时间'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      render: (text) => (text ? new Date(text).toLocaleString('zh-CN') : '-'),
    },
    {
      title: t('common.action', '操作'),
      key: 'actions',
      width: 120,
      fixed: 'right',
      render: (_, record) =>
        editingKey === record.prefixCode ? (
          <Space size="small">
            <Button type="link" size="small" onClick={handleEdit} loading={submitting}>{t('common.confirm', '确定')}</Button>
            <Button type="link" size="small" onClick={cancelEdit}>{t('common.cancel', '取消')}</Button>
          </Space>
        ) : (
          <Space size="small">
            <Button type="link" size="small" icon={<EditOutlined />} onClick={() => startEdit(record)} />
            <Popconfirm title={t('productCreation.confirmDelete', '确定删除？')} onConfirm={() => handleDelete(record.prefixCode)}>
              <Button type="link" size="small" danger icon={<DeleteOutlined />} />
            </Popconfirm>
          </Space>
        ),
    },
  ]

  return (
    <Modal title={t('productCreation.managePrefixTitle', '管理前缀码 - {{name}}', { name: supplierName || supplierCode })} open={visible} onCancel={onClose} width={900} footer={null} destroyOnClose>
      <Form form={form} layout="inline" style={{ marginBottom: 16 }}>
        <Row gutter={12} style={{ width: '100%' }}>
          <Col flex="120px">
            <Form.Item name="prefixName" rules={[{ required: true, message: t('productCreation.enterPrefixCode', '请输入前缀码') }, { pattern: /^[A-Za-z0-9]+$/, message: t('productCreation.alphaNumOnly', '仅限字母和数字') }]}>
              <Input placeholder={t('productCreation.prefixCode', '前缀码')} maxLength={10} style={{ textTransform: 'uppercase' }} onChange={(e) => handlePrefixNameChange(e, form)} />
            </Form.Item>
          </Col>
          <Col flex="200px">
            <Form.Item name="prefixDescription"><Input placeholder={t('productCreation.description', '描述')} /></Form.Item>
          </Col>
          <Col flex="80px">
            <Form.Item name="sortOrder" initialValue={0}><InputNumber min={0} placeholder="#" style={{ width: '100%' }} /></Form.Item>
          </Col>
          <Col flex="80px">
            <Form.Item name="isActive" valuePropName="checked" initialValue={true}><Switch checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} /></Form.Item>
          </Col>
          <Col>
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd} loading={submitting}>{t('common.add', '添加')}</Button>
          </Col>
        </Row>
      </Form>
      <div style={{ marginBottom: 12 }}>
        <Space>
          <Input placeholder={t('productCreation.searchPrefix', '搜索前缀码')} prefix={<SearchOutlined />} value={search} onChange={(e) => setSearch(e.target.value)} onPressEnter={() => { setPage(1); loadList() }} style={{ width: 240 }} allowClear />
          <Button onClick={() => { setPage(1); loadList() }}>{t('common.search', '搜索')}</Button>
        </Space>
      </div>
      <Spin spinning={loading}>
        <Table columns={columns} dataSource={list} rowKey="prefixCode" size="small" pagination={{ current: page, pageSize, total, showSizeChanger: true, showQuickJumper: true, showTotal: (total) => t('common.totalCount', '共 {{count}} 条', { count: total }), onChange: (p, ps) => { setPage(p); setPageSize(ps) } }} scroll={{ x: 800 }} />
      </Spin>
    </Modal>
  )
}
