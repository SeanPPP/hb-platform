import { Modal, Spin, Transfer, message } from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { Key } from 'react'
import { useEffect, useState } from 'react'
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
      message.error('加载角色分配数据失败')
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
      message.success('角色分配成功')
      onSuccess?.()
      onClose()
    } catch (error) {
      console.error(error)
      message.error('角色分配失败')
    } finally {
      setLoading(false)
    }
  }

  const handleChange = (nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
    setTargetKeys(nextTargetKeys.map(String))
  }

  return (
    <Modal
      title={user ? `为用户 "${user.username}" 分配角色` : '分配角色'}
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
          titles={['可选角色', '已分配角色']}
          listStyle={{ width: 320, height: 420 }}
          showSearch
        />
      </Spin>
    </Modal>
  )
}
