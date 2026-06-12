import {
  batchFullRefreshConcurrent,
  batchUpdateDailyStatistics,
  batchUpdateHourlyStatistics,
  batchUpdateStoreStatistics,
  batchUpdateStoreSupplierStatistics,
  batchUpdateSupplierStatistics,
  getScheduledStatisticsActionErrorMessage,
  isScheduledStatisticsJobFailure,
  triggerDailyStatistics,
  triggerFullRefreshCurrentDay,
  triggerFullRefreshPreviousAndCurrentDay,
  triggerStoreStatistics,
  triggerStoreSupplierStatistics,
  triggerSupplierStatistics,
} from './scheduledStatisticsService'
import { RequestError } from '../utils/request'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function readBody(call: { init?: RequestInit }) {
  return JSON.parse(String(call.init?.body)) as Record<string, unknown>
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({ url, init })

  return new Response(
    JSON.stringify({
      success: true,
      message: '任务已执行',
      jobId: 'job-1',
      totalDays: 2,
      processedDays: 2,
      failedDates: [],
    }),
    { status: 200, headers: { 'content-type': 'application/json' } },
  )
}) as typeof fetch

try {
  await triggerStoreStatistics({ date: '2026-06-08', branchCodes: ['1020', '1030'] })
  assertEqual(calls[0]?.url, '/api/StatisticsJobTrigger/trigger-store', '分店统计单日触发路径应正确')
  assertEqual(calls[0]?.init?.method, 'POST', '分店统计单日触发应使用 POST')
  assertEqual(readBody(calls[0]).date, '2026-06-08', '分店统计单日触发应传递 date')
  assertEqual(
    (readBody(calls[0]).branchCodes as string[]).join(','),
    '1020,1030',
    '分店统计单日触发应传递 branchCodes',
  )

  await batchUpdateStoreStatistics({
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    branchCodes: ['1020'],
  })
  assertEqual(calls[1]?.url, '/api/StatisticsJobTrigger/batch-update-store', '分店统计批量路径应正确')
  assertEqual(readBody(calls[1]).startDate, '2026-06-01', '分店统计批量应传递 startDate')
  assertEqual(readBody(calls[1]).endDate, '2026-06-08', '分店统计批量应传递 endDate')
  assertEqual((readBody(calls[1]).branchCodes as string[]).join(','), '1020', '分店统计批量应传递分店')

  await triggerSupplierStatistics({ date: '2026-06-08', supplierCodes: ['229'] })
  assertEqual(calls[2]?.url, '/api/StatisticsJobTrigger/trigger-supplier', '供应商统计单日路径应正确')
  assertEqual((readBody(calls[2]).supplierCodes as string[]).join(','), '229', '供应商统计应传递供应商')

  await batchUpdateSupplierStatistics({
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    supplierCodes: ['229', '49497'],
  })
  assertEqual(calls[3]?.url, '/api/StatisticsJobTrigger/batch-update-supplier', '供应商统计批量路径应正确')
  assertEqual(
    (readBody(calls[3]).supplierCodes as string[]).join(','),
    '229,49497',
    '供应商统计批量应传递供应商',
  )

  await triggerStoreSupplierStatistics({
    date: '2026-06-08',
    branchCodes: ['1020'],
    supplierCodes: ['229'],
  })
  assertEqual(calls[4]?.url, '/api/StatisticsJobTrigger/trigger-store-supplier', '门店供应商单日路径应正确')
  assertEqual((readBody(calls[4]).branchCodes as string[]).join(','), '1020', '门店供应商应传递分店')
  assertEqual((readBody(calls[4]).supplierCodes as string[]).join(','), '229', '门店供应商应传递供应商')

  await batchUpdateStoreSupplierStatistics({
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    branchCodes: ['1020'],
    supplierCodes: ['229'],
  })
  assertEqual(calls[5]?.url, '/api/StatisticsJobTrigger/batch-update-store-supplier', '门店供应商批量路径应正确')

  await triggerDailyStatistics({ date: '2026-06-08' })
  assertEqual(calls[6]?.url, '/api/StatisticsJobTrigger/trigger-daily', '每日统计单日路径应正确')

  await batchUpdateDailyStatistics({ startDate: '2026-06-01', endDate: '2026-06-08' })
  assertEqual(calls[7]?.url, '/api/StatisticsJobTrigger/batch-update-daily', '每日统计批量路径应正确')

  await batchUpdateHourlyStatistics({ startDate: '2026-06-01', endDate: '2026-06-08', hour: 13 })
  assertEqual(calls[8]?.url, '/api/StatisticsJobTrigger/batch-update-hourly', '分时统计批量路径应正确')
  assertEqual(readBody(calls[8]).hour, 13, '分时统计批量应传递 hour')

  await triggerFullRefreshCurrentDay()
  assertEqual(
    calls[9]?.url,
    '/api/StatisticsJobTrigger/trigger-full-refresh-current-day',
    '全量刷新当天路径应正确',
  )

  await triggerFullRefreshPreviousAndCurrentDay()
  assertEqual(calls[10]?.url, '/api/StatisticsJobTrigger/trigger-full-refresh', '全量刷新昨日和当天路径应正确')

  const concurrent = await batchFullRefreshConcurrent({
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    maxConcurrency: 3,
  })
  assertEqual(calls[11]?.url, '/api/StatisticsJobTrigger/batch-full-refresh-concurrent', '并发全量刷新路径应正确')
  assertEqual(readBody(calls[11]).maxConcurrency, 3, '并发全量刷新应传递最大并发')
  assertEqual(concurrent.message, '任务已执行', '应保留后端返回消息')
  assertEqual(concurrent.jobId, 'job-1', '应保留后端任务 ID')
  assertEqual(
    isScheduledStatisticsJobFailure({ success: false, message: '部分日期失败' }),
    true,
    'success=false 的 200 响应应被页面按失败处理',
  )
  assertEqual(
    isScheduledStatisticsJobFailure({ success: true, message: '执行完成' }),
    false,
    'success=true 的响应不应被按失败处理',
  )

  assertEqual(
    getScheduledStatisticsActionErrorMessage(
      new RequestError('请求失败', 500, { message: '后端失败原因' }),
    ),
    '后端失败原因',
    '应优先展示后端错误消息',
  )
  assertEqual(
    getScheduledStatisticsActionErrorMessage(new Error('普通错误')),
    '普通错误',
    '应展示普通 Error 消息',
  )

  console.log('scheduledStatisticsService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
