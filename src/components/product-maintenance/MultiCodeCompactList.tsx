import { StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Card, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { MultiCodeEditableItem } from "@/modules/product-maintenance/types";

interface MultiCodeCompactListProps {
  items: MultiCodeEditableItem[];
  savingItemId?: string | null;
  printingItemId?: string | null;
  draftBarcode: string;
  mainRetailPrice?: number | null;
  totalCount?: number;
  loading?: boolean;
  loadingMore?: boolean;
  hasMore?: boolean;
  onChangeDraftBarcode: (value: string) => void;
  onChangeItem: (setCodeId: string, patch: Partial<MultiCodeEditableItem>) => void;
  onSaveItem: (setCodeId: string) => void;
  onPrintItem: (setCodeId: string) => void;
  onCreateItem: () => void;
  onLoadMore?: () => void;
}

function formatPrice(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

export function MultiCodeCompactList({
  items,
  savingItemId,
  printingItemId,
  draftBarcode,
  mainRetailPrice,
  totalCount,
  loading,
  loadingMore,
  hasMore,
  onChangeDraftBarcode,
  onChangeItem,
  onSaveItem,
  onPrintItem,
  onCreateItem,
  onLoadMore,
}: MultiCodeCompactListProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("multiCode.title")}
          {totalCount != null ? ` (${items.length}/${totalCount})` : ""}
        </Text>

        {loading ? (
          <View style={styles.loadingRow}>
            <ActivityIndicator size="small" />
            <Text variant="bodySmall" style={styles.loadingText}>
              {t("multiCode.loading")}
            </Text>
          </View>
        ) : null}

        {items.map((item) => (
          <View key={item.setCodeId || item.uuid} style={styles.item}>
            <TextInput
              mode="outlined"
              dense
              label={t("multiCode.barcode")}
              value={item.barcode ?? ""}
              onChangeText={(value) => onChangeItem(item.setCodeId, { barcode: value })}
              style={styles.input}
            />
            <View style={styles.footerRow}>
              <TextInput
                mode="outlined"
                dense
                label={t("multiCode.retail")}
                value={formatPrice(item.retailPrice)}
                editable={false}
                style={[styles.input, styles.priceInput]}
              />
              <Button
                compact
                mode="outlined"
                icon="printer-outline"
                onPress={() => onPrintItem(item.setCodeId)}
                loading={printingItemId === item.setCodeId}
                disabled={printingItemId === item.setCodeId}
              >
                {printingItemId === item.setCodeId ? t("print.sendingShort") : t("print.product")}
              </Button>
              <Button
                compact
                mode="contained-tonal"
                onPress={() => onSaveItem(item.setCodeId)}
                loading={savingItemId === item.setCodeId}
                disabled={savingItemId === item.setCodeId}
              >
                {t("multiCode.save")}
              </Button>
            </View>
          </View>
        ))}

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

        <View style={[styles.item, styles.draftItem]}>
          <Text variant="bodySmall" style={styles.addTitle}>
            {t("multiCode.addTitle")}
          </Text>
          <TextInput
            mode="outlined"
            dense
            label={t("multiCode.barcode")}
            value={draftBarcode}
            onChangeText={onChangeDraftBarcode}
            style={styles.input}
          />
          <View style={styles.footerRow}>
            <TextInput
              mode="outlined"
              dense
              label={t("multiCode.retail")}
              value={formatPrice(mainRetailPrice)}
              editable={false}
              style={[styles.input, styles.priceInput]}
            />
            <Button
              compact
              mode="contained"
              onPress={onCreateItem}
              loading={savingItemId === "new-multi"}
              disabled={savingItemId === "new-multi"}
            >
              {t("multiCode.add")}
            </Button>
          </View>
          <Text variant="bodySmall" style={styles.hint}>
            {t("multiCode.followMain")}
          </Text>
        </View>
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
    gap: 10,
    paddingVertical: 10,
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
  item: {
    gap: 8,
  },
  draftItem: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#E5E7EB",
    paddingTop: 10,
  },
  addTitle: {
    color: "#475467",
  },
  input: {
    backgroundColor: "#fff",
  },
  footerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  priceInput: {
    flex: 1,
  },
  hint: {
    color: "#475467",
  },
});
