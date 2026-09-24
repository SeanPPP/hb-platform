import { useTranslation } from 'react-i18next'

import { OrderType } from '../../types/posmSalesOrder'

import { statusLabelKey } from './OrderStatusTag'
import { formatMoney, type PosmSalesOrderStatusCard, type PosmSalesOrderSummaryView } from './posmSalesOrdersLogic'

interface StatusSummaryStripProps {
  view: PosmSalesOrderSummaryView
  activeStatus: OrderType
  onSelect: (status: OrderType) => void
  /** 首次结果返回前显示占位，避免把「还没查到」误读成「0 单 $0.00」。 */
  pending?: boolean
}

const STATUS_FALLBACK: Record<number, string> = {
  [OrderType.Paid]: '已支付',
  [OrderType.Refunded]: '已退款',
  [OrderType.Cancelled]: '已取消',
  [OrderType.Pending]: '待处理',
  [OrderType.Installment]: '分期',
}

function formatCount(value: number): string {
  return value.toLocaleString('en-AU')
}

/**
 * 状态汇总条：按状态分开计数与金额，点一下就按该状态筛选。
 * 「全部」显示净实收（已支付实收 + 退款冲减）；已取消、待处理、分期标明不计入实收。
 */
export default function StatusSummaryStrip({ view, activeStatus, onSelect, pending = false }: StatusSummaryStripProps) {
  const { t } = useTranslation()
  const money = (value: number) => (pending ? '—' : formatMoney(value))
  const count = (value: number) =>
    pending ? '—' : t('posmOrders.orderCount', { value: formatCount(value), defaultValue: '{{value}} 单' })

  const caption = (card: PosmSalesOrderStatusCard) => {
    if (!card.counted) return t('posmOrders.summary.notCounted', '不计入实收')
    return card.status === OrderType.Refunded
      ? t('posmOrders.summary.refunded', '已退')
      : t('posmOrders.summary.actual', '实收')
  }

  const amountClass = (card: PosmSalesOrderStatusCard) =>
    [
      'posm-orders-status-card-amount',
      card.actualAmount < 0 ? 'is-negative' : '',
      card.counted ? '' : 'is-muted',
    ]
      .filter(Boolean)
      .join(' ')

  const dotClass = (status: OrderType) =>
    `posm-orders-dot${status === OrderType.Paid ? ' is-paid' : status === OrderType.Refunded ? ' is-refunded' : ''}`

  return (
    <div className="posm-orders-summary" role="group" aria-label={t('posmOrders.summary.ariaLabel', '按订单状态汇总与筛选')}>
      <button
        type="button"
        className={`posm-orders-status-card${activeStatus === OrderType.All ? ' is-active' : ''}`}
        aria-pressed={activeStatus === OrderType.All}
        onClick={() => onSelect(OrderType.All)}
      >
        <span className="posm-orders-status-card-head">
          <span className="posm-orders-status-card-label">{t('posmOrders.status.all', '全部')}</span>
          <span>{count(view.allCount)}</span>
        </span>
        <span className="posm-orders-status-card-value">
          <span className={`posm-orders-status-card-amount${view.netAmount < 0 ? ' is-negative' : ''}`}>
            {money(view.netAmount)}
          </span>
          <span className="posm-orders-status-card-caption">{t('posmOrders.summary.netActual', '净实收')}</span>
        </span>
      </button>
      {view.cards.map((card) => (
        <button
          key={card.status}
          type="button"
          className={`posm-orders-status-card${activeStatus === card.status ? ' is-active' : ''}`}
          aria-pressed={activeStatus === card.status}
          onClick={() => onSelect(card.status)}
        >
          <span className="posm-orders-status-card-head">
            <span className="posm-orders-status-card-label">
              <span className={dotClass(card.status)} aria-hidden="true" />
              {t(statusLabelKey(card.status), STATUS_FALLBACK[card.status])}
            </span>
            <span>{count(card.orderCount)}</span>
          </span>
          <span className="posm-orders-status-card-value">
            <span className={amountClass(card)}>{money(card.actualAmount)}</span>
            <span className="posm-orders-status-card-caption">{caption(card)}</span>
          </span>
        </button>
      ))}
      <span className="posm-orders-summary-spacer" />
      <div className="posm-orders-summary-stats">
        <div className="posm-orders-summary-stat">
          <span className="posm-orders-summary-stat-label">{t('posmOrders.summary.discountTotal', '折扣合计')}</span>
          <span className="posm-orders-summary-stat-value">{money(view.discountTotal)}</span>
        </div>
        <div className="posm-orders-summary-stat">
          <span className="posm-orders-summary-stat-label">{t('posmOrders.summary.averageTicket', '客单价')}</span>
          <span className="posm-orders-summary-stat-value">
            {view.averageTicket === null ? '—' : money(view.averageTicket)}
          </span>
        </div>
      </div>
    </div>
  )
}
