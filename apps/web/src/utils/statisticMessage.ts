const STATISTIC_MESSAGE_AMOUNT_PATTERN =
  /(商品金额|分店营业额|金额差|未匹配供应商金额)\s+(-?\d+(?:,\d{3})*(?:\.\d+)?)/g

function incrementDecimalDigits(value: string) {
  const digits = value.split('')
  for (let index = digits.length - 1; index >= 0; index -= 1) {
    if (digits[index] === '9') {
      digits[index] = '0'
      continue
    }
    digits[index] = String.fromCharCode(digits[index].charCodeAt(0) + 1)
    return digits.join('')
  }
  return `1${digits.join('')}`
}

function formatExactDecimalAmount(rawAmount: string) {
  const normalized = rawAmount.replace(/,/g, '')
  const negative = normalized.startsWith('-')
  const unsigned = negative ? normalized.slice(1) : normalized
  const [rawInteger, fraction = ''] = unsigned.split('.')
  let integer = rawInteger.replace(/^0+(?=\d)/, '') || '0'
  let cents = `${fraction}00`.slice(0, 2)

  // 第三位小数按十进制字符串进位，避免超长 .NET decimal 被 JS Number 截断精度。
  if ((fraction[2] ?? '0') >= '5') {
    const rounded = incrementDecimalDigits(`${integer}${cents}`)
    integer = rounded.slice(0, -2) || '0'
    cents = rounded.slice(-2).padStart(2, '0')
  }

  const groupedInteger = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
  // 四舍五入后为零时移除负号，避免对账提示出现误导性的 -0.00。
  const sign = negative && (integer !== '0' || cents !== '00') ? '-' : ''
  return `${sign}${groupedInteger}.${cents}`
}

export function formatStatisticMessageAmounts(message: string | null | undefined) {
  if (!message) return message

  // 仅格式化对账文案中的明确金额字段，数量、日期和诊断说明保持后端原文。
  return message.replace(STATISTIC_MESSAGE_AMOUNT_PATTERN, (_match, label: string, rawAmount: string) => {
    return `${label} ${formatExactDecimalAmount(rawAmount)}`
  })
}
