import { useMemo, useState, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { LocalSupplierProductSalesAnalysisDaily } from '../../../types/localSupplierProductSalesAnalysis'
import { buildTrendChartModel, formatAud, type TrendChartMode } from './logic'
import styles from './index.module.css'

interface TrendChartProps {
  data: LocalSupplierProductSalesAnalysisDaily[]
  ariaLabel: string
  mode?: TrendChartMode
  /** 紧凑模式用于分店钻取：只画净销量，不画进货与均价面板。 */
  compact?: boolean
}

// 与项目图表约定一致：蓝=进货、橙=净销；均价用中性灰，靠独立面板标题而非颜色区分。
const PURCHASE_COLOR = '#1668dc'
const SALES_COLOR = '#e8710a'
const PRICE_COLOR = '#5b6878'
const GRID_COLOR = '#e2e7ee'
const ZERO_COLOR = '#cdd5e0'
const TEXT_COLOR = '#6b788a'
const INK_COLOR = '#1a2330'

const FULL = { width: 860, height: 326, left: 42, right: 846, top: 22, bottom: 206, priceTop: 252, priceBottom: 300, labelY: 320 }
const COMPACT = { width: 340, height: 140, left: 30, right: 334, top: 12, bottom: 108, priceTop: 0, priceBottom: 0, labelY: 130 }
const quantityFormatter = new Intl.NumberFormat('en-AU')

/** 数据端圆角、基线端直角的柱；负值向下圆角。 */
function barPath(x: number, width: number, baseline: number, valueY: number) {
  const height = Math.abs(valueY - baseline)
  if (height < 0.5) return ''
  const radius = Math.min(3, height, width / 2)
  const direction = valueY < baseline ? 1 : -1
  return `M${x} ${baseline}V${valueY + direction * radius}Q${x} ${valueY} ${x + radius} ${valueY}H${x + width - radius}Q${x + width} ${valueY} ${x + width} ${valueY + direction * radius}V${baseline}Z`
}

export default function TrendChart({ data, ariaLabel, mode = 'daily', compact = false }: TrendChartProps) {
  const { t } = useTranslation()
  const [hoverIndex, setHoverIndex] = useState<number | null>(null)
  const model = useMemo(() => buildTrendChartModel(data, compact ? 'daily' : mode, compact ? 2 : 4), [data, mode, compact])
  const frame = compact ? COMPACT : FULL
  const cumulative = !compact && mode === 'cumulative'
  const band = (frame.right - frame.left) / Math.max(data.length, 1)
  const centerX = (index: number) => frame.left + band * (index + 0.5)
  const quantityY = (value: number) => frame.bottom - ((value - model.domainMin) / (model.domainMax - model.domainMin || 1)) * (frame.bottom - frame.top)
  const priceY = (value: number) => model.priceDomain
    ? frame.priceBottom - ((value - model.priceDomain[0]) / (model.priceDomain[1] - model.priceDomain[0] || 1)) * (frame.priceBottom - frame.priceTop)
    : frame.priceBottom
  const barWidth = compact ? Math.max(1.5, Math.min(7, band * 0.62)) : Math.max(2, Math.min(9, band * 0.34))
  const barGap = band > 12 ? 2 : 1
  const baseline = quantityY(0)

  // 悬停命中按横向分带计算，命中区比柱子大得多，窄柱也容易指到。
  const handleMove = (event: MouseEvent<SVGSVGElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    const viewX = ((event.clientX - rect.left) / rect.width) * frame.width
    const index = Math.floor((viewX - frame.left) / band)
    setHoverIndex(index >= 0 && index < data.length ? index : null)
  }

  const stepPath = (values: number[]) => values.reduce((path, value, index) => `${path}V${quantityY(value)}H${frame.left + band * (index + 1)}`, `M${frame.left} ${quantityY(values[0] ?? 0)}`)
  const priceSegments: string[] = []
  if (!compact && model.priceDomain) {
    let drawing = false
    let current = ''
    data.forEach((item, index) => {
      if (item.averageUnitPrice === null || item.averageUnitPrice === undefined) { if (current) priceSegments.push(current); current = ''; drawing = false; return }
      current += `${drawing ? 'L' : 'M'}${centerX(index)} ${priceY(item.averageUnitPrice)}`
      drawing = true
    })
    if (current) priceSegments.push(current)
  }

  const lastIndex = data.length - 1
  const purchaseEndY = quantityY(model.purchase[lastIndex] ?? 0)
  const salesEndY = quantityY(model.sales[lastIndex] ?? 0)
  // 两条累计线末端接近时，把位置较低的一条标注放到线下方，避免文字重叠。
  const endLabelsClose = Math.abs(purchaseEndY - salesEndY) < 16
  const purchaseOnTop = purchaseEndY <= salesEndY
  const hovered = hoverIndex === null ? null : data[hoverIndex]
  const hoverLeftPercent = hoverIndex === null ? 0 : (centerX(hoverIndex) / frame.width) * 100

  return (
    <div className={styles.chartWrap}>
      <svg
        role="img"
        tabIndex={0}
        aria-label={ariaLabel}
        viewBox={`0 0 ${frame.width} ${frame.height}`}
        className={compact ? styles.chartCompact : styles.chart}
        onMouseMove={handleMove}
        onMouseLeave={() => setHoverIndex(null)}
      >
        <title>{ariaLabel}</title>
        <desc>{t(compact ? 'localProductSalesAnalysis.chart.compactDescription' : 'localProductSalesAnalysis.chart.description')}</desc>
        {!compact ? <text x={frame.left} y={12} fontSize={11} fill={TEXT_COLOR}>{t(cumulative ? 'localProductSalesAnalysis.chart.cumulativeQuantityAxis' : 'localProductSalesAnalysis.chart.quantityAxis')}</text> : null}
        {model.ticks.map((tick) => (
          <g key={`tick-${tick}`}>
            <line x1={frame.left} x2={frame.right} y1={quantityY(tick)} y2={quantityY(tick)} stroke={tick === 0 ? ZERO_COLOR : GRID_COLOR} />
            <text x={frame.left - 8} y={quantityY(tick) + 4} textAnchor="end" fontSize={11} fill={TEXT_COLOR}>{quantityFormatter.format(tick)}</text>
          </g>
        ))}
        {cumulative ? (
          <g>
            <path d={`${stepPath(model.sales)}V${baseline}H${frame.left}Z`} fill={SALES_COLOR} opacity={0.12} />
            <path d={stepPath(model.purchase)} fill="none" stroke={PURCHASE_COLOR} strokeWidth={2} strokeLinejoin="round" />
            <path d={stepPath(model.sales)} fill="none" stroke={SALES_COLOR} strokeWidth={2} strokeLinejoin="round" />
            <text x={frame.right - 4} y={endLabelsClose && !purchaseOnTop ? purchaseEndY + 15 : purchaseEndY - 6} textAnchor="end" fontSize={11.5} fontWeight={600} fill={INK_COLOR}>
              {t('localProductSalesAnalysis.chart.cumulativePurchase', { value: quantityFormatter.format(model.purchase[lastIndex] ?? 0) })}
            </text>
            <text x={frame.right - 4} y={endLabelsClose && purchaseOnTop ? salesEndY + 15 : salesEndY - 6} textAnchor="end" fontSize={11.5} fontWeight={600} fill={INK_COLOR}>
              {t('localProductSalesAnalysis.chart.cumulativeSales', { value: quantityFormatter.format(model.sales[lastIndex] ?? 0) })}
            </text>
          </g>
        ) : data.map((item, index) => (
          <g key={item.date}>
            {!compact ? <path d={barPath(centerX(index) - barGap / 2 - barWidth, barWidth, baseline, quantityY(model.purchase[index]))} fill={PURCHASE_COLOR} /> : null}
            <path d={barPath(compact ? centerX(index) - barWidth / 2 : centerX(index) + barGap / 2, barWidth, baseline, quantityY(model.sales[index]))} fill={SALES_COLOR} />
            {/* 只给进货事件做直接标注：数量少且是读图的关键锚点 */}
            {!compact && model.purchase[index] > 0 ? <text x={centerX(index) - barGap / 2 - barWidth / 2} y={quantityY(model.purchase[index]) - 5} textAnchor="middle" fontSize={11} fontWeight={600} fill={INK_COLOR}>{quantityFormatter.format(model.purchase[index])}</text> : null}
          </g>
        ))}
        {!compact ? (
          <g>
            <text x={frame.left} y={frame.priceTop - 10} fontSize={11} fill={TEXT_COLOR}>{t('localProductSalesAnalysis.chart.priceAxis')}</text>
            {model.priceDomain ? (
              <>
                {model.priceDomain.map((value) => (
                  <g key={`price-${value}`}>
                    <line x1={frame.left} x2={frame.right} y1={priceY(value)} y2={priceY(value)} stroke={GRID_COLOR} />
                    <text x={frame.left - 8} y={priceY(value) + 4} textAnchor="end" fontSize={11} fill={TEXT_COLOR}>{value.toFixed(2)}</text>
                  </g>
                ))}
                {priceSegments.map((segment, index) => <path key={`segment-${index}`} d={segment} fill="none" stroke={PRICE_COLOR} strokeWidth={2} strokeLinejoin="round" />)}
                {data.length <= 31 ? data.map((item, index) => item.averageUnitPrice === null || item.averageUnitPrice === undefined ? null : <circle key={`dot-${item.date}`} cx={centerX(index)} cy={priceY(item.averageUnitPrice)} r={3} fill={PRICE_COLOR} stroke="#fff" strokeWidth={2} />) : null}
              </>
            ) : <text x={frame.left + 4} y={frame.priceTop + 28} fontSize={12} fill={TEXT_COLOR}>{t('localProductSalesAnalysis.chart.noPrice')}</text>}
          </g>
        ) : null}
        {model.xTickIndices.map((index) => <text key={`x-${data[index].date}`} x={centerX(index)} y={frame.labelY} textAnchor="middle" fontSize={compact ? 10.5 : 11} fill={TEXT_COLOR}>{data[index].date.slice(5)}</text>)}
        {hoverIndex !== null ? <line x1={centerX(hoverIndex)} x2={centerX(hoverIndex)} y1={frame.top} y2={compact ? frame.bottom : frame.priceBottom} stroke={TEXT_COLOR} strokeDasharray="3 3" /> : null}
      </svg>
      {hovered && hoverIndex !== null ? (
        <div className={styles.chartTip} style={hoverLeftPercent > 60 ? { right: `${100 - hoverLeftPercent + 2}%` } : { left: `${hoverLeftPercent + 2}%` }}>
          <strong>{hovered.date}</strong>
          {!compact ? <span>{t(cumulative ? 'localProductSalesAnalysis.chart.tipCumulativePurchase' : 'localProductSalesAnalysis.chart.tipPurchase', { value: quantityFormatter.format(model.purchase[hoverIndex]) })}</span> : null}
          <span>{t(cumulative ? 'localProductSalesAnalysis.chart.tipCumulativeSales' : 'localProductSalesAnalysis.chart.tipSales', { value: quantityFormatter.format(model.sales[hoverIndex]) })}</span>
          <span>{t('localProductSalesAnalysis.chart.tipPrice', { value: formatAud(hovered.averageUnitPrice) })}</span>
        </div>
      ) : null}
    </div>
  )
}
