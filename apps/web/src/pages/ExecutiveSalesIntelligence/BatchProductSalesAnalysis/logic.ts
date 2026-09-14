import type { BatchSalesBranch, BatchSalesDaily, BatchSalesMetrics } from '../../../types/batchProductSalesAnalysis'

const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

function nonZeroFinite(value: unknown): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value !== 0
}

/**
 * 只移除服务端补齐的全零日期。分类净量是有符号值，故退货和正负抵消也属于真实活动。
 * 同类销售和退货抵消后分类净量可为零，此时退货量或销售额仍能证明当天有真实业务。
 * 待统计尚未提供可靠分类时，以非零总销量兜底，避免把真实销售隐藏为补齐日。
 */
export function hasBatchProductSalesDailyActivity(day: BatchSalesDaily): boolean {
  const { metrics } = day
  return nonZeroFinite(metrics.regularQuantity)
    || nonZeroFinite(metrics.discountQuantity)
    || nonZeroFinite(metrics.unknownQuantity)
    || nonZeroFinite(metrics.returnQuantity)
    || nonZeroFinite(metrics.salesAmount)
    || (metrics.discountStatus === 'pending' && nonZeroFinite(metrics.quantity))
}

/** 保持服务端并列顺序，并避免原地排序影响原始分店数据。 */
export function sortBatchProductSalesBranchesByQuantity(branches: readonly BatchSalesBranch[]): BatchSalesBranch[] {
  return [...branches].sort((left, right) => right.metrics.quantity - left.metrics.quantity)
}

/** 待计算和终态不可用时不把缺少证据的正价/折扣数量显示成真实零；未知数量仍可如实展示。 */
export function getBatchProductSalesClassifiedQuantity(
  metrics: BatchSalesMetrics,
  field: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity',
  classificationUnavailable = false,
): number | null {
  if (metrics.discountStatus === 'pending' || (metrics.discountStatus === 'unknown' && field !== 'unknownQuantity')) return null
  if (classificationUnavailable && field !== 'unknownQuantity') return null
  return metrics[field]
}

/** 后台回填会逐日发布，部分可用和可恢复的失配状态也要继续读取最新快照。 */
export function shouldRefreshBatchProductSalesDiscountStatistics(status?: string): boolean {
  // 后端排队状态曾返回 Pending；OutOfSync/Unavailable 可能正由后台重试，保持既有轮询节奏以恢复展示。
  return ['queued', 'running', 'pending', 'backfilling', 'refreshing', 'partial', 'outofsync', 'unavailable'].includes(status?.toLowerCase() ?? '')
}

/** Fresh 以外的状态均需要提示。 */
export function hasBatchProductSalesDiscountStatisticsNotice(status?: string): boolean {
  return !!status && status.toLowerCase() !== 'fresh'
}

function parseDateOnly(value: string): Date | null {
  const match = DATE_ONLY_PATTERN.exec(value.trim())
  if (!match) {
    return null
  }
  const parsed = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
  if (
    parsed.getUTCFullYear() !== Number(match[1])
    || parsed.getUTCMonth() !== Number(match[2]) - 1
    || parsed.getUTCDate() !== Number(match[3])
  ) {
    return null
  }
  return parsed
}

/**
 * 使用页面传入的业务日期字符串校验，避免浏览器本地时区在 UTC 零点附近改变结果。
 */
export function getBatchProductSalesDateRangeError(
  startDate: string,
  endDate: string,
  todayDate: string,
): string | undefined {
  const start = parseDateOnly(startDate)
  const end = parseDateOnly(endDate)
  const today = parseDateOnly(todayDate)
  if (!start || !end || !today) {
    return '日期格式无效'
  }
  if (start.getTime() > end.getTime()) {
    return '开始日期不能晚于结束日期'
  }
  if (end.getTime() > today.getTime()) {
    return '结束日期不能晚于今天'
  }
  if ((end.getTime() - start.getTime()) / 86_400_000 > 365) {
    return '日期范围不能超过 366 天'
  }
  return undefined
}

/** CSV 单元格统一转义，并把可能被表格程序视为公式的前缀改为纯文本。 */
export function escapeCsvCell(value: unknown): string {
  if (value === null || value === undefined) {
    return ''
  }
  if (typeof value === 'number') {
    // 数值（包括退货负数）必须保留为可计算 CSV 数字，不能当作公式文本加单引号。
    return Number.isFinite(value) ? String(value) : ''
  }

  let text = String(value)
  if (typeof value === 'string' && /^\s*[=+\-@]/.test(text)) {
    text = `'${text}`
  }
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

export function formatCsvRow(values: readonly unknown[]): string {
  return values.map(escapeCsvCell).join(',')
}
