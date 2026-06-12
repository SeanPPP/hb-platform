import { NavBar } from 'antd-mobile'
import { Dropdown } from 'antd'
import type { MenuProps } from 'antd'
import { LogoutOutlined, MenuOutlined, ReloadOutlined, UserOutlined } from '@ant-design/icons'
import { Avatar, Button, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router-dom'
import LanguageSwitch from './LanguageSwitch'
import { useAuthStore } from '../store/auth'
import { getCurrentRoute } from '../router/routes'
import type { AccessControl } from '../types/auth'

function getPageTitle(pathname: string, access: AccessControl): string {
  const route = getCurrentRoute(pathname, access)
  if (!route) return 'layout.brand'
  return route.meta.dynamicTitle?.(route.params) || route.meta.title || 'layout.brand'
}

interface MobileNavBarProps {
  onRefresh?: () => void
  onMenuClick?: () => void
}

export default function MobileNavBar({ onRefresh, onMenuClick }: MobileNavBarProps) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()
  const { logout } = useAuthStore()

  const title = t(getPageTitle(location.pathname, useAuthStore.getState().access))

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  const userMenuItems: MenuProps['items'] = [
    {
      key: 'refresh',
      label: t('layout.refreshPage'),
      icon: <ReloadOutlined />,
      onClick: onRefresh,
    },
    {
      key: 'logout',
      label: t('layout.logout'),
      icon: <LogoutOutlined />,
      onClick: () => void handleLogout(),
    },
  ]

  return (
    <NavBar
      className="mobile-nav-bar"
      back={null}
      onBack={() => navigate(-1)}
      left={
        <Button
          type="text"
          icon={<MenuOutlined />}
          onClick={onMenuClick}
          style={{ padding: '0 4px', fontSize: 18 }}
        />
      }
      right={
        <Space size={6}>
          <LanguageSwitch className="mobile-language-switch" size="small" compact />
          <Dropdown menu={{ items: userMenuItems }} placement="bottomRight" trigger={['click']}>
            <Avatar size={28} icon={<UserOutlined />} style={{ cursor: 'pointer' }} />
          </Dropdown>
        </Space>
      }
    >
      <span className="mobile-nav-title">{title}</span>
    </NavBar>
  )
}
