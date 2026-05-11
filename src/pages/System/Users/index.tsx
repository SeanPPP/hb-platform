import { EditOutlined, EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  List,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Transfer,
  Tree,
  Typography,
  message,
} from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { ColumnsType } from 'antd/es/table'
import type { Key } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import {
  assignRolesToUser,
  assignStoresToUser,
  getUserByGuid,
  getUserRoles,
  getUserStores,
  getUsers,
  updateUser,
} from '../../../services/userService'
import { getActiveRoles } from '../../../services/roleService'
import { getPermissions, getRolePermissions } from '../../../services/roleService'
import { getStores } from '../../../services/storeService'
import type { UpdateUserDto, UserDetailDto, UserDto, UserStoreDto } from '../../../types/user'
import type { RoleOptionDto, PermissionCategoryDto } from '../../../types/role'
import type { StoreDto } from '../../../types/store'
import { getRoleColor, getStoreColor } from '../../../utils/userTableColors'

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
  const [editTab, setEditTab] = useState('info')
  const [form] = Form.useForm<UpdateUserDto>()

  const [allRoles, setAllRoles] = useState<RoleOptionDto[]>([])
  const [roleTargetKeys, setRoleTargetKeys] = useState<string[]>([])
  const [roleLoading, setRoleLoading] = useState(false)
  const [roleSaving, setRoleSaving] = useState(false)

  const [allStores, setAllStores] = useState<StoreDto[]>([])
  const [storeTargetKeys, setStoreTargetKeys] = useState<string[]>([])
  const [storeLoading, setStoreLoading] = useState(false)
  const [storeSaving, setStoreSaving] = useState(false)

  const [permCategories, setPermCategories] = useState<PermissionCategoryDto[]>([])
  const [rolePermMap, setRolePermMap] = useState<Record<string, string[]>>({})
  const [permLoading, setPermLoading] = useState(false)

  const sortedStores = useMemo(
    () => [...allStores].sort((a, b) => a.storeName.localeCompare(b.storeName)),
    [allStores],
  )

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

  const loadRoleData = async (userGuid: string) => {
    setRoleLoading(true)
    try {
      const [roles, userRoles] = await Promise.all([getActiveRoles(), getUserRoles(userGuid)])
      setAllRoles(roles)
      setRoleTargetKeys(userRoles.map((item) => item.roleGUID))
    } catch (error) {
      console.error(error)
      message.error('加载角色数据失败')
    } finally {
      setRoleLoading(false)
    }
  }

  const loadStoreData = async (userGuid: string) => {
    setStoreLoading(true)
    try {
      const [stores, userStores] = await Promise.all([
        getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }),
        getUserStores(userGuid),
      ])
      setAllStores(stores.items)
      setStoreTargetKeys(userStores.map((item) => item.storeGUID))
    } catch (error) {
      console.error(error)
      message.error('加载分店数据失败')
    } finally {
      setStoreLoading(false)
    }
  }

  const loadPermData = async (userGuid: string) => {
    setPermLoading(true)
    try {
      const [categories, userRoles] = await Promise.all([getPermissions(), getUserRoles(userGuid)])
      setPermCategories(categories)
      const map: Record<string, string[]> = {}
      await Promise.all(
        userRoles.map(async (role) => {
          try {
            const perms = await getRolePermissions(role.roleGUID)
            map[role.roleName] = perms
          } catch {
            map[role.roleName] = []
          }
        }),
      )
      setRolePermMap(map)
    } catch (error) {
      console.error(error)
      message.error('加载权限数据失败')
    } finally {
      setPermLoading(false)
    }
  }

  const handleEdit = async (record: UserDto) => {
    setEditOpen(true)
    setEditLoading(true)
    setEditingUser(null)
    setEditTab('info')
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
      void loadRoleData(record.userGUID)
      void loadStoreData(record.userGUID)
      void loadPermData(record.userGUID)
    } catch (error) {
      console.error(error)
      message.error('加载用户编辑数据失败')
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingUser) return
    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateUser(editingUser.userGUID, values)
      message.success('用户信息已更新')
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        setDetailUser(updated)
      }
      void loadData(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error('更新用户失败')
    } finally {
      setEditLoading(false)
    }
  }

  const handleSaveRoles = async () => {
    if (!editingUser) return
    setRoleSaving(true)
    try {
      await assignRolesToUser(editingUser.userGUID, { roleGuids: roleTargetKeys })
      message.success('角色分配成功')
      void loadData(page, pageSize)
      void loadPermData(editingUser.userGUID)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) setDetailUser(updated)
    } catch (error) {
      console.error(error)
      message.error('角色分配失败')
    } finally {
      setRoleSaving(false)
    }
  }

  const handleSaveStores = async () => {
    if (!editingUser) return
    setStoreSaving(true)
    try {
      await assignStoresToUser(
        editingUser.userGUID,
        storeTargetKeys.map((storeGUID) => ({
          storeGUID,
          accessLevel: 'ReadWrite',
          isPrimary: false,
        })),
      )
      message.success('分店分配成功')
      void loadData(page, pageSize)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        void reloadUserDetail(updated.userGUID)
      }
    } catch (error) {
      console.error(error)
      message.error('分店分配失败')
    } finally {
      setStoreSaving(false)
    }
  }

  const allPermSet = useMemo(() => {
    const set = new Set<string>()
    Object.values(rolePermMap).forEach((perms) => perms.forEach((p) => set.add(p)))
    return set
  }, [rolePermMap])

  const permTreeData = useMemo(() => {
    return permCategories.map((cat) => ({
      key: cat.category,
      title: <strong>{cat.displayName}</strong>,
      children: cat.permissions.map((p) => ({
        key: p.name,
        title: (
          <Space size={4}>
            <span>{p.displayName}</span>
            {Object.entries(rolePermMap)
              .filter(([, perms]) => perms.includes(p.name))
              .map(([roleName]) => (
                <Tag key={roleName} color={getRoleColor(roleName)} style={{ fontSize: 11, lineHeight: '18px', padding: '0 4px' }}>
                  {roleName}
                </Tag>
              ))}
          </Space>
        ),
      })),
    }))
  }, [permCategories, rolePermMap])

  const checkedPermKeys = useMemo(() => {
    return permCategories.flatMap((cat) =>
      cat.permissions.filter((p) => allPermSet.has(p.name)).map((p) => p.name),
    )
  }, [permCategories, allPermSet])

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
        if (!stores.length) return '--'
        return (
          <Space wrap size={[4, 4]}>
            {stores.slice(0, 2).map((store) => (
              <Tag key={store} color={getStoreColor(store)}>{store}</Tag>
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

  const editTabItems = [
    {
      key: 'info',
      label: '基本信息',
      children: (
        <Spin spinning={editLoading}>
          <Form form={form} layout="vertical" style={{ maxWidth: 480 }}>
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
            <Form.Item>
              <Button type="primary" loading={editLoading} onClick={() => void handleEditSubmit()}>
                保存基本信息
              </Button>
            </Form.Item>
          </Form>
        </Spin>
      ),
    },
    {
      key: 'roles',
      label: (
        <HasPermission code={P.Users.ManageRoles} fallback={<span>角色</span>}>
          <span>角色</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageRoles} fallback={<Typography.Text type="secondary">无权限管理角色</Typography.Text>}>
          <Spin spinning={roleLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                当前用户已分配 <strong>{roleTargetKeys.length}</strong> 个角色
              </Typography.Text>
            </div>
            <Transfer
              dataSource={allRoles.map((role) => ({
                key: role.roleGUID,
                title: role.roleName,
                description: role.description || '',
              }))}
              targetKeys={roleTargetKeys}
              onChange={(nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
                setRoleTargetKeys(nextTargetKeys.map(String))
              }}
              render={(item) => item.title}
              titles={['可选角色', '已分配角色']}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={roleSaving} onClick={() => void handleSaveRoles()}>
                保存角色分配
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'stores',
      label: (
        <HasPermission code={P.Users.ManageStores} fallback={<span>分店</span>}>
          <span>分店</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageStores} fallback={<Typography.Text type="secondary">无权限管理分店</Typography.Text>}>
          <Spin spinning={storeLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                当前用户已关联 <strong>{storeTargetKeys.length}</strong> 个分店
              </Typography.Text>
            </div>
            <Transfer
              dataSource={sortedStores.map((store) => ({
                key: store.storeGUID,
                title: `${store.storeName} (${store.storeCode})`,
                description: store.address || '',
              }))}
              targetKeys={storeTargetKeys}
              onChange={(nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
                setStoreTargetKeys(nextTargetKeys.map(String))
              }}
              render={(item) => item.title}
              titles={['可选分店', '已分配分店']}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={storeSaving} onClick={() => void handleSaveStores()}>
                保存分店分配
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'permissions',
      label: '权限',
      children: (
        <Spin spinning={permLoading}>
          <div style={{ marginBottom: 12 }}>
            <Typography.Text type="secondary">
              以下权限通过角色继承获得，共 <strong>{allPermSet.size}</strong> 项。
              权限标签表示来源角色。
            </Typography.Text>
          </div>
          {permCategories.length === 0 ? (
            <Typography.Text type="secondary">暂无权限数据</Typography.Text>
          ) : (
            <Tree
              treeData={permTreeData}
              checkedKeys={checkedPermKeys}
              checkable
              selectable={false}
              defaultExpandAll
              style={{ background: '#fafafa', padding: 12, borderRadius: 8 }}
            />
          )}
        </Spin>
      ),
    },
  ]

  return (
    <PageContainer title="用户管理" subtitle="管理用户的基本信息、角色、分店和权限。">
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

      <Drawer
        title={editingUser ? `编辑用户 - ${editingUser.username}` : '编辑用户'}
        width={860}
        open={editOpen}
        onClose={() => {
          setEditOpen(false)
          setEditingUser(null)
          form.resetFields()
        }}
        destroyOnHidden
      >
        <Tabs activeKey={editTab} onChange={setEditTab} items={editTabItems} />
      </Drawer>
    </PageContainer>
  )
}
