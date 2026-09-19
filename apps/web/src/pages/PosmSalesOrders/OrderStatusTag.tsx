import { CreditCardOutlined, GiftOutlined, WalletOutlined } from '@ant-design/icons'
import { Tag } from 'antd'
import { useTranslation } from 'react-i18next'

import { OrderType } from '../../types/posmSalesOrder'

import { paymentMethodKey } from './posmSalesOrdersLogic'

export function statusLabelKey(status?: number | null): string {
  switch (status) {
    case OrderType.Paid:
      return 'posmOrders.status.paid'
    case OrderType.Refunded:
      return 'posmOrders.status.refunded'
    case OrderType.Cancelled:
      return 'posmOrders.status.cancelled'
    case OrderType.Pending:
      return 'posmOrders.status.pending'
    case OrderType.Installment:
      return 'posmOrders.status.installment'
    default:
      return 'posmOrders.status.unknown'
  }
}

const STATUS_FALLBACK: Record<string, string> = {
  'posmOrders.status.paid': '已支付',
  'posmOrders.status.refunded': '已退款',
  'posmOrders.status.cancelled': '已取消',
  'posmOrders.status.pending': '待处理',
  'posmOrders.status.installment': '分期',
  'posmOrders.status.unknown': '未知',
}

/**
 * 订单状态：已支付占绝大多数，只用绿色圆点加文字，避免整列彩色标签的噪音；
 * 退款、已取消等少数状态用标签突出。
 */
export default function OrderStatusTag({ status }: { status?: number | null }) {
  const { t } = useTranslation()
  const key = statusLabelKey(status)
  const label = t(key, STATUS_FALLBACK[key])
  if (status === OrderType.Paid) {
    return (
      <span className="posm-orders-status">
        <span className="posm-orders-dot is-paid" aria-hidden="true" />
        {label}
      </span>
    )
  }
  const color =
    status === OrderType.Refunded ? 'red' : status === OrderType.Pending ? 'gold' : status === OrderType.Installment ? 'purple' : 'default'
  return (
    <span className="posm-orders-status">
      <Tag color={color} bordered>
        {label}
      </Tag>
    </span>
  )
}

const PAYMENT_FALLBACK = { cash: '现金', card: '刷卡', voucher: '代金券', other: '其他' }

export function PaymentMethodsText({ methods }: { methods?: number[] }) {
  const { t } = useTranslation()
  if (!methods?.length) return <span className="posm-orders-dash">—</span>
  const keys = methods.map(paymentMethodKey)
  const icon =
    keys.length > 1 ? null : keys[0] === 'card' ? <CreditCardOutlined /> : keys[0] === 'cash' ? <WalletOutlined /> : <GiftOutlined />
  return (
    <span className="posm-orders-pay">
      {icon}
      {keys.map((key) => t(`posmOrders.payment.${key}`, PAYMENT_FALLBACK[key])).join(' / ')}
    </span>
  )
}
