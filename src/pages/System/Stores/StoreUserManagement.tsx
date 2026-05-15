import { DeleteOutlined, StarFilled, StarOutlined, UserAddOutlined } from '@ant-design/icons'
import { Button, Drawer, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation()
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
      message.error(t('system.stores.loadUsersFailed'))
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
      message.error(t('system.stores.loadUserOptionsFailed'))
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
      message.warning(t('system.stores.selectUser'))
      return
    }

    setSubmitting(true)
    try {
      await addUserToStore(store.storeGUID, {
        userGUID: selectedUserGuid,
        isPrimary,
      })
      message.success(t('system.stores.addUserSuccess'))
      setAddOpen(false)
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.addUserFailed'))
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
      message.success(t('system.stores.removeUserSuccess'))
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.removeUserFailed'))
    }
  }

  const handleTogglePrimary = async (record: StoreUserDto) => {
    if (!store) {
      return
    }

    try {
      await setPrimaryUser(store.storeGUID, record.userGUID, !record.isPrimary)
      message.success(record.isPrimary ? t('system.stores.cancelPrimarySuccess') : t('system.stores.setPrimarySuccess'))
      await loadUsers()
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.updatePrimaryFailed'))
    }
  }

  const columns: ColumnsType<StoreUserDto> = [
    { title: t('system.users.username'), dataIndex: 'username', width: 160 },
    {
      title: t('system.users.fullName'),
      key: 'fullName',
      width: 160,
      render: (_, record) => record.fullName || record.realName || '--',
    },
    { title: t('system.users.email'), dataIndex: 'email', width: 220 },
    {
      title: t('system.users.roles'),
      dataIndex: 'roles',
      render: (roles: string[]) => (roles?.length ? roles.map((item) => <Tag key={item}>{item}</Tag>) : '--'),
    },
    {
      title: t('system.stores.primaryUser'),
      dataIndex: 'isPrimary',
      width: 100,
      render: (value: boolean) =>
        value ? (
          <Tag icon={<StarFilled />} color="gold">
            {t('system.stores.primary')}
          </Tag>
        ) : (
          <Tag>{t('system.stores.normal')}</Tag>
        ),
    },
    {
      title: t('column.status'),
      dataIndex: 'isActive',
      width: 90,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active') : t('common.inactive')}</Tag>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 200,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={record.isPrimary ? <StarFilled /> : <StarOutlined />} onClick={() => void handleTogglePrimary(record)}>
            {record.isPrimary ? t('system.stores.cancelPrimary') : t('system.stores.setPrimary')}
          </Button>
          <Popconfirm
            title={t('system.stores.confirmRemoveUser')}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
            onConfirm={() => void handleRemoveUser(record.userGUID)}
          >
            <Button type="link" danger icon={<DeleteOutlined />}>
              {t('system.stores.remove')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <>
      <Drawer
        title={store ? t('system.stores.userMgmtTitle', { name: store.storeName }) : t('system.stores.userMgmtTitleShort')}
        width={960}
        open={open}
        onClose={onClose}
        destroyOnHidden
        extra={
          <Button type="primary" icon={<UserAddOutlined />} onClick={() => void handleOpenAdd()}>
            {t('system.stores.addUser')}
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
        title={t('system.stores.addUserToStore')}
        open={addOpen}
        onCancel={() => setAddOpen(false)}
        onOk={() => void handleAddUser()}
        confirmLoading={submitting}
        destroyOnHidden
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Select
            placeholder={t('system.stores.selectUser')}
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
            <span>{t('system.stores.setAsPrimary')}</span>
            <Switch checked={isPrimary} onChange={setIsPrimary} />
          </Space>
        </Space>
      </Modal>
    </>
  )
}
