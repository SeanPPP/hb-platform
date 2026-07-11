import { useCallback, useEffect, useRef, useState } from "react";
import { useFocusEffect } from "@react-navigation/native";
import { useQueryClient } from "@tanstack/react-query";
import { StyleSheet, View } from "react-native";
import { Button, SegmentedButtons, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { ProductReportScreen } from "@/modules/product-report/product-report-screen";
import { RevenueReportScreen } from "@/modules/reports/RevenueReportScreen";
import { formatStatisticsFreshnessTime, useStatisticsFreshnessQuery } from "@/modules/reports/statistics-freshness";
import {
  REPORT_REFETCH_OPTIONS,
  createReportRefreshController,
  getReportRefreshQueryOptions,
  type ReportTab,
} from "@/modules/reports/report-refresh";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

export function ReportsHubScreen() {
  const { t } = useAppTranslation("common");
  const [tab, setTab] = useState<ReportTab>("revenue");
  const [isRefreshing, setIsRefreshing] = useState(false);
  const queryClient = useQueryClient();
  const freshnessQuery = useStatisticsFreshnessQuery();
  const refetchFreshness = freshnessQuery.refetch;
  const mountedRef = useRef(true);
  const refreshDependenciesRef = useRef({ queryClient, refetchFreshness });
  refreshDependenciesRef.current = { queryClient, refetchFreshness };
  const refreshControllerRef = useRef<ReturnType<typeof createReportRefreshController> | null>(null);
  if (!refreshControllerRef.current) {
    refreshControllerRef.current = createReportRefreshController(
      (activeTab) => {
        const dependencies = refreshDependenciesRef.current;
        return dependencies.queryClient.refetchQueries(
          getReportRefreshQueryOptions(activeTab),
          REPORT_REFETCH_OPTIONS,
        );
      },
      () => refreshDependenciesRef.current.refetchFreshness(),
      (refreshing) => {
        if (mountedRef.current) {
          setIsRefreshing(refreshing);
        }
      },
    );
  }
  const refreshController = refreshControllerRef.current;
  useEffect(() => {
    mountedRef.current = true;
    refreshController.resume();
    return () => {
      mountedRef.current = false;
      refreshController.dispose();
    };
  }, [refreshController]);
  useFocusEffect(
    useCallback(() => {
      // Tab 路由会常驻，重新进入报告页时主动获取最新统计状态。
      void refetchFreshness();
    }, [refetchFreshness]),
  );
  const freshnessTime = formatStatisticsFreshnessTime(freshnessQuery.data?.lastSuccessfulAtUtc ?? null);
  const freshnessLabel = freshnessQuery.isError
    ? t("reports.freshness.unavailable")
    : !freshnessTime
      ? t("reports.freshness.noSuccess")
      : t("reports.freshness.lastUpdated", { time: freshnessTime });
  const statusLabel = freshnessQuery.data?.latestRunStatus === "Running"
    ? t("reports.freshness.running")
    : freshnessQuery.data?.latestRunStatus === "Failed"
      ? t("reports.freshness.failed")
      : null;
  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <View style={styles.titleRow}>
          <Text variant="headlineSmall" style={styles.title}>
            {t("reports.title")}
          </Text>
          <Button
            compact
            icon="refresh"
            mode="text"
            loading={isRefreshing}
            disabled={isRefreshing}
            onPress={() => void refreshController.refresh(tab)}
          >
            {t("actions.refresh")}
          </Button>
        </View>
        <Text variant="bodySmall" style={styles.freshness}>
          {freshnessLabel}{statusLabel ? ` · ${statusLabel}` : ""}
        </Text>
        <SegmentedButtons
          value={tab}
          onValueChange={(value) => setTab(value as ReportTab)}
          buttons={[
            { value: "revenue", label: t("reports.sections.revenue") },
            { value: "product", label: t("reports.sections.product") },
          ]}
        />
      </View>

      {tab === "revenue"
        ? <RevenueReportScreen embedded onRefreshReport={() => refreshController.refresh("revenue")} />
        : <ProductReportScreen embedded onRefreshReport={() => refreshController.refresh("product")} />}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  header: {
    gap: 12,
    padding: 16,
    paddingBottom: 8,
  },
  title: {
    color: "#111827",
    fontWeight: "700",
  },
  titleRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  freshness: {
    color: "#6B7280",
  },
});
