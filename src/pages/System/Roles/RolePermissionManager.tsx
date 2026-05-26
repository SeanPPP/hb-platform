import { CheckOutlined, SaveOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Space, Spin, message } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { PermissionCategoryDto, RolePermissionStateDto } from '../../../types/role'
import {
  assignPermissionsToRole,
  getPermissionCatalog,
  getRolePermissionState,
} from '../../../services/roleService'

interface RolePermissionManagerProps {
  roleGuid: string
  roleName: string
  /** Called after permissions are successfully saved */
  onChanged?: () => void
  readOnly?: boolean
}

export default function RolePermissionManager({
  roleGuid,
  roleName,
  onChanged,
  readOnly = false,
}: RolePermissionManagerProps) {
  const { t } = useTranslation()
  const [categories, setCategories] = useState<PermissionCategoryDto[]>([])
  const [checkedKeys, setCheckedKeys] = useState<Set<string>>(new Set())
  const [originalKeys, setOriginalKeys] = useState<Set<string>>(new Set())
  const [permissionState, setPermissionState] = useState<RolePermissionStateDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [permissionCatalog, rolePermissionState] = await Promise.all([
        getPermissionCatalog(),
        getRolePermissionState(roleGuid),
      ])
      setCategories(permissionCatalog.categories)
      setPermissionState(rolePermissionState)
      const keySet = new Set(
        rolePermissionState.isSuperAdmin
          ? rolePermissionState.effectivePermissionCodes
          : rolePermissionState.explicitPermissionCodes,
      )
      setCheckedKeys(keySet)
      setOriginalKeys(keySet)
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadPermsFailed'))
    } finally {
      setLoading(false)
    }
  }, [roleGuid])

  useEffect(() => {
    void loadData()
  }, [loadData])

  const isSuperAdmin = permissionState?.isSuperAdmin ?? false
  const disableEditing = readOnly || isSuperAdmin

  const hasChanges = () => {
    if (checkedKeys.size !== originalKeys.size) return true
    for (const key of checkedKeys) {
      if (!originalKeys.has(key)) return true
    }
    return false
  }

  const handleToggle = (code: string) => {
    if (disableEditing) return
    setCheckedKeys((prev) => {
      const next = new Set(prev)
      if (next.has(code)) {
        next.delete(code)
      } else {
        next.add(code)
      }
      return next
    })
  }

  const handleToggleCategory = (codes: string[]) => {
    if (disableEditing) return
    setCheckedKeys((prev) => {
      const next = new Set(prev)
      const allChecked = codes.every((c) => next.has(c))
      if (allChecked) {
        for (const c of codes) next.delete(c)
      } else {
        for (const c of codes) next.add(c)
      }
      return next
    })
  }

  const handleSave = async () => {
    if (isSuperAdmin) return
    setSaving(true)
    try {
      await assignPermissionsToRole(roleGuid, {
        permissions: Array.from(checkedKeys),
      })
      setOriginalKeys(new Set(checkedKeys))
      message.success(t('system.roles.permUpdateSuccess', { name: roleName }))
      onChanged?.()
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.permSaveFailed'))
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return <Spin />
  }

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {isSuperAdmin && (
        <Alert
          type="info"
          showIcon
          message={t('system.roles.superAdminPermissionsHint', 'Admin 默认拥有所有权限，无需分配')}
        />
      )}

      {categories.map((cat) => {
        const codes = cat.permissions.map((p) => p.name)
        const allChecked = codes.length > 0 && codes.every((c) => checkedKeys.has(c))
        const someChecked = codes.some((c) => checkedKeys.has(c))
        const indeterminate = someChecked && !allChecked

        return (
          <Card
            key={cat.category}
            size="small"
            title={
              <Checkbox
                checked={allChecked}
                indeterminate={indeterminate}
                onChange={() => handleToggleCategory(codes)}
                disabled={disableEditing}
                style={{ fontWeight: 600 }}
              >
                {cat.displayName}
              </Checkbox>
            }
          >
            <Space wrap size={[12, 8]}>
              {cat.permissions.map((perm) => (
                <Checkbox
                  key={perm.name}
                  checked={checkedKeys.has(perm.name)}
                  onChange={() => handleToggle(perm.name)}
                  disabled={disableEditing}
                >
                  {perm.displayName}
                </Checkbox>
              ))}
            </Space>
          </Card>
        )
      })}

      {!readOnly && !isSuperAdmin && hasChanges() && (
        <div style={{ textAlign: 'right', paddingTop: 8 }}>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            loading={saving}
            onClick={() => void handleSave()}
          >
            {t('system.roles.savePermissions')}
          </Button>
        </div>
      )}

      {!readOnly && isSuperAdmin && categories.length > 0 && (
        <div style={{ textAlign: 'right', paddingTop: 8 }}>
          <Button type="primary" icon={<SaveOutlined />} disabled>
            {t('system.roles.savePermissions')}
          </Button>
        </div>
      )}

      {!readOnly && !isSuperAdmin && !hasChanges() && categories.length > 0 && (
        <div style={{ textAlign: 'center', paddingTop: 4, color: '#999' }}>
          <CheckOutlined style={{ marginRight: 6 }} />
          {t('system.roles.permUpToDate')}
        </div>
      )}
    </Space>
  )
}
