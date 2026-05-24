import { EditOutlined, EyeOutlined, PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
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
import { useTranslation } from 'react-i18next'
import { HasPermission, usePermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { createRole, getRoleByGuid, getRoles, updateRole } from '../../../services/roleService'
import type { CreateRoleDto, RoleDetailDto, RoleDto, UpdateRoleDto } from '../../../types/role'
import RolePermissionManager from './RolePermissionManager'
import RoleUserManagement from './RoleUserManagement'

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
      width: 160,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view')}
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
