import assert from 'node:assert/strict'
import type { ReportPeriod } from '../ReportWorkbench/logic'
import {
  fetchRevenueBranches,
  fetchRevenueHourly,
  fetchRevenueWeekly,
} from './revenueReportService'

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []
const bodies: unknown[] = [
  {
    success: true,
    data: [{ rank: 1, branchCode: 'S1', branchName: 'Store 1', revenue: 10, revenueLY: 5, orderCount: 2, orderCountLY: 1, aov: 5, aovLY: 5 }],
    statisticsPending: true,
  },
  {
    success: true,
    data: [{ hour: '09:00', branchCode: 'S1', revenue: 10, revenueLY: 5, percentage: 100, isPeak: true }],
    statisticsPending: false,
  },
  {
    success: true,
    data: [{ key: 'w2026-36', level: 'week', hierarchy: '2026-W36', revenue: 10, revenueLY: 5, orders: 2, ordersLY: 1, aov: 5, aovLY: 5, children: [] }],
    statisticsPending: true,
    statisticStatus: 'Pending',
    cacheVersion: 'weekly-v1',
  },
]

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({ url: String(input), init })
  return new Response(JSON.stringify(bodies.shift()), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}) as typeof fetch

try {
  const period: ReportPeriod = {
    startDate: '2026-09-01',
    endDate: '2026-09-03',
    compareStartDate: '2025-09-02',
    compareEndDate: '2025-09-04',
    compareMode: 'ByWeek',
  }
  const controller = new AbortController()
  const branch = await fetchRevenueBranches(period, ['S1', 'S2'], controller.signal)
  const hourly = await fetchRevenueHourly(period, ['S1'], controller.signal)
  const weekly = await fetchRevenueWeekly(period, ['S1', 'S2'], controller.signal)

  const branchUrl = new URL(calls[0]!.url, 'http://localhost')
  assert.equal(branchUrl.pathname, '/api/react/v1/dashboard/executive-branch-performance')
  assert.equal(branchUrl.searchParams.get('compareMode'), 'ByWeek')
  assert.deepEqual(branchUrl.searchParams.getAll('branchCodes'), ['S1', 'S2'])
  assert.equal(branchUrl.searchParams.get('topN'), '100')
  assert.equal(calls[0]!.init?.signal, controller.signal, '分店查询必须透传取消信号')
  assert.equal(branch.statisticStatus, 'Pending', '后台待统计包络必须进入有限轮询')
  assert.equal(branch.data[0]?.branchCode, 'S1')

  assert.equal(new URL(calls[1]!.url, 'http://localhost').pathname, '/api/react/v1/dashboard/executive-hourly-traffic')
  assert.equal(hourly.statisticStatus, 'Fresh')
  assert.equal(hourly.data[0]?.hour, '09:00')

  assert.equal(new URL(calls[2]!.url, 'http://localhost').pathname, '/api/react/v1/dashboard/weekly-performance-hierarchy')
  assert.equal(weekly.statisticStatus, 'Pending', '周层级部分快照不得被当成 Fresh')
  assert.equal(weekly.cacheVersion, 'weekly-v1')
  assert.equal(weekly.data[0]?.level, 'week')
} finally {
  globalThis.fetch = originalFetch
}

console.log('营业额接口参数、取消与统计包络：通过')
