import type {
  RevenueBranch,
  RevenueHourly,
  RevenueHourlyRow,
  RevenueSummary,
  RevenueTrend,
  RevenueWeeklyNode,
} from './types'
import dayjs from 'dayjs'
import isoWeek from 'dayjs/plugin/isoWeek'
import { quickDateSelection, validPeriod, type DateSelection } from '../ReportWorkbench/logic'

dayjs.extend(isoWeek)

const money0 = new Intl.NumberFormat('en-AU', {
  style: 'currency',
  currency: 'AUD',
  maximumFractionDigits: 0,
})
const money2 = new Intl.NumberFormat('en-AU', {
  style: 'currency',
  currency: 'AUD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})
const integer = new Intl.NumberFormat('en-AU', { maximumFractionDigits: 0 })

export function formatAud(value: number | null | undefined, decimals = 0): string {
  if (value == null || !Number.isFinite(value)) return '—'
  return (decimals === 2 ? money2 : money0).format(value)
}

export function formatInteger(value: number | null | undefined): string {
  return value == null || !Number.isFinite(value) ? '—' : integer.format(value)
}

function normalizeCode(value: string | null | undefined): string {
  return value?.trim().toLocaleLowerCase('en-AU') ?? ''
}

/** 排名必须基于本期营业额的完整结果集，接口顺序只用于同额时保持稳定。 */
export function sortRevenueBranchesByRevenue(branches: RevenueBranch[]): RevenueBranch[] {
  return [...branches]
    .sort((left, right) => right.revenue - left.revenue
      || left.rank - right.rank
      || left.branchCode.localeCompare(right.branchCode))
    .map((branch, index) => ({ ...branch, rank: index + 1 }))
}

export function getRevenueTrend(current: number | null | undefined, previous: number | null | undefined): RevenueTrend {
  if (current == null || previous == null || !Number.isFinite(current) || !Number.isFinite(previous)) {
    return { text: '—', tone: 'neutral' }
  }
  if (previous === 0) {
    return current === 0 ? { text: '0.0%', tone: 'neutral' } : { text: 'new', tone: 'neutral' }
  }
  const percentage = ((current - previous) / Math.abs(previous)) * 100
  return {
    text: `${percentage > 0 ? '+' : ''}${percentage.toFixed(1)}%`,
    tone: percentage > 0 ? 'positive' : percentage < 0 ? 'negative' : 'neutral',
  }
}

export function getRevenueSummary(
  branches: RevenueBranch[],
  selectedBranchCode: string | null,
  compare: boolean,
): RevenueSummary {
  const selectedCode = normalizeCode(selectedBranchCode)
  const scoped = selectedCode
    ? branches.filter(branch => normalizeCode(branch.branchCode) === selectedCode)
    : branches
  const revenue = scoped.reduce((sum, branch) => sum + branch.revenue, 0)
  const orders = scoped.reduce((sum, branch) => sum + branch.orderCount, 0)
  const revenueLY = compare ? scoped.reduce((sum, branch) => sum + branch.revenueLY, 0) : null
  const ordersLY = compare ? scoped.reduce((sum, branch) => sum + branch.orderCountLY, 0) : null
  return {
    revenue,
    revenueLY,
    orders,
    ordersLY,
    aov: orders > 0 ? revenue / orders : 0,
    aovLY: ordersLY != null && ordersLY > 0 && revenueLY != null ? revenueLY / ordersLY : null,
    decliningBranches: compare
      ? scoped.filter(branch => branch.revenueLY > 0 && branch.revenue < branch.revenueLY).length
      : 0,
    newBranches: compare
      ? scoped.filter(branch => branch.revenueLY === 0 && branch.revenue > 0).length
      : 0,
  }
}

export function aggregateHourlyRows(rows: RevenueHourly[], compare: boolean): RevenueHourlyRow[] {
  const byHour = new Map<string, { revenue: number; revenueLY: number }>()
  rows.forEach(row => {
    const current = byHour.get(row.hour) ?? { revenue: 0, revenueLY: 0 }
    current.revenue += row.revenue
    current.revenueLY += row.revenueLY
    byHour.set(row.hour, current)
  })
  const maximum = Math.max(0, ...Array.from(byHour.values(), value => value.revenue))
  return Array.from(byHour, ([hour, value]) => ({
    hour,
    revenue: value.revenue,
    revenueLY: compare ? value.revenueLY : null,
    percentage: maximum > 0 ? Math.round((value.revenue / maximum) * 100) : 0,
    isPeak: maximum > 0 && value.revenue >= maximum * 0.8,
  })).sort((left, right) => left.hour.localeCompare(right.hour))
}

/** 后端 key 为 wYYYY-WW-分店代码[-YYYYMMDD]，分店代码本身允许包含连字符。 */
export function getHierarchyBranchCode(node: RevenueWeeklyNode): string | null {
  if (node.level === 'week') return null
  const suffix = node.level === 'date' ? '-\\d{8}$' : '$'
  const match = node.key.match(new RegExp(`^w\\d{4}-\\d{2}-(.+?)${suffix}`))
  return match?.[1]?.trim() || null
}

export function getHierarchyDateRange(
  node: RevenueWeeklyNode,
  queryRange?: { startDate: string; endDate: string },
): { startDate: string; endDate: string } | null {
  const weekMatch = node.level === 'week' ? node.key.match(/^w(\d{4})-(\d{2})$/) : null
  if (weekMatch && queryRange) {
    const isoYear = Number(weekMatch[1])
    const isoWeekNumber = Number(weekMatch[2])
    const weekStart = dayjs(`${isoYear}-01-04`).startOf('isoWeek').add(isoWeekNumber - 1, 'week')
    const startDate = weekStart.format('YYYY-MM-DD') > queryRange.startDate
      ? weekStart.format('YYYY-MM-DD')
      : queryRange.startDate
    const weekEnd = weekStart.add(6, 'day').format('YYYY-MM-DD')
    const endDate = weekEnd < queryRange.endDate ? weekEnd : queryRange.endDate
    return startDate <= endDate ? { startDate, endDate } : null
  }
  const dates: string[] = []
  const visit = (current: RevenueWeeklyNode) => {
    if (current.level === 'date' && /^\d{4}-\d{2}-\d{2}$/.test(current.hierarchy)) dates.push(current.hierarchy)
    current.children?.forEach(visit)
  }
  visit(node)
  dates.sort()
  return dates.length > 0 ? { startDate: dates[0]!, endDate: dates[dates.length - 1]! } : null
}

export function buildSalesDetailPath(
  branchCode: string,
  selection: Pick<DateSelection, 'startDate' | 'endDate' | 'compare' | 'compareMode'>,
): string {
  const params = new URLSearchParams({
    branch: branchCode.trim(),
    startDate: selection.startDate,
    endDate: selection.endDate,
    compare: String(selection.compare),
    compareMode: selection.compareMode,
  })
  return `/executive-sales-intelligence/sales-detail-v2?${params.toString()}`
}

export function parseRevenueOverviewSearch(search: string, today?: string): {
  selection: DateSelection
  branchCode: string | null
} {
  const fallback = quickDateSelection('today', today)
  const params = new URLSearchParams(search)
  const startDate = params.get('startDate')?.trim() ?? ''
  const endDate = params.get('endDate')?.trim() ?? ''
  if (!validPeriod(startDate, endDate)) return { selection: fallback, branchCode: null }
  const compareMode = params.get('compareMode') === 'ByDate' ? 'ByDate' : 'ByWeek'
  return {
    selection: {
      ...fallback,
      startDate,
      endDate,
      quick: 'custom',
      compare: !['0', 'false'].includes(params.get('compare') ?? ''),
      compareMode,
    },
    branchCode: params.get('branch')?.trim() || null,
  }
}

export function buildRevenueOverviewSearch(selection: DateSelection, branchCode: string | null): string {
  const params = new URLSearchParams()
  if (branchCode?.trim()) params.set('branch', branchCode.trim())
  params.set('startDate', selection.startDate)
  params.set('endDate', selection.endDate)
  if (!selection.compare) params.set('compare', '0')
  if (selection.compareMode === 'ByDate') params.set('compareMode', 'ByDate')
  return `?${params.toString()}`
}

export function makeRevenueQueryScope(userGuid: string | undefined, branchCodes: string[] | null): string {
  const user = userGuid?.trim() || 'anonymous'
  if (branchCodes == null) return `${user}:all`
  const branches = [...new Set(branchCodes.map(normalizeCode).filter(Boolean))].sort()
  return `${user}:${branches.length > 0 ? branches.join(',') : 'none'}`
}
