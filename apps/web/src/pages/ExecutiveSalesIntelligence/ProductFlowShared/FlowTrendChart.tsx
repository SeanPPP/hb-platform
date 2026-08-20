import type { WarehouseProductFlowDaily } from '../../../types/warehouseProductFlowAnalysis'
import { buildFlowTrendChartModel } from './logic'

interface FlowTrendChartProps {
  data: WarehouseProductFlowDaily[]
  ariaLabel: string
}

const WIDTH = 720
const HEIGHT = 260

export default function FlowTrendChart({ data, ariaLabel }: FlowTrendChartProps) {
  const model = buildFlowTrendChartModel(data, WIDTH, HEIGHT)
  const pricePoints = model.series.averageUnitPrice.map((point) => `${point.x},${point.y}`).join(' ')

  return (
    <svg role="img" tabIndex={0} aria-label={ariaLabel} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="product-flow-chart">
      <title>{ariaLabel}</title>
      <desc>蓝色为进货量，青色为发货量，橙色为净销量，折线为平均单价；负销量在零线以下。</desc>
      <line x1="42" x2="704" y1={model.zeroY} y2={model.zeroY} stroke="#9aa4b2" strokeDasharray="4 4" />
      {model.series.inbound.map((bar, index) => <rect key={`inbound-${index}`} {...bar} fill="#246bfd" rx="1"><title>{`进货 ${bar.value}`}</title></rect>)}
      {model.series.shipped.map((bar, index) => <rect key={`shipped-${index}`} {...bar} fill="#14b8a6" rx="1"><title>{`发货 ${bar.value}`}</title></rect>)}
      {model.series.netSales.map((bar, index) => <rect key={`sales-${index}`} {...bar} fill={bar.value >= 0 ? '#f97316' : '#ef4444'} rx="1"><title>{`净销量 ${bar.value}`}</title></rect>)}
      {pricePoints ? <polyline points={pricePoints} fill="none" stroke="#475569" strokeWidth="2" /> : null}
      {model.series.averageUnitPrice.map((point) => <circle key={`price-${point.date}`} cx={point.x} cy={point.y} r="3" fill="#475569"><title>{`${point.date} 均价 ${point.value}`}</title></circle>)}
      {model.xAxisTicks.map((tick) => <text key={tick.date} x={tick.x} y="246" textAnchor="middle" fontSize="11" fill="#6b7280">{tick.date.slice(5)}</text>)}
    </svg>
  )
}
