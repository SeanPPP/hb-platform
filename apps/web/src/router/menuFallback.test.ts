import { chooseNavigationMenus } from './menuFallback'

function assertSameReference<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(message)
  }
}

const localStoreManagerMenus = [
  { key: '/dashboard' },
  {
    key: '/pos-admin',
    children: [
      { key: '/pos-admin/store-product-price' },
      { key: '/pos-admin/schedule-attendance' },
      { key: '/pos-admin/sales-orders' },
      { key: '/pos-admin/local-supplier-invoices' },
    ],
  },
]

const staleBackendMenus = [{ key: '/dashboard' }]
const completeBackendMenus = [
  { key: '/dashboard' },
  {
    key: '/pos-admin',
    children: [
      { key: '/pos-admin/store-product-price' },
      { key: '/pos-admin/schedule-attendance' },
      { key: '/pos-admin/sales-orders' },
      { key: '/pos-admin/local-supplier-invoices' },
    ],
  },
]

const warehouseStaffLocalMenus = [
  {
    key: '/warehouse',
    children: [{ key: '/warehouse/store-orders' }],
  },
]
const warehouseStaffBackendMenus = [
  {
    key: '/warehouse',
    children: [{ key: '/warehouse/store-orders' }],
  },
]
const warehouseStaffStaleBackendMenus = [{ key: '/dashboard' }]

assertSameReference(
  chooseNavigationMenus(localStoreManagerMenus, staleBackendMenus),
  localStoreManagerMenus,
  'Stale backend navigation should not hide locally authorized StoreManager menus',
)

assertSameReference(
  chooseNavigationMenus(localStoreManagerMenus, completeBackendMenus),
  completeBackendMenus,
  'Complete backend navigation should remain authoritative',
)

assertSameReference(
  chooseNavigationMenus(warehouseStaffLocalMenus, warehouseStaffBackendMenus),
  warehouseStaffBackendMenus,
  'Backend navigation with fewer authorized leaves should remain authoritative when it covers all local leaves',
)

assertSameReference(
  chooseNavigationMenus(warehouseStaffLocalMenus, warehouseStaffStaleBackendMenus),
  warehouseStaffLocalMenus,
  'Backend navigation missing a locally authorized leaf should still use the local fallback',
)

console.log('menuFallback.test: ok')
