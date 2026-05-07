import { CloseOutlined, MoreOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, Dropdown, Space, Tabs } from 'antd'
import type { MenuProps } from 'antd'
import { useNavigate } from 'react-router-dom'
import type { TabItem } from '../types/router'
import { useTabsStore } from '../store/tabs'

interface AppTabsProps {
  onRefreshCurrent: () => void
  onRemoveTab: (key: string) => void
  onRemoveOtherTabs: (key: string) => void
  onRemoveLeftTabs: (key: string) => void
  onRemoveRightTabs: (key: string) => void
}

export default function AppTabs({
  onRefreshCurrent,
  onRemoveTab,
  onRemoveOtherTabs,
  onRemoveLeftTabs,
  onRemoveRightTabs,
}: AppTabsProps) {
  const navigate = useNavigate()
  const { tabs, activeKey, setActiveKey } = useTabsStore()

  const currentTab = tabs.find((item) => item.key === activeKey)

  const menuItems: MenuProps['items'] = [
    {
      key: 'refresh',
      label: '刷新当前页签',
      icon: <ReloadOutlined />,
      onClick: onRefreshCurrent,
    },
    {
      key: 'closeOthers',
      label: '关闭其他页签',
      icon: <CloseOutlined />,
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveOtherTabs(currentTab.key),
    },
    {
      key: 'closeLeft',
      label: '关闭左侧页签',
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveLeftTabs(currentTab.key),
    },
    {
      key: 'closeRight',
      label: '关闭右侧页签',
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveRightTabs(currentTab.key),
    },
  ]

  return (
    <div className="app-tabs">
      <Tabs
        hideAdd
        type="editable-card"
        activeKey={activeKey}
        items={tabs.map((tab: TabItem) => ({
          key: tab.key,
          label: tab.title,
          closable: tab.closable !== false,
        }))}
        onChange={(key) => {
          setActiveKey(key)
          navigate(key)
        }}
        onEdit={(targetKey, action) => {
          if (action === 'remove') {
            onRemoveTab(targetKey as string)
          }
        }}
      />
      <Space size={8}>
        <Button icon={<ReloadOutlined />} onClick={onRefreshCurrent}>
          刷新
        </Button>
        <Dropdown menu={{ items: menuItems }} placement="bottomRight" trigger={['click']}>
          <Button icon={<MoreOutlined />} />
        </Dropdown>
      </Space>
    </div>
  )
}
