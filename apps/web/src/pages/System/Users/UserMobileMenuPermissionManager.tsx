import {
  DeleteOutlined,
  LockOutlined,
  PlusCircleOutlined,
  SaveOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Empty,
  List,
  Popconfirm,
  Segmented,
  Space,
  Tag,
  Typography,
} from 'antd'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { UserPermissionStateDto } from '../../../types/user'
import {
  addExpoMenuPermission,
  buildExpoUserMenuPreview,
  filterExpoRoutesByVisibility,
  removeExpoMenuDirectPermissions,
  type ExpoAppVisibleRoute,
  type ExpoMenuVisibilityFilter,
} from '../../../utils/expoRoleMenuPreview'
import { getRoleColor } from '../../../utils/userTableColors'

interface UserMobileMenuPermissionManagerProps {
  permissionState: UserPermissionStateDto | null
  directPermissionCodes: string[]
  assignablePermissionCodes: string[]
  scoped: boolean
  canEdit: boolean
  saving: boolean
  hasChanges: boolean
  onChange: (permissionCodes: string[]) => void
  onSave: () => void
}

export default function UserMobileMenuPermissionManager({
  permissionState,
  directPermissionCodes,
  assignablePermissionCodes,
  scoped,
  canEdit,
  saving,
  hasChanges,
  onChange,
  onSave,
}: UserMobileMenuPermissionManagerProps) {
  const { t } = useTranslation()
  const [visibilityFilter, setVisibilityFilter] = useState<ExpoMenuVisibilityFilter>('all')

  const preview = useMemo(() => {
    if (!permissionState) return null

    return buildExpoUserMenuPreview({
      inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
      directPermissionCodes,
      assignablePermissionCodes,
      inheritedSources: permissionState.inheritedSources,
      isSuperAdmin: permissionState.isSuperAdmin,
      implicitAllPermissions: permissionState.implicitAllPermissions,
      readOnly: !canEdit,
    })
  }, [assignablePermissionCodes, canEdit, directPermissionCodes, permissionState])

  if (scoped) {
    return (
      <Alert
        type="info"
        showIcon
        message={t(
          'system.users.mobileMenuScopedUnavailable',
          '当前账号仅能维护授权范围内的 POS 权限，无法取得该用户的完整移动端菜单。',
        )}
      />
    )
  }

  if (!permissionState || !preview) {
    return <Empty description={t('system.users.noPermData', '暂无权限数据')} />
  }

  const isImplicitAll = Boolean(
    permissionState.isSuperAdmin || permissionState.implicitAllPermissions,
  )
  const filteredRoutes = filterExpoRoutesByVisibility(preview.allRoutes, visibilityFilter)
  const visibleCount = preview.allRoutes.filter((route) => route.visible).length
  const hiddenCount = preview.allRoutes.length - visibleCount

  const applyAdd = (route: ExpoAppVisibleRoute) => {
    onChange(addExpoMenuPermission({
      directPermissionCodes,
      route,
      assignablePermissionCodes,
    }))
  }

  const applyRemove = (route: ExpoAppVisibleRoute) => {
    onChange(removeExpoMenuDirectPermissions({
      directPermissionCodes,
      route,
      assignablePermissionCodes,
    }))
  }

  const renderAction = (route: ExpoAppVisibleRoute) => {
    if (route.fixed) {
      return (
        <Tag icon={<LockOutlined />}>
          {t('system.roles.fixedMenuPermission', '固定入口')}
        </Tag>
      )
    }

    if (route.canRemove) {
      return (
        <Popconfirm
          title={t(
            'system.roles.removeMenuPermissionConfirm',
            '移除这些权限后该菜单可能不再显示，确定继续吗？',
          )}
          okText={t('common.confirm', '确认')}
          cancelText={t('common.cancel', '取消')}
          onConfirm={() => applyRemove(route)}
        >
          <Button danger size="small" icon={<DeleteOutlined />} disabled={saving}>
            {t('system.roles.removeMenuPermission', '移除权限')}
          </Button>
        </Popconfirm>
      )
    }

    if (route.canAdd) {
      return (
        <Button
          type="primary"
          size="small"
          icon={<PlusCircleOutlined />}
          disabled={saving}
          onClick={() => applyAdd(route)}
        >
          {t('system.roles.addMenuPermission', '添加权限')}
        </Button>
      )
    }

    return <Tag>{t('system.roles.readOnlyMenuPermission', '只读')}</Tag>
  }

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Alert
        type="info"
        showIcon
        message={t(
          'system.users.mobileMenuDerivedTip',
          '移动端菜单由角色继承权限和用户直接权限实时生成；这里的修改会同步到功能权限草稿。',
        )}
      />

      {isImplicitAll ? (
        <Alert
          type="info"
          showIcon
          message={t(
            'system.users.superAdminPermissionsReadOnly',
            '管理员默认拥有全部权限和移动端菜单，此处仅供查看。',
          )}
        />
      ) : !canEdit ? (
        <Alert
          type="warning"
          showIcon
          message={t('system.users.noPermissionManagePermission', '无权限编辑用户直接权限')}
        />
      ) : null}

      <Card title={t('system.roles.expoDirectTabs', 'HbwebExpo 底部直接入口')} size="small">
        {preview.displayTabs.length ? (
          <Space wrap size={[8, 8]}>
            {preview.displayTabs.map((item) => (
              item.type === 'store' ? (
                <Tag key={item.key} color="purple">
                  {item.zhTitle} · {item.children.length}
                </Tag>
              ) : (
                <Tag key={item.key} color="blue">
                  {item.route.zhTitle}
                </Tag>
              )
            ))}
          </Space>
        ) : (
          <Empty description={t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口')} />
        )}
      </Card>

      <Card title={t('system.roles.expoMenuMaintenance', 'HbwebExpo 菜单权限维护')} size="small">
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Space wrap>
            <Segmented
              value={visibilityFilter}
              options={[
                {
                  label: `${t('system.roles.menuFilterAll', '全部')} (${preview.allRoutes.length})`,
                  value: 'all',
                },
                {
                  label: `${t('system.roles.menuFilterVisible', '可见')} (${visibleCount})`,
                  value: 'visible',
                },
                {
                  label: `${t('system.roles.menuFilterHidden', '未显示')} (${hiddenCount})`,
                  value: 'hidden',
                },
              ]}
              onChange={(value) => setVisibilityFilter(value as ExpoMenuVisibilityFilter)}
            />
            <Typography.Text type="secondary">
              {t('system.roles.menuFilterCount', '当前显示 {{count}} 项', {
                count: filteredRoutes.length,
              })}
            </Typography.Text>
          </Space>

          <List
            rowKey="routeName"
            dataSource={filteredRoutes}
            locale={{ emptyText: t('system.roles.noVisibleExpoTabs', '暂无可见 HbwebExpo 底部入口') }}
            renderItem={(route) => (
              <List.Item>
                <Space direction="vertical" size={6} style={{ width: '100%' }}>
                  <Space wrap>
                    <Typography.Text strong>{route.zhTitle}</Typography.Text>
                    <Typography.Text type="secondary">{route.enTitle}</Typography.Text>
                    <Tag color="blue">{route.routeName}</Tag>
                    <Tag color={route.visible ? 'success' : 'default'}>
                      {route.visible
                        ? t('system.roles.menuPermissionVisible', '可见')
                        : t('system.roles.menuPermissionHidden', '未显示')}
                    </Tag>
                    {route.direct ? (
                      <Tag color="blue">{t('system.users.directPermissionTag', '直接授权')}</Tag>
                    ) : null}
                    {route.roleSources.map((roleName) => (
                      <Tag key={roleName} color={getRoleColor(roleName)}>{roleName}</Tag>
                    ))}
                    {renderAction(route)}
                  </Space>
                  <Space wrap size={[4, 4]}>
                    <Typography.Text type="secondary">{route.path}</Typography.Text>
                    <Typography.Text type="secondary">
                      {t('system.roles.expoPermission', '权限')}:
                    </Typography.Text>
                    {route.permissionCodes.length ? (
                      <>
                        {route.anyPermission ? (
                          <Tag color="green">
                            {t('system.roles.webMenuAnyPermission', '任一权限满足即可')}
                          </Tag>
                        ) : null}
                        {route.permissionCodes.map((permissionCode) => (
                          <Tag key={permissionCode}>{permissionCode}</Tag>
                        ))}
                      </>
                    ) : (
                      <Tag>{t('system.roles.webMenuNoDirectPermission', '无直接权限')}</Tag>
                    )}
                  </Space>
                </Space>
              </List.Item>
            )}
          />
        </Space>
      </Card>

      {canEdit && !isImplicitAll ? (
        <div style={{ textAlign: 'right' }}>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            loading={saving}
            disabled={!hasChanges}
            onClick={onSave}
          >
            {t('system.users.savePermissionAssign', '保存权限分配')}
          </Button>
        </div>
      ) : null}
    </Space>
  )
}
