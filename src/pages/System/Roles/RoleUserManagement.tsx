import { DeleteOutlined, UserAddOutlined } from '@ant-design/icons'
import { Button, Drawer, Modal, Popconfirm, Select, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation()
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
      message.error(t('system.roles.loadUsersFailed'))
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
      message.error(t('system.roles.loadUserOptionsFailed'))
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
      message.warning(t('system.roles.selectUser'))
      return
    }

    setSubmitting(true)
    try {
      await addUsersToRole(role.roleGUID, selectedUserGuids)
      message.success(t('system.roles.addUserSuccess'))
      setAddOpen(false)
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.addUserFailed'))
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
      message.success(t('system.roles.removeUserSuccess'))
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.removeUserFailed'))
    }
  }

  const columns: ColumnsType<RoleUserDto> = [
    { title: t('system.users.username'), dataIndex: 'username', width: 180 },
    { title: t('system.users.fullName'), dataIndex: 'fullName', width: 160, render: (value) => value || '--' },
    { title: t('system.users.email'), dataIndex: 'email', width: 240 },
    {
      title: t('column.status'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active') : t('common.inactive')}</Tag>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 120,
      render: (_, record) => (
        <Popconfirm
          title={t('system.roles.confirmRemoveUser')}
          okText={t('common.confirm')}
          cancelText={t('common.cancel')}
          onConfirm={() => void handleRemoveUser(record.userGUID)}
        >
          <Button type="link" danger icon={<DeleteOutlined />}>
            {t('system.roles.remove')}
          </Button>
        </Popconfirm>
      ),
    },
  ]

  return (
    <>
      <Drawer
        title={role ? t('system.roles.userMgmtTitle', { name: role.roleName }) : t('system.roles.userMgmtTitleShort')}
        width={900}
        open={open}
        onClose={onClose}
        destroyOnHidden
        extra={
          <Button type="primary" icon={<UserAddOutlined />} onClick={() => void handleOpenAdd()}>
            {t('system.roles.addUser')}
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
        title={t('system.roles.addUserToRole')}
        open={addOpen}
        onCancel={() => setAddOpen(false)}
        onOk={() => void handleAddUsers()}
        confirmLoading={submitting}
        destroyOnHidden
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Select
            mode="multiple"
            placeholder={t('system.roles.selectUser')}
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
