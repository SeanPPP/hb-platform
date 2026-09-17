import { getUserCashierBarcode, refreshUserCashierBarcode } from './userService'
import assert from 'node:assert/strict'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) throw new Error(`${label}. Expected: ${expectedText}, received: ${actualText}`)
}

const originalFetch = globalThis.fetch
const calls: Array<{ method: string; pathname: string; cache?: RequestCache; body?: string }> = []
const responseData = {
  exists: true,
  barcode: 'cashier-code-001',
  format: 'cashier',
  printCount: 2,
  createdAt: '2026-09-15T00:00:00Z',
  updatedAt: '2026-09-15T00:01:00Z',
}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const requestUrl = new URL(String(input), 'http://localhost')
  calls.push({
    method: init?.method ?? 'GET',
    pathname: requestUrl.pathname,
    cache: init?.cache,
    body: typeof init?.body === 'string' ? init.body : undefined,
  })
  return new Response(JSON.stringify({ success: true, data: responseData }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const userGuid = 'user/guid with spaces'
  const expectedPath = `/api/Users/guid/${encodeURIComponent(userGuid)}/cashier-barcode`
  const loaded = await getUserCashierBarcode(userGuid)
  const refreshed = await refreshUserCashierBarcode(userGuid, null)
  const replaced = await refreshUserCashierBarcode(userGuid, responseData.barcode)

  assertDeepEqual(calls.map(({ method, pathname }) => `${method} ${pathname}`), [
    `GET ${expectedPath}`,
    `POST ${expectedPath}/refresh`,
    `POST ${expectedPath}/refresh`,
  ], 'Cashier barcode service should use the encoded user-scoped endpoints')
  assertEqual(calls[0]?.cache, 'no-store', 'Cashier barcode GET should disable browser caching')
  assertEqual(calls[1]?.cache, undefined, 'Cashier barcode refresh should not use a stale cache option')
  assertEqual(calls[1]?.body, JSON.stringify({ expectedBarcode: null }), 'First generation should send a null expected barcode')
  assertEqual(calls[2]?.body, JSON.stringify({ expectedBarcode: responseData.barcode }), 'Reset should send the barcode read immediately before the mutation')
  assertDeepEqual(loaded, responseData, 'GET should unwrap the cashier barcode DTO')
  assertDeepEqual(refreshed, responseData, 'First generation should return the refreshed cashier barcode DTO')
  assertDeepEqual(replaced, responseData, 'Reset should return the refreshed cashier barcode DTO')
  assertEqual(calls.filter((call) => call.method === 'POST').length, 2, 'Each explicit refresh should issue exactly one POST')

  for (const failure of ['network', 'conflict', 'server'] as const) {
    let mutationCalls = 0
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      if (String(input).endsWith('/cashier-barcode/refresh')) {
        mutationCalls += 1
        if (failure === 'network') throw new TypeError('Failed to fetch')
        return new Response(JSON.stringify({ success: false, message: 'Unable to complete operation' }), {
          status: failure === 'conflict' ? 409 : 503,
          headers: { 'Content-Type': 'application/json' },
        })
      }
      return new Response('{}', { status: 200 })
    }) as typeof fetch
    await assert.rejects(() => refreshUserCashierBarcode('user-guid', null))
    assertEqual(mutationCalls, 1, `${failure} must not automatically repeat the mutation`)
  }
} finally {
  globalThis.fetch = originalFetch
}
