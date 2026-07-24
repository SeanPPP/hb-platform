import { formatStatisticMessageAmounts } from './statisticMessage'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}\nExpected: ${String(expected)}\nActual: ${String(actual)}`)
  }
}

assertEqual(
  formatStatisticMessageAmounts(
    '商品统计与分店营业额统计不一致: 2026-07-08 1024, 商品金额 3812.4000000000000000000000001, 分店营业额 3922.8000, 金额差 110.3999999999999999999999999',
  ),
  '商品统计与分店营业额统计不一致: 2026-07-08 1024, 商品金额 3,812.40, 分店营业额 3,922.80, 金额差 110.40',
  '高精度对账金额应精确格式化为两位小数',
)

assertEqual(
  formatStatisticMessageAmounts(
    '商品金额 12345678901234567890.12, 分店营业额 9007199254740993.01, 金额差 999.999, 未匹配供应商金额 0.005',
  ),
  '商品金额 12,345,678,901,234,567,890.12, 分店营业额 9,007,199,254,740,993.01, 金额差 1,000.00, 未匹配供应商金额 0.01',
  '超出安全整数范围的金额不得经由 JavaScript Number 损失精度',
)

assertEqual(
  formatStatisticMessageAmounts('商品金额 1,234,567.8, 金额差 -9,999.995'),
  '商品金额 1,234,567.80, 金额差 -10,000.00',
  '已有千分位和负数跨整数进位应保持正确',
)

assertEqual(
  formatStatisticMessageAmounts('商品金额 -0.004, 金额差 -0.005'),
  '商品金额 0.00, 金额差 -0.01',
  '负数舍入为零时应移除负号',
)

assertEqual(formatStatisticMessageAmounts(null), null, '空消息应保持原值')

console.log('statisticMessage.test: ok')
