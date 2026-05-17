import { EditOutlined, EyeOutlined, LockOutlined, PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  List,
  Modal,
  Select,
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
import { useTranslation } from 'react-i18next'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import {
  assignRolesToUser,
  assignStoresToUser,
  createUser,
  getUserByGuid,
  getUserRoles,
  getUserStores,
  getUsers,
  updateUser,
  updateUserPassword,
} from '../../../services/userService'
import { getActiveRoles } from '../../../services/roleService'
import { getPermissions, getRolePermissions } from '../../../services/roleService'
import { getStores } from '../../../services/storeService'
import type { CreateUserDto, UpdateUserDto, UserDetailDto, UserDto, UserStoreDto } from '../../../types/user'
import type { RoleOptionDto, PermissionCategoryDto } from '../../../types/role'
import type { StoreDto } from '../../../types/store'
import { getRoleColor, getStoreColor } from '../../../utils/userTableColors'
import { hashPassword } from '../../../utils/password'

export default function SystemUsersPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<UserDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)

  const [selectedStoreGuid, setSelectedStoreGuid] = useState<string | undefined>(undefined)
  const [selectedRoleGuid, setSelectedRoleGuid] = useState<string | undefined>(undefined)

  const [sortBy, setSortBy] = useState<string | undefined>(undefined)
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | null>(null)

  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [roleOptions, setRoleOptions] = useState<{ label: string; value: string }[]>([])

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

  const [resetPwdLoading, setResetPwdLoading] = useState(false)
  const [resetPwdOpen, setResetPwdOpen] = useState(false)
  const [resetPwdForm] = Form.useForm<{ newPassword: string }>()

  const [createOpen, setCreateOpen] = useState(false)
  const [createLoading, setCreateLoading] = useState(false)
  const [createTab, setCreateTab] = useState('info')
  const [createForm] = Form.useForm<CreateUserDto & { confirmPassword: string }>()

  const [createRoleTargetKeys, setCreateRoleTargetKeys] = useState<string[]>([])
  const [createRoleLoading, setCreateRoleLoading] = useState(false)

  const [createStoreTargetKeys, setCreateStoreTargetKeys] = useState<string[]>([])
  const [createStoreLoading, setCreateStoreLoading] = useState(false)

  const sortedStores = useMemo(
    () => [...allStores].sort((a, b) => a.storeName.localeCompare(b.storeName)),
    [allStores],
  )

  const loadData = async (nextPage = page, nextPageSize = pageSize, currentSortBy?: string, currentSortOrder?: 'ascend' | 'descend' | null) => {
    setLoading(true)
    try {
      const result = await getUsers({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
        storeGuid: selectedStoreGuid,
        roleGuid: selectedRoleGuid,
        sortBy: currentSortBy || undefined,
        sortDirection: currentSortOrder === 'ascend' ? 'asc' : currentSortOrder === 'descend' ? 'desc' : undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadListFailed', '加载用户列表失败'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize, undefined, undefined)
  }, [])

  useEffect(() => {
    void (async () => {
      try {
        const [storeResult, roles] = await Promise.all([
          getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }),
          getActiveRoles(),
        ])
        setStoreOptions(storeResult.items.map((s) => ({
          label: `${s.storeName} (${s.storeCode})`,
          value: s.storeGUID,
        })))
        setRoleOptions(roles.map((r) => ({ label: r.roleName, value: r.roleGUID })))
      } catch (error) {
        console.error(error)
      }
    })()
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
      message.error(t('system.users.loadDetailFailed', '加载用户详情失败'))
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
      message.error(t('system.users.loadRolesFailed', '加载角色数据失败'))
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
      message.error(t('system.users.loadStoresFailed', '加载分店数据失败'))
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
      message.error(t('system.users.loadPermsFailed', '加载权限数据失败'))
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
      message.error(t('system.users.loadEditFailed', '加载用户编辑数据失败'))
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
      message.success(t('system.users.updateSuccess', '用户信息已更新'))
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        setDetailUser(updated)
      }
      void loadData(page, pageSize, sortBy, sortOrder)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.updateFailed', '更新用户失败'))
    } finally {
      setEditLoading(false)
    }
  }

  const handleSaveRoles = async () => {
    if (!editingUser) return
    setRoleSaving(true)
    try {
      await assignRolesToUser(editingUser.userGUID, { roleGuids: roleTargetKeys })
      message.success(t('system.users.roleAssignSuccess', '角色分配成功'))
      void loadData(page, pageSize, sortBy, sortOrder)
      void loadPermData(editingUser.userGUID)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) setDetailUser(updated)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.roleAssignFailed', '角色分配失败'))
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
      message.success(t('system.users.storeAssignSuccess', '分店分配成功'))
      void loadData(page, pageSize, sortBy, sortOrder)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        void reloadUserDetail(updated.userGUID)
      }
    } catch (error) {
      console.error(error)
      message.error(t('system.users.storeAssignFailed', '分店分配失败'))
    } finally {
      setStoreSaving(false)
    }
  }

  const handleResetPassword = async () => {
    if (!editingUser) return
    try {
      const values = await resetPwdForm.validateFields()
      setResetPwdLoading(true)
      await updateUserPassword(editingUser.userGUID, { newPassword: hashPassword(values.newPassword) })
      message.success(t('system.users.resetPasswordSuccess', '密码重置成功'))
      setResetPwdOpen(false)
      resetPwdForm.resetFields()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.resetPasswordFailed', '密码重置失败'))
    } finally {
      setResetPwdLoading(false)
    }
  }

  const handleOpenCreate = async () => {
    setCreateOpen(true)
    setCreateTab('info')
    createForm.resetFields()
    setCreateRoleTargetKeys([])
    setCreateStoreTargetKeys([])
    setCreateRoleLoading(true)
    setCreateStoreLoading(true)
    try {
      const [roles, stores] = await Promise.all([
        getActiveRoles(),
        getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }),
      ])
      setAllRoles(roles)
      setAllStores(stores.items)
    } catch (error) {
      console.error(error)
    } finally {
      setCreateRoleLoading(false)
      setCreateStoreLoading(false)
    }
  }

  const handleCreateSubmit = async () => {
    try {
      const values = await createForm.validateFields()
      setCreateLoading(true)
      const payload: CreateUserDto = {
        username: values.username,
        email: values.email,
        password: hashPassword(values.password),
        fullName: values.fullName,
        isActive: values.isActive ?? true,
        roleGuids: createRoleTargetKeys,
        storeGuids: createStoreTargetKeys,
      }
      await createUser(payload)
      message.success(t('system.users.createUserSuccess', '用户创建成功'))
      setCreateOpen(false)
      createForm.resetFields()
      setCreateRoleTargetKeys([])
      setCreateStoreTargetKeys([])
      void loadData(1, pageSize, sortBy, sortOrder)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.createUserFailed', '用户创建失败'))
    } finally {
      setCreateLoading(false)
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
    {
      title: '#',
      key: 'rowIndex',
      width: 60,
      align: 'center',
      render: (_: unknown, __: UserDto, index: number) => (page - 1) * pageSize + index + 1,
    },
    { title: t('system.users.username', '用户名'), dataIndex: 'username', width: 180, sorter: true, sortOrder: sortBy === 'username' ? sortOrder : null },
    { title: t('system.users.fullName', '姓名'), dataIndex: 'fullName', width: 160, sorter: true, sortOrder: sortBy === 'fullName' ? sortOrder : null, render: (value) => value || '--' },
    { title: t('system.users.email', '邮箱'), dataIndex: 'email', width: 220 },
    {
      title: t('system.users.roles', '角色'),
      dataIndex: 'roleNames',
      width: 220,
      sorter: true,
      sortOrder: sortBy === 'roleNames' ? sortOrder : null,
      render: (value: string[]) =>
        value?.length ? value.map((item) => <Tag key={item} color={getRoleColor(item)}>{item}</Tag>) : '--',
    },
    {
      title: t('system.users.linkedStores', '关联分店'),
      dataIndex: 'storeNames',
      width: 240,
      sorter: true,
      sortOrder: sortBy === 'storeNames' ? sortOrder : null,
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
      title: t('common.status', '状态'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active', '启用') : t('common.inactive', '停用')}</Tag>
      ),
    },
    {
      title: t('common.action', '操作'),
      key: 'action',
      width: 160,
      fixed: 'right',
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view', '详情')}
          </Button>
          <HasPermission code={P.Users.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              {t('common.edit', '编辑')}
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  const editTabItems = [
    {
      key: 'info',
      label: t('system.users.basicInfo', '基本信息'),
      children: (
        <Spin spinning={editLoading}>
          <Form form={form} layout="vertical" style={{ maxWidth: 480 }}>
            <Form.Item label={t('system.users.username', '用户名')} name="username" rules={[{ required: true, message: t('system.users.usernameRequired', '请输入用户名') }]}>
              <Input />
            </Form.Item>
            <Form.Item
              label={t('system.users.email', '邮箱')}
              name="email"
              rules={[
                { required: true, message: t('system.users.emailRequired', '请输入邮箱') },
                { type: 'email', message: t('system.users.emailInvalid', '邮箱格式不正确') },
              ]}
            >
              <Input />
            </Form.Item>
            <Form.Item label={t('system.users.fullName', '姓名')} name="fullName">
              <Input />
            </Form.Item>
            <Form.Item label={t('common.status', '状态')} name="isActive" valuePropName="checked">
              <Switch checkedChildren={t('common.active', '启用')} unCheckedChildren={t('common.inactive', '停用')} />
            </Form.Item>
            <Form.Item>
              <Button type="primary" loading={editLoading} onClick={() => void handleEditSubmit()}>
                {t('system.users.saveBasicInfo', '保存基本信息')}
              </Button>
            </Form.Item>
            <HasPermission code={P.Users.ResetPassword}>
              <div style={{ paddingTop: 16, borderTop: '1px solid #f0f0f0' }}>
                <Button icon={<LockOutlined />} onClick={() => setResetPwdOpen(true)} danger>
                    {t('system.users.resetPassword', '重置密码')}
                  </Button>
              </div>
            </HasPermission>
          </Form>
        </Spin>
      ),
    },
    {
      key: 'roles',
      label: (
        <HasPermission code={P.Users.ManageRoles} fallback={<span>{t('system.users.roles', '角色')}</span>}>
          <span>{t('system.users.roles', '角色')}</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageRoles} fallback={<Typography.Text type="secondary">{t('system.users.noRolePermission', '无权限管理角色')}</Typography.Text>}>
          <Spin spinning={roleLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                {t('system.users.assignedRoles', '当前用户已分配 {{count}} 个角色', { count: roleTargetKeys.length })}
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
              titles={[t('system.users.availableRoles', '可选角色'), t('system.users.assignedRolesLabel', '已分配角色')]}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={roleSaving} onClick={() => void handleSaveRoles()}>
                {t('system.users.saveRoleAssign', '保存角色分配')}
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'stores',
      label: (
        <HasPermission code={P.Users.ManageStores} fallback={<span>{t('system.users.stores', '分店')}</span>}>
          <span>{t('system.users.stores', '分店')}</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageStores} fallback={<Typography.Text type="secondary">{t('system.users.noStorePermission', '无权限管理分店')}</Typography.Text>}>
          <Spin spinning={storeLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                {t('system.users.assignedStores', '当前用户已关联 {{count}} 个分店', { count: storeTargetKeys.length })}
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
              titles={[t('system.users.availableStores', '可选分店'), t('system.users.assignedStoresLabel', '已分配分店')]}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={storeSaving} onClick={() => void handleSaveStores()}>
                {t('system.users.saveStoreAssign', '保存分店分配')}
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'permissions',
      label: t('system.users.permissions', '权限'),
      children: (
        <Spin spinning={permLoading}>
          <div style={{ marginBottom: 12 }}>
            <Typography.Text type="secondary">
              {t('system.users.permInheritDesc', '以下权限通过角色继承获得，共 {{count}} 项。权限标签表示来源角色。', { count: allPermSet.size })}
            </Typography.Text>
          </div>
          {permCategories.length === 0 ? (
            <Typography.Text type="secondary">{t('system.users.noPermData', '暂无权限数据')}</Typography.Text>
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
    <PageContainer title={t('menu.systemUsers', '用户管理')} subtitle={t('system.users.pageSubtitle', '管理用户的基本信息、角色、分店和权限。')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('system.users.searchPlaceholder', '搜索用户名 / 姓名 / 邮箱')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 280 }}
            allowClear
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('system.users.filterByStore', '按分店过滤')}
            style={{ width: 220 }}
            value={selectedStoreGuid}
            onChange={(value) => setSelectedStoreGuid(value)}
            options={storeOptions}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('system.users.filterByRole', '按角色过滤')}
            style={{ width: 180 }}
            value={selectedRoleGuid}
            onChange={(value) => setSelectedRoleGuid(value)}
            options={roleOptions}
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize, sortBy, sortOrder)}>
            {t('common.query', '查询')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize, sortBy, sortOrder)}>
            {t('common.refresh', '刷新')}
          </Button>
          <HasPermission code={P.Users.Create}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => void handleOpenCreate()}>
              {t('system.users.createUser', '创建用户')}
            </Button>
          </HasPermission>
        </Space>

        <Table
          rowKey="userGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 1060 }}
          onChange={(_pagination, _filters, sorter) => {
            const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
            const field = currentSorter?.field || currentSorter?.column?.dataIndex
            const order = currentSorter?.order as 'ascend' | 'descend' | undefined

            if (field && order) {
              setSortBy(String(field))
              setSortOrder(order)
              void loadData(1, pageSize, String(field), order)
            } else {
              setSortBy(undefined)
              setSortOrder(null)
              void loadData(1, pageSize, undefined, undefined)
            }
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize, sortBy, sortOrder)
            },
          }}
        />
      </Card>

      <Drawer
        title={detailUser ? t('system.users.userDetailTitle', '用户详情 - {{name}}', { name: detailUser.username }) : t('system.users.userDetail', '用户详情')}
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
          <Typography.Text type="secondary">{t('system.users.loadingDetail', '正在加载用户详情...')}</Typography.Text>
        ) : !detailUser ? (
          <Typography.Text type="danger">{t('system.users.userNotFound', '未找到用户信息')}</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label={t('system.users.username', '用户名')}>{detailUser.username}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.fullName', '姓名')}>{detailUser.fullName || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.email', '邮箱')}>{detailUser.email}</Descriptions.Item>
              <Descriptions.Item label={t('common.status', '状态')}>
                <Tag color={detailUser.isActive ? 'success' : 'default'}>
                  {detailUser.isActive ? t('common.active', '启用') : t('common.inactive', '停用')}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.roles', '角色')} span={2}>
                <Space wrap>
                  {detailUser.roleNames?.length ? detailUser.roleNames.map((item) => <Tag key={item}>{item}</Tag>) : '--'}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.permissions', '权限')} span={2}>
                <Space wrap>
                  {detailUser.permissions?.length
                    ? detailUser.permissions.map((p) => <Tag key={p} color="green">{p}</Tag>)
                    : '--'}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.createdAt', '创建时间')}>{detailUser.createdAt}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.updatedAt', '更新时间')}>{detailUser.updatedAt}</Descriptions.Item>
            </Descriptions>

            <Card title={t('system.users.linkedStores', '关联分店')} size="small">
              <List
                dataSource={detailStores}
                locale={{ emptyText: t('system.users.noLinkedStores', '暂无关联分店') }}
                renderItem={(item) => (
                  <List.Item>
                    <Space>
                      <Typography.Text strong>{item.storeName}</Typography.Text>
                      <Tag>{item.storeCode}</Tag>
                      {item.isPrimary ? <Tag color="processing">{t('system.users.primaryStore', '主分店')}</Tag> : null}
                    </Space>
                  </List.Item>
                )}
              />
            </Card>
          </Space>
        )}
      </Drawer>

      <Drawer
        title={editingUser ? t('system.users.editUserTitle', '编辑用户 - {{name}}', { name: editingUser.username }) : t('system.users.editUser', '编辑用户')}
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

      <Modal
        title={t('system.users.resetPassword', '重置密码')}
        open={resetPwdOpen}
        onCancel={() => {
          setResetPwdOpen(false)
          resetPwdForm.resetFields()
        }}
        onOk={() => void handleResetPassword()}
        confirmLoading={resetPwdLoading}
        destroyOnHidden
      >
        <Form form={resetPwdForm} layout="vertical">
          <Form.Item
            label={t('system.users.newPasswordLabel', '请输入新密码')}
            name="newPassword"
            rules={[
              { required: true, message: t('system.users.newPasswordRequired', '请输入新密码') },
              { min: 6, message: t('system.users.passwordMinLength', '密码至少6位') },
            ]}
          >
            <Input.Password placeholder={t('system.users.newPasswordPlaceholder', '输入新密码')} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={t('system.users.createUser', '创建用户')}
        width={860}
        open={createOpen}
        onCancel={() => {
          if (createLoading) return
          setCreateOpen(false)
          createForm.resetFields()
          setCreateRoleTargetKeys([])
          setCreateStoreTargetKeys([])
        }}
        footer={createTab === 'info' ? [
          <Button key="cancel" onClick={() => {
            setCreateOpen(false)
            createForm.resetFields()
            setCreateRoleTargetKeys([])
            setCreateStoreTargetKeys([])
          }}>
            {t('common.cancel', '取消')}
          </Button>,
          <Button key="submit" type="primary" loading={createLoading} onClick={() => void handleCreateSubmit()}>
            {t('common.create', '新建')}
          </Button>,
        ] : null}
        destroyOnHidden
      >
        <Tabs
          activeKey={createTab}
          onChange={setCreateTab}
          items={[
            {
              key: 'info',
              label: t('system.users.basicInfo', '基本信息'),
              children: (
                <Form form={createForm} layout="vertical" style={{ maxWidth: 480 }}>
                  <Form.Item
                    label={t('system.users.username', '用户名')}
                    name="username"
                    rules={[
                      { required: true, message: t('system.users.usernameRequired', '请输入用户名') },
                      { min: 3, max: 50, message: t('system.users.usernameLengthInvalid', '用户名长度3-50个字符') },
                    ]}
                  >
                    <Input />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.email', '邮箱')}
                    name="email"
                    rules={[
                      { required: true, message: t('system.users.emailRequired', '请输入邮箱') },
                      { type: 'email', message: t('system.users.emailInvalid', '邮箱格式不正确') },
                    ]}
                  >
                    <Input />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.password', '密码')}
                    name="password"
                    rules={[
                      { required: true, message: t('system.users.passwordRequired', '请输入密码') },
                      { min: 6, message: t('system.users.passwordMinLength', '密码至少6位') },
                    ]}
                  >
                    <Input.Password />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.confirmPassword', '确认密码')}
                    name="confirmPassword"
                    dependencies={['password']}
                    rules={[
                      { required: true, message: t('system.users.confirmPasswordRequired', '请确认密码') },
                      ({ getFieldValue }) => ({
                        validator(_, value) {
                          if (!value || getFieldValue('password') === value) return Promise.resolve()
                          return Promise.reject(new Error(t('system.users.confirmPasswordMismatch', '两次输入的密码不一致')))
                        },
                      }),
                    ]}
                  >
                    <Input.Password />
                  </Form.Item>
                  <Form.Item label={t('system.users.fullName', '姓名')} name="fullName">
                    <Input />
                  </Form.Item>
                  <Form.Item label={t('common.status', '状态')} name="isActive" valuePropName="checked" initialValue={true}>
                    <Switch checkedChildren={t('common.active', '启用')} unCheckedChildren={t('common.inactive', '停用')} />
                  </Form.Item>
                </Form>
              ),
            },
            {
              key: 'roles',
              label: t('system.users.roles', '角色'),
              children: (
                <Spin spinning={createRoleLoading}>
                  <div style={{ marginBottom: 12 }}>
                    <Typography.Text type="secondary">
                      {t('system.users.createSelectedRoles', '已选择 {{count}} 个角色', { count: createRoleTargetKeys.length })}
                    </Typography.Text>
                  </div>
                  <Transfer
                    dataSource={allRoles.map((role) => ({
                      key: role.roleGUID,
                      title: role.roleName,
                      description: role.description || '',
                    }))}
                    targetKeys={createRoleTargetKeys}
                    onChange={(nextTargetKeys: Key[]) => {
                      setCreateRoleTargetKeys(nextTargetKeys.map(String))
                    }}
                    render={(item) => item.title}
                    titles={[t('system.users.availableRoles', '可选角色'), t('system.users.assignedRolesLabel', '已分配角色')]}
                    listStyle={{ width: 320, height: 400 }}
                    showSearch
                  />
                </Spin>
              ),
            },
            {
              key: 'stores',
              label: t('system.users.stores', '分店'),
              children: (
                <Spin spinning={createStoreLoading}>
                  <div style={{ marginBottom: 12 }}>
                    <Typography.Text type="secondary">
                      {t('system.users.createSelectedStores', '已选择 {{count}} 个分店', { count: createStoreTargetKeys.length })}
                    </Typography.Text>
                  </div>
                  <Transfer
                    dataSource={sortedStores.map((store) => ({
                      key: store.storeGUID,
                      title: `${store.storeName} (${store.storeCode})`,
                      description: store.address || '',
                    }))}
                    targetKeys={createStoreTargetKeys}
                    onChange={(nextTargetKeys: Key[]) => {
                      setCreateStoreTargetKeys(nextTargetKeys.map(String))
                    }}
                    render={(item) => item.title}
                    titles={[t('system.users.availableStores', '可选分店'), t('system.users.assignedStoresLabel', '已分配分店')]}
                    listStyle={{ width: 320, height: 400 }}
                    showSearch
                  />
                </Spin>
              ),
            },
          ]}
        />
      </Modal>
    </PageContainer>
  )
}
