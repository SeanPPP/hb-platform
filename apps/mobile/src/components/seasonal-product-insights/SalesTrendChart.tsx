import { useState } from "react";
import { StyleSheet, View, type LayoutChangeEvent } from "react-native";
import Svg, { G, Line, Rect, Text as SvgText } from "react-native-svg";
import {
  formatQuantity,
  shortDate,
} from "@/modules/seasonal-product-insights/logic";
import type {
  DailyTrendPoint,
  WeeklyTrendPoint,
} from "@/modules/seasonal-product-insights/types";

const CHART_HEIGHT = 184;

const COLORS = {
  bar: "#1677FF",
  peak: "#0958D9",
  muted: "#9CC2FF",
  grid: "#EEF2F7",
  baseline: "#D0D5DD",
  axisText: "#667085",
  value: "#344054",
  inbound: "#067647",
};

/** 纵轴取「好读」的刻度：约 4 格，顶部留 12% 给数值标签。 */
function niceScale(maxValue: number) {
  const target = Math.max(maxValue, 1) * 1.12;
  const rough = target / 4;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const step = [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ?? 10 * magnitude;
  return { max: Math.ceil(target / step) * step, step };
}

function useWidth() {
  const [width, setWidth] = useState(0);
  const onLayout = (event: LayoutChangeEvent) => {
    const next = Math.round(event.nativeEvent.layout.width);
    if (next !== width) setWidth(next);
  };
  return { width, onLayout };
}

function Grid({ scale, left, right, y }: { scale: { max: number; step: number }; left: number; right: number; y: (v: number) => number }) {
  const values: number[] = [];
  for (let value = 0; value <= scale.max + 1e-9; value += scale.step) values.push(value);
  return (
    <>
      {values.map((value) => (
        <Line key={`g-${value}`} x1={left} x2={right} y1={y(value)} y2={y(value)} stroke={value === 0 ? COLORS.baseline : COLORS.grid} strokeWidth={1} />
      ))}
      {values.map((value) => (
        <SvgText key={`gl-${value}`} x={left - 6} y={y(value) + 3.5} fontSize={10} fill={COLORS.axisText} textAnchor="end">
          {formatQuantity(value)}
        </SvgText>
      ))}
    </>
  );
}

export interface DailyTrendChartProps {
  dayCount: number;
  points: DailyTrendPoint[];
  markers: { index: number; quantity: number }[];
  startDate: string;
  endDate: string;
  /** 门店当地今天；落在区间末日时该柱浅色显示「未结束」。 */
  today: string | null;
  peakDate: string | null;
  endLabel: string;
  accessibilityLabel: string;
}

export function DailyTrendChart({
  dayCount,
  points,
  markers,
  startDate,
  endDate,
  today,
  peakDate,
  endLabel,
  accessibilityLabel,
}: DailyTrendChartProps) {
  const { width, onLayout } = useWidth();
  const top = 24;
  const bottom = CHART_HEIGHT - 20;
  const scale = niceScale(Math.max(0, ...points.map((point) => point.quantity)));
  const left = Math.max(30, formatQuantity(scale.max).length * 6 + 10);
  const right = Math.max(left + 1, width - 4);
  const step = (right - left) / Math.max(dayCount, 1);
  const barWidth = Math.max(2, Math.min(14, step * 0.66));
  const x = (index: number) => left + index * step + step / 2;
  const y = (value: number) => bottom - (value / scale.max) * (bottom - top);
  const peak = points.find((point) => point.date === peakDate);

  // 横轴约 4 个刻度：起点、中间两个、终点，避免文字互相压住。
  const tickIndexes = dayCount <= 1 ? [0] : [0, Math.round((dayCount - 1) / 3), Math.round(((dayCount - 1) * 2) / 3), dayCount - 1];
  const tickLabel = (index: number) => {
    if (index === dayCount - 1) return endLabel;
    const ms = Date.parse(`${startDate}T00:00:00Z`) + index * 86_400_000;
    return shortDate(new Date(ms).toISOString());
  };

  // 相邻到货标注太近时只画虚线不写数字，避免数字重叠。
  const labelledMarkers = new Set<number>();
  let lastLabelX = -Infinity;
  for (const marker of markers) {
    const mx = x(marker.index);
    if (mx - lastLabelX > 46 && mx + 30 < width) {
      labelledMarkers.add(marker.index);
      lastLabelX = mx;
    }
  }

  return (
    <View style={styles.container} onLayout={onLayout} accessible accessibilityRole="image" accessibilityLabel={accessibilityLabel}>
      {width > 0 ? (
        <Svg width={width} height={CHART_HEIGHT}>
          <Grid scale={scale} left={left} right={right} y={y} />
          {markers.filter((marker) => labelledMarkers.has(marker.index)).map((marker) => (
            <SvgText key={`ml-${marker.index}`} x={x(marker.index) + 3} y={top - 9} fontSize={10} fontWeight="700" fill={COLORS.inbound}>
              +{formatQuantity(marker.quantity)}
            </SvgText>
          ))}
          {markers.map((marker) => (
            <Line key={`m-${marker.index}`} x1={x(marker.index)} x2={x(marker.index)} y1={top - 6} y2={bottom} stroke={COLORS.inbound} strokeWidth={1} strokeDasharray="3 3" />
          ))}
          {points.map((point) => {
            const barTop = y(point.quantity);
            const fill = point.date === today && point.date === endDate
              ? COLORS.muted
              : point.date === peakDate
                ? COLORS.peak
                : COLORS.bar;
            return (
              <Rect key={point.date} x={x(point.index) - barWidth / 2} y={barTop} width={barWidth} height={Math.max(bottom - barTop, 1)} rx={1.2} fill={fill} />
            );
          })}
          {peak ? (
            <SvgText x={x(peak.index)} y={y(peak.quantity) - 5} fontSize={10} fontWeight="800" fill={COLORS.peak} textAnchor="middle">
              {formatQuantity(peak.quantity)}
            </SvgText>
          ) : null}
          {tickIndexes.map((index, position) => (
            <SvgText
              key={`t-${position}-${index}`}
              x={position === 0 ? left : position === tickIndexes.length - 1 ? right : x(index)}
              y={CHART_HEIGHT - 5}
              fontSize={10}
              fill={COLORS.axisText}
              textAnchor={position === 0 ? "start" : position === tickIndexes.length - 1 ? "end" : "middle"}
            >
              {tickLabel(index)}
            </SvgText>
          ))}
        </Svg>
      ) : null}
    </View>
  );
}

export interface WeeklyTrendChartProps {
  weeks: WeeklyTrendPoint[];
  accessibilityLabel: string;
}

export function WeeklyTrendChart({ weeks, accessibilityLabel }: WeeklyTrendChartProps) {
  const { width, onLayout } = useWidth();
  const top = 18;
  const bottom = CHART_HEIGHT - 34;
  const scale = niceScale(Math.max(0, ...weeks.map((week) => week.quantity)));
  const left = Math.max(30, formatQuantity(scale.max).length * 6 + 10);
  const right = Math.max(left + 1, width - 4);
  const step = (right - left) / Math.max(weeks.length, 1);
  const barWidth = Math.max(3, Math.min(28, step * 0.56));
  const x = (index: number) => left + index * step + step / 2;
  const y = (value: number) => bottom - (value / scale.max) * (bottom - top);
  // 周数多时隔几周标一次日期与数值，保证文字不重叠。
  const labelEvery = Math.max(1, Math.ceil(34 / Math.max(step, 1)));

  return (
    <View style={styles.container} onLayout={onLayout} accessible accessibilityRole="image" accessibilityLabel={accessibilityLabel}>
      {width > 0 ? (
        <Svg width={width} height={CHART_HEIGHT}>
          <Grid scale={scale} left={left} right={right} y={y} />
          {weeks.map((week, index) => {
            const barTop = y(week.quantity);
            const labelled = index % labelEvery === 0 || index === weeks.length - 1;
            return (
              <G key={week.startDate}>
                <Rect x={x(index) - barWidth / 2} y={barTop} width={barWidth} height={Math.max(bottom - barTop, 1)} rx={2} fill={week.partial ? COLORS.muted : COLORS.bar} />
                {labelled ? (
                  <>
                    <SvgText x={x(index)} y={barTop - 4} fontSize={10} fontWeight="700" fill={COLORS.value} textAnchor="middle">
                      {formatQuantity(week.quantity)}
                    </SvgText>
                    <SvgText x={x(index)} y={bottom + 14} fontSize={10} fill={COLORS.axisText} textAnchor="middle">
                      {shortDate(week.startDate)}
                    </SvgText>
                  </>
                ) : null}
                {week.inboundQuantity > 0 ? (
                  <SvgText x={x(index)} y={bottom + 28} fontSize={10} fontWeight="700" fill={COLORS.inbound} textAnchor="middle">
                    +{formatQuantity(week.inboundQuantity)}
                  </SvgText>
                ) : null}
              </G>
            );
          })}
        </Svg>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { width: "100%", height: CHART_HEIGHT },
});
