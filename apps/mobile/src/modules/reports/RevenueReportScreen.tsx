import { useMemo, useState } from "react";
import {
  FlatList,
  Pressable,
  RefreshControl,
  StyleSheet,
  View,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, Button, SegmentedButtons, Text } from "react-native-paper";
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
  getPreviousRevenuePeriod,
  getYesterdayRevenuePeriod,
} from "@/modules/reports/periods";
import {
  formatMoney,
  formatRatio,
  formatSignedMoney,
  getDeltaIntent,
  getIntentColor,
} from "@/modules/reports/format";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type Drilldown =
  | { type: "hourly"; branch: BranchRevenueRow }
  | { type: "daily"; branch: BranchRevenueRow };

type DetailRow = HourlyRevenueRow | DailyRevenueRow;

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

function MetricText({ value, ratio }: { value: number; ratio: number | null }) {
  const intent = getDeltaIntent(value, ratio);
  return (
    <Text variant="bodySmall" style={[styles.deltaText, { color: getIntentColor(intent) }]}>
      {formatSignedMoney(value)} / {formatRatio(ratio)}
    </Text>
  );
}

export function RevenueReportScreen() {
  const { t } = useAppTranslation("common");
  const [section, setSection] = useState<"revenue" | "product">("revenue");
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

  const shortcut =
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
  const activeQuery = drilldown ? detailQuery : summaryQuery;

  const renderSummaryRow = ({ item }: { item: BranchRevenueRow }) => (
    <Pressable
      style={styles.row}
      onPress={() => setDrilldown({ type: mode === "day" ? "hourly" : "daily", branch: item })}
    >
      <View style={styles.rowMain}>
        <Text variant="titleSmall" numberOfLines={1} ellipsizeMode="tail" style={styles.branchName}>
          {item.branchName || item.branchCode}
        </Text>
        <Text variant="bodySmall" style={styles.muted}>
          {item.branchCode} · {t("reports.transactions", { count: item.transactions })}
        </Text>
        <Text variant="bodySmall" style={styles.muted}>
          {t("reports.compareRevenue", { value: formatMoney(item.compareRevenue) })}
        </Text>
      </View>
      <View style={styles.rowMetric}>
        <Text variant="titleMedium" style={styles.amount}>
          {formatMoney(item.revenue)}
        </Text>
        <MetricText value={item.revenueDelta} ratio={item.revenueDeltaRatio} />
      </View>
    </Pressable>
  );

  const renderDetailRow = ({ item }: { item: DetailRow }) => (
    <View style={styles.row}>
      <View style={styles.rowMain}>
        <Text variant="titleSmall" numberOfLines={1} ellipsizeMode="tail" style={styles.branchName}>
          {isDailyRow(item) ? item.date : item.label}
        </Text>
        <Text variant="bodySmall" style={styles.muted}>
          {t("reports.transactions", { count: item.transactions })}
        </Text>
        <Text variant="bodySmall" style={styles.muted}>
          {t("reports.compareRevenue", { value: formatMoney(item.compareRevenue) })}
        </Text>
      </View>
      <View style={styles.rowMetric}>
        <Text variant="titleMedium" style={styles.amount}>
          {formatMoney(item.revenue)}
        </Text>
        <MetricText value={item.revenueDelta} ratio={item.revenueDeltaRatio} />
      </View>
    </View>
  );

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text variant="headlineSmall" style={styles.title}>
          {t("reports.title")}
        </Text>
        <Text variant="bodySmall" style={styles.muted}>
          {getPeriodLabel(period)}
        </Text>
      </View>

      <View style={styles.sectionTabs}>
        <Button
          mode={section === "revenue" ? "contained" : "outlined"}
          compact
          onPress={() => setSection("revenue")}
        >
          {t("reports.sections.revenue")}
        </Button>
        <Button mode="outlined" compact disabled onPress={() => setSection("product")}>
          {t("reports.sections.product")}
        </Button>
      </View>

      <SegmentedButtons
        value={mode}
        onValueChange={(value) => setPeriodMode(value as RevenuePeriodMode)}
        buttons={[
          { value: "day", label: t("reports.periods.day") },
          { value: "week", label: t("reports.periods.week") },
          { value: "month", label: t("reports.periods.month") },
        ]}
        style={styles.segmented}
      />

      <View style={styles.toolbar}>
        <Button compact mode="outlined" icon="chevron-left" onPress={() => setActivePeriod(getPreviousRevenuePeriod(period))}>
          {t("reports.actions.previous")}
        </Button>
        <Button compact mode="outlined" icon="chevron-right" onPress={() => setActivePeriod(getNextRevenuePeriod(period))}>
          {t("reports.actions.next")}
        </Button>
      </View>
      <View style={styles.toolbar}>
        <Button compact onPress={() => setActivePeriod(shortcut.period())}>
          {shortcut.label}
        </Button>
      </View>

      {drilldown ? (
        <View style={styles.detailHeader}>
          <Button compact icon="arrow-left" onPress={() => setDrilldown(null)}>
            {t("actions.back")}
          </Button>
          <Text variant="titleSmall" numberOfLines={1} style={styles.detailTitle}>
            {drilldown.branch.branchName || drilldown.branch.branchCode}
          </Text>
        </View>
      ) : null}

      {activeQuery.isLoading ? (
        <View style={styles.stateBox}>
          <ActivityIndicator />
          <Text style={styles.stateText}>{t("loading")}</Text>
        </View>
      ) : activeQuery.isError ? (
        <View style={styles.stateBox}>
          <Text variant="titleSmall">{t("reports.states.errorTitle")}</Text>
          <Button mode="contained" onPress={() => activeQuery.refetch()}>
            {t("actions.retry")}
          </Button>
        </View>
      ) : drilldown ? (
        <FlatList
          data={detailRows}
          keyExtractor={(item) => item.id}
          renderItem={renderDetailRow}
          contentContainerStyle={styles.listContent}
          refreshControl={
            <RefreshControl refreshing={detailQuery.isRefetching} onRefresh={() => detailQuery.refetch()} />
          }
          ListEmptyComponent={
            <View style={styles.stateBox}>
              <Text variant="titleSmall">{t("reports.states.empty")}</Text>
            </View>
          }
        />
      ) : (
        <FlatList
          data={rows}
          keyExtractor={(item) => item.id}
          renderItem={renderSummaryRow}
          contentContainerStyle={styles.listContent}
          refreshControl={
            <RefreshControl refreshing={summaryQuery.isRefetching} onRefresh={() => summaryQuery.refetch()} />
          }
          ListEmptyComponent={
            <View style={styles.stateBox}>
              <Text variant="titleSmall">{t("reports.states.empty")}</Text>
            </View>
          }
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F8FAFC",
    padding: 16,
  },
  header: {
    gap: 4,
    marginBottom: 12,
  },
  title: {
    fontWeight: "700",
  },
  sectionTabs: {
    flexDirection: "row",
    gap: 8,
    marginBottom: 12,
  },
  segmented: {
    marginBottom: 10,
  },
  toolbar: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    marginBottom: 8,
  },
  detailHeader: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginVertical: 6,
  },
  detailTitle: {
    flex: 1,
  },
  listContent: {
    gap: 8,
    paddingBottom: 32,
  },
  row: {
    minHeight: 82,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    padding: 14,
  },
  rowMain: {
    flex: 1,
    minWidth: 0,
  },
  branchName: {
    flexShrink: 1,
    fontWeight: "700",
  },
  muted: {
    color: "#64748B",
  },
  rowMetric: {
    alignItems: "flex-end",
    minWidth: 116,
  },
  amount: {
    fontWeight: "700",
  },
  deltaText: {
    marginTop: 2,
  },
  stateBox: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 12,
    padding: 24,
  },
  stateText: {
    color: "#64748B",
  },
});
