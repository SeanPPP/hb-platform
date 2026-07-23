import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import {
  Image,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  useWindowDimensions,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
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
  type ProductBranchBreakdownRow,
  type ProductReportProductRow,
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
import { GROWTH_COLORS, formatGrowthRate, getGrowthTone } from "@/modules/reports/growth-rate";
import { REPORT_QUERY_OPTIONS } from "@/modules/reports/report-config";
import { PRODUCT_PAGE_SIZE, SUPPLIER_PAGE_SIZE, getPageRows } from "@/modules/product-report/pagination";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type Drilldown =
  | { type: "supplier"; kind: SupplierReportKind; supplier: SupplierReportRow }
  | { type: "product"; product: ProductReportProductRow };

interface ProductReportScreenProps {
  embedded?: boolean;
  onRefreshFreshness?: () => Promise<unknown>;
  onRefreshReport?: () => Promise<unknown>;
}

function formatCount(value: number) {
  return Math.round(value).toLocaleString("en-AU");
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

export function ProductReportScreen({
  embedded = false,
  onRefreshFreshness,
  onRefreshReport,
}: ProductReportScreenProps) {
  const { t } = useAppTranslation("common");
  const { height } = useWindowDimensions();
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

  const dateRangeValid = isValidProductReportDateRange(draftStartDate, draftEndDate);
  const activeRange = useMemo(
    () => (dateRangeValid ? getCustomProductReportRange(draftStartDate, draftEndDate) ?? range : range),
    [dateRangeValid, draftEndDate, draftStartDate, range]
  );
  const branchCodes = useMemo(
    () => (selectedStoreCode ? [selectedStoreCode] : undefined),
    [selectedStoreCode]
  );

  const queryParams = useMemo(
    () => (dateRangeValid && activeRange ? buildProductReportDateQuery(activeRange, branchCodes) : null),
    [activeRange, branchCodes, dateRangeValid]
  );
  // 供应商营业额弹窗要看所有分店，不继承顶部单店筛选。
  const supplierBranchQueryParams = useMemo(
    () => (dateRangeValid && activeRange ? buildProductReportDateQuery(activeRange) : null),
    [activeRange, dateRangeValid]
  );
  // 商品分店弹窗也要完整分店数据，不继承顶部单店筛选。
  const productBranchQueryParams = useMemo(
    () => (dateRangeValid && activeRange ? buildProductReportDateQuery(activeRange) : null),
    [activeRange, dateRangeValid]
  );

  const storeOptionsQuery = useQuery({
    queryKey: ["product-report", "stores"],
    queryFn: fetchProductReportStoreOptions,
    ...REPORT_QUERY_OPTIONS,
  });

  const totalRevenueQuery = useQuery({
    queryKey: ["product-report", "total-revenue", queryParams],
    queryFn: () => fetchProductReportTotalRevenue(queryParams!),
    enabled: Boolean(queryParams),
    ...REPORT_QUERY_OPTIONS,
  });

  const supplierQuery = useQuery({
    queryKey: ["product-report", "suppliers", kind, queryParams],
    queryFn: () => fetchSupplierReportRows(kind, queryParams!, 1000),
    enabled: Boolean(queryParams),
    ...REPORT_QUERY_OPTIONS,
  });

  const supplierRows = supplierQuery.data ?? [];
  const selectedSupplier = supplierRows.find((row) => row.supplierCode === selectedSupplierCode) ?? null;
  const supplierPageCount = Math.max(1, Math.ceil(supplierRows.length / SUPPLIER_PAGE_SIZE));
  const supplierPageRows = getPageRows(supplierRows, supplierPage, SUPPLIER_PAGE_SIZE);
  const supplierSubtotal = supplierRows.reduce((sum, row) => sum + row.revenue, 0);
  const supplierCompareSubtotal = supplierRows.reduce((sum, row) => sum + row.compareRevenue, 0);
  const totalRevenue = totalRevenueQuery.data ?? { revenue: 0, compareRevenue: 0 };

  const supplierFilterCodes = useMemo(
    () => (selectedSupplierCode ? [selectedSupplierCode] : undefined),
    [selectedSupplierCode]
  );

  const productQuery = useQuery({
    queryKey: ["product-report", "products", kind, queryParams, supplierFilterCodes, productSearch, productPage],
    queryFn: () =>
      fetchProductReportProductRows(
        kind,
        queryParams!,
        supplierFilterCodes,
        productPage,
        PRODUCT_PAGE_SIZE,
        productSearch
      ),
    enabled: Boolean(queryParams),
    ...REPORT_QUERY_OPTIONS,
  });
  const productRows = productQuery.data?.rows ?? [];
  const productTotal = productQuery.data?.total ?? 0;
  const productPageCount = Math.max(1, Math.ceil(productTotal / PRODUCT_PAGE_SIZE));
  // 商品报告的两个数据区块各自接近一屏，分页和搜索栏也计入区块高度。
  const sectionScreenHeight = Math.max(560, Math.floor(height * 0.76));
  const supplierTableBodyHeight = Math.max(420, sectionScreenHeight - 112);
  const productTableBodyHeight = Math.max(380, sectionScreenHeight - 184);
  const growthNewLabel = t("productReport.metrics.newGrowth");

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

  const supplierBranchQuery = useQuery({
    queryKey: ["product-report", "supplier-branches", drilldown, supplierBranchQueryParams],
    queryFn: () =>
      fetchSupplierBranchBreakdown(
        (drilldown as Extract<Drilldown, { type: "supplier" }>).kind,
        supplierBranchQueryParams!,
        (drilldown as Extract<Drilldown, { type: "supplier" }>).supplier.supplierCode
      ),
    enabled: Boolean(supplierBranchQueryParams && drilldown?.type === "supplier"),
    ...REPORT_QUERY_OPTIONS,
  });

  const productBranchQuery = useQuery({
    queryKey: ["product-report", "product-branches", drilldown, productBranchQueryParams],
    queryFn: () =>
      fetchProductBranchBreakdown(
        productBranchQueryParams!,
        (drilldown as Extract<Drilldown, { type: "product" }>).product.productCode
      ),
    enabled: Boolean(productBranchQueryParams && drilldown?.type === "product"),
    ...REPORT_QUERY_OPTIONS,
  });
  const drilldownKind = drilldown?.type ?? null;
  // 弹窗状态按当前下钻类型取值，避免另一个禁用查询把内容渲染成空白。
  const isDrilldownLoading =
    drilldownKind === "supplier"
      ? supplierBranchQuery.isLoading
      : drilldownKind === "product"
        ? productBranchQuery.isLoading
        : false;
  const isDrilldownError =
    drilldownKind === "supplier"
      ? supplierBranchQuery.isError
      : drilldownKind === "product"
        ? productBranchQuery.isError
        : false;
  const retryDrilldown = () => {
    if (drilldownKind === "supplier") {
      void supplierBranchQuery.refetch();
      return;
    }
    if (drilldownKind === "product") {
      void productBranchQuery.refetch();
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
    if (onRefreshReport) {
      // 嵌入报告中心时统一走控制器，避免下拉与页头刷新并发。
      void onRefreshReport();
      return;
    }
    void onRefreshFreshness?.();
    storeOptionsQuery.refetch();
    if (queryParams) {
      totalRevenueQuery.refetch();
      supplierQuery.refetch();
      productQuery.refetch();
    }
  };

  const isRefreshing =
    storeOptionsQuery.isRefetching ||
    totalRevenueQuery.isRefetching ||
    supplierQuery.isRefetching ||
    productQuery.isRefetching;
  const selectedStoreLabel =
    storeOptionsQuery.data?.find((item) => item.value === selectedStoreCode)?.label ??
    t("productReport.filters.allStores");

  const renderSupplierRow = ({ item }: { item: SupplierReportRow }) => {
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
        <Pressable
          // 供应商列筛下方商品明细，营业额列单独查看分店汇总。
          onPress={() => {
            setSelectedSupplierCode(item.supplierCode);
            setProductPage(1);
          }}
          accessibilityRole="button"
          accessibilityLabel={`${getSupplierTitle(kind, item)} ${t("productReport.sections.products")}`}
          accessibilityState={{ selected: isSelected }}
          style={styles.supplierNameColumn}
        >
          <TableCell style={styles.strongText}>{getSupplierTitle(kind, item)}</TableCell>
          <TableCell style={styles.muted}>{item.supplierCode}</TableCell>
        </Pressable>
        <Pressable
          // 营业额列只打开供应商分店数据，不改变下方商品明细筛选。
          onPress={() => setDrilldown({ type: "supplier", kind, supplier: item })}
          accessibilityRole="button"
          accessibilityLabel={`${getSupplierTitle(kind, item)} ${t("productReport.drilldown.supplier")}`}
          style={styles.supplierMoneyColumn}
        >
          <TableCell numeric style={styles.strongText}>{formatMoney(item.revenue)}</TableCell>
          <TableCell numeric style={styles.muted}>{formatMoney(item.compareRevenue)}</TableCell>
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
      </View>
    );
  };

  const renderProductRow = ({ item }: { item: ProductReportProductRow }) => (
    <Pressable
      style={[styles.tableRow, styles.productTableRow]}
      onPress={() => setDrilldown({ type: "product", product: item })}
    >
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
      <View style={styles.productInfoColumn}>
        <TableCell style={styles.strongText}>{item.itemNumber || "--"}</TableCell>
        <TableCell style={styles.muted}>{item.productName || "--"}</TableCell>
      </View>
      <View style={styles.productCountColumn}>
        <TableCell numeric style={styles.strongText}>{formatCount(item.quantity)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatCount(item.compareQuantity)}</TableCell>
      </View>
      <View style={styles.productMoneyColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(item.salesAmount)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(item.compareSalesAmount)}</TableCell>
      </View>
      <View style={styles.productAverageColumn}>
        <TableCell numeric style={styles.strongText}>{formatMoney(item.averageUnitPrice)}</TableCell>
        <TableCell numeric style={styles.muted}>{formatMoney(item.compareAverageUnitPrice)}</TableCell>
      </View>
      {renderGrowthCell(item.salesAmount, item.compareSalesAmount, styles.productGrowthColumn)}
    </Pressable>
  );

  return (
    <View style={styles.container}>
      <ScrollView
        bounces={false}
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
          <Button mode="outlined" compact icon="store-outline" onPress={() => setStoreModalVisible(true)}>
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
        ) : (
          <>
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
              ) : supplierQuery.isError ? (
                <ErrorState label={t("productReport.states.error")} retryLabel={t("actions.retry")} onRetry={() => supplierQuery.refetch()} />
              ) : (
                <ScrollView horizontal showsHorizontalScrollIndicator={false}>
                  <View style={[styles.table, kind === "china" ? styles.chinaSupplierTable : styles.supplierTable]}>
                    <SupplierTableHeader kind={kind} />
                    <ScrollView
                      bounces={false}
                      nestedScrollEnabled
                      showsVerticalScrollIndicator={false}
                      style={[styles.tableBody, { height: supplierTableBodyHeight }]}
                    >
                      {supplierPageRows.length === 0 ? (
                        <EmptyState label={t("productReport.states.emptySuppliers")} />
                      ) : (
                        supplierPageRows.map((item) => (
                          <View key={item.id}>{renderSupplierRow({ item })}</View>
                        ))
                      )}
                    </ScrollView>
                  </View>
                </ScrollView>
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
              {productQuery.isLoading || supplierQuery.isLoading ? (
                <LoadingState label={t("productReport.states.loading")} />
              ) : productQuery.isError ? (
                <ErrorState label={t("productReport.states.error")} retryLabel={t("actions.retry")} onRetry={() => productQuery.refetch()} />
              ) : (
                <ScrollView horizontal showsHorizontalScrollIndicator={false}>
                  <View style={[styles.table, styles.productTable]}>
                    <ProductTableHeader />
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
                        productRows.map((item) => (
                          <View key={item.id}>{renderProductRow({ item })}</View>
                        ))
                      )}
                    </ScrollView>
                  </View>
                </ScrollView>
              )}
            </View>
          </>
        )}
      </ScrollView>

      <StorePickerModal
        visible={isStoreModalVisible}
        labelAll={t("productReport.filters.allStores")}
        options={storeOptionsQuery.data ?? []}
        selectedStoreCode={selectedStoreCode}
        onSelect={applyStore}
        onDismiss={() => setStoreModalVisible(false)}
      />
      <BranchDrilldownModal
        visible={Boolean(drilldown)}
        title={
          drilldown?.type === "supplier"
            ? t("productReport.drilldown.supplier")
            : t("productReport.drilldown.product")
        }
        supplierRows={supplierBranchQuery.data ?? []}
        productRows={productBranchQuery.data ?? []}
        isLoading={isDrilldownLoading}
        isError={isDrilldownError}
        onRetry={retryDrilldown}
        onDismiss={() => setDrilldown(null)}
        closeLabel={t("actions.close")}
        retryLabel={t("actions.retry")}
        errorLabel={t("productReport.states.error")}
        emptyLabel={drilldownEmptyLabel}
        kind={drilldownKind}
        growthNewLabel={growthNewLabel}
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
  return (
    <View style={styles.sectionHeader}>
      <Text variant="titleMedium" style={styles.sectionTitle}>
        {title}
      </Text>
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

function SupplierTableHeader({ kind }: { kind: SupplierReportKind }) {
  const { t } = useAppTranslation("common");
  return (
    <View style={[styles.tableRow, styles.tableHeaderRow, styles.supplierTableRow]}>
      <View style={styles.supplierNameColumn}>
        <TableCell style={styles.headerText}>{t("productReport.sections.suppliers")}</TableCell>
      </View>
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
    </View>
  );
}

function ProductTableHeader() {
  const { t } = useAppTranslation("common");
  return (
    <View style={[styles.tableRow, styles.tableHeaderRow, styles.productTableRow]}>
      <View style={styles.productImageColumn}>
        <TableCell style={styles.headerText}>{t("productReport.columns.image")}</TableCell>
      </View>
      <View style={styles.productInfoColumn}>
        <TableCell style={styles.headerText}>{t("productReport.columns.product")}</TableCell>
      </View>
      <View style={styles.productCountColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.quantity")}</TableCell>
      </View>
      <View style={styles.productMoneyColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.amount")}</TableCell>
      </View>
      <View style={styles.productAverageColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.columns.averagePrice")}</TableCell>
      </View>
      <View style={styles.productGrowthColumn}>
        <TableCell numeric style={styles.headerText}>{t("productReport.metrics.growthRate")}</TableCell>
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
  options: Array<{ label: string; value: string }>;
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
}) {
  const { t } = useAppTranslation("common");
  const rows = kind === "supplier" ? supplierRows : productRows;
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
          <ScrollView bounces={false} nestedScrollEnabled style={[styles.modalList, styles.drilldownModalList]}>
            <ScrollView horizontal showsHorizontalScrollIndicator={false}>
              <View style={[styles.table, kind === "product" ? styles.productDrilldownTable : styles.drilldownTable]}>
                <View style={[styles.tableRow, styles.tableHeaderRow, kind === "product" ? styles.productBranchTableRow : null]}>
                  <View style={kind === "product" ? styles.productBranchNameColumn : styles.branchColumn}>
                    <TableCell style={styles.headerText}>{t("productReport.filters.store")}</TableCell>
                  </View>
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
                    </>
                  )}
                </View>
                {kind === "supplier"
                  ? supplierRows.map((row) => <SupplierBranchRow key={row.id} row={row} renderGrowthCell={renderGrowthCell} />)
                  : productRows.map((row) => <ProductBranchRow key={row.id} row={row} renderGrowthCell={renderGrowthCell} />)}
              </View>
            </ScrollView>
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
  renderGrowthCell,
}: {
  row: SupplierBranchBreakdownRow;
  renderGrowthCell: (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => ReactNode;
}) {
  return (
    <View style={styles.tableRow}>
      <View style={styles.branchColumn}>
        <TableCell style={styles.strongText}>{row.branchName || row.branchCode}</TableCell>
        <TableCell style={styles.muted}>{row.branchCode}</TableCell>
      </View>
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
    </View>
  );
}

function ProductBranchRow({
  row,
  renderGrowthCell,
}: {
  row: ProductBranchBreakdownRow;
  renderGrowthCell: (current: number, compare: number, columnStyle?: StyleProp<ViewStyle>) => ReactNode;
}) {
  return (
    <View style={[styles.tableRow, styles.productBranchTableRow]}>
      <View style={styles.productBranchNameColumn}>
        <TableCell style={styles.strongText}>{row.branchName || row.branchCode}</TableCell>
        <TableCell style={styles.muted}>{row.branchCode}</TableCell>
      </View>
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
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    marginTop: 4,
  },
  sectionTitle: {
    flex: 1,
    color: "#111827",
    fontWeight: "700",
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
    minWidth: 518,
  },
  chinaSupplierTable: {
    minWidth: 588,
  },
  productTable: {
    minWidth: 494,
  },
  drilldownTable: {
    minWidth: 520,
  },
  productDrilldownTable: {
    minWidth: 342,
  },
  tableBody: {
    flexGrow: 0,
  },
  tableRow: {
    minHeight: 50,
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
  productTableRow: {
    gap: 6,
    paddingHorizontal: 8,
  },
  supplierTableRow: {
    gap: 4,
    paddingHorizontal: 8,
  },
  supplierNameColumn: {
    width: 108,
    minWidth: 0,
  },
  supplierMoneyColumn: {
    width: 86,
    minWidth: 0,
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
    width: 128,
    minWidth: 0,
  },
  productCountColumn: {
    width: 54,
    minWidth: 0,
  },
  productMoneyColumn: {
    width: 82,
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
