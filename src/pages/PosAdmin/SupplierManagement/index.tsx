import { PlusOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Input,
  Modal,
  Pagination,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  message,
} from 'antd'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createLocalSupplier, getLocalSuppliers, syncLocalSuppliers } from '../../../services/localSupplierService'
import type { LocalSupplierDto } from '../../../types/localSupplier'

const SORT_FIELD_MAP: Record<string, string> = {
  localSupplierCode: 'localsuppliercode',
  name: 'name',
  status: 'status',
  contactPerson: 'contactperson',
  phone: 'phone',
  email: 'email',
  remark: 'remark',
}

export default function SupplierManagementPage() {
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<LocalSupplierDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [keyword, setKeyword] = useState('')
  const [status, setStatus] = useState<string | undefined>(undefined)
  const [sortBy, setSortBy] = useState('name')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('ascend')
  const [createVisible, setCreateVisible] = useState(false)
  const [createForm] = Form.useForm()
  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const loadData = async () => {
    setLoading(true)
    try {
      const result = await getLocalSuppliers({
        pageIndex: page,
        pageSize,
        keyword,
        status,
        sortBy: SORT_FIELD_MAP[sortBy] || sortBy,
        sortOrder,
      })
      setData(result?.items ?? [])
      setTotal(result?.total ?? 0)
    } catch {
      message.error('加载供应商列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData()
  }, [page, pageSize, keyword, status, sortBy, sortOrder])

  useLayoutEffect(() => {
    const calc = () => {
      const containerH = wrapRef.current?.clientHeight || window.innerHeight
      const tbarH = toolbarRef.current?.getBoundingClientRect().height || 0
      const pagerH = pagerRef.current?.getBoundingClientRect().height || 0
      const available = containerH - tbarH - pagerH - 8
      setTableScrollY(available > 200 ? available : 200)
    }
    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [pageSize, total])

  const handleSync = async () => {
    try {
      const result = await syncLocalSuppliers()
      message.success(
        `同步完成：新增${result.createdCount ?? 0}，更新${result.updatedCount ?? 0}，停用${result.deactivatedCount ?? 0}`,
      )
      await loadData()
    } catch {
      message.error('同步失败')
    }
  }

  const handleCreate = async () => {
    const values = await createForm.validateFields()
    try {
      await createLocalSupplier({
        name: values.name,
        status: values.status ? 1 : 0,
        contactPerson: values.contactPerson,
        phone: values.phone,
        email: values.email,
        remark: values.remark,
      })
      message.success('创建成功')
      setCreateVisible(false)
      createForm.resetFields()
      await loadData()
    } catch {
      message.error('创建失败')
    }
  }

  return (
    <Card
      title="供应商管理"
      extra={
        <Space>
          <Input.Search
            allowClear
            placeholder="代码/名称"
            style={{ width: 200 }}
            onSearch={(v) => {
              setKeyword(v)
              setPage(1)
            }}
          />
          <Select
            allowClear
            placeholder="状态"
            style={{ width: 120 }}
            value={status}
            onChange={(v) => {
              setStatus(v)
              setPage(1)
            }}
            options={[
              { label: '启用', value: '1' },
              { label: '禁用', value: '0' },
            ]}
          />
          <Button icon={<ReloadOutlined />} onClick={handleSync}>
            同步
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateVisible(true)}>
            新建供应商
          </Button>
        </Space>
      }
      styles={{ body: { padding: 0 } }}
    >
      <div
        ref={wrapRef}
        style={{
          height: 'calc(100vh - 160px)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div ref={toolbarRef} style={{ padding: 16 }} />
        <div style={{ flex: 1, minHeight: 0 }}>
          <Table
            rowKey={(r) => r.guid || r.localSupplierCode}
            loading={loading}
            dataSource={data}
            pagination={false}
            scroll={tableScrollY ? { y: tableScrollY } : undefined}
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            columns={[
              {
                title: '序号',
                width: 72,
                align: 'right',
                render: (_, __, index) => (page - 1) * pageSize + index + 1,
              },
              {
                title: '代码',
                dataIndex: 'localSupplierCode',
                sorter: true,
                sortOrder: sortBy === 'localSupplierCode' ? sortOrder : undefined,
              },
              {
                title: '名称',
                dataIndex: 'name',
                sorter: true,
                sortOrder: sortBy === 'name' ? sortOrder : undefined,
              },
              {
                title: '状态',
                dataIndex: 'status',
                sorter: true,
                sortOrder: sortBy === 'status' ? sortOrder : undefined,
                render: (v: number) =>
                  v === 1 ? <Tag color="green">启用</Tag> : <Tag color="red">禁用</Tag>,
              },
              {
                title: '联系人',
                dataIndex: 'contactPerson',
                sorter: true,
                sortOrder: sortBy === 'contactPerson' ? sortOrder : undefined,
              },
              {
                title: '电话',
                dataIndex: 'phone',
                sorter: true,
                sortOrder: sortBy === 'phone' ? sortOrder : undefined,
              },
              {
                title: 'Email',
                dataIndex: 'email',
                sorter: true,
                sortOrder: sortBy === 'email' ? sortOrder : undefined,
              },
              {
                title: '备注',
                dataIndex: 'remark',
                sorter: true,
                sortOrder: sortBy === 'remark' ? sortOrder : undefined,
              },
            ]}
            onChange={(_pagination, _filters, sorter) => {
              const s = Array.isArray(sorter) ? sorter[0] : sorter
              const field = s?.field || s?.column?.dataIndex
              const order = s?.order as 'ascend' | 'descend' | undefined
              if (field && order) {
                setSortBy(field)
                setSortOrder(order)
              } else {
                setSortBy('name')
                setSortOrder('ascend')
              }
            }}
          />
        </div>
        <div
          ref={pagerRef}
          style={{
            padding: '8px 16px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            width: '100%',
            background: '#fff',
            zIndex: 1,
          }}
        >
          <div />
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            onChange={(p, ps) => {
              setPage(p)
              setPageSize(ps)
            }}
            showSizeChanger
            responsive={false}
            pageSizeOptions={[10, 20, 50, 100, 200]}
          />
        </div>
      </div>

      <Modal
        open={createVisible}
        title="新建供应商"
        onCancel={() => {
          setCreateVisible(false)
          createForm.resetFields()
        }}
        onOk={handleCreate}
      >
        <Form form={createForm} layout="vertical">
          <Form.Item name="name" label="名称" rules={[{ required: true, message: '请输入名称' }, { max: 128 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="status" label="启用" valuePropName="checked" initialValue={true}>
            <Switch />
          </Form.Item>
          <Form.Item name="contactPerson" label="联系人" rules={[{ max: 64 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="phone" label="电话" rules={[{ max: 32 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="email" label="Email" rules={[{ type: 'email' }, { max: 128 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="remark" label="备注" rules={[{ max: 256 }]}>
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}
