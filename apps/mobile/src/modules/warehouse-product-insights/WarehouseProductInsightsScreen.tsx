import { useCallback, useEffect, useRef, useState } from "react";
import {
  Keyboard,
  Pressable,
  ScrollView,
  StyleSheet,
  TextInput,
  View,
} from "react-native";
import { CameraView } from "expo-camera";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useIsFocused } from "@react-navigation/native";
import { ActivityIndicator, Button, Divider, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { WarehouseProductInsightsView } from "@/components/warehouse-product-insights/WarehouseProductInsightsView";
import { InsightBranchSheet } from "@/components/warehouse-product-insights/InsightBranchSheet";
import { InsightContainerSheet } from "@/components/warehouse-product-insights/InsightContainerSheet";
import { InsightRangeSheet } from "@/components/warehouse-product-insights/InsightRangeSheet";
import { InsightSheet } from "@/components/warehouse-product-insights/InsightSheet";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { lookupWarehouseProducts } from "@/modules/warehouse/api";
import type { WarehouseProduct } from "@/modules/warehouse/types";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { fetchWarehouseProductInsight } from "./api";
import { canViewWarehouseProductInsights } from "./logic";
import { createProductInsightRequestGate } from "@/modules/product-insights/request-gate";
import type {
  WarehouseInsightRange,
  WarehouseProductInsight,
} from "./types";

const first = (value: string | string[] | undefined) =>
  (Array.isArray(value) ? value[0] : value)?.trim() || "";

export function WarehouseProductInsightsScreen() {
  const { t } = useAppTranslation("warehouseProductInsights");
  const router = useRouter();
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const permissionScope = useAuthStore((state) =>
    JSON.stringify([state.user?.permissions, state.user?.roleNames]),
  );
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const hasPermission = useAuthStore((state) => state.access.hasPermission);
  const review = useAuthStore((state) => state.iosReviewOfflineGuardActive);
  const params = useLocalSearchParams<{ productCode?: string | string[] }>();
  const goBack = () =>
    router.canGoBack() ? router.back() : router.replace("/(shell)/workbench");

  if (review || sessionKind === "iosReview") {
    return <ScreenMessage message={t("messages.reviewUnavailable")} onBack={goBack} />;
  }
  if (!canViewWarehouseProductInsights(isAuthenticated, hasPermission, false)) {
    return <ScreenMessage message={t("messages.notAllowed")} onBack={goBack} />;
  }

  // 账号或权限变化时销毁页面私有状态与在途请求，不复用上一身份的仓库数据。
  const identity = [userGuid, sessionKind, isAuthenticated, permissionScope].join(":");
  return (
    <WarehouseProductInsightsContent
      key={`${identity}:${first(params.productCode)}`}
      initialProductCode={first(params.productCode)}
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
  const { t } = useAppTranslation("warehouseProductInsights");
  return (
    <SafeAreaView style={styles.message}>
      {loading ? <ActivityIndicator /> : null}
      <Text accessibilityLiveRegion="polite">{message}</Text>
      <Button onPress={onBack}>{t("actions.back")}</Button>
    </SafeAreaView>
  );
}

function WarehouseProductInsightsContent({
  initialProductCode,
  onBack,
}: {
  initialProductCode: string;
  onBack: () => void;
}) {
  const { t, language } = useAppTranslation("warehouseProductInsights");
  const focused = useIsFocused();
  const [query, setQuery] = useState("");
  const [data, setData] = useState<WarehouseProductInsight | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [emptyState, setEmptyState] = useState<"initial" | "not-found">("initial");
  const [candidates, setCandidates] = useState<WarehouseProduct[]>([]);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [cameraGeneration, setCameraGeneration] = useState(0);
  const [queryFocused, setQueryFocused] = useState(false);
  const [rangeVisible, setRangeVisible] = useState(false);
  const [branchVisible, setBranchVisible] = useState(false);
  const [branchSort, setBranchSort] = useState<"sales" | "shipped" | "ordered">("sales");
  const [containerVisible, setContainerVisible] = useState(false);
  const gate = useRef(createProductInsightRequestGate()).current;
  const productCode = useRef(initialProductCode);
  const appliedRange = useRef<WarehouseInsightRange | null>(null);
  const scanGeneration = useRef(0);
  const cameraOpen = useRef(false);
  cameraOpen.current = cameraVisible;

  const formatError = useCallback(
    (value: unknown, fallbackKey: string) =>
      resolveLocalizedErrorMessage(value, {
        t,
        language,
        fallbackKey,
        allowRawMessageInChinese: false,
      }),
    [language, t],
  );

  const loadProduct = useCallback(
    async (code: string, range: WarehouseInsightRange | null) => {
      const lease = gate.begin();
      productCode.current = code;
      appliedRange.current = range;
      setData(null);
      setError(null);
      setLoading(true);
      setCandidates([]);
      setBranchVisible(false);
      setContainerVisible(false);
      try {
        const result = await fetchWarehouseProductInsight(code, range, lease.signal);
        if (!lease.isCurrent()) return;
        setData(result);
        appliedRange.current = {
          startDate: result.range.startDate,
          endDate: result.range.endDate,
        };
        setQuery(result.product.itemNumber || result.product.barcode || code);
      } catch (cause) {
        if (lease.isCurrent()) setError(formatError(cause, "messages.loadFailed"));
      } finally {
        if (lease.isCurrent()) setLoading(false);
      }
    },
    [formatError, gate],
  );

  const search = useCallback(
    async (value: string) => {
      const keyword = value.trim();
      if (!keyword) return;
      const lease = gate.begin();
      productCode.current = "";
      Keyboard.dismiss();
      setQueryFocused(false);
      setQuery(keyword);
      setData(null);
      setCandidates([]);
      setError(null);
      setLoading(true);
      setEmptyState("initial");
      try {
        const items = await lookupWarehouseProducts(keyword);
        if (!lease.isCurrent()) return;
        const unique = [
          ...new Map(items.map((item) => [item.productCode, item])).values(),
        ];
        if (unique.length === 1) {
          await loadProduct(unique[0].productCode, appliedRange.current);
          return;
        }
        if (!unique.length) setEmptyState("not-found");
        else setCandidates(unique);
      } catch (cause) {
        if (lease.isCurrent()) setError(formatError(cause, "messages.lookupFailed"));
      } finally {
        if (lease.isCurrent()) setLoading(false);
      }
    },
    [formatError, gate, loadProduct],
  );

  const initialLoad = useRef(loadProduct);
  const initialCode = useRef(initialProductCode);
  const invalidateCamera = useCallback(() => {
    scanGeneration.current++;
    cameraOpen.current = false;
  }, []);
  useEffect(() => {
    if (initialCode.current) void initialLoad.current(initialCode.current, null);
    return () => {
      gate.cancel();
      invalidateCamera();
    };
    // 初始商品只在挂载时加载，后续查询与重试由事件驱动。
  }, [gate, invalidateCamera]);

  const camera = useCameraScan({
    disabled: !focused || !cameraVisible || loading,
    resetKey: `${cameraGeneration}:${cameraVisible}`,
    singleScanUntilReset: true,
    onBarcode: async (barcode) => {
      if (!cameraOpen.current) return;
      cameraOpen.current = false;
      setCameraVisible(false);
      await search(barcode);
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
      !rangeVisible &&
      !branchVisible &&
      !containerVisible &&
      !candidates.length &&
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

  const rangeLabel = data
    ? t("range.sheetLabel", {
        startDate: data.range.startDate,
        endDate: data.range.endDate,
        days: data.range.dayCount,
      })
    : "";
  const productLabel = data
    ? `${data.product.productName} · ${data.product.itemNumber ?? data.product.productCode}`
    : "";

  return (
    <View style={styles.root}>
      <WarehouseProductInsightsView
        query={query}
        onQueryChange={(value) => {
          gate.cancel();
          productCode.current = "";
          setQuery(value);
          setData(null);
          setLoading(false);
          setError(null);
          setEmptyState("initial");
        }}
        onQueryFocus={() => {
          setQueryFocused(true);
          hid.pauseHiddenInputFocus();
        }}
        onQueryBlur={() => {
          setQueryFocused(false);
          hid.resumeHiddenInputFocus();
        }}
        onSearch={() => {
          void search(query);
        }}
        onScan={() => {
          void openCamera();
        }}
        data={data}
        loading={loading}
        error={error}
        emptyState={emptyState}
        onRetry={() => {
          if (productCode.current)
            void loadProduct(productCode.current, appliedRange.current);
          else void search(query);
        }}
        onBack={onBack}
        onOpenRange={() => setRangeVisible(true)}
        onOpenBranches={(sort) => {
          setBranchSort(sort);
          setBranchVisible(true);
        }}
        onOpenContainers={() => setContainerVisible(true)}
      />
      {hid.textInputProps ? (
        <TextInput
          {...hid.textInputProps}
          style={styles.hiddenInput}
          accessible={false}
          importantForAccessibility="no-hide-descendants"
        />
      ) : null}
      {data ? (
        <InsightRangeSheet
          visible={rangeVisible}
          range={{ startDate: data.range.startDate, endDate: data.range.endDate }}
          onClose={() => setRangeVisible(false)}
          onApply={(range) => {
            setRangeVisible(false);
            if (productCode.current) void loadProduct(productCode.current, range);
          }}
        />
      ) : null}
      {data ? (
        <InsightBranchSheet
          visible={branchVisible}
          onClose={() => setBranchVisible(false)}
          subtitle={productLabel}
          rangeLabel={rangeLabel}
          branches={data.branches}
          totals={data.totals}
          scope={data.scope}
          initialSort={branchSort}
          onOpenRange={() => {
            setBranchVisible(false);
            setRangeVisible(true);
          }}
        />
      ) : null}
      {data ? (
        <InsightContainerSheet
          visible={containerVisible}
          onClose={() => setContainerVisible(false)}
          subtitle={productLabel}
          rangeLabel={t("containerSheet.range", {
            startDate: data.inboundRange.startDate,
            endDate: data.inboundRange.endDate,
          })}
          containers={data.containers}
          inboundQuantity={data.totals.inboundQuantity}
          containerCount={data.totals.containerCount}
        />
      ) : null}
      <InsightSheet
        visible={candidates.length > 0}
        title={t("candidates.title")}
        subtitle={t("candidates.subtitle", { keyword: query })}
        closeLabel={t("actions.close")}
        onClose={() => setCandidates([])}
        heightRatio={0.72}
      >
        <ScrollView>
          {candidates.map((item) => (
            <Pressable
              key={item.productCode}
              accessibilityRole="button"
              onPress={() => {
                setCandidates([]);
                void loadProduct(item.productCode, appliedRange.current);
              }}
            >
              <View style={styles.candidate}>
                <Text numberOfLines={1} style={styles.candidateName}>
                  {item.productName}
                </Text>
                <Text numberOfLines={1} style={styles.candidateMeta}>
                  {item.itemNumber ?? item.productCode}
                  {item.barcode ? ` · ${item.barcode}` : ""}
                </Text>
              </View>
              <Divider />
            </Pressable>
          ))}
        </ScrollView>
      </InsightSheet>
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
  candidate: {
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: HB_SPACING.sm,
    gap: 2,
  },
  candidateName: { fontSize: 14, color: HB_COLORS.textPrimary, fontWeight: "600" },
  candidateMeta: { fontSize: 12, color: HB_COLORS.textSecondary },
});
