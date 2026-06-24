import { Typography } from 'antd'
import dayjs from 'dayjs'
import type { TFunction } from 'i18next'
import type { StoreOrderInvoiceEmailSentInfo } from '../../../types/storeOrder'

interface InvoiceEmailSentStatusTextProps {
  info?: StoreOrderInvoiceEmailSentInfo | null
  t: TFunction
  lng?: string
}

function resolveInvoiceEmailSeparator(lng?: string) {
  return lng === 'en' || lng?.startsWith('en-') ? ': ' : '：'
}

function formatInvoiceEmailSentAt(value?: string) {
  if (!value) {
    return ''
  }

  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm') : value
}

export function buildInvoiceEmailSentStatusText(
  info: StoreOrderInvoiceEmailSentInfo | null | undefined,
  t: TFunction,
  lng?: string,
) {
  const label = t('storeOrders.invoiceEmailLabel', { lng })
  const separator = resolveInvoiceEmailSeparator(lng)
  if (!info?.hasSent) {
    return `${label}${separator}${t('storeOrders.invoiceEmailNotSent', { lng })}`
  }

  // 统一详情页和发票弹窗的提示口径，避免两处文案和时间格式漂移。
  const parts = [`${label}${separator}${t('storeOrders.invoiceEmailSent', { lng })}`]
  if (info.sentAt) {
    parts.push(`${t('storeOrders.invoiceEmailLastSentAt', { lng })}${separator}${formatInvoiceEmailSentAt(info.sentAt)}`)
  }
  if (info.toEmail) {
    parts.push(`${t('storeOrders.invoiceEmailRecipient', { lng })}${separator}${info.toEmail}`)
  }

  return parts.join(' / ')
}

export function InvoiceEmailSentStatusText({ info, t, lng }: InvoiceEmailSentStatusTextProps) {
  return <Typography.Text type="secondary">{buildInvoiceEmailSentStatusText(info, t, lng)}</Typography.Text>
}
