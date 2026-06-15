import { getClientPublicIpHeaders } from './clientPublicIp'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalWindow = globalThis.window
const originalFetch = globalThis.fetch

const storage = new Map<string, string>()
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: {
    sessionStorage: {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => storage.set(key, value),
    },
    setTimeout,
    clearTimeout,
  },
})

globalThis.fetch = (async () =>
  new Response(JSON.stringify({ ip: '8.8.8.23' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

const headers = await getClientPublicIpHeaders()
assertEqual(headers['X-Client-Public-IP'], '8.8.8.23', '登录请求应带用户设备公网 IPv4')

storage.clear()
globalThis.fetch = (async () =>
  new Response(JSON.stringify({ ip: '203.0.113.23' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

const reservedHeaders = await getClientPublicIpHeaders()
assertEqual(
  Object.keys(reservedHeaders).length,
  0,
  '保留/文档 IPv4 网段不应作为用户公网 IP header',
)

storage.clear()
globalThis.fetch = (async () =>
  new Response('service unavailable', {
    status: 503,
    headers: { 'Content-Type': 'text/plain' },
  })) as typeof fetch

const failedHeaders = await getClientPublicIpHeaders()
assertEqual(
  Object.keys(failedHeaders).length,
  0,
  '公网 IP 查询失败时应返回空 headers 且不阻塞登录',
)

globalThis.fetch = originalFetch
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
