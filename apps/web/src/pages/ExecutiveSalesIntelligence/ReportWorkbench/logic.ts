import dayjs from 'dayjs'
import isoWeek from 'dayjs/plugin/isoWeek'

dayjs.extend(isoWeek)

export type QuickRange = 'today' | 'yesterday' | 'thisWeek' | 'lastWeek' | 'thisMonth' | 'lastMonth' | 'custom'
export interface ReportPeriod {
  startDate: string
  endDate: string
  compareStartDate?: string
  compareEndDate?: string
  compareMode: 'ByDate' | 'ByWeek'
}
export interface DateSelection {
  startDate: string
  endDate: string
  quick: QuickRange
  compare: boolean
  compareMode: 'ByDate' | 'ByWeek'
}

export function quickDateSelection(quick: Exclude<QuickRange, 'custom'>, today = dayjs().format('YYYY-MM-DD')): DateSelection {
  const now = dayjs(today)
  let start = now
  let end = now
  if (quick === 'yesterday') start = end = now.subtract(1, 'day')
  if (quick === 'thisWeek') start = now.startOf('isoWeek')
  if (quick === 'lastWeek') {
    start = now.subtract(1, 'week').startOf('isoWeek')
    end = start.add(6, 'day')
  }
  if (quick === 'thisMonth') start = now.startOf('month')
  if (quick === 'lastMonth') {
    start = now.subtract(1, 'month').startOf('month')
    end = start.endOf('month')
  }
  return { startDate: start.format('YYYY-MM-DD'), endDate: end.format('YYYY-MM-DD'), quick, compare: true,
    compareMode: quick.endsWith('Month') ? 'ByDate' : 'ByWeek' }
}

export function reportPeriod(selection: DateSelection): ReportPeriod {
  const { startDate, endDate, compareMode } = selection
  const result: ReportPeriod = { startDate, endDate, compareMode }
  if (!selection.compare) return result
  const start = dayjs(startDate)
  let compareStart = start.subtract(1, 'year')
  if (compareMode === 'ByWeek') {
    // 对齐移动端：ISO 周年而非日历年；第 53 周回落到上一 ISO 年最后一周。
    const year = start.isoWeekYear() - 1
    const week = Math.min(start.isoWeek(), dayjs(`${year}-12-28`).isoWeek())
    compareStart = dayjs(`${year}-01-04`).startOf('isoWeek').add((week - 1) * 7 + start.isoWeekday() - 1, 'day')
  }
  return { ...result, compareStartDate: compareStart.format('YYYY-MM-DD'),
    compareEndDate: compareStart.add(dayjs(endDate).diff(start, 'day'), 'day').format('YYYY-MM-DD') }
}

export function validPeriod(start: string, end: string): boolean {
  return [start, end].every(value => /^\d{4}-\d{2}-\d{2}$/.test(value) && dayjs(value).format('YYYY-MM-DD') === value)
    && end >= start && dayjs(end).diff(dayjs(start), 'day') < 366
}

export function growth(current: number | null | undefined, previous: number | null | undefined): number | 'new' | null {
  if (current == null || previous == null) return null
  if (previous === 0) return current === 0 ? 0 : 'new'
  return (current - previous) / Math.abs(previous)
}

export function completeSum(values: (number | null | undefined)[]): number | null {
  return values.some(value => value == null) ? null : values.reduce<number>((sum, value) => sum + (value ?? 0), 0)
}

export function margin(profit: number | null, revenue: number): number | null {
  return profit == null || revenue === 0 ? null : profit / revenue
}

export function normalizeKeyword(value: string): string {
  return value.trim().replace(/\s+/g, ' ')
}
