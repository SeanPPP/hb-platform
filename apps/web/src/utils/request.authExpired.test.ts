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

globalThis.fetch = (async () => {
  fetchCount += 1
  return refreshSuccessResponses.shift()!
}) as typeof fetch

const retryResult = await request<{ ok: boolean }>('/api/react/v1/store-order/sync-missing-orders', {
  method: 'POST',
  data: {},
})
assertEqual(retryResult.ok, true, 'refresh 成功后应重试原请求并返回结果')
assertEqual(eventCount, 0, 'refresh 成功后不应派发 auth-expired 事件')
assertEqual(fetchCount, 3, 'refresh 成功路径应包含原请求、refresh、重试原请求')
assertEqual(replacedTo, '', 'refresh 成功后不应跳转登录页')

eventCount = 0
fetchCount = 0
replacedTo = ''

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
  assertEqual(fetchCount, 2, '认证失效路径只应请求原接口和 refresh 接口')
}

globalThis.fetch = originalFetch
Object.defineProperty(globalThis, 'window', {
  configurable: true,
  value: originalWindow,
})
