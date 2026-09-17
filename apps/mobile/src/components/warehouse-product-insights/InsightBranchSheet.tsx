import { useMemo, useState } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { Divider, Searchbar, Text } from "react-native-paper";
import { formatSellThroughRate } from "@/modules/warehouse-product-insights/logic";
import type {
  WarehouseInsightBranch,
  WarehouseInsightTotals,
} from "@/modules/warehouse-product-insights/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { InsightSheet } from "./InsightSheet";

type BranchSort = "sales" | "shipped" | "ordered";

export interface InsightBranchSheetProps {
  visible: boolean;
  onClose: () => void;
  subtitle: string;
  rangeLabel: string;
  branches: WarehouseInsightBranch[];
  totals: WarehouseInsightTotals;
  scope: "all-stores" | "authorized-stores";
  initialSort?: BranchSort;
  onOpenRange: () => void;
}

const money = (value: number) =>
  `A$${value.toLocaleString("en-AU", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

export function InsightBranchSheet({
  visible,
  onClose,
  subtitle,
  rangeLabel,
  branches,
  totals,
  scope,
  initialSort = "sales",
  onOpenRange,
}: InsightBranchSheetProps) {
  const { t } = useAppTranslation("warehouseProductInsights");
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<BranchSort>(initialSort);

  const rows = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    const filtered = branches.filter(
      (row) =>
        !keyword ||
        `${row.storeCode} ${row.storeName}`.toLowerCase().includes(keyword),
    );
    const value = (row: WarehouseInsightBranch) =>
      sort === "sales"
        ? row.salesQuantity
        : sort === "shipped"
          ? row.shippedQuantity
          : row.orderedQuantity;
    return [...filtered].sort(
      (left, right) =>
        value(right) - value(left) ||
        left.storeCode.localeCompare(right.storeCode),
    );
  }, [branches, query, sort]);

  return (
    <InsightSheet
      visible={visible}
      title={t("branchSheet.title")}
      subtitle={subtitle}
      closeLabel={t("actions.close")}
      onClose={onClose}
    >
      <View style={styles.controls}>
        <Text
          accessibilityRole="button"
          onPress={onOpenRange}
          style={styles.rangeBar}
        >
          {rangeLabel}
        </Text>
        <View style={styles.totalCard}>
          <Text style={styles.totalLabel}>
            {t("branchSheet.total", { count: branches.length })}
          </Text>
          <View style={styles.totalValues}>
            <Metric
              label={t("labels.ordered")}
              value={totals.orderedQuantity.toLocaleString()}
            />
            <Metric
              label={t("labels.shipped")}
              value={totals.shippedQuantity.toLocaleString()}
            />
            <Metric
              label={t("labels.sales")}
              value={totals.salesQuantity.toLocaleString()}
            />
            <Metric label={t("labels.salesAmount")} value={money(totals.salesAmount)} />
          </View>
        </View>
        <View style={styles.filterRow}>
          <Searchbar
            value={query}
            onChangeText={setQuery}
            placeholder={t("branchSheet.searchStore")}
            style={styles.search}
            inputStyle={styles.searchInput}
          />
          {(["sales", "shipped", "ordered"] as BranchSort[]).map((value) => (
            <Text
              key={value}
              accessibilityRole="button"
              onPress={() => setSort(value)}
              style={[styles.sort, sort === value ? styles.sortActive : null]}
            >
              {t(`branchSheet.sort.${value}`)}
            </Text>
          ))}
        </View>
      </View>
      <View style={styles.tableHeader}>
        <Text style={[styles.column, styles.storeColumn]}>
          {t("branchSheet.store")}
        </Text>
        <Text style={[styles.column, styles.numberColumn]}>
          {t("labels.ordered")}
        </Text>
        <Text style={[styles.column, styles.numberColumn]}>
          {t("labels.shipped")}
        </Text>
        <Text style={[styles.column, styles.salesColumn, styles.salesHeader]}>
          {t("labels.sales")}
        </Text>
      </View>
      <FlatList
        style={styles.list}
        data={rows}
        keyExtractor={(item) => item.storeCode}
        ItemSeparatorComponent={Divider}
        ListEmptyComponent={
          <View style={styles.empty}>
            <Text style={styles.emptyText}>{t("branchSheet.empty")}</Text>
          </View>
        }
        ListFooterComponent={
          <Text style={styles.footer}>
            {scope === "authorized-stores"
              ? t("branchSheet.authorizedScope", { count: branches.length })
              : t("branchSheet.fullScope", { count: branches.length })}
          </Text>
        }
        renderItem={({ item }) => {
          const pending = item.pendingQuantity > 0;
          const rate = formatSellThroughRate(item.sellThroughRate);
          return (
            <View style={[styles.row, pending ? styles.pendingRow : null]}>
              <View style={[styles.storeColumn, styles.storeCell]}>
                <Text numberOfLines={1} style={styles.storeName}>
                  {item.storeName}
                </Text>
                <Text numberOfLines={1} style={pending ? styles.pendingMeta : styles.storeMeta}>
                  {item.storeCode}
                  {pending
                    ? ` · ${t("branchSheet.pending", { quantity: item.pendingQuantity.toLocaleString() })}`
                    : rate
                      ? ` · ${t("branchSheet.sellThrough", { rate })}`
                      : ""}
                </Text>
              </View>
              <Text style={[styles.column, styles.numberColumn, styles.value]}>
                {item.orderedQuantity.toLocaleString()}
              </Text>
              <Text
                style={[
                  styles.column,
                  styles.numberColumn,
                  styles.value,
                  pending ? styles.pendingValue : null,
                ]}
              >
                {item.shippedQuantity.toLocaleString()}
              </Text>
              <View style={styles.salesColumn}>
                <Text style={[styles.value, styles.salesValue]}>
                  {item.salesQuantity.toLocaleString()}
                </Text>
                <Text style={styles.salesAmount}>{money(item.salesAmount)}</Text>
              </View>
            </View>
          );
        }}
      />
    </InsightSheet>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={styles.metricValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  controls: { padding: HB_SPACING.md, gap: HB_SPACING.xs },
  rangeBar: {
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outline,
    borderRadius: HB_RADIUS.control,
    paddingVertical: 9,
    paddingHorizontal: HB_SPACING.sm,
    fontSize: 12,
    color: HB_COLORS.action,
  },
  totalCard: {
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#EAF2FF",
    gap: HB_SPACING.xs,
  },
  totalLabel: { color: HB_COLORS.action, fontSize: 12 },
  totalValues: { flexDirection: "row", gap: HB_SPACING.xs },
  metric: { flex: 1, minWidth: 0 },
  metricLabel: { color: HB_COLORS.textSecondary, fontSize: 11 },
  metricValue: { color: HB_COLORS.textPrimary, fontWeight: "800", fontSize: 14 },
  filterRow: { flexDirection: "row", gap: 6, alignItems: "center" },
  search: {
    flex: 1,
    height: 40,
    backgroundColor: HB_COLORS.surfaceMuted,
    elevation: 0,
  },
  searchInput: { minHeight: 0, fontSize: 13 },
  sort: {
    fontSize: 11,
    paddingVertical: 5,
    paddingHorizontal: 9,
    borderRadius: 14,
    overflow: "hidden",
    backgroundColor: HB_COLORS.surfaceMuted,
    color: HB_COLORS.textSecondary,
  },
  sortActive: { backgroundColor: HB_COLORS.brand, color: HB_COLORS.white },
  tableHeader: {
    flexDirection: "row",
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: HB_SPACING.xs,
    backgroundColor: HB_COLORS.surfaceMuted,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
  },
  list: { flex: 1 },
  row: {
    flexDirection: "row",
    alignItems: "center",
    paddingVertical: 10,
    paddingHorizontal: HB_SPACING.md,
  },
  pendingRow: { backgroundColor: "#FEF6EE" },
  column: { color: HB_COLORS.textSecondary, fontSize: 12 },
  storeColumn: { flex: 1, minWidth: 0 },
  storeCell: { gap: 2 },
  // 四列在窄屏并排，数值列各留出左内边距，否则表头与大额数字会视觉粘连。
  numberColumn: { width: 52, textAlign: "right", paddingLeft: 6 },
  salesColumn: { width: 82, alignItems: "flex-end", paddingLeft: 6 },
  // 表头是纯文本，alignItems 不生效，需要显式右对齐才能与数值列对齐。
  salesHeader: { textAlign: "right" },
  storeName: { color: HB_COLORS.textPrimary, fontWeight: "600", fontSize: 13 },
  storeMeta: { color: HB_COLORS.textSecondary, fontSize: 11 },
  pendingMeta: { color: HB_COLORS.warning, fontSize: 11, fontWeight: "600" },
  value: { color: HB_COLORS.textPrimary, fontSize: 13 },
  pendingValue: { color: HB_COLORS.warning, fontWeight: "700" },
  salesValue: { fontWeight: "600" },
  salesAmount: { color: HB_COLORS.textSecondary, fontSize: 11 },
  empty: { padding: HB_SPACING.lg, alignItems: "center" },
  emptyText: { color: HB_COLORS.textSecondary },
  footer: {
    padding: HB_SPACING.sm,
    fontSize: 11,
    color: HB_COLORS.textSecondary,
    textAlign: "center",
  },
});
