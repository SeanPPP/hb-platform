import { Space, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import {
  buildPurchaseSalesTrendMetrics,
  formatMonthDay,
  isWeekendDate,
  toWholeQuantity,
  type PurchaseSalesTrendRow,
} from './purchaseSalesTrend'

const { Text } = Typography

interface PurchaseSalesDailyChartProps {
  row: PurchaseSalesTrendRow
  /** 标题行：货号与名称等，由调用页面传入。 */
  title?: string
}

const LEGEND_SWATCH = { display: 'inline-block', width: 10, height: 10, borderRadius: 2, marginRight: 5 } as const

/**
 * 展开区大图：左轴为日销量柱，橙色标记为进货事件；
 * 右轴为最近进货后的累计销量线，与进货量参考线同轴，两线相交即为卖完日。
 */
export default function PurchaseSalesDailyChart({ row, title }: PurchaseSalesDailyChartProps) {
  const { t } = useTranslation()
  const data = row.dailySales
  if (!data.length) {
    return <Text type="secondary">{t('purchaseSalesTrend.chart.noDaily', '该商品尚无逐日销量统计。')}</Text>
  }

  const metrics = buildPurchaseSalesTrendMetrics(row)
  const width = 960
  const height = 240
  const padLeft = 36
  const padRight = 44
  const padBottom = 26
  const padTop = 28
  const plotWidth = width - padLeft - padRight
  const plotHeight = height - padBottom - padTop
  const max = Math.max(1, ...data.map((point) => point.quantity))
  const min = Math.min(0, ...data.map((point) => point.quantity))
  const step = plotWidth / data.length
  const barWidth = Math.max(2, step - 2)
  const yLeft = (value: number) => padTop + plotHeight * ((max - value) / (max - min))
  const xAt = (index: number) => padLeft + index * step + barWidth / 2 + 1
  const rightMax = Math.max(metrics.purchasedQuantity, metrics.totalSinceLatest, 1) * 1.1
  const yRight = (value: number) => padTop + plotHeight - (Math.max(0, value) / rightMax) * plotHeight

  let running = 0
  const cumulativePoints = metrics.sinceLatest.map((point, index) => {
    running += point.quantity
    return `${xAt(metrics.latestIndex + index)},${yRight(running)}`
  })
  const soldOutIndex = metrics.soldOutDate ? data.findIndex((point) => point.date === metrics.soldOutDate) : -1
  const leftTicks = [...new Set([min, 0, Math.ceil(max / 2), max])]
  const rightTicks = [...new Set([0, Math.round(rightMax / 2), Math.round(rightMax)])]
  const labelEvery = Math.max(1, Math.ceil(data.length / 9))
  const sellThroughPercent = metrics.sellThrough === null ? '--' : `${Math.round(metrics.sellThrough * 100)}%`
  const latestDateKey = (row.latestPurchaseDate ?? '').slice(0, 10)

  return (
    <div>
      <Space size={16} wrap style={{ marginBottom: 6 }}>
        {title ? <Text strong>{title}</Text> : null}
        <Text type="secondary">
          {t('purchaseSalesTrend.chart.since', '最近进货 {{date}} 起 {{days}} 天', { date: latestDateKey, days: metrics.sinceLatest.length })}
        </Text>
        <Text>
          {t('purchaseSalesTrend.chart.purchased', '本次进货')}{' '}
          <Text strong style={{ color: '#ad4e00' }}>{metrics.purchasedQuantity.toLocaleString()}</Text>
        </Text>
        <Text>
          {t('purchaseSalesTrend.chart.total', '进货后累计销量')}{' '}
          <Text strong style={{ color: '#237804' }}>{metrics.totalSinceLatest.toLocaleString()}</Text>
        </Text>
        <Text>
          {t('purchaseSalesTrend.chart.sellThrough', '售出比')}{' '}
          <Text strong style={{ color: (metrics.sellThrough ?? 0) >= 1 ? '#cf1322' : undefined }}>{sellThroughPercent}</Text>
        </Text>
        <Text>
          {t('purchaseSalesTrend.chart.average', '日均')} <Text strong>{metrics.averagePerDay.toFixed(1)}</Text>
        </Text>
        {metrics.soldOutDate ? (
          <Text type="danger">{t('purchaseSalesTrend.chart.soldOutOn', '约 {{date}} 卖完', { date: formatMonthDay(metrics.soldOutDate) })}</Text>
        ) : (
          <Text type="secondary">
            {t('purchaseSalesTrend.chart.remaining', '按进货量估算剩余 {{value}}', {
              value: Math.max(0, metrics.purchasedQuantity - metrics.totalSinceLatest).toLocaleString(),
            })}
          </Text>
        )}
      </Space>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        style={{ width: '100%', maxWidth: width, minWidth: 560, display: 'block' }}
        role="img"
        aria-label={t('purchaseSalesTrend.chart.ariaDetail', '日销量、进货与累计销量图')}
      >
        {leftTicks.map((tick) => (
          <g key={`l-${tick}`}>
            <line x1={padLeft} x2={width - padRight} y1={yLeft(tick)} y2={yLeft(tick)} stroke={tick === 0 ? '#d9dee7' : '#eef1f5'} />
            <text x={padLeft - 6} y={yLeft(tick) + 4} fontSize={11} textAnchor="end" fill="#8c98a8">{tick}</text>
          </g>
        ))}
        {rightTicks.map((tick) => (
          <text key={`r-${tick}`} x={width - padRight + 6} y={yRight(tick) + 4} fontSize={11} fill="#52c41a">{tick}</text>
        ))}
        {data.map((point, index) => {
          const negative = point.quantity < 0
          const top = negative ? yLeft(0) : yLeft(point.quantity)
          const barHeight = Math.abs(yLeft(point.quantity) - yLeft(0))
          return (
            <rect
              key={point.date}
              x={padLeft + index * step + 1}
              y={top}
              width={barWidth}
              height={barHeight}
              rx={1.5}
              fill={negative ? '#ff7875' : index < metrics.latestIndex ? '#bfd6f6' : isWeekendDate(point.date) ? '#69b1ff' : '#1677ff'}
            >
              <title>{t('purchaseSalesTrend.chart.pointTitle', '{{date}}：{{quantity}} 件', { date: point.date, quantity: point.quantity })}</title>
            </rect>
          )
        })}
        {metrics.purchasedQuantity > 0 ? (
          <g>
            <line x1={padLeft} x2={width - padRight} y1={yRight(metrics.purchasedQuantity)} y2={yRight(metrics.purchasedQuantity)} stroke="#fa8c16" strokeDasharray="6 4" />
            <text x={width - padRight - 4} y={yRight(metrics.purchasedQuantity) - 5} fontSize={11} textAnchor="end" fill="#ad4e00">
              {t('purchaseSalesTrend.chart.purchaseLine', '进货量 {{value}}', { value: metrics.purchasedQuantity.toLocaleString() })}
            </text>
          </g>
        ) : null}
        {cumulativePoints.length > 1 ? <polyline fill="none" stroke="#52c41a" strokeWidth={2} points={cumulativePoints.join(' ')} /> : null}
        {row.purchases.map((purchase) => {
          const index = Math.max(0, data.findIndex((point) => point.date === purchase.date.slice(0, 10)))
          const isLatest = purchase.date.slice(0, 10) === latestDateKey
          const label = `${isLatest ? t('purchaseSalesTrend.chart.latestMarker', '最近进货') : t('purchaseSalesTrend.chart.previousMarker', '上次进货')} ${(toWholeQuantity(purchase.quantity) ?? 0).toLocaleString()} · ${formatMonthDay(purchase.date)}`
          // 靠右的标记把文字放到左侧，避免超出画布。
          const alignEnd = xAt(index) > width - padRight - 170
          return (
            <g key={purchase.date}>
              <line x1={xAt(index)} x2={xAt(index)} y1={padTop - 6} y2={padTop + plotHeight} stroke="#fa8c16" strokeWidth={1.5} strokeDasharray="4 3" />
              <circle cx={xAt(index)} cy={padTop - 8} r={5} fill="#fa8c16" />
              <text x={xAt(index) + (alignEnd ? -9 : 9)} y={padTop - 4} fontSize={11.5} fontWeight={600} fill="#ad4e00" textAnchor={alignEnd ? 'end' : 'start'}>{label}</text>
            </g>
          )
        })}
        {soldOutIndex >= 0 ? <circle cx={xAt(soldOutIndex)} cy={yRight(metrics.purchasedQuantity)} r={5} fill="#fff" stroke="#cf1322" strokeWidth={2} /> : null}
        {data.map((point, index) => (index % labelEvery === 0 ? (
          <text key={`x-${point.date}`} x={xAt(index)} y={height - 8} fontSize={11} textAnchor="middle" fill="#8c98a8">{formatMonthDay(point.date)}</text>
        ) : null))}
      </svg>
      <Space size={14} wrap style={{ marginTop: 4, fontSize: 12, color: '#607087' }}>
        <span><i style={{ ...LEGEND_SWATCH, background: '#1677ff' }} />{t('purchaseSalesTrend.chart.daily', '日销量（左轴）')}</span>
        <span><i style={{ ...LEGEND_SWATCH, background: '#69b1ff' }} />{t('purchaseSalesTrend.chart.weekend', '周末')}</span>
        <span><i style={{ ...LEGEND_SWATCH, background: '#bfd6f6' }} />{t('purchaseSalesTrend.chart.beforeLatest', '最近进货之前')}</span>
        <span><i style={{ ...LEGEND_SWATCH, background: '#fa8c16', borderRadius: 5 }} />{t('purchaseSalesTrend.chart.purchaseEvent', '进货事件与数量')}</span>
        <span><i style={{ display: 'inline-block', width: 14, height: 2, background: '#52c41a', verticalAlign: 'middle', marginRight: 5 }} />{t('purchaseSalesTrend.chart.cumulative', '进货后累计销量（右轴）')}</span>
        <span><i style={{ display: 'inline-block', width: 14, height: 0, borderTop: '2px dashed #fa8c16', verticalAlign: 'middle', marginRight: 5 }} />{t('purchaseSalesTrend.chart.purchaseLineLegend', '进货量参考线')}</span>
      </Space>
    </div>
  )
}
