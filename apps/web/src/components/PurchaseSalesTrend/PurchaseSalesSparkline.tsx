import { useTranslation } from 'react-i18next'
import { isWeekendDate, toWholeQuantity, type PurchaseSalesTrendRow } from './purchaseSalesTrend'

interface PurchaseSalesSparklineProps {
  row: PurchaseSalesTrendRow
  width?: number
  height?: number
}

/** 行内迷你图：蓝色日销量柱（退货为向下的红色柱）+ 橙色进货标记，进货数量标注在顶部。 */
export default function PurchaseSalesSparkline({ row, width = 300, height = 52 }: PurchaseSalesSparklineProps) {
  const { t } = useTranslation()
  const data = row.dailySales
  if (!data.length) {
    return null
  }

  const top = 14
  const max = Math.max(1, ...data.map((point) => point.quantity))
  const min = Math.min(0, ...data.map((point) => point.quantity))
  const plotHeight = height - top - 2
  const zeroY = top + plotHeight * (max / (max - min))
  const scale = plotHeight / (max - min)
  const gap = 1
  const barWidth = Math.max(1.5, (width - gap * (data.length - 1)) / data.length)
  const xOf = (date: string) => Math.max(0, data.findIndex((point) => point.date === date.slice(0, 10))) * (barWidth + gap)

  return (
    <svg
      width={width}
      height={height}
      role="img"
      aria-label={t('purchaseSalesTrend.chart.ariaSparkline', '日销量与进货迷你图')}
      style={{ display: 'block', maxWidth: '100%' }}
    >
      {data.map((point, index) => {
        const barHeight = Math.max(point.quantity !== 0 ? 1.5 : 0, Math.abs(point.quantity) * scale)
        const negative = point.quantity < 0
        return (
          <rect
            key={point.date}
            x={index * (barWidth + gap)}
            y={negative ? zeroY : zeroY - barHeight}
            width={barWidth}
            height={barHeight}
            rx={1}
            fill={negative ? '#ff7875' : isWeekendDate(point.date) ? '#69b1ff' : '#1677ff'}
          >
            <title>{t('purchaseSalesTrend.chart.pointTitle', '{{date}}：{{quantity}} 件', { date: point.date, quantity: point.quantity })}</title>
          </rect>
        )
      })}
      {row.purchases.map((purchase) => {
        const x = xOf(purchase.date) + barWidth / 2
        const quantity = toWholeQuantity(purchase.quantity) ?? 0
        return (
          <g key={purchase.date}>
            <line x1={x} x2={x} y1={top - 2} y2={height} stroke="#fa8c16" strokeWidth={1.5} strokeDasharray="3 2" />
            <circle cx={x} cy={top - 4} r={3.5} fill="#fa8c16" />
            <text x={Math.min(width - 28, x + 6)} y={top - 1} fontSize={10.5} fill="#ad4e00" fontWeight={600}>
              {quantity.toLocaleString()}
            </text>
            <title>{t('purchaseSalesTrend.chart.purchaseTitle', '{{date}} 进货 {{quantity}} 件', { date: purchase.date, quantity })}</title>
          </g>
        )
      })}
      <line x1={0} y1={zeroY} x2={width} y2={zeroY} stroke="#e5e9f0" />
    </svg>
  )
}
