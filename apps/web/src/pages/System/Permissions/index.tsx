import { DeleteOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, TeamOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Empty,
  Form,
  Input,
  Modal,
  Popconfirm,
  Space,
  Table,
  Tag,
  Typography,
  Tree,
  Transfer,
  message,
} from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { ColumnsType } from 'antd/es/table'
import type { Key } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  assignRolesToPermission,
  createPermission,
  deletePermission,
  getActiveRoles,
  getPermissionCatalog,
  getPermissionRoles,
  getSysPermissions,
} from '../../../services/roleService'
import type { CreateSysPermissionDto, PermissionCategoryDto, RoleOptionDto, SysPermissionDto } from '../../../types/role'
import { useAuthStore } from '../../../store/auth'
import { canManageSystemPermissions } from './permissionsAccess'

const CATEGORY_COLORS: Record<string, string> = {
  Users: 'blue',
  Roles: 'purple',
  Stores: 'green',
  Warehouse: 'orange',
  Products: 'cyan',
  Orders: 'magenta',
  DomesticPurchase: 'gold',
  PosAdmin: 'geekblue',
  Shop: 'volcano',
}

interface PermissionTableItem {
  id: string
  code: string
  name: string
  category: string
  description?: string
  deletable: boolean
}

function buildPermissionTableItems(
  permissionCategories: PermissionCategoryDto[],
  sysPermissions: SysPermissionDto[],
): PermissionTableItem[] {
  const sysPermissionMap = new Map(sysPermissions.map((item) => [item.code, item]))
  const items = new Map<string, PermissionTableItem>()

  permissionCategories.forEach((category) => {
    category.permissions.forEach((permission) => {
      const sysPermission = sysPermissionMap.get(permission.name)
      items.set(permission.name, {
        id: sysPermission?.id ?? permission.name,
        code: permission.name,
        name: permission.displayName || sysPermission?.name || permission.name,
        category: category.displayName || permission.category || category.category,
        description: permission.description || sysPermission?.description,
        deletable: !permission.isSystemPermission && Boolean(sysPermission),
      })
    })
  })

  sysPermissions.forEach((permission) => {
    if (items.has(permission.code)) return
    items.set(permission.code, {
      id: permission.id,
      code: permission.code,
      name: permission.name,
      category: permission.category,
      description: permission.description,
      deletable: true,
    })
  })

  return Array.from(items.values())
}

