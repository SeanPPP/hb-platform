import { CopyOutlined, PlusOutlined, SendOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  Modal,
  Pagination,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../../store/auth'
import { getStableTagColor } from '../../../utils/tagColors'
import {
  checkInvoiceNoExists,
  createInvoice,
  deleteInvoice,
  getInvoiceGrid,
  pushInvoicesToHq,
} from '../../../services/localSupplierInvoiceService'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import { getActiveStores } from '../../../services/storeService'
import type { LocalSupplierInvoiceListDto } from '../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../utils/clipboard'

const SORT_FIELD_MAP: Record<string, string> = {
  storeName: 'storeName',
  supplierName: 'supplierName',
  invoiceNo: 'invoiceNo',
  orderDate: 'orderDate',
  inboundDate: 'inboundDate',
  totalAmount: 'totalAmount',
  receivedTotalAmount: 'receivedTotalAmount',
  flowStatus: 'flowStatus',
  inboundStatus: 'inboundStatus',
  createdAt: 'createdAt',
  createdBy: 'createdBy',
  updatedAt: 'updatedAt',
  updatedBy: 'updatedBy',
}

const FLOW_STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: '草稿', color: 'default' },
  1: { label: '已提交', color: 'blue' },
  2: { label: '已审核', color: 'green' },
  3: { label: '已推送', color: 'purple' },
}

const INBOUND_STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: '未入库', color: 'default' },
  1: { label: '部分入库', color: 'orange' },
  2: { label: '已入库', color: 'green' },
}

function formatDateTime(value?: string) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', { hour12: false })
}

function formatDate(value?: string) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString('zh-CN')
}

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

