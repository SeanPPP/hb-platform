import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { FlatList, RefreshControl, StyleSheet, View } from "react-native";
import { useInfiniteQuery, useQueryClient } from "@tanstack/react-query";
import { useFocusEffect, useRouter } from "expo-router";
import { ActivityIndicator, Badge, Button, Card, Chip, Text, useTheme } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { getEmployeeProfileReviewAccess } from "./access";
import { getEmployeeProfileReviewRequestsApi } from "./api";
import { clearEmployeeProfileReviewListCache } from "./review-cache";
import { getReviewFailureKind } from "./review-logic";
import { mergeUniqueEmployeeProfileReviewPages } from "./pagination";
import type { EmployeeProfileReviewSummary } from "./types";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

export const employeeProfileReviewListQueryKey = [
  "employeeProfileReview",
  "requests",
  "Pending",
] as const;

function formatDateTime(value: string, language: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return new Intl.DateTimeFormat(language, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

export function EmployeeProfileReviewListScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const theme = useTheme();
  const { t, language } = useAppTranslation("employeeProfileReview");
  const currentUser = useAuthStore((state) => state.user);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const navigationItems = useAppNavigationStore((state) => state.items);
  const navigationReady = useAppNavigationStore((state) => state.isReady);
  const mountedRef = useRef(true);
  const [listPrivacyHidden, setListPrivacyHidden] = useState(false);
  const reviewAccess = useMemo(
    () => getEmployeeProfileReviewAccess({
      roleNames: currentUser?.roleNames,
      permissions: currentUser?.permissions,
      menuRouteNames: navigationItems.map((item) => item.routeName),
      sessionKind,
    }),
    [currentUser?.permissions, currentUser?.roleNames, navigationItems, sessionKind]
  );
  const reviewQuery = useInfiniteQuery({
    queryKey: employeeProfileReviewListQueryKey,
    enabled: navigationReady && reviewAccess.allowed,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => getEmployeeProfileReviewRequestsApi({
      page: pageParam,
      pageSize: 50,
      status: "Pending",
    }),
    getNextPageParam: (lastPage) =>
      lastPage.page * lastPage.pageSize < lastPage.total
        ? lastPage.page + 1
        : undefined,
  });

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const clearListAndExit = useCallback(async () => {
    if (mountedRef.current) {
      setListPrivacyHidden(true);
    }
    await clearEmployeeProfileReviewListCache(queryClient);
    if (mountedRef.current) {
      router.replace("/(tabs)/settings");
    }
  }, [queryClient, router]);

  const listScopeInvalidated = getReviewFailureKind(reviewQuery.error) === "forbidden";

  useEffect(() => {
    if (navigationReady && (!reviewAccess.allowed || listScopeInvalidated)) {
      // 直接深链也必须在发起详情请求前关闭，最终授权仍由后端执行。
      void clearListAndExit();
    }
  }, [clearListAndExit, listScopeInvalidated, navigationReady, reviewAccess.allowed]);

  useFocusEffect(useCallback(() => {
    if (!navigationReady || !reviewAccess.allowed || listScopeInvalidated) {
      return;
    }
    setListPrivacyHidden(false);
    // offset 分页重新聚焦时丢弃旧后续页，从新首页重新建立稳定分页窗口。
    void queryClient.resetQueries({
      queryKey: employeeProfileReviewListQueryKey,
      exact: true,
    });
  }, [listScopeInvalidated, navigationReady, queryClient, reviewAccess.allowed]));

  const openDetail = (item: EmployeeProfileReviewSummary) => {
    router.push({
      pathname: "/employee-profile-review/[requestId]",
      params: { requestId: String(item.requestId) },
    });
  };

  if (listPrivacyHidden || listScopeInvalidated) {
    return <SafeAreaView style={styles.centered} edges={["top"]} />;
  }

  if (!navigationReady || (reviewQuery.isLoading && !reviewQuery.data)) {
    return (
      <SafeAreaView style={styles.centered} edges={["top"]}>
        <ActivityIndicator size="large" accessibilityLabel={t("list.loading")} />
      </SafeAreaView>
    );
  }

  if (!reviewAccess.allowed) {
    return (
      <SafeAreaView style={styles.centered} edges={["top"]}>
        <Text selectable>{t("messages.permissionChanged")}</Text>
      </SafeAreaView>
    );
  }

  if (reviewQuery.isError && !reviewQuery.data) {
    return (
      <SafeAreaView style={styles.centered} edges={["top"]}>
        <EmptyState
          title={t("messages.listLoadFailed")}
          description={t("messages.retryHint")}
          primaryAction={{
            label: t("actions.retry"),
            icon: "refresh",
            onPress: () => void reviewQuery.refetch(),
          }}
        />
      </SafeAreaView>
    );
  }

  const items = mergeUniqueEmployeeProfileReviewPages(reviewQuery.data?.pages ?? []);
  const total = reviewQuery.data?.pages[0]?.total ?? 0;
  return (
    <SafeAreaView style={[styles.container, { backgroundColor: theme.colors.background }]} edges={["top"]}>
      <View style={styles.header}>
        <View style={styles.titleRow}>
          <Text variant="headlineSmall" selectable>{t("list.title")}</Text>
          <Badge accessibilityLabel={t("list.pendingCount", { count: total })}>
            {total}
          </Badge>
        </View>
        <Text variant="bodyMedium" style={{ color: theme.colors.onSurfaceVariant }} selectable>
          {t("list.subtitle")}
        </Text>
      </View>
      <FlatList
        data={items}
        keyExtractor={(item) => String(item.requestId)}
        contentInsetAdjustmentBehavior="automatic"
        contentContainerStyle={items.length ? styles.list : styles.emptyList}
        refreshControl={(
          <RefreshControl
            refreshing={reviewQuery.isRefetching && !reviewQuery.isFetchingNextPage}
            onRefresh={() => void reviewQuery.refetch()}
          />
        )}
        onEndReachedThreshold={0.35}
        onEndReached={() => {
          if (reviewQuery.hasNextPage && !reviewQuery.isFetchingNextPage) {
            void reviewQuery.fetchNextPage();
          }
        }}
        ListFooterComponent={reviewQuery.isFetchingNextPage ? (
          <ActivityIndicator style={styles.pageLoader} accessibilityLabel={t("list.loadingMore")} />
        ) : reviewQuery.isFetchNextPageError ? (
          <View style={styles.pageLoader}>
            <Button
              icon="refresh"
              mode="text"
              contentStyle={styles.touchTarget}
              onPress={() => void reviewQuery.fetchNextPage()}
            >
              {t("list.loadMoreFailed")}
            </Button>
          </View>
        ) : null}
        ListEmptyComponent={(
          <EmptyState
            title={t("list.empty")}
            description={t("list.emptyDescription")}
            primaryAction={{
              label: t("actions.refresh"),
              icon: "refresh",
              onPress: () => void reviewQuery.refetch(),
            }}
          />
        )}
        renderItem={({ item }) => (
          <Card
            mode="outlined"
            onPress={() => openDetail(item)}
            accessibilityLabel={t("list.openRequest", { name: item.username || item.userGuid })}
          >
            <Card.Content style={styles.cardContent}>
              <View style={styles.cardTitleRow}>
                <View style={styles.employeeText}>
                  <Text variant="titleMedium" selectable numberOfLines={1}>
                    {item.username || item.userGuid}
                  </Text>
                  <Text variant="bodySmall" style={{ color: theme.colors.onSurfaceVariant }} selectable>
                    {item.storeNames.length ? item.storeNames.join(" · ") : t("list.storeUnavailable")}
                  </Text>
                </View>
                <Chip compact icon="clock-outline">{t(`statuses.${item.status}`)}</Chip>
              </View>
              <View style={styles.chips}>
                {item.changedFields.map((field) => (
                  <Chip compact key={field}>{t(`fields.${field}`)}</Chip>
                ))}
              </View>
              <View style={styles.footerRow}>
                <Text variant="bodySmall" style={{ color: theme.colors.onSurfaceVariant }} selectable>
                  {formatDateTime(item.submittedAt, language)}
                </Text>
                <Button
                  compact
                  mode="text"
                  contentStyle={styles.touchTarget}
                  onPress={() => openDetail(item)}
                >
                  {t("actions.review")}
                </Button>
              </View>
            </Card.Content>
          </Card>
        )}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  centered: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24 },
  header: { paddingHorizontal: 16, paddingTop: 12, paddingBottom: 8, gap: 4 },
  titleRow: { flexDirection: "row", alignItems: "center", gap: 10 },
  list: { padding: 16, paddingBottom: 32, gap: 12 },
  emptyList: { flexGrow: 1, justifyContent: "center", padding: 16 },
  cardContent: { paddingVertical: 4, gap: 12 },
  cardTitleRow: { flexDirection: "row", alignItems: "flex-start", gap: 12 },
  employeeText: { flex: 1, gap: 2 },
  chips: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  footerRow: { minHeight: 48, flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 8 },
  touchTarget: { minHeight: 48 },
  pageLoader: { paddingVertical: 16 },
});
