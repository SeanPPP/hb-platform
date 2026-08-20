import { buildDailyChartModel, type DailyChartInputPoint } from './logic'

interface DailySalesChartProps {
  data: DailyChartInputPoint[]
  ariaLabel: string
  height?: number
}

const CHART_WIDTH = 720
const CHART_HEIGHT = 260
const CHART_PADDING = { left: 46, right: 16, top: 20, bottom: 34 }

export default function DailySalesChart({ data, ariaLabel, height = CHART_HEIGHT }: DailySalesChartProps) {
  const model = buildDailyChartModel(data, CHART_WIDTH, height, CHART_PADDING)
  const plotBottom = height - CHART_PADDING.bottom
  const zeroLabel = model.minValue < 0 ? '0' : String(model.minValue)

  return (
    <svg
      role="img"
      tabIndex={0}
      aria-label={ariaLabel}
      viewBox={`0 0 ${CHART_WIDTH} ${height}`}
      style={{ width: '100%', height: 'auto', background: '#fff' }}
    >
      <title>{ariaLabel}</title>
      <desc>柱状图为净销量，折线为平均单价；负数柱显示在零线以下，平均价为空时跳过该点。</desc>

      <line
        x1={CHART_PADDING.left}
        x2={CHART_WIDTH - CHART_PADDING.right}
        y1={plotBottom}
        y2={plotBottom}
        stroke="#c3c6d2"
        strokeWidth="1"
      />
      <line
        x1={CHART_PADDING.left}
        x2={CHART_WIDTH - CHART_PADDING.right}
        y1={model.zeroY}
        y2={model.zeroY}
        stroke="#434750"
        strokeWidth="1.5"
        strokeDasharray="4 4"
      />
      <text
        x={CHART_PADDING.left - 8}
        y={model.zeroY + 4}
        textAnchor="end"
        fontSize="12"
        fill="#737782"
      >
        {zeroLabel}
      </text>
      <text
        x={CHART_PADDING.left - 8}
        y={plotBottom + 4}
        textAnchor="end"
        fontSize="12"
        fill="#737782"
      >
        {model.minValue < 0 ? Math.round(model.minValue) : 0}
      </text>

      {model.bars.map((bar) => (
        <rect
          key={`${bar.date}-${bar.quantity}`}
          x={bar.x}
          y={bar.y}
          width={Math.max(2, bar.width)}
          height={bar.height}
          rx="2"
          fill={bar.quantity >= 0 ? '#1677ff' : '#fa541c'}
        >
          <title>{`${bar.date} 净销量 ${bar.quantity}`}</title>
        </rect>
      ))}

      {model.averageSegments.map((segment) => segment.length > 1 ? (
        <polyline
          key={`${segment[0].date}-${segment[segment.length - 1]?.date}`}
          points={segment.map((point) => `${point.x},${point.y}`).join(' ')}
          fill="none"
          stroke="#003670"
          strokeWidth="2"
        />
      ) : null)}
      {model.averagePoints.map((point) => (
        <circle
          key={`${point.date}-avg`}
          cx={point.x}
          cy={point.y}
          r="3"
          fill="#003670"
        >
          <title>{`${point.date} 均价 ${point.averageUnitPrice}`}</title>
        </circle>
      ))}

      {model.xAxisTicks.map((tick) => (
        <text
          key={`tick-${tick.index}`}
          x={tick.x}
          y={plotBottom + 18}
          textAnchor="middle"
          fontSize="11"
          fill="#737782"
        >
          {tick.date}
        </text>
      ))}

      {model.averageMax != null ? (
        <text
          x={CHART_WIDTH - 2}
          y={CHART_PADDING.top + 4}
          textAnchor="end"
          fontSize="11"
          fill="#003670"
        >
          {`均价 ${model.averageMax.toFixed(2)}`}
        </text>
      ) : null}
      {model.averageMin != null ? (
        <text
          x={CHART_WIDTH - 2}
          y={plotBottom}
          textAnchor="end"
          fontSize="11"
          fill="#003670"
        >
          {model.averageMin.toFixed(2)}
        </text>
      ) : null}
    </svg>
  )
}