export default function LocalSupplierInvoicesPage() {
  const navigate = useNavigate()
  const { access } = useAuthStore()
  const isAdmin = access.isAdmin

  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<LocalSupplierInvoiceListDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [sortBy, setSortBy] = useState('createdAt')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend')

  // 筛选条件
  const [storeCode, setStoreCode] = useState<string | undefined>(undefined)
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [invoiceNo, setInvoiceNo] = useState('')
  const [keyword, setKeyword] = useState('')

  // 下拉选项
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])

  // 行选择
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  // 创建 Modal
  const [createVisible, setCreateVisible] = useState(false)
  const [createForm] = Form.useForm()
  const [creating, setCreating] = useState(false)
  const [invoiceNoChecking, setInvoiceNoChecking] = useState(false)

  // 推送到 HQ
  const [pushing, setPushing] = useState(false)

  // 动态高度
  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const loadData = async () => {
    setLoading(true)
    try {
      const startRow = (page - 1) * pageSize
      const filterModel: Record<string, unknown> = {}
      if (storeCode) {
        filterModel.storeCode = { filterType: 'text', type: 'equals', filter: storeCode }
      }
      if (supplierCode) {
        filterModel.supplierCode = { filterType: 'text', type: 'equals', filter: supplierCode }
      }
      if (invoiceNo) {
        filterModel.invoiceNo = { filterType: 'text', type: 'contains', filter: invoiceNo }
      }
      if (keyword) {
        filterModel.productKeyword = { filterType: 'text', filter: keyword }
      }
      const sortField = SORT_FIELD_MAP[sortBy] || sortBy
      const sortModel = [{ colId: sortField, sort: sortOrder === 'ascend' ? 'asc' : 'desc' }]
      const result = await getInvoiceGrid({
        startRow,
        endRow: startRow + pageSize,
        pageSize,
        filterModel: Object.keys(filterModel).length ? filterModel : undefined,
        sortModel,
      } as Record<string, unknown>)
      setData(result?.items ?? [])
      setTotal(result?.total ?? 0)
    } catch {
      message.error('加载进货单列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData()
  }, [page, pageSize, sortBy, sortOrder])

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

  useEffect(() => {
    const loadOptions = async () => {
      try {
        const [stores, suppliers] = await Promise.all([
          getActiveStores(),
          getActiveLocalSuppliers(),
        ])
        setStoreOptions(stores)
        setSupplierOptions(
          suppliers.map((s) => ({
            label: s.name || s.localSupplierCode,
            value: s.localSupplierCode,
          })),
        )
      } catch {
        /* ignore */
      }
    }
    loadOptions()
  }, [])

  const handleSearch = () => {
    setPage(1)
    loadData()
  }

  const handleReset = () => {
    setStoreCode(undefined)
    setSupplierCode(undefined)
    setInvoiceNo('')
    setKeyword('')
    setSortBy('createdAt')
    setSortOrder('descend')
    setPage(1)
    setTimeout(() => loadData(), 0)
  }

  const handleDelete = async (invoiceGuid: string) => {
    try {
      await deleteInvoice(invoiceGuid)
      message.success('删除成功')
      loadData()
    } catch {
      message.error('删除失败')
    }
  }

  const handleCreate = async () => {
    const values = await createForm.validateFields()
    // 随货单号重复检测
    const invoiceNoValue = values.invoiceNo?.trim()
    if (invoiceNoValue) {
      setInvoiceNoChecking(true)
      try {
        const checkResult = await checkInvoiceNoExists({ invoiceNo: invoiceNoValue })
        if (checkResult.exists) {
          message.error('该随货单号已存在，请使用其他单号')
          setInvoiceNoChecking(false)
          return
        }
      } catch {
        // 检测失败不阻止创建
      }
      setInvoiceNoChecking(false)
    }

    setCreating(true)
    try {
      const newGuid = await createInvoice({
        storeCode: values.storeCode,
        supplierCode: values.supplierCode,
        invoiceNo: invoiceNoValue,
        orderDate: values.orderDate?.format('YYYY-MM-DD'),
        inboundDate: values.inboundDate?.format('YYYY-MM-DD'),
        remarks: values.remarks?.trim() || undefined,
      })
      message.success('创建成功')
      setCreateVisible(false)
      createForm.resetFields()
      navigate(`/pos-admin/local-supplier-invoices/${newGuid}`)
    } catch {
      message.error('创建失败')
    } finally {
      setCreating(false)
    }
  }

  const handlePushToHq = async () => {
    if (!selectedRowKeys.length) {
      message.warning('请先选择要推送的进货单')
      return
    }
    setPushing(true)
    try {
      const result = await pushInvoicesToHq(selectedRowKeys.map(String))
      message.success(
        `推送完成：成功${(result?.updated ?? 0)} 条，失败${(result?.failed ?? 0)} 条`,
      )
      setSelectedRowKeys([])
      loadData()
    } catch {
      message.error('推送失败')
    } finally {
      setPushing(false)
    }
  }

  const columns: ColumnsType<LocalSupplierInvoiceListDto> = [
    {
      title: '序号',
      width: 60,
      align: 'right',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: '分店',
      dataIndex: 'storeCode',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'storeCode' ? sortOrder : undefined,
      render: (_: string, record) => (
        <Tag color={getStableTagColor(record.storeCode || '')}>
          {record.storeName ? `${record.storeCode} - ${record.storeName}` : record.storeCode || '--'}
        </Tag>
      ),
    },
    {
      title: '供应商',
      dataIndex: 'supplierCode',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'supplierCode' ? sortOrder : undefined,
      render: (_: string, record) => (
        <Tag color={getStableTagColor(record.supplierCode || '')}>
          {record.supplierName ? `${record.supplierCode} - ${record.supplierName}` : record.supplierCode || '--'}
        </Tag>
      ),
    },
    {
      title: '随货单号',
      dataIndex: 'invoiceNo',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'invoiceNo' ? sortOrder : undefined,
      render: (value: string) => (
        <Space size={4}>
          <span>{value || '--'}</span>
          {value && (
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              onClick={() => void copyTextToClipboard(value)}
            />
          )}
        </Space>
      ),
    },
    {
      title: '订单日期',
      dataIndex: 'orderDate',
      width: 120,
      sorter: true,
      sortOrder: sortBy === 'orderDate' ? sortOrder : undefined,
      render: (v: string) => formatDate(v),
    },
    {
      title: '入库日期',
      dataIndex: 'inboundDate',
      width: 120,
      sorter: true,
      sortOrder: sortBy === 'inboundDate' ? sortOrder : undefined,
      render: (v: string) => formatDate(v),
    },
    {
      title: '总金额',
      dataIndex: 'totalAmount',
      width: 120,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'totalAmount' ? sortOrder : undefined,
      render: (v: number) => formatAmount(v),
    },
    {
      title: '已收总金额',
      dataIndex: 'receivedTotalAmount',
      width: 120,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'receivedTotalAmount' ? sortOrder : undefined,
      render: (v: number) => formatAmount(v),
    },
    {
      title: '流程状态',
      dataIndex: 'flowStatus',
      width: 100,
      sorter: true,
      sortOrder: sortBy === 'flowStatus' ? sortOrder : undefined,
      render: (v: number) => {
        const info = FLOW_STATUS_MAP[v] || { label: `状态${v}`, color: 'default' }
        return <Tag color={info.color}>{info.label}</Tag>
      },
    },
    {
      title: '入库状态',
      dataIndex: 'inboundStatus',
      width: 100,
      sorter: true,
      sortOrder: sortBy === 'inboundStatus' ? sortOrder : undefined,
      render: (v: number) => {
        const info = INBOUND_STATUS_MAP[v] || { label: `状态${v}`, color: 'default' }
        return <Tag color={info.color}>{info.label}</Tag>
      },
    },
    {
      title: '备注',
      dataIndex: 'remarks',
      width: 180,
      ellipsis: true,
      render: (v: string) => v || '--',
    },
    {
      title: '创建时间',
      dataIndex: 'createdAt',
      width: 170,
      sorter: true,
      sortOrder: sortBy === 'createdAt' ? sortOrder : undefined,
      render: (v: string) => formatDateTime(v),
    },
    {
      title: '创建人',
      dataIndex: 'createdBy',
      width: 120,
      render: (v: string) => v || '--',
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      width: 170,
      sorter: true,
      sortOrder: sortBy === 'updatedAt' ? sortOrder : undefined,
      render: (v: string) => formatDateTime(v),
    },
    {
      title: '更新人',
      dataIndex: 'updatedBy',
      width: 120,
      render: (v: string) => v || '--',
    },
    {
      title: '操作',
      key: 'action',
      fixed: 'right',
      width: 180,
      render: (_, record) => (
        <Space size={0}>
          <Button
            type="link"
            onClick={() => navigate(`/pos-admin/invoice-detail/${record.invoiceGUID}`)}
          >
            查看
          </Button>
          {isAdmin && (
            <Button
              type="link"
              onClick={() => navigate(`/pos-admin/local-supplier-invoices/${record.invoiceGUID}`)}
            >
              编辑
            </Button>
          )}
          {isAdmin && (
            <Popconfirm
              title="确认删除该进货单吗？"
              description="删除后无法恢复"
              okText="删除"
              cancelText="取消"
              okButtonProps={{ danger: true }}
              onConfirm={() => void handleDelete(record.invoiceGUID)}
            >
              <Button type="link" danger>
                删除
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <Card
      title="分店进货单"
      styles={{ body: { padding: 0 } }}
      extra={
        <Space>
          {isAdmin && (
            <Button
              icon={<SendOutlined />}
              disabled={!selectedRowKeys.length}
              loading={pushing}
              onClick={handlePushToHq}
            >
              推送到HQ ({selectedRowKeys.length})
            </Button>
          )}
          {isAdmin && (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => setCreateVisible(true)}
            >
              新建进货单
            </Button>
          )}
        </Space>
      }
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
        <div ref={toolbarRef} style={{ padding: 16 }}>
          <Space wrap>
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="选择分店"
              style={{ width: 200 }}
              value={storeCode}
              onChange={(v) => {
                setStoreCode(v)
              }}
              options={storeOptions}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="选择供应商"
              style={{ width: 200 }}
              value={supplierCode}
              onChange={(v) => {
                setSupplierCode(v)
              }}
              options={supplierOptions}
            />
            <Input
              allowClear
              placeholder="随货单号"
              style={{ width: 180 }}
              value={invoiceNo}
              onChange={(e) => setInvoiceNo(e.target.value)}
            />
            <Input
              allowClear
              placeholder="商品关键词"
              style={{ width: 180 }}
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
            />
            <Button type="primary" onClick={handleSearch}>
              查询
            </Button>
            <Button onClick={handleReset}>重置</Button>
          </Space>
        </div>

        <div style={{ flex: 1, minHeight: 0 }}>
          <Table
            rowKey="invoiceGUID"
            loading={loading}
            dataSource={data}
            columns={columns}
            pagination={false}
            scroll={{ x: 2200, y: tableScrollY }}
            rowSelection={
              isAdmin
                ? {
                    selectedRowKeys,
                    onChange: (keys) => setSelectedRowKeys(keys),
                  }
                : undefined
            }
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            onChange={(_pagination, _filters, sorter) => {
              const s = Array.isArray(sorter) ? sorter[0] : sorter
              const field = s?.field || s?.column?.dataIndex
              const order = s?.order as 'ascend' | 'descend' | undefined
              if (field && order) {
                setSortBy(String(field))
                setSortOrder(order)
              } else {
                setSortBy('createdAt')
                setSortOrder('descend')
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
        title="新建进货单"
        confirmLoading={creating}
        onCancel={() => {
          setCreateVisible(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreate()}
      >
        <Form form={createForm} layout="vertical">
          <Form.Item
            name="storeCode"
            label="分店"
            rules={[{ required: true, message: '请选择分店' }]}
          >
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="请选择分店"
              options={storeOptions}
            />
          </Form.Item>
          <Form.Item
            name="supplierCode"
            label="供应商"
            rules={[{ required: true, message: '请选择供应商' }]}
          >
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="请选择供应商"
              options={supplierOptions}
            />
          </Form.Item>
          <Form.Item
            name="invoiceNo"
            label="随货单号"
            validateTrigger={['onBlur']}
            rules={[
              { required: true, message: '请输入随货单号' },
              {
                validator: async (_, value) => {
                  if (!value?.trim()) return
                  try {
                    const result = await checkInvoiceNoExists({ invoiceNo: value.trim() })
                    if (result.exists) {
                      throw new Error('该随货单号已存在')
                    }
                  } catch (err) {
                    if (err instanceof Error && err.message === '该随货单号已存在') {
                      throw err
                    }
                  }
                },
              },
            ]}
          >
            <Input placeholder="请输入随货单号" />
          </Form.Item>
          <Form.Item name="orderDate" label="订单日期">
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="inboundDate" label="入库日期">
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="remarks" label="备注">
            <Input.TextArea rows={3} placeholder="请输入备注" />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}
