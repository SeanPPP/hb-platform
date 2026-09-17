import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Keyboard, StyleSheet } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, Button, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { PosOperationLogFilterSheet } from "@/components/pos-operation-logs/PosOperationLogFilterSheet";
import { PosOperationLogsView } from "@/components/pos-operation-logs/PosOperationLogsView";
import { createProductInsightRequestGate } from "@/modules/product-insights/request-gate";
import { useStores } from "@/modules/shop/use-stores";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { fetchPosOperationLogSummary, fetchPosOperationLogs } from "./api";
import {
  applyDeepLinkParams,
  applyQuickFilter,
  buildPosOperationLogQueryParams,
  canViewPosOperationLogs,
  createDefaultPosOperationLogFilters,
} from "./logic";
import type {
  PosOperationLogFilters,
  PosOperationLogItem,
  PosOperationLogSummary,
  PosOperationQuickFilter,
} from "./types";

const first = (value: string | string[] | undefined) =>
  (Array.isArray(value) ? value[0] : value)?.trim() || "";

export function PosOperationLogsScreen() {
  const { t } = useAppTranslation("posOperationLogs");
  const router = useRouter();
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const permissionScope = useAuthStore((state) =>
    JSON.stringify([state.user?.permissions, state.user?.roleNames]),
  );
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const hasPermission = useAuthStore((state) => state.access.hasPermission);
  const review = useAuthStore((state) => state.iosReviewOfflineGuardActive);
  const params = useLocalSearchParams<{
    orderGuid?: string | string[];
    cashier?: string | string[];
    storeCode?: string | string[];
    preset?: string | string[];
  }>();
  const goBack = () =>
    router.canGoBack() ? router.back() : router.replace("/(shell)/workbench");

  if (review || sessionKind === "iosReview") {
    return <ScreenMessage message={t("messages.reviewUnavailable")} onBack={goBack} />;
  }
  if (!canViewPosOperationLogs(isAuthenticated, hasPermission)) {
    return <ScreenMessage message={t("messages.notAllowed")} onBack={goBack} />;
  }

  const deepLink = {
    orderGuid: first(params.orderGuid),
    cashier: first(params.cashier),
    storeCode: first(params.storeCode),
    preset: first(params.preset),
  };
  // 账号、权限或深链参数变化时销毁页面状态，避免把上一身份的日志留在屏幕上。
  const identity = [userGuid, sessionKind, isAuthenticated, permissionScope].join(":");
  return (
    <PosOperationLogsContent
      key={`${identity}:${Object.values(deepLink).join("|")}`}
      deepLink={deepLink}
      onBack={goBack}
    />
  );
}

function ScreenMessage({
  message,
  loading = false,
  onBack,
}: {
  message: string;
  loading?: boolean;
  onBack: () => void;
}) {
  const { t } = useAppTranslation("posOperationLogs");
  return (
    <SafeAreaView style={styles.message}>
      {loading ? <ActivityIndicator /> : null}
      <Text accessibilityLiveRegion="polite">{message}</Text>
      <Button onPress={onBack}>{t("actions.back")}</Button>
    </SafeAreaView>
  );
}

