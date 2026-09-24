export interface RevenueBranch {
  rank: number
  branchCode: string
  branchName: string
  revenue: number
  revenueLY: number
  orderCount: number
  orderCountLY: number
  aov: number
  aovLY: number
}

export interface RevenueHourly {
  hour: string
  revenue: number
  revenueLY: number
  percentage: number
  isPeak: boolean
  branchCode?: string
  branchName?: string
  /** 后端整页快照按分店逐小时返回单数；旧接口缺字段时按 0。 */
  orderCount?: number
  orderCountLY?: number
}

export interface RevenueWeeklyNode {
  key: string
  level: 'week' | 'branch' | 'date'
  hierarchy: string
  revenue: number
  revenueLY: number
  orders: number
  ordersLY: number
  aov: number
  aovLY: number
  yoyChange?: number | null
  children?: RevenueWeeklyNode[] | null
}

export interface RevenueSummary {
  revenue: number
  revenueLY: number | null
  orders: number
  ordersLY: number | null
  aov: number
  aovLY: number | null
  decliningBranches: number
  newBranches: number
}

export interface RevenueHourlyRow {
  hour: string
  revenue: number
  revenueLY: number | null
  percentage: number
  isPeak: boolean
}

export type RevenueTrendTone = 'positive' | 'negative' | 'neutral'

export interface RevenueTrend {
  text: string
  tone: RevenueTrendTone
}
