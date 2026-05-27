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

console.log('menuFallback.test: ok')
