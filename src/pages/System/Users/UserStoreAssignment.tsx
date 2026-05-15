import { Modal, Spin, Transfer, message } from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { Key } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getStores } from '../../../services/storeService'
import { assignStoresToUser, getUserStores } from '../../../services/userService'
import type { StoreDto } from '../../../types/store'
import type { UserDto } from '../../../types/user'

interface UserStoreAssignmentProps {
  open: boolean
  user: UserDto | null
  onClose: () => void
  onSuccess?: () => void
}

export default function UserStoreAssignment({ open, user, onClose, onSuccess }: UserStoreAssignmentProps) {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [allStores, setAllStores] = useState<StoreDto[]>([])
  const [targetKeys, setTargetKeys] = useState<string[]>([])

  const sortedStores = useMemo(
    () => [...allStores].sort((a, b) => a.storeName.localeCompare(b.storeName)),
    [allStores],
  )

  const loadData = async () => {
    if (!user) {
      return
    }

    setLoading(true)
    try {
      const [stores, userStores] = await Promise.all([
        getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }),
        getUserStores(user.userGUID),
      ])
      setAllStores(stores.items)
      setTargetKeys(
        userStores
          .map((item) => item.storeGUID)
          .sort((a, b) => {
            const storeA = stores.items.find((item) => item.storeGUID === a)
            const storeB = stores.items.find((item) => item.storeGUID === b)
            if (!storeA || !storeB) {
              return 0
            }
            return storeA.storeName.localeCompare(storeB.storeName)
          }),
      )
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadStoreDataFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!open || !user) {
      return
    }

    void loadData()
  }, [open, user])

  const handleSave = async () => {
    if (!user) {
      return
    }

    setLoading(true)
    try {
      await assignStoresToUser(
        user.userGUID,
        targetKeys.map((storeGUID) => ({
          storeGUID,
          accessLevel: 'ReadWrite',
          isPrimary: false,
        })),
      )
      message.success(t('system.users.storeAssignSuccess'))
      onSuccess?.()
      onClose()
    } catch (error) {
      console.error(error)
      message.error(t('system.users.storeAssignFailed'))
    } finally {
      setLoading(false)
    }
  }

  const handleChange = (nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
    const next = nextTargetKeys.map(String).sort((a, b) => {
      const storeA = sortedStores.find((item) => item.storeGUID === a)
      const storeB = sortedStores.find((item) => item.storeGUID === b)
      if (!storeA || !storeB) {
        return 0
      }
      return storeA.storeName.localeCompare(storeB.storeName)
    })
    setTargetKeys(next)
  }

  return (
    <Modal
      title={user ? t('system.users.assignStoreTitle', { name: user.username }) : t('system.users.assignStoreTitleShort')}
      open={open}
      onOk={() => void handleSave()}
      onCancel={onClose}
      confirmLoading={loading}
      width={760}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        <Transfer
          dataSource={sortedStores.map((store) => ({
            key: store.storeGUID,
            title: `${store.storeName} (${store.storeCode})`,
            description: store.address || '',
          }))}
          targetKeys={targetKeys}
          onChange={handleChange}
          render={(item) => item.title}
          titles={[t('system.users.availableStores'), t('system.users.assignedStoresLabel')]}
          listStyle={{ width: 320, height: 420 }}
          showSearch
        />
      </Spin>
    </Modal>
  )
}
