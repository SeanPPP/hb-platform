import { useState } from "react";
import { StyleSheet, View, type LayoutChangeEvent } from "react-native";
import Svg, { Circle, Line, Path, Polygon, Rect, Text as SvgText } from "react-native-svg";
import { formatHourLabel, type CumulativeChartModel, type CumulativePoint } from "@/modules/reports/hourly-cumulative";

const CHART_HEIGHT = 188;
const MARGIN = { left: 40, right: 14, top: 22, bottom: 24 };

const COLORS = {
  current: "#2563EB",
  compare: "#6B7280",
  grid: "#EEF2F7",
  baseline: "#E5E7EB",
  axisText: "#6B7280",
  endLabel: "#475467",
  cutoff: "#0958D9",
  behind: "rgba(220, 38, 38, 0.13)",
  ahead: "rgba(22, 163, 74, 0.16)",
};

/** 纵轴取「好读」的刻度：3–4 格，顶部至少留 15% 给终点标签。 */
function getNiceScale(maxValue: number) {
  const target = Math.max(maxValue, 1) * 1.15;
  for (const step of [50, 100, 200, 500, 1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000]) {
    if (target / step <= 4) return { max: Math.ceil(target / step) * step, step };
  }
  const step = Math.ceil(target / 4 / 100_000) * 100_000;
  return { max: step * 4, step };
}

function formatAxisMoney(value: number) {
  return `$${Math.round(value).toLocaleString("en-AU")}`;
}

/** 粗估 SVG 文本宽度：全角字符按字号，其余按 0.56 倍字号，用来给标签垫白底。 */
function estimateTextWidth(text: string, fontSize: number) {
  return [...text].reduce((width, char) => width + (/[　-鿿＀-￯]/.test(char) ? fontSize : fontSize * 0.56), 0);
}

function getHourLabelStep(span: number) {
  if (span <= 12) return 2;
  if (span <= 18) return 3;
  return 4;
}

export interface CumulativeRevenueChartProps {
  model: CumulativeChartModel;
  /**
   * 用户选中的截止整点：曲线始终画到最近的完整整点，选中整点只移动标记与圆点，
   * 这样点选较早的整点也不会丢掉之后的走势；整天比较（≥ 营业结束）时不画标记。
   */
  markerHour: number;
  compareFullDayLabel: string;
  accessibilityLabel: string;
}

