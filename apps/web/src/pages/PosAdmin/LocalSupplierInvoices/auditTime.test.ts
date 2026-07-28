import { readFileSync } from 'node:fs'
import path from 'node:path'
import { formatLocalSupplierInvoiceAuditTime } from './auditTime'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function formatExpectedLocalTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

const utcTimestamp = '2026-07-28T02:34:56Z'
const expectedUtcLocalTime = formatExpectedLocalTime(utcTimestamp)

assertEqual(
  formatLocalSupplierInvoiceAuditTime('2026-07-28T02:34:56'),
  expectedUtcLocalTime,
  '旧无后缀审计时间应按 UTC 解析后显示为浏览器本地时间',
)

assertEqual(
  formatLocalSupplierInvoiceAuditTime(utcTimestamp),
  expectedUtcLocalTime,
  '带 Z 的审计时间应按响应时区解析后显示为浏览器本地时间',
)

const offsetTimestamp = '2026-07-28T12:34:56+10:00'
assertEqual(
  formatLocalSupplierInvoiceAuditTime(offsetTimestamp),
  formatExpectedLocalTime(offsetTimestamp),
  '带 offset 的审计时间应保留响应时区语义后显示为浏览器本地时间',
)

assertEqual(
  formatLocalSupplierInvoiceAuditTime('not-a-date'),
  'not-a-date',
  '非法审计时间应保留原文便于排查',
)
assertEqual(formatLocalSupplierInvoiceAuditTime(undefined), '--', 'undefined 审计时间应显示占位')
assertEqual(formatLocalSupplierInvoiceAuditTime(null), '--', 'null 审计时间应显示占位')
assertEqual(formatLocalSupplierInvoiceAuditTime(''), '--', '空审计时间应显示占位')

const pageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/LocalSupplierInvoices/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')

for (const field of ['createdAt', 'updatedAt']) {
  const auditColumnPattern = new RegExp(
    `dataIndex:\\s*'${field}'[\\s\\S]*?render:\\s*\\(v:\\s*string\\)\\s*=>\\s*formatLocalSupplierInvoiceAuditTime\\(v\\)`,
  )
  assertEqual(
    auditColumnPattern.test(pageSource),
    true,
    `${field} 列应使用审计时间工具`,
  )
}

for (const field of ['orderDate', 'inboundDate']) {
  const naturalDateColumnPattern = new RegExp(
    `dataIndex:\\s*'${field}'[\\s\\S]*?render:\\s*\\(v:\\s*string\\)\\s*=>\\s*formatDate\\(v\\)`,
  )
  assertEqual(
    naturalDateColumnPattern.test(pageSource),
    true,
    `${field} 列应继续使用自然日期格式化`,
  )
}

console.log('LocalSupplierInvoices audit time tests: ok')
