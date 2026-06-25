import { readFileSync } from 'node:fs'
import path from 'node:path'
import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import { buildInvoiceEmailSentStatusText } from './invoiceEmailSentInfo'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual(actual: unknown, expected: unknown, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const invoiceFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Invoice.tsx')
const detailSource = readFileSync(detailFile, 'utf8')
const invoiceSource = readFileSync(invoiceFile, 'utf8')

function createT(locale: Record<string, any>) {
  return (key: string) => {
    const value = key.split('.').reduce<any>((current, segment) => current?.[segment], locale)
    return typeof value === 'string' ? value : key
  }
}

const tZh = createT(zhLocale)
const tEn = createT(enLocale)

const notSentZh = buildInvoiceEmailSentStatusText(undefined, tZh as any)
assertEqual(notSentZh, '发票邮件：未发送', '未发送状态应输出中文默认提示')

const sentZh = buildInvoiceEmailSentStatusText(
  {
    hasSent: true,
    sentAt: '2026-06-08T09:15:00',
    toEmail: 'invoice@example.com',
    jobId: 'job-1',
  },
  tZh as any,
)
assert(
  sentZh.includes('发票邮件：已发送') &&
    sentZh.includes('上次发送：2026-06-08 09:15') &&
    sentZh.includes('收件人：invoice@example.com'),
  '已发送状态应输出中文时间和收件人信息',
)

const sentEn = buildInvoiceEmailSentStatusText(
  {
    hasSent: true,
    sentAt: '2026-06-08T09:15:00',
    toEmail: 'invoice@example.com',
  },
  tEn as any,
  'en',
)
assert(
  sentEn.includes('Invoice Email: Sent') &&
    sentEn.includes('Last Sent: 2026-06-08 09:15') &&
    sentEn.includes('Recipient: invoice@example.com'),
  '已发送状态应支持英文文案',
)

assert(
  detailSource.includes("import { InvoiceEmailSentStatusText } from './invoiceEmailSentInfo'") &&
    detailSource.includes('<InvoiceEmailSentStatusText info={detail.invoiceEmailSentInfo} t={t} lng={i18n.language} />'),
  '详情页联系邮箱附近应渲染发票邮件发送状态提示',
)

assert(
  invoiceSource.includes("import { InvoiceEmailSentStatusText } from './invoiceEmailSentInfo'") &&
    invoiceSource.includes('<InvoiceEmailSentStatusText info={order?.invoiceEmailSentInfo} t={t} lng={emailModalLanguage} />'),
  '发票邮件弹窗收件人输入框上方应渲染相同提示',
)

console.log('invoiceEmailSentInfo.logic.test: ok')
