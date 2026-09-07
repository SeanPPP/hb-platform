import request from '../../../utils/request'
import type { ReportPeriod } from '../ReportWorkbench/logic'
import type { ReportSnapshot } from '../ReportWorkbench/useReportQuery'
import type { RevenueBranch, RevenueHourly, RevenueWeeklyNode } from './types'

interface RevenueEnvelope<T> {
  success?: boolean
  data?: T
  message?: string
  statisticsPending?: boolean
  statisticStatus?: string
  statisticMessage?: string | null
  statisticUpdatedAt?: string | null
  cacheVersion?: string | null
}

export interface RevenueReportSnapshot {
  branches: RevenueBranch[]
  hourly: RevenueHourly[]
  weekly: RevenueWeeklyNode[]
  currentPeriodPending?: boolean
  comparePeriodPending?: boolean
  hourlyCurrentPending?: boolean
  hourlyComparePending?: boolean
  weeklyComparePending?: boolean
}

function unwrapRevenueSnapshot(payload: RevenueEnvelope<RevenueReportSnapshot>): ReportSnapshot<RevenueReportSnapshot> {
  if (payload.success === false) throw new Error(payload.message || '营业额数据加载失败')
  const data = payload.data
  if (!data || !Array.isArray(data.branches) || !Array.isArray(data.hourly) || !Array.isArray(data.weekly)) {
    throw new Error('营业额快照响应不完整')
  }
  return {
    data,
    statisticStatus: payload.statisticStatus || (payload.statisticsPending ? 'Pending' : 'Fresh'),
    statisticMessage: payload.statisticMessage ?? payload.message,
    statisticUpdatedAt: payload.statisticUpdatedAt,
    cacheVersion: payload.cacheVersion,
  }
}

/** 排名、时段与周层级一次读取，保证整页来自同一统计快照。 */
export async function fetchRevenueReportSnapshot(
  period: ReportPeriod,
  branchCodes: string[] | null,
  focusBranchCodes: string[] | null,
  signal: AbortSignal,
): Promise<ReportSnapshot<RevenueReportSnapshot>> {
  const payload = await request.get<RevenueEnvelope<RevenueReportSnapshot>>(
    '/api/react/v1/dashboard/revenue-report-snapshot',
    { signal, params: { ...period, branchCodes: branchCodes ?? undefined,
      focusBranchCodes: focusBranchCodes ?? undefined, topN: 100 } },
  )
  return unwrapRevenueSnapshot(payload)
}
