import { useEffect, useMemo, useState } from "react";
import {
  Image,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  View,
} from "react-native";
import {
  ActivityIndicator,
  Button,
  Chip,
  Icon,
  IconButton,
  Searchbar,
  Text,
} from "react-native-paper";
import {
  SafeAreaView,
  useSafeAreaInsets,
} from "react-native-safe-area-context";
import type {
  ProductInsightDailySales,
  ProductInsightMovement,
  ProductInsightOrder,
  StoreProductInsight,
} from "@/modules/product-insights/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface ProductInsightsViewProps {
  query: string;
  onQueryChange: (value: string) => void;
  /** 手输聚焦状态交给页面编排层处理 HID 扫码焦点。 */
  onQueryFocus?: () => void;
  onQueryBlur?: () => void;
  onSearch: () => void;
  onScan: () => void;
  store: { storeCode: string; storeName: string };
  onStorePress?: () => void;
  data: StoreProductInsight | null;
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  onBack: () => void;
  canViewBranchSales: boolean;
  onOpenBranchSales: () => void;
  onDetailVisibilityChange?: (visible: boolean) => void;
  emptyState?: "initial" | "not-found";
}

type DetailTab = "sales" | "purchases" | "orders" | "deliveries";
type DetailItem =
  ProductInsightMovement | ProductInsightOrder | ProductInsightDailySales;
