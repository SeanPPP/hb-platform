import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { BatchSalesDaily } from '../../../types/batchProductSalesAnalysis'
import { buildDiscountDailyChartModel, type DiscountChartKind } from './chartModel'

interface DiscountDailyChartProps {
  data: BatchSalesDaily[]
  ariaLabel: string
  className?: string
  classificationUnavailable?: boolean
}

const colors: Record<DiscountChartKind, string> = { regular: '#1677ff', discount: '#fa8c16', unknown: '#aab2bd', total: '#1677ff' }
const audFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' })

function compactDate(value: string) {
  return value.length >= 10 ? value.slice(5) : value
}
function priceRange(min: number | null, max: number | null) {
  if (min === null || max === null) return '—'
  return min === max ? audFormatter.format(min) : `${audFormatter.format(min)}–${audFormatter.format(max)}`
}

export default function DiscountDailyChart({ data, ariaLabel, className, classificationUnavailable = false }: DiscountDailyChartProps) {
  const { t } = useTranslation()
  const [activeIndex, setActiveIndex] = useState<number | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const [chartWidth, setChartWidth] = useState(720)
  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    const updateWidth = () => setChartWidth(Math.max(260, Math.round(container.getBoundingClientRect().width)))
    updateWidth()
    const observer = new ResizeObserver(updateWidth)
    observer.observe(container)
    return () => observer.disconnect()
  }, [data.length])
  // 两栏都用同一绘图区高度；窄一些的分店图按真实容器宽度重新计算坐标，不压扁文字或柱形。
  const model = buildDiscountDailyChartModel(data, chartWidth, 186, classificationUnavailable)
  const quantityOnly = model.points.some((point) => point.pending)
  const discountsPending = model.points.some((point) => point.discountPending)
  const active = activeIndex === null ? null : model.points[activeIndex]
  const tooltipX = active ? Math.min(Math.max(active.x + 12, model.plotLeft + 4), model.plotRight - 184) : 0
  const tooltipY = active ? Math.max(model.plotTop + 6, Math.min(model.plotBottom - 126, model.zeroY - 106)) : 0

  if (!data.length) return null

  return (
    <div ref={containerRef} className={className}>
      <div className="batch-product-sales-chart-legend" aria-label={t('batchProductSalesAnalysis.chart.legend')}>
        {(quantityOnly ? ['total'] as const : ['regular', 'discount', 'unknown'] as const).map((kind) => <span key={kind}><i style={{ background: colors[kind] }} />{t(`batchProductSalesAnalysis.metrics.${kind === 'total' ? 'quantity' : kind}`)}</span>)}
      </div>
      <svg role="img" aria-label={ariaLabel} viewBox={`0 0 ${model.width} ${model.height}`} className="batch-product-sales-chart">
        <title>{ariaLabel}</title>
        <desc>{t(quantityOnly ? (discountsPending ? 'batchProductSalesAnalysis.chart.quantityDescription' : 'batchProductSalesAnalysis.chart.unavailableDescription') : 'batchProductSalesAnalysis.chart.description')}</desc>
        {model.ticks.map((tick) => <g key={tick.value}><line x1={model.plotLeft} x2={model.plotRight} y1={tick.y} y2={tick.y} stroke={tick.value === 0 ? '#8590a2' : '#edf0f5'} strokeDasharray={tick.value === 0 ? '4 3' : '2 2'} /><text x={model.plotLeft - 7} y={tick.y + 4} textAnchor="end" fontSize="11" fill="#718096">{tick.value}</text></g>)}
        {model.weekDividers.map((divider) => <line key={divider.date} x1={divider.x} x2={divider.x} y1={model.plotTop} y2={model.plotBottom} stroke="#e2e8f0" strokeWidth="1" />)}
        {model.points.map((point, index) => (
          <g
            key={point.date}
            tabIndex={0}
            role="graphics-symbol"
            aria-label={t(point.pending ? (point.discountPending ? 'batchProductSalesAnalysis.chart.totalPointAria' : 'batchProductSalesAnalysis.chart.unavailablePointAria') : 'batchProductSalesAnalysis.chart.pointAria', { date: point.date, quantity: point.quantity, amount: audFormatter.format(point.salesAmount), regular: point.regularQuantity, discount: point.discountQuantity, unknown: point.unknownQuantity })}
            onFocus={() => setActiveIndex(index)}
            onBlur={() => setActiveIndex((current) => current === index ? null : current)}
            onMouseEnter={() => setActiveIndex(index)}
            onMouseLeave={() => setActiveIndex((current) => current === index ? null : current)}
          >
            {point.segments.map((segment) => segment.height > 0 ? <rect key={segment.kind} x={segment.x} y={segment.y} width={segment.width} height={segment.height} rx="1" fill={colors[segment.kind]} /> : null)}
            <rect x={point.segments[0]?.x ?? point.x} y={model.plotTop} width={point.segments[0]?.width ?? 0} height={model.plotBottom - model.plotTop} fill="transparent" />
          </g>
        ))}
        {model.xTicks.map((tick) => <text key={tick.date} x={tick.x} y={model.plotBottom + 18} textAnchor="middle" fontSize="11" fill="#718096">{compactDate(tick.date)}</text>)}
        {active?.pending ? <g aria-live="polite"><rect x={tooltipX} y={tooltipY} width="180" height="68" rx="4" fill="#172033" /><text x={tooltipX + 10} y={tooltipY + 18} fontSize="11" fill="#fff">{active.date}</text><text x={tooltipX + 10} y={tooltipY + 37} fontSize="11" fill="#9cc5ff">{t('batchProductSalesAnalysis.metrics.quantity')}: {active.quantity}</text><text x={tooltipX + 10} y={tooltipY + 55} fontSize="11" fill="#9fe0c9">{t('batchProductSalesAnalysis.metrics.amount')}: {audFormatter.format(active.salesAmount)}</text></g> : active ? <g aria-live="polite"><rect x={tooltipX} y={tooltipY} width="180" height="126" rx="4" fill="#172033" opacity=".96" /><text x={tooltipX + 10} y={tooltipY + 17} fontSize="11" fill="#fff">{active.date}</text><text x={tooltipX + 10} y={tooltipY + 33} fontSize="11" fill="#9cc5ff">{t('batchProductSalesAnalysis.metrics.regular')}: {active.regularQuantity}</text><text x={tooltipX + 10} y={tooltipY + 49} fontSize="11" fill="#ffd09b">{t('batchProductSalesAnalysis.metrics.discount')}: {active.discountQuantity}</text><text x={tooltipX + 10} y={tooltipY + 65} fontSize="11" fill="#d5d9df">{t('batchProductSalesAnalysis.metrics.unknown')}: {active.unknownQuantity}</text><text x={tooltipX + 10} y={tooltipY + 81} fontSize="11" fill="#9fe0c9">{t('batchProductSalesAnalysis.metrics.amount')}: {audFormatter.format(active.salesAmount)}</text><text x={tooltipX + 10} y={tooltipY + 100} fontSize="10" fill="#dfe7f1">{t('batchProductSalesAnalysis.chart.originalPrice')}: {priceRange(active.originalPriceMin, active.originalPriceMax)}</text><text x={tooltipX + 10} y={tooltipY + 116} fontSize="10" fill="#dfe7f1">{t('batchProductSalesAnalysis.chart.discountPrice')}: {priceRange(active.discountPriceMin, active.discountPriceMax)}</text></g> : null}
      </svg>
    </div>
  )
}
