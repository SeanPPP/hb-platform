import request, { AUTH_EXPIRED_EVENT } from './request'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
const originalWindow = globalThis.window
const originalWindowSetTimeout = globalThis.window?.setTimeout
let eventCount = 0
let fetchCount = 0
let apiRequestCount = 0
let replacedTo = ''
let refreshRequestHeaders: HeadersInit | undefined
const storage = new Map<string, string>()

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
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => storage.set(key, value),
    },
    setTimeout,
    clearTimeout,
  },
})

storage.set(
  'hbweb:client-public-ipv4',
  JSON.stringify({ ip: '8.8.8.88', expiresAt: Date.now() + 5 * 60 * 1000 }),
)
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
assertEqual(fetchCount, 3, '有效缓存 refresh 路径应只包含原请求、refresh、重试原请求')
assertEqual(replacedTo, '', 'refresh 成功后不应跳转登录页')
assertEqual(
  (refreshRequestHeaders as Record<string, string>)?.['X-Client-Public-IP'],
  '8.8.8.88',
  '自动 refresh 应携带用户设备公网 IPv4 header',
)

eventCount = 0
fetchCount = 0
apiRequestCount = 0
replacedTo = ''
refreshRequestHeaders = undefined
storage.clear()

// 公网 IP 辅助服务永久挂起时，refresh 也必须立即发出并结束认证守卫流程。
globalThis.window.setTimeout = ((callback: TimerHandler) => {
  queueMicrotask(() => (callback as () => void)())
  return 1
}) as typeof setTimeout

globalThis.fetch = (async (input: RequestInfo | URL) => {
  fetchCount += 1
  if (String(input).startsWith('https://api.ipify.org') || String(input).startsWith('https://checkip.amazonaws.com')) {
    return await new Promise<Response>(() => undefined)
  }
  apiRequestCount += 1
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
  assertEqual(apiRequestCount, 2, '无缓存且公网 IP 服务挂起时应立即完成原请求与 refresh')
  assertEqual(fetchCount >= 2, true, '后台公网 IP 查询可以继续运行但不应阻塞原请求与 refresh')
}

globalThis.fetch = originalFetch
if (originalWindowSetTimeout) {
  globalThis.window.setTimeout = originalWindowSetTimeout
}
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
