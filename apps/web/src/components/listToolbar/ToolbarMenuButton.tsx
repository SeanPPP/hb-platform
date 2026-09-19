import { DownOutlined, LoadingOutlined } from '@ant-design/icons'
import { Button, Dropdown } from 'antd'
import type { MenuProps } from 'antd'
import type { ReactNode } from 'react'

export interface ToolbarMenuAction {
  key: string
  label: ReactNode
  icon?: ReactNode
  /** 为 false 时该项不出现（通常由权限决定）。 */
  visible?: boolean
  disabled?: boolean
  danger?: boolean
  onClick: () => void
}

interface ToolbarMenuButtonProps {
  label: ReactNode
  icon?: ReactNode
  actions: ToolbarMenuAction[]
  /** 菜单里有任务进行中时，按钮本身显示 loading，提示用户展开查看。 */
  loading?: boolean
}

/**
 * 页头的低频操作分组：把同一类操作收进一个下拉菜单。
 * 所有项都无权限时整个按钮不渲染，避免出现空菜单。
 */
export default function ToolbarMenuButton({ label, icon, actions, loading }: ToolbarMenuButtonProps) {
  const visibleActions = actions.filter((action) => action.visible !== false)
  if (!visibleActions.length) {
    return null
  }

  const items: MenuProps['items'] = visibleActions.map((action) => ({
    key: action.key,
    label: action.label,
    icon: action.icon,
    disabled: action.disabled,
    danger: action.danger,
  }))

  const handleClick: MenuProps['onClick'] = ({ key }) => {
    visibleActions.find((action) => action.key === key)?.onClick()
  }

  return (
    <Dropdown menu={{ items, onClick: handleClick }} trigger={['click']}>
      {/* 不用 Button 的 loading：antd 在 loading 时会吞掉点击，任务进行中菜单就打不开了；
          这里只把图标换成转圈，菜单仍可展开查看状态或使用其他入口。 */}
      <Button icon={loading ? <LoadingOutlined /> : icon} aria-busy={loading || undefined}>
        {label}
        <DownOutlined />
      </Button>
    </Dropdown>
  )
}
