const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

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
