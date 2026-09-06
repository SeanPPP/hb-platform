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

function unwrapRevenueSnapshot<T>(payload: RevenueEnvelope<T[]> | T[]): ReportSnapshot<T[]> {
  if (Array.isArray(payload)) return { data: payload, statisticStatus: 'Fresh' }
  if (payload.success === false) throw new Error(payload.message || '营业额数据加载失败')
  const data = Array.isArray(payload.data) ? payload.data : []
  return {
    data,
    statisticStatus: payload.statisticStatus || (payload.statisticsPending ? 'Pending' : 'Fresh'),
    statisticMessage: payload.statisticMessage ?? payload.message,
    statisticUpdatedAt: payload.statisticUpdatedAt,
    cacheVersion: payload.cacheVersion,
  }
}

function queryOptions(period: ReportPeriod, branchCodes: string[] | null, signal: AbortSignal) {
  return {
    signal,
    params: {
      ...period,
      branchCodes: branchCodes ?? undefined,
    },
  }
}

export async function fetchRevenueBranches(
  period: ReportPeriod,
  branchCodes: string[] | null,
  signal: AbortSignal,
): Promise<ReportSnapshot<RevenueBranch[]>> {
  const options = queryOptions(period, branchCodes, signal)
  const payload = await request.get<RevenueEnvelope<RevenueBranch[]> | RevenueBranch[]>(
    '/api/react/v1/dashboard/executive-branch-performance',
    {
      ...options,
      params: { ...options.params, topN: 100 },
    },
  )
  return unwrapRevenueSnapshot(payload)
}

export async function fetchRevenueHourly(
  period: ReportPeriod,
  branchCodes: string[] | null,
  signal: AbortSignal,
): Promise<ReportSnapshot<RevenueHourly[]>> {
  const payload = await request.get<RevenueEnvelope<RevenueHourly[]> | RevenueHourly[]>(
    '/api/react/v1/dashboard/executive-hourly-traffic',
    queryOptions(period, branchCodes, signal),
  )
  return unwrapRevenueSnapshot(payload)
}

export async function fetchRevenueWeekly(
  period: ReportPeriod,
  branchCodes: string[] | null,
  signal: AbortSignal,
): Promise<ReportSnapshot<RevenueWeeklyNode[]>> {
  const payload = await request.get<RevenueEnvelope<RevenueWeeklyNode[]> | RevenueWeeklyNode[]>(
    '/api/react/v1/dashboard/weekly-performance-hierarchy',
    queryOptions(period, branchCodes, signal),
  )
  return unwrapRevenueSnapshot(payload)
}
