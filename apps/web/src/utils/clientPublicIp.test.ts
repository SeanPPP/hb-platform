import { getClientPublicIpHeaders } from './clientPublicIp'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalWindow = globalThis.window
const originalFetch = globalThis.fetch

const storage = new Map<string, string>()
let releaseLookup: ((response: Response) => void) | undefined
let lookupCount = 0
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

globalThis.fetch = (async () => {
  lookupCount += 1
  return await new Promise<Response>((resolve) => {
    releaseLookup = resolve
  })
}) as typeof fetch

const firstLookup = getClientPublicIpHeaders()
const secondLookup = getClientPublicIpHeaders()
const immediateHeaders = await firstLookup
await secondLookup
assertEqual(Object.keys(immediateHeaders).length, 0, '公网 IP 查询不应阻塞登录请求')
assertEqual(lookupCount, 1, '并发公网 IP 查询应复用同一个后台请求')

releaseLookup?.(
  new Response(JSON.stringify({ ip: '8.8.8.23' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }),
)
for (let attempt = 0; attempt < 10 && !storage.size; attempt += 1) {
  await new Promise((resolve) => setTimeout(resolve, 0))
}
const cachedHeaders = await getClientPublicIpHeaders()
assertEqual(cachedHeaders['X-Client-Public-IP'], '8.8.8.23', '后台查询完成后后续请求应带缓存公网 IPv4')
assertEqual(lookupCount, 1, '有效缓存命中时不应再次查询公网 IP')

storage.clear()
let timeoutCallback: (() => void) | undefined
let bodyReadStarted = false
let lookupSignal: AbortSignal | undefined
let bodyLookupCalls = 0
let timeoutCount = 0
const windowSetTimeout = window.setTimeout
window.setTimeout = ((callback: TimerHandler) => {
  timeoutCount += 1
  if (timeoutCount === 1) {
    timeoutCallback = callback as () => void
  }
  return timeoutCount
}) as typeof setTimeout
globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
  lookupSignal = init?.signal ?? undefined
  bodyLookupCalls += 1
  if (bodyLookupCalls === 1) {
    return {
      ok: true,
      text: () => {
        bodyReadStarted = true
        return new Promise<string>(() => undefined)
      },
    } as Response
  }
  return new Response(JSON.stringify({ ip: '8.8.8.24' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

const hangingBodyHeaders = await getClientPublicIpHeaders()
assertEqual(Object.keys(hangingBodyHeaders).length, 0, '响应体挂起时登录仍应立即返回空 headers')
await new Promise((resolve) => globalThis.setTimeout(resolve, 0))
assertEqual(bodyReadStarted, true, '公网 IP 查询应读取响应体')
timeoutCallback?.()
assertEqual(lookupSignal?.aborted, true, '响应体超时应中止公网 IP 查询')
for (let attempt = 0; attempt < 10 && bodyLookupCalls < 2; attempt += 1) {
  await new Promise((resolve) => globalThis.setTimeout(resolve, 0))
}
assertEqual(bodyLookupCalls, 2, '响应体超时后应释放 single-flight 并继续尝试备用服务')
const recoveredHeaders = await getClientPublicIpHeaders()
assertEqual(recoveredHeaders['X-Client-Public-IP'], '8.8.8.24', '备用公网 IP 服务成功后应写入缓存')
window.setTimeout = windowSetTimeout

const originalDateNow = Date.now
Date.now = () => originalDateNow() + 60_000
storage.clear()
let reservedCalls = 0
globalThis.fetch = (async () => {
  reservedCalls += 1
  return new Response(JSON.stringify({ ip: '203.0.113.23' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

const reservedHeaders = await getClientPublicIpHeaders()
assertEqual(
  Object.keys(reservedHeaders).length,
  0,
  '保留/文档 IPv4 网段不应作为用户公网 IP header',
)
for (let attempt = 0; attempt < 10 && reservedCalls < 2; attempt += 1) {
  await new Promise((resolve) => globalThis.setTimeout(resolve, 0))
}
assertEqual(reservedCalls, 2, '无效公网 IPv4 应检查完两个服务并结束后台查询')
assertEqual(storage.size, 0, '保留/文档 IPv4 不应写入缓存')

Date.now = () => originalDateNow() + 120_000
storage.clear()
let failureCalls = 0
globalThis.fetch = (async () => {
  failureCalls += 1
  return new Response('service unavailable', {
    status: 503,
    headers: { 'Content-Type': 'text/plain' },
  })
}) as typeof fetch

const failedHeaders = await getClientPublicIpHeaders()
assertEqual(
  Object.keys(failedHeaders).length,
  0,
  '公网 IP 查询失败时应返回空 headers 且不阻塞登录',
)
for (let attempt = 0; attempt < 10 && failureCalls < 2; attempt += 1) {
  await new Promise((resolve) => globalThis.setTimeout(resolve, 0))
}
assertEqual(failureCalls, 2, '公网 IP 查询失败时应耗尽备用服务并回收后台状态')

let retryCount = 0
globalThis.fetch = (async () => {
  retryCount += 1
  return new Response(JSON.stringify({ ip: '8.8.8.24' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

await getClientPublicIpHeaders()
assertEqual(retryCount, 0, '公网 IP 查询失败后应进入短暂冷却，避免每个请求立即重扫')
Date.now = originalDateNow

globalThis.fetch = originalFetch
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
