import dayjs from 'dayjs'

const TIMEZONE_SUFFIX_PATTERN = /(Z|[+-]\d{2}:?\d{2})$/i

function normalizeUtcTimestamp(value: string) {
  const trimmed = value.trim()
  if (!trimmed) {
    return trimmed
  }

  if (TIMEZONE_SUFFIX_PATTERN.test(trimmed)) {
    return trimmed
  }

  // 后端字段语义是 timestampUtc，SqlSugar/JSON 可能返回不带 Z 的时间字符串。
  return `${trimmed.replace(' ', 'T')}Z`
}

export function formatCenterLogTimestamp(value?: string) {
  if (!value) {
    return '-'
  }

  const parsed = dayjs(normalizeUtcTimestamp(value))
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}
