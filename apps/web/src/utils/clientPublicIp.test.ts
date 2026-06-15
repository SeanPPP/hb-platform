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
  new Response(JSON.stringify({ ip: '198.51.100.23' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

const headers = await getClientPublicIpHeaders()
assertEqual(headers['X-Client-Public-IP'], '198.51.100.23', '登录请求应带用户设备公网 IPv4')

globalThis.fetch = originalFetch
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
