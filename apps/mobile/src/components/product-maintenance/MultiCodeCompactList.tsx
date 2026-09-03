import { Pressable, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Card, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { MultiCodeEditableItem } from "@/modules/product-maintenance/types";

interface MultiCodeCompactListProps {
  items: MultiCodeEditableItem[];
  savingItemId?: string | null;
  printingItemId?: string | null;
  totalCount?: number;
  loading?: boolean;
  loadingMore?: boolean;
  hasMore?: boolean;
  onEditItemBarcode: (setCodeId: string) => void;
  onEditItemRetailPrice: (setCodeId: string) => void;
  onSaveItem: (setCodeId: string) => void;
  onPrintItem: (setCodeId: string) => void;
  onAddItem: () => void;
  onLoadMore?: () => void;
}

function formatPrice(value?: number | null) {
  return value == null ? "--" : `$${value.toFixed(2)}`;
}

export function MultiCodeCompactList({
  items,
  savingItemId,
  printingItemId,
  totalCount,
  loading,
  loadingMore,
  hasMore,
  onEditItemBarcode,
  onEditItemRetailPrice,
  onSaveItem,
  onPrintItem,
  onAddItem,
  onLoadMore,
}: MultiCodeCompactListProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.headerRow}>
          <Text variant="titleSmall" style={styles.title}>
            {t("multiCode.title")}
            {totalCount != null ? ` (${items.length}/${totalCount})` : ""}
          </Text>
          <Button
            compact
            mode="contained"
            icon="plus"
            onPress={onAddItem}
            loading={savingItemId === "new-multi"}
            disabled={savingItemId === "new-multi"}
          >
            {t("multiCode.add")}
          </Button>
        </View>

        {loading ? (
          <View style={styles.loadingRow}>
            <ActivityIndicator size="small" />
            <Text variant="bodySmall" style={styles.loadingText}>
              {t("multiCode.loading")}
            </Text>
          </View>
        ) : null}

        {items.map((item, index) => {
          // 历史多码可能没有 setCodeId，交互必须稳定地回传 UUID，不能误命中首行。
          const itemId = item.setCodeId || item.uuid;
          return (
            <View key={itemId} style={styles.row}>
              <Text variant="bodySmall" style={styles.rowNumber}>
                {index + 1}
              </Text>
              <Pressable style={styles.barcodeCell} onPress={() => onEditItemBarcode(itemId)}>
                <Text variant="bodyMedium" style={styles.barcodeText} numberOfLines={1}>
                  {item.barcode ?? "--"}
                </Text>
              </Pressable>
              <Pressable style={styles.priceCell} onPress={() => onEditItemRetailPrice(itemId)}>
                <Text variant="bodyMedium" style={styles.priceText}>
                  {formatPrice(item.retailPrice)}
                </Text>
              </Pressable>
              <IconButton
                icon="printer-outline"
                size={18}
                onPress={() => onPrintItem(itemId)}
                loading={printingItemId === itemId}
                disabled={printingItemId === itemId}
                style={styles.printButton}
              />
            </View>
          );
        })}

        {hasMore && onLoadMore ? (
          <Button
            compact
            mode="text"
            onPress={onLoadMore}
            loading={loadingMore}
            disabled={loadingMore}
          >
            {t("multiCode.loadMore")}
          </Button>
        ) : null}
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
    backgroundColor: "#fff",
  },
  content: {
    gap: 6,
    paddingVertical: 10,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  title: {
    fontWeight: "700",
  },
  loadingRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  loadingText: {
    color: "#475467",
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    paddingVertical: 4,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#F1F5F9",
  },
  rowNumber: {
    width: 24,
    color: "#94A3B8",
    textAlign: "center",
  },
  barcodeCell: {
    flex: 1,
    paddingHorizontal: 6,
    paddingVertical: 4,
  },
  barcodeText: {
    color: "#334155",
  },
  priceCell: {
    paddingHorizontal: 6,
    paddingVertical: 4,
    minWidth: 70,
    alignItems: "flex-end",
  },
  priceText: {
    color: "#334155",
    fontWeight: "600",
  },
  printButton: {
    margin: 0,
  },
});
