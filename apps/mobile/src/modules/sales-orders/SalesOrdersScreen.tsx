import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Keyboard, StyleSheet, TextInput, View } from "react-native";
import { CameraView } from "expo-camera";
import { useRouter } from "expo-router";
import { useIsFocused } from "@react-navigation/native";
import { Button, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  SalesOrderDetailSheet,
  SalesOrderFilterSheet,
  SalesOrdersView,
} from "@/components/sales-orders";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { createProductInsightRequestGate } from "@/modules/product-insights/request-gate";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { fetchSalesOrderBranches, fetchSalesOrderDetail, fetchSalesOrders } from "./api";
import { downloadTaxInvoice, openTaxInvoice, type InvoiceAction } from "./invoice";
import {
  buildDefaultSalesOrderFilters,
  buildSalesOrderQueryBody,
  canViewSalesOrders,
  countActiveSalesOrderFilters,
  formatLocalDate,
  hasMoreSalesOrderPages,
  mergeSalesOrderPages,
  toggleSortDirection,
} from "./logic";
import type {
  SalesOrderBranchCatalog,
  SalesOrderDetail,
  SalesOrderFilters,
  SalesOrderListItem,
  SalesOrderScope,
} from "./types";

export function SalesOrdersScreen() {
  const { t } = useAppTranslation("salesOrders");
  const router = useRouter();
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const permissionScope = useAuthStore((state) =>
    JSON.stringify([state.user?.permissions, state.user?.roleNames]),
  );
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const hasPermission = useAuthStore((state) => state.access.hasPermission);
  const review = useAuthStore((state) => state.iosReviewOfflineGuardActive);
  const goBack = () =>
    router.canGoBack() ? router.back() : router.replace("/(shell)/workbench");

  if (review || sessionKind === "iosReview") {
    return <ScreenMessage message={t("messages.reviewUnavailable")} onBack={goBack} />;
  }
  if (!canViewSalesOrders(isAuthenticated, hasPermission, false)) {
    return <ScreenMessage message={t("messages.notAllowed")} onBack={goBack} />;
  }

  // 账号或权限变化时销毁页面私有状态与在途请求，不复用上一身份的订单数据。
  const identity = [userGuid, sessionKind, isAuthenticated, permissionScope].join(":");
  return <SalesOrdersContent key={identity} onBack={goBack} />;
}

function ScreenMessage({ message, onBack }: { message: string; onBack: () => void }) {
  const { t } = useAppTranslation("salesOrders");
  return (
    <SafeAreaView style={styles.message}>
      <Text accessibilityLiveRegion="polite">{message}</Text>
      <Button onPress={onBack}>{t("actions.back")}</Button>
    </SafeAreaView>
  );
}

