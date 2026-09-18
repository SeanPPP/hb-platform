import type {
  LocalSupplierPurchaseSalesAnalysisRowDto,
  LocalSupplierPurchaseSalesDailyPointDto,
} from '../../types/localSupplierInvoice'

export type PurchaseSalesTrendRow = Pick<
  LocalSupplierPurchaseSalesAnalysisRowDto,
  'latestPurchaseDate' | 'latestPurchaseQty' | 'dailySales' | 'purchases'
>

export interface PurchaseSalesTrendMetrics {
  /** 最近进货日在逐日序列中的下标；序列不含该日时为 0。 */
  latestIndex: number
  /** 最近进货当天起的逐日销量。 */
  sinceLatest: LocalSupplierPurchaseSalesDailyPointDto[]
  /** 最近进货后的累计净销量（含退货负数）。 */
  totalSinceLatest: number
  averagePerDay: number
  /** 最近进货数量取整；无进货时为 0。 */
  purchasedQuantity: number
  /** 售出比 = 进货后累计销量 / 最近进货量；进货量为 0 时为 null。 */
  sellThrough: number | null
  /** 累计销量首次达到进货量的日期；未卖完为 null。 */
  soldOutDate: string | null
}

const toDateKey = (value?: string | null) => (value ? value.slice(0, 10) : '')

/** 进货数量统一按整数展示与计算，四舍五入（远离零）。 */
export function toWholeQuantity(value?: number | null) {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return null
  }
  return Math.sign(value) * Math.round(Math.abs(value))
}

export function buildPurchaseSalesTrendMetrics(row: PurchaseSalesTrendRow): PurchaseSalesTrendMetrics {
  const latestKey = toDateKey(row.latestPurchaseDate)
  const foundIndex = latestKey ? row.dailySales.findIndex((point) => point.date === latestKey) : -1
  const latestIndex = foundIndex >= 0 ? foundIndex : 0
  const sinceLatest = row.dailySales.slice(latestIndex)
  const totalSinceLatest = sinceLatest.reduce((sum, point) => sum + point.quantity, 0)
  const purchasedQuantity = toWholeQuantity(row.latestPurchaseQty) ?? 0

  let soldOutDate: string | null = null
  if (purchasedQuantity > 0) {
    let running = 0
    for (const point of sinceLatest) {
      running += point.quantity
      if (running >= purchasedQuantity) {
        soldOutDate = point.date
        break
      }
    }
  }

  return {
    latestIndex,
    sinceLatest,
    totalSinceLatest,
    averagePerDay: sinceLatest.length > 0 ? totalSinceLatest / sinceLatest.length : 0,
    purchasedQuantity,
    sellThrough: purchasedQuantity > 0 ? totalSinceLatest / purchasedQuantity : null,
    soldOutDate,
  }
}

/** 周末判定只依赖 yyyy-MM-dd 文本，避免浏览器时区把日期偏移到前一天。 */
export function isWeekendDate(date: string) {
  const [year, month, day] = date.split('-').map(Number)
  const weekday = new Date(Date.UTC(year, (month || 1) - 1, day || 1)).getUTCDay()
  return weekday === 0 || weekday === 6
}

export function formatMonthDay(date: string) {
  return date.length >= 10 ? date.slice(5, 10) : date
}

/**
 * 大图展开采用手风琴：同一时间只展开一个商品。
 * 展开某行时只保留该行，收起当前行后不再展开任何行。
 */
export function resolveAccordionExpandedKeys(expanded: boolean, rowKey: string): string[] {
  return expanded ? [rowKey] : []
}

/** 每次拿到新结果时默认只展开第一行；首行没有日销量（无大图可看）时全部收起。 */
export function resolveDefaultExpandedKeys<T extends Pick<PurchaseSalesTrendRow, 'dailySales'>>(
  items: readonly T[],
  getRowKey: (row: T) => string,
): string[] {
  const first = items[0]
  return first && first.dailySales.length > 0 ? [getRowKey(first)] : []
}
