import {
  AppstoreOutlined,
  DashboardOutlined,
  ShopOutlined,
  TagsOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import type { MenuProps } from 'antd'
import { matchPath } from 'react-router-dom'
import ForbiddenPage from '../pages/Forbidden'
import DashboardPage from '../pages/Dashboard'
import NotFoundPage from '../pages/NotFound'
import SystemRolesPage from '../pages/System/Roles'
import SystemStoresPage from '../pages/System/Stores'
import SystemUsersPage from '../pages/System/Users'
import WarehouseLocationsPage from '../pages/Warehouse/Locations'
import WarehouseProductsPage from '../pages/Warehouse/Products'
import StoreOrderDetailPage from '../pages/Warehouse/StoreOrders/Detail'
import StoreOrdersPage from '../pages/Warehouse/StoreOrders'
import type { AccessControl } from '../types/auth'
import type { AppRouteItem, AppRouteMeta, TabItem } from '../types/router'

export interface ResolvedRoute extends AppRouteItem {
  parentPaths: string[]
  params: Record<string, string>
}

interface LeafEntry {
  route: AppRouteItem
  parentPaths: string[]
}

const iconMap = {
  DashboardOutlined: <DashboardOutlined />,
  AppstoreOutlined: <AppstoreOutlined />,
  UserOutlined: <UserOutlined />,
  TeamOutlined: <TeamOutlined />,
  ShopOutlined: <ShopOutlined />,
  TagsOutlined: <TagsOutlined />,
}

export const appRoutes: AppRouteItem[] = [
  {
    path: '/dashboard',
    meta: {
      title: '工作台',
      icon: 'DashboardOutlined',
      affix: true,
      closable: false,
      keepAlive: true,
    },
    element: <DashboardPage />,
  },
  {
    path: '/system',
    meta: {
      title: '系统管理',
      icon: 'AppstoreOutlined',
    },
    children: [
      {
        path: '/system/stores',
        meta: {
          title: '分店管理',
          icon: 'ShopOutlined',
          keepAlive: true,
          accessKey: 'canReadStore',
        },
        element: <SystemStoresPage />,
      },
      {
        path: '/system/users',
        meta: {
          title: '用户管理',
          icon: 'UserOutlined',
          keepAlive: true,
          accessKey: 'canReadUser',
        },
        element: <SystemUsersPage />,
      },
      {
        path: '/system/roles',
        meta: {
          title: '角色管理',
          icon: 'TeamOutlined',
          keepAlive: true,
          accessKey: 'canReadRole',
        },
        element: <SystemRolesPage />,
      },
    ],
  },
  {
    path: '/warehouse',
    meta: {
      title: '仓库管理',
      icon: 'TagsOutlined',
    },
    children: [
      {
        path: '/warehouse/store-orders',
        meta: {
          title: '分店订货列表',
          icon: 'ShopOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <StoreOrdersPage />,
      },
      {
        path: '/warehouse/store-order/detail/:id',
        meta: {
          title: '订货明细',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouse',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: (params) => `订货明细 - ${params.id || ''}`,
        },
        element: <StoreOrderDetailPage />,
      },
      {
        path: '/warehouse/products',
        meta: {
          title: '仓库商品管理',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <WarehouseProductsPage />,
      },
      {
        path: '/warehouse/locations',
        meta: {
          title: '仓库标签管理',
          icon: 'TagsOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <WarehouseLocationsPage />,
      },
    ],
  },
]

function flattenAllRoutes(routes: AppRouteItem[]): AppRouteItem[] {
  return routes.flatMap((route) => {
    if (route.children?.length) {
      return [route, ...flattenAllRoutes(route.children)]
    }
    return [route]
  })
}

function flattenLeafRoutes(routes: AppRouteItem[], parentPaths: string[] = []): LeafEntry[] {
  return routes.flatMap((route) => {
    const currentPaths = [...parentPaths, route.path]
    if (route.children?.length) {
      return flattenLeafRoutes(route.children, currentPaths)
    }
    return [{ route, parentPaths }]
  })
}

const leafEntries = flattenLeafRoutes(appRoutes)
const allRoutes = flattenAllRoutes(appRoutes)
const titleMap = new Map(allRoutes.map((route) => [route.path, route.meta.title]))

function canAccessRoute(meta: AppRouteMeta, access: AccessControl) {
  if (!meta.accessKey) {
    return true
  }
  return access[meta.accessKey] === true
}

export function resolveRoute(pathname: string) {
  for (const entry of leafEntries) {
    const matched = matchPath({ path: entry.route.path, end: true }, pathname)
    if (matched) {
      return {
        ...entry.route,
        parentPaths: entry.parentPaths,
        params: matched.params as Record<string, string>,
      } satisfies ResolvedRoute
    }
  }
  return null
}

export function getCurrentRoute(pathname: string, access: AccessControl) {
  const route = resolveRoute(pathname)
  if (!route) {
    return null
  }

  if (!canAccessRoute(route.meta, access)) {
    return {
      ...route,
      element: <ForbiddenPage />,
    }
  }

  return route
}

export function getCurrentElement(pathname: string, access: AccessControl) {
  return getCurrentRoute(pathname, access)?.element ?? <NotFoundPage />
}

export function getOpenMenuKeys(pathname: string, access: AccessControl) {
  return getCurrentRoute(pathname, access)?.parentPaths ?? []
}

export function getSelectedMenuKeys(pathname: string, access: AccessControl) {
  const route = getCurrentRoute(pathname, access)
  if (!route) {
    return []
  }
  return [route.meta.activeMenu || route.path]
}

function buildMenusInternal(routes: AppRouteItem[], access: AccessControl): MenuProps['items'] {
  return routes
    .map((route) => {
      if (route.meta.hidden) {
        return null
      }

      const children = route.children ? buildMenusInternal(route.children, access) : undefined
      const hasChildren = Boolean(children?.length)
      const hasSelfAccess = canAccessRoute(route.meta, access)

      if (!hasSelfAccess && !hasChildren) {
        return null
      }

      return {
        key: route.path,
        icon: route.meta.icon ? iconMap[route.meta.icon as keyof typeof iconMap] : undefined,
        label: route.meta.title,
        children,
      }
    })
    .filter(Boolean) as MenuProps['items']
}

export function buildMenus(access: AccessControl) {
  return buildMenusInternal(appRoutes, access)
}

export function getBreadcrumbItems(pathname: string, access: AccessControl, currentTabTitle?: string) {
  const route = getCurrentRoute(pathname, access)
  if (!route) {
    return [{ title: '页面不存在' }]
  }

  const titles = [...route.parentPaths]
    .map((path) => titleMap.get(path))
    .filter((item): item is string => Boolean(item))

  const currentTitle = currentTabTitle || route.meta.dynamicTitle?.(route.params) || route.meta.title
  return [...titles, currentTitle].map((title) => ({ title }))
}

export function toTabItem(pathname: string, access: AccessControl): TabItem | null {
  const route = getCurrentRoute(pathname, access)
  if (!route) {
    return null
  }

  return {
    key: route.meta.affix ? route.path : pathname,
    path: pathname,
    routePath: route.path,
    title: route.meta.dynamicTitle?.(route.params) || route.meta.title,
    affix: route.meta.affix,
    closable: route.meta.closable !== false,
    keepAlive: route.meta.keepAlive,
  }
}

export function getAffixTabs(): TabItem[] {
  return leafEntries
    .filter((entry) => entry.route.meta.affix)
    .map((entry) => ({
      key: entry.route.path,
      path: entry.route.path,
      routePath: entry.route.path,
      title: entry.route.meta.title,
      affix: true,
      closable: false,
      keepAlive: entry.route.meta.keepAlive,
    }))
}