const money = (value: number) =>
  `A$${value.toLocaleString("en-AU", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
function dateLabel(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return match ? `${match[3]}/${match[2]}/${match[1]}` : "—";
}

function timestampLabel(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})/.exec(value);
  return match
    ? `${match[3]}/${match[2]}/${match[1]} ${match[4]}:${match[5]}`
    : dateLabel(value);
}

export function ProductInsightsView({
  query,
  onQueryChange,
  onQueryFocus,
  onQueryBlur,
  onSearch,
  onScan,
  store,
  onStorePress,
  data,
  loading = false,
  error,
  onRetry,
  onBack,
  canViewBranchSales,
  onOpenBranchSales,
  onDetailVisibilityChange,
  emptyState = "initial",
}: ProductInsightsViewProps) {
  const { t } = useAppTranslation("productInsights");
  const insets = useSafeAreaInsets();
  const [tab, setTab] = useState<DetailTab>("sales");
  const [detail, setDetail] = useState<DetailItem | null>(null);
  const warehouse = data?.sourceType === "warehouse";
  const sourceType = data?.sourceType;
  const productKey = data
    ? `${data.product.productCode}:${data.sourceType}`
    : "";

  useEffect(() => {
    setTab(sourceType === "warehouse" ? "orders" : "purchases");
    setDetail(null);
  }, [productKey, sourceType]);

  useEffect(() => {
    onDetailVisibilityChange?.(Boolean(detail));
  }, [detail, onDetailVisibilityChange]);
  const records = useMemo(() => {
    if (!data) return [] as DetailItem[];
    if (tab === "sales") return data.sales.records;
    if (tab === "purchases") return data.purchases.records;
    if (tab === "orders") return data.warehouse.orders;
    return data.warehouse.deliveries;
  }, [data, tab]);
  const tabs: DetailTab[] = warehouse
    ? ["sales", "orders", "deliveries"]
    : ["sales", "purchases"];

  return (
    <SafeAreaView style={styles.safe} edges={["top", "left", "right"]}>
      <View style={styles.screen}>
        <View style={styles.header}>
          <IconButton
            icon="chevron-left"
            accessibilityLabel={t("actions.back")}
            onPress={onBack}
          />
          <Text variant="titleMedium" style={styles.title}>
            {t("title")}
          </Text>
        </View>
        <Pressable
          onPress={onStorePress}
          disabled={!onStorePress}
          style={styles.storeBar}
          accessibilityRole={onStorePress ? "button" : undefined}
        >
          <Icon source="store-outline" size={17} color={HB_COLORS.action} />
          <Text numberOfLines={1} style={styles.storeText}>
            {t("store", { store: store.storeName, code: store.storeCode })}
          </Text>
          {onStorePress ? (
            <Icon source="chevron-down" size={18} color={HB_COLORS.action} />
          ) : null}
        </Pressable>
        <View style={styles.searchRow}>
          <Searchbar
            placeholder={t("search.placeholder")}
            value={query}
            multiline={false}
            numberOfLines={1}
            onChangeText={onQueryChange}
            onFocus={onQueryFocus}
            onBlur={onQueryBlur}
            onSubmitEditing={onSearch}
            style={styles.search}
            inputStyle={styles.searchInput}
          />
          <IconButton
            mode="contained"
            icon="magnify"
            onPress={onSearch}
            disabled={loading}
            accessibilityLabel={t("actions.search")}
            style={styles.searchAction}
          />
          <IconButton
            mode="contained-tonal"
            icon="barcode-scan"
            onPress={onScan}
            disabled={loading}
            accessibilityLabel={t("actions.scan")}
            style={styles.searchAction}
          />
        </View>
        <ScrollView
          contentContainerStyle={[
            styles.content,
            { paddingBottom: 88 + insets.bottom },
          ]}
          keyboardShouldPersistTaps="handled"
        >
          {loading ? (
            <Loading />
          ) : error ? (
            <Outcome
              icon="alert-circle-outline"
              title={t("states.loadFailed")}
              description={error}
              action={onRetry ? t("actions.retry") : undefined}
              onAction={onRetry}
            />
          ) : !data ? (
            <Outcome
              icon={
                emptyState === "not-found"
                  ? "package-variant-closed"
                  : "barcode-search"
              }
              title={
                emptyState === "not-found"
                  ? t("states.notFoundTitle")
                  : t("states.initialTitle")
              }
              description={
                emptyState === "not-found"
                  ? t("states.notFoundDescription")
                  : t("states.initialDescription")
              }
            />
          ) : (
            <>
              <ProductHeader data={data} t={t} />
              <View style={styles.period}>
                <Icon
                  source="calendar-range"
                  size={16}
                  color={HB_COLORS.textSecondary}
                />
                <Text style={styles.periodText}>
                  {t("range", {
                    startDate: dateLabel(data.range.startDate),
                    endDate: dateLabel(data.range.endDate),
                  })}
                </Text>
                <Text style={styles.generated}>
                  {t("generatedAt", {
                    value: timestampLabel(data.generatedAt),
                  })}
                </Text>
              </View>
              <View style={styles.summaryGrid}>
                <Summary
                  label={t("labels.salesQuantity")}
                  value={`${data.sales.quantity.toLocaleString()} ${t("units.items")}`}
                />
                <Summary
                  label={t("labels.salesAmount")}
                  value={money(data.sales.amount)}
                />
                {warehouse ? (
                  <>
                    <Summary
                      label={t("labels.orderedQuantity")}
                      value={`${data.warehouse.orderedQuantity.toLocaleString()} ${t("units.items")}`}
                    />
                    <Summary
                      label={t("labels.deliveredQuantity")}
                      value={`${data.warehouse.deliveredQuantity.toLocaleString()} ${t("units.items")}`}
                    />
                  </>
                ) : (
                  <>
                    <Summary
                      label={t("labels.purchaseQuantity")}
                      value={`${data.purchases.quantity.toLocaleString()} ${t("units.items")}`}
                    />
                    <Summary
                      label={t("labels.purchaseDocuments")}
                      value={`${data.purchases.documentCount.toLocaleString()} ${t("units.documents")}`}
                    />
                  </>
                )}
              </View>
              {!warehouse ? (
                <Text style={styles.recordScope}>{t("purchaseScope")}</Text>
              ) : null}
              <Text style={styles.updatedAt}>
                {data.salesStatisticLastUpdatedAt
                  ? t("salesUpdatedAt", {
                      value: timestampLabel(data.salesStatisticLastUpdatedAt),
                    })
                  : t("salesUpdatedUnavailable")}
              </Text>
              {!warehouse && data.purchases.records.length === 0 ? (
                <HistoryNotice record={data.purchases.lastRecord} t={t} />
              ) : null}
              {warehouse ? (
                <DeliveryHint record={data.warehouse.lastDelivery} t={t} />
              ) : null}
              <View style={styles.tabs}>
                {tabs.map((value) => (
                  <Chip
                    key={value}
                    selected={tab === value}
                    onPress={() => setTab(value)}
                    compact
                    selectedColor={HB_COLORS.action}
                    style={styles.tab}
                  >
                    {t(`tabs.${value}`)}
                  </Chip>
                ))}
              </View>
              <View style={styles.sectionHeader}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t(`tabs.${tab}`)}
                </Text>
                <Text variant="labelSmall" style={styles.count}>
                  {t("recordCount", { count: records.length })}
                </Text>
              </View>
              <RecordList records={records} onPress={setDetail} t={t} />
            </>
          )}
        </ScrollView>
        {data && canViewBranchSales ? (
          <View
            style={[
              styles.footer,
              { paddingBottom: Math.max(HB_SPACING.sm, insets.bottom) },
            ]}
          >
            <Button
              mode="contained"
              icon="chart-bar"
              onPress={onOpenBranchSales}
              contentStyle={styles.branchButton}
            >
              {t("actions.openBranchSales")}
            </Button>
          </View>
        ) : null}
        <DetailModal item={detail} onClose={() => setDetail(null)} t={t} />
      </View>
    </SafeAreaView>
  );
}

function ProductHeader({
  data,
  t,
}: {
  data: StoreProductInsight;
  t: (key: string, values?: Record<string, unknown>) => string;
}) {
  const p = data.product;
  return (
    <View style={styles.product}>
      <View style={styles.imageWrap}>
        {p.productImage ? (
          <Image source={{ uri: p.productImage }} style={styles.image} />
        ) : (
          <Icon source="package-variant" size={34} color="#98A2B3" />
        )}
      </View>
      <View style={styles.productMeta}>
        <Text variant="titleSmall" numberOfLines={2} style={styles.productName}>
          {p.productName}
        </Text>
        <Text numberOfLines={1} style={styles.meta}>
          {t("labels.itemNumber")}: {p.itemNumber ?? "—"}
        </Text>
        <Text numberOfLines={1} style={styles.meta}>
          {t("labels.barcode")}: {p.barcode ?? "—"}
        </Text>
        <Text numberOfLines={1} style={styles.meta}>
          {t("labels.supplier")}:{" "}
          {p.localSupplierName ?? p.localSupplierCode ?? "—"}
        </Text>
      </View>
    </View>
  );
}
function Summary({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.summary}>
      <Text variant="labelSmall" style={styles.summaryLabel}>
        {label}
      </Text>
      <Text variant="titleSmall" style={styles.summaryValue}>
        {value}
      </Text>
    </View>
  );
}
function HistoryNotice({
  record,
  t,
}: {
  record: ProductInsightMovement | null;
  t: (key: string, values?: Record<string, unknown>) => string;
}) {
  return (
    <View style={styles.warning}>
      <Icon source="history" size={19} color={HB_COLORS.warning} />
      <View style={styles.warningContent}>
        <Text style={styles.warningTitle}>{t("history.noPurchases")}</Text>
        {record ? (
          <Text style={styles.warningText}>
            {t("history.lastPurchase", {
              date: dateLabel(record.date),
              quantity: record.quantity.toLocaleString(),
              documentNo: record.documentNo,
              supplier: record.supplierName ?? "—",
            })}
          </Text>
        ) : (
          <Text style={styles.warningText}>{t("history.none")}</Text>
        )}
      </View>
    </View>
  );
}
function DeliveryHint({
  record,
  t,
}: {
  record: ProductInsightMovement | null;
  t: (key: string, values?: Record<string, unknown>) => string;
}) {
  return (
    <View style={styles.delivery}>
      <Icon
        source="truck-delivery-outline"
        size={19}
        color={HB_COLORS.action}
      />
      <Text style={styles.deliveryText}>
        {record
          ? t("warehouse.lastDelivery", {
              date: dateLabel(record.date),
              quantity: record.quantity.toLocaleString(),
            })
          : t("warehouse.noDelivery")}
      </Text>
    </View>
  );
}
function RecordList({
  records,
  onPress,
  t,
}: {
  records: DetailItem[];
  onPress: (item: DetailItem) => void;
  t: (key: string) => string;
}) {
  if (!records.length)
    return (
      <View style={styles.recordsEmpty}>
        <Text>{t("states.noRecords")}</Text>
      </View>
    );
  return (
    <View style={styles.records}>
      {records.map((item) => (
        <Pressable
          key={"id" in item ? item.id : item.date}
          onPress={() => onPress(item)}
          style={styles.record}
        >
          <View style={styles.recordMain}>
            <Text style={styles.recordDate}>{dateLabel(item.date)}</Text>
            <Text numberOfLines={1} style={styles.recordDocument}>
              {"documentNo" in item ? item.documentNo : t("labels.salesDetail")}
            </Text>
          </View>
          <View style={styles.recordValue}>
            <Text style={styles.quantity}>
              {item.quantity.toLocaleString()}
            </Text>
            {"amount" in item ? (
              <Text style={styles.amount}>{money(item.amount)}</Text>
            ) : null}
            <Icon source="chevron-right" size={17} color="#98A2B3" />
          </View>
        </Pressable>
      ))}
    </View>
  );
}
function DetailModal({
  item,
  onClose,
  t,
}: {
  item: DetailItem | null;
  onClose: () => void;
  t: (key: string) => string;
}) {
  if (!item) return null;
  return (
    <Modal transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.modalBackdrop} onPress={onClose}>
        <Pressable
          style={styles.modalCard}
          onPress={(event) => event.stopPropagation()}
        >
          <Text variant="titleMedium">{t("detail.title")}</Text>
          <DetailRow label={t("labels.date")} value={dateLabel(item.date)} />
          {"documentNo" in item ? (
            <DetailRow label={t("labels.documentNo")} value={item.documentNo} />
          ) : null}
          <DetailRow
            label={t("labels.quantity")}
            value={item.quantity.toLocaleString()}
          />
          {"amount" in item ? (
            <DetailRow
              label={t("labels.salesAmount")}
              value={money(item.amount)}
            />
          ) : null}
          {"supplierName" in item ? (
            <DetailRow
              label={t("labels.supplier")}
              value={item.supplierName ?? "—"}
            />
          ) : null}
          {"deliveredQuantity" in item ? (
            <>
              <DetailRow
                label={t("labels.deliveredQuantity")}
                value={item.deliveredQuantity.toLocaleString()}
              />
              <DetailRow
                label={t("labels.deliveryDate")}
                value={item.deliveryDate ?? "—"}
              />
            </>
          ) : null}
          <Button mode="contained-tonal" onPress={onClose}>
            {t("actions.close")}
          </Button>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailRow}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}
function Loading() {
  return (
    <View style={styles.outcome}>
      <ActivityIndicator size="large" color={HB_COLORS.brand} />
    </View>
  );
}
function Outcome({
  icon,
  title,
  description,
  action,
  onAction,
}: {
  icon: string;
  title: string;
  description: string;
  action?: string;
  onAction?: () => void;
}) {
  return (
    <View style={styles.outcome}>
      <Icon source={icon} size={42} color="#98A2B3" />
      <Text variant="titleSmall" style={styles.outcomeTitle}>
        {title}
      </Text>
      <Text style={styles.outcomeText}>{description}</Text>
      {action && onAction ? (
        <Button mode="contained-tonal" onPress={onAction}>
          {action}
        </Button>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: HB_COLORS.background },
  screen: { flex: 1 },
  header: {
    height: 52,
    flexDirection: "row",
    alignItems: "center",
    paddingRight: HB_SPACING.md,
    backgroundColor: HB_COLORS.white,
  },
  title: { color: HB_COLORS.textPrimary, fontWeight: "700" },
  storeBar: {
    minHeight: 38,
    paddingHorizontal: HB_SPACING.md,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    backgroundColor: "#EAF2FF",
  },
  storeText: {
    flex: 1,
    minWidth: 0,
    color: HB_COLORS.action,
    fontSize: 13,
    fontWeight: "700",
  },
  searchRow: {
    flexDirection: "row",
    padding: HB_SPACING.sm,
    gap: 6,
    backgroundColor: HB_COLORS.white,
    borderBottomWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
  },
  search: {
    flex: 1,
    height: 44,
    backgroundColor: HB_COLORS.white,
    elevation: 0,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
  },
  searchInput: { minHeight: 0, fontSize: 14 },
  searchAction: { width: 42, height: 42, margin: 0 },
  content: { padding: HB_SPACING.sm, paddingBottom: 88, gap: HB_SPACING.sm },
  product: {
    flexDirection: "row",
    gap: HB_SPACING.sm,
    padding: HB_SPACING.sm,
    backgroundColor: HB_COLORS.white,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    borderRadius: HB_RADIUS.surface,
  },
  imageWrap: {
    width: 80,
    height: 80,
    borderRadius: HB_RADIUS.control,
    overflow: "hidden",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  image: { width: "100%", height: "100%", resizeMode: "cover" },
  productMeta: { flex: 1, minWidth: 0, gap: 2 },
  productName: { color: HB_COLORS.textPrimary, fontWeight: "800" },
  meta: { color: HB_COLORS.textSecondary, fontSize: 12 },
  period: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 5,
    paddingHorizontal: HB_SPACING.sm,
  },
  periodText: { color: HB_COLORS.textSecondary, fontSize: 12 },
  generated: { marginLeft: "auto", color: "#98A2B3", fontSize: 11 },
  summaryGrid: { flexDirection: "row", flexWrap: "wrap", gap: HB_SPACING.xs },
  summary: {
    width: "48.8%",
    flexGrow: 1,
    minWidth: 145,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.white,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    gap: 3,
  },
  summaryLabel: { color: HB_COLORS.textSecondary },
  summaryValue: { color: HB_COLORS.textPrimary, fontWeight: "800" },
  recordScope: { color: HB_COLORS.textSecondary, fontSize: 11 },
  updatedAt: { color: HB_COLORS.textSecondary, fontSize: 11 },
  warning: {
    flexDirection: "row",
    gap: HB_SPACING.xs,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    borderWidth: 1,
    borderColor: "#FEC84B",
    backgroundColor: "#FFFAEB",
  },
  warningContent: { flex: 1, minWidth: 0, gap: 2 },
  warningTitle: { color: HB_COLORS.warning, fontWeight: "800" },
  warningText: { color: "#7A2E0E", fontSize: 12, lineHeight: 18 },
  delivery: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#EAF2FF",
  },
  deliveryText: { flex: 1, color: HB_COLORS.action, fontSize: 12 },
  tabs: { flexDirection: "row", flexWrap: "wrap", gap: HB_SPACING.xs },
  tab: { backgroundColor: HB_COLORS.white },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: HB_SPACING.xs,
  },
  sectionTitle: { fontWeight: "800", color: HB_COLORS.textPrimary },
  count: { color: HB_COLORS.textSecondary },
  records: {
    borderRadius: HB_RADIUS.surface,
    overflow: "hidden",
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
  },
  record: {
    minHeight: 54,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: HB_SPACING.xs,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
  },
  recordMain: { flex: 1, minWidth: 0, gap: 2 },
  recordDate: { color: HB_COLORS.textPrimary, fontSize: 13, fontWeight: "700" },
  recordDocument: { color: HB_COLORS.textSecondary, fontSize: 12 },
  recordValue: {
    flexDirection: "row",
    alignItems: "center",
    gap: 5,
    marginLeft: HB_SPACING.xs,
  },
  quantity: { color: HB_COLORS.textPrimary, fontWeight: "800" },
  amount: { color: HB_COLORS.textSecondary, fontSize: 12 },
  recordsEmpty: {
    padding: HB_SPACING.lg,
    alignItems: "center",
    backgroundColor: HB_COLORS.white,
    borderRadius: HB_RADIUS.surface,
  },
  footer: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 0,
    padding: HB_SPACING.sm,
    backgroundColor: HB_COLORS.white,
    borderTopWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
  },
  branchButton: { minHeight: 44 },
  outcome: {
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: HB_SPACING.lg,
    paddingVertical: 72,
    gap: HB_SPACING.sm,
  },
  outcomeTitle: { color: HB_COLORS.textPrimary, fontWeight: "800" },
  outcomeText: {
    color: HB_COLORS.textSecondary,
    textAlign: "center",
    lineHeight: 20,
  },
  modalBackdrop: {
    flex: 1,
    padding: HB_SPACING.lg,
    justifyContent: "center",
    backgroundColor: "rgba(16,24,40,0.44)",
  },
  modalCard: {
    padding: HB_SPACING.md,
    borderRadius: HB_RADIUS.sheet,
    backgroundColor: HB_COLORS.white,
    gap: HB_SPACING.sm,
  },
  detailRow: {
    flexDirection: "row",
    gap: HB_SPACING.sm,
    justifyContent: "space-between",
  },
  detailLabel: { color: HB_COLORS.textSecondary },
  detailValue: {
    flex: 1,
    color: HB_COLORS.textPrimary,
    textAlign: "right",
    fontWeight: "600",
  },
});
