import { useMemo, useState } from "react";
import {
  FlatList,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, Button, Modal, Portal, SegmentedButtons, Text } from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import {
  BranchRevenueRow,
  DailyRevenueRow,
  HourlyRevenueRow,
  fetchBranchDailyPerformance,
  fetchExecutiveBranchPerformance,
  fetchExecutiveHourlyTraffic,
} from "@/modules/reports/api";
import {
  RevenuePeriod,
  RevenuePeriodMode,
  getCompareRevenuePeriod,
  getDefaultRevenuePeriod,
  getLastMonthRevenuePeriod,
  getLastWeekRevenuePeriod,
  getNextRevenuePeriod,
  parseDateKey,
  getPreviousRevenuePeriod,
  getYesterdayRevenuePeriod,
} from "@/modules/reports/periods";
import { formatMoney } from "@/modules/reports/format";
import { GROWTH_COLORS, formatGrowthRate, getGrowthTone } from "@/modules/reports/growth-rate";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type Drilldown =
  | { type: "hourly"; branch: BranchRevenueRow }
  | { type: "daily"; branch: BranchRevenueRow };

type DetailRow = HourlyRevenueRow | DailyRevenueRow;

interface RevenueReportScreenProps {
  embedded?: boolean;
}

function buildQuery(period: RevenuePeriod, branchCode?: string) {
  const compareMode =
    period.mode === "day"
      ? "lastYearSameWeekday"
      : period.mode === "week"
        ? "lastYearIsoWeek"
        : "lastYearSameMonth";
  const comparePeriod = getCompareRevenuePeriod(period, compareMode);
  return {
    startDate: period.startDate,
    endDate: period.endDate,
    compareStartDate: comparePeriod.startDate,
    compareEndDate: comparePeriod.endDate,
    compareMode: period.mode === "month" ? "ByDate" as const : "ByWeek" as const,
    branchCodes: branchCode ? [branchCode] : undefined,
  };
}

function getPeriodLabel(period: RevenuePeriod) {
  return period.startDate === period.endDate
    ? period.startDate
    : `${period.startDate} - ${period.endDate}`;
}

function isDailyRow(row: DetailRow): row is DailyRevenueRow {
  return "date" in row;
}

function getDetailDateParts(value: string) {
  try {
    const date = parseDateKey(value);
    return { dateLabel: value, weekday: date.getDay() };
  } catch {
    return { dateLabel: value, weekday: null };
  }
}

function formatCount(value: number) {
  return Math.round(value).toLocaleString("en-AU");
}

function formatWholeMoney(value: number | null | undefined) {
  const amount = typeof value === "number" && Number.isFinite(value) ? value : 0;
  // 营业额表空间有限，只在营业额列取整；客单价仍保留两位小数。
  return `$${Math.round(amount).toLocaleString("en-AU")}`;
}

function TableText({
  children,
  style,
  numeric,
}: {
  children: string;
  style?: object;
  numeric?: boolean;
}) {
  return (
    <Text
      variant="bodySmall"
      numberOfLines={1}
      selectable
      style={[styles.tableCellText, numeric ? styles.numericText : null, style]}
    >
      {children}
    </Text>
  );
}

function DetailPeriodText({
  item,
  getWeekdayLabel,
}: {
  item: DetailRow;
  getWeekdayLabel: (weekday: number) => string;
}) {
  if (!isDailyRow(item)) {
    return <TableText style={styles.strongText}>{item.label}</TableText>;
  }

  const label = getDetailDateParts(item.date);
  return (
    <Text
      variant="bodySmall"
      numberOfLines={1}
      selectable
      style={[styles.tableCellText, styles.strongText]}
    >
      {label.dateLabel}
      {label.weekday !== null ? <Text style={styles.weekdayText}> {getWeekdayLabel(label.weekday)}</Text> : null}
    </Text>
  );
}