export function CumulativeRevenueChart({
  model,
  markerHour,
  compareFullDayLabel,
  accessibilityLabel,
}: CumulativeRevenueChartProps) {
  const [width, setWidth] = useState(0);
  const onLayout = (event: LayoutChangeEvent) => {
    const next = Math.round(event.nativeEvent.layout.width);
    if (next !== width) setWidth(next);
  };

  const plotTop = MARGIN.top;
  const plotBottom = CHART_HEIGHT - MARGIN.bottom;
  const span = Math.max(1, model.endHour - model.startHour);
  const scale = getNiceScale(model.maxValue);
  const gridValues: number[] = [];
  for (let value = 0; value <= scale.max; value += scale.step) gridValues.push(value);
  const axisLabelWidth = Math.max(...gridValues.map((value) => estimateTextWidth(formatAxisMoney(value), 10)));
  // 轴标签显示完整千位金额，左边距随最长标签增加，避免金额被图表裁掉。
  const plotLeft = Math.max(MARGIN.left, Math.ceil(axisLabelWidth) + 10);
  const plotRight = Math.max(plotLeft + 1, width - MARGIN.right);
  const x = (hour: number) => plotLeft + ((hour - model.startHour) / span) * (plotRight - plotLeft);
  const y = (value: number) => plotBottom - (value / scale.max) * (plotBottom - plotTop);
  const toPath = (points: readonly CumulativePoint[]) =>
    points
      .map((point, index) => `${index === 0 ? "M" : "L"}${x(point.hour).toFixed(1)},${y(point.value).toFixed(1)}`)
      .join(" ");

  const labelStep = getHourLabelStep(span);
  const hourLabels: number[] = [];
  for (let hour = model.startHour; hour <= model.endHour; hour += labelStep) hourLabels.push(hour);

  const lastCurrent = model.currentPoints[model.currentPoints.length - 1];
  const markerCurrent = model.currentPoints.find((point) => point.hour === markerHour);
  const markerCompare = model.comparePoints.find((point) => point.hour === markerHour);
  const compareEnd = model.comparePoints[model.comparePoints.length - 1];
  const showCutoffMarker = markerCurrent !== undefined && markerHour < model.endHour;
  const cutoffX = x(markerHour);
  // 当期曲线高过去年终点时（全天领先），标签放到终点下方，避免盖住当期曲线。
  const currentPeak = Math.max(model.liveTail?.value ?? 0, lastCurrent?.value ?? 0);
  const endLabelY = compareEnd
    ? currentPeak > compareEnd.value
      ? y(compareEnd.value) + 16
      : y(compareEnd.value) - 8
    : 0;

  return (
    <View
      style={styles.container}
      onLayout={onLayout}
      accessible
      accessibilityRole="image"
      accessibilityLabel={accessibilityLabel}
    >
      {width > 0 ? (
        <Svg width={width} height={CHART_HEIGHT}>
          {gridValues.map((value) => (
            <Line
              key={`grid-${value}`}
              x1={plotLeft}
              x2={plotRight}
              y1={y(value)}
              y2={y(value)}
              stroke={value === 0 ? COLORS.baseline : COLORS.grid}
              strokeWidth={1}
            />
          ))}
          {gridValues.map((value) => (
            <SvgText
              key={`grid-label-${value}`}
              x={plotLeft - 6}
              y={y(value) + 3.5}
              fontSize={10}
              fill={COLORS.axisText}
              textAnchor="end"
            >
              {formatAxisMoney(value)}
            </SvgText>
          ))}

          {model.gapRegions.map((region, index) => (
            <Polygon
              key={`gap-${index}`}
              points={[
                ...region.points.map((point) => `${x(point.hour).toFixed(1)},${y(point.current).toFixed(1)}`),
                ...[...region.points]
                  .reverse()
                  .map((point) => `${x(point.hour).toFixed(1)},${y(point.compare).toFixed(1)}`),
              ].join(" ")}
              fill={region.tone === "ahead" ? COLORS.ahead : COLORS.behind}
            />
          ))}

          {showCutoffMarker ? (
            <Line
              x1={cutoffX}
              x2={cutoffX}
              y1={plotTop - 4}
              y2={plotBottom}
              stroke={COLORS.cutoff}
              strokeWidth={1}
              opacity={0.45}
            />
          ) : null}

          <Path
            d={toPath(model.comparePoints)}
            fill="none"
            stroke={COLORS.compare}
            strokeWidth={1.6}
            strokeDasharray="5 4"
            strokeLinejoin="round"
          />
          <Path
            d={toPath(model.currentPoints)}
            fill="none"
            stroke={COLORS.current}
            strokeWidth={2.6}
            strokeLinejoin="round"
            strokeLinecap="round"
          />

          {model.liveTail && lastCurrent ? (
            <>
              <Path
                d={`M${x(lastCurrent.hour).toFixed(1)},${y(lastCurrent.value).toFixed(1)} L${x(model.liveTail.hour).toFixed(1)},${y(model.liveTail.value).toFixed(1)}`}
                fill="none"
                stroke={COLORS.current}
                strokeWidth={2}
                strokeDasharray="2 3"
                strokeLinecap="round"
              />
              <Circle
                cx={x(model.liveTail.hour)}
                cy={y(model.liveTail.value)}
                r={3.2}
                fill="#FFFFFF"
                stroke={COLORS.current}
                strokeWidth={1.6}
              />
            </>
          ) : null}

          {markerCurrent && markerCompare ? (
            <Circle
              cx={cutoffX}
              cy={y(markerCompare.value)}
              r={3.4}
              fill="#FFFFFF"
              stroke={COLORS.compare}
              strokeWidth={1.6}
            />
          ) : null}
          {markerCurrent ? (
            <Circle
              cx={cutoffX}
              cy={y(markerCurrent.value)}
              r={4.2}
              fill={COLORS.current}
              stroke="#FFFFFF"
              strokeWidth={2}
            />
          ) : null}

          {compareEnd ? (
            <>
              <Circle cx={x(compareEnd.hour)} cy={y(compareEnd.value)} r={2.5} fill={COLORS.compare} />
              {/* 白底挡住截止竖线，避免竖线穿过「去年全天」标签。 */}
              <Rect
                x={plotRight - estimateTextWidth(compareFullDayLabel, 10) - 4}
                y={endLabelY - 11}
                width={estimateTextWidth(compareFullDayLabel, 10) + 6}
                height={14}
                rx={3}
                fill="#FFFFFF"
                opacity={0.92}
              />
              <SvgText x={plotRight} y={endLabelY} fontSize={10} fill={COLORS.endLabel} textAnchor="end">
                {compareFullDayLabel}
              </SvgText>
            </>
          ) : null}

          {showCutoffMarker ? (
            <>
              <Rect x={cutoffX - 20} y={1} width={40} height={17} rx={8.5} fill={COLORS.cutoff} />
              <SvgText x={cutoffX} y={13.2} fontSize={10} fontWeight="700" fill="#FFFFFF" textAnchor="middle">
                {formatHourLabel(markerHour)}
              </SvgText>
            </>
          ) : null}

          {hourLabels.map((hour) => (
            <SvgText
              key={`hour-${hour}`}
              x={x(hour)}
              y={plotBottom + 16}
              fontSize={10}
              fill={COLORS.axisText}
              textAnchor="middle"
            >
              {formatHourLabel(hour)}
            </SvgText>
          ))}
        </Svg>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    width: "100%",
    height: CHART_HEIGHT,
  },
});
