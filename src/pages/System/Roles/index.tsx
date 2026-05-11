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
import { getRoleByGuid, getRoles, updateRole } from '../../../services/roleService'
import type { RoleDetailDto, RoleDto, UpdateRoleDto } from '../../../types/role'
import RoleUserManagement from './RoleUserManagement'

export default function SystemRolesPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<RoleDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)

  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailRole, setDetailRole] = useState<RoleDetailDto | null>(null)

  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingRole, setEditingRole] = useState<RoleDetailDto | null>(null)
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
      message.error('加载角色列表失败')
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

  const handleViewDetail = async (record: RoleDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailRole(null)
    try {
      const detail = await getRoleByGuid(record.roleGUID)
      setDetailRole(detail)
    } catch (error) {
      console.error(error)
      message.error('加载角色详情失败')
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const handleEdit = async (record: RoleDto) => {
    setEditOpen(true)
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
      message.error('加载角色编辑数据失败')
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
      message.success('角色信息已更新')
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
      message.error('更新角色失败')
    } finally {
      setEditLoading(false)
    }
  }

  const columns: ColumnsType<RoleDto> = [
    { title: '角色名称', dataIndex: 'roleName', width: 220 },
    { title: '描述', dataIndex: 'description', render: (value) => value || '--' },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
      ),
    },
    { title: '关联用户数', dataIndex: 'userCount', width: 140 },
    {
      title: '操作',
      key: 'action',
      width: 160,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            详情
          </Button>
          <HasPermission code={P.Roles.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              编辑
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer title="角色管理" subtitle="管理角色基本信息和关联用户。">
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索角色名称"
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
        title={detailRole ? `角色详情 - ${detailRole.roleName}` : '角色详情'}
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
                管理用户
              </Button>
            </HasPermission>
          ) : null
        }
      >
        {detailLoading ? (
          <Typography.Text type="secondary">正在加载角色详情...</Typography.Text>
        ) : !detailRole ? (
          <Typography.Text type="danger">未找到角色信息</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label="角色名称">{detailRole.roleName}</Descriptions.Item>
              <Descriptions.Item label="状态">
                <Tag color={detailRole.isActive ? 'success' : 'default'}>
                  {detailRole.isActive ? '启用' : '停用'}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="描述" span={2}>
                {detailRole.description || '--'}
              </Descriptions.Item>
              <Descriptions.Item label="关联用户数">{detailRole.userCount}</Descriptions.Item>
              <Descriptions.Item label="更新时间">{detailRole.updatedAt}</Descriptions.Item>
            </Descriptions>

            <Card title="权限" size="small">
              <Space wrap>
                {detailRole.permissions?.length
                  ? detailRole.permissions.map((item) => <Tag key={item}>{item}</Tag>)
                  : '暂无权限'}
              </Space>
            </Card>

            <Card title="关联用户" size="small">
              <List
                dataSource={detailRole.users ?? []}
                locale={{ emptyText: '暂无关联用户' }}
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
        title={editingRole ? `编辑角色 - ${editingRole.roleName}` : '编辑角色'}
        open={editOpen}
        onCancel={() => {
          setEditOpen(false)
          setEditingRole(null)
          form.resetFields()
        }}
        onOk={() => void handleEditSubmit()}
        confirmLoading={editLoading}
        width={640}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item label="角色名称" name="roleName" rules={[{ required: true, message: '请输入角色名称' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="描述" name="description">
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item label="状态" name="isActive" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="停用" />
          </Form.Item>
        </Form>
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
