import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { ReactNode, RefObject } from "react";
import {
  Animated,
  Image,
  InteractionManager,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  useWindowDimensions,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActivityIndicator,
  Button,
  Modal,
  Portal,
  SegmentedButtons,
  Text,
  TextInput,
} from "react-native-paper";
import {
  buildProductReportDateQuery,
  fetchProductBranchBreakdown,
  fetchProductReportProductRows,
  fetchProductReportStoreOptions,
  fetchProductReportTotalRevenue,
  fetchSupplierBranchBreakdown,
  fetchSupplierReportRows,
  getProductReportCacheVersionState,
  getProductReportCacheVersionSyncDecision,
  type ProductBranchBreakdownRow,
  type ProductReportProductPage,
  type ProductReportProductRow,
  type ProductReportSnapshot,
  type ProductReportTotalRevenue,
  type SupplierBranchBreakdownRow,
  type SupplierReportKind,
  type SupplierReportRow,
} from "@/modules/product-report/api";
import {
  getCustomProductReportRange,
  getDefaultProductReportRange,
  getProductReportQuickRange,
  isValidProductReportDateRange,
  type ProductReportQuickRangeKey,
} from "@/modules/product-report/date-ranges";
import { formatMoney } from "@/modules/reports/format";
import {
  getCashierEnabledStoreCodes,
  getCashierScopedBranchCodes,
} from "@/modules/reports/cashier-enabled-store-scope";
import { GROWTH_COLORS, formatGrowthRate, getGrowthTone } from "@/modules/reports/growth-rate";
import { REPORT_QUERY_OPTIONS } from "@/modules/reports/report-config";
import {
  ReportLoadPerformanceTimer,
  ReportLoadVisibilityGate,
  discardReportNavigationStart,
  hasUsableSuccessfulReportCache,
  recordReportLoadPerformance,
  type ReportLoadCacheState,
  type ReportLoadPerformanceMeasurement,
} from "@/modules/reports/report-load-performance";
import { PRODUCT_PAGE_SIZE, SUPPLIER_PAGE_SIZE, getPageRows } from "@/modules/product-report/pagination";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type Drilldown =
  | { type: "supplier"; kind: SupplierReportKind; supplier: SupplierReportRow }
  | { type: "product"; product: ProductReportProductRow };

interface ProductReportScreenProps {
  embedded?: boolean;
  onRefreshFreshness?: () => Promise<unknown>;
  onRefreshReport?: () => Promise<unknown>;
  reportNavigationActionId?: number | null;
}

interface ProductPageSummary {
  currentSales: number;
  compareSales: number;
  currentGrossProfit: number | null;
  compareGrossProfit: number | null;
  currentGrossMarginRate: number | null;
  compareGrossMarginRate: number | null;
}

const MAIN_REPORT_CACHE_VERSION_REFETCH_LIMIT = 2;

function formatCount(value: number) {
  return Math.round(value).toLocaleString("en-AU");
}

function formatRowNumber(value: number) {
  return String(value).padStart(2, "0");
}

function formatWholeMoney(value: number) {
  // 高密度排行用整元值避免大金额被箭头或下一列截断，详情仍使用两位小数。
  const amount = Number.isFinite(value) ? value : 0;
  return `$${amount.toLocaleString("en-AU", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  })}`;
}

function formatNullableMoney(value: number | null) {
  return value === null ? "—" : formatMoney(value);
}

function formatGrossMarginRate(value: number | null, costPendingLabel: string) {
  // 后端统一返回 0-1 比率（例如 0.4 = 40%），展示层再换算为百分比。
  return value === null ? costPendingLabel : `${(value * 100).toFixed(1)}%`;
}

function formatShare(value: number, denominator: number) {
  if (!Number.isFinite(value) || !Number.isFinite(denominator) || denominator <= 0) {
    return "--";
  }
  return `${((value / denominator) * 100).toFixed(1)}%`;
}

function getSupplierTitle(kind: SupplierReportKind, row: SupplierReportRow) {
  if (kind === "china") {
    return `${row.supplierCode} ${row.supplierName.slice(0, 3)}`.trim();
  }
  return row.supplierName || row.supplierCode;
}

