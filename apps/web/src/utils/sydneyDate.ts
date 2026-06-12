import { getDateTagColor } from './tagColors'

const SYDNEY_TIME_ZONE = 'Australia/Sydney'

const dateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})(?:T00:00:00(?:\.0+)?)?$/

const sydneyDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: SYDNEY_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function formatDateParts(year: string, month: string, day: string) {
  return `${year}/${month}/${day}`
}

function formatIntlDate(date: Date) {
  const parts = sydneyDateFormatter.formatToParts(date).reduce<Record<string, string>>((result, part) => {
    if (part.type !== 'literal') {
      result[part.type] = part.value
    }
    return result
  }, {})

  return parts.year && parts.month && parts.day
    ? formatDateParts(parts.year, parts.month, parts.day)
    : sydneyDateFormatter.format(date).replace(/-/g, '/')
}

export function formatSydneyDate(value?: string | null) {
  if (!value) {
    return '--'
  }

  const trimmedValue = value.trim()
  if (!trimmedValue) {
    return '--'
  }

  const dateOnlyMatch = trimmedValue.match(dateOnlyPattern)
  if (dateOnlyMatch) {
    // 后端货柜日期是不带时区的业务日期，直接按悉尼日历日期显示，避免浏览器本地时区造成日期偏移。
    return formatDateParts(dateOnlyMatch[1], dateOnlyMatch[2], dateOnlyMatch[3])
  }

  const date = new Date(trimmedValue)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  // 带时区的时间戳按悉尼时区折算，只展示日期，不展示具体时间。
  return formatIntlDate(date)
}

export function getSydneyDateTagColor(value?: string | null) {
  const displayDate = formatSydneyDate(value)
  if (displayDate === '--') {
    return 'default'
  }

  // 日期颜色使用格式化后的悉尼日期作为 key，保证同一天颜色稳定、不同日期便于扫视区分。
  return getDateTagColor(displayDate)
}
