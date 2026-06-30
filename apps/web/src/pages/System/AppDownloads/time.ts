import dayjs from 'dayjs'

const timezoneSuffixPattern = /(Z|[+-]\d{2}:?\d{2})$/i

function normalizeUtcTimestamp(value: string) {
  const trimmed = value.trim()
  if (!trimmed) {
    return trimmed
  }

  if (timezoneSuffixPattern.test(trimmed)) {
    return trimmed
  }

  // 后端 App 下载时间语义是 UTC，SQL/JSON 可能返回不带 Z 的字符串。
  return `${trimmed.replace(' ', 'T')}Z`
}

export function formatAppDownloadLocalDateTime(value?: string | null) {
  if (!value) {
    return '--'
  }

  const parsed = dayjs(normalizeUtcTimestamp(value))
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}
