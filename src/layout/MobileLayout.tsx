import { useState } from 'react'
import { SafeArea } from 'antd-mobile'
import { Drawer, Menu } from 'antd'
import { useLocation, useNavigate } from 'react-router-dom'
import { buildMenus, getCurrentElement, getCurrentRoute } from '../router/routes'
import { useAuthStore } from '../store/auth'
import MobileNavBar from '../components/MobileNavBar'
import MobileTabBar from '../components/MobileTabBar'

export default function MobileLayout() {
  const [menuOpen, setMenuOpen] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const { access } = useAuthStore()

  const menus = buildMenus(access)
  const currentElement = getCurrentElement(location.pathname, access)
  const currentRoute = getCurrentRoute(location.pathname, access)

  const handleMenuClick = ({ key }: { key: string }) => {
    navigate(key)
    setMenuOpen(false)
  }

  const handleRefresh = () => {
    window.location.reload()
  }

  return (
    <div className="mobile-layout">
      <MobileNavBar onRefresh={handleRefresh} onMenuClick={() => setMenuOpen(true)} />

      <div className="mobile-content">
        {currentElement}
      </div>

      <div className="mobile-tab-bar-wrapper">
        <MobileTabBar access={access} />
        <SafeArea position="bottom" />
      </div>

      <Drawer
        title="HB Admin"
        placement="left"
        onClose={() => setMenuOpen(false)}
        open={menuOpen}
        width="80%"
        styles={{ body: { padding: 0 } }}
      >
        <Menu
          mode="inline"
          items={menus}
          selectedKeys={currentRoute?.meta.activeMenu ? [currentRoute.meta.activeMenu] : [location.pathname]}
          defaultOpenKeys={currentRoute?.parentPaths}
          onClick={handleMenuClick}
        />
      </Drawer>
    </div>
  )
}
