import { CheckOutlined, SaveOutlined } from '@ant-design/icons'
import { Alert, Button, Checkbox, Collapse, Space, Spin, Typography, message } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { SyntheticEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { PermissionCategoryDto, RolePermissionStateDto } from '../../../types/role'
import {
  assignPermissionsToRole,
  getPermissionCatalog,
  getRolePermissionState,
} from '../../../services/roleService'
import { sortRolePermissionCategories } from './rolePermissionCategories'

interface RolePermissionManagerProps {
  roleGuid: string
  roleName: string
  /** 权限保存成功后触发 */
  onChanged?: () => void
  readOnly?: boolean
}

/**
 * 阻止事件继续冒泡到 Collapse 表头。
 * 表头整行是折叠按钮（点击、Enter、Space 都会切换展开），
 * 分组全选复选框放在表头内时必须同时拦截 click 和 keydown，
 * 否则勾选分组会连带折叠面板。
 */
const stopHeaderPropagation = (event: SyntheticEvent) => {
  event.stopPropagation()
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
  // 当前展开的权限分组 key；默认全部展开，保持与折叠功能加入前一致的可见范围
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])
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
      setExpandedKeys(permissionCatalog.categories.map((category) => category.category))
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

  const sortedCategories = useMemo(() => sortRolePermissionCategories(categories), [categories])
  const allGroupKeys = useMemo(
    () => sortedCategories.map((category) => category.category),
    [sortedCategories],
  )
  const allExpanded = allGroupKeys.length > 0 && allGroupKeys.every((key) => expandedKeys.includes(key))

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

  const handleToggleAllGroups = () => {
    setExpandedKeys(allExpanded ? [] : allGroupKeys)
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

  const collapseItems = sortedCategories.map((cat) => {
    const codes = cat.permissions.map((p) => p.name)
    const checkedCount = codes.filter((c) => checkedKeys.has(c)).length
    const allChecked = codes.length > 0 && checkedCount === codes.length
    const indeterminate = checkedCount > 0 && !allChecked

    return {
      key: cat.category,
      label: (
        // 复选框区域独立于折叠表头：勾选只切换分组权限，不触发展开/收起
        <span onClick={stopHeaderPropagation} onKeyDown={stopHeaderPropagation}>
          <Checkbox
            checked={allChecked}
            indeterminate={indeterminate}
            onChange={() => handleToggleCategory(codes)}
            disabled={disableEditing}
            style={{ fontWeight: 600 }}
          >
            {cat.displayName}
          </Checkbox>
        </span>
      ),
      // 折叠后仍能看到该分组的勾选进度
      extra: (
        <Typography.Text type="secondary" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {t('system.roles.permissionGroupSelectedCount', {
            checked: checkedCount,
            total: codes.length,
          })}
        </Typography.Text>
      ),
      children: (
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
      ),
    }
  })

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {isSuperAdmin && (
        <Alert
          type="info"
          showIcon
          message={t('system.roles.superAdminPermissionsHint', 'Admin 默认拥有所有权限，无需分配')}
        />
      )}

      <div>
        {collapseItems.length > 0 && (
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
            <Button type="link" size="small" style={{ paddingInline: 0 }} onClick={handleToggleAllGroups}>
              {allExpanded
                ? t('system.roles.collapseAllPermissionGroups', '全部收起')
                : t('system.roles.expandAllPermissionGroups', '全部展开')}
            </Button>
          </div>
        )}
        <Collapse
          size="small"
          activeKey={expandedKeys}
          onChange={(keys) => setExpandedKeys(keys)}
          items={collapseItems}
        />
      </div>

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
