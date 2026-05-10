import {
  AppstoreOutline,
  BillOutline,
  ReceivePaymentOutline,
  SetOutline,
  ShopbagOutline,
} from 'antd-mobile-icons'
import { useLocation, useNavigate } from 'react-router-dom'
import type { AccessControl } from '../types/auth'
import { useEffect, useRef } from 'react'

interface TabConfig {
  key: string
  title: string
  icon: React.ReactNode
  path: string
  accessKey?: keyof AccessControl
}

const tabs: TabConfig[] = [
  {
    key: 'dashboard',
    title: '工作台',
    icon: <AppstoreOutline />,
    path: '/dashboard',
  },
  {
    key: 'warehouse',
    title: '仓库',
    icon: <ShopbagOutline />,
    path: '/warehouse/store-orders',
    accessKey: 'canManageWarehouse',
  },
  {
    key: 'pos-admin',
    title: '收银',
    icon: <ReceivePaymentOutline />,
    path: '/pos-admin/suppliers',
    accessKey: 'canManageStore',
  },
  {
    key: 'domestic-purchase',
    title: '采购',
    icon: <BillOutline />,
    path: '/domestic-purchase/china-suppliers',
    accessKey: 'canManageWarehouse',
  },
  {
    key: 'system',
    title: '系统',
    icon: <SetOutline />,
    path: '/system/stores',
    accessKey: 'canReadStore',
  },
]

function getActiveTab(pathname: string): string {
  for (const tab of tabs) {
    if (pathname === tab.path || pathname.startsWith('/' + tab.key + '/')) {
      return tab.key
    }
    if (tab.key === 'dashboard' && pathname === '/dashboard') {
      return tab.key
    }
  }
  return 'dashboard'
}

interface MobileTabBarProps {
  access: AccessControl
}

export default function MobileTabBar({ access }: MobileTabBarProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const activeKey = getActiveTab(location.pathname)
  const scrollRef = useRef<HTMLDivElement>(null)
  const activeRef = useRef<HTMLButtonElement>(null)

  const visibleTabs = tabs.filter((tab) => {
    if (!tab.accessKey) return true
    return access[tab.accessKey] === true
  })

  useEffect(() => {
    if (activeRef.current && scrollRef.current) {
      const container = scrollRef.current
      const el = activeRef.current
      const itemWidth = el.offsetWidth
      const visibleCount = Math.round(container.offsetWidth / itemWidth)
      const idx = visibleTabs.findIndex((t) => t.key === activeKey)
      const page = Math.floor(idx / visibleCount)
      container.scrollTo({ left: page * visibleCount * itemWidth, behavior: 'smooth' })
    }
  }, [activeKey, visibleTabs])

  return (
    <div className="mobile-scroll-tab-bar" ref={scrollRef}>
      {visibleTabs.map((tab) => {
        const isActive = tab.key === activeKey
        return (
          <button
            key={tab.key}
            ref={isActive ? activeRef : undefined}
            className={`mobile-scroll-tab-item${isActive ? ' active' : ''}`}
            onClick={() => navigate(tab.path, { replace: true })}
          >
            <span className="mobile-scroll-tab-icon">{tab.icon}</span>
            <span className="mobile-scroll-tab-title">{tab.title}</span>
          </button>
        )
      })}
    </div>
  )
}
