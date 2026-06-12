import { formatSydneyDate, getSydneyDateTagColor } from './sydneyDate'

function assertEqual(actual: unknown, expected: unknown, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}，实际: ${String(actual)}，期望: ${String(expected)}`)
  }
}

function main() {
  assertEqual(
    formatSydneyDate('2026-06-26T00:00:00'),
    '2026/06/26',
    '无时区货柜日期应按悉尼业务日期显示且不显示时间',
  )

  assertEqual(
    formatSydneyDate('2026-06-25T14:30:00Z'),
    '2026/06/26',
    '带 UTC 时区的时间戳应转换成悉尼日期',
  )

  assertEqual(formatSydneyDate(undefined), '--', '空日期应显示占位符')
  assertEqual(formatSydneyDate('bad-date'), 'bad-date', '非法日期应保留原值便于排查数据')
  assertEqual(getSydneyDateTagColor('2026-06-26T00:00:00'), 'blue', '悉尼日期应有稳定颜色')
  assertEqual(getSydneyDateTagColor('2026-06-25T00:00:00'), 'cyan', '不同日期应映射到不同颜色')
  assertEqual(getSydneyDateTagColor(undefined), 'default', '空日期不应显示彩色标签')

  console.log('sydneyDate.test: ok')
}

main()
