import {
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  ReloadOutlined,
  ShoppingOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Avatar, Breadcrumb, Button, Dropdown, Layout, Menu, Space, Typography } from 'antd'
import type { MenuProps } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import AppTabs from '../components/AppTabs'
import RouteKeepAlive, { type RouteKeepAliveRef } from '../components/RouteKeepAlive'
import { useIsMobile } from '../hooks/useIsMobile'
import MobileLayout from './MobileLayout'
import {
  buildMenus,
  getBreadcrumbItems,
  getCurrentElement,
  getCurrentRoute,
  getOpenMenuKeys,
  getSelectedMenuKeys,
  toTabItem,
} from '../router/routes'
import { useAuthStore } from '../store/auth'
import { useTabsStore } from '../store/tabs'

const { Header, Sider, Content } = Layout

function DesktopAdminLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const [openKeys, setOpenKeys] = useState<string[]>([])
  const navigate = useNavigate()
  const location = useLocation()
  const keepAliveRef = useRef<RouteKeepAliveRef>(null)

  const { currentUser, access, logout } = useAuthStore()
  const {
    tabs,
    activeKey,
    pinTabsBar,
    setActiveKey,
    ensureTab,
    removeTab,
    removeOtherTabs,
    removeLeftTabs,
    removeRightTabs,
    resetTabs,
  } = useTabsStore()

  const currentRoute = getCurrentRoute(location.pathname, access)
  const currentTab = tabs.find((item) => item.key === location.pathname || item.key === currentRoute?.path)
  const currentElement = getCurrentElement(location.pathname, access)
  const menus = useMemo(() => buildMenus(access), [access])
  const selectedKeys = getSelectedMenuKeys(location.pathname, access)
  const cacheKeys = tabs.filter((item) => item.keepAlive).map((item) => item.key)

  useEffect(() => {
    const tab = toTabItem(location.pathname, access)
    if (tab) {
      ensureTab(tab)
    }
    setActiveKey(tab?.key || location.pathname)
    setOpenKeys(getOpenMenuKeys(location.pathname, access))
  }, [access, ensureTab, location.pathname, setActiveKey])

  const handleMenuClick = ({ key }: { key: string }) => {
    navigate(key)
  }

  const handleRemoveTab = async (key: string) => {
    const nextPath = removeTab(key)
    await keepAliveRef.current?.destroy(key)
    navigate(nextPath)
  }

  const handleRemoveOtherTabs = async (key: string) => {
    const removedKeys = removeOtherTabs(key)
    if (removedKeys.length > 0) {
      await keepAliveRef.current?.destroy(removedKeys)
    }
    navigate(key)
  }

  const handleRemoveLeftTabs = async (key: string) => {
    const removedKeys = removeLeftTabs(key)
    if (removedKeys.length > 0) {
      await keepAliveRef.current?.destroy(removedKeys)
    }
    navigate(key)
  }

  const handleRemoveRightTabs = async (key: string) => {
    const removedKeys = removeRightTabs(key)
    if (removedKeys.length > 0) {
      await keepAliveRef.current?.destroy(removedKeys)
    }
    navigate(key)
  }

  const handleRefreshCurrent = () => {
    keepAliveRef.current?.refresh(activeKey)
  }

  const handleLogout = async () => {
    await logout()
    resetTabs()
    navigate('/login', { replace: true })
  }

  const userMenuItems: MenuProps['items'] = [
    {
      key: 'refresh-profile',
      label: '刷新当前页',
      icon: <ReloadOutlined />,
      onClick: handleRefreshCurrent,
    },
    {
      key: 'logout',
      label: '退出登录',
      icon: <LogoutOutlined />,
      onClick: () => {
        void handleLogout()
      },
    },
  ]

  return (
    <Layout className="admin-layout">
      <Sider className="admin-sider" trigger={null} collapsible collapsed={collapsed} width={248} theme="light">
        <div className="brand">
          <div className="brand-mark">HB</div>
          {!collapsed ? <span className="brand-text">HB Admin Platform</span> : null}
        </div>
        <Menu
          className="admin-side-menu"
          mode="inline"
          items={menus}
          selectedKeys={selectedKeys}
          openKeys={collapsed ? [] : openKeys}
          onOpenChange={(keys) => setOpenKeys(keys as string[])}
          onClick={handleMenuClick}
        />
      </Sider>

      <Layout>
        <Header className="admin-header">
          <Space size={16}>
            <Button
              type="text"
              icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
              onClick={() => setCollapsed((value) => !value)}
            />
            <Breadcrumb
              items={getBreadcrumbItems(location.pathname, access, currentTab?.title)}
            />
          </Space>

          <Space size={12}>
            <Button
              type="primary"
              icon={<ShoppingOutlined />}
              onClick={() => window.open('/shop', '_blank')}
            >
              订货前台
            </Button>
            <Dropdown menu={{ items: userMenuItems }} placement="bottomRight" trigger={['click']}>
              <Space className="header-user">
                <Avatar icon={<UserOutlined />} />
                <div className="header-user-meta">
                  <Typography.Text strong>{currentUser?.username || '--'}</Typography.Text>
                  <Typography.Text type="secondary">{currentUser?.roleNames?.[0] || '未分配角色'}</Typography.Text>
                </div>
              </Space>
            </Dropdown>
          </Space>
        </Header>

        <div className={`tabs-shell${pinTabsBar ? ' tabs-shell-pinned' : ''}`}>
          <AppTabs
            onRefreshCurrent={handleRefreshCurrent}
            onRemoveTab={handleRemoveTab}
            onRemoveOtherTabs={handleRemoveOtherTabs}
            onRemoveLeftTabs={handleRemoveLeftTabs}
            onRemoveRightTabs={handleRemoveRightTabs}
          />
        </div>

        <Content className="admin-content">
          <RouteKeepAlive
            ref={keepAliveRef}
            activeKey={activeKey}
            include={cacheKeys}
            currentElement={currentElement}
          />
        </Content>
      </Layout>
    </Layout>
  )
}

export default function AdminLayout() {
  const isMobile = useIsMobile()

  if (isMobile) {
    return <MobileLayout />
  }

  return <DesktopAdminLayout />
}
