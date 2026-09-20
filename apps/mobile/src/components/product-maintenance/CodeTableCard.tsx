import { Pressable, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Card, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface CodeTableRow {
  id: string;
  barcode?: string | null;
  /** 已格式化的价格（不含 $）；null 表示无价格。 */
  price: string | null;
  /** 价格跟随主条码（多码零售价为空）时显示灰底「跟随」。 */
  followsMain?: boolean;
  dirty?: boolean;
}

interface CodeTableCardProps {
  title: string;
  priceColumnLabel: string;
  loadingText: string;
  rows: CodeTableRow[];
  totalCount?: number;
  savingItemId?: string | null;
  printingItemId?: string | null;
  adding?: boolean;
  loading?: boolean;
  loadingMore?: boolean;
  hasMore?: boolean;
  onEditItemBarcode: (id: string) => void;
  onEditItemRetailPrice: (id: string) => void;
  onSaveItem: (id: string) => void;
  onPrintItem: (id: string) => void;
  onAddItem: () => void;
  onLoadMore?: () => void;
  /**
   * 离线态只读：隐藏新增与整行保存、关闭「加载更多」（离线快照已含全部码），
   * 打印保留 —— 打标签是纯本地的蓝牙操作，断网时正是最需要的功能。
   */
  readOnly?: boolean;
}

/** 套装 / 多码共用的「条码 | 价格 | 操作」表格卡。 */
export function CodeTableCard({
  title,
  priceColumnLabel,
  loadingText,
  rows,
  totalCount,
  savingItemId,
  printingItemId,
  adding = false,
  loading,
  loadingMore,
  hasMore,
  onEditItemBarcode,
  onEditItemRetailPrice,
  onSaveItem,
  onPrintItem,
  onAddItem,
  onLoadMore,
  readOnly = false,
}: CodeTableCardProps) {
  const { t } = useAppTranslation("productQuery");
  const remaining = totalCount != null ? Math.max(totalCount - rows.length, 0) : 0;

  return (
    <Card style={styles.card} mode="contained">
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text style={styles.title} numberOfLines={1}>{title}</Text>
          {totalCount != null ? (
            <Text style={styles.loaded}>
              {t("codes.loaded", { loaded: rows.length, total: totalCount })}
            </Text>
          ) : null}
        </View>
        {readOnly ? null : (
          <Button
            compact
            mode="contained-tonal"
            icon="plus"
            onPress={onAddItem}
            loading={adding}
            disabled={adding}
          >
            {t("setCode.add")}
          </Button>
        )}
      </View>

      <View style={styles.columns}>
        <Text style={[styles.columnLabel, styles.barcodeColumn]}>{t("codes.barcodeColumn")}</Text>
        <Text style={[styles.columnLabel, styles.priceColumn]}>{priceColumnLabel}</Text>
        <Text style={[styles.columnLabel, styles.actionsColumn]}>{t("codes.actionsColumn")}</Text>
      </View>

      {loading ? (
        <View style={styles.loadingRow}>
          <ActivityIndicator size="small" />
          <Text variant="bodySmall" style={styles.loadingText}>{loadingText}</Text>
        </View>
      ) : null}

      {rows.map((row) => {
        const saving = savingItemId === row.id;
        const printing = printingItemId === row.id;
        return (
          <View key={row.id} style={[styles.row, row.dirty ? styles.rowDirty : null]}>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${t("codes.barcodeColumn")} ${row.barcode ?? "--"}`}
              onPress={readOnly ? undefined : () => onEditItemBarcode(row.id)}
              style={[styles.cell, styles.barcodeColumn, row.dirty ? styles.cellDirty : null]}
            >
              <Text style={styles.barcodeText} numberOfLines={1}>{row.barcode ?? "--"}</Text>
            </Pressable>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${priceColumnLabel} ${row.followsMain ? t("codes.follow") : row.price ?? "--"}`}
              onPress={readOnly ? undefined : () => onEditItemRetailPrice(row.id)}
              style={[
                styles.cell,
                styles.priceColumn,
                row.followsMain ? styles.cellFollow : null,
                row.dirty ? styles.cellDirty : null,
              ]}
            >
              <Text
                style={[styles.priceText, row.followsMain ? styles.followText : null]}
                numberOfLines={1}
              >
                {row.followsMain ? t("codes.follow") : row.price != null ? `$${row.price}` : "--"}
              </Text>
            </Pressable>
            <View style={[styles.actionsColumn, styles.actions]}>
              {/* 编码为单行即时保存：无改动时保存按钮置灰禁用，有改动才可点。 */}
              {readOnly ? null : (
              <IconButton
                icon="content-save-outline"
                accessibilityLabel={t("codes.saveRow")}
                size={18}
                mode={row.dirty ? "contained" : undefined}
                containerColor={row.dirty ? HB_COLORS.brand : undefined}
                iconColor={row.dirty ? HB_COLORS.white : "#98A2B3"}
                onPress={() => onSaveItem(row.id)}
                loading={saving}
                disabled={!row.dirty || saving}
                style={styles.actionButton}
              />
              )}
              <IconButton
                icon="printer-outline"
                accessibilityLabel={t("codes.printRow")}
                size={18}
                onPress={() => onPrintItem(row.id)}
                loading={printing}
                disabled={printing}
                style={styles.actionButton}
              />
            </View>
          </View>
        );
      })}

      {hasMore && onLoadMore && !readOnly ? (
        <Button
          compact
          mode="text"
          onPress={onLoadMore}
          loading={loadingMore}
          disabled={loadingMore}
          style={styles.loadMore}
        >
          {t("codes.loadMore", { count: remaining })}
        </Button>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: HB_RADIUS.surface,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
    overflow: "hidden",
    paddingBottom: HB_SPACING.xxs,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: HB_SPACING.xs,
    paddingHorizontal: HB_SPACING.sm,
    paddingTop: HB_SPACING.sm,
    paddingBottom: HB_SPACING.xs,
  },
  headerText: {
    flex: 1,
    minWidth: 0,
    flexDirection: "row",
    alignItems: "baseline",
    gap: HB_SPACING.xs,
  },
  title: {
    fontSize: 15,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
    flexShrink: 1,
  },
  loaded: {
    fontSize: 12,
    color: HB_COLORS.textSecondary,
    fontVariant: ["tabular-nums"],
  },
  columns: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 6,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  columnLabel: {
    fontSize: 11,
    fontWeight: "600",
    color: HB_COLORS.textSecondary,
  },
  barcodeColumn: {
    flex: 1,
    minWidth: 0,
  },
  priceColumn: {
    width: 84,
  },
  actionsColumn: {
    width: 76,
    textAlign: "center",
  },
  loadingRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: HB_SPACING.xs,
  },
  loadingText: {
    color: HB_COLORS.textSecondary,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 6,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  rowDirty: {
    backgroundColor: "#EAF2FF",
  },
  cell: {
    minHeight: 36,
    justifyContent: "center",
    paddingHorizontal: HB_SPACING.xs,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    backgroundColor: HB_COLORS.white,
  },
  cellFollow: {
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  cellDirty: {
    borderWidth: 2,
    borderColor: HB_COLORS.brand,
    paddingHorizontal: 7,
  },
  barcodeText: {
    fontSize: 14,
    color: HB_COLORS.textPrimary,
    fontVariant: ["tabular-nums"],
  },
  priceText: {
    fontSize: 14,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
    textAlign: "right",
    fontVariant: ["tabular-nums"],
  },
  followText: {
    fontWeight: "500",
    color: HB_COLORS.textSecondary,
    textAlign: "center",
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 2,
  },
  actionButton: {
    width: 34,
    height: 34,
    margin: 0,
  },
  loadMore: {
    alignSelf: "center",
    marginTop: HB_SPACING.xxs,
  },
});
