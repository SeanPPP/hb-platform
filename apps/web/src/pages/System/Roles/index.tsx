import {
  AppstoreOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  LockOutlined,
  PlusCircleOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  List,
  Modal,
  Popconfirm,
  Segmented,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { HasPermission, usePermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { useAuthStore } from '../../../store/auth'
import type { AccessControl } from '../../../types/auth'
import { P } from '../../../types/permissions'
import {
  buildExpoRoleMenuPreview,
  filterExpoRoutesByVisibility,
  type ExpoAppVisibleRoute,
  type ExpoAppDisplayTab,
  type ExpoMenuVisibilityFilter,
} from '../../../utils/expoRoleMenuPreview'
import { applyRolePermissionMutation, buildRolePreviewAccess, isImplicitAllRole } from '../../../utils/roleMenuPreview'
import {
  buildWebRoleMenuPreview,
  filterWebMenuNodesByVisibility,
  type WebMenuPreviewNode,
  type WebMenuVisibilityFilter,
} from '../../../utils/webMenuPreview'
import {
  assignPermissionsToRole,
  createRole,
  getRoleByGuid,
  getRolePermissionState,
  getRoles,
  updateRole,
} from '../../../services/roleService'
import type { CreateRoleDto, RoleDetailDto, RoleDto, RolePermissionStateDto, UpdateRoleDto } from '../../../types/role'
import RolePermissionManager from './RolePermissionManager'
import RoleUserManagement from './RoleUserManagement'

type ExpoDirectTabPreviewItem =
  | {
      type: 'route'
      key: string
      route: ExpoAppVisibleRoute
    }
  | {
      type: 'store'
      key: 'store'
      zhTitle: string
      enTitle: string
      children: ExpoAppVisibleRoute[]
    }

function toExpoDirectTabPreviewItems(displayTabs: ExpoAppDisplayTab[]): ExpoDirectTabPreviewItem[] {
  return displayTabs.map((item) => {
    if (item.type === 'store') {
      return item
    }
    return {
      type: 'route',
      key: item.key,
      route: item.route,
    }
  })
}

function countWebMenuPreviewNodes(items: WebMenuPreviewNode[]): number {
  return items.reduce((total, item) => total + 1 + countWebMenuPreviewNodes(item.children ?? []), 0)
}

export default function SystemRolesPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<RoleDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)

  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailRole, setDetailRole] = useState<RoleDetailDto | null>(null)
  const canManageRolePermissions = usePermission(P.Roles.ManagePermissions)

  const [createOpen, setCreateOpen] = useState(false)
  const [createLoading, setCreateLoading] = useState(false)
  const [createForm] = Form.useForm<CreateRoleDto>()

  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingRole, setEditingRole] = useState<RoleDetailDto | null>(null)
  const [editRoleGuid, setEditRoleGuid] = useState<string>('')
  const [form] = Form.useForm<UpdateRoleDto>()

  const [roleUserOpen, setRoleUserOpen] = useState(false)
  const [menuPreviewOpen, setMenuPreviewOpen] = useState(false)
  const [menuPreviewLoading, setMenuPreviewLoading] = useState(false)
  const [menuPreviewRole, setMenuPreviewRole] = useState<RoleDto | null>(null)
  const [menuPreviewAccess, setMenuPreviewAccess] = useState<AccessControl | null>(null)
  const [menuPreviewPermissionState, setMenuPreviewPermissionState] = useState<RolePermissionStateDto | null>(null)
  const [menuPreviewSavingKey, setMenuPreviewSavingKey] = useState<string | null>(null)
  const [webMenuVisibilityFilter, setWebMenuVisibilityFilter] = useState<WebMenuVisibilityFilter>('all')
  const [expoMenuVisibilityFilter, setExpoMenuVisibilityFilter] = useState<ExpoMenuVisibilityFilter>('all')
  const refreshCurrentUserSilently = useAuthStore((state) => state.refreshCurrentUserSilently)

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getRoles({
        page: nextPage,
        pageSize: nextPageSize,
        searchKeyword: keyword || undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const reloadRoleDetail = async (roleGuid: string) => {
    const detail = await getRoleByGuid(roleGuid)
    setDetailRole(detail)
    return detail
  }

  const reloadMenuPreviewPermissionState = async (roleGuid: string) => {
    const permissionState = await getRolePermissionState(roleGuid)
    setMenuPreviewPermissionState(permissionState)
    setMenuPreviewAccess(buildRolePreviewAccess(permissionState))
    return permissionState
  }

  const handleCreateOpen = () => {
    createForm.setFieldsValue({
      isActive: true,
    })
    setCreateOpen(true)
  }

  const handleCreateSubmit = async () => {
    try {
      const values = await createForm.validateFields()
      setCreateLoading(true)
      const created = await createRole({
        ...values,
        description: values.description?.trim() || undefined,
        roleName: values.roleName.trim(),
        isActive: values.isActive ?? true,
      })
      message.success(t('system.roles.createSuccess'))
      setCreateOpen(false)
      createForm.resetFields()
      await loadData(1, pageSize)
      if (canManageRolePermissions) {
        void handleEdit(created)
      }
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.roles.createFailed'))
    } finally {
      setCreateLoading(false)
    }
  }

  const handleViewDetail = async (record: RoleDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailRole(null)
    try {
      const detail = await getRoleByGuid(record.roleGUID)
      setDetailRole(detail)
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadDetailFailed'))
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const handleEdit = async (record: RoleDto) => {
    setEditOpen(true)
    setEditRoleGuid(record.roleGUID)
    setEditLoading(true)
    setEditingRole(null)
    form.resetFields()
    try {
      const detail = await getRoleByGuid(record.roleGUID)
      setEditingRole(detail)
      form.setFieldsValue({
        roleName: detail.roleName,
        description: detail.description,
        isActive: detail.isActive,
      })
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadEditFailed'))
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingRole) return
    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateRole(editingRole.roleGUID, values)
      message.success(t('system.roles.updateSuccess'))
      setEditOpen(false)
      setEditingRole(null)
      form.resetFields()
      if (detailRole?.roleGUID === updated.roleGUID) {
        setDetailRole((current) => (current ? { ...current, ...updated } : updated))
      }
      void loadData(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.roles.updateFailed'))
    } finally {
      setEditLoading(false)
    }
  }

  const handleOpenMenuPreview = async (record: RoleDto) => {
    setMenuPreviewOpen(true)
    setMenuPreviewLoading(true)
    setMenuPreviewRole(record)
    setMenuPreviewAccess(null)
    setMenuPreviewPermissionState(null)
    setWebMenuVisibilityFilter('all')
    setExpoMenuVisibilityFilter('all')
    try {
      await reloadMenuPreviewPermissionState(record.roleGUID)
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadMenuPreviewFailed', '加载菜单预览失败'))
      setMenuPreviewOpen(false)
    } finally {
      setMenuPreviewLoading(false)
    }
  }

  const handleMenuPermissionChange = async ({
    key,
    addPermissionCodes = [],
    removePermissionCodes = [],
  }: {
    key: string
    addPermissionCodes?: string[]
    removePermissionCodes?: string[]
  }) => {
    if (!menuPreviewRole || !menuPreviewPermissionState) return
    if (!canManageRolePermissions) {
      message.warning(t('system.roles.menuPermissionReadonlyTip', '当前账号没有维护角色权限的权限。'))
      return
    }
    if (isImplicitAllRole(menuPreviewPermissionState)) {
      message.info(t('system.roles.superAdminPermissionsReadOnly', '管理员默认拥有所有权限和菜单，无需在此处维护。'))
      return
    }

    const nextPermissions = applyRolePermissionMutation({
      currentPermissionCodes: menuPreviewPermissionState.explicitPermissionCodes,
      addPermissionCodes,
      removePermissionCodes,
    })

    setMenuPreviewSavingKey(key)
    try {
      await assignPermissionsToRole(menuPreviewRole.roleGUID, { permissions: nextPermissions })
      await reloadMenuPreviewPermissionState(menuPreviewRole.roleGUID)
      await refreshCurrentUserSilently()
      message.success(t('system.roles.menuPermissionSaved', '权限已更新'))
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.permSaveFailed', '保存权限失败'))
    } finally {
      setMenuPreviewSavingKey(null)
    }
  }

  const renderPermissionTags = (permissionCodes: string[]) => {
    if (!permissionCodes.length) {
      return <Tag>{t('system.roles.webMenuNoDirectPermission', '无直接权限')}</Tag>
    }

    return (
      <>
        <Tag color="green">{t('system.roles.webMenuAnyPermission', '任一权限满足即可')}</Tag>
        {permissionCodes.map((code) => (
          <Tag key={code}>{code}</Tag>
        ))}
      </>
    )
  }

  const renderMenuPermissionActions = (item: {
    key: string
    visible: boolean
    edit: {
      canAdd: boolean
      canRemove: boolean
      isReadOnly: boolean
      isFixed: boolean
      addPermissionCodes: string[]
      removePermissionCodes: string[]
    }
  }) => {
    if (item.edit.isFixed) {
      return (
        <Tag icon={<LockOutlined />} color="default">
          {t('system.roles.fixedMenuPermission', '固定入口')}
        </Tag>
      )
    }

    if (item.edit.isReadOnly) {
      return <Tag>{t('system.roles.readOnlyMenuPermission', '只读')}</Tag>
    }

    if (item.edit.canRemove) {
      return (
        <Popconfirm
          title={t('system.roles.removeMenuPermissionConfirm', '移除这些权限后该菜单可能不再显示，确定继续吗？')}
          okText={t('common.confirm')}
          cancelText={t('common.cancel')}
          onConfirm={() =>
            void handleMenuPermissionChange({
              key: item.key,
              removePermissionCodes: item.edit.removePermissionCodes,
            })
          }
        >
          <Button
            danger
            size="small"
            icon={<DeleteOutlined />}
            loading={menuPreviewSavingKey === item.key}
          >
            {t('system.roles.removeMenuPermission', '移除权限')}
          </Button>
        </Popconfirm>
      )
    }

    if (item.edit.canAdd) {
      return (
        <Button
          type="primary"
          size="small"
          icon={<PlusCircleOutlined />}
          loading={menuPreviewSavingKey === item.key}
          onClick={() =>
            void handleMenuPermissionChange({
              key: item.key,
              addPermissionCodes: item.edit.addPermissionCodes,
            })
          }
        >
          {t('system.roles.addMenuPermission', '添加权限')}
        </Button>
      )
    }

    return null
  }

  const toExpoMenuActionTarget = (item: ExpoAppVisibleRoute) => ({
    key: `expo-${item.routeName}`,
    visible: item.visible,
    edit: {
      canAdd: !item.visible && !item.readOnly && !item.locked && item.addPermissionCodes.length > 0,
      canRemove: item.visible && !item.readOnly && !item.locked && item.removePermissionCodes.length > 0,
      isReadOnly: item.readOnly,
      isFixed: item.locked,
      addPermissionCodes: item.addPermissionCodes,
      removePermissionCodes: item.removePermissionCodes,
    },
  })

  const renderWebMenuPreview = (items: WebMenuPreviewNode[], level = 0): ReactNode => {
    if (!items.length) {
      return <Empty description={t('system.roles.noVisibleMenus', '暂无可见菜单')} />
    }

    return (
      <List
        dataSource={items}
        renderItem={(item) => (
          <List.Item style={{ display: 'block', paddingLeft: level * 16 }}>
            <Space direction="vertical" size={6} style={{ width: '100%' }}>
              <Space wrap>
                <Typography.Text strong>{item.title}</Typography.Text>
                <Tag color="blue">{t('system.roles.webMenuPath', '路径')}: {item.path}</Tag>
                <Tag color={item.visible ? 'success' : 'default'}>
                  {item.visible
                    ? t('system.roles.menuPermissionVisible', '可见')
                    : t('system.roles.menuPermissionHidden', '未显示')}
                </Tag>
                {item.accessKey ? <Tag>{item.accessKey}</Tag> : null}
                {renderMenuPermissionActions(item)}
              </Space>
              <Space wrap size={8}>
                <Typography.Text type="secondary">
                  {t('system.roles.webMenuPermission', '对应权限')}:
                </Typography.Text>
                {item.permissionCodes.length ? renderPermissionTags(item.permissionCodes) : (
                  <>
                    {renderPermissionTags(item.permissionCodes)}
                    {item.children?.length ? (
                      <Typography.Text type="secondary">
                        {t('system.roles.webMenuVisibleByChildren', '由可见子菜单决定')}
                      </Typography.Text>
                    ) : null}
                  </>
                )}
              </Space>
              {item.children?.length ? renderWebMenuPreview(item.children, level + 1) : null}
            </Space>
          </List.Item>
        )}
      />
    )
  }

  const renderExpoRouteList = (items: ExpoAppVisibleRoute[], emptyText: string, editable = false) => {
    if (!items.length) {
      return <Empty description={emptyText} />
    }

    return (
      <List
        dataSource={items}
        renderItem={(item) => (
          <List.Item>
            <Space direction="vertical" size={4} style={{ width: '100%' }}>
              <Space wrap>
                <Typography.Text strong>{item.zhTitle}</Typography.Text>
                <Typography.Text type="secondary">{item.enTitle}</Typography.Text>
                <Tag color="blue">{item.routeName}</Tag>
                <Tag color={item.visible ? 'success' : 'default'}>
                  {item.visible
                    ? t('system.roles.menuPermissionVisible', '可见')
                    : t('system.roles.menuPermissionHidden', '未显示')}
                </Tag>
                <Tag>{item.icon}</Tag>
                {editable ? renderMenuPermissionActions(toExpoMenuActionTarget(item)) : null}
              </Space>
              <Space wrap size={8}>
                <Typography.Text type="secondary">{item.path}</Typography.Text>
                <Typography.Text type="secondary">
                  {t('system.roles.expoPermission', '权限')}:
                </Typography.Text>
                {renderPermissionTags(item.permissionCodes)}
              </Space>
            </Space>
          </List.Item>
        )}
      />
    )
  }

  const renderExpoDirectTabs = (items: ExpoDirectTabPreviewItem[]) => {
    if (!items.length) {
      return <Empty description={t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口')} />
    }

    return (
      <List
        dataSource={items}
        renderItem={(item) => {
          if (item.type === 'store') {
            return (
              <List.Item>
                <Space direction="vertical" size={4} style={{ width: '100%' }}>
                  <Space wrap>
                    <Typography.Text strong>{item.zhTitle}</Typography.Text>
                    <Typography.Text type="secondary">{item.enTitle}</Typography.Text>
                    <Tag color="purple">store</Tag>
                    <Tag>{t('system.roles.expoCollapsedMenu', '折叠菜单')}</Tag>
                  </Space>
                  <Typography.Text type="secondary">
                    {t('system.roles.expoStoreChildrenCount', '{{count}} 个门店子入口', { count: item.children.length })}
                  </Typography.Text>
                </Space>
              </List.Item>
            )
          }

          return (
            <List.Item>
              <Space direction="vertical" size={4} style={{ width: '100%' }}>
                <Space wrap>
                  <Typography.Text strong>{item.route.zhTitle}</Typography.Text>
                  <Typography.Text type="secondary">{item.route.enTitle}</Typography.Text>
                  <Tag color="blue">{item.route.routeName}</Tag>
                  <Tag>{item.route.icon}</Tag>
                </Space>
                <Space wrap size={8}>
                  <Typography.Text type="secondary">{item.route.path}</Typography.Text>
                  <Typography.Text type="secondary">
                    {t('system.roles.expoPermission', '权限')}:
                  </Typography.Text>
                  {renderPermissionTags(item.route.permissionCodes)}
                </Space>
              </Space>
            </List.Item>
          )
        }}
      />
    )
  }

  const columns: ColumnsType<RoleDto> = [
    { title: t('system.roles.roleName'), dataIndex: 'roleName', width: 220 },
    { title: t('column.description'), dataIndex: 'description', render: (value) => value || '--' },
    {
      title: t('column.status'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active') : t('common.inactive')}</Tag>
      ),
    },
    { title: t('system.roles.linkedUserCount'), dataIndex: 'userCount', width: 140 },
    {
      title: t('column.action'),
      key: 'action',
      width: 260,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view')}
          </Button>
          <Button type="link" icon={<AppstoreOutlined />} onClick={() => void handleOpenMenuPreview(record)}>
            {t('system.roles.menuPreview', '菜单预览')}
          </Button>
          <HasPermission code={P.Roles.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              {t('common.edit')}
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  const previewDesktopMenus = menuPreviewAccess
    ? buildWebRoleMenuPreview(menuPreviewAccess, (key, fallback) => (fallback ? t(key, fallback) : t(key)), {
        includeHidden: true,
        explicitPermissionCodes: menuPreviewPermissionState?.explicitPermissionCodes,
        readOnly: !canManageRolePermissions || !menuPreviewPermissionState || isImplicitAllRole(menuPreviewPermissionState),
      })
    : []
  const previewExpoMenu = menuPreviewAccess
    ? buildExpoRoleMenuPreview(menuPreviewAccess, undefined, {
        explicitPermissionCodes: menuPreviewPermissionState?.explicitPermissionCodes,
        readOnly: !canManageRolePermissions || !menuPreviewPermissionState || isImplicitAllRole(menuPreviewPermissionState),
      })
    : null
  const previewDesktopFilteredMenus = filterWebMenuNodesByVisibility(previewDesktopMenus, webMenuVisibilityFilter)
  const previewDesktopAllMenuCount = countWebMenuPreviewNodes(previewDesktopMenus)
  const previewDesktopVisibleMenuCount = countWebMenuPreviewNodes(
    filterWebMenuNodesByVisibility(previewDesktopMenus, 'visible'),
  )
  const previewDesktopHiddenMenuCount = countWebMenuPreviewNodes(
    filterWebMenuNodesByVisibility(previewDesktopMenus, 'hidden'),
  )
  const previewExpoDirectTabs = previewExpoMenu ? toExpoDirectTabPreviewItems(previewExpoMenu.displayTabs) : []
  const previewExpoFilteredRoutes = previewExpoMenu
    ? filterExpoRoutesByVisibility(previewExpoMenu.allRoutes, expoMenuVisibilityFilter)
    : []
  const previewExpoVisibleRouteCount = previewExpoMenu?.allRoutes.filter((route) => route.visible).length ?? 0
  const previewExpoHiddenRouteCount = previewExpoMenu?.allRoutes.filter((route) => !route.visible).length ?? 0
  const previewExpoAllRouteCount = previewExpoMenu?.allRoutes.length ?? 0

  return (
    <PageContainer title={t('system.roles.pageTitle')} subtitle={t('system.roles.pageSubtitle')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('system.roles.searchPlaceholder')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 260 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize)}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize)}>
            {t('common.refresh')}
          </Button>
          <HasPermission code={P.Roles.Create}>
            <Button type="primary" icon={<PlusOutlined />} onClick={handleCreateOpen}>
              {t('system.roles.createRole')}
            </Button>
          </HasPermission>
        </Space>

        <Table
          rowKey="roleGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
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
        title={detailRole ? t('system.roles.detailTitle', { name: detailRole.roleName }) : t('system.roles.detailTitleShort')}
        width={820}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailRole(null)
        }}
        destroyOnHidden
        extra={
          detailRole ? (
            <HasPermission code={P.Roles.ManageUsers}>
              <Button type="primary" onClick={() => setRoleUserOpen(true)}>
                {t('system.roles.manageUsers')}
              </Button>
            </HasPermission>
          ) : null
        }
      >
        {detailLoading ? (
          <Typography.Text type="secondary">{t('system.roles.loadingDetail')}</Typography.Text>
        ) : !detailRole ? (
          <Typography.Text type="danger">{t('system.roles.notFound')}</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label={t('system.roles.roleName')}>{detailRole.roleName}</Descriptions.Item>
              <Descriptions.Item label={t('column.status')}>
                <Tag color={detailRole.isActive ? 'success' : 'default'}>
                  {detailRole.isActive ? t('common.active') : t('common.inactive')}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('column.description')} span={2}>
                {detailRole.description || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.roles.linkedUserCount')}>{detailRole.userCount}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.updatedAt')}>{detailRole.updatedAt}</Descriptions.Item>
            </Descriptions>

            <Card title={t('system.roles.permissions')} size="small">
              <Space wrap>
                {detailRole.permissions?.length
                  ? detailRole.permissions.map((item) => <Tag key={item}>{item}</Tag>)
                  : t('system.roles.noPermissions')}
              </Space>
            </Card>

            <Card title={t('system.roles.linkedUsers')} size="small">
              <List
                dataSource={detailRole.users ?? []}
                locale={{ emptyText: t('system.roles.noLinkedUsers') }}
                renderItem={(item) => (
                  <List.Item>
                    <Space>
                      <Typography.Text strong>{item.username}</Typography.Text>
                      <Typography.Text type="secondary">{item.email}</Typography.Text>
                    </Space>
                  </List.Item>
                )}
              />
            </Card>
          </Space>
        )}
      </Drawer>

      <Drawer
        title={
          menuPreviewRole
            ? t('system.roles.menuPreviewTitle', { name: menuPreviewRole.roleName, defaultValue: `菜单预览 - ${menuPreviewRole.roleName}` })
            : t('system.roles.menuPreview', '菜单预览')
        }
        width={760}
        open={menuPreviewOpen}
        onClose={() => {
          setMenuPreviewOpen(false)
          setMenuPreviewRole(null)
          setMenuPreviewAccess(null)
          setMenuPreviewPermissionState(null)
          setMenuPreviewSavingKey(null)
          setWebMenuVisibilityFilter('all')
          setExpoMenuVisibilityFilter('all')
        }}
        loading={menuPreviewLoading}
        destroyOnHidden
      >
        {menuPreviewAccess && menuPreviewPermissionState ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered size="small" column={2}>
              <Descriptions.Item label={t('system.roles.roleName')}>{menuPreviewPermissionState.roleName}</Descriptions.Item>
              <Descriptions.Item label={t('system.roles.visiblePermissionCount', '有效权限数')}>
                {menuPreviewPermissionState.effectivePermissionCodes.length}
              </Descriptions.Item>
            </Descriptions>

            {isImplicitAllRole(menuPreviewPermissionState) ? (
              <Alert
                type="info"
                showIcon
                message={t('system.roles.superAdminMenuPreviewTip', '管理员默认预览全部可访问菜单。')}
              />
            ) : null}

            <Tabs
              items={[
                {
                  key: 'desktop',
                  label: t('system.roles.desktopMenuPreview', '桌面菜单'),
                  children: (
                    <Space direction="vertical" size={12} style={{ width: '100%' }}>
                      <Space wrap>
                        <Segmented
                          value={webMenuVisibilityFilter}
                          options={[
                            {
                              label: `${t('system.roles.menuFilterAll', '全部')} (${previewDesktopAllMenuCount})`,
                              value: 'all',
                            },
                            {
                              label: `${t('system.roles.menuFilterVisible', '可见')} (${previewDesktopVisibleMenuCount})`,
                              value: 'visible',
                            },
                            {
                              label: `${t('system.roles.menuFilterHidden', '未显示')} (${previewDesktopHiddenMenuCount})`,
                              value: 'hidden',
                            },
                          ]}
                          onChange={(value) => setWebMenuVisibilityFilter(value as WebMenuVisibilityFilter)}
                        />
                        <Typography.Text type="secondary">
                          {t('system.roles.menuFilterCount', '当前显示 {{count}} 项', {
                            count: countWebMenuPreviewNodes(previewDesktopFilteredMenus),
                          })}
                        </Typography.Text>
                      </Space>
                      {renderWebMenuPreview(previewDesktopFilteredMenus)}
                    </Space>
                  ),
                },
                {
                  key: 'mobile',
                  label: t('system.roles.expoMobileMenuPreview', 'HbwebExpo 移动端菜单'),
                  children: (
                    <Space direction="vertical" size={16} style={{ width: '100%' }}>
                      <Alert
                        type="info"
                        showIcon
                        message={t(
                          'system.roles.expoMenuPreviewTip',
                          '此预览按 HbwebExpo 当前 app-menu 与底部栏折叠规则计算。',
                        )}
                      />
                      <Card title={t('system.roles.expoDirectTabs', 'HbwebExpo 底部直接入口')} size="small">
                        {renderExpoDirectTabs(previewExpoDirectTabs)}
                      </Card>
                      <Card title={t('system.roles.expoStoreMenu', 'HbwebExpo 门店折叠菜单')} size="small">
                        {previewExpoMenu
                          ? renderExpoRouteList(
                              previewExpoMenu.storeChildren,
                              t('system.roles.noVisibleExpoStoreMenus', '暂无可见门店折叠菜单'),
                            )
                          : (
                              <Empty description={t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口')} />
                            )}
                      </Card>
                      <Card title={t('system.roles.expoMenuMaintenance', 'HbwebExpo 菜单权限维护')} size="small">
                        {previewExpoMenu
                          ? (
                              <Space direction="vertical" size={12} style={{ width: '100%' }}>
                                <Space wrap>
                                  <Segmented
                                    value={expoMenuVisibilityFilter}
                                    options={[
                                      {
                                        label: `${t('system.roles.menuFilterAll', '全部')} (${previewExpoAllRouteCount})`,
                                        value: 'all',
                                      },
                                      {
                                        label: `${t('system.roles.menuFilterVisible', '可见')} (${previewExpoVisibleRouteCount})`,
                                        value: 'visible',
                                      },
                                      {
                                        label: `${t('system.roles.menuFilterHidden', '未显示')} (${previewExpoHiddenRouteCount})`,
                                        value: 'hidden',
                                      },
                                    ]}
                                    onChange={(value) => setExpoMenuVisibilityFilter(value as ExpoMenuVisibilityFilter)}
                                  />
                                  <Typography.Text type="secondary">
                                    {t('system.roles.menuFilterCount', '当前显示 {{count}} 项', {
                                      count: previewExpoFilteredRoutes.length,
                                    })}
                                  </Typography.Text>
                                </Space>
                                {renderExpoRouteList(
                                  previewExpoFilteredRoutes,
                                  t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口'),
                                  true,
                                )}
                              </Space>
                            )
                          : (
                              <Empty description={t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口')} />
                            )}
                      </Card>
                    </Space>
                  ),
                },
              ]}
            />
          </Space>
        ) : null}
      </Drawer>

      <Modal
        title={t('system.roles.createTitle')}
        open={createOpen}
        onCancel={() => {
          setCreateOpen(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreateSubmit()}
        confirmLoading={createLoading}
        width={620}
        destroyOnHidden
      >
        <Form form={createForm} layout="vertical" initialValues={{ isActive: true }}>
          <Form.Item
            label={t('system.roles.roleName')}
            name="roleName"
            rules={[
              { required: true, message: t('system.roles.roleNameRequired') },
              { whitespace: true, message: t('system.roles.roleNameRequired') },
              { min: 2, max: 50, message: t('system.roles.roleNameLength') },
            ]}
          >
            <Input maxLength={50} />
          </Form.Item>
          <Form.Item
            label={t('column.description')}
            name="description"
            rules={[{ max: 200, message: t('system.roles.descriptionLength') }]}
          >
            <Input.TextArea rows={4} maxLength={200} showCount />
          </Form.Item>
          <Form.Item label={t('column.status')} name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingRole ? t('system.roles.editTitle', { name: editingRole.roleName }) : t('system.roles.editTitleShort')}
        open={editOpen}
        onCancel={() => {
          setEditOpen(false)
          setEditRoleGuid('')
          setEditingRole(null)
          form.resetFields()
        }}
        onOk={() => void handleEditSubmit()}
        confirmLoading={editLoading}
        width={820}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item label={t('system.roles.roleName')} name="roleName" rules={[{ required: true, message: t('system.roles.roleNameRequired') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('column.description')} name="description">
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item label={t('column.status')} name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
          </Form.Item>
        </Form>

        {editRoleGuid ? (
          <div style={{ marginTop: 16, borderTop: '1px solid #f0f0f0', paddingTop: 16 }}>
            <Typography.Title level={5} style={{ marginBottom: 12 }}>
              {t('system.roles.permissions', '权限分配')}
            </Typography.Title>
            <HasPermission code={P.Roles.ManagePermissions}>
              <RolePermissionManager
                roleGuid={editRoleGuid}
                roleName={editingRole?.roleName ?? ''}
                onChanged={() => void loadData(page, pageSize)}
              />
            </HasPermission>
          </div>
        ) : null}
      </Modal>

      <RoleUserManagement
        open={roleUserOpen}
        role={detailRole}
        onClose={() => setRoleUserOpen(false)}
        onChanged={() => {
          if (!detailRole) return
          void reloadRoleDetail(detailRole.roleGUID)
          void loadData(page, pageSize)
        }}
      />
    </PageContainer>
  )
}