function TableCell({
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

function FrozenHorizontalTable({
  children,
}: {
  children: (scrollX: Animated.Value) => ReactNode;
}) {
  const scrollX = useRef(new Animated.Value(0)).current;
  return (
    <Animated.ScrollView
      horizontal
      bounces={false}
      scrollEventThrottle={16}
      onScroll={Animated.event(
        [{ nativeEvent: { contentOffset: { x: scrollX } } }],
        { useNativeDriver: true },
      )}
      showsHorizontalScrollIndicator>
      {children(scrollX)}
    </Animated.ScrollView>
  );
}

function FrozenLeadingColumns({
  scrollX,
  children,
  style,
  tone = "body",
}: {
  scrollX: Animated.Value;
  children: ReactNode;
  style: StyleProp<ViewStyle>;
  tone?: "body" | "header" | "selected";
}) {
  return (
    <Animated.View
      style={[
        styles.frozenLeadingColumns,
        tone === "header"
          ? styles.frozenHeaderColumns
          : tone === "selected"
            ? styles.frozenSelectedColumns
            : styles.frozenBodyColumns,
        style,
        { transform: [{ translateX: scrollX }] },
      ]}
    >
      {children}
    </Animated.View>
  );
}

function ProductPageSummaryCard({
  summary,
  caption,
}: {
  summary: ProductPageSummary;
  caption: string;
}) {
  const { t } = useAppTranslation("common");
  const costPendingLabel = t("productReport.states.costPending");
  const metrics = [
    {
      key: "sales",
      label: t("productReport.metrics.revenue"),
      current: formatMoney(summary.currentSales),
      compare: formatMoney(summary.compareSales),
    },
    {
      key: "grossProfit",
      label: t("productReport.metrics.grossProfit"),
      current: formatNullableMoney(summary.currentGrossProfit),
      compare: formatNullableMoney(summary.compareGrossProfit),
    },
    {
      key: "grossMargin",
      label: t("productReport.metrics.grossMarginRate"),
      current: formatGrossMarginRate(summary.currentGrossMarginRate, costPendingLabel),
      compare: formatGrossMarginRate(summary.compareGrossMarginRate, costPendingLabel),
    },
  ];

  return (
    <View style={styles.productSummaryCard}>
      <View style={styles.productSummaryHeader}>
        <Text variant="titleMedium" style={styles.sectionTitle}>
          {t("productReport.sections.pageSummary")}
        </Text>
        <Text variant="bodySmall" style={styles.muted} numberOfLines={1}>
          {caption}
        </Text>
      </View>
      <View style={styles.productSummaryGrid}>
        <View style={styles.productSummaryLabelColumn}>
          <TableCell style={styles.headerText}> </TableCell>
          <TableCell style={styles.strongText}>{t("reports.metrics.current")}</TableCell>
          <TableCell style={styles.muted}>{t("productReport.metrics.compare")}</TableCell>
        </View>
        {metrics.map((metric) => (
          <View key={metric.key} style={styles.productSummaryMetric}>
            <TableCell numeric style={styles.headerText}>{metric.label}</TableCell>
            <TableCell numeric style={styles.strongText}>{metric.current}</TableCell>
            <TableCell numeric style={styles.muted}>{metric.compare}</TableCell>
          </View>
        ))}
      </View>
    </View>
  );
}

export function ProductReportScreen({
  embedded = false,
  onRefreshFreshness,
  onRefreshReport,
  reportNavigationActionId = null,
}: ProductReportScreenProps) {
  const { t } = useAppTranslation("common");
  const { height, width } = useWindowDimensions();
  const queryClient = useQueryClient();
  const productLoadTimer = useRef(new ReportLoadPerformanceTimer()).current;
  const productLoadActiveRef = useRef(false);
  const productLoadSessionKeyRef = useRef<object | null>(null);
  const firstProductReportDataVisibleRef = useRef(false);
  // 首数据指标只认真实明细行，汇总卡可见不能代替供应商或商品行可见。
  const firstSupplierReportRowRef = useRef<View>(null);
  const firstProductReportRowRef = useRef<View>(null);
  const drilldownLoadGate = useRef(new ReportLoadVisibilityGate()).current;
  const drilldownLoadQueryKeyRef = useRef<readonly unknown[] | null>(null);
  const drilldownLoadKindRef = useRef<"supplier" | "product" | null>(null);
  const drilldownRequestGenerationRef = useRef(0);
  const drilldownPhysicalRowVisibleRef = useRef(false);
  const drilldownPhysicalPresentationReadyRef = useRef(false);
  const mainReportVersionSyncRef = useRef<{ sessionKey: object | null; attemptCount: number }>({
    sessionKey: null,
    attemptCount: 0,
  });
  const [kind, setKind] = useState<SupplierReportKind>("australia");
  const [range, setRange] = useState(() => getDefaultProductReportRange());
  const [draftStartDate, setDraftStartDate] = useState(range.startDate);
  const [draftEndDate, setDraftEndDate] = useState(range.endDate);
  const [selectedStoreCode, setSelectedStoreCode] = useState<string | undefined>();
  const [isStoreModalVisible, setStoreModalVisible] = useState(false);
  const [selectedSupplierCode, setSelectedSupplierCode] = useState<string | null>(null);
  const [supplierPage, setSupplierPage] = useState(1);
  const [productPage, setProductPage] = useState(1);
  const [productSearchDraft, setProductSearchDraft] = useState("");
  const [productSearch, setProductSearch] = useState("");
  const [drilldown, setDrilldown] = useState<Drilldown | null>(null);
  const [mainReportVersionSyncExhausted, setMainReportVersionSyncExhausted] = useState(false);

  const dateRangeValid = isValidProductReportDateRange(draftStartDate, draftEndDate);
  const activeRange = useMemo(
    () => (dateRangeValid ? getCustomProductReportRange(draftStartDate, draftEndDate) ?? range : range),
    [dateRangeValid, draftEndDate, draftStartDate, range]
  );

  const storeOptionsQuery = useQuery({
    queryKey: ["product-report", "stores"],
    queryFn: ({ signal }) => fetchProductReportStoreOptions({ signal }),
    ...REPORT_QUERY_OPTIONS,
  });
  const cashierEnabledStoreCodes = useMemo(
    () => getCashierEnabledStoreCodes(storeOptionsQuery.data ?? []),
    [storeOptionsQuery.data],
  );
  const cashierStoreScopeVersion = storeOptionsQuery.dataUpdatedAt;
  const branchCodes = useMemo(
    () => getCashierScopedBranchCodes(cashierEnabledStoreCodes, selectedStoreCode),
    [cashierEnabledStoreCodes, selectedStoreCode],
  );

  const queryParams = useMemo(
    // 收银启用门店白名单是业务查询的安全边界；未就绪、为空或旧选择失效时一律不回退到全店请求。
    () => (
      dateRangeValid
      && activeRange
      && storeOptionsQuery.isSuccess
      && !storeOptionsQuery.isFetching
      && branchCodes.length > 0
        ? buildProductReportDateQuery(activeRange, branchCodes)
        : null
    ),
    [activeRange, branchCodes, dateRangeValid, storeOptionsQuery.isFetching, storeOptionsQuery.isSuccess],
  );
  useLayoutEffect(() => {
    if (reportNavigationActionId === null) return;
    // 白名单仍在加载时保留导航计时；否则会把真实首屏请求之前的点击起点提前丢弃。
    if (dateRangeValid && storeOptionsQuery.isPending) return;
    if (
      !dateRangeValid
      || storeOptionsQuery.isError
      || (storeOptionsQuery.isSuccess && cashierEnabledStoreCodes.length === 0)
    ) {
      discardReportNavigationStart("product", reportNavigationActionId);
    }
  }, [cashierEnabledStoreCodes.length, dateRangeValid, reportNavigationActionId, storeOptionsQuery.isError, storeOptionsQuery.isPending, storeOptionsQuery.isSuccess]);

  useEffect(() => {
    if (
      storeOptionsQuery.isFetching
      || !storeOptionsQuery.isSuccess
      || !selectedStoreCode
      || branchCodes.length > 0
    ) return;
    // 门店在刷新期间被停用时，先阻断旧范围请求，再清空与旧选择相关的局部状态。
    setSelectedStoreCode(undefined);
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
    setDrilldown(null);
  }, [branchCodes.length, selectedStoreCode, storeOptionsQuery.isFetching, storeOptionsQuery.isSuccess]);

  useEffect(() => {
    if (storeOptionsQuery.isFetching) {
      // 重验期间隐藏选择器，但保留下钻意图；成功后仅在新白名单仍有效时恢复。
      setStoreModalVisible(false);
      return;
    }
    if (
      storeOptionsQuery.isSuccess
      && cashierEnabledStoreCodes.length > 0
    ) return;
    // 白名单不可用时关闭下钻，避免展示上一次范围的缓存明细。
    setDrilldown(null);
    setStoreModalVisible(false);
  }, [cashierEnabledStoreCodes.length, storeOptionsQuery.isFetching, storeOptionsQuery.isSuccess]);

  // 供应商营业额弹窗要看所有分店，不继承顶部单店筛选。
  const supplierBranchQueryParams = useMemo(
    () => (
      dateRangeValid
      && activeRange
      && storeOptionsQuery.isSuccess
      && !storeOptionsQuery.isFetching
      && cashierEnabledStoreCodes.length > 0
        ? buildProductReportDateQuery(activeRange, cashierEnabledStoreCodes)
        : null
    ),
    [activeRange, cashierEnabledStoreCodes, dateRangeValid, storeOptionsQuery.isFetching, storeOptionsQuery.isSuccess],
  );
  // 商品分店弹窗也要完整分店数据，不继承顶部单店筛选。
  const productBranchQueryParams = useMemo(
    () => (
      dateRangeValid
      && activeRange
      && storeOptionsQuery.isSuccess
      && !storeOptionsQuery.isFetching
      && cashierEnabledStoreCodes.length > 0
        ? buildProductReportDateQuery(activeRange, cashierEnabledStoreCodes)
        : null
    ),
    [activeRange, cashierEnabledStoreCodes, dateRangeValid, storeOptionsQuery.isFetching, storeOptionsQuery.isSuccess],
  );
  const supplierFilterCodes = useMemo(
    () => (selectedSupplierCode ? [selectedSupplierCode] : undefined),
    [selectedSupplierCode]
  );
  const totalRevenueQueryKey = useMemo(
    () => ["product-report", "total-revenue", cashierStoreScopeVersion, queryParams] as const,
    [cashierStoreScopeVersion, queryParams],
  );
  const supplierQueryKey = useMemo(
    () => ["product-report", "suppliers", kind, cashierStoreScopeVersion, queryParams] as const,
    [cashierStoreScopeVersion, kind, queryParams],
  );
  const productQueryKey = useMemo(
    () => [
      "product-report",
      "products",
      kind,
      cashierStoreScopeVersion,
      queryParams,
      supplierFilterCodes,
      productSearch,
      productPage,
    ] as const,
    [cashierStoreScopeVersion, kind, productPage, productSearch, queryParams, supplierFilterCodes],
  );
  const productLoadSessionKey = useMemo(
    () => ({ totalRevenueQueryKey, supplierQueryKey, productQueryKey }),
    [productQueryKey, supplierQueryKey, totalRevenueQueryKey],
  );

  const startProductLoad = useCallback((cacheState: ReportLoadCacheState) => {
    productLoadTimer.start(cacheState, "product");
    productLoadActiveRef.current = true;
  }, [productLoadTimer]);
  const ensureProductLoadStarted = useCallback(() => {
    if (
      !queryParams
      || (
        productLoadSessionKeyRef.current === productLoadSessionKey
        && productLoadActiveRef.current
      )
    ) return;
    productLoadSessionKeyRef.current = productLoadSessionKey;
    firstProductReportDataVisibleRef.current = false;
    const cachedTotalRevenue = queryClient.getQueryData<ProductReportTotalRevenue>(totalRevenueQueryKey);
    const cachedSupplierRows = queryClient.getQueryData<ProductReportSnapshot<SupplierReportRow[]>>(supplierQueryKey);
    const cachedProductPage = queryClient.getQueryData<ProductReportSnapshot<ProductReportProductPage>>(productQueryKey);
    const hasCompleteCache =
      hasUsableSuccessfulReportCache(
        queryClient.getQueryState(totalRevenueQueryKey)?.status,
        cachedTotalRevenue,
        (cachedTotalRevenue) => cachedTotalRevenue.isComplete,
      )
      && hasUsableSuccessfulReportCache(
        queryClient.getQueryState(supplierQueryKey)?.status,
        cachedSupplierRows,
        (cachedSupplierRows) => cachedSupplierRows.isComplete,
      )
      && hasUsableSuccessfulReportCache(
        queryClient.getQueryState(productQueryKey)?.status,
        cachedProductPage,
        (cachedProductPage) => cachedProductPage.isComplete,
      )
      && getProductReportCacheVersionState([
        cachedTotalRevenue,
        cachedSupplierRows,
        cachedProductPage,
      ]) === "aligned"
      && (cachedSupplierRows.data.length > 0 || cachedProductPage.data.rows.length > 0);
    startProductLoad(hasCompleteCache ? "warm" : "cold");
  }, [productLoadSessionKey, productQueryKey, queryClient, queryParams, startProductLoad, supplierQueryKey, totalRevenueQueryKey]);
  const failProductLoad = useCallback(() => {
    if (productLoadSessionKeyRef.current !== productLoadSessionKey) return;
    productLoadTimer.fail();
    productLoadActiveRef.current = false;
  }, [productLoadSessionKey, productLoadTimer]);

  const totalRevenueQuery = useQuery({
    queryKey: totalRevenueQueryKey,
    queryFn: async ({ signal }) => {
      ensureProductLoadStarted();
      try {
        return await fetchProductReportTotalRevenue(queryParams!, { signal });
      } catch (error) {
        failProductLoad();
        throw error;
      }
    },
    enabled: Boolean(queryParams),
    ...REPORT_QUERY_OPTIONS,
  });

  const supplierQuery = useQuery({
    queryKey: supplierQueryKey,
    queryFn: async ({ signal }) => {
      ensureProductLoadStarted();
      try {
        return await fetchSupplierReportRows(kind, queryParams!, 1000, { signal });
      } catch (error) {
        failProductLoad();
        throw error;
      }
    },
    enabled: Boolean(queryParams),
    ...REPORT_QUERY_OPTIONS,
  });

  const supplierRows = supplierQuery.data?.data ?? [];
  const supplierPageCount = Math.max(1, Math.ceil(supplierRows.length / SUPPLIER_PAGE_SIZE));
  const supplierPageRows = getPageRows(supplierRows, supplierPage, SUPPLIER_PAGE_SIZE);
  const supplierSubtotal = supplierRows.reduce((sum, row) => sum + row.revenue, 0);
  const supplierCompareSubtotal = supplierRows.reduce((sum, row) => sum + row.compareRevenue, 0);
  const totalRevenue = totalRevenueQuery.data ?? { revenue: 0, compareRevenue: 0 };
  const totalRevenueStatisticsPending =
    totalRevenueQuery.isLoading || (
      totalRevenueQuery.data !== undefined
      && !totalRevenueQuery.data.isComplete
      && (!totalRevenueQuery.data.pollingExhausted || totalRevenueQuery.isFetching)
    );
  const totalRevenueStatisticsIncomplete =
    totalRevenueQuery.data !== undefined
    && !totalRevenueQuery.data.isComplete
    && totalRevenueQuery.data.pollingExhausted
    && !totalRevenueQuery.isFetching;

  const productQuery = useQuery({
    queryKey: productQueryKey,
    queryFn: async ({ signal }) => {
      ensureProductLoadStarted();
      try {
        return await fetchProductReportProductRows(
          kind,
          queryParams!,
          supplierFilterCodes,
          productPage,
          PRODUCT_PAGE_SIZE,
          productSearch,
          { signal },
        );
      } catch (error) {
        failProductLoad();
        throw error;
      }
    },
    enabled: Boolean(queryParams),
    placeholderData: keepPreviousData,
    ...REPORT_QUERY_OPTIONS,
  });
  const productSectionLoading = productQuery.isLoading || productQuery.isPlaceholderData;
  const mainReportCacheVersionState = getProductReportCacheVersionState([
    totalRevenueQuery.data,
    supplierQuery.data,
    productQuery.data,
  ]);
  const mainReportQueriesFetching =
    totalRevenueQuery.isFetching || supplierQuery.isFetching || productQuery.isFetching;
  const refetchMainReport = useCallback(() => Promise.all([
    queryClient.refetchQueries({ queryKey: totalRevenueQueryKey, exact: true, type: "active" }),
    queryClient.refetchQueries({ queryKey: supplierQueryKey, exact: true, type: "active" }),
    queryClient.refetchQueries({ queryKey: productQueryKey, exact: true, type: "active" }),
  ]), [productQueryKey, queryClient, supplierQueryKey, totalRevenueQueryKey]);
  const resetMainReportVersionSync = useCallback(() => {
    mainReportVersionSyncRef.current = { sessionKey: productLoadSessionKey, attemptCount: 0 };
    setMainReportVersionSyncExhausted(false);
  }, [productLoadSessionKey]);

  useEffect(() => {
    if (!queryParams) return;
    let syncState = mainReportVersionSyncRef.current;
    if (syncState.sessionKey !== productLoadSessionKey) {
      syncState = { sessionKey: productLoadSessionKey, attemptCount: 0 };
      mainReportVersionSyncRef.current = syncState;
      setMainReportVersionSyncExhausted(false);
    }

    const decision = getProductReportCacheVersionSyncDecision(
      mainReportCacheVersionState,
      syncState.attemptCount,
      mainReportQueriesFetching,
      MAIN_REPORT_CACHE_VERSION_REFETCH_LIMIT,
    );
    if (decision === "ready") {
      syncState.attemptCount = 0;
      setMainReportVersionSyncExhausted(false);
      return;
    }
    if (decision === "exhausted") {
      setMainReportVersionSyncExhausted(true);
      failProductLoad();
      return;
    }
    if (decision !== "refetch") return;

    // 三块并发结果不属于同一批次时整组重取；次数受限，期间继续隐藏所有业务行。
    syncState.attemptCount += 1;
    setMainReportVersionSyncExhausted(false);
    void refetchMainReport();
  }, [
    failProductLoad,
    mainReportCacheVersionState,
    mainReportQueriesFetching,
    productLoadSessionKey,
    queryParams,
    refetchMainReport,
  ]);
  const mainReportCacheVersionMismatch = mainReportCacheVersionState === "mismatch";
  const mainReportVersionSyncExhaustedForSession =
    mainReportVersionSyncRef.current.sessionKey === productLoadSessionKey
    && mainReportVersionSyncExhausted;
  const mainReportStatisticsPending =
    storeOptionsQuery.isFetching
    || totalRevenueStatisticsPending
    || supplierQuery.isLoading
    || productQuery.isLoading
    || (
      mainReportCacheVersionMismatch
      && (mainReportQueriesFetching || !mainReportVersionSyncExhaustedForSession)
    )
    || (
      supplierQuery.data !== undefined
      && !supplierQuery.data.isComplete
      && (!supplierQuery.data.pollingExhausted || supplierQuery.isFetching)
    )
    || (
      productQuery.data !== undefined
      && !productQuery.data.isComplete
      && (!productQuery.data.pollingExhausted || productQuery.isFetching)
    );
  const mainReportStatisticsIncomplete =
    totalRevenueStatisticsIncomplete
    || (
      mainReportCacheVersionMismatch
      && mainReportVersionSyncExhaustedForSession
      && !mainReportQueriesFetching
    )
    || (
      supplierQuery.data !== undefined
      && !supplierQuery.data.isComplete
      && supplierQuery.data.pollingExhausted
      && !supplierQuery.isFetching
    )
    || (
      productQuery.data !== undefined
      && !productQuery.data.isComplete
      && productQuery.data.pollingExhausted
      && !productQuery.isFetching
    );
  const mainReportRequestError =
    storeOptionsQuery.isError || totalRevenueQuery.isError || supplierQuery.isError || productQuery.isError;

  const completeProductLoad = useCallback(() => {
    if (!firstProductReportDataVisibleRef.current) return;
    const measurement = productLoadTimer.markFirstRowVisible();
    if (!measurement) return;
    productLoadActiveRef.current = false;
    recordReportLoadPerformance("product", measurement);
  }, [productLoadTimer]);

  const markProductDataVisible = useCallback(() => {
    if (!productLoadActiveRef.current) return;
    [firstSupplierReportRowRef, firstProductReportRowRef].forEach((rowRef) => {
      rowRef.current?.measureInWindow((x, y, measuredWidth, measuredHeight) => {
        if (!productLoadActiveRef.current) return;
        if (
          measuredWidth <= 0
          || measuredHeight <= 0
          || x >= width
          || x + measuredWidth <= 0
          || y >= height
          || y + measuredHeight <= 0
        ) return;
        firstProductReportDataVisibleRef.current = true;
        completeProductLoad();
      });
    });
  }, [completeProductLoad, height, width]);

  const scheduleProductDataVisibilityCheck = useCallback(() => {
    requestAnimationFrame(markProductDataVisible);
  }, [markProductDataVisible]);

  useLayoutEffect(() => {
    ensureProductLoadStarted();
    return () => {
      if (productLoadSessionKeyRef.current !== productLoadSessionKey) return;
      productLoadTimer.cancel();
      productLoadActiveRef.current = false;
      productLoadSessionKeyRef.current = null;
    };
  }, [ensureProductLoadStarted, productLoadSessionKey, productLoadTimer]);

  useLayoutEffect(() => {
    const hasIncompleteSnapshot =
      (totalRevenueQuery.data !== undefined && !totalRevenueQuery.data.isComplete)
      || (supplierQuery.data !== undefined && !supplierQuery.data.isComplete)
      || (productQuery.data !== undefined && !productQuery.data.isComplete);
    if (hasIncompleteSnapshot) {
      if (!mainReportStatisticsPending) failProductLoad();
      return;
    }
    const hasCompleteData =
      totalRevenueQuery.data?.isComplete === true &&
      supplierQuery.data?.isComplete === true &&
      productQuery.data?.isComplete === true &&
      mainReportCacheVersionState === "aligned" &&
      !totalRevenueQuery.isFetching &&
      !supplierQuery.isFetching &&
      !productQuery.isFetching;
    if (!hasCompleteData) return;
    const hasBusinessRows =
      (supplierQuery.data?.data.length ?? 0) > 0
      || (productQuery.data?.data.rows.length ?? 0) > 0;
    if (!hasBusinessRows) {
      // 完整空结果只有空态，没有“首条业务数据”，不得进入 first-data 达标率。
      productLoadTimer.cancel();
      productLoadActiveRef.current = false;
      return;
    }
    productLoadTimer.markDataNormalized();
    markProductDataVisible();
  }, [
    failProductLoad,
    mainReportStatisticsPending,
    mainReportCacheVersionState,
    markProductDataVisible,
    productLoadTimer,
    productQuery.data,
    productQuery.isFetching,
    supplierQuery.data,
    supplierQuery.isFetching,
    totalRevenueQuery.data,
    totalRevenueQuery.isFetching,
  ]);
  const productRows = useMemo(() => productQuery.data?.data.rows ?? [], [productQuery.data]);
  const productTotal = productQuery.data?.data.total ?? 0;
  const productPageSummary = useMemo<ProductPageSummary>(() => {
    const currentSales = productRows.reduce((sum, row) => sum + row.salesAmount, 0);
    const compareSales = productRows.reduce((sum, row) => sum + row.compareSalesAmount, 0);
    const hasCurrentGrossProfit = productRows.length > 0
      && productRows.every((row) => row.grossProfit !== null);
    const hasCompareGrossProfit = productRows.length > 0
      && productRows.every((row) => row.compareGrossProfit !== null);
    const currentGrossProfit = hasCurrentGrossProfit
      ? productRows.reduce((sum, row) => sum + (row.grossProfit ?? 0), 0)
      : null;
    const compareGrossProfit = hasCompareGrossProfit
      ? productRows.reduce((sum, row) => sum + (row.compareGrossProfit ?? 0), 0)
      : null;

    return {
      currentSales,
      compareSales,
      currentGrossProfit,
      compareGrossProfit,
      currentGrossMarginRate:
        currentSales > 0 && currentGrossProfit !== null ? currentGrossProfit / currentSales : null,
      compareGrossMarginRate:
        compareSales > 0 && compareGrossProfit !== null ? compareGrossProfit / compareSales : null,
    };
  }, [productRows]);
  const productPageCount = Math.max(1, Math.ceil(productTotal / PRODUCT_PAGE_SIZE));
  // 商品报告的两个数据区块各自接近一屏，分页和搜索栏也计入区块高度。
  const sectionScreenHeight = Math.max(560, Math.floor(height * 0.76));
  const supplierTableBodyHeight = Math.max(420, sectionScreenHeight - 112);
  const productTableBodyHeight = Math.max(380, sectionScreenHeight - 184);
  const growthNewLabel = t("productReport.metrics.newGrowth");
  const costPendingLabel = t("productReport.states.costPending");

  const renderGrowthCell = (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => {
    const tone = getGrowthTone(current, compare);
    return (
      <View style={[styles.growthColumn, columnStyle]}>
        <TableCell numeric style={[styles.strongText, { color: GROWTH_COLORS[tone] }]}>
          {formatGrowthRate(current, compare, growthNewLabel)}
        </TableCell>
      </View>
    );
  };

  const renderGrossProfitCell = (
    current: number | null,
    compare: number | null,
    columnStyle?: StyleProp<ViewStyle>
  ) => (
    <View style={[styles.grossProfitColumn, columnStyle]}>
      <TableCell numeric style={styles.strongText}>{formatNullableMoney(current)}</TableCell>
      <TableCell numeric style={styles.muted}>{formatNullableMoney(compare)}</TableCell>
    </View>
  );

  const renderGrossMarginCell = (
    current: number | null,
    compare: number | null,
    columnStyle?: StyleProp<ViewStyle>
  ) => (
    <View style={[styles.grossMarginColumn, columnStyle]}>
      <TableCell numeric style={styles.strongText}>{formatGrossMarginRate(current, costPendingLabel)}</TableCell>
      <TableCell numeric style={styles.muted}>{formatGrossMarginRate(compare, costPendingLabel)}</TableCell>
    </View>
  );

  const supplierBranchQueryKey = useMemo(
    () => [
      "product-report",
      "supplier-branches",
      cashierStoreScopeVersion,
      drilldown,
      supplierBranchQueryParams,
    ] as const,
    [cashierStoreScopeVersion, drilldown, supplierBranchQueryParams],
  );
  const productBranchQueryKey = useMemo(
    () => [
      "product-report",
      "product-branches",
      cashierStoreScopeVersion,
      drilldown,
      productBranchQueryParams,
    ] as const,
    [cashierStoreScopeVersion, drilldown, productBranchQueryParams],
  );
  const startDrilldownLoad = useCallback((
    queryKey: readonly unknown[],
    nextKind: "supplier" | "product",
    hasUsableCache: boolean,
  ) => {
    const requestGeneration = drilldownRequestGenerationRef.current + 1;
    drilldownRequestGenerationRef.current = requestGeneration;
    const isSameDrilldownQuery = drilldownLoadQueryKeyRef.current === queryKey;
    if (!isSameDrilldownQuery) {
      drilldownPhysicalRowVisibleRef.current = false;
      drilldownPhysicalPresentationReadyRef.current = false;
    }
    drilldownLoadQueryKeyRef.current = queryKey;
    drilldownLoadKindRef.current = nextKind;
    drilldownLoadGate.start(hasUsableCache ? "warm" : "cold", {
      restorePhysicalState: isSameDrilldownQuery
        ? {
            firstRowVisible: drilldownPhysicalRowVisibleRef.current,
            presentationReady: drilldownPhysicalPresentationReadyRef.current,
          }
        : undefined,
    });
    return requestGeneration;
  }, [drilldownLoadGate]);
  const failDrilldownLoad = useCallback((
    requestGeneration: number,
    queryKey: readonly unknown[],
  ) => {
    if (
      drilldownRequestGenerationRef.current !== requestGeneration
      || drilldownLoadQueryKeyRef.current !== queryKey
    ) return;
    drilldownLoadGate.fail();
  }, [drilldownLoadGate]);

  const supplierBranchQuery = useQuery({
    queryKey: supplierBranchQueryKey,
    queryFn: async ({ signal }) => {
      const cachedSupplierBranches = queryClient.getQueryData<
        ProductReportSnapshot<SupplierBranchBreakdownRow[]>
      >(supplierBranchQueryKey);
      const requestGeneration = startDrilldownLoad(
        supplierBranchQueryKey,
        "supplier",
        hasUsableSuccessfulReportCache(
          queryClient.getQueryState(supplierBranchQueryKey)?.status,
          cachedSupplierBranches,
          (cachedSupplierBranches) => cachedSupplierBranches.isComplete && cachedSupplierBranches.data.length > 0,
        ),
      );
      try {
        const result = await fetchSupplierBranchBreakdown(
          (drilldown as Extract<Drilldown, { type: "supplier" }>).kind,
          supplierBranchQueryParams!,
          (drilldown as Extract<Drilldown, { type: "supplier" }>).supplier.supplierCode,
          { signal },
        );
        if (
          signal.aborted
          || drilldownRequestGenerationRef.current !== requestGeneration
          || drilldownLoadQueryKeyRef.current !== supplierBranchQueryKey
        ) {
          const abortError = new Error("Stale supplier branch report request");
          abortError.name = "AbortError";
          throw abortError;
        }
        return result;
      } catch (error) {
        failDrilldownLoad(requestGeneration, supplierBranchQueryKey);
        throw error;
      }
    },
    enabled: Boolean(supplierBranchQueryParams && drilldown?.type === "supplier"),
    ...REPORT_QUERY_OPTIONS,
  });

  const productBranchQuery = useQuery({
    queryKey: productBranchQueryKey,
    queryFn: async ({ signal }) => {
      const cachedProductBranches = queryClient.getQueryData<
        ProductReportSnapshot<ProductBranchBreakdownRow[]>
      >(productBranchQueryKey);
      const requestGeneration = startDrilldownLoad(
        productBranchQueryKey,
        "product",
        hasUsableSuccessfulReportCache(
          queryClient.getQueryState(productBranchQueryKey)?.status,
          cachedProductBranches,
          (cachedProductBranches) => cachedProductBranches.isComplete && cachedProductBranches.data.length > 0,
        ),
      );
      try {
        const result = await fetchProductBranchBreakdown(
          productBranchQueryParams!,
          (drilldown as Extract<Drilldown, { type: "product" }>).product.productCode,
          { signal },
        );
        if (
          signal.aborted
          || drilldownRequestGenerationRef.current !== requestGeneration
          || drilldownLoadQueryKeyRef.current !== productBranchQueryKey
        ) {
          const abortError = new Error("Stale product branch report request");
          abortError.name = "AbortError";
          throw abortError;
        }
        return result;
      } catch (error) {
        failDrilldownLoad(requestGeneration, productBranchQueryKey);
        throw error;
      }
    },
    enabled: Boolean(productBranchQueryParams && drilldown?.type === "product"),
    ...REPORT_QUERY_OPTIONS,
  });
  const drilldownKind = drilldown?.type ?? null;
  const activeDrilldownQuery = drilldownKind === "supplier"
    ? supplierBranchQuery
    : drilldownKind === "product"
      ? productBranchQuery
      : null;
  const activeDrilldownQueryKey = drilldownKind === "supplier"
    ? supplierBranchQueryKey
    : drilldownKind === "product"
      ? productBranchQueryKey
      : null;
  // 弹窗状态按当前下钻类型取值，避免另一个禁用查询把内容渲染成空白。
  const isDrilldownLoading =
    activeDrilldownQuery?.isLoading
    || Boolean(
      activeDrilldownQuery?.data !== undefined
      && !activeDrilldownQuery.data.isComplete
      && (!activeDrilldownQuery.data.pollingExhausted || activeDrilldownQuery.isFetching),
    );
  const isDrilldownStatisticsIncomplete = Boolean(
    activeDrilldownQuery?.data !== undefined
    && !activeDrilldownQuery.data.isComplete
    && activeDrilldownQuery.data.pollingExhausted
    && !activeDrilldownQuery.isFetching,
  );
  const isDrilldownError =
    drilldownKind === "supplier"
      ? supplierBranchQuery.isError
      : drilldownKind === "product"
        ? productBranchQuery.isError
        : false;
  const recordDrilldownMeasurement = useCallback((measurement: ReportLoadPerformanceMeasurement | null) => {
    if (!measurement || !drilldownLoadKindRef.current) return;
    recordReportLoadPerformance(
      drilldownLoadKindRef.current === "supplier" ? "supplier-branches" : "product-branches",
      measurement,
    );
  }, []);
  const updateDrilldownFirstDataVisibility = useCallback((visible: boolean) => {
    // 该回调已在 InteractionManager 后测量视口，因此同时证明弹窗展示完成。
    drilldownPhysicalPresentationReadyRef.current = true;
    drilldownPhysicalRowVisibleRef.current = visible;
    recordDrilldownMeasurement(drilldownLoadGate.setPresentationReady(true));
    recordDrilldownMeasurement(drilldownLoadGate.setFirstRowVisible(visible));
  }, [drilldownLoadGate, recordDrilldownMeasurement]);

  useLayoutEffect(() => {
    if (!activeDrilldownQueryKey) {
      drilldownLoadGate.cancel();
      drilldownLoadQueryKeyRef.current = null;
      drilldownLoadKindRef.current = null;
      drilldownPhysicalRowVisibleRef.current = false;
      drilldownPhysicalPresentationReadyRef.current = false;
      return;
    }
    return () => {
      if (drilldownLoadQueryKeyRef.current !== activeDrilldownQueryKey) return;
      drilldownRequestGenerationRef.current += 1;
      drilldownLoadGate.cancel();
      drilldownLoadQueryKeyRef.current = null;
      drilldownLoadKindRef.current = null;
      drilldownPhysicalRowVisibleRef.current = false;
      drilldownPhysicalPresentationReadyRef.current = false;
    };
  }, [activeDrilldownQueryKey, drilldownLoadGate]);

  useLayoutEffect(() => {
    if (
      !activeDrilldownQueryKey
      || activeDrilldownQuery?.data === undefined
      || activeDrilldownQuery.isFetching
      || drilldownLoadQueryKeyRef.current !== activeDrilldownQueryKey
    ) return;
    if (isDrilldownError) {
      // 错误态会隐藏旧缓存行；同 key 重试不能继承已经不可见的行状态。
      drilldownPhysicalRowVisibleRef.current = false;
      drilldownLoadGate.setFirstRowVisible(false);
      return;
    }
    if (!activeDrilldownQuery.data.isComplete) {
      if (activeDrilldownQuery.data.pollingExhausted) {
        drilldownPhysicalRowVisibleRef.current = false;
        drilldownLoadGate.fail();
      }
      return;
    }
    if (activeDrilldownQuery.data.data.length === 0) {
      // 空结果没有首条业务行，不能把空态误记成 2 秒达标。
      drilldownPhysicalRowVisibleRef.current = false;
      drilldownLoadGate.cancel();
      return;
    }
    recordDrilldownMeasurement(drilldownLoadGate.markDataNormalized());
  }, [
    activeDrilldownQuery?.data,
    activeDrilldownQuery?.dataUpdatedAt,
    activeDrilldownQuery?.isFetching,
    activeDrilldownQueryKey,
    drilldownLoadGate,
    isDrilldownError,
    recordDrilldownMeasurement,
  ]);
  const retryDrilldown = () => {
    if (
      (drilldownKind === "supplier" && supplierBranchQueryParams)
      || (drilldownKind === "product" && productBranchQueryParams)
    ) {
      // 先重验白名单，避免用上一次成功范围重试刚被停用的分店。
      void storeOptionsQuery.refetch();
    }
  };
  const drilldownEmptyLabel =
    drilldownKind === "supplier"
      ? t("productReport.states.emptySupplierBranches")
      : t("productReport.states.emptyProducts");

  const setQuickRange = (key: ProductReportQuickRangeKey) => {
    const next = getProductReportQuickRange(key);
    setRange(next);
    setDraftStartDate(next.startDate);
    setDraftEndDate(next.endDate);
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
  };

  const applyKind = (nextKind: string) => {
    setKind(nextKind as SupplierReportKind);
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
  };

  const applyStore = (storeCode?: string) => {
    setSelectedStoreCode(storeCode);
    setStoreModalVisible(false);
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
  };

  const updateDraftStartDate = (value: string) => {
    setDraftStartDate(value);
    const nextRange = getCustomProductReportRange(value, draftEndDate);
    if (nextRange) {
      setRange(nextRange);
    }
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
  };

  const updateDraftEndDate = (value: string) => {
    setDraftEndDate(value);
    const nextRange = getCustomProductReportRange(draftStartDate, value);
    if (nextRange) {
      setRange(nextRange);
    }
    setSelectedSupplierCode(null);
    setSupplierPage(1);
    setProductPage(1);
  };

  const applyProductSearch = () => {
    setProductSearch(productSearchDraft.trim());
    setProductPage(1);
  };

  const clearProductSearch = () => {
    setProductSearchDraft("");
    setProductSearch("");
    setProductPage(1);
  };

  const refresh = () => {
    resetMainReportVersionSync();
    if (onRefreshReport) {
      // 嵌入报告中心时统一走控制器，避免下拉与页头刷新并发。
      void onRefreshReport();
      return;
    }
    void onRefreshFreshness?.();
    void storeOptionsQuery.refetch();
  };

  const isRefreshing =
    storeOptionsQuery.isRefetching ||
    totalRevenueQuery.isRefetching ||
    supplierQuery.isRefetching ||
    productQuery.isRefetching;
  const selectedStoreLabel =
    storeOptionsQuery.data?.find((item) => item.value === selectedStoreCode)?.label ??
    t("productReport.filters.allStores");

  const renderSupplierRow = ({
    item,
    rowNumber,
    scrollX,
  }: {
    item: SupplierReportRow;
    rowNumber: number;
    scrollX: Animated.Value;
  }) => {
    const isSelected = item.supplierCode === selectedSupplierCode;
    const currentSupplierShare =
      kind === "china"
        ? formatShare(item.revenue, supplierSubtotal)
        : formatShare(item.revenue, totalRevenue.revenue);
    const compareSupplierShare =
      kind === "china"
        ? formatShare(item.compareRevenue, supplierCompareSubtotal)
        : formatShare(item.compareRevenue, totalRevenue.compareRevenue);
    return (
      <View style={[styles.tableRow, styles.supplierTableRow, isSelected ? styles.selectedRow : null]}>
        <FrozenLeadingColumns
          scrollX={scrollX}
          style={styles.frozenSupplierColumns}
          tone={isSelected ? "selected" : "body"}
        >
          <View style={styles.rowNumberColumn}>
            <TableCell numeric style={styles.strongText}>{formatRowNumber(rowNumber)}</TableCell>
          </View>
          <Pressable
            // 供应商列筛下方商品明细，营业额列单独查看分店汇总。
            onPress={() => {
              setSelectedSupplierCode(item.supplierCode);
              setProductPage(1);
            }}
            accessibilityRole="button"
            accessibilityLabel={`${getSupplierTitle(kind, item)} ${t("productReport.sections.products")}`}
            accessibilityState={{ selected: isSelected }}
            style={[styles.supplierNameColumn, styles.fullHeightCell]}
          >
            <TableCell style={styles.strongText}>{getSupplierTitle(kind, item)}</TableCell>
            <View style={styles.supplierFilterMeta}>
              <TableCell style={[styles.muted, styles.supplierCodeText]}>{item.supplierCode}</TableCell>
              <Text variant="labelSmall" style={styles.filterProductsHint}>
                {t("productReport.actions.filterProducts")}
              </Text>
            </View>
          </Pressable>
        </FrozenLeadingColumns>
        <Pressable
          // 营业额列只打开供应商分店数据，不改变下方商品明细筛选。
          onPress={() => setDrilldown({ type: "supplier", kind, supplier: item })}
          accessibilityRole="button"
          accessibilityLabel={`${getSupplierTitle(kind, item)} ${t("productReport.drilldown.supplier")}`}
          style={[styles.supplierMoneyColumn, styles.fullHeightCell]}
        >
          <TableCell numeric style={styles.strongText}>{formatWholeMoney(item.revenue)}</TableCell>
          <TableCell numeric style={styles.muted}>{formatWholeMoney(item.compareRevenue)}</TableCell>
          <Text style={styles.inlineChevron} accessibilityElementsHidden>›</Text>
        </Pressable>
        {renderGrowthCell(item.revenue, item.compareRevenue, styles.supplierGrowthColumn)}
        <View style={styles.supplierShareColumn}>
          <TableCell numeric style={styles.strongText}>{currentSupplierShare}</TableCell>
          <TableCell numeric style={styles.muted}>{compareSupplierShare}</TableCell>
        </View>
        {kind === "china" ? (
          <View style={styles.supplierShareColumn}>
            <TableCell numeric style={styles.strongText}>{formatShare(item.revenue, totalRevenue.revenue)}</TableCell>
            <TableCell numeric style={styles.muted}>{formatShare(item.compareRevenue, totalRevenue.compareRevenue)}</TableCell>
          </View>
        ) : null}
        <View style={styles.supplierCountColumn}>
          <TableCell numeric style={styles.strongText}>{formatCount(item.orderCount)}</TableCell>
          <TableCell numeric style={styles.muted}>{formatCount(item.compareOrderCount)}</TableCell>
        </View>
        <View style={styles.supplierMoneyColumn}>
          <TableCell numeric style={styles.strongText}>{formatMoney(item.averageTransaction)}</TableCell>
          <TableCell numeric style={styles.muted}>{formatMoney(item.compareAverageTransaction)}</TableCell>
        </View>
        {renderGrossProfitCell(item.grossProfit, item.compareGrossProfit)}
        {renderGrossMarginCell(item.grossMarginRate, item.compareGrossMarginRate)}
      </View>
    );
  };

  const renderProductRow = ({
    item,
    rowNumber,
    scrollX,
  }: {
    item: ProductReportProductRow;
    rowNumber: number;
    scrollX: Animated.Value;
  }) => (
    <Pressable
      style={[styles.tableRow, styles.productTableRow]}
      onPress={() => setDrilldown({ type: "product", product: item })}
      accessibilityRole="button"
      accessibilityLabel={`${item.itemNumber || item.productCode} ${item.productName || ""} ${t("productReport.drilldown.product")}`}
    >
      <FrozenLeadingColumns scrollX={scrollX} style={styles.frozenProductColumns}>
        <View style={styles.rowNumberColumn}>
          <TableCell numeric style={styles.strongText}>{formatRowNumber(rowNumber)}</TableCell>
        </View>
        <View style={styles.productInfoColumn}>
          <TableCell style={styles.strongText}>{item.itemNumber || "--"}</TableCell>
          <TableCell style={styles.muted}>{item.productName || "--"}</TableCell>
        </View>
      </FrozenLeadingColumns>
      <View style={styles.productMoneyColumn}>
        <TableCell numeric style={styles.strongText}>{formatWholeMoney(item.salesAmount)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatWholeMoney(item.compareSalesAmount)}</TableCell>
      </View>
      <View style={styles.productImageColumn}>
        {item.productImage ? (
          <Image source={{ uri: item.productImage }} style={styles.productImage} resizeMode="cover" />
        ) : (
          <View style={styles.productImagePlaceholder}>
            <Text variant="labelSmall" style={styles.placeholderText}>
              {t("productReport.columns.image")}
            </Text>
          </View>
        )}
      </View>
      <View style={styles.productCountColumn}>
        <TableCell numeric style={styles.strongText}>{formatCount(item.quantity)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatCount(item.compareQuantity)}</TableCell>
      </View>
      <View style={styles.productAverageColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(item.averageUnitPrice)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(item.compareAverageUnitPrice)}</TableCell>
      </View>
      {renderGrowthCell(item.salesAmount, item.compareSalesAmount, styles.productGrowthColumn)}
      {renderGrossProfitCell(item.grossProfit, item.compareGrossProfit)}
      {renderGrossMarginCell(item.grossMarginRate, item.compareGrossMarginRate)}
    </Pressable>
  );

  return (
    <View style={styles.container}>
      <ScrollView
        bounces={false}
        onScroll={markProductDataVisible}
        scrollEventThrottle={16}
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={refresh} />}
      >
        {!embedded ? (
          <View style={styles.header}>
            <Text variant="headlineSmall" style={styles.title}>
              {t("productReport.title")}
            </Text>
            <Text variant="bodySmall" style={styles.muted}>
              {dateRangeValid ? `${draftStartDate} - ${draftEndDate}` : t("productReport.states.invalidDate")}
            </Text>
          </View>
        ) : (
          <Text variant="bodySmall" style={styles.muted}>
            {dateRangeValid ? `${draftStartDate} - ${draftEndDate}` : t("productReport.states.invalidDate")}
          </Text>
        )}

        <SegmentedButtons
          value={kind}
          onValueChange={applyKind}
          buttons={[
            { value: "australia", label: t("productReport.tabs.australia") },
            { value: "china", label: t("productReport.tabs.china") },
          ]}
        />

        <View style={styles.filterBar}>
          <Button
            mode="outlined"
            compact
            icon="store-outline"
            disabled={
              !storeOptionsQuery.isSuccess
              || storeOptionsQuery.isFetching
              || cashierEnabledStoreCodes.length === 0
            }
            onPress={() => setStoreModalVisible(true)}
          >
            {selectedStoreLabel}
          </Button>
          {selectedSupplierCode ? (
            <Button
              mode="outlined"
              compact
              icon="close"
              onPress={() => {
                setSelectedSupplierCode(null);
                setProductPage(1);
              }}
            >
              {t("productReport.actions.clearSupplier")}
            </Button>
          ) : null}
        </View>

        <View style={styles.dateInputs}>
          <TextInput
            mode="outlined"
            dense
            label={t("productReport.filters.startDate")}
            value={draftStartDate}
            onChangeText={updateDraftStartDate}
            style={styles.dateInput}
            autoCapitalize="none"
          />
          <TextInput
            mode="outlined"
            dense
            label={t("productReport.filters.endDate")}
            value={draftEndDate}
            onChangeText={updateDraftEndDate}
            style={styles.dateInput}
            autoCapitalize="none"
          />
        </View>

        <View style={styles.quickBar}>
          {(["today", "yesterday", "thisWeek", "lastWeek", "thisMonth", "lastMonth"] as const).map((key) => (
            <Button key={key} compact mode={range.key === key ? "contained" : "outlined"} onPress={() => setQuickRange(key)}>
              {t(`productReport.shortcuts.${key}`)}
            </Button>
          ))}
        </View>

        {!dateRangeValid ? (
          <View style={styles.stateBox}>
            <Text variant="bodyMedium">{t("productReport.states.invalidDate")}</Text>
          </View>
        ) : mainReportRequestError ? (
          <ErrorState
            label={t("productReport.states.error")}
            retryLabel={t("actions.retry")}
            onRetry={() => {
              resetMainReportVersionSync();
              void storeOptionsQuery.refetch();
            }}
          />
        ) : mainReportStatisticsPending ? (
          <LoadingState label={t("reports.states.refreshingStatistics")} />
        ) : mainReportStatisticsIncomplete ? (
          <ErrorState
            label={t("reports.states.statisticsIncomplete")}
            retryLabel={t("actions.retry")}
            onRetry={() => {
              resetMainReportVersionSync();
              void storeOptionsQuery.refetch();
            }}
          />
        ) : storeOptionsQuery.isSuccess && cashierEnabledStoreCodes.length === 0 ? (
          <EmptyState label={t("reports.states.noCashierEnabledStores")} />
        ) : (
          <>
            {productSectionLoading ? (
              <View style={[styles.productSummaryCard, styles.productSummaryLoading]}>
                <ActivityIndicator size="small" />
                <Text variant="bodySmall" style={styles.muted}>
                  {t("productReport.states.loading")}
                </Text>
              </View>
            ) : (
              <ProductPageSummaryCard
                summary={productPageSummary}
                caption={t("productReport.pageSummaryCaption", {
                  start: productTotal === 0 ? 0 : (productPage - 1) * PRODUCT_PAGE_SIZE + 1,
                  end: Math.min(productPage * PRODUCT_PAGE_SIZE, productTotal),
                  total: productTotal,
                })}
              />
            )}
            <View style={[styles.reportSection, { minHeight: sectionScreenHeight }]}>
              <SectionHeader
                title={t("productReport.sections.suppliers")}
                page={supplierPage}
                pageCount={supplierPageCount}
                onPrevious={() => setSupplierPage((current) => Math.max(1, current - 1))}
                onNext={() => setSupplierPage((current) => Math.min(supplierPageCount, current + 1))}
                previousLabel={t("productReport.actions.previous")}
                nextLabel={t("productReport.actions.next")}
              />
              {supplierQuery.isLoading ? (
                <LoadingState label={t("productReport.states.loading")} />
              ) : (
                <FrozenHorizontalTable>
                  {(scrollX) => (
                    <View style={[styles.table, kind === "china" ? styles.chinaSupplierTable : styles.supplierTable]}>
                      <SupplierTableHeader kind={kind} scrollX={scrollX} />
                      <ScrollView
                        bounces={false}
                        nestedScrollEnabled
                        showsVerticalScrollIndicator={false}
                        style={[styles.tableBody, { height: supplierTableBodyHeight }]}
                      >
                        {supplierPageRows.length === 0 ? (
                          <EmptyState label={t("productReport.states.emptySuppliers")} />
                        ) : (
                          supplierPageRows.map((item, index) => (
                            <View
                              key={item.id}
                              ref={index === 0 ? firstSupplierReportRowRef : undefined}
                              collapsable={index === 0 ? false : undefined}
                              onLayout={index === 0 ? scheduleProductDataVisibilityCheck : undefined}
                            >
                              {renderSupplierRow({
                                item,
                                rowNumber: (supplierPage - 1) * SUPPLIER_PAGE_SIZE + index + 1,
                                scrollX,
                              })}
                            </View>
                          ))
                        )}
                      </ScrollView>
                    </View>
                  )}
                </FrozenHorizontalTable>
              )}
            </View>

            <View style={[styles.reportSection, { minHeight: sectionScreenHeight }]}>
              <SectionHeader
                title={t("productReport.sections.products")}
                page={productPage}
                pageCount={productPageCount}
                onPrevious={() => setProductPage((current) => Math.max(1, current - 1))}
                onNext={() => setProductPage((current) => Math.min(productPageCount, current + 1))}
                previousLabel={t("productReport.actions.previous")}
                nextLabel={t("productReport.actions.next")}
              />
              <View style={styles.productSearchBar}>
                <TextInput
                  mode="outlined"
                  dense
                  label={t("productReport.filters.productSearch")}
                  value={productSearchDraft}
                  onChangeText={setProductSearchDraft}
                  onSubmitEditing={applyProductSearch}
                  returnKeyType="search"
                  autoCapitalize="none"
                  style={styles.productSearchInput}
                />
                <View style={styles.productSearchActions}>
                  <Button compact mode="contained" onPress={applyProductSearch}>
                    {t("productReport.actions.searchProduct")}
                  </Button>
                  {productSearch ? (
                    <Button compact mode="outlined" onPress={clearProductSearch}>
                      {t("productReport.actions.clearProductSearch")}
                    </Button>
                  ) : null}
                </View>
              </View>
              {productSectionLoading ? (
                <LoadingState label={t("productReport.states.loading")} />
              ) : (
                <FrozenHorizontalTable>
                  {(scrollX) => (
                  <View style={[styles.table, styles.productTable]}>
                    <ProductTableHeader scrollX={scrollX} />
                    <ScrollView
                      bounces={false}
                      nestedScrollEnabled
                      showsVerticalScrollIndicator={false}
                      style={[styles.tableBody, { height: productTableBodyHeight }]}
                    >
                      {productRows.length === 0 ? (
                        <EmptyState
                          label={t(productSearch ? "productReport.states.emptyProductSearch" : "productReport.states.emptyProducts")}
                        />
                      ) : (
                        productRows.map((item, index) => (
                          <View
                            key={item.id}
                            ref={index === 0 ? firstProductReportRowRef : undefined}
                            collapsable={index === 0 ? false : undefined}
                            onLayout={index === 0 ? scheduleProductDataVisibilityCheck : undefined}
                          >
                            {renderProductRow({
                              item,
                              rowNumber: (productPage - 1) * PRODUCT_PAGE_SIZE + index + 1,
                              scrollX,
                            })}
                          </View>
                        ))
                      )}
                    </ScrollView>
                  </View>
                  )}
                </FrozenHorizontalTable>
              )}
            </View>
          </>
        )}
      </ScrollView>

      <StorePickerModal
        visible={
          isStoreModalVisible
          && storeOptionsQuery.isSuccess
          && !storeOptionsQuery.isFetching
        }
        labelAll={t("productReport.filters.allStores")}
        options={
          storeOptionsQuery.isSuccess && !storeOptionsQuery.isFetching
            ? storeOptionsQuery.data ?? []
            : []
        }
        selectedStoreCode={selectedStoreCode}
        onSelect={applyStore}
        onDismiss={() => setStoreModalVisible(false)}
      />
      <BranchDrilldownModal
        visible={
          Boolean(drilldown)
          && storeOptionsQuery.isSuccess
          && !storeOptionsQuery.isFetching
        }
        title={
          drilldown?.type === "supplier"
            ? t("productReport.drilldown.supplier")
            : t("productReport.drilldown.product")
        }
        supplierRows={supplierBranchQuery.data?.data ?? []}
        productRows={productBranchQuery.data?.data ?? []}
        isLoading={isDrilldownLoading}
        isError={isDrilldownError || isDrilldownStatisticsIncomplete}
        onRetry={retryDrilldown}
        onDismiss={() => setDrilldown(null)}
        closeLabel={t("actions.close")}
        retryLabel={t("actions.retry")}
        errorLabel={isDrilldownStatisticsIncomplete
          ? t("reports.states.statisticsIncomplete")
          : t("productReport.states.error")}
        emptyLabel={drilldownEmptyLabel}
        kind={drilldownKind}
        growthNewLabel={growthNewLabel}
        costPendingLabel={costPendingLabel}
        onFirstDataVisibilityChange={updateDrilldownFirstDataVisibility}
      />
    </View>
  );
}

function SectionHeader({
  title,
  page,
  pageCount,
  onPrevious,
  onNext,
  previousLabel,
  nextLabel,
}: {
  title: string;
  page: number;
  pageCount: number;
  onPrevious: () => void;
  onNext: () => void;
  previousLabel: string;
  nextLabel: string;
}) {
  const { t } = useAppTranslation("common");
  return (
    <View style={styles.sectionHeader}>
      <View style={styles.sectionTitleBlock}>
        <Text variant="titleMedium" style={styles.sectionTitle}>
          {title}
        </Text>
        <View style={styles.valueLegend}>
          <Text variant="labelSmall" style={styles.strongText}>{t("reports.metrics.current")}</Text>
          <Text variant="labelSmall" style={styles.muted}>/ {t("productReport.metrics.compare")}</Text>
        </View>
      </View>
      <View style={styles.pager}>
        <Button compact mode="outlined" disabled={page <= 1} onPress={onPrevious}>
          {previousLabel}
        </Button>
        <Text variant="bodySmall" style={styles.pageText}>
          {page}/{pageCount}
        </Text>
        <Button compact mode="outlined" disabled={page >= pageCount} onPress={onNext}>
          {nextLabel}
        </Button>
      </View>
    </View>
  );
}

function SupplierTableHeader({ kind, scrollX }: { kind: SupplierReportKind; scrollX: Animated.Value }) {
  const { t } = useAppTranslation("common");
  return (
    <View style={[styles.tableRow, styles.tableHeaderRow, styles.supplierTableRow]}>
      <FrozenLeadingColumns scrollX={scrollX} style={styles.frozenSupplierColumns} tone="header">
        <View style={styles.rowNumberColumn}>
          <TableCell numeric style={styles.headerText}>{t("productReport.columns.rowNumber")}</TableCell>
        </View>
        <View style={styles.supplierNameColumn}>
          <TableCell style={styles.headerText}>{t("productReport.sections.suppliers")}</TableCell>
        </View>
      </FrozenLeadingColumns>
      <View style={styles.supplierMoneyColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.revenue")}</TableCell>
      </View>
      <View style={styles.supplierGrowthColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.growthRate")}</TableCell>
      </View>
      <View style={styles.supplierShareColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.supplierShare")}</TableCell>
      </View>
      {kind === "china" ? (
        <View style={styles.supplierShareColumn}>
          <TableCell numeric style={styles.headerText}>{t("productReport.metrics.chinaShare")}</TableCell>
        </View>
      ) : null}
      <View style={styles.supplierCountColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.orders")}</TableCell>
      </View>
      <View style={styles.supplierMoneyColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.aov")}</TableCell>
      </View>
      <View style={styles.grossProfitColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossProfit")}</TableCell>
      </View>
      <View style={styles.grossMarginColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossMarginRate")}</TableCell>
      </View>
    </View>
  );
}

function ProductTableHeader({ scrollX }: { scrollX: Animated.Value }) {
  const { t } = useAppTranslation("common");
  return (
    <View style={[styles.tableRow, styles.tableHeaderRow, styles.productTableRow]}>
      <FrozenLeadingColumns scrollX={scrollX} style={styles.frozenProductColumns} tone="header">
        <View style={styles.rowNumberColumn}>
          <TableCell numeric style={styles.headerText}>{t("productReport.columns.rowNumber")}</TableCell>
        </View>
        <View style={styles.productInfoColumn}>
          <TableCell style={styles.headerText}>{t("productReport.columns.product")}</TableCell>
        </View>
      </FrozenLeadingColumns>
      <View style={styles.productMoneyColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.amount")}</TableCell>
      </View>
      <View style={styles.productImageColumn}>
        <TableCell style={styles.headerText}>{t("productReport.columns.image")}</TableCell>
      </View>
      <View style={styles.productCountColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.quantity")}</TableCell>
      </View>
      <View style={styles.productAverageColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.averagePrice")}</TableCell>
      </View>
      <View style={styles.productGrowthColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.growthRate")}</TableCell>
      </View>
      <View style={styles.grossProfitColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossProfit")}</TableCell>
      </View>
      <View style={styles.grossMarginColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossMarginRate")}</TableCell>
      </View>
    </View>
  );
}

function LoadingState({ label }: { label: string }) {
  return (
    <View style={styles.stateBox}>
      <ActivityIndicator />
      <Text>{label}</Text>
    </View>
  );
}

function ErrorState({ label, retryLabel, onRetry }: { label: string; retryLabel: string; onRetry: () => void }) {
  return (
    <View style={styles.stateBox}>
      <Text variant="bodyMedium">{label}</Text>
      <Button mode="contained" onPress={onRetry}>
        {retryLabel}
      </Button>
    </View>
  );
}

function EmptyState({ label }: { label: string }) {
  return (
    <View style={styles.stateBox}>
      <Text variant="bodyMedium">{label}</Text>
    </View>
  );
}

function StorePickerModal({
  visible,
  labelAll,
  options,
  selectedStoreCode,
  onSelect,
  onDismiss,
}: {
  visible: boolean;
  labelAll: string;
  options: { label: string; value: string }[];
  selectedStoreCode?: string;
  onSelect: (storeCode?: string) => void;
  onDismiss: () => void;
}) {
  return (
    <Portal>
      <Modal visible={visible} onDismiss={onDismiss} contentContainerStyle={styles.modal}>
        <ScrollView
          bounces={false}
          nestedScrollEnabled
          showsVerticalScrollIndicator
          keyboardShouldPersistTaps="handled"
          style={styles.storeModalList}
          contentContainerStyle={styles.storeModalListContent}
        >
          <Button
            mode={!selectedStoreCode ? "contained" : "outlined"}
            onPress={() => onSelect(undefined)}
            style={styles.modalOption}
          >
            {labelAll}
          </Button>
          {options.map((option) => (
            <Button
              key={option.value}
              mode={selectedStoreCode === option.value ? "contained" : "outlined"}
              onPress={() => onSelect(option.value)}
              style={styles.modalOption}
            >
              {option.label}
            </Button>
          ))}
        </ScrollView>
      </Modal>
    </Portal>
  );
}

function BranchDrilldownModal({
  visible,
  title,
  supplierRows,
  productRows,
  isLoading,
  isError,
  onRetry,
  onDismiss,
  closeLabel,
  retryLabel,
  errorLabel,
  emptyLabel,
  kind,
  growthNewLabel,
  costPendingLabel,
  onFirstDataVisibilityChange,
}: {
  visible: boolean;
  title: string;
  supplierRows: SupplierBranchBreakdownRow[];
  productRows: ProductBranchBreakdownRow[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  onDismiss: () => void;
  closeLabel: string;
  retryLabel: string;
  errorLabel: string;
  emptyLabel: string;
  kind: "supplier" | "product" | null;
  growthNewLabel: string;
  costPendingLabel: string;
  onFirstDataVisibilityChange: (visible: boolean) => void;
}) {
  const { t } = useAppTranslation("common");
  const { height: windowHeight } = useWindowDimensions();
  const rows = kind === "supplier" ? supplierRows : productRows;
  const firstRowRef = useRef<View>(null);
  const visibilityTaskRef = useRef<{ cancel: () => void } | null>(null);
  const visibilityGenerationRef = useRef(0);
  const scheduleFirstRowVisibilityCheck = useCallback(() => {
    visibilityTaskRef.current?.cancel();
    const generation = visibilityGenerationRef.current + 1;
    visibilityGenerationRef.current = generation;
    visibilityTaskRef.current = InteractionManager.runAfterInteractions(() => {
      if (
        visibilityGenerationRef.current !== generation
        || !visible
        || isLoading
        || isError
        || rows.length === 0
      ) return;
      firstRowRef.current?.measureInWindow((_x, y, _width, measuredHeight) => {
        if (visibilityGenerationRef.current !== generation) return;
        const isVisible = measuredHeight > 0 && y < windowHeight && y + measuredHeight > 0;
        onFirstDataVisibilityChange(isVisible);
      });
    });
  }, [isError, isLoading, onFirstDataVisibilityChange, rows.length, visible, windowHeight]);
  useLayoutEffect(() => {
    if (!visible || isLoading || isError || rows.length === 0) return;
    scheduleFirstRowVisibilityCheck();
    return () => {
      visibilityGenerationRef.current += 1;
      visibilityTaskRef.current?.cancel();
      visibilityTaskRef.current = null;
    };
  }, [isError, isLoading, kind, rows, scheduleFirstRowVisibilityCheck, visible]);
  const renderGrowthCell = (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => {
    const tone = getGrowthTone(current, compare);
    return (
      <View style={[styles.growthColumn, columnStyle]}>
        <TableCell numeric style={[styles.strongText, { color: GROWTH_COLORS[tone] }]}>
          {formatGrowthRate(current, compare, growthNewLabel)}
        </TableCell>
      </View>
    );
  };
  const renderGrossProfitCell = (current: number | null, compare: number | null) => (
    <View style={styles.grossProfitColumn}>
      <TableCell numeric style={styles.strongText}>{formatNullableMoney(current)}</TableCell>
      <TableCell numeric style={styles.muted}>{formatNullableMoney(compare)}</TableCell>
    </View>
  );
  const renderGrossMarginCell = (current: number | null, compare: number | null) => (
    <View style={styles.grossMarginColumn}>
      <TableCell numeric style={styles.strongText}>{formatGrossMarginRate(current, costPendingLabel)}</TableCell>
      <TableCell numeric style={styles.muted}>{formatGrossMarginRate(compare, costPendingLabel)}</TableCell>
    </View>
  );
  return (
    <Portal>
      <Modal visible={visible} onDismiss={onDismiss} contentContainerStyle={[styles.modal, styles.drilldownModal]}>
        <Text variant="titleMedium" style={styles.modalTitle}>
          {title}
        </Text>
        {isLoading ? (
          <LoadingState label={t("productReport.states.loading")} />
        ) : isError ? (
          <ErrorState label={errorLabel} retryLabel={retryLabel} onRetry={onRetry} />
        ) : rows.length === 0 ? (
          <EmptyState label={emptyLabel} />
        ) : (
          <ScrollView
            bounces={false}
            nestedScrollEnabled
            onScroll={scheduleFirstRowVisibilityCheck}
            scrollEventThrottle={16}
            style={[styles.modalList, styles.drilldownModalList]}
          >
            <FrozenHorizontalTable>
              {(scrollX) => (
              <View style={[styles.table, kind === "product" ? styles.productDrilldownTable : styles.drilldownTable]}>
                <View style={[styles.tableRow, styles.tableHeaderRow, kind === "product" ? styles.productBranchTableRow : null]}>
                  <FrozenLeadingColumns
                    scrollX={scrollX}
                    style={kind === "product" ? styles.frozenProductBranchColumns : styles.frozenBranchColumns}
                    tone="header"
                  >
                    <View style={styles.rowNumberColumn}>
                      <TableCell numeric style={styles.headerText}>{t("productReport.columns.rowNumber")}</TableCell>
                    </View>
                    <View style={kind === "product" ? styles.productBranchNameColumn : styles.branchColumn}>
                      <TableCell style={styles.headerText}>{t("productReport.filters.store")}</TableCell>
                    </View>
                  </FrozenLeadingColumns>
                  {kind === "product" ? (
                    <>
                      <View style={styles.productBranchCountColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.columns.quantity")}</TableCell>
                      </View>
                      <View style={styles.productBranchMoneyColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.columns.amount")}</TableCell>
                      </View>
                      <View style={styles.productBranchAverageColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.columns.averagePrice")}</TableCell>
                      </View>
                      <View style={styles.productBranchGrowthColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.growthRate")}</TableCell>
                      </View>
                      <View style={styles.grossProfitColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossProfit")}</TableCell>
                      </View>
                      <View style={styles.grossMarginColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossMarginRate")}</TableCell>
                      </View>
                    </>
                  ) : (
                    <>
                      <View style={styles.moneyColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.columns.amount")}</TableCell>
                      </View>
                      <View style={styles.growthColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.growthRate")}</TableCell>
                      </View>
                      <View style={styles.countColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.orders")}</TableCell>
                      </View>
                      <View style={styles.moneyColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.aov")}</TableCell>
                      </View>
                      <View style={styles.grossProfitColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossProfit")}</TableCell>
                      </View>
                      <View style={styles.grossMarginColumn}>
                        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.grossMarginRate")}</TableCell>
                      </View>
                    </>
                  )}
                </View>
                {kind === "supplier"
                  ? supplierRows.map((row, index) => (
                      <SupplierBranchRow
                        key={row.id}
                        row={row}
                        rowNumber={index + 1}
                        renderGrowthCell={renderGrowthCell}
                        renderGrossProfitCell={renderGrossProfitCell}
                        renderGrossMarginCell={renderGrossMarginCell}
                        rowRef={index === 0 ? firstRowRef : undefined}
                        scrollX={scrollX}
                      />
                    ))
                  : productRows.map((row, index) => (
                      <ProductBranchRow
                        key={row.id}
                        row={row}
                        rowNumber={index + 1}
                        renderGrowthCell={renderGrowthCell}
                        renderGrossProfitCell={renderGrossProfitCell}
                        renderGrossMarginCell={renderGrossMarginCell}
                        rowRef={index === 0 ? firstRowRef : undefined}
                        scrollX={scrollX}
                      />
                    ))}
              </View>
              )}
            </FrozenHorizontalTable>
          </ScrollView>
        )}
        <Button mode="contained" onPress={onDismiss}>
          {closeLabel}
        </Button>
      </Modal>
    </Portal>
  );
}

function SupplierBranchRow({
  row,
  rowNumber,
  renderGrowthCell,
  renderGrossProfitCell,
  renderGrossMarginCell,
  rowRef,
  scrollX,
}: {
  row: SupplierBranchBreakdownRow;
  rowNumber: number;
  renderGrowthCell: (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => ReactNode;
  renderGrossProfitCell: (current: number | null, compare: number | null) => ReactNode;
  renderGrossMarginCell: (current: number | null, compare: number | null) => ReactNode;
  rowRef?: RefObject<View | null>;
  scrollX: Animated.Value;
}) {
  return (
    <View ref={rowRef} style={styles.tableRow}>
      <FrozenLeadingColumns scrollX={scrollX} style={styles.frozenBranchColumns}>
        <View style={styles.rowNumberColumn}>
          <TableCell numeric style={styles.strongText}>{formatRowNumber(rowNumber)}</TableCell>
        </View>
        <View style={styles.branchColumn}>
          <TableCell style={styles.strongText}>{row.branchName || row.branchCode}</TableCell>
          <TableCell style={styles.muted}>{row.branchCode}</TableCell>
        </View>
      </FrozenLeadingColumns>
      <View style={styles.moneyColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(row.revenue)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(row.compareRevenue)}</TableCell>
      </View>
      {renderGrowthCell(row.revenue, row.compareRevenue)}
      <View style={styles.countColumn}>
        <TableCell numeric style={styles.strongText}>{formatCount(row.orderCount)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatCount(row.compareOrderCount)}</TableCell>
      </View>
      <View style={styles.moneyColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(row.averageTransaction)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(row.compareAverageTransaction)}</TableCell>
      </View>
      {renderGrossProfitCell(row.grossProfit, row.compareGrossProfit)}
      {renderGrossMarginCell(row.grossMarginRate, row.compareGrossMarginRate)}
    </View>
  );
}

function ProductBranchRow({
  row,
  rowNumber,
  renderGrowthCell,
  renderGrossProfitCell,
  renderGrossMarginCell,
  rowRef,
  scrollX,
}: {
  row: ProductBranchBreakdownRow;
  rowNumber: number;
  renderGrowthCell: (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => ReactNode;
  renderGrossProfitCell: (current: number | null, compare: number | null) => ReactNode;
  renderGrossMarginCell: (current: number | null, compare: number | null) => ReactNode;
  rowRef?: RefObject<View | null>;
  scrollX: Animated.Value;
}) {
  return (
    <View ref={rowRef} style={[styles.tableRow, styles.productBranchTableRow]}>
      <FrozenLeadingColumns scrollX={scrollX} style={styles.frozenProductBranchColumns}>
        <View style={styles.rowNumberColumn}>
          <TableCell numeric style={styles.strongText}>{formatRowNumber(rowNumber)}</TableCell>
        </View>
        <View style={styles.productBranchNameColumn}>
          <TableCell style={styles.strongText}>{row.branchName || row.branchCode}</TableCell>
          <TableCell style={styles.muted}>{row.branchCode}</TableCell>
        </View>
      </FrozenLeadingColumns>
      <View style={styles.productBranchCountColumn}>
        <TableCell numeric style={styles.strongText}>{formatCount(row.quantity)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatCount(row.compareQuantity)}</TableCell>
      </View>
      <View style={styles.productBranchMoneyColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(row.salesAmount)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(row.compareSalesAmount)}</TableCell>
      </View>
      <View style={styles.productBranchAverageColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(row.averageUnitPrice)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(row.compareAverageUnitPrice)}</TableCell>
      </View>
      {renderGrowthCell(row.salesAmount, row.compareSalesAmount, styles.productBranchGrowthColumn)}
      {renderGrossProfitCell(row.grossProfit, row.compareGrossProfit)}
      {renderGrossMarginCell(row.grossMarginRate, row.compareGrossMarginRate)}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  content: {
    gap: 12,
    padding: 16,
    paddingBottom: 40,
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
  filterBar: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  dateInputs: {
    flexDirection: "row",
    gap: 8,
  },
  dateInput: {
    flex: 1,
    minWidth: 132,
    backgroundColor: "#FFFFFF",
  },
  quickBar: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  productSearchBar: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 8,
  },
  productSearchInput: {
    flex: 1,
    minWidth: 180,
    backgroundColor: "#FFFFFF",
  },
  productSearchActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  reportSection: {
    gap: 8,
  },
  productSummaryCard: {
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "#E2E8F0",
    borderRadius: 10,
    paddingHorizontal: 10,
    paddingTop: 8,
    paddingBottom: 6,
    backgroundColor: "#FFFFFF",
  },
  productSummaryLoading: {
    minHeight: 88,
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
  },
  productSummaryHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    borderBottomWidth: 1,
    borderBottomColor: "#E2E8F0",
    paddingBottom: 6,
  },
  productSummaryGrid: {
    flexDirection: "row",
    alignItems: "flex-start",
    paddingTop: 4,
  },
  productSummaryLabelColumn: {
    width: 42,
    gap: 2,
  },
  productSummaryMetric: {
    flex: 1,
    minWidth: 0,
    gap: 2,
    paddingHorizontal: 3,
  },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    marginTop: 4,
  },
  sectionTitle: {
    color: "#111827",
    fontWeight: "700",
  },
  sectionTitleBlock: {
    flex: 1,
    minWidth: 0,
  },
  valueLegend: {
    flexDirection: "row",
    alignItems: "center",
    gap: 3,
  },
  pager: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  pageText: {
    minWidth: 44,
    textAlign: "center",
    color: "#4B5563",
  },
  listContent: {
    gap: 8,
  },
  table: {
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  supplierTable: {
    minWidth: 626,
  },
  chinaSupplierTable: {
    minWidth: 696,
  },
  productTable: {
    minWidth: 648,
  },
  drilldownTable: {
    minWidth: 712,
  },
  productDrilldownTable: {
    minWidth: 534,
  },
  tableBody: {
    flexGrow: 0,
  },
  tableRow: {
    minHeight: 54,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    borderBottomWidth: 1,
    borderBottomColor: "#E5E7EB",
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  tableHeaderRow: {
    minHeight: 38,
    backgroundColor: "#F3F4F6",
  },
  frozenLeadingColumns: {
    position: "relative",
    zIndex: 3,
    elevation: 1,
    alignSelf: "stretch",
    flexDirection: "row",
    alignItems: "center",
    marginVertical: -8,
    paddingVertical: 8,
    borderRightWidth: 1,
    borderRightColor: "#CBD5E1",
  },
  frozenBodyColumns: {
    backgroundColor: "#FFFFFF",
  },
  frozenHeaderColumns: {
    backgroundColor: "#F3F4F6",
  },
  frozenSelectedColumns: {
    backgroundColor: "#EFF6FF",
  },
  frozenSupplierColumns: {
    gap: 3,
    marginLeft: -4,
    paddingLeft: 4,
  },
  frozenProductColumns: {
    gap: 3,
    marginLeft: -4,
    paddingLeft: 4,
  },
  frozenBranchColumns: {
    gap: 8,
    marginLeft: -10,
    paddingLeft: 10,
  },
  frozenProductBranchColumns: {
    gap: 4,
    marginLeft: -6,
    paddingLeft: 6,
  },
  productTableRow: {
    gap: 3,
    paddingHorizontal: 4,
  },
  supplierTableRow: {
    gap: 3,
    paddingHorizontal: 4,
  },
  supplierNameColumn: {
    width: 80,
    minWidth: 0,
  },
  supplierMoneyColumn: {
    width: 68,
    minWidth: 0,
    paddingRight: 10,
    position: "relative",
  },
  fullHeightCell: {
    alignSelf: "stretch",
    justifyContent: "center",
  },
  supplierFilterMeta: {
    flexDirection: "row",
    alignItems: "center",
    gap: 3,
  },
  supplierCodeText: {
    flex: 1,
    minWidth: 0,
  },
  filterProductsHint: {
    color: "#2563EB",
    fontWeight: "700",
  },
  inlineChevron: {
    position: "absolute",
    top: 8,
    right: 1,
    color: "#64748B",
    fontSize: 20,
    lineHeight: 24,
  },
  supplierGrowthColumn: {
    width: 66,
    minWidth: 0,
  },
  supplierShareColumn: {
    width: 66,
    minWidth: 0,
  },
  supplierCountColumn: {
    width: 58,
    minWidth: 0,
  },
  productNameColumn: {
    width: 190,
    minWidth: 0,
  },
  itemColumn: {
    width: 100,
    minWidth: 0,
  },
  imageColumn: {
    width: 58,
    alignItems: "center",
  },
  productImageColumn: {
    width: 52,
    alignItems: "center",
  },
  productInfoColumn: {
    // 商品明细横向滚动时固定名称列，避免宽屏下把数量列推得太远。
    width: 80,
    minWidth: 0,
  },
  productCountColumn: {
    width: 54,
    minWidth: 0,
  },
  productMoneyColumn: {
    width: 68,
    minWidth: 0,
  },
  productAverageColumn: {
    width: 68,
    minWidth: 0,
  },
  productGrowthColumn: {
    width: 64,
    minWidth: 0,
  },
  branchColumn: {
    width: 130,
    minWidth: 0,
  },
  productBranchTableRow: {
    gap: 4,
    paddingHorizontal: 6,
  },
  productBranchNameColumn: {
    width: 68,
    minWidth: 0,
  },
  productBranchCountColumn: {
    width: 46,
    minWidth: 0,
  },
  productBranchMoneyColumn: {
    width: 76,
    minWidth: 0,
  },
  productBranchAverageColumn: {
    width: 60,
    minWidth: 0,
  },
  productBranchGrowthColumn: {
    width: 64,
    minWidth: 0,
  },
  moneyColumn: {
    width: 96,
    minWidth: 0,
  },
  countColumn: {
    width: 72,
    minWidth: 0,
  },
  shareColumn: {
    width: 80,
    minWidth: 0,
  },
  growthColumn: {
    width: 78,
    minWidth: 0,
  },
  rowNumberColumn: {
    width: 24,
    minWidth: 0,
  },
  grossProfitColumn: {
    width: 72,
    minWidth: 0,
  },
  grossMarginColumn: {
    width: 72,
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
  row: {
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  selectedRow: {
    borderColor: "#2563EB",
    backgroundColor: "#EFF6FF",
  },
  rowPressable: {
    gap: 10,
    padding: 12,
  },
  rowHeader: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  rowTitleWrap: {
    flex: 1,
    minWidth: 0,
  },
  rowTitle: {
    color: "#111827",
    fontWeight: "700",
  },
  metricsGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  metricBox: {
    minWidth: 112,
    flex: 1,
    borderRadius: 6,
    backgroundColor: "#F3F4F6",
    paddingHorizontal: 8,
    paddingVertical: 6,
  },
  metricLabel: {
    color: "#4B5563",
  },
  metricValue: {
    color: "#111827",
    fontWeight: "700",
  },
  metricCompare: {
    color: "#6B7280",
  },
  productRow: {
    minHeight: 92,
    flexDirection: "row",
    gap: 10,
    borderWidth: 1,
    borderColor: "#E5E7EB",
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    padding: 10,
  },
  productImage: {
    width: 44,
    height: 44,
    borderRadius: 6,
    backgroundColor: "#E5E7EB",
  },
  productImagePlaceholder: {
    width: 44,
    height: 44,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 6,
    backgroundColor: "#E5E7EB",
  },
  placeholderText: {
    color: "#6B7280",
  },
  productMain: {
    flex: 1,
    minWidth: 0,
    gap: 4,
  },
  productMetrics: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
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
  modal: {
    maxHeight: "82%",
    margin: 18,
    gap: 10,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    padding: 16,
  },
  drilldownModal: {
    flex: 1,
    height: "100%",
    maxHeight: "100%",
    margin: 0,
    borderRadius: 0,
    paddingHorizontal: 10,
    paddingTop: 48,
    paddingBottom: 24,
  },
  modalTitle: {
    color: "#111827",
    fontWeight: "700",
  },
  modalOption: {
    alignSelf: "stretch",
  },
  storeModalList: {
    flexShrink: 1,
  },
  storeModalListContent: {
    gap: 10,
  },
  modalList: {
    maxHeight: 420,
  },
  drilldownModalList: {
    flex: 1,
    maxHeight: "100%",
  },
  drillRow: {
    gap: 3,
    borderBottomWidth: 1,
    borderBottomColor: "#E5E7EB",
    paddingVertical: 10,
  },
});
