import { EditOutlined, EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  List,
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
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { getUserByGuid, getUserStores, getUsers, updateUser } from '../../../services/userService'
import type { UpdateUserDto, UserDetailDto, UserDto, UserStoreDto } from '../../../types/user'
import { getRoleColor, getStoreColor } from '../../../utils/userTableColors'
import UserRoleAssignment from './UserRoleAssignment'
import UserStoreAssignment from './UserStoreAssignment'

export default function SystemUsersPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<UserDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailUser, setDetailUser] = useState<UserDetailDto | null>(null)
  const [detailStores, setDetailStores] = useState<UserStoreDto[]>([])
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingUser, setEditingUser] = useState<UserDetailDto | null>(null)
  const [roleAssignOpen, setRoleAssignOpen] = useState(false)
  const [storeAssignOpen, setStoreAssignOpen] = useState(false)
  const [form] = Form.useForm<UpdateUserDto>()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getUsers({
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
      message.error('加载用户列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const reloadUserDetail = async (userGuid: string) => {
    const [detail, stores] = await Promise.all([getUserByGuid(userGuid), getUserStores(userGuid).catch(() => [])])
    setDetailUser(detail)
    setDetailStores(stores)
    return detail
  }

  const reloadEditingUser = async (userGuid: string) => {
    const detail = await getUserByGuid(userGuid)
    setEditingUser(detail)
    return detail
  }

  const handleViewDetail = async (record: UserDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailUser(null)
    setDetailStores([])
    try {
      const [detail, stores] = await Promise.all([
        getUserByGuid(record.userGUID),
        getUserStores(record.userGUID).catch(() => []),
      ])
      setDetailUser(detail)
      setDetailStores(stores)
    } catch (error) {
      console.error(error)
      message.error('加载用户详情失败')
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const handleEdit = async (record: UserDto) => {
    setEditOpen(true)
    setEditLoading(true)
    setEditingUser(null)
    form.resetFields()
    try {
      const detail = await getUserByGuid(record.userGUID)
      setEditingUser(detail)
      form.setFieldsValue({
        username: detail.username,
        email: detail.email,
        fullName: detail.fullName,
        isActive: detail.isActive,
      })
    } catch (error) {
      console.error(error)
      message.error('加载用户编辑数据失败')
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingUser) {
      return
    }

    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateUser(editingUser.userGUID, values)
      message.success('用户信息已更新')
      setEditOpen(false)
      setEditingUser(updated)
      form.resetFields()
      if (detailUser?.userGUID === updated.userGUID) {
        setDetailUser(updated)
      }
      void loadData(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error('更新用户失败')
    } finally {
      setEditLoading(false)
    }
  }

  const columns: ColumnsType<UserDto> = [
    { title: '用户名', dataIndex: 'username', width: 180 },
    { title: '姓名', dataIndex: 'fullName', width: 160, render: (value) => value || '--' },
    { title: '邮箱', dataIndex: 'email', width: 220 },
    {
      title: '角色',
      dataIndex: 'roleNames',
      width: 220,
      render: (value: string[]) =>
        value?.length ? value.map((item) => <Tag key={item} color={getRoleColor(item)}>{item}</Tag>) : '--',
    },
    {
      title: '关联分店',
      dataIndex: 'storeNames',
      width: 240,
      render: (value: string[]) => {
        const stores = [...(value || [])].sort((left, right) => left.localeCompare(right))
        if (!stores.length) {
          return '--'
        }

        return (
          <Space wrap size={[4, 4]}>
            {stores.slice(0, 2).map((store) => (
              <Tag key={store} color={getStoreColor(store)}>
                {store}
              </Tag>
            ))}
            {stores.length > 2 ? <Tag>+{stores.length - 2}</Tag> : null}
          </Space>
        )
      },
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
      fixed: 'right',
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            详情
          </Button>
          <HasPermission code={P.Users.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              编辑
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title="用户管理"
      subtitle="用户详情和编辑改为列表页内弹窗打开，保留现有列表分页与权限体系。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索用户名 / 姓名 / 邮箱"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 280 }}
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
          rowKey="userGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 980 }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>

      <Drawer
        title={detailUser ? `用户详情 - ${detailUser.username}` : '用户详情'}
        width={820}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailUser(null)
          setDetailStores([])
        }}
        destroyOnHidden
      >
        {detailLoading ? (
          <Typography.Text type="secondary">正在加载用户详情...</Typography.Text>
        ) : !detailUser ? (
          <Typography.Text type="danger">未找到用户信息</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label="用户名">{detailUser.username}</Descriptions.Item>
              <Descriptions.Item label="姓名">{detailUser.fullName || '--'}</Descriptions.Item>
              <Descriptions.Item label="邮箱">{detailUser.email}</Descriptions.Item>
              <Descriptions.Item label="状态">
                <Tag color={detailUser.isActive ? 'success' : 'default'}>
                  {detailUser.isActive ? '启用' : '停用'}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="角色" span={2}>
                <Space wrap>
                  {detailUser.roleNames?.length ? detailUser.roleNames.map((item) => <Tag key={item}>{item}</Tag>) : '--'}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="创建时间">{detailUser.createdAt}</Descriptions.Item>
              <Descriptions.Item label="更新时间">{detailUser.updatedAt}</Descriptions.Item>
            </Descriptions>

            <Card title="关联分店" size="small">
              <List
                dataSource={detailStores}
                locale={{ emptyText: '暂无关联分店' }}
                renderItem={(item) => (
                  <List.Item>
                    <Space>
                      <Typography.Text strong>{item.storeName}</Typography.Text>
                      <Tag>{item.storeCode}</Tag>
                      {item.isPrimary ? <Tag color="processing">主分店</Tag> : null}
                    </Space>
                  </List.Item>
                )}
              />
            </Card>
          </Space>
        )}
      </Drawer>

      <Modal
        title={editingUser ? `编辑用户 - ${editingUser.username}` : '编辑用户'}
        open={editOpen}
        onCancel={() => {
          setEditOpen(false)
          setEditingUser(null)
          form.resetFields()
        }}
        onOk={() => void handleEditSubmit()}
        confirmLoading={editLoading}
        width={640}
        destroyOnHidden
        footer={(_, { OkBtn, CancelBtn }) => (
          <Space style={{ width: '100%', justifyContent: 'space-between' }}>
            <Space>
              <HasPermission code={P.Users.ManageRoles}>
                <Button disabled={!editingUser} onClick={() => setRoleAssignOpen(true)}>
                  分配角色
                </Button>
              </HasPermission>
              <HasPermission code={P.Users.ManageStores}>
                <Button disabled={!editingUser} onClick={() => setStoreAssignOpen(true)}>
                  分配分店
                </Button>
              </HasPermission>
            </Space>
            <Space>
              <CancelBtn />
              <OkBtn />
            </Space>
          </Space>
        )}
      >
        <Form form={form} layout="vertical">
          <Form.Item label="用户名" name="username" rules={[{ required: true, message: '请输入用户名' }]}>
            <Input />
          </Form.Item>
          <Form.Item
            label="邮箱"
            name="email"
            rules={[
              { required: true, message: '请输入邮箱' },
              { type: 'email', message: '邮箱格式不正确' },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item label="姓名" name="fullName">
            <Input />
          </Form.Item>
          <Form.Item label="状态" name="isActive" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="停用" />
          </Form.Item>
        </Form>
      </Modal>

      <UserRoleAssignment
        open={roleAssignOpen}
        user={editingUser}
        onClose={() => setRoleAssignOpen(false)}
        onSuccess={() => {
          if (!editingUser) {
            return
          }
          void reloadEditingUser(editingUser.userGUID)
          if (detailUser?.userGUID === editingUser.userGUID) {
            void reloadUserDetail(editingUser.userGUID)
          }
          void loadData(page, pageSize)
        }}
      />

      <UserStoreAssignment
        open={storeAssignOpen}
        user={editingUser}
        onClose={() => setStoreAssignOpen(false)}
        onSuccess={() => {
          if (!editingUser) {
            return
          }
          void reloadEditingUser(editingUser.userGUID)
          if (detailUser?.userGUID === editingUser.userGUID) {
            void reloadUserDetail(editingUser.userGUID)
          }
          void loadData(page, pageSize)
        }}
      />
    </PageContainer>
  )
}