export default function SystemPermissionsPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const canWritePermissions = canManageSystemPermissions(access)
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<PermissionTableItem[]>([])
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null)
  const [keyword, setKeyword] = useState('')

  const [createOpen, setCreateOpen] = useState(false)
  const [createLoading, setCreateLoading] = useState(false)
  const [createForm] = Form.useForm<CreateSysPermissionDto>()

  const [assignOpen, setAssignOpen] = useState(false)
  const [assignLoading, setAssignLoading] = useState(false)
  const [assignSaving, setAssignSaving] = useState(false)
  const [currentPermission, setCurrentPermission] = useState<PermissionTableItem | null>(null)
  const [allRoles, setAllRoles] = useState<RoleOptionDto[]>([])
  const [roleTargetKeys, setRoleTargetKeys] = useState<string[]>([])

  const loadData = async () => {
    setLoading(true)
    try {
      const [permissionCatalog, sysPermissions] = await Promise.all([
        getPermissionCatalog(),
        getSysPermissions(),
      ])
      setData(buildPermissionTableItems(permissionCatalog.categories, sysPermissions))
    } catch (error) {
      console.error(error)
      message.error(t('system.permissions.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [])

  const categories = useMemo(() => [...new Set(data.map((item) => item.category))].sort(), [data])

  useEffect(() => {
    if (categoryFilter && !categories.includes(categoryFilter)) {
      setCategoryFilter(null)
    }
  }, [categories, categoryFilter])

  const treeData = useMemo(
    () => [
      {
        key: 'all',
        title: `${t('system.permissions.allCategories')} (${data.length})`,
      },
      ...categories.map((category) => ({
        key: category,
        title: `${category} (${data.filter((item) => item.category === category).length})`,
      })),
    ],
    [categories, data, t],
  )

  const normalizedKeyword = keyword.trim().toLowerCase()

  const filteredData = useMemo(() => {
    const byCategory = categoryFilter ? data.filter((item) => item.category === categoryFilter) : data
    const byKeyword = normalizedKeyword
      ? byCategory.filter((item) => {
          const code = item.code.toLowerCase()
          const name = item.name.toLowerCase()
          return code.includes(normalizedKeyword) || name.includes(normalizedKeyword)
        })
      : byCategory

    return [...byKeyword].sort((a, b) => a.name.localeCompare(b.name))
  }, [categoryFilter, data, normalizedKeyword])

  const handleCreate = async () => {
    if (!canWritePermissions) {
      message.warning(t('system.permissions.noManagePermission', '无权限管理权限'))
      return
    }

    try {
      const values = await createForm.validateFields()
      setCreateLoading(true)
      const payload: CreateSysPermissionDto = {
        code: values.code,
        name: values.name,
        category: values.category,
        description: values.description,
        actions: values.actions?.length ? values.actions : undefined,
      }
      await createPermission(payload)
      message.success(t('system.permissions.createSuccess'))
      setCreateOpen(false)
      createForm.resetFields()
      void loadData()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.permissions.createFailed'))
    } finally {
      setCreateLoading(false)
    }
  }

  const handleAssignRoles = async (record: PermissionTableItem) => {
    if (!canWritePermissions) {
      message.warning(t('system.permissions.noManagePermission', '无权限管理权限'))
      return
    }

    setCurrentPermission(record)
    setAssignOpen(true)
    setAssignLoading(true)
    try {
      const [roles, permRoles] = await Promise.all([getActiveRoles(), getPermissionRoles(record.code)])
      setAllRoles(roles)
      setRoleTargetKeys(permRoles.map((item) => item.roleGUID))
    } catch (error) {
      console.error(error)
      message.error(t('system.permissions.loadRolesFailed'))
    } finally {
      setAssignLoading(false)
    }
  }

  const handleSaveRoles = async () => {
    if (!currentPermission) return
    if (!canWritePermissions) {
      message.warning(t('system.permissions.noManagePermission', '无权限管理权限'))
      return
    }

    setAssignSaving(true)
    try {
      await assignRolesToPermission(currentPermission.code, roleTargetKeys)
      message.success(t('system.permissions.roleAssignSuccess', { name: currentPermission.name }))
      setAssignOpen(false)
    } catch (error) {
      console.error(error)
      message.error(t('system.permissions.roleAssignFailed'))
    } finally {
      setAssignSaving(false)
    }
  }

  const handleDelete = async (record: PermissionTableItem) => {
    if (!canWritePermissions) {
      message.warning(t('system.permissions.noManagePermission', '无权限管理权限'))
      return
    }

    try {
      await deletePermission(record.code)
      message.success(t('common.deleteSuccess'))
      void loadData()
    } catch (error) {
      console.error(error)
      message.error(t('common.deleteFailed'))
    }
  }

  const columns: ColumnsType<PermissionTableItem> = [
    {
      title: '#',
      width: 48,
      render: (_, __, index) => index + 1,
    },
    {
      title: t('system.permissions.permissionCodeCol'),
      dataIndex: 'code',
      width: 220,
      render: (value) => <Tag>{value}</Tag>,
    },
    {
      title: t('system.permissions.permissionName'),
      dataIndex: 'name',
      width: 180,
      sorter: (a, b) => a.name.localeCompare(b.name),
      defaultSortOrder: 'ascend',
    },
    {
      title: t('system.permissions.category'),
      dataIndex: 'category',
      width: 130,
      render: (value) => <Tag color={CATEGORY_COLORS[value] || 'default'}>{value}</Tag>,
    },
    {
      title: t('column.description'),
      dataIndex: 'description',
      ellipsis: true,
      render: (value) => value || '--',
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 180,
      render: (_, record) => (
        <Space>
          {canWritePermissions ? (
            <Button type="link" icon={<TeamOutlined />} onClick={() => void handleAssignRoles(record)}>
              {t('system.permissions.assignRoles')}
            </Button>
          ) : null}
          {canWritePermissions && record.deletable ? (
            <Popconfirm
              title={t('common.delete')}
              description={t('common.deleteIrreversible', '删除后不可恢复')}
              onConfirm={() => void handleDelete(record)}
              okText={t('common.delete')}
              cancelText={t('common.cancel')}
            >
              <Button type="link" danger icon={<DeleteOutlined />}>
                {t('common.delete')}
              </Button>
            </Popconfirm>
          ) : canWritePermissions ? (
            <Typography.Text type="secondary">--</Typography.Text>
          ) : null}
        </Space>
      ),
    },
  ]

  const actionOptions = ['Create', 'View', 'Edit', 'Delete']

  return (
    <PageContainer title={t('system.permissions.pageTitle')} subtitle={t('system.permissions.pageSubtitle')}>
      <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
        <Card title={t('system.permissions.categoryFilter')} style={{ width: 260, flexShrink: 0 }}>
          {treeData.length ? (
            <div
              style={{
                maxHeight: 'calc(100vh - 280px)',
                overflowY: 'auto',
                overflowX: 'hidden',
                paddingRight: 4,
              }}
            >
              <Tree
                blockNode
                selectedKeys={[categoryFilter ?? 'all']}
                onSelect={(keys) => {
                  const selectedKey = typeof keys[0] === 'string' ? keys[0] : 'all'
                  setCategoryFilter(selectedKey === 'all' ? null : selectedKey)
                }}
                treeData={treeData}
              />
            </div>
          ) : (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('common.noData', '暂无数据')} />
          )}
        </Card>

        <Card style={{ flex: 1, minWidth: 0 }}>
          <Space wrap style={{ marginBottom: 16 }}>
            <Input
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder={t('system.permissions.searchPlaceholder')}
              prefix={<SearchOutlined />}
              style={{ width: 280 }}
              allowClear
            />
            {canWritePermissions ? (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
                {t('system.permissions.newPermission')}
              </Button>
            ) : null}
            <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>
              {t('common.refresh')}
            </Button>
          </Space>

          <Table
            rowKey="id"
            loading={loading}
            columns={columns}
            dataSource={filteredData}
            pagination={{ pageSize: 20, showSizeChanger: true, pageSizeOptions: ['20', '50', '100'] }}
          />
        </Card>
      </div>

      <Modal
        title={t('system.permissions.newPermission')}
        open={createOpen}
        onCancel={() => {
          setCreateOpen(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreate()}
        confirmLoading={createLoading}
        okButtonProps={{ disabled: !canWritePermissions }}
        width={600}
        destroyOnHidden
      >
        <Form form={createForm} layout="vertical">
          <Form.Item label={t('system.permissions.permissionCodeCol')} name="code" rules={[{ required: true, message: t('system.permissions.permissionCodeRequired') }]}>
            <Input placeholder={t('system.permissions.codePlaceholder')} />
          </Form.Item>
          <Form.Item label={t('system.permissions.permissionName')} name="name" rules={[{ required: true, message: t('system.permissions.permissionNameRequired') }]}>
            <Input placeholder={t('system.permissions.namePlaceholder')} />
          </Form.Item>
          <Form.Item label={t('system.permissions.category')} name="category" rules={[{ required: true, message: t('system.permissions.category') + t('system.permissions.permissionCodeRequired').replace(t('system.permissions.permissionCodeCol'), '') }]}>
            <Input placeholder={t('system.permissions.categoryPlaceholder')} />
          </Form.Item>
          <Form.Item label={t('column.description')} name="description">
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="actions" label={t('system.permissions.batchGeneration')}>
            <Checkbox.Group
              options={actionOptions.map((action) => ({ label: action, value: action }))}
            />
          </Form.Item>
          <div style={{ color: '#999', fontSize: 12, marginTop: -16, marginBottom: 24 }}>
            {t('system.permissions.batchGenDesc')}
          </div>
        </Form>
      </Modal>

      <Modal
        title={currentPermission ? t('system.permissions.assignRolesTitle', { name: currentPermission.name }) : t('system.permissions.assignRolesTitleShort')}
        open={assignOpen}
        onCancel={() => {
          setAssignOpen(false)
          setCurrentPermission(null)
        }}
        onOk={() => void handleSaveRoles()}
        confirmLoading={assignSaving}
        okButtonProps={{ disabled: !canWritePermissions }}
        width={700}
        destroyOnHidden
      >
        {assignLoading ? (
          <div style={{ textAlign: 'center', padding: 40 }}>{t('system.permissions.loading')}</div>
        ) : (
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
            titles={[t('system.users.availableRoles'), t('system.users.assignedRolesLabel')]}
            listStyle={{ width: 280, height: 400 }}
            showSearch
          />
        )}
      </Modal>
    </PageContainer>
  )
}
