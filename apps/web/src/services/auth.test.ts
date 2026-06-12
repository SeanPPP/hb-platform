import { normalizeCurrentUser, refreshSession } from './auth'

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
      IsActive: false,
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

assertEqual(
  userWithLegacyStoreFlags.stores?.[0]?.isActive,
  false,
  'Current user stores should normalize PascalCase inactive store status',
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
      isActive: true,
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

assertEqual(
  userWithCamelStoreFlags.stores?.[0]?.isActive,
  true,
  'Current user stores should keep camelCase active store status',
)

const originalFetch = globalThis.fetch
const refreshRequests: Array<{ input: string; init?: RequestInit }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  refreshRequests.push({ input: String(input), init })
  return new Response(JSON.stringify({ success: true, data: {} }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

assertEqual(await refreshSession(), true, 'session refresh 成功时应返回 true')
assertEqual(refreshRequests[0]?.input, '/api/Auth/session/refresh', 'session refresh 应调用刷新接口')
assertEqual(refreshRequests[0]?.init?.credentials, 'include', 'session refresh 应携带 Cookie')

globalThis.fetch = (async () =>
  new Response(JSON.stringify({ success: false, message: 'expired' }), {
    status: 401,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

assertEqual(await refreshSession(), false, 'session refresh 失败时应返回 false')

globalThis.fetch = originalFetch
