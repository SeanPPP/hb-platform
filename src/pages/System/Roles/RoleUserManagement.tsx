import { DeleteOutlined, UserAddOutlined } from '@ant-design/icons'
import { Button, Drawer, Modal, Popconfirm, Select, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { addUsersToRole, getRoleUsers, removeUserFromRole } from '../../../services/roleService'
import { getUsers } from '../../../services/userService'
import type { RoleDto, RoleUserDto } from '../../../types/role'
import type { UserDto } from '../../../types/user'

interface RoleUserManagementProps {
  open: boolean
  role: RoleDto | null
  onClose: () => void
  onChanged?: () => void
}

export default function RoleUserManagement({ open, role, onClose, onChanged }: RoleUserManagementProps) {
  const [loading, setLoading] = useState(false)
  const [users, setUsers] = useState<RoleUserDto[]>([])
  const [addOpen, setAddOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [allUsers, setAllUsers] = useState<UserDto[]>([])
  const [selectedUserGuids, setSelectedUserGuids] = useState<string[]>([])

  const loadUsers = async () => {
    if (!role) {
      return
    }

    setLoading(true)
    try {
      const result = await getRoleUsers(role.roleGUID, { page: 1, pageSize: 200 })
      setUsers(result.items)
    } catch (error) {
      console.error(error)
      message.error('加载角色用户失败')
    } finally {
      setLoading(false)
    }
  }

  const loadAvailableUsers = async () => {
    try {
      const result = await getUsers({ page: 1, pageSize: 200 })
      setAllUsers(result.items)
    } catch (error) {
      console.error(error)
      message.error('加载用户选项失败')
    }
  }

  useEffect(() => {
    if (!open || !role) {
      return
    }

    void loadUsers()
  }, [open, role])

  const availableUsers = useMemo(() => {
    const assigned = new Set(users.map((item) => item.userGUID))
    return allUsers.filter((item) => !assigned.has(item.userGUID))
  }, [allUsers, users])

  const handleOpenAdd = async () => {
    setSelectedUserGuids([])
    setAddOpen(true)
    if (!allUsers.length) {
      await loadAvailableUsers()
    }
  }

  const handleAddUsers = async () => {
    if (!role || !selectedUserGuids.length) {
      message.warning('请选择用户')
      return
    }

    setSubmitting(true)
    try {
      await addUsersToRole(role.roleGUID, selectedUserGuids)
      message.success('添加用户成功')
      setAddOpen(false)
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error('添加用户失败')
    } finally {
      setSubmitting(false)
    }
  }

  const handleRemoveUser = async (userGuid: string) => {
    if (!role) {
      return
    }

    try {
      await removeUserFromRole(role.roleGUID, userGuid)
      message.success('移除用户成功')
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error('移除用户失败')
    }
  }

  const columns: ColumnsType<RoleUserDto> = [
    { title: '用户名', dataIndex: 'username', width: 180 },
    { title: '姓名', dataIndex: 'fullName', width: 160, render: (value) => value || '--' },
    { title: '邮箱', dataIndex: 'email', width: 240 },
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
      width: 120,
      render: (_, record) => (
        <Popconfirm
          title="确定移除此用户吗？"
          okText="确定"
          cancelText="取消"
          onConfirm={() => void handleRemoveUser(record.userGUID)}
        >
          <Button type="link" danger icon={<DeleteOutlined />}>
            移除
          </Button>
        </Popconfirm>
      ),
    },
  ]

  return (
    <>
      <Drawer
        title={role ? `角色用户管理 - ${role.roleName}` : '角色用户管理'}
        width={900}
        open={open}
        onClose={onClose}
        destroyOnHidden
        extra={
          <Button type="primary" icon={<UserAddOutlined />} onClick={() => void handleOpenAdd()}>
            添加用户
          </Button>
        }
      >
        <Table
          rowKey="userGUID"
          loading={loading}
          dataSource={users}
          columns={columns}
          pagination={{ pageSize: 10, showSizeChanger: false }}
          scroll={{ x: 820 }}
        />
      </Drawer>

      <Modal
        title="添加用户到角色"
        open={addOpen}
        onCancel={() => setAddOpen(false)}
        onOk={() => void handleAddUsers()}
        confirmLoading={submitting}
        destroyOnHidden
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Select
            mode="multiple"
            placeholder="请选择用户"
            value={selectedUserGuids}
            onChange={setSelectedUserGuids}
            showSearch
            optionFilterProp="label"
            options={availableUsers.map((item) => ({
              value: item.userGUID,
              label: `${item.username}${item.fullName ? ` / ${item.fullName}` : ''}`,
            }))}
          />
        </Space>
      </Modal>
    </>
  )
}
