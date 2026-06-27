import {
  getPaymentTerminalSettings,
  saveLinklyCredential,
  saveSquareToken,
} from './paymentTerminalSettingsService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function readBody(call: { init?: RequestInit }) {
  return JSON.parse(String(call.init?.body)) as Record<string, unknown>
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({ url, init })

  return new Response(
    JSON.stringify({
      success: true,
      data: {
        square: [
          { environment: 'Production', configured: true, enabled: true },
          { environment: 'Sandbox', configured: false, enabled: false },
        ],
        stores: [{ storeCode: '001', storeName: 'City Store' }],
        selectedStoreCode: '001',
        linkly: [
          { storeCode: '001', environment: 'Production', username: 'linkly-user', hasPassword: true },
          { storeCode: '001', environment: 'Sandbox', username: '', hasPassword: false },
        ],
      },
    }),
    { status: 200, headers: { 'content-type': 'application/json' } },
  )
}) as typeof fetch

try {
  const current = await getPaymentTerminalSettings('001')
  assert(calls[0]?.url.includes('/api/react/v1/payment-terminal-settings'), 'GET URL should use settings endpoint')
  assert(calls[0]?.url.includes('storeCode=001'), 'GET URL should include storeCode query')
  assertEqual(calls[0]?.init?.method, 'GET', 'GET method should be used')
  assertEqual(current.selectedStoreCode, '001', 'GET should unwrap response data')

  await saveSquareToken({
    environment: 'Sandbox',
    accessToken: 'sandbox-secret',
    clearToken: false,
  }, '002')
  assert(calls[1]?.url.includes('/api/react/v1/payment-terminal-settings/square'), 'Square URL should be correct')
  assert(calls[1]?.url.includes('storeCode=002'), 'Square save should preserve selected store')
  assertEqual(calls[1]?.init?.method, 'PUT', 'Square save should use PUT')
  assertEqual(readBody(calls[1]).accessToken, 'sandbox-secret', 'Square save should send token')

  await saveLinklyCredential({
    storeCode: '001',
    environment: 'Production',
    username: 'linkly-user',
    password: 'linkly-password',
    clearCredential: false,
  })
  assertEqual(calls[2]?.url, '/api/react/v1/payment-terminal-settings/linkly', 'Linkly URL should be correct')
  assertEqual(calls[2]?.init?.method, 'PUT', 'Linkly save should use PUT')
  assertEqual(readBody(calls[2]).password, 'linkly-password', 'Linkly save should send password when provided')

  console.log('paymentTerminalSettingsService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
