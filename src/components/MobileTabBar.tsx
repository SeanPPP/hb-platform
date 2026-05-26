import {
  AppstoreOutline,
  BillOutline,
  ReceivePaymentOutline,
  SetOutline,
  ShopbagOutline,
} from 'antd-mobile-icons'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import type { AccessControl } from '../types/auth'
import { createElement, useEffect, useRef, type ReactNode } from 'react'

export type MobileTabTranslate = ReturnType<typeof useTranslation>['t']

export interface MobileTabConfig {
  key: string
  title: string
  icon: ReactNode
  path: string
  accessKey?: keyof AccessControl
}

export const getMobileTabs = (t: MobileTabTranslate): MobileTabConfig[] => [
  {
    key: 'dashboard',
    title: t('mobileTab.dashboard', '工作台'),
    icon: createElement(AppstoreOutline),
    path: '/dashboard',
  },
  {
    key: 'warehouse',
    title: t('mobileTab.warehouse', '仓库'),
    icon: createElement(ShopbagOutline),
    path: '/warehouse/store-orders',
    accessKey: 'canManageWarehouseOrders',
  },
  {
    key: 'pos-admin',
    title: t('mobileTab.posAdmin', '收银'),
    icon: createElement(ReceivePaymentOutline),
    path: '/pos-admin/suppliers',
    accessKey: 'canManageStoreProducts',
  },
  {
    key: 'domestic-purchase',
    title: t('mobileTab.domesticPurchase', '采购'),
    icon: createElement(BillOutline),
    path: '/domestic-purchase/china-suppliers',
    accessKey: 'canManageDomesticSuppliers',
  },
  {
    key: 'system',
    title: t('mobileTab.system', '系统'),
    icon: createElement(SetOutline),
    path: '/system/stores',
    accessKey: 'canReadStore',
  },
]

export function getVisibleMobileTabs(access: AccessControl, t: MobileTabTranslate): MobileTabConfig[] {
  return getMobileTabs(t).filter((tab) => {
    if (!tab.accessKey) return true
    return access[tab.accessKey] === true
  })
}

function getActiveTab(pathname: string, tabs: MobileTabConfig[]): string {
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
  const { t } = useTranslation()
  const tabs = getMobileTabs(t)
  const activeKey = getActiveTab(location.pathname, tabs)
  const scrollRef = useRef<HTMLDivElement>(null)
  const activeRef = useRef<HTMLButtonElement>(null)
  const visibleTabs = getVisibleMobileTabs(access, t)

  useEffect(() => {
    if (activeRef.current && scrollRef.current) {
      const container = scrollRef.current
      const el = activeRef.current
      const itemWidth = el.offsetWidth
      const visibleCount = Math.round(container.offsetWidth / itemWidth)
      const idx = visibleTabs.findIndex((tab) => tab.key === activeKey)
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
