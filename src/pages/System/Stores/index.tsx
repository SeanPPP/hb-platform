import { EditOutlined, EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
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
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState } from 'react'
import PageContainer from '../../../components/PageContainer'
import { getStoreByGuid, getStores, updateStore } from '../../../services/storeService'
import type { StoreDetailDto, StoreDto, UpdateStoreDto } from '../../../types/store'
import StoreUserManagement from './StoreUserManagement'

export default function SystemStoresPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<StoreDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailStore, setDetailStore] = useState<StoreDetailDto | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingStore, setEditingStore] = useState<StoreDetailDto | null>(null)
  const [storeUserOpen, setStoreUserOpen] = useState(false)
  const [storeUserTarget, setStoreUserTarget] = useState<StoreDto | null>(null)
  const [form] = Form.useForm<UpdateStoreDto>()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getStores({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error('加载分店列表失败')
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

  const handleViewDetail = async (record: StoreDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailStore(null)
    try {
      const detail = await getStoreByGuid(record.storeGUID)
      setDetailStore(detail)
    } catch (error) {
      console.error(error)
      message.error('加载分店详情失败')
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
      message.error('加载分店编辑数据失败')
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
      message.success('分店信息已更新')
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
      message.error('更新分店失败')
    } finally {
      setEditLoading(false)
    }
  }

  const handleOpenStoreUsers = (store: StoreDto) => {
    setStoreUserTarget(store)
    setStoreUserOpen(true)
  }

  const columns: ColumnsType<StoreDto> = [
    { title: '分店名称', dataIndex: 'storeName', width: 240 },
    { title: '分店编码', dataIndex: 'storeCode', width: 140 },
    { title: '品牌', dataIndex: 'brandName', width: 180, render: (value) => value || '--' },
    { title: '电话', dataIndex: 'contactPhone', width: 160, render: (value) => value || '--' },
    {
      title: '相关用户数',
      dataIndex: 'totalUsers',
      width: 120,
      render: (value: number | undefined, record) => (
        <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(record)}>
          {value ?? 0}
        </Button>
      ),
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 160,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            详情
          </Button>
          <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
            编辑
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title="分店管理"
      subtitle="分店详情和编辑改为列表页内弹窗打开，交互方式与老版本保持一致。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索分店名称 / 编码"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 260 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize)}>
            查询
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize)}>
            刷新
          </Button>
        </Space>

        <Table
          rowKey="storeGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 980 }}
          pagination={{
            current: page,
            pageSize,
            total,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>

      <Drawer
        title={detailStore ? `分店详情 - ${detailStore.storeCode}` : '分店详情'}
        width={860}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailStore(null)
        }}
        destroyOnHidden
        extra={
          detailStore ? (
            <Button type="primary" onClick={() => handleOpenStoreUsers(detailStore)}>
              管理用户
            </Button>
          ) : null
        }
      >
        {detailLoading ? (
          <Typography.Text type="secondary">正在加载分店详情...</Typography.Text>
        ) : !detailStore ? (
          <Typography.Text type="danger">未找到分店信息</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label="分店名称">{detailStore.storeName}</Descriptions.Item>
              <Descriptions.Item label="分店编码">{detailStore.storeCode}</Descriptions.Item>
              <Descriptions.Item label="品牌名称">{detailStore.brandName || '--'}</Descriptions.Item>
              <Descriptions.Item label="ABN">{detailStore.abn || '--'}</Descriptions.Item>
              <Descriptions.Item label="联系电话">{detailStore.contactPhone || '--'}</Descriptions.Item>
              <Descriptions.Item label="联系邮箱">{detailStore.contactEmail || '--'}</Descriptions.Item>
              <Descriptions.Item label="状态">
                <Tag color={detailStore.isActive ? 'success' : 'default'}>
                  {detailStore.isActive ? '启用' : '停用'}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="用户数">
                <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(detailStore)}>
                  {detailStore.activeUsers ?? 0} / {detailStore.totalUsers ?? 0}
                </Button>
              </Descriptions.Item>
              <Descriptions.Item label="地址" span={2}>
                {detailStore.address || '--'}
              </Descriptions.Item>
              <Descriptions.Item label="描述" span={2}>
                {detailStore.description || '--'}
              </Descriptions.Item>
              <Descriptions.Item label="创建时间">{detailStore.createdAt}</Descriptions.Item>
              <Descriptions.Item label="更新时间">{detailStore.updatedAt}</Descriptions.Item>
            </Descriptions>
          </Space>
        )}
      </Drawer>

      <Modal
        title={editingStore ? `编辑分店 - ${editingStore.storeCode}` : '编辑分店'}
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
          <Form.Item label="分店名称" name="storeName" rules={[{ required: true, message: '请输入分店名称' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="分店编码" name="storeCode" rules={[{ required: true, message: '请输入分店编码' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="品牌名称" name="brandName">
            <Input />
          </Form.Item>
          <Form.Item label="联系电话" name="contactPhone">
            <Input />
          </Form.Item>
          <Form.Item label="联系邮箱" name="contactEmail" rules={[{ type: 'email', message: '邮箱格式不正确' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="ABN" name="abn">
            <Input />
          </Form.Item>
          <Form.Item label="地址" name="address">
            <Input.TextArea rows={3} />
          </Form.Item>
          <Form.Item label="描述" name="description">
            <Input.TextArea rows={3} />
          </Form.Item>
          <Form.Item label="状态" name="isActive" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="停用" />
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
