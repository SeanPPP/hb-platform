import { PlusOutlined, ReloadOutlined, TeamOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Form,
  Input,
  Modal,
  Space,
  Table,
  Tag,
  Transfer,
  message,
} from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { ColumnsType } from 'antd/es/table'
import type { Key } from 'react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  assignRolesToPermission,
  createPermission,
  getActiveRoles,
  getPermissionRoles,
  getSysPermissions,
} from '../../../services/roleService'
import type { CreateSysPermissionDto, RoleOptionDto, SysPermissionDto } from '../../../types/role'

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

export default function SystemPermissionsPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<SysPermissionDto[]>([])
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null)

  const [createOpen, setCreateOpen] = useState(false)
  const [createLoading, setCreateLoading] = useState(false)
  const [createForm] = Form.useForm<CreateSysPermissionDto>()

  const [assignOpen, setAssignOpen] = useState(false)
  const [assignLoading, setAssignLoading] = useState(false)
  const [assignSaving, setAssignSaving] = useState(false)
  const [currentPermission, setCurrentPermission] = useState<SysPermissionDto | null>(null)
  const [allRoles, setAllRoles] = useState<RoleOptionDto[]>([])
  const [roleTargetKeys, setRoleTargetKeys] = useState<string[]>([])

  const loadData = async () => {
    setLoading(true)
    try {
      const result = await getSysPermissions()
      setData(result)
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

  const categories = [...new Set(data.map((item) => item.category))].sort()

  const filteredData = categoryFilter ? data.filter((item) => item.category === categoryFilter) : data

  const handleCreate = async () => {
    try {
      const values = await createForm.validateFields()
      setCreateLoading(true)
      await createPermission(values)
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

  const handleAssignRoles = async (record: SysPermissionDto) => {
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

  const columns: ColumnsType<SysPermissionDto> = [
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
    { title: t('system.permissions.permissionName'), dataIndex: 'name', width: 150 },
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
      width: 120,
      render: (_, record) => (
        <Button type="link" icon={<TeamOutlined />} onClick={() => void handleAssignRoles(record)}>
          {t('system.permissions.assignRoles')}
        </Button>
      ),
    },
  ]

  const actionOptions = ['Create', 'View', 'Edit', 'Delete']

  return (
    <PageContainer title={t('system.permissions.pageTitle')} subtitle={t('system.permissions.pageSubtitle')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <span style={{ marginRight: 4 }}>{t('system.permissions.categoryFilter')}</span>
          <Checkbox
            checked={categoryFilter === null}
            onChange={() => setCategoryFilter(null)}
          >
            {t('system.permissions.allCategories')}
          </Checkbox>
          {categories.map((cat) => (
            <Checkbox
              key={cat}
              checked={categoryFilter === cat}
              onChange={() => setCategoryFilter(categoryFilter === cat ? null : cat)}
            >
              <Tag color={CATEGORY_COLORS[cat] || 'default'}>{cat}</Tag>
            </Checkbox>
          ))}
          <span style={{ marginLeft: 16 }} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            {t('system.permissions.newPermission')}
          </Button>
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

      <Modal
        title={t('system.permissions.newPermission')}
        open={createOpen}
        onCancel={() => {
          setCreateOpen(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreate()}
        confirmLoading={createLoading}
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
          <Form.Item label={t('system.permissions.batchGeneration')}>
            <Checkbox.Group
              options={actionOptions.map((action) => ({ label: action, value: action }))}
              onChange={(values) => {
                createForm.setFieldValue('actions', values as string[])
              }}
            />
            <div style={{ color: '#999', fontSize: 12, marginTop: 4 }}>
              {t('system.permissions.batchGenDesc')}
            </div>
          </Form.Item>
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
