import { getUserLoginRecords } from './userService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
let requestedUrl = ''

globalThis.fetch = (async (input) => {
  requestedUrl = String(input)
  return new Response(JSON.stringify({
    success: true,
    data: {
      items: [
        {
          sessionId: 'session-1',
          loginAt: '2026-06-16T00:00:00Z',
          ipAddress: '203.0.113.10',
          userAgent: 'Test Browser',
          expiresAt: '2026-06-23T00:00:00Z',
          isRevoked: false,
          isExpired: false,
          status: 'active',
        },
      ],
      total: 3,
      page: 2,
      pageSize: 1,
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const result = await getUserLoginRecords('user-1', { page: 2, pageSize: 1 })
  const requestUrl = new URL(requestedUrl, 'http://localhost')

  assertEqual(requestUrl.pathname, '/api/Users/guid/user-1/login-records', '登录记录应调用用户级接口')
  assertEqual(requestUrl.searchParams.get('page'), '2', '登录记录应透传页码')
  assertEqual(requestUrl.searchParams.get('pageSize'), '1', '登录记录应透传每页数量')
  assertEqual(result.total, 3, '登录记录应解析分页总数')
  assertEqual(result.items[0]?.ipAddress, '203.0.113.10', '登录记录应保留 IP 字段')
  assertEqual(result.items[0]?.status, 'active', '登录记录应保留状态字段')
} finally {
  globalThis.fetch = originalFetch
}
