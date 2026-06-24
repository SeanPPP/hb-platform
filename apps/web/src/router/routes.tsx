import {
  AppstoreOutlined,
  BankOutlined,
  BarChartOutlined,
  BuildOutlined,
  CalendarOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  DollarOutlined,
  EnvironmentOutlined,
  FileDoneOutlined,
  FileTextOutlined,
  GiftOutlined,
  InboxOutlined,
  IdcardOutlined,
  KeyOutlined,
  MailOutlined,
  MoneyCollectOutlined,
  NumberOutlined,
  PictureOutlined,
  QrcodeOutlined,
  ReconciliationOutlined,
  ScheduleOutlined,
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
import i18n from '../i18n'
import ForbiddenPage from '../pages/Forbidden'
import DashboardPage from '../pages/Dashboard'
import DomesticChinaSuppliersPage from '../pages/DomesticPurchase/ChinaSuppliers'
import DomesticProductsPage from '../pages/DomesticPurchase/DomesticProducts'
import ProductPrefixCodeManagementPage from '../pages/DomesticPurchase/ProductPrefixCodeManagement'
import ProductCreationPage from '../pages/DomesticPurchase/ProductCreation'
import ProductImportPage from '../pages/DomesticPurchase/ProductImport'
import ProductGradeManagementPage from '../pages/Warehouse/ProductGradeManagement'
import NotFoundPage from '../pages/NotFound'
import ExecutiveSalesIntelligencePage from '../pages/ExecutiveSalesIntelligence'
import SalesDetailAnalysisPage from '../pages/ExecutiveSalesIntelligence/SalesDetailAnalysisV2'
import ProductMovementReportPage from '../pages/ExecutiveSalesIntelligence/ProductMovementReport'
import PosmSalesOrdersPage from '../pages/PosmSalesOrders'
import PosAdminCashRegisterUsersPage from '../pages/PosAdmin/CashRegisterUsers'
import PosAdminPricingStrategiesPage from '../pages/PosAdmin/PricingStrategies'
import PosAdminPromotionsPage from '../pages/PosAdmin/Promotions'
import PosAdminScheduleAttendancePage from '../pages/PosAdmin/ScheduleAttendance'
import PosAdminDeviceRegistrationPage from '../pages/PosAdmin/DeviceRegistration'
import PosAdminSupplierManagementPage from '../pages/PosAdmin/SupplierManagement'
import PosAdminProductManagementPage from '../pages/PosAdmin/ProductManagement'
import PosAdminStoreProductPricePage from '../pages/PosAdmin/StoreProductPrice'
import PosAdminAdvertisementsPage from '../pages/PosAdmin/Advertisements'
import LocalSupplierInvoicesPage from '../pages/PosAdmin/LocalSupplierInvoices'
import LocalSupplierInvoiceDetailPage from '../pages/PosAdmin/LocalSupplierInvoiceDetailPage'
import LocalSupplierInvoiceSalesAnalysisPage from '../pages/PosAdmin/LocalSupplierInvoiceSalesAnalysis'
import LocalSupplierPurchaseSalesAnalysisPage from '../pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis'
import InvoiceEditPage from '../pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit'
import SystemAppDownloadsPage from '../pages/System/AppDownloads'
import SystemCenterLogsPage from '../pages/System/CenterLogs'
import InvoiceEmailSettingsPage from '../pages/System/InvoiceEmailSettings'
import SystemScheduledStatisticsPage from '../pages/System/ScheduledStatistics'
import SystemRolesPage from '../pages/System/Roles'
import SystemStoresPage from '../pages/System/Stores'
import SystemEmployeeProfilesPage from '../pages/System/EmployeeProfiles'
import SystemUsersPage from '../pages/System/Users'
import SystemPermissionsPage from '../pages/System/Permissions'
import WarehouseCategoriesPage from '../pages/Warehouse/Categories'
import ContainerDetailPage from '../pages/Warehouse/ContainerDetail'
import ContainersPage from '../pages/Warehouse/Containers'
import WarehouseLocationsPage from '../pages/Warehouse/Locations'
import WarehouseProductsPage from '../pages/Warehouse/Products'
import StoreOrderDetailPage from '../pages/Warehouse/StoreOrders/Detail'
import StoreOrderInvoicePage from '../pages/Warehouse/StoreOrders/Invoice'
import StoreOrderPickingListPage from '../pages/Warehouse/StoreOrders/PickingList'
import StoreOrdersPage from '../pages/Warehouse/StoreOrders'
import StoreOrderImportPriceVariancePage from '../pages/Warehouse/StoreOrderImportPriceVariance'
import type { AccessControl } from '../types/auth'
import type { NavigationMenuDto } from '../types/auth'
import type { AppRouteItem, AppRouteMeta, TabItem } from '../types/router'
import { chooseNavigationMenus } from './menuFallback'
import { shouldIncludeLocalMenuRoute } from './menuVisibility'

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
  BarChartOutlined: <BarChartOutlined />,
  BuildOutlined: <BuildOutlined />,
  CalendarOutlined: <CalendarOutlined />,
  DatabaseOutlined: <DatabaseOutlined />,
  DollarOutlined: <DollarOutlined />,
  EnvironmentOutlined: <EnvironmentOutlined />,
  FileDoneOutlined: <FileDoneOutlined />,
  FileTextOutlined: <FileTextOutlined />,
  GiftOutlined: <GiftOutlined />,
  InboxOutlined: <InboxOutlined />,
  IdcardOutlined: <IdcardOutlined />,
  MailOutlined: <MailOutlined />,
  UserOutlined: <UserOutlined />,
  TeamOutlined: <TeamOutlined />,
  NumberOutlined: <NumberOutlined />,
  ReconciliationOutlined: <ReconciliationOutlined />,
  ScheduleOutlined: <ScheduleOutlined />,
  SettingOutlined: <SettingOutlined />,
  ShopOutlined: <ShopOutlined />,
  ShoppingCartOutlined: <ShoppingCartOutlined />,
  TagsOutlined: <TagsOutlined />,
  MoneyCollectOutlined: <MoneyCollectOutlined />,
  KeyOutlined: <KeyOutlined />,
  PictureOutlined: <PictureOutlined />,
  QrcodeOutlined: <QrcodeOutlined />,
  TrophyOutlined: <TrophyOutlined />,
  WalletOutlined: <WalletOutlined />,
}

