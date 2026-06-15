import { formatUserLocalDateTime } from './time'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const timezoneOffsetMinutes = new Date().getTimezoneOffset()
const expectedHour = String((24 - timezoneOffsetMinutes / 60) % 24).padStart(2, '0')

assertEqual(
  formatUserLocalDateTime('2026-06-15T00:00:00'),
  `2026-06-15 ${expectedHour}:00:00`,
  '无时区登录时间应按 UTC 解析并显示为本地时间',
)

assertEqual(
  formatUserLocalDateTime('bad-date'),
  'bad-date',
  '非法时间应保留原值便于排查',
)

assertEqual(
  formatUserLocalDateTime(undefined, '-'),
  '-',
  '空时间应显示页面传入的占位符',
)
