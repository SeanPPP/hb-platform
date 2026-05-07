import { DeleteOutlined, StarFilled, StarOutlined, UserAddOutlined } from '@ant-design/icons'
import { Button, Drawer, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { getUsers } from '../../../services/userService'
import {
  addUserToStore,
  getStoreUsers,
  removeUserFromStore,
  setPrimaryUser,
} from '../../../services/storeService'
import type { StoreDto, StoreUserDto } from '../../../types/store'
import type { UserDto } from '../../../types/user'

interface StoreUserManagementProps {
  open: boolean
  store: StoreDto | null
  onClose: () => void
  onChanged?: () => void
}

export default function StoreUserManagement({ open, store, onClose, onChanged }: StoreUserManagementProps) {
  const [loading, setLoading] = useState(false)
  const [users, setUsers] = useState<StoreUserDto[]>([])
  const [addOpen, setAddOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [allUsers, setAllUsers] = useState<UserDto[]>([])
  const [selectedUserGuid, setSelectedUserGuid] = useState<string>()
  const [isPrimary, setIsPrimary] = useState(false)

  const loadUsers = async () => {
    if (!store) {
      return
    }

    setLoading(true)
    try {
      const result = await getStoreUsers({
        storeGuid: store.storeGUID,
        query: { page: 1, pageSize: 100 },
      })
      setUsers(result.items)
    } catch (error) {
      console.error(error)
      message.error('加载分店用户失败')
    } finally {
      setLoading(false)
    }
  }

  const loadAvailableUsers = async () => {
    try {
      const result = await getUsers({
        page: 1,
        pageSize: 200,
      })
      setAllUsers(result.items)
    } catch (error) {
      console.error(error)
      message.error('加载用户选项失败')
    }
  }

  useEffect(() => {
    if (!open || !store) {
      return
    }

    void loadUsers()
  }, [open, store])

  const availableUsers = useMemo(() => {
    const assigned = new Set(users.map((item) => item.userGUID))
    return allUsers.filter((item) => !assigned.has(item.userGUID))
  }, [allUsers, users])

  const handleOpenAdd = async () => {
    setSelectedUserGuid(undefined)
    setIsPrimary(false)
    setAddOpen(true)
    if (!allUsers.length) {
      await loadAvailableUsers()
    }
  }

  const handleAddUser = async () => {
    if (!store || !selectedUserGuid) {
      message.warning('请选择用户')
      return
    }

    setSubmitting(true)
    try {
      await addUserToStore(store.storeGUID, {
        userGUID: selectedUserGuid,
        isPrimary,
      })
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
    if (!store) {
      return
    }

    try {
      await removeUserFromStore(store.storeGUID, userGuid)
      message.success('移除用户成功')
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error('移除用户失败')
    }
  }

  const handleTogglePrimary = async (record: StoreUserDto) => {
    if (!store) {
      return
    }

    try {
      await setPrimaryUser(store.storeGUID, record.userGUID, !record.isPrimary)
      message.success(record.isPrimary ? '已取消主要用户' : '已设为主要用户')
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error('更新主要用户失败')
    }
  }

  const columns: ColumnsType<StoreUserDto> = [
    { title: '用户名', dataIndex: 'username', width: 160 },
    {
      title: '姓名',
      key: 'fullName',
      width: 160,
      render: (_, record) => record.fullName || record.realName || '--',
    },
    { title: '邮箱', dataIndex: 'email', width: 220 },
    {
      title: '角色',
      dataIndex: 'roles',
      render: (roles: string[]) => (roles?.length ? roles.map((item) => <Tag key={item}>{item}</Tag>) : '--'),
    },
    {
      title: '主用户',
      dataIndex: 'isPrimary',
      width: 100,
      render: (value: boolean) =>
        value ? (
          <Tag icon={<StarFilled />} color="gold">
            主要
          </Tag>
        ) : (
          <Tag>普通</Tag>
        ),
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 90,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 200,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={record.isPrimary ? <StarFilled /> : <StarOutlined />} onClick={() => void handleTogglePrimary(record)}>
            {record.isPrimary ? '取消主要' : '设为主要'}
          </Button>
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
        </Space>
      ),
    },
  ]

  return (
    <>
      <Drawer
        title={store ? `分店用户管理 - ${store.storeName}` : '分店用户管理'}
        width={960}
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
          scroll={{ x: 980 }}
        />
      </Drawer>

      <Modal
        title="添加用户到分店"
        open={addOpen}
        onCancel={() => setAddOpen(false)}
        onOk={() => void handleAddUser()}
        confirmLoading={submitting}
        destroyOnHidden
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Select
            placeholder="请选择用户"
            value={selectedUserGuid}
            onChange={setSelectedUserGuid}
            showSearch
            optionFilterProp="label"
            options={availableUsers.map((item) => ({
              value: item.userGUID,
              label: `${item.username}${item.fullName ? ` / ${item.fullName}` : ''}`,
            }))}
          />
          <Space>
            <span>设为主用户</span>
            <Switch checked={isPrimary} onChange={setIsPrimary} />
          </Space>
        </Space>
      </Modal>
    </>
  )
}
