import { useMemo, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import type { ProductReportCostStatus } from "@/modules/product-report/api";
import {
  CHINA_BRANCH_COLLAPSED_ROW_COUNT,
  DEFAULT_CHINA_BRANCH_SHARE_SORT,
  getChinaBranchShareScaleMax,
  sortChinaBranchShareRows,
  toggleChinaBranchShareSort,
  type ChinaBranchShareRow,
  type ChinaBranchShareSort,
  type ChinaBranchShareSortField,
  type ChinaGoodsSummary,
} from "@/modules/product-report/china-goods-share";
import { formatWholeDollars } from "@/modules/reports/format";
import { GROWTH_COLORS, formatGrowthRate, getGrowthTone } from "@/modules/reports/growth-rate";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const SORT_HIT_SLOP = { top: 10, bottom: 10 };
const SHARE_BAR_COLOR = "#2563EB";
const COMPARE_TICK_COLOR = "#111827";

function formatPercent(value: number | null) {
  return value === null || !Number.isFinite(value) ? "--" : `${(value * 100).toFixed(1)}%`;
}

/** 占比增减按 0.1 个百分点取整后再定颜色，避免显示 0.0 却标成绿色或红色。 */
function getPointsTone(points: number | null) {
  if (points === null) return "flat" as const;
  const rounded = Math.round(points * 10);
  return rounded === 0 ? "flat" as const : rounded > 0 ? "up" as const : "down" as const;
}

function formatSignedPoints(points: number | null) {
  if (points === null || !Number.isFinite(points)) return "--";
  const rounded = Math.round(points * 10) / 10;
  const sign = rounded > 0 ? "+" : rounded < 0 ? "−" : "";
  return `${sign}${Math.abs(rounded).toFixed(1)}`;
}

function getSharePointsDelta(current: number | null, compare: number | null) {
  return current === null || compare === null ? null : (current - compare) * 100;
}

function formatMarginRate(
  value: number | null,
  costStatus: ProductReportCostStatus,
  costPendingLabel: string,
  noActivityLabel: string,
) {
  if (costStatus === "NoActivity") return noActivityLabel;
  if (costStatus === "Missing") return costPendingLabel;
  return formatPercent(value);
}

function SummaryMetric({
  label,
  value,
  compareLabel,
  delta,
  deltaColor,
  bordered,
}: {
  label: string;
  value: string;
  compareLabel: string;
  delta: string;
  deltaColor: string;
  bordered?: boolean;
}) {
  return (
    <View style={[styles.summaryMetric, bordered ? styles.summaryMetricBordered : null]}>
      <Text variant="labelSmall" style={styles.muted} numberOfLines={1}>{label}</Text>
      {/* 全部分店月度合计可能到七位数，窄屏三等分时自动缩小字号，不截断金额。 */}
      <Text
        variant="titleMedium"
        style={styles.summaryValue}
        numberOfLines={1}
        adjustsFontSizeToFit
        minimumFontScale={0.7}
        selectable
      >
        {value}
      </Text>
      <Text variant="labelSmall" style={styles.muted} numberOfLines={1}>{compareLabel}</Text>
      <Text variant="labelSmall" style={[styles.summaryDelta, { color: deltaColor }]} numberOfLines={1}>{delta}</Text>
    </View>
  );
}

/** 中国供应商页签顶部汇总：中国货营业额、占总营业额、中国货毛利率，均带同期。 */
export function ChinaGoodsSummaryCard({ summary }: { summary: ChinaGoodsSummary }) {
  const { t } = useAppTranslation("common");
  const compare = t("productReport.metrics.compare");
  const costPendingLabel = t("productReport.states.costPending");
  const noActivityLabel = t("productReport.states.costNoActivity");
  const shareDelta = getSharePointsDelta(summary.share, summary.compareShare);
  const marginDelta = summary.costStatus === "Complete" && summary.compareCostStatus === "Complete"
    ? getSharePointsDelta(summary.grossMarginRate, summary.compareGrossMarginRate)
    : null;
  const pointsLabel = (points: number | null) => points === null
    ? "--"
    : t("productReport.chinaGoods.pointsValue", { value: formatSignedPoints(points) });
  return (
    <View style={styles.summaryCard}>
      <SummaryMetric
        label={t("productReport.chinaGoods.revenue")}
        value={formatWholeDollars(summary.revenue)}
        compareLabel={`${compare} ${formatWholeDollars(summary.compareRevenue)}`}
        delta={formatGrowthRate(summary.revenue, summary.compareRevenue, t("productReport.metrics.newGrowth"))}
        deltaColor={GROWTH_COLORS[getGrowthTone(summary.revenue, summary.compareRevenue)]}
      />
      <SummaryMetric
        bordered
        label={t("productReport.chinaGoods.shareOfTotal")}
        value={formatPercent(summary.share)}
        compareLabel={`${compare} ${formatPercent(summary.compareShare)}`}
        delta={pointsLabel(shareDelta)}
        deltaColor={GROWTH_COLORS[getPointsTone(shareDelta)]}
      />
      <SummaryMetric
        bordered
        label={t("productReport.chinaGoods.grossMarginRate")}
        value={formatMarginRate(summary.grossMarginRate, summary.costStatus, costPendingLabel, noActivityLabel)}
        compareLabel={`${compare} ${formatMarginRate(
          summary.compareGrossMarginRate,
          summary.compareCostStatus,
          costPendingLabel,
          noActivityLabel,
        )}`}
        delta={pointsLabel(marginDelta)}
        deltaColor={GROWTH_COLORS[getPointsTone(marginDelta)]}
      />
    </View>
  );
}

function SortHeader({
  label,
  field,
  sort,
  onSort,
  align,
}: {
  label: string;
  field: ChinaBranchShareSortField;
  sort: ChinaBranchShareSort;
  onSort: (field: ChinaBranchShareSortField) => void;
  align: "left" | "right";
}) {
  const { t } = useAppTranslation("common");
  const active = sort.field === field;
  const descending = sort.order === "desc";
  const state = !active
    ? t("productReport.sort.none")
    : descending ? t("productReport.sort.desc") : t("productReport.sort.asc");
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected: active }}
      accessibilityLabel={t("productReport.sort.accessibilityLabel", { column: label, state })}
      hitSlop={SORT_HIT_SLOP}
      onPress={() => onSort(field)}
      style={[styles.sortHeader, align === "right" ? styles.sortHeaderRight : null]}
    >
      <Text variant="bodySmall" numberOfLines={1} style={[styles.headerText, styles.sortLabel, active ? styles.sortActive : null]}>
        {label}
      </Text>
      <Text variant="bodySmall" style={[styles.sortIndicator, active ? styles.sortActive : null]}>
        {active ? (descending ? "▼" : "▲") : "⇅"}
      </Text>
    </Pressable>
  );
}

