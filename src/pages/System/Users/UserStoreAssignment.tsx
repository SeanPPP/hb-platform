import { Modal, Spin, Transfer, message } from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { Key } from 'react'
import { useEffect, useMemo, useState } from 'react'
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
      message.error('加载分店分配数据失败')
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
      message.success('分店分配成功')
      onSuccess?.()
      onClose()
    } catch (error) {
      console.error(error)
      message.error('分店分配失败')
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
      title={user ? `为用户 "${user.username}" 分配分店` : '分配分店'}
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
          titles={['可选分店', '已分配分店']}
          listStyle={{ width: 320, height: 420 }}
          showSearch
        />
      </Spin>
    </Modal>
  )
}
