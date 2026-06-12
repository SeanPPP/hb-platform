import { Modal, Spin, Transfer, message } from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { Key } from 'react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getActiveRoles } from '../../../services/roleService'
import { assignRolesToUser, getUserRoles } from '../../../services/userService'
import type { RoleOptionDto } from '../../../types/role'
import type { UserDto } from '../../../types/user'

interface UserRoleAssignmentProps {
  open: boolean
  user: UserDto | null
  onClose: () => void
  onSuccess?: () => void
}

export default function UserRoleAssignment({ open, user, onClose, onSuccess }: UserRoleAssignmentProps) {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [allRoles, setAllRoles] = useState<RoleOptionDto[]>([])
  const [targetKeys, setTargetKeys] = useState<string[]>([])

  const loadData = async () => {
    if (!user) {
      return
    }

    setLoading(true)
    try {
      const [roles, userRoles] = await Promise.all([getActiveRoles(), getUserRoles(user.userGUID)])
      setAllRoles(roles)
      setTargetKeys(userRoles.map((item) => item.roleGUID))
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadRoleDataFailed'))
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
      await assignRolesToUser(user.userGUID, { roleGuids: targetKeys })
      message.success(t('system.users.roleAssignSuccess'))
      onSuccess?.()
      onClose()
    } catch (error) {
      console.error(error)
      message.error(t('system.users.roleAssignFailed'))
    } finally {
      setLoading(false)
    }
  }

  const handleChange = (nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
    setTargetKeys(nextTargetKeys.map(String))
  }

  return (
    <Modal
      title={user ? t('system.users.assignRoleTitle', { name: user.username }) : t('system.users.assignRoleTitleShort')}
      open={open}
      onOk={() => void handleSave()}
      onCancel={onClose}
      confirmLoading={loading}
      width={760}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        <Transfer
          dataSource={allRoles.map((role) => ({
            key: role.roleGUID,
            title: role.roleName,
            description: role.description || '',
          }))}
          targetKeys={targetKeys}
          onChange={handleChange}
          render={(item) => item.title}
          titles={[t('system.users.availableRoles'), t('system.users.assignedRolesLabel')]}
          listStyle={{ width: 320, height: 420 }}
          showSearch
        />
      </Spin>
    </Modal>
  )
}
