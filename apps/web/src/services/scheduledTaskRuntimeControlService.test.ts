import {
  getScheduledTaskRuntimeControl,
  updateScheduledTaskRuntimeControl,
} from './scheduledTaskRuntimeControlService'

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

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({ url, init })

  return new Response(
    JSON.stringify({
      success: true,
      data: {
        schedulerEnabled: true,
        schedulerEnabledByConfig: true,
        effectiveSchedulerEnabled: url.includes('runtime-control') && init?.method === 'POST',
        currentInstanceId: 'api-a',
        activeInstanceId: init?.method === 'POST' ? 'api-a' : null,
        knownInstances: [
          {
            instanceId: 'api-a',
            isCurrent: true,
            isActive: init?.method === 'POST',
          },
        ],
      },
    }),
    { status: 200, headers: { 'content-type': 'application/json' } },
  )
}) as typeof fetch

async function run() {
  calls.length = 0

  const status = await getScheduledTaskRuntimeControl()
  assertEqual(calls[0]?.url, '/api/scheduled-task/runtime-control', '状态接口路径应正确')
  assertEqual(status.currentInstanceId, 'api-a', '应返回当前实例')
  assertEqual(status.activeInstanceId, null, '状态接口应允许返回空调度实例')
  assert(!status.effectiveSchedulerEnabled, '空调度实例时当前实例不应有效执行调度')

  const switchedStatus = await updateScheduledTaskRuntimeControl({
    schedulerEnabled: false,
    activeInstanceId: 'api-b',
  })
  const updateCall = calls[1]
  assertEqual(updateCall?.url, '/api/scheduled-task/runtime-control', '更新接口路径应正确')
  assertEqual(updateCall?.init?.method, 'POST', '更新接口应使用 POST')
  assertEqual(switchedStatus.activeInstanceId, 'api-a', '调度实例非空时应按接口返回实例值')
  assert(switchedStatus.effectiveSchedulerEnabled, '调度实例匹配当前实例时应有效执行调度')
  assertEqual(
    updateCall?.init?.body,
    JSON.stringify({ schedulerEnabled: false, activeInstanceId: 'api-b' }),
    '更新接口请求体应正确',
  )

  await updateScheduledTaskRuntimeControl({ schedulerEnabled: true, activeInstanceId: null })
  const clearActiveCall = calls[2]
  assertEqual(
    clearActiveCall?.init?.body,
    JSON.stringify({ schedulerEnabled: true, activeInstanceId: null }),
    '更新接口应允许显式清空调度实例',
  )
}

run()
  .then(() => {
    globalThis.fetch = originalFetch
    console.log('scheduledTaskRuntimeControlService.test: ok')
  })
  .catch((error) => {
    globalThis.fetch = originalFetch
    console.error(error)
    process.exitCode = 1
  })
