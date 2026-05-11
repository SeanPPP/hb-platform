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
      message.error('加载权限列表失败')
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
      message.success('权限创建成功')
      setCreateOpen(false)
      createForm.resetFields()
      void loadData()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error('创建权限失败')
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
      message.error('加载角色数据失败')
    } finally {
      setAssignLoading(false)
    }
  }

  const handleSaveRoles = async () => {
    if (!currentPermission) return
    setAssignSaving(true)
    try {
      await assignRolesToPermission(currentPermission.code, roleTargetKeys)
      message.success(`已更新「${currentPermission.name}」的角色分配`)
      setAssignOpen(false)
    } catch (error) {
      console.error(error)
      message.error('分配角色失败')
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
      title: '权限代码',
      dataIndex: 'code',
      width: 220,
      render: (value) => <Tag>{value}</Tag>,
    },
    { title: '权限名称', dataIndex: 'name', width: 150 },
    {
      title: '分类',
      dataIndex: 'category',
      width: 130,
      render: (value) => <Tag color={CATEGORY_COLORS[value] || 'default'}>{value}</Tag>,
    },
    {
      title: '描述',
      dataIndex: 'description',
      ellipsis: true,
      render: (value) => value || '--',
    },
    {
      title: '操作',
      key: 'action',
      width: 120,
      render: (_, record) => (
        <Button type="link" icon={<TeamOutlined />} onClick={() => void handleAssignRoles(record)}>
          分配角色
        </Button>
      ),
    },
  ]

  const actionOptions = ['Create', 'View', 'Edit', 'Delete']

  return (
    <PageContainer title="权限管理" subtitle="管理系统权限定义，并为权限分配角色。">
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <span style={{ marginRight: 4 }}>分类筛选：</span>
          <Checkbox
            checked={categoryFilter === null}
            onChange={() => setCategoryFilter(null)}
          >
            全部
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
            新增权限
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>
            刷新
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
        title="新增权限"
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
          <Form.Item label="权限代码" name="code" rules={[{ required: true, message: '请输入权限代码' }]}>
            <Input placeholder="如 Users.Create" />
          </Form.Item>
          <Form.Item label="权限名称" name="name" rules={[{ required: true, message: '请输入权限名称' }]}>
            <Input placeholder="如 创建用户" />
          </Form.Item>
          <Form.Item label="权限分类" name="category" rules={[{ required: true, message: '请输入权限分类' }]}>
            <Input placeholder="如 Users" />
          </Form.Item>
          <Form.Item label="描述" name="description">
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item label="批量生成操作">
            <Checkbox.Group
              options={actionOptions.map((action) => ({ label: action, value: action }))}
              onChange={(values) => {
                createForm.setFieldValue('actions', values as string[])
              }}
            />
            <div style={{ color: '#999', fontSize: 12, marginTop: 4 }}>
              勾选后将以"权限代码"为基础自动生成多个权限（如 Users.Create、Users.View 等）
            </div>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={currentPermission ? `分配角色 - ${currentPermission.name}` : '分配角色'}
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
          <div style={{ textAlign: 'center', padding: 40 }}>加载中...</div>
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
            titles={['可选角色', '已分配角色']}
            listStyle={{ width: 280, height: 400 }}
            showSearch
          />
        )}
      </Modal>
    </PageContainer>
  )
}
