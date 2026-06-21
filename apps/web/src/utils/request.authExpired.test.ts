import request, { AUTH_EXPIRED_EVENT } from './request'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
const originalWindow = globalThis.window
let eventCount = 0
let fetchCount = 0
let replacedTo = ''
let refreshRequestHeaders: HeadersInit | undefined

Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: {
    location: {
      pathname: '/warehouse/store-orders',
      search: '',
      replace: (url: string) => {
        replacedTo = url
      },
    },
    dispatchEvent: (event: Event) => {
      if (event.type === AUTH_EXPIRED_EVENT) {
        eventCount += 1
      }
      return true
    },
    sessionStorage: {
      getItem: () => null,
      setItem: () => undefined,
    },
    setTimeout,
    clearTimeout,
  },
})

const refreshSuccessResponses = [
  new Response(JSON.stringify({ success: false, message: 'unauthorized' }), {
    status: 401,
    headers: { 'Content-Type': 'application/json' },
  }),
  new Response(JSON.stringify({ success: true, data: {} }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }),
  new Response(JSON.stringify({ ok: true }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }),
]

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  fetchCount += 1
  if (String(input).startsWith('https://api.ipify.org')) {
    return new Response(JSON.stringify({ ip: '8.8.8.88' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }
  if (String(input).includes('/api/Auth/session/refresh')) {
    refreshRequestHeaders = init?.headers
  }
  return refreshSuccessResponses.shift()!
}) as typeof fetch

const retryResult = await request<{ ok: boolean }>('/api/react/v1/store-order/sync-missing-orders', {
  method: 'POST',
  data: {},
})
assertEqual(retryResult.ok, true, 'refresh 成功后应重试原请求并返回结果')
assertEqual(eventCount, 0, 'refresh 成功后不应派发 auth-expired 事件')
assertEqual(fetchCount, 4, 'refresh 成功路径应包含原请求、公网 IP 查询、refresh、重试原请求')
assertEqual(replacedTo, '', 'refresh 成功后不应跳转登录页')
assertEqual(
  (refreshRequestHeaders as Record<string, string>)?.['X-Client-Public-IP'],
  '8.8.8.88',
  '自动 refresh 应携带用户设备公网 IPv4 header',
)

eventCount = 0
fetchCount = 0
replacedTo = ''
refreshRequestHeaders = undefined

globalThis.fetch = (async () => {
  fetchCount += 1
  return new Response(JSON.stringify({ success: false, message: 'unauthorized' }), {
    status: 401,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await request('/api/react/v1/store-order/sync-missing-orders', { method: 'POST', data: {} })
  throw new Error('401 请求应抛出 RequestError')
} catch {
  assertEqual(eventCount, 1, '认证失效时应派发 auth-expired 事件')
  assertEqual(fetchCount, 4, '认证失效路径应允许公网 IP 查询失败后继续 refresh 并派发失效事件')
}

globalThis.fetch = originalFetch
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
