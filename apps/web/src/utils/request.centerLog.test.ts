import request from './request'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
let fetchCount = 0

globalThis.fetch = (async () => {
  fetchCount += 1
  return new Response(JSON.stringify({ success: false, message: 'ingest failed' }), {
    status: 500,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await request('/api/system/logs/ingest', {
    method: 'POST',
    data: { logs: [] },
  })
  throw new Error('日志写入失败时应抛出 RequestError')
} catch {
  assertEqual(fetchCount, 1, '日志写入接口失败时不应递归触发二次上报')
}

globalThis.fetch = originalFetch
console.log('request.centerLog.test: ok')