function ShareBar({ share, compareShare, scaleMax }: { share: number | null; compareShare: number | null; scaleMax: number }) {
  const toPercent = (value: number) => `${Math.min(100, Math.max(0, (value / scaleMax) * 100))}%` as const;
  return (
    <View style={styles.shareBar} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
      <View style={styles.shareBarTrack} />
      {share !== null ? <View style={[styles.shareBarFill, { width: toPercent(share) }]} /> : null}
      {/* 黑色竖线标出同期占比：落在蓝条外说明占比上升，落在蓝条内说明下降。 */}
      {compareShare !== null ? (
        <View style={[styles.shareBarTick, { left: toPercent(compareShare), marginLeft: -1 }]} />
      ) : null}
    </View>
  );
}

/**
 * 分店中国货占比：中国货金额 ÷ 分店营业额，本期与同期上下两行，默认按中国货金额降序。
 * 主报表四块数据同批次到齐后才渲染本区块，因此这里不再单独处理加载与失败状态。
 */
export function ChinaBranchShareSection({ rows }: { rows: readonly ChinaBranchShareRow[] }) {
  const { t } = useAppTranslation("common");
  const [sort, setSort] = useState<ChinaBranchShareSort>(DEFAULT_CHINA_BRANCH_SHARE_SORT);
  const [expanded, setExpanded] = useState(false);
  const sortedRows = useMemo(() => sortChinaBranchShareRows(rows, sort), [rows, sort]);
  const scaleMax = useMemo(() => getChinaBranchShareScaleMax(rows), [rows]);
  const visibleRows = expanded ? sortedRows : sortedRows.slice(0, CHINA_BRANCH_COLLAPSED_ROW_COUNT);
  const canExpand = sortedRows.length > CHINA_BRANCH_COLLAPSED_ROW_COUNT;

  return (
    <View style={styles.section}>
      <View style={styles.sectionHeader}>
        <View style={styles.sectionTitleBlock}>
          <View style={styles.titleLine}>
            <Text variant="titleMedium" style={styles.sectionTitle}>{t("productReport.chinaGoods.branchSection")}</Text>
            <Text variant="labelSmall" style={styles.muted}>
              <Text variant="labelSmall" style={styles.strong}>{t("reports.metrics.current")}</Text>
              {` / ${t("productReport.metrics.compare")}`}
            </Text>
          </View>
          <Text variant="labelSmall" style={styles.muted}>{t("productReport.chinaGoods.branchFormula")}</Text>
        </View>
        <View style={styles.legend} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
          <View style={styles.legendItem}>
            <View style={styles.legendBar} />
            <Text variant="labelSmall" style={styles.muted}>{t("reports.metrics.current")}</Text>
          </View>
          <View style={styles.legendItem}>
            <View style={styles.legendTick} />
            <Text variant="labelSmall" style={styles.muted}>{t("productReport.metrics.compare")}</Text>
          </View>
        </View>
      </View>

      <View style={styles.table}>
        <View style={[styles.row, styles.headerRow]}>
          <View style={styles.branchColumn}>
            <Text variant="bodySmall" style={styles.headerText} numberOfLines={1}>{t("productReport.chinaGoods.branch")}</Text>
            <Text variant="labelSmall" style={styles.muted} numberOfLines={1}>{t("productReport.chinaGoods.branchRevenue")}</Text>
          </View>
          <View style={styles.amountColumn}>
            <SortHeader
              label={t("productReport.chinaGoods.chinaAmount")}
              field="amount"
              sort={sort}
              onSort={(field) => setSort((current) => toggleChinaBranchShareSort(current, field))}
              align="right"
            />
          </View>
          <View style={styles.shareColumn}>
            <SortHeader
              label={t("productReport.chinaGoods.branchShare")}
              field="share"
              sort={sort}
              onSort={(field) => setSort((current) => toggleChinaBranchShareSort(current, field))}
              align="left"
            />
            <View style={styles.scaleLine}>
              <View style={styles.shareNumbers} />
              {/* 横条只有约 60pt 宽，刻度只标两端；中点标签会和两端挤在一起。 */}
              <View style={styles.scaleLabels}>
                <Text style={styles.scaleText}>0</Text>
                <Text style={styles.scaleText}>{formatPercent(scaleMax).replace(".0%", "%")}</Text>
              </View>
            </View>
          </View>
          <View style={styles.deltaColumn}>
            <Text variant="bodySmall" style={[styles.headerText, styles.alignRight]} numberOfLines={1}>
              {t("productReport.chinaGoods.shareDelta")}
            </Text>
            <Text variant="labelSmall" style={[styles.muted, styles.alignRight]} numberOfLines={1}>
              {t("productReport.chinaGoods.points")}
            </Text>
          </View>
        </View>
        {visibleRows.length === 0 ? (
          <View style={styles.stateBox}>
            <Text variant="bodySmall">{t("productReport.chinaGoods.emptyBranches")}</Text>
          </View>
        ) : visibleRows.map((row) => (
          <View key={row.branchCode} style={styles.row}>
            <View style={styles.branchColumn}>
              <Text variant="bodySmall" style={styles.strong} numberOfLines={1} selectable>{row.branchName}</Text>
              <Text variant="labelSmall" style={[styles.muted, styles.numeric]} numberOfLines={1}>
                {formatWholeDollars(row.branchRevenue)}
              </Text>
            </View>
            <View style={styles.amountColumn}>
              <Text variant="bodySmall" style={[styles.strong, styles.numeric, styles.alignRight]} numberOfLines={1} selectable>
                {formatWholeDollars(row.chinaRevenue)}
              </Text>
              <Text variant="bodySmall" style={[styles.muted, styles.numeric, styles.alignRight]} numberOfLines={1}>
                {formatWholeDollars(row.compareChinaRevenue)}
              </Text>
            </View>
            <View style={[styles.shareColumn, styles.shareCell]}>
              <View style={styles.shareNumbers}>
                <Text variant="bodySmall" style={[styles.strong, styles.numeric, styles.alignRight]} numberOfLines={1}>
                  {formatPercent(row.share)}
                </Text>
                <Text variant="bodySmall" style={[styles.muted, styles.numeric, styles.alignRight]} numberOfLines={1}>
                  {formatPercent(row.compareShare)}
                </Text>
              </View>
              <ShareBar share={row.share} compareShare={row.compareShare} scaleMax={scaleMax} />
            </View>
            <View style={styles.deltaColumn}>
              <Text
                variant="bodySmall"
                style={[styles.strong, styles.numeric, styles.alignRight, { color: GROWTH_COLORS[getPointsTone(row.shareDeltaPoints)] }]}
                numberOfLines={1}
              >
                {formatSignedPoints(row.shareDeltaPoints)}
              </Text>
            </View>
          </View>
        ))}
        {canExpand ? (
          <Pressable
            accessibilityRole="button"
            onPress={() => setExpanded((current) => !current)}
            style={styles.expandButton}
          >
            <Text variant="labelLarge" style={styles.expandText}>
              {expanded
                ? t("productReport.chinaGoods.collapseBranches")
                : t("productReport.chinaGoods.expandBranches", { count: sortedRows.length })}
            </Text>
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  muted: {
    color: "#6B7280",
  },
  strong: {
    color: "#111827",
    fontWeight: "700",
  },
  numeric: {
    fontVariant: ["tabular-nums"],
  },
  alignRight: {
    textAlign: "right",
  },
  summaryCard: {
    flexDirection: "row",
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 10,
    backgroundColor: "#FFFFFF",
    paddingVertical: 12,
  },
  summaryMetric: {
    flex: 1,
    minWidth: 0,
    gap: 2,
    paddingHorizontal: 10,
  },
  summaryMetricBordered: {
    borderLeftWidth: 1,
    borderLeftColor: "#EEF0F3",
  },
  summaryValue: {
    color: "#111827",
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  summaryDelta: {
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  section: {
    gap: 8,
  },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "flex-end",
    justifyContent: "space-between",
    gap: 8,
    marginTop: 4,
  },
  sectionTitleBlock: {
    flex: 1,
    minWidth: 0,
    gap: 2,
  },
  titleLine: {
    flexDirection: "row",
    alignItems: "baseline",
    gap: 8,
  },
  sectionTitle: {
    color: "#111827",
    fontWeight: "700",
  },
  legend: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  legendItem: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  legendBar: {
    width: 12,
    height: 8,
    borderRadius: 2,
    backgroundColor: SHARE_BAR_COLOR,
  },
  legendTick: {
    width: 2,
    height: 12,
    borderRadius: 1,
    backgroundColor: COMPARE_TICK_COLOR,
  },
  stateBox: {
    minHeight: 72,
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    padding: 12,
  },
  table: {
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  row: {
    minHeight: 52,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    borderBottomWidth: 1,
    borderBottomColor: "#EEF0F3",
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  headerRow: {
    minHeight: 44,
    alignItems: "flex-end",
    backgroundColor: "#F3F4F6",
    borderBottomColor: "#E5E7EB",
  },
  headerText: {
    color: "#374151",
    fontWeight: "700",
  },
  branchColumn: {
    // 分店名吃掉剩余宽度，其余三列固定，窄屏也不横滑。
    flex: 1,
    minWidth: 60,
    gap: 2,
  },
  amountColumn: {
    width: 78,
    gap: 2,
  },
  shareColumn: {
    width: 122,
    gap: 3,
  },
  shareCell: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  shareNumbers: {
    width: 46,
    gap: 2,
  },
  deltaColumn: {
    // 英文表头 "Change" 在 44pt 会截成 "Cha…"，文案已缩写为 Chg，48pt 连 "-12.3" 也放得下。
    width: 48,
    gap: 2,
  },
  sortHeader: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
    minWidth: 0,
  },
  sortHeaderRight: {
    justifyContent: "flex-end",
  },
  sortLabel: {
    flexShrink: 1,
  },
  sortIndicator: {
    color: "#9CA3AF",
    fontSize: 10,
    lineHeight: 14,
  },
  sortActive: {
    color: "#2563EB",
  },
  scaleLine: {
    flexDirection: "row",
    gap: 8,
  },
  scaleLabels: {
    flex: 1,
    flexDirection: "row",
    justifyContent: "space-between",
  },
  scaleText: {
    color: "#9CA3AF",
    fontSize: 10,
    lineHeight: 12,
  },
  shareBar: {
    flex: 1,
    height: 18,
    position: "relative",
  },
  shareBarTrack: {
    position: "absolute",
    left: 0,
    right: 0,
    top: 5,
    height: 8,
    borderRadius: 2,
    backgroundColor: "#E8EDF5",
  },
  shareBarFill: {
    position: "absolute",
    left: 0,
    top: 5,
    height: 8,
    borderRadius: 2,
    backgroundColor: SHARE_BAR_COLOR,
  },
  shareBarTick: {
    position: "absolute",
    top: 1,
    width: 2,
    height: 16,
    borderRadius: 1,
    backgroundColor: COMPARE_TICK_COLOR,
  },
  expandButton: {
    minHeight: 44,
    alignItems: "center",
    justifyContent: "center",
  },
  expandText: {
    color: "#2563EB",
    fontWeight: "700",
  },
});