export function RevenueReportScreen({ embedded = false }: RevenueReportScreenProps) {
  const { t } = useAppTranslation("common");
  const insets = useSafeAreaInsets();
  const [mode, setMode] = useState<RevenuePeriodMode>("day");
  const [period, setPeriod] = useState(() => getDefaultRevenuePeriod("day"));
  const [drilldown, setDrilldown] = useState<Drilldown | null>(null);

  const queryParams = useMemo(() => buildQuery(period), [period]);
  const summaryQuery = useQuery({
    queryKey: ["reports", "revenue-summary", queryParams],
    queryFn: () => fetchExecutiveBranchPerformance(queryParams),
  });

  const detailParams = useMemo(
    () => buildQuery(period, drilldown?.branch.branchCode),
    [drilldown?.branch.branchCode, period]
  );
  const detailQuery = useQuery<DetailRow[]>({
    queryKey: ["reports", drilldown?.type, detailParams],
    queryFn: async () => {
      if (drilldown?.type === "hourly") {
        return fetchExecutiveHourlyTraffic(detailParams);
      }
      return fetchBranchDailyPerformance(detailParams);
    },
    enabled: Boolean(drilldown),
  });

  const setPeriodMode = (nextMode: RevenuePeriodMode) => {
    setMode(nextMode);
    setPeriod(getDefaultRevenuePeriod(nextMode));
    setDrilldown(null);
  };

  const setActivePeriod = (nextPeriod: RevenuePeriod) => {
    setMode(nextPeriod.mode);
    setPeriod(nextPeriod);
    setDrilldown(null);
  };

  const currentShortcut =
    mode === "day"
      ? t("reports.shortcuts.today")
      : mode === "week"
        ? t("reports.shortcuts.thisWeek")
        : t("reports.shortcuts.thisMonth");
  const previousShortcut =
    mode === "day"
      ? {
          label: t("reports.shortcuts.yesterday"),
          period: getYesterdayRevenuePeriod,
        }
      : mode === "week"
        ? {
            label: t("reports.shortcuts.lastWeek"),
            period: getLastWeekRevenuePeriod,
          }
        : {
            label: t("reports.shortcuts.lastMonth"),
            period: getLastMonthRevenuePeriod,
          };

  const rows = summaryQuery.data ?? [];
  const detailRows = (detailQuery.data ?? []) as DetailRow[];
  const growthNewLabel = t("reports.metrics.newGrowth");
  const getWeekdayLabel = (weekday: number) => t(`reports.weekdaysShort.${weekday}`);

  const renderGrowthCell = (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => {
    const tone = getGrowthTone(current, compare);
    return (
      <View style={[styles.growthColumn, columnStyle]}>
        <TableText numeric style={[styles.strongText, { color: GROWTH_COLORS[tone] }]}>
          {formatGrowthRate(current, compare, growthNewLabel)}
        </TableText>
      </View>
    );
  };

  const renderSummaryRow = ({ item }: { item: BranchRevenueRow }) => (
    <Pressable
      style={styles.tableRow}
      accessibilityRole="button"
      accessibilityLabel={t(
        mode === "day"
          ? "reports.accessibility.openHourlyDetail"
          : "reports.accessibility.openDailyDetail",
        { branch: item.branchName || item.branchCode }
      )}
      onPress={() => setDrilldown({ type: mode === "day" ? "hourly" : "daily", branch: item })}
    >
      <View style={styles.branchColumn}>
        <TableText style={styles.strongText}>{item.branchName || item.branchCode}</TableText>
        <TableText style={styles.muted}>{item.branchCode}</TableText>
      </View>
      <View style={styles.amountColumn}>
        <TableText numeric style={styles.strongText}>{formatWholeMoney(item.revenue)}</TableText>
        <TableText numeric style={styles.muted}>{formatWholeMoney(item.compareRevenue)}</TableText>
      </View>
      {renderGrowthCell(item.revenue, item.compareRevenue)}
      <View style={styles.countColumn}>
        <TableText numeric style={styles.strongText}>{formatCount(item.transactions)}</TableText>
        <TableText numeric style={styles.muted}>{formatCount(item.compareTransactions)}</TableText>
      </View>
      <View style={styles.amountColumn}>
        <TableText numeric style={styles.strongText}>{formatMoney(item.averageTransaction)}</TableText>
        <TableText numeric style={styles.muted}>{formatMoney(item.compareAverageTransaction)}</TableText>
      </View>
    </Pressable>
  );

  const renderDetailRow = (item: DetailRow, index: number) => (
    <View
      key={item.id}
      style={[
        styles.tableRow,
        styles.detailTableRow,
        index === detailRows.length - 1 ? styles.lastTableRow : null,
      ]}
    >
      <View style={styles.detailPeriodColumn}>
        <DetailPeriodText item={item} getWeekdayLabel={getWeekdayLabel} />
      </View>
      <View style={styles.detailAmountColumn}>
        <TableText numeric style={styles.strongText}>{formatWholeMoney(item.revenue)}</TableText>
        <TableText numeric style={styles.muted}>{formatWholeMoney(item.compareRevenue)}</TableText>
      </View>
      {renderGrowthCell(item.revenue, item.compareRevenue, styles.detailGrowthColumn)}
      <View style={styles.detailCountColumn}>
        <TableText numeric style={styles.strongText}>{formatCount(item.transactions)}</TableText>
        <TableText numeric style={styles.muted}>{formatCount(item.compareTransactions)}</TableText>
      </View>
      <View style={styles.detailAmountColumn}>
        <TableText numeric style={styles.strongText}>{formatMoney(item.averageTransaction)}</TableText>
        <TableText numeric style={styles.muted}>{formatMoney(item.compareAverageTransaction)}</TableText>
      </View>
    </View>
  );

  return (
    <View style={styles.container}>
      <View style={styles.content}>
        {!embedded ? (
          <View style={styles.header}>
            <Text variant="headlineSmall" style={styles.title}>
              {t("reports.title")}
            </Text>
            <Text variant="bodySmall" style={styles.muted}>
              {getPeriodLabel(period)}
            </Text>
          </View>
        ) : (
          <Text variant="bodySmall" style={styles.muted}>
            {getPeriodLabel(period)}
          </Text>
        )}

        <SegmentedButtons
          value={mode}
          onValueChange={(value) => setPeriodMode(value as RevenuePeriodMode)}
          buttons={[
            { value: "day", label: t("reports.periods.day") },
            { value: "week", label: t("reports.periods.week") },
            { value: "month", label: t("reports.periods.month") },
          ]}
        />

        <View style={styles.toolbar}>
          <Button compact mode="outlined" icon="chevron-left" onPress={() => setActivePeriod(getPreviousRevenuePeriod(period))}>
            {t("reports.actions.previous")}
          </Button>
          <Button compact mode="outlined" icon="chevron-right" onPress={() => setActivePeriod(getNextRevenuePeriod(period))}>
            {t("reports.actions.next")}
          </Button>
          <Button compact onPress={() => setActivePeriod(getDefaultRevenuePeriod(mode))}>
            {currentShortcut}
          </Button>
          <Button compact onPress={() => setActivePeriod(previousShortcut.period())}>
            {previousShortcut.label}
          </Button>
        </View>

        <View style={[styles.table, styles.mainTable]}>
          <View style={[styles.tableRow, styles.tableHeaderRow]}>
            <View style={styles.branchColumn}>
              <TableText style={styles.headerText}>{t("productReport.filters.store")}</TableText>
            </View>
            <View style={styles.amountColumn}>
              <TableText numeric style={styles.headerText}>{t("reports.metrics.revenue")}</TableText>
            </View>
            <View style={styles.growthColumn}>
              <TableText numeric style={styles.headerText}>{t("reports.metrics.growthRate")}</TableText>
            </View>
            <View style={styles.countColumn}>
              <TableText numeric style={styles.headerText}>{t("reports.metrics.transactions")}</TableText>
            </View>
            <View style={styles.amountColumn}>
              <TableText numeric style={styles.headerText}>{t("reports.metrics.averageTransaction")}</TableText>
            </View>
          </View>
          {summaryQuery.isLoading ? (
            <StateBox label={t("loading")} loading />
          ) : summaryQuery.isError ? (
            <StateBox label={t("reports.states.errorTitle")} actionLabel={t("actions.retry")} onAction={() => summaryQuery.refetch()} />
          ) : (
            <FlatList
              data={rows}
              keyExtractor={(item) => item.id}
              renderItem={renderSummaryRow}
              bounces={false}
              style={styles.tableList}
              contentContainerStyle={[styles.listContent, { paddingBottom: insets.bottom + 96 }]}
              refreshControl={
                <RefreshControl refreshing={summaryQuery.isRefetching} onRefresh={() => summaryQuery.refetch()} />
              }
              ListEmptyComponent={<StateBox label={t("reports.states.empty")} />}
            />
          )}
        </View>
      </View>

      <Portal>
        <Modal
          visible={Boolean(drilldown)}
          onDismiss={() => setDrilldown(null)}
          contentContainerStyle={styles.modal}
        >
          {/* 弹窗外框包住整块白卡，内层只负责留白和内容间距。 */}
          <View style={styles.modalContent}>
            <View style={styles.modalHeader}>
              <Text variant="titleMedium" style={styles.title} numberOfLines={1}>
                {drilldown?.branch.branchName || drilldown?.branch.branchCode}
              </Text>
              <Button compact icon="close" onPress={() => setDrilldown(null)}>
                {t("actions.close")}
              </Button>
            </View>
            <View style={styles.modalTableViewport}>
              <ScrollView horizontal bounces={false} showsHorizontalScrollIndicator={false}>
                <View style={[styles.table, styles.detailTable]}>
                  <View style={[styles.tableRow, styles.tableHeaderRow, styles.detailTableRow]}>
                    <View style={styles.detailPeriodColumn}>
                      <TableText style={styles.headerText}>
                        {mode === "day" ? t("reports.periods.day") : t("reports.periods.date")}
                      </TableText>
                    </View>
                    <View style={styles.detailAmountColumn}>
                      <TableText numeric style={styles.headerText}>{t("reports.metrics.revenue")}</TableText>
                    </View>
                    <View style={styles.detailGrowthColumn}>
                      <TableText numeric style={styles.headerText}>{t("reports.metrics.growthRate")}</TableText>
                    </View>
                    <View style={styles.detailCountColumn}>
                      <TableText numeric style={styles.headerText}>{t("reports.metrics.transactions")}</TableText>
                    </View>
                    <View style={styles.detailAmountColumn}>
                      <TableText numeric style={styles.headerText}>{t("reports.metrics.averageTransaction")}</TableText>
                    </View>
                  </View>
                  {detailQuery.isLoading ? (
                    <StateBox label={t("loading")} loading />
                  ) : detailQuery.isError ? (
                    <StateBox label={t("reports.states.errorTitle")} actionLabel={t("actions.retry")} onAction={() => detailQuery.refetch()} />
                  ) : detailRows.length === 0 ? (
                    <StateBox label={t("reports.states.empty")} />
                  ) : (
                    <ScrollView bounces={false} nestedScrollEnabled style={styles.modalList}>
                      {detailRows.map(renderDetailRow)}
                    </ScrollView>
                  )}
                </View>
              </ScrollView>
            </View>
          </View>
        </Modal>
      </Portal>
    </View>
  );
}

function StateBox({
  label,
  actionLabel,
  onAction,
  loading = false,
}: {
  label: string;
  actionLabel?: string;
  onAction?: () => void;
  loading?: boolean;
}) {
  return (
    <View style={styles.stateBox}>
      {loading ? <ActivityIndicator /> : null}
      <Text variant="bodyMedium" style={styles.stateText}>
        {label}
      </Text>
      {actionLabel && onAction ? (
        <Button mode="contained" onPress={onAction}>
          {actionLabel}
        </Button>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  content: {
    flex: 1,
    gap: 12,
    padding: 16,
    paddingTop: 8,
  },
  header: {
    gap: 4,
  },
  title: {
    color: "#111827",
    fontWeight: "700",
  },
  muted: {
    color: "#6B7280",
  },
  toolbar: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  table: {
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  mainTable: {
    flex: 1,
    minHeight: 0,
  },
  tableRow: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    borderBottomWidth: 1,
    borderBottomColor: "#E5E7EB",
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  tableHeaderRow: {
    minHeight: 38,
    backgroundColor: "#F3F4F6",
  },
  lastTableRow: {
    borderBottomWidth: 0,
  },
  branchColumn: {
    flex: 0.95,
    minWidth: 0,
  },
  amountColumn: {
    flex: 1.05,
    minWidth: 0,
  },
  countColumn: {
    flex: 0.78,
    minWidth: 0,
  },
  growthColumn: {
    flex: 0.9,
    minWidth: 0,
  },
  detailTable: {
    minWidth: 384,
    width: "100%",
  },
  detailTableRow: {
    minHeight: 42,
    gap: 3,
    paddingHorizontal: 4,
    paddingVertical: 6,
  },
  detailPeriodColumn: {
    // 周/月弹窗保留完整日期和星期缩写，避免日期被压成省略号。
    width: 120,
    minWidth: 0,
  },
  detailAmountColumn: {
    width: 68,
    minWidth: 0,
  },
  detailGrowthColumn: {
    flex: 0,
    width: 64,
    minWidth: 0,
  },
  detailCountColumn: {
    width: 44,
    minWidth: 0,
  },
  tableCellText: {
    color: "#111827",
    fontVariant: ["tabular-nums"],
  },
  numericText: {
    textAlign: "right",
  },
  strongText: {
    fontWeight: "700",
  },
  headerText: {
    color: "#374151",
    fontWeight: "700",
  },
  weekdayText: {
    color: "#2563EB",
    fontWeight: "700",
  },
  listContent: {
    paddingBottom: 8,
  },
  tableList: {
    flex: 1,
  },
  stateBox: {
    minHeight: 96,
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
    padding: 16,
  },
  stateText: {
    color: "#64748B",
  },
  modal: {
    alignSelf: "stretch",
    maxHeight: "82%",
    marginHorizontal: 12,
    marginVertical: 18,
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "#D1D5DB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  modalContent: {
    gap: 8,
    padding: 10,
  },
  modalTableViewport: {
    alignSelf: "stretch",
    flexShrink: 1,
  },
  modalList: {
    flexGrow: 0,
    maxHeight: 360,
  },
  modalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
});
