import {
  AppstoreOutlined,
  BankOutlined,
  BuildOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  DollarOutlined,
  EnvironmentOutlined,
  FileDoneOutlined,
  FileTextOutlined,
  GiftOutlined,
  InboxOutlined,
  KeyOutlined,
  MoneyCollectOutlined,
  NumberOutlined,
  ReconciliationOutlined,
  SettingOutlined,
  ShopOutlined,
  ShoppingCartOutlined,
  TagsOutlined,
  TeamOutlined,
  TrophyOutlined,
  UserOutlined,
  WalletOutlined,
} from '@ant-design/icons'
import type { MenuProps } from 'antd'
import { matchPath } from 'react-router-dom'
import ForbiddenPage from '../pages/Forbidden'
import DashboardPage from '../pages/Dashboard'
import DomesticChinaSuppliersPage from '../pages/DomesticPurchase/ChinaSuppliers'
import DomesticProductsPage from '../pages/DomesticPurchase/DomesticProducts'
import ProductPrefixCodeManagementPage from '../pages/DomesticPurchase/ProductPrefixCodeManagement'
import ProductCreationPage from '../pages/DomesticPurchase/ProductCreation'
import ProductImportPage from '../pages/DomesticPurchase/ProductImport'
import ProductGradeManagementPage from '../pages/Warehouse/ProductGradeManagement'
import NotFoundPage from '../pages/NotFound'
import PosmSalesOrdersPage from '../pages/PosmSalesOrders'
import PosAdminCashRegisterUsersPage from '../pages/PosAdmin/CashRegisterUsers'
import PosAdminPricingStrategiesPage from '../pages/PosAdmin/PricingStrategies'
import PosAdminPromotionsPage from '../pages/PosAdmin/Promotions'
import PosAdminSupplierManagementPage from '../pages/PosAdmin/SupplierManagement'
import PosAdminProductManagementPage from '../pages/PosAdmin/ProductManagement'
import PosAdminStoreProductPricePage from '../pages/PosAdmin/StoreProductPrice'
import LocalSupplierInvoicesPage from '../pages/PosAdmin/LocalSupplierInvoices'
import LocalSupplierInvoiceDetailPage from '../pages/PosAdmin/LocalSupplierInvoiceDetailPage'
import InvoiceEditPage from '../pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit'
import SystemRolesPage from '../pages/System/Roles'
import SystemStoresPage from '../pages/System/Stores'
import SystemUsersPage from '../pages/System/Users'
import SystemPermissionsPage from '../pages/System/Permissions'
import WarehouseCategoriesPage from '../pages/Warehouse/Categories'
import WarehouseLocationsPage from '../pages/Warehouse/Locations'
import WarehouseProductsPage from '../pages/Warehouse/Products'
import StoreOrderDetailPage from '../pages/Warehouse/StoreOrders/Detail'
import StoreOrderInvoicePage from '../pages/Warehouse/StoreOrders/Invoice'
import StoreOrderPickingListPage from '../pages/Warehouse/StoreOrders/PickingList'
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
  BankOutlined: <BankOutlined />,
  BuildOutlined: <BuildOutlined />,
  DatabaseOutlined: <DatabaseOutlined />,
  DollarOutlined: <DollarOutlined />,
  EnvironmentOutlined: <EnvironmentOutlined />,
  FileDoneOutlined: <FileDoneOutlined />,
  FileTextOutlined: <FileTextOutlined />,
  GiftOutlined: <GiftOutlined />,
  InboxOutlined: <InboxOutlined />,
  UserOutlined: <UserOutlined />,
  TeamOutlined: <TeamOutlined />,
  NumberOutlined: <NumberOutlined />,
  ReconciliationOutlined: <ReconciliationOutlined />,
  SettingOutlined: <SettingOutlined />,
  ShopOutlined: <ShopOutlined />,
  ShoppingCartOutlined: <ShoppingCartOutlined />,
  TagsOutlined: <TagsOutlined />,
  MoneyCollectOutlined: <MoneyCollectOutlined />,
  KeyOutlined: <KeyOutlined />,
  TrophyOutlined: <TrophyOutlined />,
  WalletOutlined: <WalletOutlined />,
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
      icon: 'SettingOutlined',
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
      {
        path: '/system/permissions',
        meta: {
          title: '权限管理',
          icon: 'KeyOutlined',
          keepAlive: true,
          accessKey: 'canReadRole',
        },
        element: <SystemPermissionsPage />,
      },
    ],
  },
  {
    path: '/domestic-purchase',
    meta: {
      title: '国内采购',
      icon: 'ShoppingCartOutlined',
    },
    children: [
      {
        path: '/domestic-purchase/china-suppliers',
        meta: {
          title: '国内供应商',
          icon: 'BankOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <DomesticChinaSuppliersPage />,
      },
      {
        path: '/domestic-purchase/domestic-products',
        meta: {
          title: '国内商品',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canReadProduct',
        },
        element: <DomesticProductsPage />,
      },
      {
        path: '/domestic-purchase/prefix-code-management',
        meta: {
          title: '前缀管理',
          icon: 'NumberOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <ProductPrefixCodeManagementPage />,
      },
      {
        path: '/domestic-purchase/product-creation',
        meta: {
          title: '货号条码创建',
          icon: 'BuildOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <ProductCreationPage />,
      },
      {
        path: '/domestic-purchase/product-import',
        meta: {
          title: '商品导入',
          icon: 'InboxOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <ProductImportPage />,
      },
    ],
  },
  {
    path: '/warehouse',
    meta: {
      title: '仓库管理',
      icon: 'DatabaseOutlined',
    },
    children: [
      {
        path: '/warehouse/store-orders',
        meta: {
          title: '分店订货列表',
          icon: 'ReconciliationOutlined',
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
          dynamicTitle: () => '订货明细',
        },
        element: <StoreOrderDetailPage />,
      },
      {
        path: '/warehouse/store-order/picking/:id',
        meta: {
          title: '配货单',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouse',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: () => '配货单',
        },
        element: <StoreOrderPickingListPage />,
      },
      {
        path: '/warehouse/store-order/invoice/:id',
        meta: {
          title: '发票',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouse',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: () => '发票',
        },
        element: <StoreOrderInvoicePage />,
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
        path: '/warehouse/categories',
        meta: {
          title: '分类管理',
          icon: 'TagsOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <WarehouseCategoriesPage />,
      },
      {
        path: '/warehouse/locations',
        meta: {
          title: '仓库标签管理',
          icon: 'EnvironmentOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <WarehouseLocationsPage />,
      },
      {
        path: '/warehouse/product-grade-management',
        meta: {
          title: '商品等级管理',
          icon: 'TrophyOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouse',
        },
        element: <ProductGradeManagementPage />,
      },
    ],
  },
  {
    path: '/pos-admin',
    meta: {
      title: '收银管理',
      icon: 'WalletOutlined',
    },
    children: [
      {
        path: '/pos-admin/suppliers',
        meta: {
          title: '供应商管理',
          icon: 'ShopOutlined',
          keepAlive: true,
          accessKey: 'canManageStore',
        },
        element: <PosAdminSupplierManagementPage />,
      },
      {
        path: '/pos-admin/products',
        meta: {
          title: '商品管理',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canManageStore',
        },
        element: <PosAdminProductManagementPage />,
      },
      {
        path: '/pos-admin/store-product-price',
        meta: {
          title: '分店商品价格',
          icon: 'DollarOutlined',
          keepAlive: true,
          accessKey: 'canManageStore',
        },
        element: <PosAdminStoreProductPricePage />,
      },
      {
        path: '/pos-admin/pricing-strategies',
        meta: {
          title: '自动价格策略',
          icon: 'FileTextOutlined',
          keepAlive: true,
          accessKey: 'isAdmin',
        },
        element: <PosAdminPricingStrategiesPage />,
      },
      {
        path: '/pos-admin/promotions',
        meta: {
          title: '促销管理',
          icon: 'GiftOutlined',
          keepAlive: true,
          accessKey: 'isAdmin',
        },
        element: <PosAdminPromotionsPage />,
      },
      {
        path: '/pos-admin/cash-register-users',
        meta: {
          title: '收银用户条码',
          icon: 'UserOutlined',
          keepAlive: true,
          accessKey: 'canManageStore',
        },
        element: <PosAdminCashRegisterUsersPage />,
      },
      {
        path: '/pos-admin/sales-orders',
        meta: {
          title: '收银记录',
          icon: 'FileDoneOutlined',
          keepAlive: true,
          accessKey: 'canReadOrder',
        },
        element: <PosmSalesOrdersPage />,
      },
      {
        path: '/pos-admin/local-supplier-invoices',
        meta: {
          title: '分店进货单',
          icon: 'ReconciliationOutlined',
          keepAlive: true,
          accessKey: 'canManageStore',
        },
        element: <LocalSupplierInvoicesPage />,
      },
      {
        path: '/pos-admin/invoice-detail/:id',
        meta: {
          title: '进货单详情',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageStore',
          activeMenu: '/pos-admin/local-supplier-invoices',
          dynamicTitle: () => '进货单详情',
        },
        element: <LocalSupplierInvoiceDetailPage />,
      },
      {
        path: '/pos-admin/local-supplier-invoices/:id',
        meta: {
          title: '编辑进货单',
          hidden: true,
          keepAlive: true,
          accessKey: 'isAdmin',
          activeMenu: '/pos-admin/local-supplier-invoices',
          dynamicTitle: () => '编辑进货单',
        },
        element: <InvoiceEditPage />,
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
