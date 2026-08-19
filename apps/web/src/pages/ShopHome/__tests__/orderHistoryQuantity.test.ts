import { formatOrderHistoryQuantity } from '../orderHistoryQuantity'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

assertEqual(formatOrderHistoryQuantity(12), '12', '整数数量保持整数')
assertEqual(formatOrderHistoryQuantity(12.5), '12.5', '一位小数不应取整')
assertEqual(formatOrderHistoryQuantity(12.34), '12.34', '两位小数必须保留')
assertEqual(formatOrderHistoryQuantity(null), '—', '空数量显示占位符')

console.log('orderHistoryQuantity.test: ok')
