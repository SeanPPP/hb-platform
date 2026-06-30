import { formatAppDownloadLocalDateTime } from './time'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function pad(value: number) {
  return String(value).padStart(2, '0')
}

function formatExpectedLocalTime(value: string) {
  const date = new Date(value)
  return [
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`,
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`,
  ].join(' ')
}

const expectedLocalTime = formatExpectedLocalTime('2026-06-30T09:23:17Z')

assertEqual(
  formatAppDownloadLocalDateTime('2026-06-30T09:23:17'),
  expectedLocalTime,
  '无时区后缀的 App 下载时间应按 UTC 解析后显示为本地时间',
)

assertEqual(
  formatAppDownloadLocalDateTime('2026-06-30T09:23:17Z'),
  expectedLocalTime,
  '带 Z 的 App 下载时间应显示同一个本地时间',
)

assertEqual(
  formatAppDownloadLocalDateTime('bad-date'),
  'bad-date',
  '非法时间应保留原值便于排查',
)

assertEqual(formatAppDownloadLocalDateTime(undefined), '--', '空时间应显示占位')

console.log('AppDownloads time tests: ok')
