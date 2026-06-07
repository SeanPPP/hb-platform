import { formatCenterLogTimestamp } from './time'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

assertEqual(
  formatCenterLogTimestamp('2026-06-07T22:59:30'),
  '2026-06-08 08:59:30',
  '无时区后缀的中心日志时间应按 UTC 转成本地时间',
)

assertEqual(
  formatCenterLogTimestamp('2026-06-07T22:59:30Z'),
  '2026-06-08 08:59:30',
  '带 Z 的中心日志时间应按 UTC 转成本地时间',
)

assertEqual(formatCenterLogTimestamp(undefined), '-', '空时间显示占位')

console.log('centerLog time.test: ok')