function SalesOrdersContent({ onBack }: { onBack: () => void }) {
  const { t, language } = useAppTranslation("salesOrders");
  const focused = useIsFocused();
  const today = useMemo(() => formatLocalDate(new Date()), []);
  const [filters, setFilters] = useState<SalesOrderFilters>(() =>
    buildDefaultSalesOrderFilters(today),
  );
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<SalesOrderListItem[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const [scope, setScope] = useState<SalesOrderScope | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searched, setSearched] = useState(false);
  const [catalog, setCatalog] = useState<SalesOrderBranchCatalog | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [filterVisible, setFilterVisible] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [selected, setSelected] = useState<SalesOrderListItem | null>(null);
  const [detail, setDetail] = useState<SalesOrderDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [invoiceBusy, setInvoiceBusy] = useState<InvoiceAction | null>(null);
  const [invoiceError, setInvoiceError] = useState<string | null>(null);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [cameraGeneration, setCameraGeneration] = useState(0);
  const [queryFocused, setQueryFocused] = useState(false);

  const listGate = useRef(createProductInsightRequestGate()).current;
  const detailGate = useRef(createProductInsightRequestGate()).current;
  const catalogGate = useRef(createProductInsightRequestGate()).current;
  // 已生效的查询条件；翻页与重试都以它为准，不读取输入框里尚未提交的关键词。
  const applied = useRef({ filters, keyword: "", pageNumber: 1 });
  const scanGeneration = useRef(0);
  const cameraOpen = useRef(false);
  cameraOpen.current = cameraVisible;

  const formatError = useCallback(
    (value: unknown, fallbackKey: string) => {
      const code = (value as { code?: unknown } | null)?.code;
      if (code === "DATE_RANGE_TOO_LONG") return t("messages.rangeTooLong");
      if (code === "SALES_ORDER_SHARE_UNAVAILABLE") return t("messages.shareUnavailable");
      return resolveLocalizedErrorMessage(value, {
        t,
        language,
        fallbackKey,
        allowRawMessageInChinese: false,
      });
    },
    [language, t],
  );

  const load = useCallback(
    async (nextFilters: SalesOrderFilters, keyword: string, pageNumber: number) => {
      const append = pageNumber > 1;
      const lease = listGate.begin();
      applied.current = { ...applied.current, filters: nextFilters, keyword, pageNumber };
      setError(null);
      if (append) {
        setLoadingMore(true);
      } else {
        setLoading(true);
        setItems([]);
        setTotal(null);
        setHasMore(false);
        setSearched(true);
      }
      try {
        const page = await fetchSalesOrders(
          buildSalesOrderQueryBody(nextFilters, keyword, pageNumber),
          lease.signal,
        );
        if (!lease.isCurrent()) return;
        setHasMore(hasMoreSalesOrderPages(page));
        setScope(page.scope);
        setTotal(page.total);
        setItems((current) => (append ? mergeSalesOrderPages(current, page.items) : page.items));
      } catch (cause) {
        if (lease.isCurrent()) setError(formatError(cause, "messages.loadFailed"));
      } finally {
        if (lease.isCurrent()) {
          setLoading(false);
          setLoadingMore(false);
        }
      }
    },
    [formatError, listGate],
  );

  const loadCatalog = useCallback(async () => {
    const lease = catalogGate.begin();
    setCatalogLoading(true);
    setCatalogError(null);
    try {
      const result = await fetchSalesOrderBranches(lease.signal);
      if (!lease.isCurrent()) return;
      setCatalog(result);
      setScope((current) => current ?? result.scope);
    } catch (cause) {
      if (lease.isCurrent()) setCatalogError(formatError(cause, "messages.branchesFailed"));
    } finally {
      if (lease.isCurrent()) setCatalogLoading(false);
    }
  }, [catalogGate, formatError]);

  const initialLoad = useRef({ load, loadCatalog, filters });
  const invalidateCamera = useCallback(() => {
    scanGeneration.current++;
    cameraOpen.current = false;
  }, []);
  useEffect(() => {
    // 默认区间是今天，进入页面即查询；分店清单并行加载供筛选面板使用。
    void initialLoad.current.load(initialLoad.current.filters, "", 1);
    void initialLoad.current.loadCatalog();
    return () => {
      listGate.cancel();
      detailGate.cancel();
      catalogGate.cancel();
      invalidateCamera();
    };
  }, [catalogGate, detailGate, invalidateCamera, listGate]);

  const search = useCallback(
    (value: string) => {
      Keyboard.dismiss();
      setQueryFocused(false);
      setQuery(value);
      void load(applied.current.filters, value, 1);
    },
    [load],
  );

  const applyFilters = useCallback(
    (next: SalesOrderFilters) => {
      setFilters(next);
      setFilterVisible(false);
      void load(next, applied.current.keyword, 1);
    },
    [load],
  );

  const openOrder = useCallback(
    async (item: SalesOrderListItem) => {
      const lease = detailGate.begin();
      setSelected(item);
      setDetail(null);
      setDetailError(null);
      setInvoiceError(null);
      setInvoiceBusy(null);
      setDetailLoading(true);
      try {
        const result = await fetchSalesOrderDetail(item.orderGuid, lease.signal);
        if (!lease.isCurrent()) return;
        setDetail(result);
      } catch (cause) {
        if (lease.isCurrent()) setDetailError(formatError(cause, "messages.detailFailed"));
      } finally {
        if (lease.isCurrent()) setDetailLoading(false);
      }
    },
    [detailGate, formatError],
  );

  const handleInvoice = useCallback(
    async (action: InvoiceAction) => {
      if (!selected) return;
      const orderGuid = selected.orderGuid;
      setInvoiceBusy(action);
      setInvoiceError(null);
      try {
        const file = await downloadTaxInvoice(orderGuid);
        await openTaxInvoice(file.fileUri, action);
      } catch (cause) {
        setInvoiceError(formatError(cause, "messages.invoiceFailed"));
      } finally {
        setInvoiceBusy(null);
      }
    },
    [formatError, selected],
  );

  const camera = useCameraScan({
    disabled: !focused || !cameraVisible || loading,
    resetKey: `${cameraGeneration}:${cameraVisible}`,
    singleScanUntilReset: true,
    onBarcode: async (barcode) => {
      if (!cameraOpen.current) return;
      cameraOpen.current = false;
      setCameraVisible(false);
      search(barcode);
    },
  });
  const openCamera = async () => {
    const generation = ++scanGeneration.current;
    Keyboard.dismiss();
    setQueryFocused(false);
    try {
      const permission = camera.permission?.granted
        ? camera.permission
        : await camera.requestPermission();
      if (generation !== scanGeneration.current) return;
      if (!permission.granted) {
        setError(t("messages.cameraPermission"));
        return;
      }
      setCameraGeneration((value) => value + 1);
      setCameraVisible(true);
    } catch (cause) {
      if (generation === scanGeneration.current)
        setError(formatError(cause, "messages.cameraUnavailable"));
    }
  };
  const hid = useHidBarcodeScanner({
    enabled:
      focused &&
      !queryFocused &&
      !cameraVisible &&
      !filterVisible &&
      !selected &&
      !loading,
    onScan: search,
  });
  useEffect(() => {
    if (!focused) {
      scanGeneration.current++;
      cameraOpen.current = false;
      setCameraVisible(false);
    }
  }, [focused]);

  const branchCount = catalog?.branches.length ?? null;

  return (
    <View style={styles.root}>
      <SalesOrdersView
        query={query}
        onQueryChange={setQuery}
        onQueryFocus={() => {
          setQueryFocused(true);
          hid.pauseHiddenInputFocus();
        }}
        onQueryBlur={() => {
          setQueryFocused(false);
          hid.resumeHiddenInputFocus();
        }}
        onSearch={() => search(query)}
        onScan={() => {
          void openCamera();
        }}
        filters={filters}
        scope={scope}
        branchCount={branchCount}
        activeFilterCount={countActiveSalesOrderFilters(filters, today)}
        items={items}
        total={total}
        loading={loading}
        loadingMore={loadingMore}
        hasMore={hasMore}
        error={error}
        searched={searched}
        onRetry={() => {
          const { filters: current, keyword, pageNumber } = applied.current;
          void load(current, keyword, items.length > 0 ? pageNumber : 1);
        }}
        onLoadMore={() => {
          const { filters: current, keyword, pageNumber } = applied.current;
          void load(current, keyword, pageNumber + 1);
        }}
        onRefresh={() => {
          const { filters: current, keyword } = applied.current;
          void load(current, keyword, 1);
        }}
        onBack={onBack}
        onOpenFilters={() => {
          setFilterVisible(true);
          if (!catalog && !catalogLoading) void loadCatalog();
        }}
        onToggleSort={() => {
          const next = { ...applied.current.filters, sortDirection: toggleSortDirection(applied.current.filters.sortDirection) };
          setFilters(next);
          void load(next, applied.current.keyword, 1);
        }}
        onOpenOrder={(item) => {
          void openOrder(item);
        }}
      />
      {hid.textInputProps ? (
        <TextInput
          {...hid.textInputProps}
          style={styles.hiddenInput}
          accessible={false}
          importantForAccessibility="no-hide-descendants"
        />
      ) : null}
      <SalesOrderFilterSheet
        visible={filterVisible}
        filters={filters}
        today={today}
        branches={catalog?.branches ?? []}
        branchesLoading={catalogLoading}
        branchesError={catalogError}
        scope={scope}
        onRetryBranches={() => {
          void loadCatalog();
        }}
        onClose={() => setFilterVisible(false)}
        onApply={applyFilters}
      />
      <SalesOrderDetailSheet
        visible={selected != null}
        summary={selected}
        detail={detail}
        loading={detailLoading}
        error={detailError}
        invoiceBusy={invoiceBusy}
        invoiceError={invoiceError}
        onRetry={() => {
          if (selected) void openOrder(selected);
        }}
        onInvoice={(action) => {
          void handleInvoice(action);
        }}
        onClose={() => {
          detailGate.cancel();
          setSelected(null);
          setDetail(null);
          setDetailError(null);
          setInvoiceError(null);
          setInvoiceBusy(null);
        }}
      />
      <BusinessSheet
        visible={cameraVisible && focused}
        title={t("camera.title")}
        onDismiss={() => {
          scanGeneration.current++;
          cameraOpen.current = false;
          setCameraVisible(false);
        }}
        footer={
          <Button
            onPress={() => {
              cameraOpen.current = false;
              setCameraVisible(false);
            }}
          >
            {t("actions.close")}
          </Button>
        }
      >
        {cameraVisible && focused && camera.permission?.granted ? (
          <CameraView style={styles.camera} {...camera.cameraProps} />
        ) : null}
      </BusinessSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: HB_COLORS.background },
  message: {
    flex: 1,
    gap: HB_SPACING.md,
    padding: HB_SPACING.lg,
    justifyContent: "center",
    backgroundColor: HB_COLORS.background,
  },
  camera: { height: 300 },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
    left: -100,
  },
});