function PosOperationLogsContent({
  deepLink,
  onBack,
}: {
  deepLink: { orderGuid: string; cashier: string; storeCode: string; preset: string };
  onBack: () => void;
}) {
  const { t, language } = useAppTranslation("posOperationLogs");
  const router = useRouter();
  const { stores } = useStores();
  const isAdmin = useAuthStore((state) => state.access.isAdmin);
  const [filters, setFilters] = useState<PosOperationLogFilters>(() =>
    applyDeepLinkParams(createDefaultPosOperationLogFilters(), deepLink),
  );
  const [searchDraft, setSearchDraft] = useState(filters.keyword);
  const [items, setItems] = useState<PosOperationLogItem[]>([]);
  const [total, setTotal] = useState(0);
  const [pageNumber, setPageNumber] = useState(1);
  const [summary, setSummary] = useState<PosOperationLogSummary | null>(null);
  const [summaryError, setSummaryError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filterVisible, setFilterVisible] = useState(false);
  const listGate = useRef(createProductInsightRequestGate()).current;
  const summaryGate = useRef(createProductInsightRequestGate()).current;

  const formatError = useCallback(
    (value: unknown) =>
      resolveLocalizedErrorMessage(value, {
        t,
        language,
        fallbackKey: "messages.loadFailed",
        allowRawMessageInChinese: false,
      }),
    [language, t],
  );

  const load = useCallback(
    async (nextFilters: PosOperationLogFilters, mode: "initial" | "refresh") => {
      const queryParams = buildPosOperationLogQueryParams(nextFilters);
      if (!queryParams) return;
      const lease = listGate.begin();
      const summaryLease = summaryGate.begin();
      if (mode === "refresh") setRefreshing(true);
      else setLoading(true);
      setError(null);
      setPageNumber(1);
      // 汇总与列表并行；汇总失败只影响计数展示，不阻塞列表。
      void fetchPosOperationLogSummary(queryParams, summaryLease.signal)
        .then((result) => {
          if (!summaryLease.isCurrent()) return;
          setSummary(result);
          setSummaryError(false);
        })
        .catch(() => {
          if (!summaryLease.isCurrent()) return;
          setSummary(null);
          setSummaryError(true);
        });
      try {
        const page = await fetchPosOperationLogs(queryParams, 1, lease.signal);
        if (!lease.isCurrent()) return;
        setItems(page.items);
        setTotal(page.total);
      } catch (cause) {
        if (!lease.isCurrent()) return;
        setError(formatError(cause));
        setItems([]);
        setTotal(0);
      } finally {
        if (lease.isCurrent()) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [formatError, listGate, summaryGate],
  );

  const loadMore = useCallback(async () => {
    if (loadingMore || loading || items.length >= total) return;
    const queryParams = buildPosOperationLogQueryParams(filters);
    if (!queryParams) return;
    const lease = listGate.begin();
    const nextPage = pageNumber + 1;
    setLoadingMore(true);
    try {
      const page = await fetchPosOperationLogs(queryParams, nextPage, lease.signal);
      if (!lease.isCurrent()) return;
      // 后端按时间倒序分页，翻页期间若有新事件写入会造成重复，按 eventId 去重。
      setItems((current) => {
        const seen = new Set(current.map((item) => item.eventId));
        return [...current, ...page.items.filter((item) => !seen.has(item.eventId))];
      });
      setTotal(page.total);
      setPageNumber(nextPage);
    } catch (cause) {
      if (lease.isCurrent()) setError(formatError(cause));
    } finally {
      if (lease.isCurrent()) setLoadingMore(false);
    }
  }, [filters, formatError, items.length, listGate, loading, loadingMore, pageNumber, total]);

  const initialLoad = useRef(load);
  const initialFilters = useRef(filters);
  useEffect(() => {
    void initialLoad.current(initialFilters.current, "initial");
    return () => {
      listGate.cancel();
      summaryGate.cancel();
    };
  }, [listGate, summaryGate]);

  const applyFilters = useCallback(
    (next: PosOperationLogFilters) => {
      setFilters(next);
      setSearchDraft(next.keyword);
      setFilterVisible(false);
      void load(next, "initial");
    },
    [load],
  );

  const scopeLabel = useMemo(() => {
    if (filters.storeCode) {
      const store = stores.find((item) => item.storeCode === filters.storeCode);
      return t("subtitle.store", { store: store?.storeName ?? filters.storeCode });
    }
    return isAdmin ? t("subtitle.allStores") : t("subtitle.scoped");
  }, [filters.storeCode, isAdmin, stores, t]);

  return (
    <>
      <PosOperationLogsView
        filters={filters}
        stores={stores}
        scopeLabel={scopeLabel}
        items={items}
        total={total}
        summary={summary}
        summaryError={summaryError}
        loading={loading}
        refreshing={refreshing}
        loadingMore={loadingMore}
        hasMore={items.length < total}
        error={error}
        searchDraft={searchDraft}
        onSearchDraftChange={(value) => {
          setSearchDraft(value);
          // 清空搜索框时立即回到未过滤状态，不用再点一次搜索。
          if (!value && filters.keyword) applyFilters({ ...filters, keyword: "" });
        }}
        onSearchSubmit={() => {
          Keyboard.dismiss();
          applyFilters({ ...filters, keyword: searchDraft.trim() });
        }}
        onQuickFilter={(quick: PosOperationQuickFilter) => applyFilters(applyQuickFilter(filters, quick))}
        onOpenFilters={() => setFilterVisible(true)}
        onClearOrderTrace={() => applyFilters({ ...filters, orderGuid: "", preset: "today" })}
        onRefresh={() => void load(filters, "refresh")}
        onLoadMore={() => void loadMore()}
        onRetry={() => void load(filters, "initial")}
        onOpenItem={(item) =>
          router.push({
            pathname: "/(shell)/pos-operation-logs/[eventId]",
            params: { eventId: item.eventId },
          })
        }
        onBack={onBack}
      />
      <PosOperationLogFilterSheet
        visible={filterVisible}
        filters={filters}
        stores={stores}
        onClose={() => setFilterVisible(false)}
        onApply={applyFilters}
        onReset={() => applyFilters(createDefaultPosOperationLogFilters())}
      />
    </>
  );
}

const styles = StyleSheet.create({
  message: {
    flex: 1,
    gap: HB_SPACING.md,
    padding: HB_SPACING.lg,
    justifyContent: "center",
    backgroundColor: HB_COLORS.background,
  },
});
