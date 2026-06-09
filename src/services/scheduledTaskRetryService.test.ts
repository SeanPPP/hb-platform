import {
  getScheduledTaskDetail,
  getScheduledTaskList,
  retryFailedScheduledTasksByType,
  retryScheduledTask,
} from './scheduledTaskRetryService'

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

  if (url.includes('/list')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              Id: 'task-1',
              TaskType: 'UpdateSupplierStatistics',
              Status: 'Failed',
              TriggeredBy: 'Manual',
              StartedAt: '2026-06-08T10:00:00Z',
              ErrorMessage: '失败原因',
            },
          ],
          totalCount: 1,
          pageIndex: 2,
          pageSize: 20,
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  if (url.endsWith('/api/ScheduledTaskRetry/task-1')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          Id: 'task-1',
          TaskType: 'UpdateSupplierStatistics',
          Status: 'Failed',
          TaskParameters: JSON.stringify({ Date: '2026-06-08' }),
          ErrorMessage: '失败原因',
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  return new Response(JSON.stringify({ success: true, message: '重试已启动' }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}) as typeof fetch

try {
  const list = await getScheduledTaskList({
    taskType: 'UpdateSupplierStatistics',
    status: 'Failed',
    triggeredBy: 'Manual',
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    pageNumber: 2,
    pageSize: 20,
  })
  const listUrl = new URL(calls[0].url, 'http://localhost')
  assertEqual(listUrl.pathname, '/api/ScheduledTaskRetry/list', '任务列表路径应正确')
  assertEqual(listUrl.searchParams.get('taskType'), 'UpdateSupplierStatistics', '任务列表应传递类型')
  assertEqual(listUrl.searchParams.get('status'), 'Failed', '任务列表应传递状态')
  assertEqual(listUrl.searchParams.get('triggeredBy'), 'Manual', '任务列表应传递触发来源')
  assertEqual(list.total, 1, '任务列表应兼容 totalCount')
  assertEqual(list.page, 2, '任务列表应兼容 pageIndex')
  assertEqual(list.items[0]?.id, 'task-1', '任务列表应兼容 PascalCase ID')
  assertEqual(list.items[0]?.startedAt, '2026-06-08T10:00:00Z', '任务列表应兼容 PascalCase 开始时间')
  assertEqual(list.items[0]?.errorMessage, '失败原因', '任务列表应保留失败原因')

  const detail = await getScheduledTaskDetail('task-1')
  assertEqual(calls[1]?.url, '/api/ScheduledTaskRetry/task-1', '任务详情路径应正确')
  if (!detail.parameters || typeof detail.parameters === 'string') {
    throw new Error('任务详情参数应解包为对象')
  }
  assertEqual(detail.parameters.Date, '2026-06-08', '任务详情应解析后端 TaskParameters JSON')

  await retryScheduledTask('task-1')
  assertEqual(calls[2]?.url, '/api/ScheduledTaskRetry/task-1', '单任务重试路径应正确')
  assertEqual(calls[2]?.init?.method, 'POST', '单任务重试应使用 POST')

  await retryFailedScheduledTasksByType({
    taskType: 'UpdateSupplierStatistics',
    startDate: '2026-06-01',
    endDate: '2026-06-08',
  })
  assertEqual(calls[3]?.url, '/api/ScheduledTaskRetry/retry-by-type', '按类型重试路径应正确')
  assertEqual(
    calls[3]?.init?.body,
    JSON.stringify({
      taskType: 'UpdateSupplierStatistics',
      startDate: '2026-06-01',
      endDate: '2026-06-08',
    }),
    '按类型重试请求体应正确',
  )

  console.log('scheduledTaskRetryService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
