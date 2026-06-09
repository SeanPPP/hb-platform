export const BEST_SELLERS_DEFAULT_DAYS = 30

export interface BestSellerDateRange {
  startDate: string
  endDate: string
  days: number
}

function padDatePart(value: number) {
  return String(value).padStart(2, '0')
}

export function formatLocalBusinessDate(date: Date) {
  return [
    date.getFullYear(),
    padDatePart(date.getMonth() + 1),
    padDatePart(date.getDate()),
  ].join('-')
}

export function buildBestSellerDateRange(days = BEST_SELLERS_DEFAULT_DAYS, referenceDate = new Date()): BestSellerDateRange {
  const normalizedDays = Number.isFinite(days) && days > 0 ? Math.floor(days) : BEST_SELLERS_DEFAULT_DAYS
  const end = new Date(referenceDate)
  end.setHours(0, 0, 0, 0)
  // 热销榜默认不包含今天，避免今天 POSM 延迟上传导致前台回退实时查询。
  end.setDate(end.getDate() - 1)

  const start = new Date(end)
  start.setDate(start.getDate() - normalizedDays + 1)

  return {
    startDate: formatLocalBusinessDate(start),
    endDate: formatLocalBusinessDate(end),
    days: normalizedDays,
  }
}
