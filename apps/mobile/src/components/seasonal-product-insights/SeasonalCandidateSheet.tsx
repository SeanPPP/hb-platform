import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import { InsightSheet } from "@/components/warehouse-product-insights/InsightSheet";
import { formatQuantity } from "@/modules/seasonal-product-insights/logic";
import type { SeasonalLookupResult } from "@/modules/seasonal-product-insights/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { ProductImageBox } from "./ProductImageBox";

export interface SeasonalCandidateSheetProps {
  keyword: string;
  result: SeasonalLookupResult | null;
  onSelect: (productCode: string) => void;
  onClose: () => void;
}

/** 把货号里与关键词相同的片段拆出来高亮（不区分大小写）。 */
function splitMatch(value: string, keyword: string) {
  const index = value.toUpperCase().indexOf(keyword.trim().toUpperCase());
  if (!keyword.trim() || index < 0) return [value, "", ""] as const;
  const end = index + keyword.trim().length;
  return [value.slice(0, index), value.slice(index, end), value.slice(end)] as const;
}

export function SeasonalCandidateSheet({ keyword, result, onSelect, onClose }: SeasonalCandidateSheetProps) {
  const { t } = useAppTranslation("seasonalProductInsights");
  const visible = Boolean(result && result.items.length > 1);
  const byBarcode = result?.matchMode === "barcode";
  return (
    <InsightSheet
      visible={visible}
      title={t("candidates.title", { count: result?.items.length ?? 0 })}
      subtitle={
        result?.truncated
          ? t("candidates.truncated", { count: result.items.length })
          : t(byBarcode ? "candidates.byBarcode" : "candidates.byItemNumber", { keyword })
      }
      closeLabel={t("actions.close")}
      onClose={onClose}
      heightRatio={0.72}
    >
      <ScrollView keyboardShouldPersistTaps="handled">
        {result?.items.map((item) => {
          const [before, match, after] = byBarcode
            ? ([item.itemNumber ?? "—", "", ""] as const)
            : splitMatch(item.itemNumber ?? "—", keyword);
          return (
            <Pressable
              key={item.productCode}
              accessibilityRole="button"
              accessibilityLabel={`${item.productName} ${item.itemNumber ?? ""}`}
              onPress={() => onSelect(item.productCode)}
              style={({ pressed }) => [styles.row, pressed ? styles.rowPressed : null]}
            >
              <ProductImageBox uri={item.productImage} size={44} label={item.productName} />
              <View style={styles.main}>
                <Text numberOfLines={1} style={styles.name}>
                  {item.productName}
                </Text>
                <Text numberOfLines={1} style={styles.meta}>
                  {t("labels.itemNumber")} {before}
                  {match ? <Text style={styles.match}>{match}</Text> : null}
                  {after}
                  {byBarcode && item.barcode ? ` · ${item.barcode}` : ""}
                </Text>
              </View>
              <View style={styles.stock}>
                <Text style={styles.stockLabel}>{t("kpi.stock")}</Text>
                <Text style={[styles.stockValue, item.theoreticalStock < 0 ? styles.negative : null]}>
                  {formatQuantity(item.theoreticalStock)}
                </Text>
              </View>
            </Pressable>
          );
        })}
      </ScrollView>
    </InsightSheet>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: 10,
    minHeight: 64,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outlineMuted,
  },
  rowPressed: { backgroundColor: HB_COLORS.surfaceMuted },
  main: { flex: 1, minWidth: 0, gap: 3 },
  name: { fontSize: 14, fontWeight: "600", color: HB_COLORS.textPrimary },
  meta: { fontSize: 12, color: "#667085" },
  match: { color: HB_COLORS.action, fontWeight: "700" },
  stock: { alignItems: "flex-end", gap: 2 },
  stockLabel: { fontSize: 10.5, color: "#98A2B3" },
  stockValue: { fontSize: 14, fontWeight: "700", color: HB_COLORS.textPrimary, fontVariant: ["tabular-nums"] },
  negative: { color: HB_COLORS.danger },
});
