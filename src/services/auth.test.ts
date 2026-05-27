import { normalizeCurrentUser } from './auth'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const userWithLegacyStoreFlags = normalizeCurrentUser({
  userGUID: 'current-user-guid',
  username: 'storemanager',
  email: 'storemanager@example.com',
  permissions: [],
  roleNames: ['StoreManager'],
  storeNames: ['Managed Store'],
  Stores: [
    {
      StoreGUID: 'managed-store-guid',
      StoreName: 'Managed Store',
      StoreCode: 'M1',
      IsPrimary: true,
      AssignedAt: '2026-01-01T00:00:00Z',
    },
  ],
})

assertEqual(
  userWithLegacyStoreFlags.stores?.[0]?.storeGUID,
  'managed-store-guid',
  'Current user stores should normalize PascalCase store GUID',
)

assertEqual(
  userWithLegacyStoreFlags.stores?.[0]?.isManageable,
  true,
  'Current user stores should treat legacy IsPrimary as manageable',
)

const userWithCamelStoreFlags = normalizeCurrentUser({
  userGUID: 'current-user-guid',
  username: 'storemanager',
  email: 'storemanager@example.com',
  permissions: [],
  roleNames: ['StoreManager'],
  storeNames: ['Managed Store'],
  stores: [
    {
      storeGUID: 'managed-store-guid',
      storeName: 'Managed Store',
      storeCode: 'M1',
      isPrimary: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
  ],
})

assertEqual(
  userWithCamelStoreFlags.stores?.[0]?.isManageable,
  true,
  'Current user stores should treat legacy isPrimary as manageable',
)
