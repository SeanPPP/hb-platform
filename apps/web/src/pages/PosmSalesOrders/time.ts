import dayjs from 'dayjs'

const timezoneSuffixPattern = /(Z|[+-]\d{2}:?\d{2})$/i
const dotNetIsoTimestampPattern =
  /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:?\d{2})?$/i

function isLeapYear(year: number): boolean {
  return year % 400 === 0 || (year % 4 === 0 && year % 100 !== 0)
}

function isValidPosmSalesOrderTimestamp(value: string): boolean {
  const match = dotNetIsoTimestampPattern.exec(value)
  if (!match) return false

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const hour = Number(match[4])
  const minute = Number(match[5])
  const second = Number(match[6])
  const daysInMonth = [
    31,
    isLeapYear(year) ? 29 : 28,
    31,
    30,
    31,
    30,
    31,
    31,
    30,
    31,
    30,
    31,
  ]
  if (
    year < 1 ||
    month < 1 ||
    month > 12 ||
    day < 1 ||
    day > daysInMonth[month - 1] ||
    hour > 23 ||
    minute > 59 ||
    second > 59
  ) {
    return false
  }

  const timezoneSuffix = match[7]
  if (timezoneSuffix && timezoneSuffix.toUpperCase() !== 'Z') {
    const offsetDigits = timezoneSuffix.slice(1).replace(':', '')
    const offsetHours = Number(offsetDigits.slice(0, 2))
    const offsetMinutes = Number(offsetDigits.slice(2, 4))
    if (offsetHours > 14 || offsetMinutes > 59 || (offsetHours === 14 && offsetMinutes !== 0)) {
      return false
    }
  }

  return true
}

export function normalizePosmSalesOrderUtcTime(value: string): string {
  const trimmed = value.trim()
  if (timezoneSuffixPattern.test(trimmed)) {
    return trimmed
  }

  // 主站 API 的订单时间语义是 UTC，但 SQL/JSON 可能返回不带时区后缀的字符串。
  return `${trimmed}Z`
}

export function formatPosmSalesOrderLocalTime(
  value: string | null | undefined,
  format: string,
): string {
  if (!value?.trim()) {
    return '-'
  }

  const trimmed = value.trim()
  // dayjs 会把 2 月 30 日等日期自动滚动到下个月，必须先做严格日历校验。
  if (!isValidPosmSalesOrderTimestamp(trimmed)) {
    return value
  }

  const parsed = dayjs(normalizePosmSalesOrderUtcTime(trimmed))
  return parsed.isValid() ? parsed.format(format) : value
}
