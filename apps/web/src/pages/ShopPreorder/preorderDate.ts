const PREORDER_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})(?:$|T)/

function isLeapYear(year: number) {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
}

function getDaysInMonth(year: number, month: number) {
  if (month === 2) return isLeapYear(year) ? 29 : 28
  return [4, 6, 9, 11].includes(month) ? 30 : 31
}

export function getPreorderDateDisplay(value: string | null | undefined) {
  if (!value) return null
  const match = PREORDER_DATE_PATTERN.exec(value.trim())
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > getDaysInMonth(year, month)) return null

  // 兼容旧接口的日期时间字符串时只取业务日期，禁止交给 Date/dayjs 做时区换算。
  return `${match[1]}-${match[2]}-${match[3]}`
}