export const appRoutes: AppRouteItem[] = [
  {
    path: '/dashboard',
    meta: {
      title: 'menu.dashboard',
      icon: 'DashboardOutlined',
      affix: true,
      closable: false,
      keepAlive: true,
      accessKey: 'canAccessDashboard',
    },
    element: <DashboardPage />,
  },
  {
    path: '/system',
    meta: {
      title: 'menu.system',
      icon: 'SettingOutlined',
    },
    children: [
      {
        path: '/system/stores',
        meta: {
          title: 'menu.systemStores',
          icon: 'ShopOutlined',
          keepAlive: true,
          accessKey: 'canReadStore',
        },
        element: <SystemStoresPage />,
      },
      {
        path: '/system/employee-profiles',
        meta: {
          title: 'menu.systemEmployeeProfiles',
          icon: 'IdcardOutlined',
          keepAlive: true,
          accessKey: 'canViewEmployeeProfiles',
        },
        element: <SystemEmployeeProfilesPage />,
      },
      {
        path: '/system/center-logs',
        meta: {
          title: 'menu.systemCenterLogs',
          icon: 'FileTextOutlined',
          keepAlive: true,
          accessKey: 'canViewSystemLogs',
        },
        element: <SystemCenterLogsPage />,
      },
      {
        path: '/system/scheduled-statistics',
        meta: {
          title: 'menu.scheduledStatistics',
          icon: 'ScheduleOutlined',
          keepAlive: true,
          accessKey: 'canManageScheduledTasks',
        },
        element: <SystemScheduledStatisticsPage />,
      },
      {
        path: '/system/invoice-email-settings',
        meta: {
          title: 'menu.invoiceEmailSettings',
          icon: 'MailOutlined',
          keepAlive: true,
          accessKey: 'canManageSystemSettings',
        },
        element: <InvoiceEmailSettingsPage />,
      },
      {
        path: '/system/users',
        meta: {
          title: 'menu.systemUsers',
          icon: 'UserOutlined',
          keepAlive: true,
          accessKey: 'canReadUser',
        },
        element: <SystemUsersPage />,
      },
      {
        path: '/system/roles',
        meta: {
          title: 'menu.systemRoles',
          icon: 'TeamOutlined',
          keepAlive: true,
          accessKey: 'canReadRole',
        },
        element: <SystemRolesPage />,
      },
      {
        path: '/system/permissions',
        meta: {
          title: 'menu.systemPermissions',
          icon: 'KeyOutlined',
          keepAlive: true,
          accessKey: 'canReadRole',
        },
        element: <SystemPermissionsPage />,
      },
      {
        path: '/system/device-registration',
        meta: {
          title: 'menu.deviceRegistration',
          icon: 'BuildOutlined',
          keepAlive: true,
          accessKey: 'canViewDeviceRegistration',
        },
        element: <PosAdminDeviceRegistrationPage />,
      },
      {
        path: '/system/app-downloads',
        meta: {
          title: 'menu.appDownloads',
          icon: 'QrcodeOutlined',
          keepAlive: true,
          accessKey: 'canViewAppDownloads',
        },
        element: <SystemAppDownloadsPage />,
      },
    ],
  },
  {
    path: '/domestic-purchase',
    meta: {
      title: 'menu.domesticPurchase',
      icon: 'ShoppingCartOutlined',
    },
    children: [
      {
        path: '/domestic-purchase/china-suppliers',
        meta: {
          title: 'menu.chinaSuppliers',
          icon: 'BankOutlined',
          keepAlive: true,
          accessKey: 'canManageDomesticSuppliers',
        },
        element: <DomesticChinaSuppliersPage />,
      },
      {
        path: '/domestic-purchase/domestic-products',
        meta: {
          title: 'menu.domesticProducts',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canReadProduct',
        },
        element: <DomesticProductsPage />,
      },
      {
        path: '/domestic-purchase/prefix-code-management',
        meta: {
          title: 'menu.prefixCodeManagement',
          icon: 'NumberOutlined',
          keepAlive: true,
          accessKey: 'canManageDomesticPrefixCodes',
        },
        element: <ProductPrefixCodeManagementPage />,
      },
      {
        path: '/domestic-purchase/product-creation',
        meta: {
          title: 'menu.productCreation',
          icon: 'BuildOutlined',
          keepAlive: true,
          accessKey: 'canManageDomesticProducts',
        },
        element: <ProductCreationPage />,
      },
      {
        path: '/domestic-purchase/product-import',
        meta: {
          title: 'menu.productImport',
          icon: 'InboxOutlined',
          keepAlive: true,
          accessKey: 'canManageDomesticProducts',
        },
        element: <ProductImportPage />,
      },
    ],
  },
  {
    path: '/warehouse',
    meta: {
      title: 'menu.warehouse',
      icon: 'DatabaseOutlined',
    },
    children: [
      {
        path: '/warehouse/store-orders',
        meta: {
          title: 'menu.storeOrders',
          icon: 'ReconciliationOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseOrders',
        },
        element: <StoreOrdersPage />,
      },
      {
        path: '/warehouse/store-order-import-price-variance',
        meta: {
          title: 'menu.storeOrderImportPriceVariance',
          icon: 'BarChartOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseOrders',
        },
        element: <StoreOrderImportPriceVariancePage />,
      },
      {
        path: '/warehouse/store-order/detail/:id',
        meta: {
          title: 'menu.storeOrderDetail',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouseOrders',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: () => i18n.t('menu.storeOrderDetail'),
        },
        element: <StoreOrderDetailPage />,
      },
      {
        path: '/warehouse/store-order/picking/:id',
        meta: {
          title: 'menu.pickingList',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouseOrders',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: () => i18n.t('menu.pickingList'),
        },
        element: <StoreOrderPickingListPage />,
      },
      {
        path: '/warehouse/store-order/invoice/:id',
        meta: {
          title: 'menu.invoice',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageWarehouseOrders',
          activeMenu: '/warehouse/store-orders',
          dynamicTitle: () => i18n.t('menu.invoice'),
        },
        element: <StoreOrderInvoicePage />,
      },
      {
        path: '/warehouse/containers',
        meta: {
          title: 'menu.containers',
          icon: 'InboxOutlined',
          keepAlive: true,
          accessKey: 'canViewContainers',
        },
        element: <ContainersPage />,
      },
      {
        path: '/warehouse/container/detail/:containerGuid',
        meta: {
          title: 'menu.containerDetail',
          hidden: true,
          keepAlive: true,
          accessKey: 'canViewContainers',
          activeMenu: '/warehouse/containers',
          dynamicTitle: () => i18n.t('menu.containerDetail'),
        },
        element: <ContainerDetailPage />,
      },
      {
        path: '/warehouse/products',
        meta: {
          title: 'menu.warehouseProducts',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseProducts',
        },
        element: <WarehouseProductsPage />,
      },
      {
        path: '/warehouse/categories',
        meta: {
          title: 'menu.categories',
          icon: 'TagsOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseCategories',
        },
        element: <WarehouseCategoriesPage />,
      },
      {
        path: '/warehouse/locations',
        meta: {
          title: 'menu.warehouseLocations',
          icon: 'EnvironmentOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseLocations',
        },
        element: <WarehouseLocationsPage />,
      },
      {
        path: '/warehouse/product-grade-management',
        meta: {
          title: 'menu.productGradeManagement',
          icon: 'TrophyOutlined',
          keepAlive: true,
          accessKey: 'canManageWarehouseProducts',
        },
        element: <ProductGradeManagementPage />,
      },
    ],
  },
  {
    path: '/executive-sales-intelligence',
    meta: {
      title: 'menu.executiveSalesIntelligence',
      icon: 'BarChartOutlined',
      accessKey: 'canViewSalesIntelligence',
    },
    children: [
      {
        path: '/executive-sales-intelligence/overview',
        meta: {
          title: 'menu.salesData',
          icon: 'DashboardOutlined',
          keepAlive: true,
          accessKey: 'canViewReports',
        },
        element: <ExecutiveSalesIntelligencePage />,
      },
      {
        path: '/executive-sales-intelligence/sales-detail-v2',
        meta: {
          title: 'menu.salesDetail',
          icon: 'FileTextOutlined',
          keepAlive: true,
          accessKey: 'canViewReports',
        },
        element: <SalesDetailAnalysisPage />,
      },
      {
        path: '/executive-sales-intelligence/product-movement-report',
        meta: {
          title: 'menu.productMovementReport',
          icon: 'ReconciliationOutlined',
          keepAlive: true,
          accessKey: 'canViewProductMovementReport',
        },
        element: <ProductMovementReportPage />,
      },
    ],
  },
  {
    path: '/pos-admin',
    meta: {
      title: 'menu.posAdmin',
      icon: 'WalletOutlined',
    },
    children: [
      {
        path: '/pos-admin/suppliers',
        meta: {
          title: 'menu.suppliers',
          icon: 'ShopOutlined',
          keepAlive: true,
          accessKey: 'canViewAustralianSuppliers',
        },
        element: <PosAdminSupplierManagementPage />,
      },
      {
        path: '/pos-admin/products',
        meta: {
          title: 'menu.productManagement',
          icon: 'AppstoreOutlined',
          keepAlive: true,
          accessKey: 'canViewPosProducts',
        },
        element: <PosAdminProductManagementPage />,
      },
      {
        path: '/pos-admin/store-product-price',
        meta: {
          title: 'menu.storeProductPrice',
          icon: 'DollarOutlined',
          keepAlive: true,
          accessKey: 'canManageStoreProducts',
        },
        element: <PosAdminStoreProductPricePage />,
      },
      {
        path: '/pos-admin/pricing-strategies',
        meta: {
          title: 'menu.pricingStrategies',
          icon: 'FileTextOutlined',
          keepAlive: true,
          accessKey: 'canManagePricing',
        },
        element: <PosAdminPricingStrategiesPage />,
      },
      {
        path: '/pos-admin/promotions',
        meta: {
          title: 'menu.promotions',
          icon: 'GiftOutlined',
          keepAlive: true,
          accessKey: 'canManagePromotions',
        },
        element: <PosAdminPromotionsPage />,
      },
      {
        path: '/pos-admin/advertisements',
        meta: {
          title: 'menu.advertisements',
          icon: 'PictureOutlined',
          keepAlive: true,
          accessKey: 'canManageAdvertisements',
        },
        element: <PosAdminAdvertisementsPage />,
      },
      {
        path: '/pos-admin/schedule-attendance',
        meta: {
          title: 'menu.scheduleAttendance',
          icon: 'CalendarOutlined',
          keepAlive: true,
          accessKey: 'canViewAttendanceSchedule',
        },
        element: <PosAdminScheduleAttendancePage />,
      },
      {
        path: '/pos-admin/cash-register-users',
        meta: {
          title: 'menu.cashRegisterUsers',
          icon: 'UserOutlined',
          keepAlive: true,
          accessKey: 'canManageStoreOps',
        },
        element: <PosAdminCashRegisterUsersPage />,
      },
      {
        path: '/pos-admin/sales-orders',
        meta: {
          title: 'menu.salesOrders',
          icon: 'FileDoneOutlined',
          keepAlive: true,
          accessKey: 'canReadOrder',
        },
        element: <PosmSalesOrdersPage />,
      },
      {
        path: '/pos-admin/local-supplier-invoices',
        meta: {
          title: 'menu.localSupplierInvoices',
          icon: 'ReconciliationOutlined',
          keepAlive: true,
          accessKey: 'canManageLocalPurchase',
        },
        element: <LocalSupplierInvoicesPage />,
      },
      {
        path: '/pos-admin/local-supplier-purchase-sales-analysis',
        meta: {
          title: 'menu.localSupplierPurchaseSalesAnalysis',
          icon: 'BarChartOutlined',
          keepAlive: true,
          accessKey: 'canManageLocalPurchase',
        },
        element: <LocalSupplierPurchaseSalesAnalysisPage />,
      },
      {
        path: '/pos-admin/invoice-detail/:id',
        meta: {
          title: 'menu.invoiceDetail',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageLocalPurchase',
          activeMenu: '/pos-admin/local-supplier-invoices',
          dynamicTitle: () => i18n.t('menu.invoiceDetail'),
        },
        element: <LocalSupplierInvoiceDetailPage />,
      },
      {
        path: '/pos-admin/local-supplier-invoices/:id/sales-analysis',
        meta: {
          title: 'menu.localSupplierInvoiceSalesAnalysis',
          hidden: true,
          keepAlive: true,
          accessKey: 'canManageLocalPurchase',
          activeMenu: '/pos-admin/local-supplier-invoices',
          dynamicTitle: () => i18n.t('menu.localSupplierInvoiceSalesAnalysis', '进货单销量分析'),
        },
        element: <LocalSupplierInvoiceSalesAnalysisPage />,
      },
      {
        path: '/pos-admin/local-supplier-invoices/:id',
        meta: {
          title: 'menu.editInvoice',
          hidden: true,
          keepAlive: true,
          accessKey: 'canEditLocalPurchase',
          activeMenu: '/pos-admin/local-supplier-invoices',
          dynamicTitle: () => i18n.t('menu.editInvoice'),
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

      const hasRouteChildren = Boolean(route.children?.length)
      const children = hasRouteChildren ? buildMenusInternal(route.children!, access) : undefined
      const hasChildren = Boolean(children?.length)
      const hasSelfAccess = canAccessRoute(route.meta, access)

      if (
        !shouldIncludeLocalMenuRoute({
          hasRouteChildren,
          hasVisibleChildren: hasChildren,
          hasSelfAccess,
        })
      ) {
        return null
      }

      return {
        key: route.path,
        icon: route.meta.icon ? iconMap[route.meta.icon as keyof typeof iconMap] : undefined,
        label: i18n.t(route.meta.title),
        children,
      }
    })
    .filter(Boolean) as MenuProps['items']
}

function isWarehouseStaffNavigationLimited(access: AccessControl) {
  return (
    access.isWarehouseStaff &&
    !access.isAdmin &&
    !access.isWarehouseManager &&
    (access.hasRole('WarehouseStaff') || access.hasRole('仓库员工'))
  )
}

function buildWarehouseStaffMenus(access: AccessControl): MenuProps['items'] {
  if (!access.canManageWarehouseOrders) {
    return []
  }

  // 仓库员工侧边栏只保留分店订货相关入口，旧 Warehouse.Manage 仍可用于接口权限但不展开其它导航。
  return [
    {
      key: '/warehouse',
      icon: iconMap.DatabaseOutlined,
      label: i18n.t('menu.warehouse'),
      children: [
        {
          key: '/warehouse/store-orders',
          icon: iconMap.ReconciliationOutlined,
          label: i18n.t('menu.storeOrders'),
        },
        {
          key: '/warehouse/store-order-import-price-variance',
          icon: iconMap.BarChartOutlined,
          label: i18n.t('menu.storeOrderImportPriceVariance'),
        },
      ],
    },
  ]
}

export function buildMenus(access: AccessControl, navigationMenu?: NavigationMenuDto[]) {
  const localMenus = isWarehouseStaffNavigationLimited(access)
    ? buildWarehouseStaffMenus(access)
    : buildMenusInternal(appRoutes, access)
  if (navigationMenu !== undefined) {
    return chooseNavigationMenus(localMenus, buildMenusFromBackend(navigationMenu))
  }
  return localMenus
}

function buildMenusFromBackend(nodes: NavigationMenuDto[]): MenuProps['items'] {
  return nodes.map((node) => ({
    key: node.path,
    icon: node.icon ? iconMap[node.icon as keyof typeof iconMap] : undefined,
    label: i18n.t(node.titleKey),
    children: node.children?.length ? buildMenusFromBackend(node.children) : undefined,
  }))
}

export function getBreadcrumbItems(pathname: string, access: AccessControl, currentTabTitle?: string) {
  const route = getCurrentRoute(pathname, access)
  if (!route) {
    return [{ title: i18n.t('menu.pageNotFound') }]
  }

  const titles = [...route.parentPaths]
    .map((path) => titleMap.get(path))
    .filter((item): item is string => Boolean(item))
    .map((key) => i18n.t(key))

  const currentTitle = currentTabTitle || route.meta.dynamicTitle?.(route.params) || i18n.t(route.meta.title)
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
    title: route.meta.dynamicTitle?.(route.params) || i18n.t(route.meta.title),
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
      title: i18n.t(entry.route.meta.title),
      affix: true,
      closable: false,
      keepAlive: entry.route.meta.keepAlive,
    }))
}
