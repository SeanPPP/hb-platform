import {
  createDeviceActivationCode,
  getDeviceActivationCodes,
  getDeviceActivationManageableStores,
  normalizeDeviceActivationCodeListResponse,
  revokeDeviceActivationCode,
} from './deviceActivationCodeService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function assertRejects(action: () => Promise<unknown>, label: string) {
  try {
    await action()
  } catch {
    return
  }
  throw new Error(label)
}

const normalized = normalizeDeviceActivationCodeListResponse({
  success: true,
  data: {
    Items: [{
      GrantId: 'grant-1',
      StoreCode: 'S01',
      StoreName: 'Brisbane',
      DeviceSystem: 'Windows',
      Status: 'Available',
      CreatedAtUtc: '2026-08-27T01:00:00Z',
      CreatedBy: 'admin',
      Reason: 'new register',
      ExpiresAtUtc: '2026-08-28T01:00:00Z',
    }],
    Total: 1,
    Page: 1,
    PageSize: 20,
    TotalPages: 1,
  },
})

assertEqual(normalized.items[0]?.grantId, 'grant-1', 'Should normalize grant ID')
assertEqual(normalized.items[0]?.storeName, 'Brisbane', 'Should normalize store name')
assertEqual(normalized.items[0]?.status, 'Available', 'Should normalize grant status')
assertEqual(normalized.total, 1, 'Should normalize pagination total')

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; method?: string; body?: string }> = []
let rejectNextList = false

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({
    url,
    method: init?.method,
    body: typeof init?.body === 'string' ? init.body : undefined,
  })

  if (rejectNextList) {
    rejectNextList = false
    return new Response(JSON.stringify({
      success: false,
      errorCode: 'DEVICE_ACTIVATION_STORE_SCOPE_FORBIDDEN',
      message: 'No manageable stores',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }

  if (url.endsWith('/manageable-stores')) {
    return new Response(JSON.stringify({
      success: true,
      data: [{ storeCode: 'S01', storeName: 'Brisbane' }],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }

  if (url.endsWith('/revoke')) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        grantId: 'grant-1',
        storeCode: 'S01',
        deviceSystem: 'Windows',
        status: 'Revoked',
        createdAtUtc: '2026-08-27T01:00:00Z',
        createdBy: 'admin',
        reason: 'new register',
        expiresAtUtc: '2026-08-28T01:00:00Z',
        revokedAtUtc: '2026-08-27T02:00:00Z',
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }

  if (init?.method === 'POST') {
    return new Response(JSON.stringify({
      success: true,
      data: {
        activationCode: 'HBDEV1-00000000000000000000000000-11111111111111111111111111',
        grant: {
          grantId: 'grant-2',
          storeCode: 'S01',
          storeName: 'Brisbane',
          deviceSystem: 'iPadOS',
          status: 'Available',
          createdAtUtc: '2026-08-27T01:00:00Z',
          createdBy: 'admin',
          reason: 'new ipad',
          expiresAtUtc: '2026-08-28T01:00:00Z',
        },
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }

  return new Response(JSON.stringify({
    success: true,
    data: {
      items: [{
        grantId: 'grant-1',
        storeCode: 'S01',
        deviceSystem: 'Windows',
        status: 'Available',
        createdAtUtc: '2026-08-27T01:00:00Z',
        createdBy: 'admin',
        reason: 'new register',
        expiresAtUtc: '2026-08-28T01:00:00Z',
      }],
      total: 1,
      page: 2,
      pageSize: 30,
      totalPages: 1,
    },
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}) as typeof fetch

try {
  await getDeviceActivationCodes({
    page: 2,
    pageSize: 30,
    storeCode: 'S01',
    deviceSystem: 'Windows',
    status: 'Available',
  })
  const stores = await getDeviceActivationManageableStores()
  const created = await createDeviceActivationCode({
    storeCode: 'S01',
    deviceSystem: 'iPadOS',
    validForMinutes: 1440,
    reason: ' new ipad ',
  })
  const revoked = await revokeDeviceActivationCode('grant-1', ' no longer needed ')

  assertEqual(
    calls[0]?.url,
    '/api/react/v1/device-activation-codes?page=2&pageSize=30&storeCode=S01&deviceSystem=Windows&status=Available',
    'List should use the approved route and filters',
  )
  assertEqual(
    calls[1]?.url,
    '/api/react/v1/device-activation-codes/manageable-stores',
    'Store options should use the scoped route',
  )
  assertEqual(stores[0]?.storeCode, 'S01', 'Store options should be returned')
  assertEqual(
    calls[2]?.body,
    JSON.stringify({
      storeCode: 'S01',
      deviceSystem: 'iPadOS',
      validForMinutes: 1440,
      reason: 'new ipad',
    }),
    'Create should trim reason and preserve the approved TTL',
  )
  assertEqual(created.grant.grantId, 'grant-2', 'Create should normalize the grant')
  assertEqual(created.activationCode.startsWith('HBDEV1-'), true, 'Create should return one-time code')
  assertEqual(
    calls[3]?.url,
    '/api/react/v1/device-activation-codes/grant-1/revoke',
    'Revoke should use the grant route',
  )
  assertEqual(
    calls[3]?.body,
    JSON.stringify({ reason: 'no longer needed' }),
    'Revoke should trim its reason',
  )
  assertEqual(revoked.status, 'Revoked', 'Revoke should return updated status')

  rejectNextList = true
  await assertRejects(
    () => getDeviceActivationCodes({ page: 1, pageSize: 30 }),
    'HTTP 200 business rejection must not be normalized into an empty activation-code list',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('deviceActivationCodeService.test: ok')
