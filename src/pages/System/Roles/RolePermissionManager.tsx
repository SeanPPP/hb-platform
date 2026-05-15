import { CheckOutlined, SaveOutlined } from '@ant-design/icons'
import { Button, Card, Checkbox, Space, Spin, message } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { PermissionCategoryDto } from '../../../types/role'
import { assignPermissionsToRole, getPermissions, getRolePermissions } from '../../../services/roleService'

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
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [allPerms, rolePerms] = await Promise.all([
        getPermissions(),
        getRolePermissions(roleGuid),
      ])
      setCategories(allPerms)
      const keySet = new Set(rolePerms)
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

  const hasChanges = () => {
    if (checkedKeys.size !== originalKeys.size) return true
    for (const key of checkedKeys) {
      if (!originalKeys.has(key)) return true
    }
    return false
  }

  const handleToggle = (code: string) => {
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
                disabled={readOnly}
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
                  disabled={readOnly}
                >
                  {perm.displayName}
                </Checkbox>
              ))}
            </Space>
          </Card>
        )
      })}

      {!readOnly && hasChanges() && (
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

      {!readOnly && !hasChanges() && categories.length > 0 && (
        <div style={{ textAlign: 'center', paddingTop: 4, color: '#999' }}>
          <CheckOutlined style={{ marginRight: 6 }} />
          {t('system.roles.permUpToDate')}
        </div>
      )}
    </Space>
  )
}
