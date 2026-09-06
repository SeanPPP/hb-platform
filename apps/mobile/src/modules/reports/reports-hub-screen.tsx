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
  getReportStoreScopeRefreshQueryOptions,
  type ReportTab,
} from "@/modules/reports/report-refresh";
import {
  discardReportNavigationStart,
  getPendingReportNavigationToken,
  markReportNavigationStart,
} from "@/modules/reports/report-load-performance";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

export function ReportsHubScreen() {
  const { t } = useAppTranslation("common");
  const [tab, setTab] = useState<ReportTab>("revenue");
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [focusNavigationActionId, setFocusNavigationActionId] = useState<number | null>(null);
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
          getReportStoreScopeRefreshQueryOptions(activeTab),
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
      const navigationActionId = getPendingReportNavigationToken(tab);
      setFocusNavigationActionId(navigationActionId);
      if (navigationActionId === null) return;

      // 先重验收银启用门店范围；白名单的 dataUpdatedAt 会切换业务 query key 并自动取数。
      // 这样不会在新范围返回前，用旧白名单并发刷新业务数据。
      void queryClient
        .refetchQueries(getReportStoreScopeRefreshQueryOptions(tab), REPORT_REFETCH_OPTIONS)
        .catch(() => undefined);

      return () => {
        discardReportNavigationStart(tab, navigationActionId);
        setFocusNavigationActionId((current) => current === navigationActionId ? null : current);
      };
    }, [queryClient, refetchFreshness, tab]),
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
  const handleTabChange = useCallback((value: string) => {
    if (value !== "revenue" && value !== "product") return;
    if (value === tab) return;
    // 先记录用户点击，再提交页签状态；新屏幕的首个 query 会一次性消费对应标记。
    markReportNavigationStart(value);
    setTab(value);
  }, [tab]);
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
          onValueChange={handleTabChange}
          buttons={[
            { value: "revenue", label: t("reports.sections.revenue") },
            { value: "product", label: t("reports.sections.product") },
          ]}
        />
      </View>

      {tab === "revenue"
        ? <RevenueReportScreen
            embedded
            freshnessLabel={freshnessLabel}
            reportNavigationActionId={focusNavigationActionId}
            onRefreshReport={() => refreshController.refresh("revenue")}
          />
        : <ProductReportScreen
            embedded
            reportNavigationActionId={focusNavigationActionId}
            onRefreshReport={() => refreshController.refresh("product")}
          />}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  header: {
    gap: 8,
    paddingHorizontal: 12,
    paddingTop: 10,
    paddingBottom: 6,
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
